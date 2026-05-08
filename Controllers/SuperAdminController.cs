using AnketOtomasyonu.Authorization;
using AnketOtomasyonu.Data;
using AnketOtomasyonu.Helpers;
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
    /// <summary>Tüm aksiyonlar <c>/SuperAdmin/{action}/{id?}</c> ile eşleşir.</summary>
    [Authorize(Policy = AnketPermissions.SuperAdmin)]
    [Route("SuperAdmin/[action]/{id?}")]
    public class SuperAdminController : Controller
    {
        private readonly ISurveyService _surveyService;
        private readonly ISurveyResponseService _responseService;
        private readonly ApplicationDbContext _db;
        private readonly IBirimService _birimService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SuperAdminController> _logger;
        private readonly IUnitApiService _unitApiService;

        public SuperAdminController(
            ISurveyService surveyService,
            ISurveyResponseService responseService,
            ApplicationDbContext db,
            IBirimService birimService,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<SuperAdminController> logger,
            IUnitApiService unitApiService)
        {
            _surveyService = surveyService;
            _responseService = responseService;
            _db = db;
            _birimService = birimService;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
            _unitApiService = unitApiService;
        }

        /// <summary>Anket oluşturma formu ile aynı kaynak: CachedUnits → API → appsettings Birimler.</summary>
        private async Task<List<string>> GetSurveyFormUnitNamesAsync()
        {
            var names = await _db.CachedUnits
                .AsNoTracking()
                .Where(u => u.IsActive && !string.IsNullOrEmpty(u.Name))
                .OrderBy(u => u.Name)
                .Select(u => u.Name)
                .ToListAsync();

            if (names.Count > 0)
                return names;

            var apiUnits = await _unitApiService.GetAllUnitsAsync();
            names = apiUnits
                .Where(u => u.IsActive && !string.IsNullOrWhiteSpace(u.Name))
                .Select(u => u.Name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (names.Count > 0)
                return names;

            return _birimService.GetAllNames();
        }

        // ─── BİRİM DURUM TANILAMA ──────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> UnitDiag()
        {
            int dbCount = 0;
            string dbError = "";
            string apiResult = "";

            try { dbCount = await _db.CachedUnits.CountAsync(); }
            catch (Exception ex) { dbError = ex.Message; }

            try
            {
                var (u, _) = await _unitApiService.ForceRefreshAsync(string.Empty);
                apiResult = u > 0 ? $"✅ API'den {u} birim alındı" : "❌ API 0 birim döndürdü";
            }
            catch (Exception ex) { apiResult = $"❌ API hatası: {ex.Message}"; }

            return Content(
                $"DB CachedUnits satır sayısı: {dbCount}\n" +
                $"DB hatası: {(string.IsNullOrEmpty(dbError) ? "yok" : dbError)}\n" +
                $"API test: {apiResult}",
                "text/plain; charset=utf-8");
        }

        // ─── BİRİM SENKRONİZASYONU (Manuel Tetik) ──────────────────────────────
        // Dashboard'daki "Birimleri Senkronize Et" butonundan çağrılır.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [ActionName("SyncUnitCatalog")]
        public async Task<IActionResult> SyncUnits()
        {
            try
            {
                // 1. API'den tüm birimleri çek (System User ile otomatik login)
                var (unitCount, _) = await _unitApiService.ForceRefreshAsync(string.Empty);
                if (unitCount == 0)
                {
                    TempData["Error"] = "❌ API'den birim listesi alınamadı. /SuperAdmin/UnitDiag adresine giderek detaylı hatayı görebilirsiniz.";
                    return RedirectToAction("Dashboard");
                }

                var units = await _unitApiService.GetAllUnitsAsync();
                var now = DateTime.UtcNow;
                var incoming = units.Select(u => new CachedUnit
                {
                    Id = u.Id, Name = u.Name, ParentId = u.ParentId,
                    UnitTypeId = u.UnitTypeId, UnitTypeName = u.UnitTypeName,
                    IsActive = u.IsActive, LastSyncedAt = now
                }).ToList();

                // 2. DB'ye upsert
                var existingIds = (await _db.CachedUnits.Select(x => x.Id).ToListAsync()).ToHashSet();
                var toAdd    = incoming.Where(u => !existingIds.Contains(u.Id)).ToList();
                var toUpdate = incoming.Where(u =>  existingIds.Contains(u.Id)).ToList();

                if (toAdd.Count > 0) await _db.CachedUnits.AddRangeAsync(toAdd);
                foreach (var upd in toUpdate) _db.CachedUnits.Update(upd);

                var staleIds = existingIds.Except(incoming.Select(u => u.Id)).ToList();
                if (staleIds.Count > 0)
                {
                    var toRemove = await _db.CachedUnits.Where(u => staleIds.Contains(u.Id)).ToListAsync();
                    _db.CachedUnits.RemoveRange(toRemove);
                }

                await _db.SaveChangesAsync();
                TempData["Success"] = $"✅ {incoming.Count} birim başarıyla senkronize edildi. Artık anket oluştururken birim seçebilirsiniz.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SyncUnits] Senkronizasyon hatası.");
                TempData["Error"] = $"❌ Hata: {ex.Message}";
            }
            return RedirectToAction("Dashboard");
        }

        // ─── ANKET OLUŞTURMA (otomatik onaylı taslak) ───────────────────────────
        // Birim yöneticisi: /Admin/CreateSurvey (onay bekler). SuperAdmin: /SuperAdmin/CreateSurvey.
        [HttpGet]
        public async Task<IActionResult> CreateSurvey()
        {
            var model = new SurveyCreateViewModel();
            await HydrateSuperAdminSurveyCreateFormAsync(model);
            ViewBag.SelectedRoles = new List<string> { "Student" };
            return View("~/Views/Admin/CreateSurvey.cshtml", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateSurvey(SurveyCreateDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Title))
            {
                var model = new SurveyCreateViewModel
                {
                    Title = dto.Title,
                    Description = dto.Description,
                    SelectedBirim = dto.CreatedByBirim
                };
                await HydrateSuperAdminSurveyCreateFormAsync(model);
                TempData["Error"] = "Anket başlığı zorunludur.";
                return View("~/Views/Admin/CreateSurvey.cshtml", model);
            }

            var createdById = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0";
            var createdByName = User.FindFirstValue(ClaimTypes.Name) ?? "Bilinmiyor";

            if (dto.UnitId.HasValue && string.IsNullOrWhiteSpace(dto.UnitName))
            {
                var dbUnit = await _db.CachedUnits.FindAsync(dto.UnitId.Value);
                if (dbUnit != null) dto.UnitName = dbUnit.Name;
            }

            var createdByBirim = !string.IsNullOrEmpty(dto.CreatedByBirim)
                ? dto.CreatedByBirim
                : (User.FindFirstValue("PersonelBirim") ?? "MERKEZ");

            dto.Questions = dto.Questions?
                .Where(q => q != null && !string.IsNullOrWhiteSpace(q.Text))
                .ToList() ?? new List<QuestionCreateDto>();
            foreach (var q in dto.Questions)
            {
                if (q.Options == null) continue;
                q.Options = q.Options.Where(o => o != null && !string.IsNullOrWhiteSpace(o.Text)).ToList();
            }

            await _surveyService.CreateSurveyAsync(
                dto, createdById, createdByName, createdByBirim, isSuperAdmin: true, expandAllTokenScope: null);

            TempData["Success"] = "Anket oluşturuldu. Taslak olarak kaydedildi; yayınlamak için listeden Yayınla butonunu kullanın.";
            return RedirectToAction(nameof(Dashboard));
        }

        private async Task HydrateSuperAdminSurveyCreateFormAsync(SurveyCreateViewModel model)
        {
            ViewBag.AllBirimler = _birimService.GetAllNames();
            ViewBag.FormController = "SuperAdmin";

            var dbUnits = await _db.CachedUnits
                .Where(u => u.IsActive && !string.IsNullOrEmpty(u.Name))
                .OrderBy(u => u.Name)
                .ToListAsync();

            if (dbUnits.Count == 0)
            {
                var apiUnits = await _unitApiService.GetAllUnitsAsync();
                dbUnits = apiUnits
                    .Where(u => u.IsActive && !string.IsNullOrWhiteSpace(u.Name))
                    .Select(u => new CachedUnit
                    {
                        Id = u.Id, Name = u.Name, ParentId = u.ParentId,
                        UnitTypeId = u.UnitTypeId, UnitTypeName = u.UnitTypeName,
                        IsActive = u.IsActive, LastSyncedAt = DateTime.UtcNow
                    })
                    .OrderBy(u => u.Name)
                    .ToList();
            }

            if (dbUnits.Count == 0)
            {
                dbUnits = _birimService.GetAll()
                    .Select(b => new CachedUnit
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
            model.AuthorizedUnits = dbUnits
                .Select(u => u.Name.Trim())
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        [HttpGet]
        public async Task<IActionResult> EditSurvey(int id)
        {
            var survey = await _surveyService.GetSurveyForEditAsync(id);
            if (survey == null)
            {
                TempData["Error"] = "Anket bulunamadı.";
                return RedirectToAction(nameof(Dashboard));
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

            await HydrateSuperAdminSurveyCreateFormAsync(vm);

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

            return View("~/Views/Admin/CreateSurvey.cshtml", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditSurvey(int id, SurveyCreateDto dto)
        {
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
                return RedirectToAction(nameof(EditSurvey), new { id });
            }

            if (!dto.Questions.Any())
            {
                TempData["Error"] = "En az bir soru eklemelisiniz.";
                return RedirectToAction(nameof(EditSurvey), new { id });
            }

            if (dto.UnitId.HasValue && string.IsNullOrWhiteSpace(dto.UnitName))
            {
                var dbUnit = await _db.CachedUnits.FindAsync(dto.UnitId.Value);
                if (dbUnit != null) dto.UnitName = dbUnit.Name;
            }

            if (!string.IsNullOrEmpty(dto.CreatedByBirim))
                dto.CreatedByBirim = dto.CreatedByBirim.Trim();

            await _surveyService.UpdateSurveyAsync(id, dto, resetToApproval: false, expandAllTokenScope: null);
            TempData["Success"] = "Anket başarıyla güncellendi.";
            return RedirectToAction(nameof(Dashboard));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Publish(int id)
        {
            var row = await _db.Surveys.AsNoTracking()
                .Where(s => s.Id == id)
                .Select(s => new { s.ApprovalStatus })
                .FirstOrDefaultAsync();

            if (row == null)
            {
                TempData["Error"] = "Anket bulunamadı.";
                return RedirectToAction(nameof(Dashboard));
            }

            if (row.ApprovalStatus != ApprovalStatus.Approved)
            {
                TempData["Error"] = "Bu anket henüz SuperAdmin tarafından onaylanmadı. Onaylanmadan yayınlanamaz.";
                return RedirectToAction(nameof(Dashboard));
            }

            await _surveyService.PublishSurveyAsync(id);
            TempData["Success"] = "Anket yayınlandı! Artık öğrenciler görebilir.";
            return RedirectToAction(nameof(Dashboard));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Close(int id)
        {
            if (!await _db.Surveys.AsNoTracking().AnyAsync(s => s.Id == id))
            {
                TempData["Error"] = "Anket bulunamadı.";
                return RedirectToAction(nameof(Dashboard));
            }

            await _surveyService.CloseSurveyAsync(id);
            TempData["Success"] = "Anket kapatıldı.";
            return RedirectToAction(nameof(Dashboard));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Republish(int id)
        {
            var row = await _db.Surveys.AsNoTracking()
                .Where(s => s.Id == id)
                .Select(s => new { s.ApprovalStatus })
                .FirstOrDefaultAsync();

            if (row == null)
            {
                TempData["Error"] = "Anket bulunamadı.";
                return RedirectToAction(nameof(Dashboard));
            }

            if (row.ApprovalStatus != ApprovalStatus.Approved)
            {
                TempData["Error"] = "Bu anket henüz SuperAdmin tarafından onaylanmadı.";
                return RedirectToAction(nameof(Dashboard));
            }

            await _surveyService.PublishSurveyAsync(id);
            TempData["Success"] = "Anket tekrar yayınlandı!";
            return RedirectToAction(nameof(Dashboard));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            if (!await _db.Surveys.AsNoTracking().AnyAsync(s => s.Id == id))
            {
                TempData["Error"] = "Anket bulunamadı.";
                return RedirectToAction(nameof(Dashboard));
            }

            await _surveyService.DeleteSurveyAsync(id);
            TempData["Success"] = "Anket silindi.";
            return RedirectToAction(nameof(Dashboard));
        }

        // ─── DASHBOARD ──────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Dashboard(
            string? birim = null,
            string? statusFilter = null,
            string? startDate = null,
            string? endDate = null,
            string dateSort = "newest",
            int page = 1)
        {
            var allSurveys = await _surveyService.GetAllSurveySummariesAsync();

            var allBirimler = await GetSurveyFormUnitNamesAsync();

            // Birim filtresi
            var turkish = new CultureInfo("tr-TR");
            var birimFiltered = string.IsNullOrEmpty(birim)
                ? allSurveys.ToList()
                : allSurveys
                    .Where(s => turkish.CompareInfo.Compare(s.CreatedByBirim, birim, CompareOptions.IgnoreCase) == 0)
                    .ToList();

            // Tarih aralığı filtresi
            DateTime? start = null, end = null;
            if (!string.IsNullOrEmpty(startDate) && DateTime.TryParseExact(startDate, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var sd))
                start = sd;
            if (!string.IsNullOrEmpty(endDate) && DateTime.TryParseExact(endDate, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var ed))
                end = ed.AddDays(1).AddSeconds(-1);

            if (start.HasValue)
                birimFiltered = birimFiltered.Where(s => s.CreatedAt >= start.Value).ToList();
            if (end.HasValue)
                birimFiltered = birimFiltered.Where(s => s.CreatedAt <= end.Value).ToList();

            // Durum filtresi (stat kartlarından)
            var statusFiltered = statusFilter switch
            {
                "active"   => birimFiltered.Where(s => s.Status == SurveyStatus.Active).ToList(),
                "draft"    => birimFiltered.Where(s => s.Status == SurveyStatus.Draft
                                                    && s.ApprovalStatus != ApprovalStatus.Pending).ToList(),
                "passive"  => birimFiltered.Where(s => s.Status == SurveyStatus.Inactive).ToList(),
                "pending"  => birimFiltered.Where(s => s.ApprovalStatus == ApprovalStatus.Pending).ToList(),
                "rejected" => birimFiltered.Where(s => s.ApprovalStatus == ApprovalStatus.Rejected).ToList(),
                _          => birimFiltered
            };

            // Sıralama
            var sortOldestFirst = string.Equals(dateSort, "oldest", StringComparison.OrdinalIgnoreCase);

            // Sayfalama
            const int pageSize = 25;
            if (page < 1) page = 1;
            var totalCount = statusFiltered.Count;
            var ordered = sortOldestFirst
                ? statusFiltered.OrderBy(s => s.CreatedAt)
                : statusFiltered.OrderByDescending(s => s.CreatedAt);
            var paged = ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var targetUnitMap = await GetTargetUnitNamesBySurveyIdsAsync(paged.Select(s => s.Id));

            var vm = new SuperAdminDashboardViewModel
            {
                TotalSurveys        = birimFiltered.Count,
                ActiveSurveys       = birimFiltered.Count(s => s.Status == SurveyStatus.Active),
                DraftSurveys        = birimFiltered.Count(s => s.Status == SurveyStatus.Draft
                                                            && s.ApprovalStatus != ApprovalStatus.Pending),
                PassiveSurveys      = birimFiltered.Count(s => s.Status == SurveyStatus.Inactive),
                PendingApprovalCount = birimFiltered.Count(s => s.ApprovalStatus == ApprovalStatus.Pending),
                RejectedCount       = birimFiltered.Count(s => s.ApprovalStatus == ApprovalStatus.Rejected),
                TotalResponses      = birimFiltered.Sum(s => s.ResponseCount),
                TotalAdminCount     = await _db.AdminPermissions.CountAsync(),
                SelectedBirim  = birim,
                StatusFilter   = statusFilter,
                StartDateStr   = startDate,
                EndDateStr     = endDate,
                DateSort       = sortOldestFirst ? "oldest" : "newest",
                AllBirimler    = allBirimler,
                CurrentPage    = page,
                TotalCount     = totalCount,
                PageSize       = pageSize,
                Surveys        = paged.Select(s => MapToListItem(s, targetUnitMap)).ToList()
            };

            return View(vm);
        }

        // ─── ANKET ONAYLAMA ─────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> SurveyApprovals(string? birim = null)
        {
            var allSurveys = await _surveyService.GetAllSurveySummariesAsync();

            var allBirimler = await GetSurveyFormUnitNamesAsync();

            var pending = allSurveys
                .Where(s => s.ApprovalStatus == ApprovalStatus.Pending)
                .ToList();

            if (!string.IsNullOrEmpty(birim))
                pending = pending.Where(s => s.CreatedByBirim == birim).ToList();

            var pendingTargetMap = await GetTargetUnitNamesBySurveyIdsAsync(pending.Select(s => s.Id));

            var vm = new SuperAdminDashboardViewModel
            {
                SelectedBirim = birim,
                AllBirimler = allBirimler,
                Surveys = pending.Select(s => MapToListItem(s, pendingTargetMap)).ToList(),
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
        public async Task<IActionResult> AllResults(string? birim = null, string? startDate = null, string? endDate = null, string dateSort = "newest")
        {
            var allSurveys = await _surveyService.GetAllSurveySummariesAsync();
            var allBirimler = await GetSurveyFormUnitNamesAsync();

            var turkish = new CultureInfo("tr-TR");
            var sortOldestFirst = string.Equals(dateSort, "oldest", StringComparison.OrdinalIgnoreCase);

            DateTime? start = null, end = null;
            if (!string.IsNullOrEmpty(startDate) && DateTime.TryParseExact(startDate, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var sd))
                start = sd;
            if (!string.IsNullOrEmpty(endDate) && DateTime.TryParseExact(endDate, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var ed))
                end = ed.AddDays(1).AddSeconds(-1); // gün sonu dahil

            // Dashboard ile aynı kaynak + birim eşlemesi (Türkçe I/İ); yanıt sayısı 0 olsa da listelenir (detay boş olabilir).
            IEnumerable<SurveySummaryDto> filtered = allSurveys;

            if (!string.IsNullOrEmpty(birim))
            {
                filtered = filtered.Where(s =>
                    turkish.CompareInfo.Compare(s.CreatedByBirim ?? "", birim, CompareOptions.IgnoreCase) == 0);
            }

            if (start.HasValue)
                filtered = filtered.Where(s => s.CreatedAt >= start.Value);
            if (end.HasValue)
                filtered = filtered.Where(s => s.CreatedAt <= end.Value);

            var ordered = sortOldestFirst
                ? filtered.OrderBy(s => s.CreatedAt)
                : filtered.OrderByDescending(s => s.CreatedAt);
            var list = ordered.ToList();

            var resultTargetMap = await GetTargetUnitNamesBySurveyIdsAsync(list.Select(s => s.Id));

            var vm = new SuperAdminResultsViewModel
            {
                SelectedBirim = birim,
                AllBirimler = allBirimler,
                Surveys = list.Select(s => MapToListItem(s, resultTargetMap)).ToList(),
                StartDate = start,
                EndDate = end,
                StartDateStr = startDate,
                EndDateStr = endDate,
                DateSort = sortOldestFirst ? "oldest" : "newest"
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
        public async Task<IActionResult> AdminManagement(string? filterBirim = null, string? searchName = null, int page = 1)
        {
            var admins = await _db.AdminPermissions
                .OrderBy(a => a.PersonelBirim)
                .ThenBy(a => a.Username)
                .ToListAsync();

            var allBirimler = await GetSurveyFormUnitNamesAsync();

            if (!string.IsNullOrEmpty(filterBirim))
                admins = admins.Where(a => a.PersonelBirim == filterBirim).ToList();

            if (!string.IsNullOrEmpty(searchName))
            {
                var q = searchName.ToLower(new System.Globalization.CultureInfo("tr-TR"));
                admins = admins.Where(a =>
                    a.Username.ToLower(new System.Globalization.CultureInfo("tr-TR")).Contains(q) ||
                    (a.Note != null && a.Note.ToLower(new System.Globalization.CultureInfo("tr-TR")).Contains(q))
                ).ToList();
            }

            // Sayfalama
            const int pageSize = 25;
            if (page < 1) page = 1;
            var totalCount = admins.Count;
            var paged = admins
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var vm = new AdminManagementViewModel
            {
                Admins = paged.Select(a => new AdminPermissionViewModel
                {
                    Id = a.Id,
                    Username = a.Username,
                    PersonelBirim = a.PersonelBirim,
                    Note = a.Note,
                    CreatedAt = a.CreatedAt
                }).ToList(),
                AllBirimler  = allBirimler,
                FilterBirim  = filterBirim,
                SearchName   = searchName,
                CurrentPage  = page,
                TotalCount   = totalCount,
                PageSize     = pageSize
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
            var birimRaw = personelBirim.Trim();
            var turkish = new CultureInfo("tr-TR");
            var catalogNames = await GetSurveyFormUnitNamesAsync();
            bool birimOk = _birimService.GetIdByName(birimRaw) != null
                || catalogNames.Any(b => turkish.CompareInfo.Compare(b, birimRaw, CompareOptions.IgnoreCase) == 0);
            if (!birimOk)
            {
                TempData["Error"] = $"'{birimRaw}' geçerli bir birim adı değil. Lütfen listeden seçiniz.";
                return RedirectToAction("AdminManagement");
            }

            personelBirim = birimRaw.ToUpper(turkish);

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

        // ─── LDAP & KIMLIK ARAMA (AJAX) ─────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetPersonelInfo(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return BadRequest("Username is required");
            
            var stajToken = "tekn0l0j1T00cken";
            var input = username.Replace("@selcuk.edu.tr", "").Trim();
            bool isTcInput = input.Length == 11 && input.All(char.IsDigit);

            try
            {
                string tc = "";
                string ad = "";
                string soyad = "";
                string birim = "";
                string jobType = "";

                if (isTcInput)
                {
                    tc = input;
                    var profile = await CallPersonnelProfileAsync(tc, stajToken);
                    if (profile != null)
                    {
                        ad = profile.Ad;
                        soyad = profile.Soyad;
                        birim = profile.PersonelBirim;
                    }
                    else
                    {
                        return Json(new { success = false, message = "TC ile personel bulunamadı." });
                    }
                }
                else
                {
                    // 1. LDAP'tan Personel Bilgilerini getir
                    var ldapPer = await CallTcDondurAsync(input, stajToken);
                    if (ldapPer == null || string.IsNullOrWhiteSpace(ldapPer.TC))
                    {
                        _logger.LogWarning("[GetPersonelInfo] LDAP üzerinde personel bulunamadı: {U}", input);
                        return Json(new { success = false, message = "Kullanıcı LDAP üzerinde bulunamadı veya TC bilgisi alınamadı." });
                    }

                    tc = ldapPer.TC;
                    ad = ldapPer.Ad;
                    soyad = ldapPer.Soyad;
                    jobType = ldapPer.JobRecordType;

                    // 2. Kimlik servisinden akademik detayları getir
                    var profile = await CallPersonnelProfileAsync(tc, stajToken);
                    if (profile != null)
                    {
                        if (string.IsNullOrWhiteSpace(ad)) ad = profile.Ad;
                        if (string.IsNullOrWhiteSpace(soyad)) soyad = profile.Soyad;
                        birim = profile.PersonelBirim;
                    }
                    
                    if (string.IsNullOrWhiteSpace(birim)) birim = ldapPer.PersonelBirim;
                }

                return Json(new { 
                    success = true, 
                    tc = tc,
                    ad = ad,
                    soyad = soyad,
                    birim = birim,
                    jobType = jobType
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetPersonelInfo sistem hatası: {U}", username);
                return Json(new { success = false, message = "Sistem hatası: " + ex.Message });
            }
        }

        // ─── YARDIMCI ───────────────────────────────────────────────────────────

        /// <summary>Verilen anket ID'leri için SurveyBirim tablosundan hedef birim adlarını toplu çeker.</summary>
        private async Task<IReadOnlyDictionary<int, List<string>>> GetTargetUnitNamesBySurveyIdsAsync(IEnumerable<int> surveyIds)
        {
            var ids = surveyIds.ToList();
            if (ids.Count == 0) return new Dictionary<int, List<string>>();

            var rows = await _db.SurveyBirimler
                .AsNoTracking()
                .Where(b => ids.Contains(b.SurveyId) && !string.IsNullOrEmpty(b.Birim))
                .Select(b => new { b.SurveyId, b.Birim })
                .ToListAsync();

            return rows
                .GroupBy(r => r.SurveyId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(r => r.Birim!).Distinct().OrderBy(x => x).ToList()
                );
        }

        private static SurveyListItemViewModel MapToListItem(
            SurveySummaryDto s,
            IReadOnlyDictionary<int, List<string>>? targetUnitMap = null) => new()
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
            ApprovalNote   = s.ApprovalNote,
            TargetUnits    = SurveyTargetUnitsHelper.ResolveFromDto(s, targetUnitMap),
            IsCreatedByCurrentUser = true
        };

        // ─── SOAP HELPERS ───────────────────────────────────────────────────────
        private async Task<PersonnelDetail?> CallTcDondurAsync(string mail, string token)
        {
            string soapEnvelope = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <tcDondurMailPersonel xmlns=""http://tempuri.org/"">
      <mail>{System.Security.SecurityElement.Escape(mail)}</mail>
      <sifre></sifre>
      <token>{System.Security.SecurityElement.Escape(token)}</token>
    </tcDondurMailPersonel>
  </soap:Body>
</soap:Envelope>";

            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://restwebservis.selcuk.edu.tr/LDAPAuth.asmx");
            request.Headers.Add("SOAPAction", "\"http://tempuri.org/tcDondurMailPersonel\"");
            request.Content = new System.Net.Http.StringContent(soapEnvelope, System.Text.Encoding.UTF8, "text/xml");

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var xmlString = await response.Content.ReadAsStringAsync();
            var doc = System.Xml.Linq.XDocument.Parse(xmlString);
            var node = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "tcDondurMailPersonelResult");
            if (node == null) return null;

            string F(string name) => node.Elements()
                .FirstOrDefault(x => string.Equals(x.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value ?? 
                node.Attribute(name)?.Value ?? "";

            return new PersonnelDetail
            {
                TC            = F("personelTckimlikno").Length > 0 ? F("personelTckimlikno") : F("TCKIMLIK"),
                Ad            = F("personelAd"),
                Soyad         = F("personelSoyad"),
                PersonelBirim = F("personelBirim"),
                JobRecordType = F("personelJobrecordtipi")
            };
        }

        private async Task<PersonnelDetail?> CallPersonnelProfileAsync(string tc, string token)
        {
            string soapEnvelope = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <NetiketPersonelDondurStajyer xmlns=""http://tempuri.org/"">
      <tc>{System.Security.SecurityElement.Escape(tc)}</tc>
      <token>{System.Security.SecurityElement.Escape(token)}</token>
    </NetiketPersonelDondurStajyer>
  </soap:Body>
</soap:Envelope>";

            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://restwebservis.selcuk.edu.tr/kimlik.asmx");
            request.Headers.Add("SOAPAction", "\"http://tempuri.org/NetiketPersonelDondurStajyer\"");
            request.Content = new System.Net.Http.StringContent(soapEnvelope, System.Text.Encoding.UTF8, "text/xml");

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var xmlString = await response.Content.ReadAsStringAsync();
            var doc = System.Xml.Linq.XDocument.Parse(xmlString);
            var node = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "NetiketPersonelDondurStajyerResult");
            if (node == null) return null;

            string F(string name) => node.Elements()
                .FirstOrDefault(x => string.Equals(x.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value ?? "";

            // Akademik birim için NETBIRIM, yoksa FIILIBIRIM kontrolü
            string birim = F("NETBIRIM");
            if (string.IsNullOrWhiteSpace(birim)) birim = F("FIILIBIRIM");

            return new PersonnelDetail
            {
                TC            = F("TCKIMLIK").Length > 0 ? F("TCKIMLIK") : tc,
                Ad            = F("AD"),
                Soyad         = F("SOYAD"),
                PersonelBirim = birim
            };
        }

        private class PersonnelDetail
        {
            public string TC { get; set; } = "";
            public string Ad { get; set; } = "";
            public string Soyad { get; set; } = "";
            public string PersonelBirim { get; set; } = "";
            public string JobRecordType { get; set; } = "";
        }
    }
}
