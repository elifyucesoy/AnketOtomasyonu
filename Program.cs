using AnketOtomasyonu.Authorization;
using AnketOtomasyonu.Data;
using AnketOtomasyonu.Repositories.Implementations;
using AnketOtomasyonu.Repositories.Interfaces;
using AnketOtomasyonu.Services.Implementations;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// DATABASE
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// MVC
builder.Services.AddControllersWithViews();

// HTTP
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();

// SESSION
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// AUTH SERVICE HANDLER (Gereksiz eski yapı devre dışı, sadece IAuthService interface'leri bırakılabilir ama artık Cookie var)
// Cookie tabanlı auth için gerekli temizlik yapıldı.

// COOKIE AUTHENTICATION
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "AnketSonAuth";
        options.LoginPath = "/Auth/Login";
        options.AccessDeniedPath = "/Auth/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8); // Session gibi 8 saat
        options.SlidingExpiration = true;
    });

// YENİ ROLE-BASED YETKİLENDİRME (Claims)
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ANKET_API_STUDENT", policy =>
        policy.RequireRole("Student", "Employee", "Admin", "SuperAdmin"));

    options.AddPolicy("ANKET_API_ADMIN", policy =>
        policy.RequireRole("Admin", "SuperAdmin"));

    options.AddPolicy("ANKET_API_ADMIN_OR_ANKET_API_STUDENT", policy =>
        policy.RequireRole("Admin", "SuperAdmin", "Student", "Employee"));
});

// REPOSITORIES
builder.Services.AddScoped<ISurveyRepository, SurveyRepository>();
builder.Services.AddScoped<ISurveyResponseRepository, SurveyResponseRepository>();

// SERVICES
builder.Services.AddScoped<ISurveyService, SurveyService>();
builder.Services.AddScoped<ISurveyResponseService, SurveyResponseService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();        // ← Session middleware
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();