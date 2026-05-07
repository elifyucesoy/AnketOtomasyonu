using AnketOtomasyonu.Data;
using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AnketOtomasyonu.Services.Implementations
{
    public class SurveyService : ISurveyService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<SurveyService> _logger;

        public SurveyService(ApplicationDbContext context, ILogger<SurveyService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<Survey?> GetSurveyWithQuestionsAsync(int surveyId)
        {
            return await _context.Surveys
                .Include(s => s.Questions.OrderBy(q => q.OrderIndex))
                    .ThenInclude(q => q.Options.OrderBy(o => o.OrderIndex))
                .Include(s => s.Responses)
                .Include(s => s.TargetUnits)
                .FirstOrDefaultAsync(s => s.Id == surveyId);
        }

        public async Task<Survey?> GetSurveyForEditAsync(int surveyId)
        {
            return await _context.Surveys
                .AsNoTracking()
                .Include(s => s.Questions.OrderBy(q => q.OrderIndex))
                    .ThenInclude(q => q.Options.OrderBy(o => o.OrderIndex))
                .Include(s => s.TargetUnits)
                .FirstOrDefaultAsync(s => s.Id == surveyId);
        }

        public async Task<IEnumerable<Survey>> GetAllSurveysAsync()
        {
            return await _context.Surveys
                .Include(s => s.Questions)
                .Include(s => s.Responses)
                .Include(s => s.TargetUnits)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<SurveySummaryDto>> GetAllSurveySummariesAsync()
        {
            return await SurveySummariesQuery(_context.Surveys.AsNoTracking())
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<SurveySummaryDto>> GetSurveySummariesByCreatorAsync(string creatorUserId)
        {
            return await SurveySummariesQuery(
                    _context.Surveys.AsNoTracking().Where(s => s.CreatedByUserId == creatorUserId))
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<SurveySummaryDto>> GetActiveAnonymousSurveySummariesAsync()
        {
            var now = DateTime.Now;
            return await SurveySummariesQuery(
                    _context.Surveys.AsNoTracking()
                        .Where(s => s.Status == SurveyStatus.Active
                            && s.IsAnonymous
                            && (s.StartDate == null || s.StartDate <= now)
                            && (s.EndDate == null || s.EndDate >= now)))
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }

        public async Task<Dictionary<int, List<string>>> GetTargetUnitNamesBySurveyIdsAsync(IReadOnlyList<int> surveyIds)
        {
            if (surveyIds == null || surveyIds.Count == 0)
                return new Dictionary<int, List<string>>();

            var rows = await _context.SurveyBirimler.AsNoTracking()
                .Where(sb => surveyIds.Contains(sb.SurveyId))
                .Select(sb => new { sb.SurveyId, sb.Birim })
                .ToListAsync();

            return rows
                .GroupBy(r => r.SurveyId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Birim)
                        .Where(b => !string.IsNullOrWhiteSpace(b))
                        .Select(b => b.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(b => b, StringComparer.OrdinalIgnoreCase)
                        .ToList());
        }

        private static IQueryable<SurveySummaryDto> SurveySummariesQuery(IQueryable<Survey> source)
        {
            return source.Select(s => new SurveySummaryDto
            {
                Id = s.Id,
                Title = s.Title,
                Description = s.Description,
                Status = s.Status,
                CreatedByUserId = s.CreatedByUserId,
                CreatedByName = s.CreatedByName,
                CreatedAt = s.CreatedAt,
                StartDate = s.StartDate,
                EndDate = s.EndDate,
                TargetRoles = s.TargetRoles,
                IsAnonymous = s.IsAnonymous,
                TargetFaculties = s.TargetFaculties,
                TargetDepartments = s.TargetDepartments,
                CreatedByBirim = s.CreatedByBirim,
                UnitId = s.UnitId,
                UnitName = s.UnitName,
                ApprovalStatus = s.ApprovalStatus,
                ApprovalNote = s.ApprovalNote,
                ApprovedAt = s.ApprovedAt,
                QuestionCount = s.Questions.Count,
                ResponseCount = s.Responses.Count
            });
        }

        public async Task<IEnumerable<Survey>> GetActiveSurveysAsync()
        {
            // Debug: Şimdilik tarih kısıtlamasını kaldıralım, sadece Status=Active olanları çekelim
            return await _context.Surveys
                .Where(s => s.Status == SurveyStatus.Active)
                .Include(s => s.Questions)
                .Include(s => s.Responses)
                .Include(s => s.TargetUnits)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<Survey>> GetActiveAnonymousSurveysAsync()
        {
            var now = DateTime.Now;
            return await _context.Surveys
                .Where(s => s.Status == SurveyStatus.Active
                    && s.IsAnonymous
                    && (s.StartDate == null || s.StartDate <= now)
                    && (s.EndDate == null || s.EndDate >= now))
                .Include(s => s.Questions)
                .Include(s => s.Responses)
                .Include(s => s.TargetUnits)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<Survey>> GetSurveysByCreatorAsync(string creatorUserId)
        {
            return await _context.Surveys
                .Where(s => s.CreatedByUserId == creatorUserId)
                .Include(s => s.Questions)
                .Include(s => s.Responses)
                .Include(s => s.TargetUnits)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<Survey>> GetSurveysByBirimAsync(string birim)
        {
            return await _context.Surveys
                .Where(s => s.TargetUnits.Any(pu => pu.Birim == birim))
                .Include(s => s.Questions)
                .Include(s => s.Responses)
                .Include(s => s.TargetUnits)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<Survey>> GetSurveysByBirimsAsync(List<string> birims)
        {
            if (birims == null || birims.Count == 0)
                return Enumerable.Empty<Survey>();

            return await _context.Surveys
                .Where(s => s.TargetUnits.Any(pu => birims.Contains(pu.Birim)))
                .Include(s => s.Questions)
                .Include(s => s.Responses)
                .Include(s => s.TargetUnits)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
        }

        public async Task<Survey> CreateSurveyAsync(
            SurveyCreateDto dto, string creatorUserId, string creatorName, string? creatorBirim = null, bool isSuperAdmin = false)
        {
            var survey = new Survey
            {
                Title = dto.Title?.Trim() ?? string.Empty,
                Description = dto.Description?.Trim() ?? string.Empty,
                IsAnonymous = dto.IsAnonymous,
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                TargetRoles = dto.TargetRoles != null && dto.TargetRoles.Any()
                    ? string.Join(",", dto.TargetRoles)
                    : string.Empty,
                TargetFaculties = dto.TargetFaculties != null && dto.TargetFaculties.Any()
                    ? string.Join(",", dto.TargetFaculties)
                    : null,
                TargetDepartments = dto.TargetDepartments != null && dto.TargetDepartments.Any()
                    ? string.Join(",", dto.TargetDepartments)
                    : null,
                CreatedByUserId = creatorUserId,
                CreatedByName = creatorName ?? string.Empty,
                CreatedByBirim = dto.CreatedByBirim ?? creatorBirim ?? string.Empty,
                UnitId = dto.UnitId,
                UnitName = dto.UnitName,
                Status = SurveyStatus.Draft,
                ApprovalStatus = isSuperAdmin ? ApprovalStatus.Approved : ApprovalStatus.Pending,
                ApprovalNote = isSuperAdmin ? "SuperAdmin tarafından oluşturuldu" : null,
                ApprovedAt = isSuperAdmin ? DateTime.UtcNow : null,
                CreatedAt = DateTime.UtcNow
            };

            // TargetUnits set up based on TargetFaculties
            if (dto.TargetFaculties != null && dto.TargetFaculties.Any())
            {
                foreach (var b in dto.TargetFaculties)
                {
                    survey.TargetUnits.Add(new SurveyBirim { Birim = b.Trim() });
                }
            }

            int order = 1;
            foreach (var qDto in dto.Questions)
            {
                var question = new Question
                {
                    Text = qDto.Text,
                    Type = qDto.Type,
                    IsRequired = qDto.IsRequired,
                    OrderIndex = order++
                };

                if (qDto.Type == QuestionType.Likert)
                {
                    question.Options = new List<QuestionOption>
                    {
                        new() { Text = "Çok Kötü",  Value = 1, OrderIndex = 1 },
                        new() { Text = "Kötü",       Value = 2, OrderIndex = 2 },
                        new() { Text = "Kararsızım", Value = 3, OrderIndex = 3 },
                        new() { Text = "İyi",        Value = 4, OrderIndex = 4 },
                        new() { Text = "Çok İyi",    Value = 5, OrderIndex = 5 },
                    };
                }
                else if (qDto.Type == QuestionType.MultipleChoice)
                {
                    var labels = new[] { "A", "B", "C", "D" };
                    question.Options = qDto.Options.Take(4)
                        .Select((o, i) => new QuestionOption
                        {
                            Text = $"{labels[i]}) {o.Text}",
                            OrderIndex = i + 1
                        }).ToList();
                }

                survey.Questions.Add(question);
            }

            _context.Surveys.Add(survey);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Anket oluşturuldu Id={Id}", survey.Id);
            return survey;
        }

        public async Task PublishSurveyAsync(int surveyId)
        {
            var s = await _context.Surveys.FindAsync(surveyId);
            if (s == null) return;
            s.Status = SurveyStatus.Active;
            s.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        public async Task CloseSurveyAsync(int surveyId)
        {
            var s = await _context.Surveys.FindAsync(surveyId);
            if (s == null) return;
            s.Status = SurveyStatus.Inactive;   // Kapalı kaldırıldı → Pasif
            s.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        public async Task DeleteSurveyAsync(int surveyId)
        {
            var s = await _context.Surveys.FindAsync(surveyId);
            if (s == null) return;
            _context.Surveys.Remove(s);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateSurveyAsync(int surveyId, SurveyCreateDto dto, bool resetToApproval = false)
        {
            var survey = await _context.Surveys
                .Include(s => s.TargetUnits)
                .Include(s => s.Questions)
                    .ThenInclude(q => q.Options)
                .FirstOrDefaultAsync(s => s.Id == surveyId);

            if (survey == null) return;

            // Temel bilgileri güncelle (DB sütunları null kabul etmediğinden CreateSurvey ile aynı normalizasyon)
            survey.Title = dto.Title?.Trim() ?? string.Empty;
            survey.Description = dto.Description?.Trim() ?? string.Empty;
            survey.IsAnonymous = dto.IsAnonymous;
            survey.StartDate = dto.StartDate;
            survey.EndDate = dto.EndDate;
            survey.TargetRoles = dto.TargetRoles != null && dto.TargetRoles.Any()
                ? string.Join(",", dto.TargetRoles)
                : string.Empty;
            survey.TargetFaculties = dto.TargetFaculties != null && dto.TargetFaculties.Any()
                ? string.Join(",", dto.TargetFaculties)
                : null;
            survey.TargetDepartments = dto.TargetDepartments != null && dto.TargetDepartments.Any()
                ? string.Join(",", dto.TargetDepartments)
                : null;
            survey.CreatedByBirim = dto.CreatedByBirim ?? survey.CreatedByBirim ?? string.Empty;
            if (dto.UnitId.HasValue) survey.UnitId = dto.UnitId;
            if (dto.UnitName != null) survey.UnitName = dto.UnitName;
            survey.UpdatedAt = DateTime.UtcNow;

            _context.SurveyBirimler.RemoveRange(survey.TargetUnits);
            if (dto.TargetFaculties != null && dto.TargetFaculties.Any())
            {
                foreach (var b in dto.TargetFaculties)
                {
                    survey.TargetUnits.Add(new SurveyBirim { Birim = b.Trim() });
                }
            }

            // Admin düzenlemesi → onaya gönder (Taslak + Pending)
            if (resetToApproval)
            {
                survey.Status = SurveyStatus.Draft;
                survey.ApprovalStatus = ApprovalStatus.Pending;
                survey.ApprovalNote = "Düzenlendi (Onay Bekliyor)";
                survey.ApprovedAt = null;
            }
            else
            {
                // SuperAdmin düzenlemesi veya otomatik onay durumu
                survey.ApprovalNote = "Düzenlendi (SuperAdmin tarafından)";
            }

            // FK ihlali önlemi: Eski cevapları sil (SurveyAnswers → SelectedOptionId FK)
            // SurveyResponse silmek cascade ile SurveyAnswer'ları da siler
            var responses = await _context.SurveyResponses
                .Where(r => r.SurveyId == surveyId)
                .ToListAsync();
            if (responses.Any())
            {
                _context.SurveyResponses.RemoveRange(responses);
                await _context.SaveChangesAsync(); // Önce cevapların silindiğini DB'ye işle!
                _logger.LogInformation("Anket düzenlendi, {N} eski cevap silindi. SurveyId={Id}", responses.Count, surveyId);
            }

            // Eski soruları ve seçenekleri sil
            foreach (var q in survey.Questions.ToList())
            {
                _context.RemoveRange(q.Options);
                _context.Remove(q);
            }

            // Yeni soruları ekle
            int order = 1;
            foreach (var qDto in dto.Questions)
            {
                var question = new Question
                {
                    Text = qDto.Text,
                    Type = qDto.Type,
                    IsRequired = qDto.IsRequired,
                    OrderIndex = order++
                };

                if (qDto.Type == QuestionType.Likert)
                {
                    question.Options = new List<QuestionOption>
                    {
                        new() { Text = "Çok Kötü",  Value = 1, OrderIndex = 1 },
                        new() { Text = "Kötü",       Value = 2, OrderIndex = 2 },
                        new() { Text = "Kararsızım", Value = 3, OrderIndex = 3 },
                        new() { Text = "İyi",        Value = 4, OrderIndex = 4 },
                        new() { Text = "Çok İyi",    Value = 5, OrderIndex = 5 },
                    };
                }
                else if (qDto.Type == QuestionType.MultipleChoice)
                {
                    var labels = new[] { "A", "B", "C", "D" };
                    question.Options = qDto.Options.Take(4)
                        .Select((o, i) => new QuestionOption
                        {
                            Text = $"{labels[i]}) {o.Text}",
                            OrderIndex = i + 1
                        }).ToList();
                }

                survey.Questions.Add(question);
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Anket güncellendi Id={Id}", surveyId);
        }
    }
}