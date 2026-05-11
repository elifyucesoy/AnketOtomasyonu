using AnketOtomasyonu.Data;
using AnketOtomasyonu.Helpers;
using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AnketOtomasyonu.Services.Implementations
{
    public class SurveyResponseService : ISurveyResponseService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<SurveyResponseService> _logger;
        private readonly IUnitApiService _unitApi;

        public SurveyResponseService(
            ApplicationDbContext context,
            ILogger<SurveyResponseService> logger,
            IUnitApiService unitApi)
        {
            _context = context;
            _logger = logger;
            _unitApi = unitApi;
        }

        public async Task<bool> HasUserRespondedAsync(int surveyId, string userId)
        {
            return await _context.SurveyResponses
                .AnyAsync(r => r.SurveyId == surveyId && r.UserId == userId);
        }

        public async Task<HashSet<string>> GetRespondedUserIdsAsync(int surveyId, IEnumerable<string> userIds)
        {
            var distinct = (userIds ?? Enumerable.Empty<string>())
                .Where(u => !string.IsNullOrEmpty(u))
                .Distinct()
                .ToList();

            if (distinct.Count == 0)
                return new HashSet<string>(StringComparer.Ordinal);

            var hits = await _context.SurveyResponses
                .AsNoTracking()
                .Where(r => r.SurveyId == surveyId && distinct.Contains(r.UserId))
                .Select(r => r.UserId)
                .ToListAsync();

            return new HashSet<string>(hits, StringComparer.Ordinal);
        }

        public async Task<bool> HasRespondedByIpAsync(int surveyId, string ipAddress)
        {
            return await _context.SurveyResponses
                .AnyAsync(r => r.SurveyId == surveyId && r.IpAddress == ipAddress);
        }

        public async Task<(bool success, string message)> SubmitResponseAsync(
            SurveySubmitDto dto, string userId, string? ipAddress,
            string? userFullName = null, string? fakulteAdi = null, string? bolumAdi = null,
            int? respondentUnitId = null, string? birimAdi = null)
        {
            var survey = await _context.Surveys
                .Include(s => s.Questions)
                .FirstOrDefaultAsync(s => s.Id == dto.SurveyId);

            if (survey == null)
                return (false, "Anket bulunamadı.");

            if (survey.Status != SurveyStatus.Active)
                return (false, "Bu anket aktif değil.");

            if (survey.IsAnonymous)
            {
                // Anonim ankette IP ile tekrar kontrolü
                if (!string.IsNullOrEmpty(ipAddress))
                {
                    var alreadyByIp = await _context.SurveyResponses
                        .AnyAsync(r => r.SurveyId == dto.SurveyId
                                    && r.IpAddress == ipAddress);
                    if (alreadyByIp)
                        return (false, "Bu anketi zaten doldurdunuz.");
                }
            }
            else
            {
                // Normal ankette UserId ile tekrar kontrolü
                var already = await _context.SurveyResponses
                    .AnyAsync(r => r.SurveyId == dto.SurveyId && r.UserId == userId);
                if (already)
                    return (false, "Bu anketi zaten doldurdunuz.");
            }

            // Sadece zorunlu soruları kontrol et
            var requiredIds = survey.Questions
                .Where(q => q.IsRequired)
                .Select(q => q.Id).ToList();

            var answeredIds = dto.Answers
                .Where(a => a.SelectedOptionId.HasValue || !string.IsNullOrWhiteSpace(a.OpenEndedAnswer))
                .Select(a => a.QuestionId)
                .Distinct()
                .ToList();

            var missingRequiredIds = requiredIds.Except(answeredIds).ToList();

            if (missingRequiredIds.Any())
            {
                return (false, "Lütfen zorunlu (*) soruları cevaplayınız.");
            }

            var response = new SurveyResponse
            {
                SurveyId = dto.SurveyId,
                // Anonim ankette kullanıcı ID'si olarak IP adresi kaydedilir.
                // Normal ankette login olan kullanıcının ID'si kullanılır.
                UserId = survey.IsAnonymous
                    ? (ipAddress ?? "anonymous")
                    : userId,
                SubmittedAt = DateTime.UtcNow,
                IpAddress = ipAddress,
                // Katılımcı adı bilinçli olarak saklanmaz (yalnızca birim/bölüm alanları).
                UserFullName = null,
                FakulteAdi = fakulteAdi,
                BolumAdi = bolumAdi,
                RespondentUnitId = respondentUnitId,
                BirimAdi = birimAdi,
                Answers = dto.Answers.Select(a =>
                {
                    var qdef = survey.Questions.FirstOrDefault(q => q.Id == a.QuestionId);
                    return new SurveyAnswer
                    {
                        QuestionId = a.QuestionId,
                        QuestionType = qdef?.Type,
                        SelectedOptionId = a.SelectedOptionId,
                        OpenEndedAnswer = a.OpenEndedAnswer
                    };
                }).ToList()
            };

            _context.SurveyResponses.Add(response);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Cevap kaydedildi SurveyId={S} Anonim={A}",
                dto.SurveyId, survey.IsAnonymous);

            return (true, "Anket başarıyla gönderildi. Teşekkür ederiz!");
        }

        public async Task<RespondentFilterOptionsDto> GetRespondentFilterOptionsAsync(int surveyId)
        {
            var rows = await _context.SurveyResponses.AsNoTracking()
                .Where(r => r.SurveyId == surveyId)
                .Select(r => new { r.BolumAdi, r.BirimAdi })
                .ToListAsync();

            var tr = StringComparer.OrdinalIgnoreCase;
            return new RespondentFilterOptionsDto
            {
                Bolumler = rows.Where(r => !string.IsNullOrEmpty(r.BolumAdi))
                    .Select(r => r.BolumAdi!)
                    .Distinct(tr).OrderBy(x => x, tr).ToList(),
                Birimler = rows.Where(r => !string.IsNullOrEmpty(r.BirimAdi))
                    .Select(r => r.BirimAdi!)
                    .Distinct(tr).OrderBy(x => x, tr).ToList()
            };
        }

        public async Task<SurveyResultDto> GetSurveyResultsAsync(int surveyId, string? fakulte = null, string? bolum = null, string? birim = null)
        {
            var query = _context.Surveys
                .AsSplitQuery()
                .Include(s => s.Questions).ThenInclude(q => q.Options)
                .Include(s => s.Responses).ThenInclude(r => r.Answers)
                .AsQueryable();

            var survey = await query.FirstOrDefaultAsync(s => s.Id == surveyId);

            if (survey == null) return new SurveyResultDto();

            // Filtreleme uygulama
            var filteredResponses = survey.Responses.AsEnumerable();

            if (!string.IsNullOrEmpty(fakulte))
            {
                filteredResponses = filteredResponses.Where(r => 
                    string.Equals(r.FakulteAdi, fakulte, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(bolum))
            {
                filteredResponses = filteredResponses.Where(r =>
                    string.Equals(r.BolumAdi, bolum, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(birim))
            {
                filteredResponses = filteredResponses.Where(r =>
                    string.Equals(r.BirimAdi, birim, StringComparison.OrdinalIgnoreCase));
            }

            var responsesList = filteredResponses.ToList();

            var distinctRespondentUnitIds = responsesList
                .Select(r => r.RespondentUnitId)
                .Where(id => id.HasValue && id.Value > 0)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();

            var parentByUnitId = new Dictionary<int, int?>();
            foreach (var uid in distinctRespondentUnitIds)
            {
                try
                {
                    var unitDto = await _unitApi.GetUnitByIdAsync(uid);
                    parentByUnitId[uid] = unitDto?.ParentId;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "UnitById başarısız RespondentUnitId={U}", uid);
                }
            }

            var allUnits = await _unitApi.GetAllUnitsAsync();
            var unitTypes = await _unitApi.GetAllUnitTypesAsync();

            var respondentsOrdered = responsesList.OrderByDescending(r => r.SubmittedAt).ToList();
            var respondentsDto = new List<RespondentInfoDto>();
            foreach (var r in respondentsOrdered)
            {
                var (birimDisp, bolumDisp) = await EnrichBirimBolumForResultsAsync(r, unitTypes, allUnits);
                respondentsDto.Add(new RespondentInfoDto
                {
                    UserFullName = null,
                    FakulteAdi = r.FakulteAdi,
                    BirimAdi = birimDisp,
                    BolumAdi = bolumDisp,
                    RespondentUnitId = r.RespondentUnitId,
                    ParentUnitId = r.RespondentUnitId is > 0 &&
                                   parentByUnitId.TryGetValue(r.RespondentUnitId.Value, out var p)
                        ? p
                        : null,
                    SubmittedAt = r.SubmittedAt
                });
            }

            return new SurveyResultDto
            {
                SurveyId = survey.Id,
                Title = survey.Title,
                TotalResponses = responsesList.Count,
                Respondents = respondentsDto,
                Questions = survey.Questions.Select(q =>
                {
                    var answers = responsesList
                        .SelectMany(r => r.Answers)
                        .Where(a => a.QuestionId == q.Id).ToList();

                    return new QuestionResultDto
                    {
                        QuestionId = q.Id,
                        QuestionText = q.Text,
                        QuestionType = q.Type,
                        AnswerCount = answers.Count,
                        AverageSatisfaction = q.Type == QuestionType.Likert && answers.Any(a => a.SelectedOptionId.HasValue)
                            ? answers
                                .Where(a => a.SelectedOptionId.HasValue)
                                .Select(a => {
                                    var opt = q.Options.FirstOrDefault(o => o.Id == a.SelectedOptionId);
                                    return (double)(opt?.Value ?? 0);
                                })
                                .Average()
                            : 0,
                        OptionResults = q.Options.Select(o => new OptionResultDto
                        {
                            OptionId = o.Id,
                            OptionText = o.Text,
                            Count = answers.Count(a => a.SelectedOptionId == o.Id),
                            Percentage = answers.Count == 0 ? 0 :
                                Math.Round(
                                    (double)answers.Count(a => a.SelectedOptionId == o.Id)
                                    / answers.Count * 100, 1)
                        }).ToList(),
                        OpenEndedAnswers = q.Type == QuestionType.OpenEnded
                            ? answers
                                .Where(a => !string.IsNullOrWhiteSpace(a.OpenEndedAnswer))
                                .Select(a => a.OpenEndedAnswer!).ToList()
                            : new List<string>()
                    };
                }).ToList(),
                DepartmentResults = responsesList
                    .Where(r => !string.IsNullOrEmpty(r.BolumAdi))
                    .GroupBy(r => r.BolumAdi!)
                    .Select(g => new DepartmentResultDto
                    {
                        DepartmentName = g.Key,
                        ResponseCount = g.Count(),
                        AverageSatisfaction = g.SelectMany(r => r.Answers)
                            .Where(a => survey.Questions.Any(q => q.Id == a.QuestionId && q.Type == QuestionType.Likert))
                            .Where(a => a.SelectedOptionId.HasValue)
                            .Select(a => {
                                var q = survey.Questions.First(x => x.Id == a.QuestionId);
                                var opt = q.Options.First(o => o.Id == a.SelectedOptionId);
                                return (double)(opt.Value ?? 0);
                            })
                            .DefaultIfEmpty(0)
                            .Average()
                    }).ToList(),

                FakulteResults = responsesList
                    .Where(r => !string.IsNullOrEmpty(r.FakulteAdi))
                    .GroupBy(r => r.FakulteAdi!)
                    .Select(g => new FakulteResultDto
                    {
                        FakulteName = g.Key,
                        ResponseCount = g.Count(),
                        AverageSatisfaction = g.SelectMany(r => r.Answers)
                            .Where(a => survey.Questions.Any(q => q.Id == a.QuestionId && q.Type == QuestionType.Likert))
                            .Where(a => a.SelectedOptionId.HasValue)
                            .Select(a => {
                                var q = survey.Questions.First(x => x.Id == a.QuestionId);
                                var opt = q.Options.First(o => o.Id == a.SelectedOptionId);
                                return (double)(opt.Value ?? 0);
                            })
                            .DefaultIfEmpty(0)
                            .Average()
                    }).ToList(),

                BirimResults = responsesList
                    .Where(r => !string.IsNullOrEmpty(r.BirimAdi))
                    .GroupBy(r => r.BirimAdi!)
                    .Select(g => new BirimResultDto
                    {
                        BirimName = g.Key,
                        ResponseCount = g.Count(),
                        AverageSatisfaction = g.SelectMany(r => r.Answers)
                            .Where(a => survey.Questions.Any(q => q.Id == a.QuestionId && q.Type == QuestionType.Likert))
                            .Where(a => a.SelectedOptionId.HasValue)
                            .Select(a => {
                                var q = survey.Questions.First(x => x.Id == a.QuestionId);
                                var opt = q.Options.First(o => o.Id == a.SelectedOptionId);
                                return (double)(opt.Value ?? 0);
                            })
                            .DefaultIfEmpty(0)
                            .Average()
                    }).ToList()
            };
        }

        private async Task<(string? birim, string? bolum)> EnrichBirimBolumForResultsAsync(
            SurveyResponse r,
            IReadOnlyList<UnitTypeDto> unitTypes,
            IReadOnlyList<UnitDto> allUnits)
        {
            var birim = r.BirimAdi ?? r.FakulteAdi;
            var bolum = r.BolumAdi;

            if (!string.IsNullOrWhiteSpace(bolum))
                return (birim, bolum);

            if (r.RespondentUnitId is not int uid || uid <= 0)
                return (birim, bolum);

            try
            {
                var u = await _unitApi.GetUnitByIdAsync(uid);
                if (u == null)
                    return (birim, bolum);

                var fromUnitTypes = ResolveBolumFromUnitTypes(unitTypes, uid);

                if (LooksLikeFacultyUnit(u))
                {
                    if (!string.IsNullOrWhiteSpace(fromUnitTypes))
                        bolum = fromUnitTypes;
                    if (string.IsNullOrWhiteSpace(birim))
                        birim = u.Name?.Trim();
                    return (birim, bolum);
                }

                bolum = !string.IsNullOrWhiteSpace(fromUnitTypes)
                    ? fromUnitTypes
                    : u.Name?.Trim();

                var reporting = await FindFacultyReportingUnitAsync(u, r.FakulteAdi, allUnits);
                var birimOut = reporting?.Name?.Trim();
                if (string.IsNullOrWhiteSpace(birimOut))
                    birimOut = r.FakulteAdi;
                if (!string.IsNullOrWhiteSpace(birimOut))
                    birim = birimOut;

                if (string.IsNullOrWhiteSpace(birim) ||
                    string.Equals(birim, bolum, StringComparison.OrdinalIgnoreCase))
                {
                    var parent = await _unitApi.GetParentUnitAsync(u.Id);
                    if (parent != null && !string.IsNullOrWhiteSpace(parent.Name))
                        birim = parent.Name.Trim();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sonuçlar: bölüm/birim zenginleştirme başarısız RespondentUnitId={U}", r.RespondentUnitId);
            }

            return (birim, bolum);
        }

        private static string? ResolveBolumFromUnitTypes(IReadOnlyList<UnitTypeDto> types, int anchorUnitId)
        {
            if (anchorUnitId <= 0 || types.Count == 0)
                return null;

            var candidates = types.Where(t => t.UnitId == anchorUnitId).ToList();
            if (candidates.Count == 0)
                return null;

            var dept = candidates
                .Where(t => IsDepartmentLike(t.TypeDiscriminator))
                .Select(t => t.DisplayName)
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

            if (!string.IsNullOrEmpty(dept))
                return dept.Trim();

            return candidates
                .Select(t => t.DisplayName)
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
                ?.Trim();
        }

        private static bool IsDepartmentLike(string typeDiscriminator)
        {
            if (string.IsNullOrEmpty(typeDiscriminator)) return true;
            return typeDiscriminator.Contains("bölüm", StringComparison.OrdinalIgnoreCase)
                   || typeDiscriminator.Contains("program", StringComparison.OrdinalIgnoreCase)
                   || typeDiscriminator.Contains("anabilim", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<UnitDto?> FindFacultyReportingUnitAsync(
            UnitDto leaf, string? fakulteAdi, IReadOnlyList<UnitDto> allUnits)
        {
            if (LooksLikeFacultyUnit(leaf))
                return leaf;

            if (!string.IsNullOrEmpty(fakulteAdi))
            {
                var fac = MatchUnitInCatalog(allUnits, fakulteAdi);
                if (fac != null)
                    return fac;
            }

            var current = leaf;
            for (var depth = 0; depth < 12; depth++)
            {
                var parent = await _unitApi.GetParentUnitAsync(current.Id);
                if (parent == null)
                    break;
                if (LooksLikeFacultyUnit(parent))
                    return parent;
                current = parent;
            }

            return await ElevateToReportingUnitAsync(leaf, fakulteAdi, allUnits);
        }

        private async Task<UnitDto> ElevateToReportingUnitAsync(
            UnitDto start, string? fakulteAdi, IReadOnlyList<UnitDto> allUnits)
        {
            if (LooksLikeFacultyUnit(start))
                return start;

            if (!string.IsNullOrEmpty(fakulteAdi))
            {
                var fac = MatchUnitInCatalog(allUnits, fakulteAdi);
                if (fac != null)
                    return fac;
            }

            var current = start;
            for (var depth = 0; depth < 8; depth++)
            {
                var parent = await _unitApi.GetParentUnitAsync(current.Id);
                if (parent == null)
                    break;

                if (!string.IsNullOrEmpty(fakulteAdi) &&
                    string.Equals(parent.Name?.Trim(), fakulteAdi.Trim(), StringComparison.OrdinalIgnoreCase))
                    return parent;

                if (LooksLikeFacultyUnit(parent))
                    return parent;

                current = parent;
            }

            return start;
        }

        private static UnitDto? MatchUnitInCatalog(IReadOnlyList<UnitDto> all, string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return null;
            var cTrim = candidate.Trim();
            var norm = SurveyUnitMatchHelper.NormalizeBirim(cTrim);
            foreach (var u in all)
            {
                if (string.Equals(u.Name?.Trim(), cTrim, StringComparison.OrdinalIgnoreCase))
                    return u;
            }

            foreach (var u in all)
            {
                if (SurveyUnitMatchHelper.NormalizeBirim(u.Name ?? "") == norm)
                    return u;
            }

            return null;
        }

        private static bool LooksLikeFacultyUnit(UnitDto u)
        {
            var type = u.UnitTypeName ?? "";
            var name = u.Name ?? "";
            return type.Contains("Fakülte", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("Fakültesi", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("Fakultesi", StringComparison.OrdinalIgnoreCase);
        }
    }
}