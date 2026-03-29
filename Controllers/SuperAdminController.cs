using AnketOtomasyonu.Data;
using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Models.ViewModels;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AnketOtomasyonu.Controllers
{
    [Authorize(Policy = "ANKET_API_SUPERADMIN")]
    public class SuperAdminController : Controller
    {
        private readonly ISurveyService _surveyService;
        private readonly ISurveyResponseService _responseService;
        private readonly ApplicationDbContext _db;
        private readonly IBirimService _birimService;

        public SuperAdminController(
            ISurveyService surveyService,
            ISurveyResponseService responseService,
            ApplicationDbContext db,
            IBirimService birimService)
        {
            _surveyService = surveyService;
            _responseService = responseService;
            _db = db;
            _birimService = birimService;
        }

        // ─── DASHBOARD ──────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Dashboard(string? birim = null)
        {
            var allSurveys = (await _surveyService.GetAllSurveysAsync()).ToList();

            // Birim listesi artık appsettings.json'dan (BirimDondur servisi) geliyor
            var allBirimler = _birimService.GetAllNames();

            // Filtre
            var filtered = string.IsNullOrEmpty(birim)
                ? allSurveys
                : allSurveys.Where(s => s.CreatedByBirim == birim).ToList();

            var vm = new SuperAdminDashboardViewModel
            {
                TotalSurveys = filtered.Count,
                ActiveSurveys = filtered.Count(s => s.Status == SurveyStatus.Active),
                DraftSurveys = filtered.Count(s => s.Status == SurveyStatus.Draft),
                PendingApprovalCount = filtered.Count(s => s.ApprovalStatus == ApprovalStatus.Pending
                                                        && s.Status == SurveyStatus.Draft),
                TotalResponses = filtered.Sum(s => s.Responses.Count),
                TotalAdminCount = await _db.AdminPermissions.CountAsync(),
                SelectedBirim = birim,
                AllBirimler = allBirimler,
                Surveys = filtered.Select(s => MapToListItem(s)).ToList()
            };

            return View(vm);
        }

        // ─── ANKET ONAYLAMA ─────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> SurveyApprovals(string? birim = null)
        {
            var allSurveys = (await _surveyService.GetAllSurveysAsync()).ToList();

            var allBirimler = _birimService.GetAllNames();

            var pending = allSurveys
                .Where(s => s.ApprovalStatus == ApprovalStatus.Pending)
                .ToList();

            if (!string.IsNullOrEmpty(birim))
                pending = pending.Where(s => s.CreatedByBirim == birim).ToList();

            var vm = new SuperAdminDashboardViewModel
            {
                SelectedBirim = birim,
                AllBirimler = allBirimler,
                Surveys = pending.Select(s => MapToListItem(s)).ToList(),
                PendingApprovalCount = pending.Count
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveSurvey(int id, string? note)
        {
            var survey = await _db.Surveys.FindAsync(id);
            if (survey == null) return NotFound();

            survey.ApprovalStatus = ApprovalStatus.Approved;
            survey.ApprovalNote = note;
            survey.ApprovedAt = DateTime.UtcNow;
            // Onaylandığında otomatik yayınla (Status = Active)
            survey.Status = SurveyStatus.Active;
            survey.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            TempData["Success"] = $"'{survey.Title}' anketi onaylandı ve yayınlandı!";
            return RedirectToAction("SurveyApprovals");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectSurvey(int id, string? note)
        {
            var survey = await _db.Surveys.FindAsync(id);
            if (survey == null) return NotFound();
 
            survey.ApprovalStatus = ApprovalStatus.Rejected;
            survey.ApprovalNote = note;
            survey.ApprovedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
 
            TempData["Error"] = $"'{survey.Title}' anketi reddedildi.";
            return RedirectToAction("SurveyApprovals");
        }
 
        [HttpGet]
        public async Task<IActionResult> PreviewSurvey(int id)
        {
            var survey = await _surveyService.GetSurveyWithQuestionsAsync(id);
            if (survey == null) return NotFound();
 
            return View(survey);
        }

        // ─── SONUÇLAR ───────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> AllResults(string? birim = null)
        {
            var allSurveys = (await _surveyService.GetAllSurveysAsync()).ToList();

            var allBirimler = _birimService.GetAllNames();

            var filtered = (string.IsNullOrEmpty(birim)
                ? allSurveys
                : allSurveys.Where(s => s.CreatedByBirim == birim).ToList())
                .Where(s => s.Responses.Count > 0)
                .ToList();

            var vm = new SuperAdminResultsViewModel
            {
                SelectedBirim = birim,
                AllBirimler = allBirimler,
                Surveys = filtered.Select(s => MapToListItem(s)).ToList()
            };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> Results(int id)
        {
            var results = await _responseService.GetSurveyResultsAsync(id);
            ViewBag.SurveyId = id;
            return View(results);
        }

        // ─── ADMİN YÖNETİMİ ────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> AdminManagement(string? filterBirim = null)
        {
            var admins = await _db.AdminPermissions
                .OrderBy(a => a.PersonelBirim)
                .ThenBy(a => a.Username)
                .ToListAsync();

            // Tüm birimler artık appsettings.json'dan
            var allBirimler = _birimService.GetAllNames();

            if (!string.IsNullOrEmpty(filterBirim))
                admins = admins.Where(a => a.PersonelBirim == filterBirim).ToList();

            var vm = new AdminManagementViewModel
            {
                Admins = admins.Select(a => new AdminPermissionViewModel
                {
                    Id = a.Id,
                    Username = a.Username,
                    PersonelBirim = a.PersonelBirim,
                    Note = a.Note,
                    CreatedAt = a.CreatedAt
                }).ToList(),
                AllBirimler = allBirimler,
                FilterBirim = filterBirim
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddAdmin(string username, string personelBirim, string? note)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(personelBirim))
            {
                TempData["Error"] = "Kullanıcı adı ve birim zorunludur.";
                return RedirectToAction("AdminManagement");
            }

            username = username.Trim().ToLower();
            personelBirim = personelBirim.Trim().ToUpper();

            // Birim listesinde var mı kontrol et
            var birimId = _birimService.GetIdByName(personelBirim);
            if (birimId == null)
            {
                TempData["Error"] = $"'{personelBirim}' geçerli bir birim adı değil. Lütfen listeden seçiniz.";
                return RedirectToAction("AdminManagement");
            }

            bool exists = await _db.AdminPermissions
                .AnyAsync(p => p.Username.ToLower() == username && p.PersonelBirim.ToUpper() == personelBirim);

            if (exists)
            {
                TempData["Error"] = $"'{username}' kullanıcısı zaten '{personelBirim}' birimi için admin yetkisine sahip.";
                return RedirectToAction("AdminManagement");
            }

            _db.AdminPermissions.Add(new AdminPermission
            {
                Username = username,
                PersonelBirim = personelBirim,
                Note = note?.Trim(),
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            TempData["Success"] = $"'{username}' kullanıcısına '{personelBirim}' birimi için admin yetkisi verildi.";
            return RedirectToAction("AdminManagement");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveAdmin(int id)
        {
            var perm = await _db.AdminPermissions.FindAsync(id);
            if (perm == null)
            {
                TempData["Error"] = "Kayıt bulunamadı.";
                return RedirectToAction("AdminManagement");
            }

            _db.AdminPermissions.Remove(perm);
            await _db.SaveChangesAsync();

            TempData["Success"] = $"'{perm.Username}' kullanıcısının admin yetkisi kaldırıldı.";
            return RedirectToAction("AdminManagement");
        }

        // ─── YARDIMCI ───────────────────────────────────────────────────────────
        private static SurveyListItemViewModel MapToListItem(Survey s) => new()
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
            CreatedByBirim = s.CreatedByBirim ?? "-",
            CreatedAt      = s.CreatedAt,
            ApprovalStatus = s.ApprovalStatus,
            ApprovalNote   = s.ApprovalNote
        };
    }
}
