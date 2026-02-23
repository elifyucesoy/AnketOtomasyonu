using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Models.ViewModels;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnketOtomasyonu.Controllers
{
    [Authorize]
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

        [HttpGet]
        public async Task<IActionResult> Fill(int id)
        {
            var survey = await _surveyService.GetSurveyWithQuestionsAsync(id);
            if (survey == null)
            {
                TempData["Error"] = "Anket bulunamadı.";
                return RedirectToAction("Index", "Home");
            }

            if (survey.Status != SurveyStatus.Active)
            {
                TempData["Error"] = "Bu anket aktif değil.";
                return RedirectToAction("Index", "Home");
            }

            var userId = HttpContext.Session.GetString("UserId");

            // UserId yoksa veya "0" ise session bozulmuş — tekrar login
            if (string.IsNullOrEmpty(userId) || userId == "0")
                return RedirectToAction("Login", "Auth");

            // Anonim olmayan ankette tekrar doldurma kontrolü
            if (!survey.IsAnonymous &&
                await _responseService.HasUserRespondedAsync(id, userId))
            {
                TempData["Error"] = "Bu anketi zaten doldurdunuz.";
                return RedirectToAction("Index", "Home");
            }

            // Anonim ankette IP ile tekrar kontrolü
            if (survey.IsAnonymous)
            {
                var ip = GetClientIp();
                if (!string.IsNullOrEmpty(ip) &&
                    await _responseService.HasRespondedByIpAsync(id, ip))
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
            var userId = HttpContext.Session.GetString("UserId");

            if (string.IsNullOrEmpty(userId) || userId == "0")
                return RedirectToAction("Login", "Auth");

            var ip = GetClientIp();

            var (success, message) =
                await _responseService.SubmitResponseAsync(dto, userId, ip);

            if (!success)
            {
                TempData["Error"] = message;
                return RedirectToAction("Fill", new { id = dto.SurveyId });
            }

            TempData["SuccessMessage"] = message;
            return RedirectToAction("Success");
        }

        [HttpGet]
        public IActionResult Success() => View();

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