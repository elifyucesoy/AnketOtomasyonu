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
        private readonly IUnitApiService _unitApiService;
        private readonly ILogger<AkademikController> _logger;

        public AkademikController(
            ISurveyService surveyService,
            IUnitApiService unitApiService,
            ILogger<AkademikController> logger)
        {
            _surveyService = surveyService;
            _unitApiService = unitApiService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            var active = (await _surveyService.GetActiveSurveysAsync()).ToList();

            var token = User.FindFirstValue("AccessToken");
            var deptScope = await AkademikDepartmentScopeHelper.TryResolveAsync(User, _unitApiService, token, _logger);

            IEnumerable<Survey> surveys;
            if (deptScope.Resolved && (deptScope.UnitIds.Count > 0 || deptScope.NormalizedNames.Count > 0))
            {
                var filtered = new List<Survey>();
                foreach (var s in active)
                {
                    // Bölüm/fakülte hedefi olmayan “tamamen açık” kartları akademik listeye alma
                    if (AkademikDepartmentScopeHelper.SurveyHasNoUnitTargeting(s))
                        continue;

                    string? extraUnitName = null;
                    if (s.UnitId.HasValue && s.UnitId.Value > 0)
                    {
                        var u = await _unitApiService.GetUnitByIdAsync(s.UnitId.Value, token);
                        extraUnitName = u?.Name;
                    }

                    if (AkademikDepartmentScopeHelper.SurveyMatchesDepartmentScope(s, extraUnitName, deptScope))
                        filtered.Add(s);
                }

                surveys = filtered.OrderByDescending(x => x.CreatedAt);
            }
            else
            {
                // Bölüm API’si yok: yalnızca claim’deki fakülte/bölüm/personel birimi ile anket hedeflerini daralt
                var filtered = new List<Survey>();
                foreach (var s in active)
                {
                    string? extraUnitName = null;
                    if (s.UnitId.HasValue && s.UnitId.Value > 0)
                    {
                        var u = await _unitApiService.GetUnitByIdAsync(s.UnitId.Value, token);
                        extraUnitName = u?.Name;
                    }

                    if (AkademikDepartmentScopeHelper.SurveyMatchesAkademikClaimsFallback(s, extraUnitName, User))
                        filtered.Add(s);
                }

                surveys = filtered.OrderByDescending(x => x.CreatedAt);
            }

            ViewBag.UserBirim = User.FindFirstValue("PersonelBirim");

            return View(surveys.ToList());
        }
    }
}
