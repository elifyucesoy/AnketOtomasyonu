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
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Claims;
using AnketOtomasyonu.Configuration;

namespace AnketOtomasyonu.Controllers
{
    [AllowAnonymous]
    public class SurveyResponseController : Controller
    {
        private readonly ISurveyService _surveyService;
        private readonly ISurveyResponseService _responseService;
        private readonly IUnitApiService _unitApiService;
        private readonly ICatalogFacultyDepartmentResolver _catalogFacultyDepartmentResolver;
        private readonly ILogger<SurveyResponseController> _logger;

        public SurveyResponseController(
            ISurveyService surveyService,
            ISurveyResponseService responseService,
            IUnitApiService unitApiService,
            ICatalogFacultyDepartmentResolver catalogFacultyDepartmentResolver,
            ILogger<SurveyResponseController> logger)
        {
            _surveyService = surveyService;
            _responseService = responseService;
            _unitApiService = unitApiService;
            _catalogFacultyDepartmentResolver = catalogFacultyDepartmentResolver;
            _logger = logger;
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
            var sw = Stopwatch.StartNew();

            // Önce hafif metadata ile akış kararını ver (ders değerlendirme mi, normal mi).
            // Bu sayede gereksiz yere ağır include'lı sorgu çalıştırmıyoruz.
            var meta = await _surveyService.GetSurveyMetadataAsync(id);
            if (meta == null)
            {
                TempData["Error"] = "Anket bulunamadı.";
                return RedirectToAction("NotFound_", "SurveyResponse");
            }

            if (meta.Status != SurveyStatus.Active && !User.HasAnketPermission(AnketPermissions.SuperAdmin))
            {
                TempData["Error"] = "Bu anket aktif değil.";
                if (meta.IsAnonymous)
                    return RedirectToAction("NotFound_", "SurveyResponse");
                return RedirectToAction("Index", "Home");
            }

            if (meta.ApprovalStatus != ApprovalStatus.Approved && !User.HasAnketPermission(AnketPermissions.SuperAdmin))
            {
                TempData["Error"] = "Bu anket henüz onaylanmadığı için katılıma açık değildir.";
                if (meta.IsAnonymous)
                    return RedirectToAction("NotFound_", "SurveyResponse");
                return RedirectToAction("Index", "Home");
            }

            // ── DERS DEĞERLENDİRME — SurveyType kontrolü ─────────────────────
            if (meta.SurveyType == SurveyType.CourseEvaluation)
            {
                var courseState2 = CourseEvalSessionHelper.Get(HttpContext.Session);
                if (courseState2 == null || courseState2.SurveyId != id)
                    return RedirectToAction("CourseEvalLogin", "CourseEvaluation", new { id });
            }

            // ── DERS DEĞERLENDİRME — OBIS session akışı ─────────────────────
            var courseState = CourseEvalSessionHelper.Get(HttpContext.Session);
            if (courseState != null && courseState.SurveyId == id)
            {
                // Ders seçilmediyse ders listesine gönder (DB sorgusu YAPMA — gereksiz).
                if (string.IsNullOrEmpty(courseState.SelectedCourseKey))
                    return RedirectToAction("CourseEvalCourses", "CourseEvaluation", new { id });

                var ceUserId = CourseEvalSessionHelper.BuildResponseUserId(
                    courseState.OgrNo, courseState.SelectedCourseKey);

                if (await _responseService.HasUserRespondedAsync(id, ceUserId))
                {
                    TempData["Error"] = "Bu ders için anketi zaten doldurdunuz.";
                    return RedirectToAction("CourseEvalCourses", "CourseEvaluation", new { id });
                }

                // OBIS akışı: TargetUnits gerekmiyor → en hafif sorgu (sadece Questions + Options).
                var dbSwOnly = Stopwatch.StartNew();
                var ceSurvey = await _surveyService.GetSurveyWithQuestionsOnlyAsync(id);
                dbSwOnly.Stop();
                if (ceSurvey == null)
                {
                    TempData["Error"] = "Anket bulunamadı.";
                    return RedirectToAction("NotFound_", "SurveyResponse");
                }

                // Seçili dersin görünen bilgisini ViewData'ya aktar
                var selCourse = courseState.Courses
                    .FirstOrDefault(c => c.Key == courseState.SelectedCourseKey);
                if (selCourse != null)
                {
                    ViewData["CourseEvalDersNo"]  = selCourse.DersNo;
                    ViewData["CourseEvalDersAdi"] = selCourse.DersAdi;
                    ViewData["CourseEvalYil"]     = selCourse.Yil;
                    ViewData["CourseEvalKey"]     = selCourse.Key;
                }
                ViewData["IsCourseEval"] = true;

                var ceVm = new SurveyFillViewModel
                {
                    SurveyId    = ceSurvey.Id,
                    Title       = ceSurvey.Title,
                    Description = ceSurvey.Description,
                    IsAnonymous = ceSurvey.IsAnonymous,
                    Questions   = ceSurvey.Questions
                        .OrderBy(q => q.OrderIndex)
                        .Select(q => new FillQuestionViewModel
                        {
                            QuestionId  = q.Id,
                            Text        = q.Text,
                            Type        = q.Type,
                            IsRequired  = q.IsRequired,
                            OrderIndex  = q.OrderIndex,
                            Options     = q.Options
                                .OrderBy(o => o.OrderIndex)
                                .Select(o => new FillOptionViewModel
                                {
                                    Id         = o.Id,
                                    Text       = o.Text,
                                    Value      = o.Value,
                                    OrderIndex = o.OrderIndex
                                }).ToList()
                        }).ToList()
                };
                sw.Stop();
                _logger.LogInformation(
                    "[Fill OBIS] SurveyId={Id} db={Db}ms toplam={Total}ms soruSayisi={Q}",
                    id, dbSwOnly.ElapsedMilliseconds, sw.ElapsedMilliseconds, ceVm.Questions.Count);
                return View(ceVm);
            }
            // ── DERS DEĞERLENDİRME session sonu ──────────────────────────────

            // Normal akış: hedef birim/bölüm kontrolleri için TargetUnits gerekiyor → tam graf.
            var fullSurveySw = Stopwatch.StartNew();
            var survey = await _surveyService.GetSurveyForEditAsync(id);
            fullSurveySw.Stop();
            if (survey == null)
            {
                TempData["Error"] = "Anket bulunamadı.";
                return RedirectToAction("NotFound_", "SurveyResponse");
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
                            .Select(o2 => new FillOptionViewModel
                            {
                                Id = o2.Id,
                                Text = o2.Text,
                                Value = o2.Value,
                                OrderIndex = o2.OrderIndex
                            }).ToList()
                    }).ToList()
            };

            sw.Stop();
            _logger.LogInformation(
                "[Fill Normal] SurveyId={Id} db(survey)={Db}ms toplam={Total}ms soruSayisi={Q}",
                id, fullSurveySw.ElapsedMilliseconds, sw.ElapsedMilliseconds, vm.Questions.Count);
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(SurveySubmitDto dto)
        {
            var ip = GetClientIp();

            // Hafif metadata sorgusu — Submit içinde yalnızca SurveyType ve IsAnonymous bakılıyor;
            // SubmitResponseAsync ayrıca kendi içinde Survey + Questions sorgusu yapıyor.
            var survey = await _surveyService.GetSurveyMetadataAsync(dto.SurveyId);
            if (survey == null)
            {
                TempData["Error"] = "Anket bulunamadı.";
                return RedirectToAction("NotFound_", "SurveyResponse");
            }

            // ── SurveyType = CourseEvaluation ise session yoksa login'e gönder ──
            if (survey.SurveyType == SurveyType.CourseEvaluation)
            {
                var cs = CourseEvalSessionHelper.Get(HttpContext.Session);
                if (cs == null || cs.SurveyId != dto.SurveyId)
                    return RedirectToAction("CourseEvalLogin", "CourseEvaluation", new { id = dto.SurveyId });
            }

            // ── DERS DEĞERLENDİRME — OBIS session Submit akışı ───────────────
            var courseState = CourseEvalSessionHelper.Get(HttpContext.Session);
            if (courseState != null && courseState.SurveyId == dto.SurveyId
                && !string.IsNullOrEmpty(courseState.SelectedCourseKey))
            {
                var ceUserId = CourseEvalSessionHelper.BuildResponseUserId(
                    courseState.OgrNo, courseState.SelectedCourseKey);

                // BirimAdi: öncelik fakülte; OBIS yalnızca bölüm döndüyse bölümü üst sütunda göster.
                var ceBirim = !string.IsNullOrWhiteSpace(courseState.Profile.FakulteAdi)
                    ? courseState.Profile.FakulteAdi.Trim()
                    : (string.IsNullOrWhiteSpace(courseState.Profile.BolumAdi)
                        ? null
                        : courseState.Profile.BolumAdi.Trim());

                var (ceSuc, ceMsg) = await _responseService.SubmitResponseAsync(
                    dto,
                    userId:       ceUserId,
                    ipAddress:    ip,
                    userFullName: null,
                    fakulteAdi:   courseState.Profile.FakulteAdi,
                    bolumAdi:     courseState.Profile.BolumAdi,
                    respondentUnitId: null,
                    birimAdi:     ceBirim);

                if (!ceSuc)
                {
                    TempData["Error"] = ceMsg;
                    return RedirectToAction("Fill", new { id = dto.SurveyId });
                }

                // OBIS akışı: aynı oturumda başka ders seçilebilsin — çıkış/ana sayfa yok.
                courseState.SelectedCourseKey = null;
                CourseEvalSessionHelper.Set(HttpContext.Session, courseState);
                TempData["SuccessMessage"] = "Anketiniz kaydedildi. İsterseniz başka bir ders için devam edebilirsiniz.";
                return RedirectToAction("CourseEvalCourses", "CourseEvaluation", new { id = dto.SurveyId });
            }
            // ── DERS DEĞERLENDİRME session sonu ──────────────────────────────

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

            // Birim/bölüm claim'leri; katılımcı adı veritabanına yazılmaz.
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
                    dto, userId, ip, userFullName: null, fakulteAdi, bolumForDb,
                    respondentUnitId, birimAdiCatalog);

            if (!success)
            {
                TempData["Error"] = message;
                if (survey.IsAnonymous)
                    return RedirectToAction("AlreadyFilled");
                return RedirectToAction("Fill", new { id = dto.SurveyId });
            }

            return await CompleteSurveyAndGoHomeAsync(message);
        }

        /// <summary>
        /// Kimlik çerezi kapatılır ve (OBIS oturumu hariç) uygulama çerezleri silinir; ana sayfaya yönlendirilir.
        /// ASP.NET oturum çerezi korunur — ders değerlendirme (OBIS) oturumu etkilenmez.
        /// </summary>
        private async Task<IActionResult> CompleteSurveyAndGoHomeAsync(string successMessage)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            const string sessionCookieName = ".AspNetCore.Session";
            var pathBase = Request.PathBase.HasValue ? Request.PathBase.Value! : "/";
            foreach (var key in Request.Cookies.Keys.ToList())
            {
                if (string.Equals(key, sessionCookieName, StringComparison.Ordinal))
                    continue;

                Response.Cookies.Delete(key, new CookieOptions
                {
                    Path = pathBase
                });
                Response.Cookies.Delete(key, new CookieOptions
                {
                    Path = "/"
                });
            }

            TempData["SuccessMessage"] = successMessage;
            return RedirectToAction("Index", "Home");
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