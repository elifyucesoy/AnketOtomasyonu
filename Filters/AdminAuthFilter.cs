using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AnketOtomasyonu.Filters
{
    /// <summary>
    /// Admin sayfaları için session tabanlı yetki kontrolü.
    /// [Authorize] + JWT pipeline'ından tamamen bağımsız çalışır;
    /// direkt session["UserRole"] ve session["AccessToken"] kontrol eder.
    ///
    /// Kullanım: [AdminAuth] — controller veya action üzerine koy.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class AdminAuthAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var session  = context.HttpContext.Session;
            var token    = session.GetString("AccessToken");
            var userRole = session.GetString("UserRole");

            // 1) Oturum açılmamış → Login sayfasına yönlendir
            if (string.IsNullOrEmpty(token))
            {
                var path = context.HttpContext.Request.Path
                         + context.HttpContext.Request.QueryString;
                var returnUrl = Uri.EscapeDataString(path);
                context.Result = new RedirectToActionResult(
                    "Login", "Auth", new { returnUrl });
                return;
            }

            // 2) Oturum açık ama Admin değil → Erişim reddedildi
            if (userRole != "Admin")
            {
                context.Result = new RedirectToActionResult(
                    "AccessDenied", "Auth", null);
            }
        }
    }
}
