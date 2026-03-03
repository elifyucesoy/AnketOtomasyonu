using Microsoft.AspNetCore.Mvc.Filters;

namespace AnketOtomasyonu.Filters
{
    /// <summary>
    /// Bu attribute uygulanan sayfalarda tarayıcı cache'i devre dışı bırakır.
    /// Geri tuşuna basınca tarayıcı cache'den değil sunucudan ister,
    /// sunucu da oturum kontrolü yaparak login'e yönlendirir.
    /// </summary>
    public class NoCacheAttribute : ActionFilterAttribute
    {
        public override void OnResultExecuting(ResultExecutingContext context)
        {
            context.HttpContext.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            context.HttpContext.Response.Headers["Pragma"] = "no-cache";
            context.HttpContext.Response.Headers["Expires"] = "0";
            base.OnResultExecuting(context);
        }
    }
}