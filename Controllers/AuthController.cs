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
                var (profile, birimFromProfileJson, institutionalJobFromProfile) =
                    await GetProfileAsync(baseUrl, bearerToken);
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

                // ── ADIM 4: UnitIds → UnitList/cache + eksikler için UnitById (SuperAdmin ile aynı mantık; gerekirse System User)
                List<UnitDto> userUnits = new();
                if (profile.UnitIds?.Any() == true)
                {
                    var distinctCount = profile.UnitIds.Count(id => id > 0);
                    userUnits = await _unitApiService.ResolveProfileUnitIdsAsync(profile.UnitIds, accessToken);
                    _logger.LogInformation(
                        "[LOGIN] {U} → {Resolved}/{Distinct} birim çözüldü (UnitList + UnitById; unitIds: {Ids})",
                        profile.Username, userUnits.Count, distinctCount, string.Join(",", profile.UnitIds));
                }

                // ── ADIM 5: UI ana rol (izin önceliği) + öğrenci tipi (GetProfile öncelikli) + kurumsal personel tipi (anket hedefi; izinlerden bağımsız)
                string role = "";
                string jobType;

                if      (granted.Contains(AnketPermissions.SuperAdmin)) role = "SuperAdmin";
                else if (granted.Contains(AnketPermissions.Admin))      role = "Admin";
                else if (granted.Contains(AnketPermissions.Akademik))   role = "Akademik";
                else if (granted.Contains(AnketPermissions.Idari))      role = "Employee";
                else if (granted.Contains(AnketPermissions.Student))    role = "Student";

                var userTypeId = profile.UserTypeId;
                if (userTypeId != 1 && granted.Contains(AnketPermissions.Student))
                    userTypeId = 1;

                jobType = BuildInstitutionalJobRecordClaim(profile, institutionalJobFromProfile);

                // ── ADIM 6: Admin ise DB'den yetkili birimleri al ─────────────────
                // Birim adı: önce GetProfile→UnitIds→UnitList (herkes için aynı), sonra model/JSON — fiili birim
                var birimFromModel = profile.ResolvedBirimOrUnit;
                string personelBirim = userUnits.FirstOrDefault()?.Name
                    ?? birimFromModel
                    ?? birimFromProfileJson
                    ?? "";
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
                        // Yetkili birimler yalnızca AuthorizedUnits claim'inde; PersonelBirim = GetProfile fiili birim (anket birimi)
                        if (string.IsNullOrWhiteSpace(personelBirim))
                            personelBirim = authorizedUnits[0];
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
                    accessToken,
                    profile.UnitIds,
                    profile.FakulteAdi,
                    profile.BolumAdi);

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
        // SuperAdmin / Admin / Akademik panelinden çıkışta doğrudan anonim
        // anket listesi (/Home/Index) sayfasına yönlendirilir; eski "Login"
        // davranışı kaldırılmıştır. Home/Index [AllowAnonymous] olduğundan
        // erişim için yeniden giriş zorunlu değildir.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Home");
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

        /// GET /api/v1/Auth/GetProfile → CurrentUser + JSON içinden birim metni (Endpoint her zaman aynı property dönmeyebilir).
        private async Task<(CurrentUser? profile, string? birimFromJson, string? institutionalJobRaw)> GetProfileAsync(
            string baseUrl, string bearer)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var req    = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/v1/Auth/GetProfile");
                req.Headers.Add("Authorization", bearer);

                var resp = await client.SendAsync(req);
                var raw  = await resp.Content.ReadAsStringAsync();
                _logger.LogDebug("[PROFILE-API] {Status} {Body}", resp.StatusCode, raw);

                if (!resp.IsSuccessStatusCode) return (null, null, null);

                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                using var jsonDoc = JsonDocument.Parse(raw);
                var root = jsonDoc.RootElement;
                var valueEl = root.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.Object
                    ? val
                    : root;

                var profile = JsonSerializer.Deserialize<CurrentUser>(valueEl.GetRawText(), opts);
                var birimJson = ExtractBirimFromProfileElement(valueEl);

                if (string.IsNullOrWhiteSpace(birimJson) && profile != null)
                    birimJson = profile.ResolvedBirimOrUnit;

                var institutionalJobRaw = ExtractJobRecordTypeFromProfileElement(valueEl);

                return (profile, birimJson, institutionalJobRaw);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PROFILE-API] Hata");
                return (null, null, null);
            }
        }

        /// <summary>GetProfile JSON’dan kurumsal personel tipi (ANKET_* ile karıştırılmaz).</summary>
        private static string? ExtractJobRecordTypeFromProfileElement(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object) return null;

            foreach (var p in el.EnumerateObject())
            {
                if (!IsJobRecordJsonProperty(p.Name)) continue;
                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    var s = p.Value.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }

            return null;
        }

        private static bool IsJobRecordJsonProperty(string name) =>
            name.Equals("jobRecordType", StringComparison.OrdinalIgnoreCase)
            || name.Equals("personelJobrecordtipi", StringComparison.OrdinalIgnoreCase)
            || name.Equals("personelJobRecordTipi", StringComparison.OrdinalIgnoreCase)
            || name.Equals("jobRecordTipi", StringComparison.OrdinalIgnoreCase)
            || name.Equals("jobrecordtipi", StringComparison.OrdinalIgnoreCase)
            || name.Equals("personelTipi", StringComparison.OrdinalIgnoreCase)
            || name.Equals("jobRecord", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Cookie <c>JobRecordType</c>: öğrenci boş; personel için API metni veya varsayılan akademik kadro (Idari yalnızca API açıkça idari diyorsa).
        /// </summary>
        private static string BuildInstitutionalJobRecordClaim(CurrentUser profile, string? rawFromApi)
        {
            if (profile.UserTypeId == 1)
                return "";

            if (!string.IsNullOrWhiteSpace(rawFromApi))
                return NormalizeInstitutionalJobRecord(rawFromApi);

            return "Akademik";
        }

        private static string NormalizeInstitutionalJobRecord(string raw)
        {
            var s = raw.Trim();
            if (s.Length == 0) return "Akademik";

            if (s.Contains("idari", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Idari", StringComparison.OrdinalIgnoreCase)
                || s.Contains("İdari", StringComparison.OrdinalIgnoreCase))
                return "Idari";

            return "Akademik";
        }

        /// <summary>GetProfile value nesnesinden birim/fakülte metnini çıkarır (Swagger dışı varyasyonlar için).</summary>
        private static string? ExtractBirimFromProfileElement(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object) return null;

            foreach (var key in new[]
                     {
                         "birimAdi", "birimName", "birim", "unitName", "personelBirim",
                         "organizationUnitName", "departmentName", "department",
                         "facultyName", "fakulteAdi", "netBirim", "fiiliBirim", "corporateUnitName"
                     })
            {
                foreach (var p in el.EnumerateObject())
                {
                    if (!p.Name.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
                    if (p.Value.ValueKind == JsonValueKind.String)
                    {
                        var s = p.Value.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(s)) return s;
                    }
                    // { "unit": { "name": "..." } }
                    if (p.Value.ValueKind == JsonValueKind.Object &&
                        p.Value.TryGetProperty("name", out var nm) &&
                        nm.ValueKind == JsonValueKind.String)
                    {
                        var s = nm.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(s)) return s;
                    }
                }
            }

            if (el.TryGetProperty("units", out var units) && units.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in units.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    foreach (var nameKey in new[] { "name", "title", "displayName" })
                    {
                        if (item.TryGetProperty(nameKey, out var nm) && nm.ValueKind == JsonValueKind.String)
                        {
                            var s = nm.GetString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(s)) return s;
                        }
                    }
                }
            }

            return null;
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
            string? accessToken = null,
            ICollection<int>? profileUnitIds = null,
            string? fakulteAdiFromProfile = null,
            string? bolumAdiFromProfile = null)
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

            // UnitList senkronu / eşleşme boşsa: GetProfile'daki unitIds + birim metni ile yine UnitId zinciri (ANKET_IDARI / personel)
            if (userUnits.Count == 0 && profileUnitIds?.Count > 0)
            {
                var nameFallback = personelBirim;
                foreach (var uid in profileUnitIds.Distinct())
                {
                    claims.Add(new Claim("UnitId", uid.ToString()));
                    if (!string.IsNullOrWhiteSpace(nameFallback))
                        claims.Add(new Claim("UnitName", nameFallback));
                }
                _logger.LogInformation("[LOGIN] UnitList eşleşmesi yok; {N} adet UnitId doğrudan profile.UnitIds ile yazıldı.", profileUnitIds.Count);
            }

            if (!string.IsNullOrWhiteSpace(fakulteAdiFromProfile))
                claims.Add(new Claim("FakulteAdi", fakulteAdiFromProfile.Trim()));

            if (!string.IsNullOrWhiteSpace(bolumAdiFromProfile))
                claims.Add(new Claim("BolumAdi", bolumAdiFromProfile.Trim()));

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
