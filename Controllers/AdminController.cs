using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Models.ViewModels;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
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

        // Çoklu yetki sistemi kaldırıldı (isteğe bağlı olarak sadeleştirildi)

        private async Task<bool> CheckOwnershipAsync(int surveyId)
        {
            var userRole = User.FindFirstValue(ClaimTypes.Role);
            if (userRole == "SuperAdmin") return true;

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var survey = await _surveyService.GetSurveyWithQuestionsAsync(surveyId);
            if (survey == null) return false;

            return survey.CreatedByUserId == userId;
        }

        [HttpGet]
        public async Task<IActionResult> Dashboard(string? birim = null)
        {
            var userRole = User.FindFirstValue(ClaimTypes.Role);
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0";
            
            // Kullanıcının yetkili olduğu tüm birimler (claim'den)
            var authorizedUnits = User.FindAll("AuthorizedUnits").Select(c => c.Value).Distinct().ToList();
            if (!authorizedUnits.Any())
            {
                var userBirim = User.FindFirstValue("PersonelBirim");
                if (!string.IsNullOrEmpty(userBirim)) authorizedUnits.Add(userBirim);
            }

            List<Survey> all;
            if (userRole == "SuperAdmin")
            {
                all = (await _surveyService.GetAllSurveysAsync()).ToList();
            }
            else
            {
                all = (await _surveyService.GetSurveysByCreatorAsync(userId)).ToList();
            }

            // Seçili birim filtresi varsa uygula
            var filtered = all;
            if (!string.IsNullOrEmpty(birim))
            {
                filtered = all.Where(s => string.Equals(s.CreatedByBirim, birim, StringComparison.CurrentCultureIgnoreCase)).ToList();
            }

            var vm = new AdminDashboardViewModel
            {
                TotalSurveys = filtered.Count,
                ActiveSurveys = filtered.Count(s => s.Status == SurveyStatus.Active),
                DraftSurveys = filtered.Count(s => s.Status == SurveyStatus.Draft),
                TotalResponses = filtered.Sum(s => s.Responses.Count),
                TotalUsers = 0,
                AuthorizedUnits = authorizedUnits,
                SelectedBirim = birim,
                RecentSurveys = filtered.OrderByDescending(s => s.CreatedAt).Take(50).Select(s => new SurveyListItemViewModel
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
            var model = new SurveyCreateViewModel();

            // Kullanıcının yetkili olduğu birimler (Claims'den)
            // Türkçe büyük/küçük harf duyarsız tekrar giderim (İ/i sorunu için)
            var trCulture = new CultureInfo("tr-TR");
            model.AuthorizedUnits = User.FindAll("AuthorizedUnits")
                .Select(c => c.Value)
                .GroupBy(x => x.ToUpper(trCulture))
                .Select(g => g.First())
                .ToList();

            // Eğer AuthorizedUnits boşsa PersonelBirim'i ekle
            if (!model.AuthorizedUnits.Any())
            {
                var birim = User.FindFirstValue("PersonelBirim");
                if (!string.IsNullOrEmpty(birim)) model.AuthorizedUnits.Add(birim);
            }

            ViewBag.AllBirimler = _birimService.GetAllNames();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateSurvey(SurveyCreateDto dto)
        {
            // Sadece başlık zorunlu — diğer alanlar opsiyonel
            if (string.IsNullOrWhiteSpace(dto.Title))
            {
                var errorModel = new SurveyCreateViewModel
                {
                    Title = dto.Title,
                    Description = dto.Description
                };
                var trCult = new CultureInfo("tr-TR");
                errorModel.AuthorizedUnits = User.FindAll("AuthorizedUnits")
                    .Select(c => c.Value)
                    .GroupBy(x => x.ToUpper(trCult))
                    .Select(g => g.First())
                    .ToList();
                if (!errorModel.AuthorizedUnits.Any())
                {
                    var ub = User.FindFirstValue("PersonelBirim");
                    if (!string.IsNullOrEmpty(ub)) errorModel.AuthorizedUnits.Add(ub);
                }
                errorModel.SelectedBirim = dto.CreatedByBirim;
                ViewBag.AllBirimler = _birimService.GetAllNames();
                TempData["Error"] = "Anket başlığı zorunludur.";
                return View(errorModel);
            }

            // Kullanıcı bilgisi claim'den okunur
            var createdById   = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0";
            var createdByName = User.FindFirstValue(ClaimTypes.Name) ?? "Bilinmiyor";

            // Admin formdan hangi birimi seçtiyse o kullanılır;
            // Seçilmediyse PersonelBirim claim'i, o da yoksa MERKEZ
            var createdByBirim = !string.IsNullOrEmpty(dto.CreatedByBirim)
                ? dto.CreatedByBirim
                : (User.FindFirstValue("PersonelBirim") ?? "MERKEZ");

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
                SelectedBirim = survey.CreatedByBirim
            };

            ViewBag.AllBirimler = _birimService.GetAllNames();
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
        public async Task<IActionResult> Results(int id, string? fakulte = null, string? bolum = null)
        {
            if (!await CheckOwnershipAsync(id)) return Unauthorized();

            var results = await _responseService.GetSurveyResultsAsync(id, fakulte, bolum);
            
            // Filtre dropdownları için listeleri hazırla
            // Tüm fakülteleri BirimService'den alıyoruz
            ViewBag.Fakulteler = _birimService.GetAllNames();
            
            // Bölümleri ise bu ankete gelen cevaplardan (tüm cevaplardan) çekiyoruz ki filtre anlamlı olsun
            var allResultsForUnits = await _responseService.GetSurveyResultsAsync(id); 
            ViewBag.Bolumler = allResultsForUnits.Respondents
                .Where(r => !string.IsNullOrEmpty(r.BolumAdi))
                .Select(r => r.BolumAdi)
                .Distinct()
                .OrderBy(b => b)
                .ToList();

            ViewBag.SelectedFakulte = fakulte;
            ViewBag.SelectedBolum = bolum;

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