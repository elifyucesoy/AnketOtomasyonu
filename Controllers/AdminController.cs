using AnketOtomasyonu.Authorization;
using AnketOtomasyonu.Data;
using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Models.ViewModels;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        private readonly IUnitApiService _unitApiService;
        private readonly ApplicationDbContext _db;

        public AdminController(
            ISurveyService surveyService,
            ISurveyResponseService responseService,
            IBirimService birimService,
            IUnitApiService unitApiService,
            ApplicationDbContext db)
        {
            _surveyService = surveyService;
            _responseService = responseService;
            _birimService = birimService;
            _unitApiService = unitApiService;
            _db = db;
        }

        // Çoklu yetki sistemi kaldırıldı (isteğe bağlı olarak sadeleştirildi)

        private async Task<bool> CheckOwnershipAsync(int surveyId)
        {
            if (User.HasSuperAdminAccess()) return true;

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var ownerId = await _db.Surveys.AsNoTracking()
                .Where(s => s.Id == surveyId)
                .Select(s => s.CreatedByUserId)
                .FirstOrDefaultAsync();
            return ownerId != null && ownerId == userId;
        }

        /// <summary>Sil / yayın / kapat sonrası: süper admin SuperAdmin panosuna, diğerleri Admin panosuna.</summary>
        private IActionResult RedirectToManagingDashboard()
        {
            if (User.HasSuperAdminAccess())
                return RedirectToAction("Dashboard", "SuperAdmin");
            return RedirectToAction("Dashboard");
        }

        /// <summary>
        /// «Tüm birimler» (<c>__ALL__</c>) POST’ta sunucuda genişletilir: SuperAdmin için <c>null</c> (CachedUnits),
        /// normal Admin için yetkili birim adları.
        /// </summary>
        private IReadOnlyList<string>? GetExpandAllTargetScopeForCurrentUser()
        {
            if (User.HasSuperAdminAccess())
                return null;

            var trCulture = new CultureInfo("tr-TR");
            var units = User.FindAll("AuthorizedUnits")
                .Select(c => c.Value)
                .GroupBy(x => x.ToUpper(trCulture))
                .Select(g => g.First())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
            if (units.Count == 0)
            {
                var birim = User.FindFirstValue("PersonelBirim");
                if (!string.IsNullOrEmpty(birim)) units.Add(birim);
            }
            return units;
        }

        /// <summary>CreateSurvey / EditSurvey formları için ortak birim listesi ve yetkili birimler.</summary>
        private async Task HydrateSurveyCreateFormAsync(SurveyCreateViewModel model)
        {
            var trCulture = new CultureInfo("tr-TR");
            var isSuperAdmin = User.HasSuperAdminAccess();

            ViewBag.AllBirimler = _birimService.GetAllNames();

            var dbUnits = await _db.CachedUnits
                .Where(u => u.IsActive && !string.IsNullOrEmpty(u.Name))
                .OrderBy(u => u.Name)
                .ToListAsync();

            if (dbUnits.Count == 0)
            {
                var apiUnits = await _unitApiService.GetAllUnitsAsync();
                dbUnits = apiUnits
                    .Where(u => u.IsActive && !string.IsNullOrWhiteSpace(u.Name))
                    .Select(u => new Models.Entities.CachedUnit
                    {
                        Id = u.Id, Name = u.Name, ParentId = u.ParentId,
                        UnitTypeId = u.UnitTypeId, UnitTypeName = u.UnitTypeName,
                        IsActive = u.IsActive, LastSyncedAt = DateTime.UtcNow
                    })
                    .OrderBy(u => u.Name)
                    .ToList();
            }

            if (dbUnits.Count == 0 && isSuperAdmin)
            {
                dbUnits = _birimService.GetAll()
                    .Select(b => new Models.Entities.CachedUnit
                    {
                        Id = b.Id,
                        Name = b.Name,
                        ParentId = null,
                        UnitTypeId = null,
                        UnitTypeName = null,
                        IsActive = true,
                        LastSyncedAt = DateTime.UtcNow
                    })
                    .OrderBy(u => u.Name)
                    .ToList();
            }

            ViewBag.UnitList = dbUnits;

            if (isSuperAdmin)
            {
                model.AuthorizedUnits = dbUnits.Select(u => u.Name).ToList();
            }
            else
            {
                model.AuthorizedUnits = User.FindAll("AuthorizedUnits")
                    .Select(c => c.Value)
                    .GroupBy(x => x.ToUpper(trCulture))
                    .Select(g => g.First())
                    .ToList();

                if (!model.AuthorizedUnits.Any())
                {
                    var birim = User.FindFirstValue("PersonelBirim");
                    if (!string.IsNullOrEmpty(birim)) model.AuthorizedUnits.Add(birim);
                }
            }
        }

        [HttpGet]
        public async Task<IActionResult> Dashboard(string? birim = null)
        {
            if (User.HasSuperAdminAccess())
                return RedirectToAction("Dashboard", "SuperAdmin");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0";
            
            // Kullanıcının yetkili olduğu tüm birimler (claim'den)
            var authorizedUnits = User.FindAll("AuthorizedUnits").Select(c => c.Value).Distinct().ToList();
            if (!authorizedUnits.Any())
            {
                var userBirim = User.FindFirstValue("PersonelBirim");
                if (!string.IsNullOrEmpty(userBirim)) authorizedUnits.Add(userBirim);
            }

            var all = (await _surveyService.GetSurveySummariesByCreatorAsync(userId)).ToList();

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
                TotalResponses = filtered.Sum(s => s.ResponseCount),
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
                    QuestionCount  = s.QuestionCount,
                    ResponseCount  = s.ResponseCount,
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
        public async Task<IActionResult> CreateSurvey()
        {
            var model = new SurveyCreateViewModel();
            await HydrateSurveyCreateFormAsync(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateSurvey(SurveyCreateDto dto)
        {
            var trCult = new CultureInfo("tr-TR");

            // Sadece başlık zorunlu — diğer alanlar opsiyonel
            if (string.IsNullOrWhiteSpace(dto.Title))
            {
                ViewBag.AllBirimler = _birimService.GetAllNames();
                var errDbUnits = await _db.CachedUnits
                    .Where(u => u.IsActive && !string.IsNullOrEmpty(u.Name))
                    .OrderBy(u => u.Name).ToListAsync();
                if (errDbUnits.Count == 0)
                {
                    var apiU = await _unitApiService.GetAllUnitsAsync();
                    errDbUnits = apiU.Where(u => u.IsActive)
                        .Select(u => new Models.Entities.CachedUnit { Id = u.Id, Name = u.Name })
                        .OrderBy(u => u.Name).ToList();
                }
                if (errDbUnits.Count == 0 && User.HasSuperAdminAccess())
                {
                    errDbUnits = _birimService.GetAll()
                        .Select(b => new Models.Entities.CachedUnit
                        {
                            Id = b.Id, Name = b.Name, IsActive = true,
                            LastSyncedAt = DateTime.UtcNow
                        })
                        .OrderBy(u => u.Name).ToList();
                }
                ViewBag.UnitList = errDbUnits;
                var errorModel = new SurveyCreateViewModel
                {
                    Title = dto.Title, Description = dto.Description,
                    SelectedBirim = dto.CreatedByBirim,
                    AuthorizedUnits = User.HasSuperAdminAccess()
                        ? errDbUnits.Select(u => u.Name).ToList()
                        : User.FindAll("AuthorizedUnits").Select(c => c.Value)
                            .GroupBy(x => x.ToUpper(trCult)).Select(g => g.First()).ToList()
                };
                if (!errorModel.AuthorizedUnits.Any())
                {
                    var ub = User.FindFirstValue("PersonelBirim");
                    if (!string.IsNullOrEmpty(ub)) errorModel.AuthorizedUnits.Add(ub);
                }
                TempData["Error"] = "Anket başlığı zorunludur.";
                return View(errorModel);
            }

            // Kullanıcı bilgisi claim'den okunur
            var createdById   = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0";
            var createdByName = User.FindFirstValue(ClaimTypes.Name) ?? "Bilinmiyor";

            // UnitName: dto'dan geliyorsa kullan, yoksa DB'den çek
            if (dto.UnitId.HasValue && string.IsNullOrWhiteSpace(dto.UnitName))
            {
                var dbUnit = await _db.CachedUnits.FindAsync(dto.UnitId.Value);
                if (dbUnit != null) dto.UnitName = dbUnit.Name;
            }

            // Admin formdan hangi birimi seçtiyse o kullanılır;
            // Seçilmediyse PersonelBirim claim'i, o da yoksa MERKEZ
            var createdByBirim = !string.IsNullOrEmpty(dto.CreatedByBirim)
                ? dto.CreatedByBirim
                : (User.FindFirstValue("PersonelBirim") ?? "MERKEZ");

            var isSuperAdmin = User.HasSuperAdminAccess();
            var expandScope = GetExpandAllTargetScopeForCurrentUser();
            await _surveyService.CreateSurveyAsync(dto, createdById, createdByName, createdByBirim, isSuperAdmin, expandScope);

            TempData["Success"] = "Anket başarıyla oluşturuldu. Yayınlamak için Yayınla butonuna tıklayın.";
            return RedirectToManagingDashboard();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Publish(int id)
        {
            if (!await CheckOwnershipAsync(id)) return Unauthorized();

            var survey = await _surveyService.GetSurveyWithQuestionsAsync(id);

            // SuperAdmin doğrudan yayınlayabilir; Admin için onay şart
            if (!User.HasSuperAdminAccess() && survey?.ApprovalStatus != Models.Entities.ApprovalStatus.Approved)
            {
                TempData["Error"] = "Bu anket henüz SuperAdmin tarafından onaylanmadı. Onaylanmadan yayınlanamaz.";
                return RedirectToManagingDashboard();
            }

            await _surveyService.PublishSurveyAsync(id);
            TempData["Success"] = "Anket yayınlandı! Artık öğrenciler görebilir.";
            return RedirectToManagingDashboard();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Close(int id)
        {
            if (!await CheckOwnershipAsync(id)) return Unauthorized();

            await _surveyService.CloseSurveyAsync(id);
            TempData["Success"] = "Anket kapatıldı.";
            return RedirectToManagingDashboard();
        }

        [HttpGet]
        public async Task<IActionResult> EditSurvey(int id)
        {
            if (!await CheckOwnershipAsync(id)) return Unauthorized();

            var survey = await _surveyService.GetSurveyForEditAsync(id);
            if (survey == null)
            {
                TempData["Error"] = "Anket bulunamadı.";
                return RedirectToManagingDashboard();
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

            await HydrateSurveyCreateFormAsync(vm);

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

            if (!string.IsNullOrEmpty(survey.TargetRoles))
            {
                ViewBag.SelectedRoles = survey.TargetRoles
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            }

            var selectedFaculties = survey.TargetUnits
                .OrderBy(t => t.Birim)
                .Select(t => t.Birim)
                .Distinct()
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .ToList();
            if (selectedFaculties.Count == 0 && !string.IsNullOrEmpty(survey.TargetFaculties))
            {
                selectedFaculties = survey.TargetFaculties
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            }
            if (selectedFaculties.Count == 0 && !string.IsNullOrWhiteSpace(survey.CreatedByBirim)
                && !string.Equals(survey.CreatedByBirim, "MERKEZ", StringComparison.OrdinalIgnoreCase))
            {
                selectedFaculties = new List<string> { survey.CreatedByBirim };
            }
            ViewBag.SelectedTargetFaculties = selectedFaculties;

            return View("CreateSurvey", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditSurvey(int id, SurveyCreateDto dto)
        {
            if (!await CheckOwnershipAsync(id)) return Unauthorized();

            // CreateSurvey ile aynı: ModelState / örtük soru doğrulamasına güvenme (yanlış pozitifleri önler).
            dto.Questions = dto.Questions?
                .Where(q => q != null && !string.IsNullOrWhiteSpace(q.Text))
                .ToList() ?? new List<QuestionCreateDto>();

            foreach (var q in dto.Questions)
            {
                if (q.Options == null) continue;
                q.Options = q.Options.Where(o => o != null && !string.IsNullOrWhiteSpace(o.Text)).ToList();
            }

            if (string.IsNullOrWhiteSpace(dto.Title))
            {
                TempData["Error"] = "Anket başlığı zorunludur.";
                return RedirectToAction("EditSurvey", new { id });
            }

            if (!dto.Questions.Any())
            {
                TempData["Error"] = "En az bir soru eklemelisiniz.";
                return RedirectToAction("EditSurvey", new { id });
            }

            var expandScope = GetExpandAllTargetScopeForCurrentUser();
            await _surveyService.UpdateSurveyAsync(id, dto, false, expandScope);
            TempData["Success"] = "Anket başarıyla güncellendi.";
            return RedirectToManagingDashboard();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Republish(int id)
        {
            if (!await CheckOwnershipAsync(id)) return Unauthorized();

            var survey = await _surveyService.GetSurveyWithQuestionsAsync(id);

            if (!User.HasSuperAdminAccess() && survey?.ApprovalStatus != Models.Entities.ApprovalStatus.Approved)
            {
                TempData["Error"] = "Bu anket henüz SuperAdmin tarafından onaylanmadı.";
                return RedirectToManagingDashboard();
            }

            await _surveyService.PublishSurveyAsync(id);
            TempData["Success"] = "Anket tekrar yayınlandı!";
            return RedirectToManagingDashboard();
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
            return RedirectToManagingDashboard();
        }
    }
}