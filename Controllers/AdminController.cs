using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Models.ViewModels;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AnketOtomasyonu.Controllers
{
    [Authorize(Policy = "ANKET_API_ADMIN")]
    public class AdminController : Controller
    {
        private readonly ISurveyService _surveyService;
        private readonly ISurveyResponseService _responseService;
        private readonly IBirimService _birimService;

        public AdminController(
            ISurveyService surveyService,
            ISurveyResponseService responseService,
            IBirimService birimService)
        {
            _surveyService = surveyService;
            _responseService = responseService;
            _birimService = birimService;
        }

        private async Task<bool> CheckOwnershipAsync(int surveyId)
        {
            var userRole = User.FindFirstValue(ClaimTypes.Role);
            if (userRole == "SuperAdmin") return true;

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var personelBirim = User.FindFirstValue("PersonelBirim");
            var survey = await _surveyService.GetSurveyWithQuestionsAsync(surveyId);
            if (survey == null) return false;

            // Admin kendi birimi ile eşleşen anketleri yönetebilir
            if (!string.IsNullOrEmpty(personelBirim) && survey.CreatedByBirim == personelBirim)
                return true;

            // Geriye uyumluluk: CreatedByBirim olmayan eski anketlerde userId kontrolü
            return survey.CreatedByUserId == userId;
        }

        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            var userRole = User.FindFirstValue(ClaimTypes.Role);
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0";
            var personelBirim = User.FindFirstValue("PersonelBirim");

            List<Survey> all;
            if (userRole == "SuperAdmin")
            {
                all = (await _surveyService.GetAllSurveysAsync()).ToList();
            }
            else if (!string.IsNullOrEmpty(personelBirim))
            {
                // Normal Admin: birime göre filtrele + kendi oluşturduklarını da ekle
                var birimSurveys = (await _surveyService.GetSurveysByBirimAsync(personelBirim)).ToList();
                var ownSurveys = (await _surveyService.GetSurveysByCreatorAsync(userId)).ToList();
                all = birimSurveys.UnionBy(ownSurveys, s => s.Id).OrderByDescending(s => s.CreatedAt).ToList();
            }
            else
            {
                all = (await _surveyService.GetSurveysByCreatorAsync(userId)).ToList();
            }

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
                    IsAnonymous = s.IsAnonymous,
                    Status = s.Status switch
                    {
                        SurveyStatus.Active   => "Aktif",
                        SurveyStatus.Draft    => "Taslak",
                        SurveyStatus.Inactive => "Pasif",
                        SurveyStatus.Closed   => "Kapalı",
                        _ => "Bilinmiyor"
                    },
                    StatusBadgeClass = s.Status switch
                    {
                        SurveyStatus.Active   => "bg-success",
                        SurveyStatus.Draft    => "bg-warning text-dark",
                        SurveyStatus.Inactive => "bg-secondary",
                        _ => "bg-danger"
                    },
                    QuestionCount  = s.Questions.Count,
                    ResponseCount  = s.Responses.Count,
                    CreatedByName  = s.CreatedByName,
                    CreatedByBirim = s.CreatedByBirim ?? "",
                    CreatedAt      = s.CreatedAt,
                    ApprovalStatus = s.ApprovalStatus,
                    ApprovalNote   = s.ApprovalNote
                }).ToList()
            };

            return View(vm);
        }

        [HttpGet]
        public IActionResult CreateSurvey()
        {
            ViewBag.Birimler = _birimService.GetAllNames();
            ViewBag.AdminBirim = User.FindFirstValue("PersonelBirim");
            return View(new SurveyCreateViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
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

            // Kullanıcı bilgisi claim'den okunur
            var createdById   = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0";
            var createdByName = User.FindFirstValue(ClaimTypes.Name) ?? "Bilinmiyor";
            var createdByBirim = User.FindFirstValue("PersonelBirim");

            await _surveyService.CreateSurveyAsync(dto, createdById, createdByName, createdByBirim);

            TempData["Success"] = "Anket başarıyla oluşturuldu. Yayınlamak için Yayınla butonuna tıklayın.";
            return RedirectToAction("Dashboard");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Publish(int id)
        {
            if (!await CheckOwnershipAsync(id)) return Unauthorized();

            var userRole = User.FindFirstValue(ClaimTypes.Role);
            var survey = await _surveyService.GetSurveyWithQuestionsAsync(id);

            // SuperAdmin doğrudan yayınlayabilir; Admin için onay şart
            if (userRole != "SuperAdmin" && survey?.ApprovalStatus != Models.Entities.ApprovalStatus.Approved)
            {
                TempData["Error"] = "Bu anket henüz SuperAdmin tarafından onaylanmadı. Onaylanmadan yayınlanamaz.";
                return RedirectToAction("Dashboard");
            }

            await _surveyService.PublishSurveyAsync(id);
            TempData["Success"] = "Anket yayınlandı! Artık öğrenciler görebilir.";
            return RedirectToAction("Dashboard");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Close(int id)
        {
            if (!await CheckOwnershipAsync(id)) return Unauthorized();

            await _surveyService.CloseSurveyAsync(id);
            TempData["Success"] = "Anket kapatıldı.";
            return RedirectToAction("Dashboard");
        }

        [HttpGet]
        public async Task<IActionResult> EditSurvey(int id)
        {
            if (!await CheckOwnershipAsync(id)) return Unauthorized();

            var survey = await _surveyService.GetSurveyWithQuestionsAsync(id);
            if (survey == null)
            {
                TempData["Error"] = "Anket bulunamadı.";
                return RedirectToAction("Dashboard");
            }

            var vm = new SurveyCreateViewModel
            {
                Title = survey.Title,
                Description = survey.Description,
                IsAnonymous = survey.IsAnonymous,
                StartDate = survey.StartDate,
                EndDate = survey.EndDate,
            };

            ViewBag.Birimler = _birimService.GetAllNames();
            ViewBag.AdminBirim = User.FindFirstValue("PersonelBirim");
            ViewBag.SurveyId = survey.Id;
            ViewBag.SurveyStatus = survey.Status;
            ViewBag.ExistingQuestions = survey.Questions
                .OrderBy(q => q.OrderIndex)
                .Select(q => new {
                    text = q.Text,
                    type = (int)q.Type,
                    isRequired = q.IsRequired,
                    options = q.Options
                        .OrderBy(o => o.OrderIndex)
                        .Select(o => new { text = o.Text.Contains(") ") ? o.Text.Substring(o.Text.IndexOf(") ") + 2) : o.Text })
                        .ToList()
                }).ToList();

            // TargetRoles'u ayarla
            if (!string.IsNullOrEmpty(survey.TargetRoles))
            {
                ViewBag.SelectedRoles = survey.TargetRoles.Split(',').ToList();
            }

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditSurvey(int id, SurveyCreateDto dto)
        {
            if (!await CheckOwnershipAsync(id)) return Unauthorized();

            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Lütfen zorunlu alanları doldurunuz.";
                return RedirectToAction("EditSurvey", new { id });
            }

            await _surveyService.UpdateSurveyAsync(id, dto);
            TempData["Success"] = "Anket başarıyla güncellendi.";
            return RedirectToAction("Dashboard");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Republish(int id)
        {
            if (!await CheckOwnershipAsync(id)) return Unauthorized();

            var userRole = User.FindFirstValue(ClaimTypes.Role);
            var survey = await _surveyService.GetSurveyWithQuestionsAsync(id);

            if (userRole != "SuperAdmin" && survey?.ApprovalStatus != Models.Entities.ApprovalStatus.Approved)
            {
                TempData["Error"] = "Bu anket henüz SuperAdmin tarafından onaylanmadı.";
                return RedirectToAction("Dashboard");
            }

            await _surveyService.PublishSurveyAsync(id);
            TempData["Success"] = "Anket tekrar yayınlandı!";
            return RedirectToAction("Dashboard");
        }

        [HttpGet]
        public async Task<IActionResult> PreviewSurvey(int id)
        {
            if (!await CheckOwnershipAsync(id)) return Unauthorized();

            var survey = await _surveyService.GetSurveyWithQuestionsAsync(id);
            if (survey == null) return NotFound();

            return View(survey);
        }

        [HttpGet]
        public async Task<IActionResult> Results(int id)
        {
            if (!await CheckOwnershipAsync(id)) return Unauthorized();

            var results = await _responseService.GetSurveyResultsAsync(id);
            return View(results);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            if (!await CheckOwnershipAsync(id)) return Unauthorized();

            await _surveyService.DeleteSurveyAsync(id);
            TempData["Success"] = "Anket silindi.";
            return RedirectToAction("Dashboard");
        }
    }
}