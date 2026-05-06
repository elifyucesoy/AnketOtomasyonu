using AnketOtomasyonu.Authorization;
using AnketOtomasyonu.Authorization.Models;
using AnketOtomasyonu.Data;
using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Models.ViewModels;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Net.Http.Json;

namespace AnketOtomasyonu.Controllers
{
    [AllowAnonymous]
    public class AuthController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;
        private readonly IWebHostEnvironment _env;
        private readonly ApplicationDbContext _db;
        private readonly IUnitApiService _unitApiService;

        public AuthController(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<AuthController> logger,
            IWebHostEnvironment env,
            ApplicationDbContext db,
            IUnitApiService unitApiService)
        {
            _httpClientFactory = httpClientFactory;
            _configuration     = configuration;
            _logger            = logger;
            _env               = env;
            _db                = db;
            _unitApiService    = unitApiService;
        }

        // ─── GET: Login ──────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Login(string? returnUrl = null)
        {
            bool isSurveyFill = !string.IsNullOrEmpty(returnUrl)
                && returnUrl.Contains("SurveyResponse/Fill", StringComparison.OrdinalIgnoreCase);

            if (User.Identity?.IsAuthenticated == true)
            {
                var currentRole = User.FindFirstValue(ClaimTypes.Role);
                bool isStudentOrEmployee = currentRole == "Student" || currentRole == "Employee";

                if (isSurveyFill && isStudentOrEmployee)
                    await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                else if (!isSurveyFill)
                {
                    if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                        return Redirect(returnUrl);
                    return RedirectByRole(currentRole);
                }
            }

            ViewBag.ReturnUrl    = returnUrl;
            ViewBag.IsSurveyFill = isSurveyFill;
            return View(new LoginViewModel());
        }

        // ─── POST: Login ─────────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            if (!ModelState.IsValid) return View(model);

            try
            {
                var baseUrl = _configuration["PermissionService:BaseUrl"]
                    ?? throw new InvalidOperationException("PermissionService:BaseUrl ayarlanmamış");

                // ── ADIM 1: Login → accessToken al ───────────────────────────────
                var accessToken = await PostLoginAsync(baseUrl, model.Username.Trim(), model.Password);
                if (accessToken is null)
                {
                    ViewBag.Error = "Kullanıcı adı veya şifre hatalı.";
                    return View(model);
                }

                var bearerToken = $"Bearer {accessToken}";

                // ── ADIM 2: GetProfile → kullanıcı bilgilerini ve unitIds'i al ──
                var profile = await GetProfileAsync(baseUrl, bearerToken);
                if (profile is null)
                {
                    ViewBag.Error = "Kullanıcı profili alınamadı.";
                    return View(model);
                }

                // ── ADIM 3: HasPermission — tüm izin kodlarını kontrol et ────────
                var permChecks = await Task.WhenAll(
                    AnketPermissions.AllCodes.Select(c => HasPermAsync(baseUrl, bearerToken, c)).ToArray());
                var granted = AnketPermissions.AllCodes
                    .Where((c, i) => permChecks[i])
                    .ToList();

                if (granted.Count == 0)
                {
                    _logger.LogWarning("[LOGIN] İzin bulunamadı: {U}", profile.Username);
                    ViewBag.Error = "Bu sisteme erişim yetkiniz bulunmuyor.";
                    return View(model);
                }

                // ── ADIM 4: UnitIds → Unit bilgilerini çek (cache'den) ────────────
                List<UnitDto> userUnits = new();
                if (profile.UnitIds?.Any() == true)
                {
                    // UnitList'i cache'le (30 gün), kullanıcının birimlerini filtrele
                    userUnits = await _unitApiService.GetUnitsByIdsAsync(profile.UnitIds, accessToken);
                    _logger.LogInformation("[LOGIN] {U} → {N} birim bulundu (unitIds: {Ids})",
                        profile.Username, userUnits.Count, string.Join(",", profile.UnitIds));
                }

                // ── ADIM 5: Rol belirleme ─────────────────────────────────────────
                string role    = "";
                string jobType = "";
                int userTypeId = 0;

                if      (granted.Contains(AnketPermissions.SuperAdmin)) { role = "SuperAdmin"; jobType = "Akademik"; }
                else if (granted.Contains(AnketPermissions.Admin))      { role = "Admin";      jobType = "Akademik"; }
                else if (granted.Contains(AnketPermissions.Akademik))   { role = "Akademik";   jobType = "Akademik"; }
                else if (granted.Contains(AnketPermissions.Idari))      { role = "Employee";   jobType = "Idari"; }
                else if (granted.Contains(AnketPermissions.Student))    { role = "Student";    userTypeId = 1; }

                // ── ADIM 6: Admin ise DB'den yetkili birimleri al ─────────────────
                string personelBirim = userUnits.FirstOrDefault()?.Name ?? "";
                List<string>? authorizedUnits = null;

                if (role == "Admin")
                {
                    var dbBirims = await _db.AdminPermissions
                        .Where(p => p.Username.ToLower() == model.Username.Trim().ToLower() ||
                                    p.Username.ToLower() == profile.Username.ToLower())
                        .Select(p => p.PersonelBirim)
                        .ToListAsync();

                    if (dbBirims.Count > 0)
                    {
                        var tr = new CultureInfo("tr-TR");
                        authorizedUnits = dbBirims
                            .GroupBy(x => x.ToUpper(tr))
                            .Select(g => g.First())
                            .ToList();
                        personelBirim = authorizedUnits.First();
                    }
                }

                // ── ADIM 7: Cookie claims oluştur ve oturumu aç ──────────────────
                var identity = BuildIdentity(
                    profile.Id.ToString(),
                    $"{profile.Name} {profile.Surname}".Trim(),
                    role, userTypeId,
                    personelBirim, jobType,
                    granted,
                    userUnits,
                    authorizedUnits,
                    accessToken);

                _logger.LogInformation("[LOGIN-OK] {User} → Rol:{Role} Birim:{Birim}",
                    profile.Username, role, personelBirim);

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identity));

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);

                return RedirectByRole(role);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LOGIN] Hata");
                ViewBag.Error = "Sistemsel bir hata oluştu. Lütfen tekrar deneyin.";
                return View(model);
            }
        }

        // ─── Logout ──────────────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult AccessDenied() => View();

        // ═══════════════════════════════════════════════════════════════════════
        //  ÖZEL YARDIMCI METODLAR
        // ═══════════════════════════════════════════════════════════════════════

        /// POST /api/v1/Auth/Login → accessToken veya null
        private async Task<string?> PostLoginAsync(string baseUrl, string username, string password)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var resp   = await client.PostAsJsonAsync(
                    $"{baseUrl}/api/v1/Auth/Login",
                    new { userName = username, password, deviceToken = "web", channel = 0 });

                var raw = await resp.Content.ReadAsStringAsync();
                _logger.LogDebug("[LOGIN-API] {Status} {Body}", resp.StatusCode, raw);

                if (!resp.IsSuccessStatusCode) return null;

                var doc = JsonSerializer.Deserialize<JsonElement>(raw);
                if (doc.TryGetProperty("isSucceeded", out var ok) && !ok.GetBoolean()) return null;
                if (doc.TryGetProperty("value", out var val) &&
                    val.TryGetProperty("accessToken", out var tok))
                    return tok.GetString();

                return null;
            }
            catch (Exception ex) { _logger.LogError(ex, "[LOGIN-API] Hata"); return null; }
        }

        /// GET /api/v1/Auth/GetProfile → CurrentUser (unitIds dahil) veya null
        private async Task<CurrentUser?> GetProfileAsync(string baseUrl, string bearer)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var req    = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/v1/Auth/GetProfile");
                req.Headers.Add("Authorization", bearer);

                var resp = await client.SendAsync(req);
                var raw  = await resp.Content.ReadAsStringAsync();
                _logger.LogDebug("[PROFILE-API] {Status} {Body}", resp.StatusCode, raw);

                if (!resp.IsSuccessStatusCode) return null;

                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var doc  = JsonSerializer.Deserialize<JsonElement>(raw, opts);

                if (doc.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.Object)
                    return val.Deserialize<CurrentUser>(opts);

                return doc.Deserialize<CurrentUser>(opts);
            }
            catch (Exception ex) { _logger.LogError(ex, "[PROFILE-API] Hata"); return null; }
        }

        /// POST /api/v1/Permission/HasPermission
        private async Task<bool> HasPermAsync(string baseUrl, string bearer, string permCode)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var req    = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/v1/Permission/HasPermission");
                req.Headers.Add("Authorization", bearer);
                req.Content = JsonContent.Create(new
                {
                    GroupCode = AnketPermissions.GroupCode,
                    Codes     = new[] { permCode },
                    Operation = 1
                });

                var resp = await client.SendAsync(req);
                var raw  = await resp.Content.ReadAsStringAsync();
                _logger.LogDebug("[HASPERM-API] {Code} → {Status} {Body}", permCode, resp.StatusCode, raw);

                if (!resp.IsSuccessStatusCode) return false;
                return PermissionApiResponseParser.TryParseBool(raw, out var ok) && ok;
            }
            catch (Exception ex) { _logger.LogError(ex, "[HASPERM-API] {Code} Hata", permCode); return false; }
        }

        // ─── Claims Identity ─────────────────────────────────────────────────────
        private ClaimsIdentity BuildIdentity(
            string userId, string fullName, string role, int userTypeId,
            string personelBirim, string jobType,
            IReadOnlyCollection<string> grantedPermissions,
            List<UnitDto> userUnits,
            List<string>? authorizedUnits = null,
            string? accessToken = null)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId),
                new(ClaimTypes.Name,           fullName),
                new(ClaimTypes.Role,           role),
                new("UserTypeId",              userTypeId.ToString()),
                new("PersonelBirim",           personelBirim),
                new("JobRecordType",           jobType),
            };

            // AnketPermission claims (HasAnketPermission() extension ile kontrol edilir)
            foreach (var code in grantedPermissions.Distinct())
                claims.Add(new Claim(AnketPermissions.ClaimType, code));

            // AccessToken claim — AuthServiceHandler'ın HasPermission çağrısında kullanır
            if (!string.IsNullOrEmpty(accessToken))
                claims.Add(new Claim("AccessToken", accessToken));

            // Kullanıcının birimleri (GetProfile → UnitIds → UnitList)
            foreach (var unit in userUnits)
            {
                claims.Add(new Claim("UnitId",   unit.Id.ToString()));
                claims.Add(new Claim("UnitName", unit.Name));
                if (unit.UnitTypeId.HasValue)
                    claims.Add(new Claim("UnitTypeId", unit.UnitTypeId.Value.ToString()));
                if (!string.IsNullOrEmpty(unit.UnitTypeName))
                    claims.Add(new Claim("UnitTypeName", unit.UnitTypeName));
            }

            // Admin yetkili birimleri (DB'den)
            if (authorizedUnits?.Count > 0)
                foreach (var u in authorizedUnits.Distinct())
                    claims.Add(new Claim("AuthorizedUnits", u));

            return new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        }

        // ─── Rol'e göre yönlendirme ──────────────────────────────────────────────
        private IActionResult RedirectByRole(string? role) => role switch
        {
            "SuperAdmin" => RedirectToAction("Dashboard", "SuperAdmin"),
            "Admin"      => RedirectToAction("Dashboard", "Admin"),
            "Akademik"   => RedirectToAction("Dashboard", "Akademik"),
            _            => RedirectToAction("Index",     "Home")
        };
    }
}
