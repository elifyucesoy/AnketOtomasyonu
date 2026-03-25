using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Models.ViewModels;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;

namespace AnketOtomasyonu.Controllers
{
    [AllowAnonymous]
    public class SurveyResponseController : Controller
    {
        private readonly ISurveyService _surveyService;
        private readonly ISurveyResponseService _responseService;

        public SurveyResponseController(
            ISurveyService surveyService,
            ISurveyResponseService responseService)
        {
            _surveyService = surveyService;
            _responseService = responseService;
        }

        // GET /SurveyResponse/PublicSurveys
        [HttpGet]
        public async Task<IActionResult> PublicSurveys()
        {
            var surveys = await _surveyService.GetActiveAnonymousSurveysAsync();

            var vm = new SurveyIndexViewModel
            {
                UserFullName = User.FindFirstValue(ClaimTypes.Name),
                UserRole = User.FindFirstValue(ClaimTypes.Role),
                IsLoggedIn = User.Identity?.IsAuthenticated == true,
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

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (survey.IsAnonymous)
            {
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
                if (User.Identity == null || !User.Identity.IsAuthenticated)
                {
                    var returnUrl = Url.Action("Fill", "SurveyResponse", new { id });
                    return RedirectToAction("Login", "Auth", new { returnUrl });
                }

                // ── KULLANICI TİPİ KONTROLÜ ──
                var userType = User.FindFirstValue("UserTypeId") == "0" ? "Employee" : "Student";
                if (!string.IsNullOrEmpty(survey.TargetRoles) && !string.IsNullOrEmpty(userType))
                {
                    var allowedTypes = survey.TargetRoles
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (!allowedTypes.Contains(userType, StringComparer.OrdinalIgnoreCase))
                    {
                        TempData["Error"] = "Bu anket sizin kullanıcı tipinize açık değildir.";
                        return RedirectToAction("NotFound_", "SurveyResponse");
                    }
                }

                if (await _responseService.HasUserRespondedAsync(id, userId!))
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

            var survey = await _surveyService.GetSurveyWithQuestionsAsync(dto.SurveyId);
            if (survey == null)
            {
                TempData["Error"] = "Anket bulunamadı.";
                return RedirectToAction("NotFound_", "SurveyResponse");
            }

            string userId;

            if (survey.IsAnonymous)
            {
                userId = ip;
            }
            else
            {
                if (User.Identity == null || !User.Identity.IsAuthenticated)
                {
                    var returnUrl = Url.Action("Fill", "SurveyResponse", new { id = dto.SurveyId });
                    return RedirectToAction("Login", "Auth", new { returnUrl });
                }

                var userType = User.FindFirstValue("UserTypeId") == "0" ? "Employee" : "Student";
                if (!string.IsNullOrEmpty(survey.TargetRoles) && !string.IsNullOrEmpty(userType))
                {
                    var allowedTypes = survey.TargetRoles
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (!allowedTypes.Contains(userType, StringComparer.OrdinalIgnoreCase))
                    {
                        TempData["Error"] = "Bu anket sizin kullanıcı tipinize açık değildir.";
                        return RedirectToAction("NotFound_", "SurveyResponse");
                    }
                }

                userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
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
            if (survey.IsAnonymous)
                TempData["IsAnonymous"] = "true";

            // Anket gönderildikten sonra logout yap — bir sonraki anket için tekrar login gerekecek
            if (!survey.IsAnonymous)
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return RedirectToAction("Success");
        }

        [HttpGet]
        public IActionResult Success() => View();

        [HttpGet]
        public IActionResult AlreadyFilled() => View();

        [HttpGet("SurveyResponse/NotFound_")]
        public IActionResult NotFound_() => View();

        // ── IP ALMA YARDIMCI METODU ──────────────────────
        private string GetClientIp()
        {
            var forwarded = HttpContext.Request.Headers["X-Forwarded-For"]
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(forwarded))
                return forwarded.Split(',')[0].Trim();

            var ip = HttpContext.Connection.RemoteIpAddress;
            if (ip == null) return "unknown";

            if (ip.IsIPv4MappedToIPv6)
                return ip.MapToIPv4().ToString();

            return ip.ToString();
        }
    }
}