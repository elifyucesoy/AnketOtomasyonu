using AnketOtomasyonu.Authorization;
using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Models.ViewModels;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnketOtomasyonu.Controllers
{
    [Authorize(Policy = "AnketAdmin")]
    public class AdminController : Controller
    {
        private readonly ISurveyService _surveyService;
        private readonly ISurveyResponseService _responseService;
        private readonly IAuthServiceHandler _authHandler;

        public AdminController(
            ISurveyService surveyService,
            ISurveyResponseService responseService,
            IAuthServiceHandler authHandler)
        {
            _surveyService = surveyService;
            _responseService = responseService;
            _authHandler = authHandler;
        }

        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            var all = (await _surveyService.GetAllSurveysAsync()).ToList();

            var vm = new AdminDashboardViewModel
            {
                TotalSurveys = all.Count,
                ActiveSurveys = all.Count(s => s.Status == SurveyStatus.Active),
                DraftSurveys = all.Count(s => s.Status == SurveyStatus.Draft),
                TotalResponses = all.Sum(s => s.Responses.Count),
                TotalUsers = 0,
                RecentSurveys = all.Take(20).Select(s => new SurveyListItemViewModel
                {
                    Id = s.Id,
                    Title = s.Title,
                    IsAnonymous =s.IsAnonymous,
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
                    CreatedAt = s.CreatedAt
                }).ToList()
            };

            return View(vm);
        }

        [HttpGet]
        public IActionResult CreateSurvey() => View(new SurveyCreateViewModel());

        [HttpPost]
        public async Task<IActionResult> CreateSurvey(SurveyCreateDto dto)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Lütfen zorunlu alanları doldurunuz.";
                return View(new SurveyCreateViewModel
                {
                    Title = dto.Title,
                    Description = dto.Description
                });
            }

            var user = await _authHandler.GetCurrentUser();
            if (user == null) return RedirectToAction("Login", "Auth");

            await _surveyService.CreateSurveyAsync(
                dto, user.Id.ToString(), $"{user.Name} {user.Surname}");

            TempData["Success"] = "Anket başarıyla oluşturuldu. Yayınlamak için Yayınla butonuna tıklayın.";
            return RedirectToAction("Dashboard");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Publish(int id)
        {
            await _surveyService.PublishSurveyAsync(id);
            TempData["Success"] = "Anket yayınlandı! Artık öğrenciler görebilir.";
            return RedirectToAction("Dashboard");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Close(int id)
        {
            await _surveyService.CloseSurveyAsync(id);
            TempData["Success"] = "Anket kapatıldı.";
            return RedirectToAction("Dashboard");
        }

        [HttpGet]
        public async Task<IActionResult> Results(int id)
        {
            var results = await _responseService.GetSurveyResultsAsync(id);
            return View(results);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            await _surveyService.DeleteSurveyAsync(id);
            TempData["Success"] = "Anket silindi.";
            return RedirectToAction("Dashboard");
        }
    }
}