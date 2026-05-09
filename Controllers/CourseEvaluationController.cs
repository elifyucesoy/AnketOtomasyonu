using AnketOtomasyonu.Authorization;
using AnketOtomasyonu.Configuration;
using AnketOtomasyonu.Helpers;
using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Models.ViewModels;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AnketOtomasyonu.Controllers
{
    /// <summary>Ders değerlendirme anketi — yalnızca OBIS SOAP kimlik ve ders listesi akışı.</summary>
    [AllowAnonymous]
    public sealed class CourseEvaluationController : Controller
    {
        private readonly ISurveyService _surveyService;
        private readonly ISurveyResponseService _responseService;
        private readonly IObisSoapService _obisSoapService;
        private readonly IOptionsMonitor<CourseEvaluationOptions> _courseEvaluationOptions;

        public CourseEvaluationController(
            ISurveyService surveyService,
            ISurveyResponseService responseService,
            IObisSoapService obisSoapService,
            IOptionsMonitor<CourseEvaluationOptions> courseEvaluationOptions)
        {
            _surveyService = surveyService;
            _responseService = responseService;
            _obisSoapService = obisSoapService;
            _courseEvaluationOptions = courseEvaluationOptions;
        }

        [HttpGet]
        public async Task<IActionResult> CourseEvalLogin(int id)
        {
            var survey = await _surveyService.GetSurveyWithQuestionsAsync(id);
            if (survey == null)
            {
                TempData["Error"] = "Anket bulunamadı.";
                return RedirectToAction("Index", "Home");
            }

            if (survey.Status != SurveyStatus.Active && !User.HasAnketPermission(AnketPermissions.SuperAdmin))
            {
                TempData["Error"] = "Bu anket aktif değil.";
                return RedirectToAction("Index", "Home");
            }

            if (survey.ApprovalStatus != ApprovalStatus.Approved && !User.HasAnketPermission(AnketPermissions.SuperAdmin))
            {
                TempData["Error"] = "Bu anket henüz onaylanmadığı için katılıma açık değildir.";
                return RedirectToAction("Index", "Home");
            }

            if (!_courseEvaluationOptions.CurrentValue.UseObisParticipantFlow(survey))
                return RedirectToAction("Fill", "SurveyResponse", new { id });

            return View(new CourseEvalLoginViewModel
            {
                SurveyId = id,
                SurveyTitle = survey.Title
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CourseEvalLogin(int id, CourseEvalLoginViewModel model)
        {
            var survey = await _surveyService.GetSurveyWithQuestionsAsync(id);
            if (survey == null)
            {
                TempData["Error"] = "Anket bulunamadı.";
                return RedirectToAction("Index", "Home");
            }

            if (survey.Status != SurveyStatus.Active && !User.HasAnketPermission(AnketPermissions.SuperAdmin))
            {
                TempData["Error"] = "Bu anket aktif değil.";
                return RedirectToAction("Index", "Home");
            }

            if (survey.ApprovalStatus != ApprovalStatus.Approved && !User.HasAnketPermission(AnketPermissions.SuperAdmin))
            {
                TempData["Error"] = "Bu anket henüz onaylanmadığı için katılıma açık değildir.";
                return RedirectToAction("Index", "Home");
            }

            if (!_courseEvaluationOptions.CurrentValue.UseObisParticipantFlow(survey))
                return RedirectToAction("Fill", "SurveyResponse", new { id });

            model.SurveyId = id;
            model.SurveyTitle = survey.Title;
            if (!ModelState.IsValid)
                return View(model);

            var obis = await _obisSoapService.GetOgrenciDersleriAsync(model.OgrNo!.Trim(), model.Parola!);
            if (!obis.Success || obis.Courses.Count == 0)
            {
                ModelState.AddModelError(
                    string.Empty,
                    obis.ErrorMessage ?? "Geçersiz öğrenci numarası veya şifre.");
                return View(model);
            }

            var state = new CourseEvalSessionState
            {
                SurveyId = id,
                OgrNo = model.OgrNo.Trim(),
                Parola = model.Parola!,
                Profile = obis.Profile,
                Courses = obis.Courses,
                SelectedCourseKey = null
            };
            CourseEvalSessionHelper.Set(HttpContext.Session, state);

            return RedirectToAction(nameof(CourseEvalCourses), new { id });
        }

        [HttpGet]
        public async Task<IActionResult> CourseEvalCourses(int id)
        {
            var survey = await _surveyService.GetSurveyWithQuestionsAsync(id);
            if (survey == null)
            {
                TempData["Error"] = "Anket bulunamadı.";
                return RedirectToAction("Index", "Home");
            }

            if (!_courseEvaluationOptions.CurrentValue.UseObisParticipantFlow(survey))
                return RedirectToAction("Fill", "SurveyResponse", new { id });

            var st = CourseEvalSessionHelper.Get(HttpContext.Session);
            if (st == null || st.SurveyId != id)
                return RedirectToAction(nameof(CourseEvalLogin), new { id });

            var obis = await _obisSoapService.GetOgrenciDersleriAsync(st.OgrNo, st.Parola);
            if (!obis.Success || obis.Courses.Count == 0)
            {
                CourseEvalSessionHelper.Clear(HttpContext.Session);
                TempData["Error"] = obis.ErrorMessage ?? "Geçersiz öğrenci numarası veya şifre.";
                return RedirectToAction(nameof(CourseEvalLogin), new { id });
            }

            st.Profile = obis.Profile;
            st.Courses = obis.Courses;
            CourseEvalSessionHelper.Set(HttpContext.Session, st);

            var vm = new CourseEvalCoursesViewModel
            {
                SurveyId = id,
                SurveyTitle = survey.Title,
                OgrNo = st.OgrNo,
                StudentDisplayName = obis.Profile.FullName
            };

            foreach (var c in obis.Courses)
            {
                var uid = CourseEvalSessionHelper.BuildResponseUserId(st.OgrNo, c.Key);
                var done = await _responseService.HasUserRespondedAsync(id, uid);
                vm.Courses.Add(new CourseEvalCourseItemViewModel
                {
                    Key = c.Key,
                    DisplayLine = c.DisplayLine,
                    AlreadyResponded = done
                });
            }

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CourseEvalSelectCourse(int id, [FromForm] string? courseKey)
        {
            var survey = await _surveyService.GetSurveyWithQuestionsAsync(id);
            if (survey == null)
            {
                TempData["Error"] = "Anket bulunamadı.";
                return RedirectToAction("Index", "Home");
            }

            if (!_courseEvaluationOptions.CurrentValue.UseObisParticipantFlow(survey))
                return RedirectToAction("Fill", "SurveyResponse", new { id });

            var st = CourseEvalSessionHelper.Get(HttpContext.Session);
            if (st == null || st.SurveyId != id)
                return RedirectToAction(nameof(CourseEvalLogin), new { id });

            var key = courseKey?.Trim();
            if (string.IsNullOrEmpty(key))
            {
                TempData["Error"] = "Ders seçiniz.";
                return RedirectToAction(nameof(CourseEvalCourses), new { id });
            }

            var obis = await _obisSoapService.GetOgrenciDersleriAsync(st.OgrNo, st.Parola);
            if (!obis.Success || obis.Courses.Count == 0)
            {
                CourseEvalSessionHelper.Clear(HttpContext.Session);
                TempData["Error"] = obis.ErrorMessage ?? "Geçersiz öğrenci numarası veya şifre.";
                return RedirectToAction(nameof(CourseEvalLogin), new { id });
            }

            if (!obis.Courses.Any(c => string.Equals(c.Key, key, StringComparison.Ordinal)))
            {
                TempData["Error"] = "Seçilen ders güncel listede bulunamadı.";
                return RedirectToAction(nameof(CourseEvalCourses), new { id });
            }

            st.Profile = obis.Profile;
            st.Courses = obis.Courses;
            st.SelectedCourseKey = key;
            CourseEvalSessionHelper.Set(HttpContext.Session, st);

            return RedirectToAction("Fill", "SurveyResponse", new { id });
        }
    }
}
