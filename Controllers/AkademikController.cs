using AnketOtomasyonu.Authorization;
using AnketOtomasyonu.Helpers;
using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AnketOtomasyonu.Controllers
{
    [Authorize(Policy = AnketPermissions.Akademik)]
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
            // Onaylı + aktif; birim eşlemesi Home / SurveyResponse ile aynı (çoklu UnitName, PersonelBirim, …).
            var surveys = (await _surveyService.GetActiveSurveysAsync())
                .Where(s => s.ApprovalStatus == ApprovalStatus.Approved)
                .Where(s => SurveyUnitMatchHelper.MatchesSurveyBirimStrings(User, s.CreatedByBirim, s.TargetFaculties))
                .OrderByDescending(s => s.CreatedAt)
                .ToList();

            ViewBag.UserBirim = User.FindFirstValue("PersonelBirim");

            return View(surveys);
        }
    }
}
