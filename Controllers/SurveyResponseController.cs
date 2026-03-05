using AnketOtomasyonu.Authorization;
using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Models.ViewModels;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnketOtomasyonu.Controllers
{
    [AllowAnonymous]
    public class SurveyResponseController : Controller
    {
        private readonly ISurveyService _surveyService;
        private readonly ISurveyResponseService _responseService;
        private readonly IAuthServiceHandler _authServiceHandler;

        public SurveyResponseController(
            ISurveyService surveyService,
            ISurveyResponseService responseService,
            IAuthServiceHandler authServiceHandler)
        {
            _surveyService = surveyService;
            _responseService = responseService;
            _authServiceHandler = authServiceHandler;
        }

        // GET /SurveyResponse/PublicSurveys
        // Herkese açık sayfa — tüm aktif anonim anketleri listeler, login gerekmez.
        [HttpGet]
        public async Task<IActionResult> PublicSurveys()
        {
            var surveys = await _surveyService.GetActiveAnonymousSurveysAsync();

            var vm = new SurveyIndexViewModel
            {
                UserFullName = HttpContext.Session.GetString("UserFullName"),
                UserRole = HttpContext.Session.GetString("UserRole"),
                IsLoggedIn = !string.IsNullOrEmpty(HttpContext.Session.GetString("AccessToken")),
                Surveys = surveys.Select(s => new SurveyListItemViewModel
                {
                    Id = s.Id,
                    Title = s.Title,
                    Description = s.Description,
                    Status = "Aktif",
                    StatusBadgeClass = "bg-success",
                    QuestionCount = s.Questions.Count,
                    ResponseCount = s.Responses.Count,
                    CreatedByName = s.CreatedByName,
                    CreatedAt = s.CreatedAt,
                    IsAnonymous = true,
                    TargetRoles = s.TargetRoles
                }).ToList()
            };

            return View(vm);
        }

        // GET /SurveyResponse/Fill/{id}
        // Anonim anketlerde login gerekmez; URL'deki id ile doğrudan erişilebilir.
        [HttpGet]
        public async Task<IActionResult> Fill(int id)
        {
            var survey = await _surveyService.GetSurveyWithQuestionsAsync(id);
            if (survey == null)
            {
                TempData["Error"] = "Anket bulunamadı.";
                return RedirectToAction("NotFound_", "SurveyResponse");
            }

            if (survey.Status != SurveyStatus.Active)
            {
                TempData["Error"] = "Bu anket aktif değil.";
                if (survey.IsAnonymous)
                    return RedirectToAction("NotFound_", "SurveyResponse");
                return RedirectToAction("Index", "Home");
            }

            var userId = HttpContext.Session.GetString("UserId");

            if (survey.IsAnonymous)
            {
                // ── ANONİM ANKET: login gerekmez, IP ile tekrar doldurma kontrolü ──
                var ip = GetClientIp();
                if (!string.IsNullOrEmpty(ip) &&
                    await _responseService.HasRespondedByIpAsync(id, ip))
                {
                    TempData["Error"] = "Bu anketi zaten doldurdunuz.";
                    return RedirectToAction("AlreadyFilled");
                }
            }
            else
            {
                // ── NORMAL ANKET: her seferinde taze login zorunlu ──
                var accessToken = HttpContext.Session.GetString("AccessToken");
                var fillAuth = HttpContext.Session.GetString("FillAuthenticated");

                // Taze login bayrağı yoksa veya token geçersizse → login'e yönlendir
                if (string.IsNullOrEmpty(fillAuth)
                    || string.IsNullOrEmpty(accessToken)
                    || !await ValidateAccessTokenAsync(accessToken))
                {
                    ClearSessionData();
                    var returnUrl = Url.Action("Fill", "SurveyResponse", new { id });
                    return RedirectToAction("Login", "Auth", new { returnUrl });
                }

                // Bayrağı tüket — bir sonraki Fill ziyaretinde tekrar login gerekecek
                HttpContext.Session.Remove("FillAuthenticated");

                // UserId'yi taze session'dan al
                userId = HttpContext.Session.GetString("UserId");

                if (await _responseService.HasUserRespondedAsync(id, userId))
                {
                    TempData["Error"] = "Bu anketi zaten doldurdunuz.";
                    return RedirectToAction("Index", "Home");
                }
            }

            var vm = new SurveyFillViewModel
            {
                SurveyId = survey.Id,
                Title = survey.Title,
                Description = survey.Description,
                IsAnonymous = survey.IsAnonymous,
                Questions = survey.Questions
                    .OrderBy(q => q.OrderIndex)
                    .Select(q => new FillQuestionViewModel
                    {
                        QuestionId = q.Id,
                        Text = q.Text,
                        Type = q.Type,
                        IsRequired = q.IsRequired,
                        OrderIndex = q.OrderIndex,
                        Options = q.Options
                            .OrderBy(o => o.OrderIndex)
                            .Select(o => new FillOptionViewModel
                            {
                                Id = o.Id,
                                Text = o.Text,
                                Value = o.Value,
                                OrderIndex = o.OrderIndex
                            }).ToList()
                    }).ToList()
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(SurveySubmitDto dto)
        {
            var ip = GetClientIp();

            // Anketi çekerek IsAnonymous kontrolü yap
            var survey = await _surveyService.GetSurveyWithQuestionsAsync(dto.SurveyId);
            if (survey == null)
            {
                TempData["Error"] = "Anket bulunamadı.";
                return RedirectToAction("NotFound_", "SurveyResponse");
            }

            string userId;

            if (survey.IsAnonymous)
            {
                // Anonim ankette login gerekmez; userId olarak IP kaydedilecek
                userId = ip;
            }
            else
            {
                // Normal ankette login zorunlu + token geçerlilik kontrolü
                var sessionUserId = HttpContext.Session.GetString("UserId");
                var submitAccessToken = HttpContext.Session.GetString("AccessToken");
                if (string.IsNullOrEmpty(submitAccessToken) || !await ValidateAccessTokenAsync(submitAccessToken))
                {
                    ClearSessionData();
                    var returnUrl = Url.Action("Fill", "SurveyResponse", new { id = dto.SurveyId });
                    return RedirectToAction("Login", "Auth", new { returnUrl });
                }

                userId = sessionUserId;
            }

            var (success, message) =
                await _responseService.SubmitResponseAsync(dto, userId, ip);

            if (!success)
            {
                TempData["Error"] = message;
                if (survey.IsAnonymous)
                    return RedirectToAction("AlreadyFilled");
                return RedirectToAction("Fill", new { id = dto.SurveyId });
            }

            TempData["SuccessMessage"] = message;
            // Anonim anketlerde isAnonymous bilgisini Success sayfasına taşı
            if (survey.IsAnonymous)
                TempData["IsAnonymous"] = "true";

            // Anket gönderildikten sonra session'ı temizle — bir sonraki anket için tekrar login gerekecek
            if (!survey.IsAnonymous)
                ClearSessionData();

            return RedirectToAction("Success");
        }

        [HttpGet]
        public IActionResult Success() => View();

        // Anonim anketlerde "zaten doldurdunuz" sayfası — login gerekmez
        [HttpGet]
        public IActionResult AlreadyFilled() => View();

        // Anket bulunamadı / aktif değil — login gerekmez
        [HttpGet("SurveyResponse/NotFound_")]
        public IActionResult NotFound_() => View();

        // ── ACCESS TOKEN DOĞRULAMA ─────────────────────────
        /// <summary>
        /// Token'ı uzak PermissionService'e gönderip hâlâ geçerli olup olmadığını kontrol eder.
        /// Eski, süresi dolmuş veya geçersiz tokenlar için false döner.
        /// </summary>
        private async Task<bool> ValidateAccessTokenAsync(string accessToken)
        {
            try
            {
                return await _authServiceHandler.ValidateAuthServiceAsync(accessToken);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Geçersiz token tespit edildiğinde session verilerini temizler.
        /// </summary>
        private void ClearSessionData()
        {
            HttpContext.Session.Remove("AccessToken");
            HttpContext.Session.Remove("UserId");
            HttpContext.Session.Remove("UserFullName");
            HttpContext.Session.Remove("UserRole");
        }

        // ── IP ALMA YARDIMCI METODU ──────────────────────
        private string GetClientIp()
        {
            // Proxy/load balancer varsa X-Forwarded-For header'ına bak
            var forwarded = HttpContext.Request.Headers["X-Forwarded-For"]
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(forwarded))
                return forwarded.Split(',')[0].Trim();

            // Doğrudan bağlantı IP'si
            var ip = HttpContext.Connection.RemoteIpAddress;
            if (ip == null) return "unknown";

            // IPv6 loopback (::1) → IPv4'e çevir
            if (ip.IsIPv4MappedToIPv6)
                return ip.MapToIPv4().ToString();

            return ip.ToString();
        }
    }
}