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

        public SurveyResponseController(
            ISurveyService surveyService,
            ISurveyResponseService responseService,
            IUnitApiService unitApiService)
        {
            _surveyService = surveyService;
            _responseService = responseService;
            _unitApiService = unitApiService;
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

            if (survey.Status != SurveyStatus.Active && !User.HasSuperAdminAccess())
            {
                TempData["Error"] = "Bu anket aktif değil.";
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

                // ── KULLANICI TİPİ KONTROLÜ ──
                var userType = AnketSurveyRoleResolver.ResolveSurveyUserRoleType(User);
                if (!string.IsNullOrEmpty(survey.TargetRoles) && !string.IsNullOrEmpty(userType))
                {
                    var allowedTypes = survey.TargetRoles
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (!TargetRoleMatchesUser(allowedTypes, User, userType))
                    {
                        TempData["Error"] = "Bu anket sizin kullanıcı tipinize açık değildir.";
                        return RedirectToAction("NotFound_", "SurveyResponse");
                    }
                }

                // ── YENİ HEDEFLEME KONTROLLERİ (Fakülte/Birim ve Bölüm) ──

                // 1. Hedef Fakülteler/Birimler — önce UnitId zinciri; başarısızsa metin eşlemesi (öğrenci/personel/akademik/admin/superadmin, çoklu izin fark etmez)
                bool birimMatch = false;
                if (survey.UnitId.HasValue && survey.UnitId.Value > 0)
                    birimMatch = await CheckUnitIdAccessAsync(survey.UnitId.Value);
                if (!birimMatch)
                    birimMatch = TryMatchSurveyBirimByStrings(survey);
                // ANKET_API_SUPER_ADMIN: çoklu rol + boş birim profili sık; hedef rol uygunsa birim filtresini aş
                if (!birimMatch && User.HasSuperAdminAccess())
                    birimMatch = true;

                if (!birimMatch)
                {
                    TempData["Error"] = "Bu anket sizin biriminize/fakültenize yönelik değildir.";
                    return RedirectToAction("NotFound_", "SurveyResponse");
                }

                // 2. Hedef Bölümler Kontrolü
                if (!string.IsNullOrEmpty(survey.TargetDepartments))
                {
                    var targets = survey.TargetDepartments.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var userBolum = userType == "Student"
                        ? User.FindFirstValue("BolumAdi")
                        : User.FindFirstValue("JobTitle");

                    if (userType == "Student") // Bölüm kontrolü genelde öğrenciler için kritik
                    {
                        var normalizedBolum = SurveyUnitMatchHelper.NormalizeBirim(userBolum);
                        if (string.IsNullOrEmpty(normalizedBolum) || !targets.Any(t => SurveyUnitMatchHelper.NormalizeBirim(t) == normalizedBolum))
                        {
                            TempData["Error"] = "Bu anket okuduğunuz bölüme açık değildir.";
                            return RedirectToAction("NotFound_", "SurveyResponse");
                        }
                    }
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

                var userType = AnketSurveyRoleResolver.ResolveSurveyUserRoleType(User);
                if (!string.IsNullOrEmpty(survey.TargetRoles) && !string.IsNullOrEmpty(userType))
                {
                    var allowedTypes = survey.TargetRoles
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (!TargetRoleMatchesUser(allowedTypes, User, userType))
                    {
                        TempData["Error"] = "Bu anket sizin kullanıcı tipinize açık değildir.";
                        return RedirectToAction("NotFound_", "SurveyResponse");
                    }
                }

                // ── YENİ HEDEFLEME KONTROLLERİ (Fakülte/Birim ve Bölüm) ──

                // 1. Hedef Fakülteler/Birimler — önce UnitId; olmazsa metin yedeği (tüm izin kombinasyonları için Fill ile aynı)
                bool birimMatch = false;
                if (survey.UnitId.HasValue && survey.UnitId.Value > 0)
                    birimMatch = await CheckUnitIdAccessAsync(survey.UnitId.Value);
                if (!birimMatch)
                    birimMatch = TryMatchSurveyBirimByStrings(survey);
                if (!birimMatch && User.HasSuperAdminAccess())
                    birimMatch = true;

                if (!birimMatch)
                {
                    TempData["Error"] = "Bu anket sizin biriminize/fakültenize yönelik değildir.";
                    return RedirectToAction("NotFound_", "SurveyResponse");
                }

                // 2. Hedef Bölümler Kontrolü
                if (!string.IsNullOrEmpty(survey.TargetDepartments))
                {
                    var targets = survey.TargetDepartments.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var userBolum = userType == "Student"
                        ? User.FindFirstValue("BolumAdi")
                        : User.FindFirstValue("JobTitle");

                    if (userType == "Student")
                    {
                        var normalizedUserBolum = SurveyUnitMatchHelper.NormalizeBirim(userBolum);
                        if (string.IsNullOrEmpty(normalizedUserBolum) || !targets.Any(t => SurveyUnitMatchHelper.NormalizeBirim(t) == normalizedUserBolum))
                        {
                            TempData["Error"] = "Bu anket okuduğunuz bölüme açık değildir.";
                            return RedirectToAction("NotFound_", "SurveyResponse");
                        }
                    }
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

            var (success, message) =
                await _responseService.SubmitResponseAsync(dto, userId, ip, userFullName, fakulteAdi, bolumAdi);

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

        // ── YARDIMCI METODLAR ──────────────────────────────

        /// <summary>
        /// UnitId tutmazsa: cookie’deki <b>tüm</b> birim anahtarları (çoklu UnitName, PersonelBirim, FakulteAdi, AuthorizedUnits).
        /// Hangi beş ANKET_* izin kombinasyonu olursa olsun aynı merkezi eşleme — tek birime sıkıştırılmaz.
        /// </summary>
        private bool TryMatchSurveyBirimByStrings(Survey survey) =>
            SurveyUnitMatchHelper.MatchesSurveyBirimStrings(User, survey.CreatedByBirim, survey.TargetFaculties);

        /// <summary>
        /// Hedef roller: önce <see cref="AnketSurveyRoleResolver.ResolveSurveyUserRoleType"/>, sonra cookie’deki
        /// 5 ANKET_* izin kodundan herhangi biri; anket hedefiyle <b>en az biri</b> eşleşebilir (çoklu izin).
        /// </summary>
        private static bool TargetRoleMatchesUser(string[] allowedTypes, ClaimsPrincipal user, string userType)
        {
            if (allowedTypes.Contains(userType, StringComparer.OrdinalIgnoreCase))
                return true;

            if (string.Equals(userType, "Akademik", StringComparison.OrdinalIgnoreCase) &&
                allowedTypes.Contains("Employee", StringComparer.OrdinalIgnoreCase))
                return true;

            if (string.Equals(userType, "Idari", StringComparison.OrdinalIgnoreCase))
            {
                if (allowedTypes.Contains("Idari", StringComparer.OrdinalIgnoreCase)) return true;
                if (allowedTypes.Contains("Employee", StringComparer.OrdinalIgnoreCase)) return true;
            }

            // ── 5 izin kodu × anket hedef rolü (ANKET_API_* / ANKET_IDARI) ──
            foreach (var target in allowedTypes)
            {
                foreach (var code in AnketPermissions.AllCodes)
                {
                    if (!user.HasClaim(AnketPermissions.ClaimType, code))
                        continue;
                    if (SurveyTargetMatchesPermissionCode(code, target))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Uzak sistemdeki izin kodu, anket oluştururken seçilen hedef rol satırıyla uyumlu mu?
        /// </summary>
        private static bool SurveyTargetMatchesPermissionCode(string permissionCode, string surveyTargetRaw)
        {
            var t = surveyTargetRaw.Trim();
            if (string.IsNullOrEmpty(t)) return false;

            bool Is(params string[] aliases) =>
                aliases.Any(a => string.Equals(a, t, StringComparison.OrdinalIgnoreCase));

            return permissionCode switch
            {
                AnketPermissions.SuperAdmin => true,
                AnketPermissions.Admin =>
                    Is("Employee", "Student", "Akademik", "Idari", "Admin", "SuperAdmin", "Personel"),
                AnketPermissions.Akademik =>
                    Is("Akademik", "Employee", "Personel"),
                AnketPermissions.Idari =>
                    Is("Idari", "Employee", "Personel"),
                AnketPermissions.Student =>
                    Is("Student", "Öğrenci"),
                _ => false
            };
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
            // Kullanıcının tüm UnitId claimlerini al (birden fazla olabilir)
            var userUnitIds = User.FindAll("UnitId")
                .Select(c => int.TryParse(c.Value, out var id) ? id : 0)
                .Where(id => id > 0)
                .ToHashSet();

            if (!userUnitIds.Any()) return false;

            // 1. Direkt eşleşme
            if (userUnitIds.Contains(surveyUnitId)) return true;

            // 2. ParentId zinciri: her kullanıcı birimi için üst birimi kontrol et
            //    UnitList cache'den gelir (API çağrısı yapmaz)
            foreach (var userUnitId in userUnitIds)
            {
                var parentUnit = await _unitApiService.GetParentUnitAsync(userUnitId);
                if (parentUnit != null && parentUnit.Id == surveyUnitId)
                    return true;

                // İsteğe bağlı: bir kademe daha yukarı çık (bölüm → fakülte → üniversite)
                if (parentUnit != null)
                {
                    var grandParent = await _unitApiService.GetParentUnitAsync(parentUnit.Id);
                    if (grandParent != null && grandParent.Id == surveyUnitId)
                        return true;
                }
            }

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