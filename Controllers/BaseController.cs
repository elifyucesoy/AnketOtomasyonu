using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AnketOtomasyonu.Controllers
{
    public abstract class BaseController : Controller
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);

            // Eğer istek X-Requested-With: XMLHttpRequest (AJAX) veya özel bir header ile gelmişse Layout'u null yaparız
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.ContainsKey("X-SPA-Request"))
            {
                ViewBag.IsSPA = true;
            }
        }

        // View dönerken SPA sinyalini header veya ViewBag ile veririz ama tam View döneriz 
        // çünkü Section'lar (Scripts, Styles) PartialView içinde çalışmaz.
        protected IActionResult SpaView(object? model = null)
        {
            return View(model);
        }

        protected IActionResult SpaView(string viewName, object? model = null)
        {
            return View(viewName, model);
        }
    }
}
