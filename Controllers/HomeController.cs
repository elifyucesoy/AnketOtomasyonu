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

            IEnumerable<Survey> allSurveys = (await _surveyService.GetAllSurveysAsync()).ToList();
            
            IEnumerable<Survey> filteredSurveys;
            if (isAuth && (userRole == "SuperAdmin" || userRole == "Admin" || userRole == "Akademik"))
            {
                // Yetkili kullanıcılar her şeyi görebilir (Taslak dahil)
                filteredSurveys = allSurveys;
            }
            else
            {
                // Öğrenci, Personel ve Ziyaretçiler sadece AKTİF anketleri görür
                filteredSurveys = allSurveys.Where(s => s.Status == SurveyStatus.Active);
            }

            var surveys = filteredSurveys.AsEnumerable();

            if (isAuth)
            {
                if (userRole == "SuperAdmin")
                {
                    // SuperAdmin her şeyi görür
                }
                else if (userRole == "Admin")
                {
                    // Admin kendi biriminin anketlerini + MERKEZ anketlerini + kendisine hedeflemiş anketleri görür
                    var unit = User.FindFirstValue("PersonelBirim")?.Trim();
                    surveys = surveys.Where(s =>
                        s.Status == SurveyStatus.Active || // Aktif olan her şeyi görsün
                        string.Equals(s.CreatedByBirim, "MERKEZ", StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(unit) && string.Equals(s.CreatedByBirim, unit, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(s.TargetFaculties) && !string.IsNullOrEmpty(unit) &&
                         s.TargetFaculties.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                          .Contains(unit, StringComparer.OrdinalIgnoreCase))
                    );
                }
                else if (userRole == "Akademik")
                {
                    // Akademik personel: kendi biriminin anketlerini görür (read-only, dolduramaz)
                    var unit = User.FindFirstValue("PersonelBirim");
                    surveys = surveys.Where(s =>
                        s.Status == SurveyStatus.Active || // Aktif olan her şeyi görsün
                        (!string.IsNullOrEmpty(unit) && string.Equals(s.CreatedByBirim, unit, StringComparison.OrdinalIgnoreCase)) ||
                        string.Equals(s.CreatedByBirim, "MERKEZ", StringComparison.OrdinalIgnoreCase)
                    );
                }
                else 
                {
                    // Öğrenci ve Employee: Tüm AKTİF anketleri görür (filtreleme artık yok, hepsi listelenecek)
                    // (Fill aşamasında yetki kontrolü zaten yapılıyor)
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
                    QuestionCount = s.Questions.Count,
                    ResponseCount = s.Responses.Count,
                    CreatedByName = s.CreatedByName,
                    CreatedByBirim = s.CreatedByBirim,
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

        [Authorize(Policy = "ANKET_API_ADMIN_OR_ANKET_API_STUDENT")]
        [HttpGet]
        public async Task<IActionResult> Results(int id)
        {
            var userRole = User.FindFirstValue(ClaimTypes.Role);
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var personelBirim = User.FindFirstValue("PersonelBirim");

            var survey = await _surveyService.GetSurveyWithQuestionsAsync(id);
            if (survey == null) return NotFound();

            // Sadece kendi biriminin anketlerinin sonuçlarını görebilir,
            // (veya SuperAdmin hepsini görür, kendi oluşturduğuysa da görür)
            bool canView = false;
            if (userRole == "SuperAdmin") canView = true;
            else if (userRole == "Admin" && !string.IsNullOrEmpty(personelBirim) && string.Equals(survey.CreatedByBirim, personelBirim, StringComparison.OrdinalIgnoreCase)) canView = true;
            else if (userRole == "Akademik" && !string.IsNullOrEmpty(personelBirim) && string.Equals(survey.CreatedByBirim, personelBirim, StringComparison.OrdinalIgnoreCase)) canView = true;
            else if (survey.CreatedByUserId == userId) canView = true; // Oluşturan kişi

            if (!canView) return Unauthorized();

            var results = await _responseService.GetSurveyResultsAsync(id);
            ViewBag.SurveyId = id;
            return View("~/Views/Admin/Results.cshtml", results);
        }
    }
}
