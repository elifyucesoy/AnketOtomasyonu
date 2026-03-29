using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AnketOtomasyonu.Controllers
{
    [Authorize(Roles = "Akademik")]
    public class AkademikController : Controller
    {
        private readonly ISurveyService _surveyService;

        public AkademikController(ISurveyService surveyService)
        {
            _surveyService = surveyService;
        }

        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            var personelBirim = User.FindFirstValue("PersonelBirim");

            if (string.IsNullOrEmpty(personelBirim))
            {
                // Fallback, if token misses unit
                return View(new List<Survey>());
            }

            // Fakültenin veya bölümün (kendi PersonelBirim) sadece "Onaylanmış/Yayınlanan" ve aktif anketlerini getir.
            var surveys = (await _surveyService.GetSurveysByBirimAsync(personelBirim))
                .Where(s => s.ApprovalStatus == ApprovalStatus.Approved && s.Status == SurveyStatus.Active)
                .OrderByDescending(s => s.CreatedAt)
                .ToList();

            ViewBag.UserBirim = personelBirim;

            return View(surveys);
        }
    }
}
