using AnketOtomasyonu.Authorization;
using AnketOtomasyonu.Helpers;
using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Models.ViewModels;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;

namespace AnketOtomasyonu.Controllers
{
    [AllowAnonymous]
    public class SurveyResponseController : Controller
    {
        private readonly ISurveyService _surveyService;
        private readonly ISurveyResponseService _responseService;
        private readonly IUnitApiService _unitApiService;
        private readonly ICatalogFacultyDepartmentResolver _catalogFacultyDepartmentResolver;

        public SurveyResponseController(
            ISurveyService surveyService,
            ISurveyResponseService responseService,
            IUnitApiService unitApiService,
            ICatalogFacultyDepartmentResolver catalogFacultyDepartmentResolver)
        {
            _surveyService = surveyService;
            _responseService = responseService;
            _unitApiService = unitApiService;
            _catalogFacultyDepartmentResolver = catalogFacultyDepartmentResolver;
        }

        // GET /SurveyResponse/PublicSurveys
        [HttpGet]
        public async Task<IActionResult> PublicSurveys()
        {
            var surveys = (await _surveyService.GetActiveAnonymousSurveySummariesAsync()).ToList();
            var targetUnitMap = await _surveyService.GetTargetUnitNamesBySurveyIdsAsync(
                surveys.Select(s => s.Id).ToList());

            var vm = new SurveyIndexViewModel
            {
                UserFullName = User.FindFirstValue(ClaimTypes.Name),
                UserRole = User.FindFirstValue(ClaimTypes.Role),
                IsLoggedIn = User.Identity?.IsAuthenticated == true,
                Surveys = surveys.Select(s => new SurveyListItemViewModel
                {
                    Id = s.Id,
                    Title = s.Title,
                    Description = s.Description,
                    Status = "Aktif",
                    StatusBadgeClass = "bg-success",
                    QuestionCount = s.QuestionCount,
                    ResponseCount = s.ResponseCount,
                    CreatedByName = s.CreatedByName,
                    CreatedByBirim = s.CreatedByBirim ?? string.Empty,
                    CreatedAt = s.CreatedAt,
                    IsAnonymous = true,
                    TargetRoles = s.TargetRoles,
                    TargetUnits = SurveyTargetUnitsHelper.Resolve(s, targetUnitMap)
                }).ToList()
            };

            return View(vm);
        }

        // GET /SurveyResponse/Fill/{id}
        [HttpGet]
        public async Task<IActionResult> Fill(int id)
        {
            var survey = await _surveyService.GetSurveyWithQuestionsAsync(id);
            if (survey == null)
            {
                TempData["Error"] = "Anket bulunamadı.";
                return RedirectToAction("NotFound_", "SurveyResponse");
            }

            if (survey.Status != SurveyStatus.Active && !User.HasAnketPermission(AnketPermissions.SuperAdmin))
            {
                TempData["Error"] = "Bu anket aktif değil.";
                if (survey.IsAnonymous)
                    return RedirectToAction("NotFound_", "SurveyResponse");
                return RedirectToAction("Index", "Home");
            }

            if (survey.ApprovalStatus != ApprovalStatus.Approved && !User.HasAnketPermission(AnketPermissions.SuperAdmin))
            {
                TempData["Error"] = "Bu anket henüz onaylanmadığı için katılıma açık değildir.";
                if (survey.IsAnonymous)
                    return RedirectToAction("NotFound_", "SurveyResponse");
                return RedirectToAction("Index", "Home");
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (survey.IsAnonymous)
            {
                var ip = GetClientIp();
                if (!string.IsNullOrEmpty(ip) &&
                    await _responseService.HasRespondedByIpAsync(id, ip))
                {
                    TempData["Error"] = "Bu anketi zaten doldurdunuz.";
                    return RedirectToAction("AlreadyFilled");
                }
            }
            else
            {
                if (User.Identity == null || !User.Identity.IsAuthenticated)
                {
                    var returnUrl = Url.Action("Fill", "SurveyResponse", new { id });
                    return RedirectToAction("Login", "Auth", new { returnUrl });
                }

                // ── HEDEF ROLLER: tek rol yerine tüm ANKET_* claim’leri anket hedefleriyle eşleştirilir (çoklu yetki birleşimi).
                if (!string.IsNullOrWhiteSpace(survey.TargetRoles))
                {
                    var allowedTypes = survey.TargetRoles
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (!SurveyTargetRoleHelper.TargetRoleMatchesUser(allowedTypes, User))
                    {
                        TempData["Error"] = "Bu anketin hedef rolü, hesabınızdaki ANKET izinleriyle eşleşmiyor.";
                        return RedirectToAction("NotFound_", "SurveyResponse");
                    }
                }

                // ── BİRİM: önce isim listesi (hedef + UnitName + UnitId→UnitList adı), sonra UnitId üst zinciri
                var birimMatch = await TrySurveyBirimAccessAsync(survey);

                if (!birimMatch)
                {
                    TempData["Error"] = "Bu anket sizin biriminize/fakültenize yönelik değildir.";
                    return RedirectToAction("NotFound_", "SurveyResponse");
                }

                if (!await UserMatchesSurveyTargetDepartmentsAsync(survey))
                {
                    TempData["Error"] = "Bu anket seçilen bölüm/hedef kapsamına dahil değilsiniz.";
                    return RedirectToAction("NotFound_", "SurveyResponse");
                }

                if (await _responseService.HasUserRespondedAsync(id, userId!))
                {
                    TempData["Error"] = "Bu anketi zaten doldurdunuz.";
                    return RedirectToAction("Index", "Home");
                }
            }

            var vm = new SurveyFillViewModel
            {
                SurveyId = survey.Id,
                Title = survey.Title,
                Description = survey.Description,
                IsAnonymous = survey.IsAnonymous,
                Questions = survey.Questions
                    .OrderBy(q => q.OrderIndex)
                    .Select(q => new FillQuestionViewModel
                    {
                        QuestionId = q.Id,
                        Text = q.Text,
                        Type = q.Type,
                        IsRequired = q.IsRequired,
                        OrderIndex = q.OrderIndex,
                        Options = q.Options
                            .OrderBy(o => o.OrderIndex)
                            .Select(o => new FillOptionViewModel
                            {
                                Id = o.Id,
                                Text = o.Text,
                                Value = o.Value,
                                OrderIndex = o.OrderIndex
                            }).ToList()
                    }).ToList()
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(SurveySubmitDto dto)
        {
            var ip = GetClientIp();

            var survey = await _surveyService.GetSurveyWithQuestionsAsync(dto.SurveyId);
            if (survey == null)
            {
                TempData["Error"] = "Anket bulunamadı.";
                return RedirectToAction("NotFound_", "SurveyResponse");
            }

            string userId;

            if (survey.IsAnonymous)
            {
                userId = ip;
            }
            else
            {
                if (User.Identity == null || !User.Identity.IsAuthenticated)
                {
                    var returnUrl = Url.Action("Fill", "SurveyResponse", new { id = dto.SurveyId });
                    return RedirectToAction("Login", "Auth", new { returnUrl });
                }

                userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

                if (!string.IsNullOrWhiteSpace(survey.TargetRoles))
                {
                    var allowedTypes = survey.TargetRoles
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (!SurveyTargetRoleHelper.TargetRoleMatchesUser(allowedTypes, User))
                    {
                        TempData["Error"] = "Bu anketin hedef rolü, hesabınızdaki ANKET izinleriyle eşleşmiyor.";
                        return RedirectToAction("NotFound_", "SurveyResponse");
                    }
                }

                var birimMatch = await TrySurveyBirimAccessAsync(survey);

                if (!birimMatch)
                {
                    TempData["Error"] = "Bu anket sizin biriminize/fakültenize yönelik değildir.";
                    return RedirectToAction("NotFound_", "SurveyResponse");
                }

                if (!await UserMatchesSurveyTargetDepartmentsAsync(survey))
                {
                    TempData["Error"] = "Bu anket seçilen bölüm/hedef kapsamına dahil değilsiniz.";
                    return RedirectToAction("NotFound_", "SurveyResponse");
                }

                if (await _responseService.HasUserRespondedAsync(dto.SurveyId, userId!))
                {
                    TempData["Error"] = "Bu anketi zaten doldurdunuz.";
                    return RedirectToAction("Index", "Home");
                }
            }

            // Demografik bilgileri claim'lerden oku
            var userFullName = User.FindFirstValue(ClaimTypes.Name);
            var fakulteAdi = User.FindFirstValue("FakulteAdi");
            var bolumAdi = User.FindFirstValue("BolumAdi");

            int? respondentUnitId = null;
            string? birimAdiCatalog = null;
            string? bolumFromCatalog = null;
            if (!survey.IsAnonymous && User.Identity?.IsAuthenticated == true)
                (respondentUnitId, birimAdiCatalog, bolumFromCatalog) = await ResolveRespondentCatalogUnitAsync(User);

            var bolumForDb = !string.IsNullOrWhiteSpace(bolumFromCatalog)
                ? bolumFromCatalog
                : bolumAdi;

            var (success, message) =
                await _responseService.SubmitResponseAsync(
                    dto, userId, ip, userFullName, fakulteAdi, bolumForDb,
                    respondentUnitId, birimAdiCatalog);

            if (!success)
            {
                TempData["Error"] = message;
                if (survey.IsAnonymous)
                    return RedirectToAction("AlreadyFilled");
                return RedirectToAction("Fill", new { id = dto.SurveyId });
            }

            TempData["SuccessMessage"] = message;
            if (survey.IsAnonymous)
                TempData["IsAnonymous"] = "true";

            // Anket gönderildikten sonra logout yap — bir sonraki anket için tekrar login gerekecek
            if (!survey.IsAnonymous)
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return RedirectToAction("Success");
        }

        [HttpGet]
        public IActionResult Success() => View();

        [HttpGet]
        public IActionResult AlreadyFilled() => View();

        [HttpGet("SurveyResponse/NotFound_")]
        public IActionResult NotFound_() => View();

        /// <summary>
        /// UnitList + UnitById (ResolveProfileUnitIds ile aynı kaynak): yaprak birim (bölüm) ile üst birim (fakülte) ayrı.
        /// Kayıtta <see cref="SurveyResponse.BolumAdi"/> = bölüm, <see cref="SurveyResponse.BirimAdi"/> = fakülte/üst birim.
        /// </summary>
        private async Task<(int? UnitId, string? BirimAdi, string? BolumAdi)> ResolveRespondentCatalogUnitAsync(ClaimsPrincipal user)
        {
            var token = user.FindFirstValue("AccessToken");
            var personelBirim = user.FindFirstValue("PersonelBirim")?.Trim();
            var fakulteAdi = user.FindFirstValue("FakulteAdi")?.Trim();
            var bolumClaim = user.FindFirstValue("BolumAdi")?.Trim();

            var ids = user.FindAll("UnitId")
                .Select(c => int.TryParse(c.Value, out var v) ? v : (int?)null)
                .Where(v => v is > 0)
                .Select(v => v!.Value)
                .Distinct()
                .ToList();

            var fromClaimIds = ids.Count > 0
                ? await _unitApiService.ResolveProfileUnitIdsAsync(ids, token)
                : new List<UnitDto>();

            var extraHints = user.FindAll("UnitName")
                .Select(c => c.Value?.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var resolved = await _catalogFacultyDepartmentResolver.ResolveAsync(
                fromClaimIds,
                personelBirim,
                fakulteAdi,
                bolumClaim,
                token,
                extraHints);

            return (resolved.UnitId, resolved.ReportingFacultyOrUnitName, resolved.DepartmentName);
        }

        // ── YARDIMCI METODLAR ──────────────────────────────

        /// <summary>
        /// Admin panelinden oluşturulan anketlerde hedef çoklu boş kalıp yalnızca <see cref="Survey.UnitId"/>/
        /// <see cref="Survey.UnitName"/> dolu olabildiğinden önce birleşik isim listesi, sonra üst kimlik zinciri.
        /// </summary>
        private async Task<bool> TrySurveyBirimAccessAsync(Survey survey)
        {
            string? fromUnitList = null;
            if (survey.UnitId.HasValue && survey.UnitId.Value > 0)
            {
                var u = await _unitApiService.GetUnitByIdAsync(survey.UnitId.Value);
                fromUnitList = u?.Name;
            }

            var csv = SurveyFillAccessHelper.BuildAccessCsv(survey, fromUnitList);
            var keysFromUnitIds = await BuildUserUnitHierarchyNormalizedKeysAsync(User);

            if (SurveyUnitMatchHelper.MatchesSurveyBirimStrings(
                    User, survey.CreatedByBirim, csv, keysFromUnitIds, includeAuthorizedUnits: false))
                return true;

            if (survey.UnitId.HasValue && survey.UnitId.Value > 0)
                return await CheckUnitIdAccessAsync(survey.UnitId.Value);

            return false;
        }

        private async Task<bool> UserMatchesSurveyTargetDepartmentsAsync(Survey survey)
        {
            if (string.IsNullOrWhiteSpace(survey.TargetDepartments))
                return true;

            var targets = survey.TargetDepartments
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(SurveyUnitMatchHelper.NormalizeBirim)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (targets.Count == 0)
                return true;

            var userKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string? s)
            {
                var n = SurveyUnitMatchHelper.NormalizeBirim(s);
                if (!string.IsNullOrEmpty(n))
                    userKeys.Add(n);
            }

            Add(User.FindFirstValue("BolumAdi"));
            Add(User.FindFirstValue("FakulteAdi"));
            Add(User.FindFirstValue("PersonelBirim"));
            foreach (var c in User.FindAll("UnitName"))
                Add(c.Value);

            foreach (var k in await BuildUserUnitHierarchyNormalizedKeysAsync(User))
                userKeys.Add(k);

            var (_, _, bolumCatalog) = await ResolveRespondentCatalogUnitAsync(User);
            Add(bolumCatalog);

            return userKeys.Any(targets.Contains);
        }

        /// <summary>
        /// Öğrencide cookie’de bazen yalnızca <c>UnitId</c> olur; claim metinleri boş kalır.
        /// UnitList’ten her UnitId ve üst zincir adlarını ekleyerek admin anketindeki birim metinleriyle eşler.
        /// </summary>
        private async Task<HashSet<string>> BuildUserUnitHierarchyNormalizedKeysAsync(ClaimsPrincipal user)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in user.FindAll("UnitId"))
            {
                if (!int.TryParse(c.Value, out var uid) || uid <= 0)
                    continue;

                var cur = uid;
                for (var depth = 0; depth < 14 && cur > 0; depth++)
                {
                    var unit = await _unitApiService.GetUnitByIdAsync(cur);
                    if (!string.IsNullOrWhiteSpace(unit?.Name))
                    {
                        var n = SurveyUnitMatchHelper.NormalizeBirim(unit.Name);
                        if (!string.IsNullOrEmpty(n))
                            keys.Add(n);
                    }

                    var parent = await _unitApiService.GetParentUnitAsync(cur);
                    if (parent == null)
                        break;
                    cur = parent.Id;
                }
            }

            return keys;
        }

        /// <summary>
        /// Kullanıcının UnitId claimlerini okur ve verilen surveyUnitId ile eşleşip
        /// eşleşmediğini kontrol eder.
        /// Eşleşme mantığı:
        ///   1. Kullanıcının herhangi bir UnitId'si == surveyUnitId  (direkt eşleşme)
        ///   2. Kullanıcının biriminin parentId'si == surveyUnitId  (alt birim → üst fakülte)
        /// </summary>
        private async Task<bool> CheckUnitIdAccessAsync(int surveyUnitId)
        {
            var userUnitIds = User.FindAll("UnitId")
                .Select(c => int.TryParse(c.Value, out var id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (userUnitIds.Count == 0) return false;

            // Kullanıcı biriminden yukarı çık (bölüm → fakülte …); anket birimi zincirde mi?
            foreach (var userUnitId in userUnitIds)
            {
                var cur = userUnitId;
                for (var depth = 0; depth < 16 && cur > 0; depth++)
                {
                    if (cur == surveyUnitId)
                        return true;

                    var parent = await _unitApiService.GetParentUnitAsync(cur);
                    if (parent == null)
                        break;
                    cur = parent.Id;
                }
            }

            // Anket hedefi alt birim (ör. bölüm) iken kullanıcıda yalnız üst fakülte UnitId varsa eşleşme yok:
            // yukarı doğru yürüyüp “herhangi bir üst kullanıcı kimliğinde var mı” kontrolü kaldırıldı.

            return false;
        }

        // ── IP ALMA YARDIMCI METODU ──────────────────────
        private string GetClientIp()
        {
            var forwarded = HttpContext.Request.Headers["X-Forwarded-For"]
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(forwarded))
                return forwarded.Split(',')[0].Trim();

            var ip = HttpContext.Connection.RemoteIpAddress;
            if (ip == null) return "unknown";

            if (ip.IsIPv4MappedToIPv6)
                return ip.MapToIPv4().ToString();

            return ip.ToString();
        }
    }
}