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
    [Authorize(Policy = AnketPermissions.PolicyAdminArea)]
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

        /// <summary>
        /// Birim yöneticisi: yalnız yetkili birim adlarıyla örtüşen anketler; detay için
        /// <see cref="ISurveyService.IsSurveyInAdminUnitScopeAsync"/>. SuperAdmin bu controller’ı kullanmaz.
        /// </summary>
        private Task<bool> CanAccessSurveyInAdminPanelAsync(int surveyId) =>
            _surveyService.IsSurveyInAdminUnitScopeAsync(
                surveyId,
                AdminUnitScopeHelper.GetAuthorizedUnitNames(User));

        private IActionResult RedirectToManagingDashboard() => RedirectToAction("Dashboard");

        private IActionResult RedirectAccessDeniedToDashboard()
        {
            TempData["Error"] = "Bu ankete atanmış birim kapsamınız dahilinde değilsiniz.";
            return RedirectToManagingDashboard();
        }

        private IActionResult RedirectNotOwnerToDashboard()
        {
            TempData["Error"] = "Bu anketi yalnızca oluşturan yönetici düzenleyebilir, silebilir veya yayınlayabilir. Siz aynı birimde yalnızca görüntüleme ve sonuçlara erişebilirsiniz.";
            return RedirectToManagingDashboard();
        }

        private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

        private Task<bool> IsCurrentUserSurveyOwnerAsync(int surveyId) =>
            _surveyService.IsSurveyCreatedByUserAsync(surveyId, CurrentUserId);

        /// <summary>«Tüm birimler» (<c>__ALL__</c>) yalnızca yetkili birim listesine genişler.</summary>
        private IReadOnlyList<string> GetExpandAllTargetScopeForCurrentUser() =>
            AdminUnitScopeHelper.GetAuthorizedUnitNames(User);

        /// <summary>CreateSurvey / EditSurvey — yalnızca PersonelBirim + AuthorizedUnits kapsamı.</summary>
        private async Task HydrateSurveyCreateFormAsync(SurveyCreateViewModel model)
        {
            ViewBag.AllBirimler = _birimService.GetAllNames();

            model.AuthorizedUnits = AdminUnitScopeHelper.GetAuthorizedUnitNames(User);

            var scopeSet = new HashSet<string>(model.AuthorizedUnits, StringComparer.OrdinalIgnoreCase);
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

            ViewBag.UnitList = dbUnits
                .Where(u => scopeSet.Contains(u.Name.Trim()))
                .ToList();
            ViewBag.FormController = "Admin";
        }

        [HttpGet]
        public async Task<IActionResult> Dashboard(
            string? birim = null,
            string? statusFilter = null,
            string? startDate = null,
            string? endDate = null,
            string dateSort = "newest",
            int page = 1)
        {
            var currentUserId = CurrentUserId ?? "";
            var authorizedUnits = AdminUnitScopeHelper.GetAuthorizedUnitNames(User);
            var all = (await _surveyService.GetSurveySummariesForAdminUnitScopeAsync(authorizedUnits)).ToList();

            // Birim filtresi — oluşturan birimi, yayın birimi veya SurveyBirim hedefleri
            IReadOnlyList<SurveySummaryDto> birimFiltered;
            if (string.IsNullOrEmpty(birim))
                birimFiltered = all;
            else
            {
                var birimNorm = birim.Trim();
                var targetMap = await _surveyService.GetTargetUnitNamesBySurveyIdsAsync(all.Select(x => x.Id).ToList());
                birimFiltered = all.Where(s =>
                    string.Equals(s.CreatedByBirim?.Trim(), birimNorm, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.UnitName?.Trim(), birimNorm, StringComparison.OrdinalIgnoreCase)
                    || (targetMap.TryGetValue(s.Id, out var tg)
                        && tg.Any(t => string.Equals(t.Trim(), birimNorm, StringComparison.OrdinalIgnoreCase))))
                    .ToList();
            }

            // Stat sayıları (birim filtresi sonrası, durum filtresi öncesi)
            var totalSurveys     = birimFiltered.Count;
            var activeSurveys    = birimFiltered.Count(s => s.Status == SurveyStatus.Active);
            var draftSurveys     = birimFiltered.Count(s => s.Status == SurveyStatus.Draft);
            var passiveSurveys   = birimFiltered.Count(s => s.Status == SurveyStatus.Inactive);
            var pendingCount     = birimFiltered.Count(s => s.ApprovalStatus == ApprovalStatus.Pending);
            var rejectedCount    = birimFiltered.Count(s => s.ApprovalStatus == ApprovalStatus.Rejected);
            var totalResponses   = birimFiltered.Sum(s => s.ResponseCount);

            // Durum / onay filtresi
            var filtered = birimFiltered.AsEnumerable();
            filtered = statusFilter switch
            {
                "active"   => filtered.Where(s => s.Status == SurveyStatus.Active),
                "draft"    => filtered.Where(s => s.Status == SurveyStatus.Draft),
                "passive"  => filtered.Where(s => s.Status == SurveyStatus.Inactive),
                "pending"  => filtered.Where(s => s.ApprovalStatus == ApprovalStatus.Pending),
                "rejected" => filtered.Where(s => s.ApprovalStatus == ApprovalStatus.Rejected),
                _          => filtered
            };

            // Tarih aralığı filtresi
            DateTime? start = null, end = null;
            if (DateTime.TryParse(startDate, out var sd)) { start = sd.Date; filtered = filtered.Where(s => s.CreatedAt.Date >= start.Value); }
            if (DateTime.TryParse(endDate,   out var ed)) { end   = ed.Date; filtered = filtered.Where(s => s.CreatedAt.Date <= end.Value);   }

            // Sıralama
            filtered = dateSort == "oldest"
                ? filtered.OrderBy(s => s.CreatedAt)
                : filtered.OrderByDescending(s => s.CreatedAt);

            var list = filtered.ToList();

            // Sayfalama
            const int pageSize = 25;
            page = Math.Max(1, page);
            var totalCount = list.Count;
            var paged = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var vm = new AdminDashboardViewModel
            {
                TotalSurveys         = totalSurveys,
                ActiveSurveys        = activeSurveys,
                DraftSurveys         = draftSurveys,
                PassiveSurveys       = passiveSurveys,
                PendingApprovalCount = pendingCount,
                RejectedCount        = rejectedCount,
                TotalResponses       = totalResponses,
                TotalUsers           = 0,
                AuthorizedUnits      = authorizedUnits,
                SelectedBirim        = birim,
                SurveyStatusFilter   = statusFilter,
                StartDateStr         = startDate,
                EndDateStr           = endDate,
                DateSort             = dateSort,
                CurrentPage          = page,
                TotalCount           = totalCount,
                PageSize             = pageSize,
                RecentSurveys = paged.Select(s => new SurveyListItemViewModel
                {
                    Id = s.Id,
                    Title = s.Title,
                    IsAnonymous = s.IsAnonymous,
                    IsCreatedByCurrentUser = !string.IsNullOrEmpty(currentUserId)
                        && string.Equals(s.CreatedByUserId, currentUserId, StringComparison.Ordinal),
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
            ViewBag.SelectedRoles = new List<string> { "Student" };
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
                ViewBag.FormController = "Admin";
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

            // Birim yöneticisi: onay bekler. Otomatik onaylı taslak yalnızca /SuperAdmin/CreateSurvey.
            var expandScope = GetExpandAllTargetScopeForCurrentUser();
            await _surveyService.CreateSurveyAsync(dto, createdById, createdByName, createdByBirim, isSuperAdmin: false, expandScope);

            TempData["Success"] = "Anket başarıyla oluşturuldu. Yayınlamak için Yayınla butonuna tıklayın.";
            return RedirectToManagingDashboard();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Publish(int id)
        {
            if (!await CanAccessSurveyInAdminPanelAsync(id)) return RedirectAccessDeniedToDashboard();
            if (!await IsCurrentUserSurveyOwnerAsync(id)) return RedirectNotOwnerToDashboard();

            var approvalRow = await _db.Surveys.AsNoTracking()
                .Where(s => s.Id == id)
                .Select(s => new { s.ApprovalStatus })
                .FirstOrDefaultAsync();

            if (approvalRow == null)
            {
                TempData["Error"] = "Anket bulunamadı.";
                return RedirectToManagingDashboard();
            }

            // SuperAdmin doğrudan yayınlayabilir; Admin için onay şart
            if (!User.HasSuperAdminAccess() && approvalRow.ApprovalStatus != ApprovalStatus.Approved)
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
            if (!await CanAccessSurveyInAdminPanelAsync(id)) return RedirectAccessDeniedToDashboard();
            if (!await IsCurrentUserSurveyOwnerAsync(id)) return RedirectNotOwnerToDashboard();

            await _surveyService.CloseSurveyAsync(id);
            TempData["Success"] = "Anket kapatıldı.";
            return RedirectToManagingDashboard();
        }

        [HttpGet]
        public async Task<IActionResult> EditSurvey(int id)
        {
            if (!await CanAccessSurveyInAdminPanelAsync(id)) return RedirectAccessDeniedToDashboard();
            if (!await IsCurrentUserSurveyOwnerAsync(id)) return RedirectNotOwnerToDashboard();

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
            if (!await CanAccessSurveyInAdminPanelAsync(id)) return RedirectAccessDeniedToDashboard();
            if (!await IsCurrentUserSurveyOwnerAsync(id)) return RedirectNotOwnerToDashboard();

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
            if (!await CanAccessSurveyInAdminPanelAsync(id)) return RedirectAccessDeniedToDashboard();
            if (!await IsCurrentUserSurveyOwnerAsync(id)) return RedirectNotOwnerToDashboard();

            var approvalRow = await _db.Surveys.AsNoTracking()
                .Where(s => s.Id == id)
                .Select(s => new { s.ApprovalStatus })
                .FirstOrDefaultAsync();

            if (approvalRow == null)
            {
                TempData["Error"] = "Anket bulunamadı.";
                return RedirectToManagingDashboard();
            }

            if (!User.HasSuperAdminAccess() && approvalRow.ApprovalStatus != ApprovalStatus.Approved)
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
            if (!await CanAccessSurveyInAdminPanelAsync(id)) return RedirectAccessDeniedToDashboard();

            var survey = await _surveyService.GetSurveyWithQuestionsAsync(id);
            if (survey == null) return NotFound();

            ViewBag.CanManageSurvey = await IsCurrentUserSurveyOwnerAsync(id);
            return View(survey);
        }

        [HttpGet]
        public async Task<IActionResult> Results(int id, string? fakulte = null, string? bolum = null, string? birim = null)
        {
            if (!await CanAccessSurveyInAdminPanelAsync(id)) return RedirectAccessDeniedToDashboard();

            var results = await _responseService.GetSurveyResultsAsync(id, fakulte, bolum, birim);

            ViewBag.Fakulteler = _birimService.GetAllNames();

            var opts = await _responseService.GetRespondentFilterOptionsAsync(id);
            ViewBag.Bolumler = opts.Bolumler;
            ViewBag.Birimler = opts.Birimler;

            ViewBag.SelectedFakulte = fakulte;
            ViewBag.SelectedBolum = bolum;
            ViewBag.SelectedBirim = birim;

            return View(results);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            // Silinmiş ankete ikinci POST (çift tıklama, yavaş ağ) Unauthorized yerine yönlendir — aksi halde 401 görünür.
            var stillThere = await _db.Surveys.AsNoTracking().AnyAsync(x => x.Id == id);
            if (!stillThere)
            {
                TempData["Error"] = "Anket bulunamadı veya zaten silindi.";
                return RedirectToManagingDashboard();
            }

            if (!await CanAccessSurveyInAdminPanelAsync(id)) return RedirectAccessDeniedToDashboard();
            if (!await IsCurrentUserSurveyOwnerAsync(id)) return RedirectNotOwnerToDashboard();

            await _surveyService.DeleteSurveyAsync(id);
            TempData["Success"] = "Anket silindi.";
            return RedirectToManagingDashboard();
        }
    }
}