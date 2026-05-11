using AnketOtomasyonu.Authorization;
using AnketOtomasyonu.Helpers;
using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Models.ViewModels;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AnketOtomasyonu.Controllers
{
    [AllowAnonymous]
    public class HomeController : Controller
    {
        private readonly ISurveyService _surveyService;
        private readonly ISurveyResponseService _responseService;
        private readonly IUnitApiService _unitApiService;

        public HomeController(
            ISurveyService surveyService,
            ISurveyResponseService responseService,
            IUnitApiService unitApiService)
        {
            _surveyService = surveyService;
            _responseService = responseService;
            _unitApiService = unitApiService;
        }

        public async Task<IActionResult> Index()
        {
            var userRole = User.FindFirstValue(ClaimTypes.Role);
            var isAuth = User.Identity?.IsAuthenticated == true;
            var hasSuperClaim = User.HasAnketPermission(AnketPermissions.SuperAdmin);
            var isAdmin = User.HasAnketPermission(AnketPermissions.Admin);
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

            var allSummaries = await _surveyService.GetAllSurveySummariesAsync();

            // SuperAdmin: tüm anketler (her durumda)
            // Admin: tüm Active+Approved anketler + kendi oluşturduğu anketler (taslak dahil)
            // Diğer: sadece Active+Approved
            IEnumerable<SurveySummaryDto> filteredSurveys;
            if (isAuth && hasSuperClaim)
            {
                filteredSurveys = allSummaries;
            }
            else if (isAuth && isAdmin)
            {
                filteredSurveys = allSummaries.Where(s =>
                    (s.Status == SurveyStatus.Active && s.ApprovalStatus == ApprovalStatus.Approved)
                    || string.Equals(s.CreatedByUserId, currentUserId, StringComparison.Ordinal));
            }
            else
            {
                filteredSurveys = allSummaries.Where(s =>
                    s.Status == SurveyStatus.Active
                    && s.ApprovalStatus == ApprovalStatus.Approved);
            }

            var filteredSurveyList = filteredSurveys.ToList();
            var targetUnitMap = await _surveyService.GetTargetUnitNamesBySurveyIdsAsync(
                filteredSurveyList.Select(s => s.Id).ToList());

            var surveys = filteredSurveyList.AsEnumerable();

            if (isAuth)
            {
                if (hasSuperClaim)
                {
                    // SuperAdmin: katalogda kısıt yok
                }
                else if (isAdmin)
                {
                    // Admin: birim kısıtı YOK (tüm üniversite anketlerini görür). Hedef rol filtresi aşağıda uygulanır.
                }
                else if (User.HasAnyStaffSurveyPermission())
                {
                    // Akademik / İdari: kendi birim anahtarları
                    surveys = surveys.Where(s =>
                        string.Equals(s.CreatedByBirim, "MERKEZ", StringComparison.OrdinalIgnoreCase) ||
                        SurveyUnitMatchHelper.MatchesSurveySummary(User, s, targetUnitMap, includeAuthorizedUnits: false));
                }
                else
                {
                    surveys = surveys.Where(s =>
                        SurveyUnitMatchHelper.MatchesSurveySummary(User, s, targetUnitMap, includeAuthorizedUnits: false));
                }

                if (!hasSuperClaim && !isAdmin)
                {
                    surveys = surveys.Where(s =>
                        SurveyTargetRoleHelper.TargetRolesAllowUser(s.TargetRoles, User));
                }
            }
            else
            {
                // Giriş yapmamış kullanıcı da tüm AKTİF anketleri görür (ama doldurmak için login gerekecek)
            }

            var surveyRows = surveys.ToList();

            var vm = new SurveyIndexViewModel
            {
                UserFullName = User.FindFirstValue(ClaimTypes.Name),
                UserRole = User.FindFirstValue(ClaimTypes.Role),
                IsLoggedIn = User.Identity?.IsAuthenticated == true,
                CanUseStaffSurveyTools = User.HasAnketPermission(AnketPermissions.SuperAdmin)
                    || User.HasAnketPermission(AnketPermissions.Admin),
                PreferSuperAdminSurveyLinks = hasSuperClaim,
                Surveys = surveyRows.Select(s => new SurveyListItemViewModel
                {
                    Id = s.Id,
                    Title = s.Title,
                    Description = s.Description,
                    Status = s.Status switch
                    {
                        SurveyStatus.Active => "Aktif",
                        SurveyStatus.Draft => "Taslak",
                        SurveyStatus.Inactive => "Pasif",
                        SurveyStatus.Closed => "Kapalı",
                        _ => "Bilinmiyor"
                    },
                    StatusBadgeClass = s.Status switch
                    {
                        SurveyStatus.Active => "bg-success",
                        SurveyStatus.Draft => "bg-warning text-dark",
                        SurveyStatus.Inactive => "bg-secondary",
                        _ => "bg-danger"
                    },
                    SurveyType = s.SurveyType,
                    QuestionCount = s.QuestionCount,
                    ResponseCount = s.ResponseCount,
                    CreatedByName = s.CreatedByName,
                    CreatedByBirim = s.CreatedByBirim ?? string.Empty,
                    CreatedAt = s.CreatedAt,
                    IsAnonymous = s.IsAnonymous,
                    TargetRoles = s.TargetRoles,
                    TargetUnits = SurveyTargetUnitsHelper.Resolve(s, targetUnitMap),
                    IsCreatedByCurrentUser = !string.IsNullOrEmpty(currentUserId)
                        && string.Equals(s.CreatedByUserId, currentUserId, StringComparison.Ordinal),
                    ApprovalStatus = s.ApprovalStatus
                }).ToList()
            };

            // Employee veya Admin ise "CreatedByBirim" bilgisini View'e taşı ki "Sonuçları Gör" butonu için kullanabilelim
            ViewBag.UserBirim = User.FindFirstValue("PersonelBirim");
            // Student için kendi fakülte/bölüm anketlerini ayırabilmemiz için FakulteAdi ekliyoruz
            ViewBag.UserFakulte = User.FindFirstValue("FakulteAdi");
            ViewBag.UserBolum = User.FindFirstValue("BolumAdi");

            return View(vm);
        }

        [Authorize(Policy = AnketPermissions.PolicySurveyResultsEntry)]
        [HttpGet]
        public async Task<IActionResult> Results(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var survey = await _surveyService.GetSurveyWithQuestionsAsync(id);
            if (survey == null) return NotFound();

            // Birim eşleşmesi: SurveyResponse ile aynı — UnitId için API adı + yetkili birim claim’leri sonuçta kullanılmaz
            string? fromUnitList = null;
            var accessToken = User.FindFirstValue("AccessToken");
            if (survey.UnitId.HasValue && survey.UnitId.Value > 0)
            {
                var unitDto = await _unitApiService.GetUnitByIdAsync(survey.UnitId.Value, accessToken);
                fromUnitList = unitDto?.Name;
            }

            var targetCsv = SurveyFillAccessHelper.BuildAccessCsv(survey, fromUnitList);
            bool unitMatches = SurveyUnitMatchHelper.MatchesSurveyBirimStrings(
                User,
                survey.CreatedByBirim,
                targetCsv,
                null,
                includeAuthorizedUnits: false);

            // Saf akademik: sonuç ekranı = akademik dashboard ile aynı kural (geniş unitMatches tek başına yetmez)
            bool birimUygun = unitMatches;
            if (User.HasAnketPermission(AnketPermissions.Akademik)
                && !User.HasAnketPermission(AnketPermissions.Admin)
                && !User.HasAnketPermission(AnketPermissions.SuperAdmin))
            {
                var deptScope = await AkademikDepartmentScopeHelper.TryResolveAsync(User, _unitApiService, accessToken, null);
                if (deptScope.Resolved && (deptScope.UnitIds.Count > 0 || deptScope.NormalizedNames.Count > 0))
                {
                    if (AkademikDepartmentScopeHelper.SurveyHasNoUnitTargeting(survey))
                        birimUygun = false;
                    else
                        birimUygun = AkademikDepartmentScopeHelper.SurveyMatchesDepartmentScope(survey, fromUnitList, deptScope);
                }
                else
                    birimUygun = AkademikDepartmentScopeHelper.SurveyMatchesAkademikClaimsFallback(survey, fromUnitList, User);
            }

            bool canView = User.HasAnketPermission(AnketPermissions.SuperAdmin)
                || survey.CreatedByUserId == userId
                || (birimUygun && User.HasAnySurveyResultsStaffCapability());

            if (!canView) return Unauthorized();

            var results = await _responseService.GetSurveyResultsAsync(id);
            ViewBag.SurveyId = id;
            var opts = await _responseService.GetRespondentFilterOptionsAsync(id);
            ViewBag.Bolumler = opts.Bolumler;
            ViewBag.Birimler = opts.Birimler;
            return View("~/Views/Admin/Results.cshtml", results);
        }
    }
}
