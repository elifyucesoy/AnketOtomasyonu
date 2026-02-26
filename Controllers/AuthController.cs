using AnketOtomasyonu.Authorization;
using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AnketOtomasyonu.Controllers
{
    public class AuthController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IAuthServiceHandler _authHandler;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            IHttpClientFactory httpClientFactory,
            IAuthServiceHandler authHandler,
            IConfiguration configuration,
            ILogger<AuthController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _authHandler = authHandler;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (!string.IsNullOrEmpty(HttpContext.Session.GetString("AccessToken")))
            {
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);
                return RedirectToAction("Index", "Home");
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

                // AuthServiceHandler içindeki CurrentUser kullan (namespace açık belirtiliyor)
                AnketOtomasyonu.Authorization.CurrentUser? user = null;
                if (profileResp.IsSuccessStatusCode)
                {
                    user = JsonSerializer.Deserialize<AnketOtomasyonu.Authorization.CurrentUser>(
                        profileContent,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }

                _logger.LogInformation(
                    "Parsed — UserId:{Id} Name:{Name} Surname:{Surname}",
                    user?.Id, user?.Name, user?.Surname);

                // 3) Session'a yaz
                HttpContext.Session.SetString("AccessToken", $"Bearer {accessToken}");
                HttpContext.Session.SetString("UserId", (user?.Id ?? 0).ToString());

                var fullName = $"{user?.Name} {user?.Surname}".Trim();
                HttpContext.Session.SetString("UserFullName",
                    string.IsNullOrWhiteSpace(fullName) ? (user?.Username ?? "") : fullName);

                // 4) Admin izni
                var isAdmin = await _authHandler.ValidatePermissionServiceAsync(
                    $"Bearer {accessToken}", "ANKET_API", ["ANKET_API"]);

                HttpContext.Session.SetString("UserRole", isAdmin ? "Admin" : "Kullanici");

                _logger.LogInformation(
                    "Login OK — User:{Name} Id:{Id} Admin:{A}",
                    fullName, user?.Id, isAdmin);

                // returnUrl varsa oraya yönlendir, yoksa ana sayfa
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);
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
            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult AccessDenied() => View();
    }
}