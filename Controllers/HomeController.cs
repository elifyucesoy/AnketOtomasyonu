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

        public HomeController(ISurveyService surveyService, ISurveyResponseService responseService)
        {
            _surveyService = surveyService;
            _responseService = responseService;
        }

        public async Task<IActionResult> Index()
        {
            var userRole = User.FindFirstValue(ClaimTypes.Role);
            var isAuth = User.Identity?.IsAuthenticated == true;
            var hasSuperClaim = User.HasAnketPermission(AnketPermissions.SuperAdmin);

            var allSummaries = await _surveyService.GetAllSurveySummariesAsync();

            // Çoklu ANKET_*: Admin + İdari + Akademik vb. birlikte — geniş katalog / birim filtresi birleşik uygulanır
            IEnumerable<SurveySummaryDto> filteredSurveys;
            if (isAuth && User.HasStaffSurveyExtendedCatalogAccess(userRole))
            {
                filteredSurveys = allSummaries;
            }
            else
            {
                filteredSurveys = allSummaries.Where(s => s.Status == SurveyStatus.Active);
            }

            var surveys = filteredSurveys.AsEnumerable();

            if (isAuth)
            {
                if (User.HasSuperAdminAccess())
                {
                    // SuperAdmin her şeyi görür
                }
                else if (User.HasAnyStaffSurveyPermission() || AnketPermissionClaims.IsStaffSurveyRole(userRole))
                {
                    // Personel izinlerinden herhangi biri veya Admin/Akademik/Employee rolü: birim + MERKEZ + aktif
                    surveys = surveys.Where(s =>
                        s.Status == SurveyStatus.Active ||
                        string.Equals(s.CreatedByBirim, "MERKEZ", StringComparison.OrdinalIgnoreCase) ||
                        SurveyUnitMatchHelper.MatchesSurveySummary(User, s));
                }
                else
                {
                    // Öğrenci vb.: Fill’de detaylı kontrol; listede yalnızca aktif set (yukarıda)
                }
            }
            else
            {
                // Giriş yapmamış kullanıcı da tüm AKTİF anketleri görür (ama doldurmak için login gerekecek)
            }

            var vm = new SurveyIndexViewModel
            {
                UserFullName = User.FindFirstValue(ClaimTypes.Name),
                UserRole = User.FindFirstValue(ClaimTypes.Role),
                IsLoggedIn = User.Identity?.IsAuthenticated == true,
                CanUseStaffSurveyTools = User.HasSuperAdminAccess()
                    || User.HasAnketPermission(AnketPermissions.Admin)
                    || userRole == "SuperAdmin" || userRole == "Admin",
                PreferSuperAdminSurveyLinks = hasSuperClaim || userRole == "SuperAdmin",
                Surveys = surveys.Select(s => new SurveyListItemViewModel
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
                    QuestionCount = s.QuestionCount,
                    ResponseCount = s.ResponseCount,
                    CreatedByName = s.CreatedByName,
                    CreatedByBirim = s.CreatedByBirim ?? string.Empty,
                    CreatedAt = s.CreatedAt,
                    IsAnonymous = s.IsAnonymous,
                    TargetRoles = s.TargetRoles
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
            var userRole = User.FindFirstValue(ClaimTypes.Role);
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var survey = await _surveyService.GetSurveyWithQuestionsAsync(id);
            if (survey == null) return NotFound();

            // Birim eşleşmesi + Admin / Akademik / İdari izinlerinden herhangi biri (çoklu yetki birleşimi)
            bool unitMatches = SurveyUnitMatchHelper.MatchesSurveyBirimStrings(User, survey.CreatedByBirim, survey.TargetFaculties);

            bool canView = User.HasSuperAdminAccess()
                || survey.CreatedByUserId == userId
                || (unitMatches && User.HasAnySurveyResultsStaffCapability(userRole));

            if (!canView) return Unauthorized();

            var results = await _responseService.GetSurveyResultsAsync(id);
            ViewBag.SurveyId = id;
            return View("~/Views/Admin/Results.cshtml", results);
        }
    }
}
