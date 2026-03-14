using AnketOtomasyonu.Authorization;
using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace AnketOtomasyonu.Controllers
{
    [AllowAnonymous]
    public class AuthController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IAuthServiceHandler _authHandler;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;
        private readonly IWebHostEnvironment _env;

        public AuthController(
            IHttpClientFactory httpClientFactory,
            IAuthServiceHandler authHandler,
            IConfiguration configuration,
            ILogger<AuthController> logger,
            IWebHostEnvironment env)
        {
            _httpClientFactory = httpClientFactory;
            _authHandler = authHandler;
            _configuration = configuration;
            _logger = logger;
            _env = env;
        }

        // ─── SADECE DEVELOPMENT ─────────────────────────────────────────────────
        // /Auth/DevLogin  →  gerçek API olmadan session'ı elle kurar (test amaçlı)
        [HttpGet]
        public IActionResult DevLogin()
        {
            if (!_env.IsDevelopment() || _configuration.GetValue<bool>("DevLogin:Enabled") == false)
                return NotFound();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DevLogin(string role)
        {
            if (!_env.IsDevelopment() || _configuration.GetValue<bool>("DevLogin:Enabled") == false)
                return NotFound();

            HttpContext.Session.Clear();
            HttpContext.Session.SetString("AccessToken", "Bearer dev-test-token");
            HttpContext.Session.SetString("UserId", "9999");

            // role: "Admin" | "Employee" | "Student"
            string sessionRole, userTypeStr;
            int userTypeId;

            switch (role)
            {
                case "Admin":
                    sessionRole  = "Admin";
                    userTypeStr  = "Employee";
                    userTypeId   = 0;
                    break;
                case "Employee":
                    sessionRole  = "Employee";
                    userTypeStr  = "Employee";
                    userTypeId   = 0;
                    break;
                default: // "Student"
                    sessionRole  = "Student";
                    userTypeStr  = "Student";
                    userTypeId   = 1;
                    break;
            }

            HttpContext.Session.SetString("UserRole",     sessionRole);
            HttpContext.Session.SetString("UserType",     userTypeStr);
            HttpContext.Session.SetInt32 ("UserTypeId",   userTypeId);
            HttpContext.Session.SetString("UserFullName", $"DevUser ({sessionRole})");

            _logger.LogWarning("[DEV-LOGIN] Session kuruldu → Role:{R} UserType:{T}",
                sessionRole, userTypeStr);

            return sessionRole == "Admin"
                ? RedirectToAction("Dashboard", "Admin")
                : RedirectToAction("Index", "Home");
        }
        // ────────────────────────────────────────────────────────────────────────

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            // Anket doldurma sayfasına gidiyorsa her zaman taze login iste
            bool isSurveyFill = !string.IsNullOrEmpty(returnUrl)
                && returnUrl.Contains("SurveyResponse/Fill", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(HttpContext.Session.GetString("AccessToken")))
            {
                if (isSurveyFill)
                {
                    // Anket doldurmak için eski session geçersiz — temizle, login formunu göster
                    HttpContext.Session.Clear();
                }
                else
                {
                    if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                        return Redirect(returnUrl);
                    return RedirectToAction("Index", "Home");
                }
            }

            ViewBag.ReturnUrl = returnUrl;
            return View(new LoginViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            if (!ModelState.IsValid) return View(model);

            try
            {
                var baseUrl = _configuration["PermissionService:BaseUrl"]!;
                var client = _httpClientFactory.CreateClient();

                // 1) Login
                var loginBody = new
                {
                    userName = model.Username,
                    password = model.Password,
                    deviceToken = "web",
                    channel = 0
                };

                var loginResp = await client.PostAsync(
                    $"{baseUrl}/api/v1/Auth/Login",
                    new StringContent(
                        JsonSerializer.Serialize(loginBody),
                        System.Text.Encoding.UTF8,
                        "application/json"));

                var loginContent = await loginResp.Content.ReadAsStringAsync();
                _logger.LogInformation("Login Response: {Content}", loginContent);

                var loginResult = JsonSerializer.Deserialize<LoginResponseDto>(
                    loginContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (loginResult == null || !loginResult.IsSucceeded || loginResult.Value == null)
                {
                    ViewBag.Error = loginResult?.Error?.Message
                        ?? "Kullanıcı adı veya şifre hatalı.";
                    return View(model);
                }

                var accessToken = loginResult.Value.AccessToken;

                // 2) GetProfile
                var profileReq = new HttpRequestMessage(HttpMethod.Get,
                    $"{baseUrl}/api/v1/Auth/GetProfile");
                profileReq.Headers.Add("Authorization", $"Bearer {accessToken}");

                var profileResp = await client.SendAsync(profileReq);
                var profileContent = await profileResp.Content.ReadAsStringAsync();

                _logger.LogInformation("Profile Status: {Code}", profileResp.StatusCode);
                _logger.LogInformation("Profile Content: {Content}", profileContent);

                // GetProfile yanıtı Login gibi { isSucceeded, error, value } sarmalıdır.
                // value içinde userTypeId (0=Employee, 1=Student) ve hasPermission alanları gelir.
                AnketOtomasyonu.Authorization.CurrentUser? user = null;
                if (profileResp.IsSuccessStatusCode)
                {
                    var profileResult = JsonSerializer.Deserialize<ProfileResponseDto>(
                        profileContent,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (profileResult?.IsSucceeded == true && profileResult.Value != null)
                        user = profileResult.Value;
                    else
                        // Fallback: bazı API'ler value sarması olmadan doğrudan döner
                        user = JsonSerializer.Deserialize<AnketOtomasyonu.Authorization.CurrentUser>(
                            profileContent,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }

                _logger.LogInformation(
                    "Parsed — UserId:{Id} Name:{Name} Surname:{Surname} UserTypeId:{UT} HasPermission:{HP}",
                    user?.Id, user?.Name, user?.Surname, user?.UserTypeId, user?.HasPermission);

                // 3) Session'a yaz
                HttpContext.Session.SetString("AccessToken", $"Bearer {accessToken}");
                HttpContext.Session.SetString("UserId", (user?.Id ?? 0).ToString());

                var fullName = $"{user?.Name} {user?.Surname}".Trim();
                HttpContext.Session.SetString("UserFullName",
                    string.IsNullOrWhiteSpace(fullName) ? (user?.Username ?? "") : fullName);

                // 4) UserRole belirleme:
                //    hasPermission=true           → "Admin"   (dashboard, anket yönetimi)
                //    hasPermission=false, type=0  → "Employee" (personel, ANKET_API_STUDENT erişimi)
                //    hasPermission=false, type=1  → "Student"  (öğrenci, ANKET_API_STUDENT erişimi)
                var isAdmin    = user?.HasPermission ?? false;
                var userTypeId = user?.UserTypeId ?? 1;
                var userTypeStr = userTypeId == 0 ? "Employee" : "Student";

                var sessionRole = isAdmin ? "Admin" : userTypeStr; // "Admin" | "Employee" | "Student"
                HttpContext.Session.SetString("UserRole",   sessionRole);
                HttpContext.Session.SetString("UserType",   userTypeStr);
                HttpContext.Session.SetInt32 ("UserTypeId", userTypeId);

                _logger.LogInformation(
                    "Login OK — User:{Name} Id:{Id} Role:{R} UserTypeId:{UT} HasPermission:{HP}",
                    fullName, user?.Id, sessionRole, userTypeId, user?.HasPermission);

                // Anket doldurma sayfasına yönlendiriliyorsa tek kullanımlık bayrak set et
                if (!string.IsNullOrEmpty(returnUrl)
                    && returnUrl.Contains("SurveyResponse/Fill", StringComparison.OrdinalIgnoreCase))
                {
                    HttpContext.Session.SetString("FillAuthenticated", "true");
                }

                // returnUrl varsa oraya yönlendir
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);

                // Admin ise doğrudan dashboard'a, değilse ana sayfaya
                if (isAdmin)
                    return RedirectToAction("Dashboard", "Admin");

                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login hatası");
                ViewBag.Error = "Sunucu hatası. Lütfen tekrar deneyin.";
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult AccessDenied() => View();
    }
}