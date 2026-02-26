using AnketOtomasyonu.Authorization;
using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Models.ViewModels;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnketOtomasyonu.Controllers
{
    //[Authorize(Policy = "AnketAdmin")]
    public class SurveyController : Controller
    {
        private readonly ISurveyService _surveyService;
        private readonly IAuthServiceHandler _authHandler;

        public SurveyController(ISurveyService surveyService, IAuthServiceHandler authHandler)
        {
            _surveyService = surveyService;
            _authHandler = authHandler;
        }

        // Anket Listesi — giriş yapan adminin kendi anketleri
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var currentUser = await _authHandler.GetCurrentUser();
            if (currentUser == null)
                return RedirectToAction("Login", "Auth");

            var surveys = await _surveyService.GetSurveysByCreatorAsync(currentUser.Id.ToString());
            return View(surveys);
        }

        // Anket Detayı
        [HttpGet]
        public async Task<IActionResult> Detail(int id)
        {
            var survey = await _surveyService.GetSurveyWithQuestionsAsync(id);
            if (survey == null) return NotFound();

            var vm = new SurveyDetailViewModel
            {
                Id = survey.Id,
                Title = survey.Title,
                Description = survey.Description,
                Status = survey.Status switch
                {
                    SurveyStatus.Active => "Aktif",
                    SurveyStatus.Draft => "Taslak",
                    SurveyStatus.Inactive => "Pasif",
                    SurveyStatus.Closed => "Kapalı",
                    _ => "Bilinmiyor"
                },
                StatusBadgeClass = survey.Status switch
                {
                    SurveyStatus.Active => "bg-success",
                    SurveyStatus.Draft => "bg-warning text-dark",
                    SurveyStatus.Inactive => "bg-secondary",
                    _ => "bg-danger"
                },
                // ✅ CreatedByUser yok — CreatedByName string olarak survey'de saklı
                CreatedByName = survey.CreatedByName,
                CreatedAt = survey.CreatedAt,
                StartDate = survey.StartDate,
                EndDate = survey.EndDate,
                IsAnonymous = survey.IsAnonymous,
                TargetRoles = survey.TargetRoles,
                ResponseCount = survey.Responses?.Count ?? 0,
                Questions = survey.Questions
                    .OrderBy(q => q.OrderIndex)
                    .Select(q => new QuestionDetailViewModel
                    {
                        Id = q.Id,
                        Text = q.Text,
                        Type = q.Type,
                        TypeName = q.Type switch
                        {
                            QuestionType.Likert => "Likert",
                            QuestionType.MultipleChoice => "Çoktan Seçmeli",
                            QuestionType.OpenEnded => "Açık Uçlu",
                            _ => "Bilinmiyor"
                        },
                        IsRequired = q.IsRequired,
                        OrderIndex = q.OrderIndex,
                        Options = q.Options
                            .OrderBy(o => o.OrderIndex)
                            .Select(o => new OptionDetailViewModel
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

        // Anket Yayınlama
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Publish(int id)
        {
            await _surveyService.PublishSurveyAsync(id);
            TempData["Success"] = "Anket yayınlandı!";
            return RedirectToAction("Detail", new { id });
        }
    }
}