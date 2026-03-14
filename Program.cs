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

// AUTH SERVICE HANDLER
builder.Services.AddScoped<IAuthServiceHandler, AuthServiceHandler>();
builder.Services.AddScoped<IAuthorizationHandler, AuthServicePermissionHandler>();
// Admin paneli için session tabanlı yetki kontrolü (GetProfile.HasPermission)
builder.Services.AddScoped<IAuthorizationHandler, SessionAdminHandler>();

// ── HOCAMIN KODU — aynen, sadece OnMessageReceived + OnChallenge + OnForbidden eklendi ──
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "JwtBearer";
    options.DefaultChallengeScheme = "JwtBearer";
    options.DefaultScheme = "JwtBearer";
})
.AddJwtBearer("JwtBearer", options =>
{
    options.SaveToken = true;
    options.RequireHttpsMetadata = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = false,
        ValidateIssuerSigningKey = false,
        // "dev-test-token" geçerli JWT formatında değil; parse hatası → 401.
        // Development modda yapısal hata olursa minimal geçerli JWT döndürülür,
        // gerçek doğrulama OnTokenValidated'da yapılır.
        SignatureValidator = (token, parameters) =>
        {
            try { return new JsonWebToken(token); }
            catch
            {
                // eyJhbGciOiJub25lIn0.e30.dev  =  {"alg":"none"}.{}.dev
                return new JsonWebToken("eyJhbGciOiJub25lIn0.e30.dev");
            }
        }
    };

    options.Events = new JwtBearerEvents
    {
        // Session'dan token oku
        OnMessageReceived = context =>
        {
            // 1) Header'dan bak
            var header = context.Request.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(header))
            {
                context.Token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? header["Bearer ".Length..].Trim()
                    : header.Trim();
                return Task.CompletedTask;
            }

            // 2) Session'dan oku
            var sessionToken = context.HttpContext.Session.GetString("AccessToken");
            if (!string.IsNullOrWhiteSpace(sessionToken))
            {
                context.Token = sessionToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? sessionToken["Bearer ".Length..].Trim()
                    : sessionToken.Trim();
            }

            return Task.CompletedTask;
        },

        OnTokenValidated = async context =>
        {
            var serviceProvider = context.HttpContext.RequestServices;
            var logger = serviceProvider.GetRequiredService<ILogger<JwtBearerEvents>>();

            try
            {
                var accessToken = context.Request.Headers["Authorization"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(accessToken))
                    accessToken = context.HttpContext.Session.GetString("AccessToken");

                // Development: DevLogin sahte token → remote API'ye sorma, geç
                var env = serviceProvider.GetRequiredService<IWebHostEnvironment>();
                if (env.IsDevelopment() && accessToken?.Contains("dev-test-token") == true)
                {
                    logger.LogWarning("[DEV] Dev token algılandı, remote doğrulama atlandı.");
                    return;
                }

                if (!string.IsNullOrEmpty(accessToken))
                {
                    bool tokenIsValid = false;
                    try
                    {
                        var authHandler = serviceProvider.GetRequiredService<IAuthServiceHandler>();
                        tokenIsValid = await authHandler.ValidateAuthServiceAsync(accessToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error validating token with remote service");
                    }

                    if (!tokenIsValid)
                    {
                        logger.LogInformation("Remote service validation failed.");
                        context.Fail("Token validation failed");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in OnTokenValidated");
                context.Fail("Token validation failed");
            }
        },

       
        OnAuthenticationFailed = context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILogger<JwtBearerEvents>>();
            logger.LogError(context.Exception,
                "❌ Authentication FAILED: {Message}", context.Exception.Message);
            Console.WriteLine($"❌ AUTH FAILED: {context.Exception.Message}");
            return Task.CompletedTask;
        },

        // HOCAMIN KODU — aynen + MVC için Login yönlendirmesi
        OnChallenge = context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILogger<JwtBearerEvents>>();
            logger.LogWarning("🔒 Challenge: {Error} - {ErrorDescription}",
                context.Error, context.ErrorDescription);
            Console.WriteLine($"🔒 CHALLENGE: {context.Error} - {context.ErrorDescription}");

            if (!context.Request.Path.StartsWithSegments("/api"))
            {
                context.HandleResponse();
                var returnUrl = Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
                context.Response.Redirect($"/Auth/Login?returnUrl={returnUrl}");
            }

            return Task.CompletedTask;
        },

        OnForbidden = context =>
        {
            if (!context.Request.Path.StartsWithSegments("/api"))
                context.Response.Redirect("/Auth/AccessDenied");
            return Task.CompletedTask;
        }
    };
})
.AddJwtBearer("JwtSimple", options =>
{
  
    options.SaveToken = true;
    options.RequireHttpsMetadata = false;

    var secretKey = builder.Configuration["JwtSettings:SecretKey"];
    var issuer = builder.Configuration["JwtSettings:Issuer"];
    var audience = builder.Configuration["JwtSettings:Audience"];

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = !string.IsNullOrEmpty(issuer),
        ValidateAudience = !string.IsNullOrEmpty(audience),
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = issuer,
        ValidAudience = audience,
        IssuerSigningKey = !string.IsNullOrEmpty(secretKey)
            ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
            : null,
        ClockSkew = TimeSpan.Zero
    };

    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILogger<JwtBearerEvents>>();
            logger.LogError(context.Exception, "❌ Simple JWT Authentication FAILED");
            return Task.CompletedTask;
        }
    };
});

// HOCAMIN POLICY'LERİ — aynen + AnketAdmin eklendi
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ANKET_API_STUDENT", policy =>
        policy.Requirements.Add(new AuthServiceRequirement(
            "ANKET_API", ["ANKET_API_STUDENT"])));

    // Admin: GetProfile'dan gelen hasPermission=true → login'de session["UserRole"]="Admin" yazılır.
    // SessionAdminHandler bunu okur — permission API çağrısı YAPILMAZ.
    options.AddPolicy("ANKET_API_ADMIN", policy =>
        policy.Requirements.Add(new SessionAdminRequirement()));

    // Hem admin hem öğrenci/personel: permission API'den canlı kontrol.
    options.AddPolicy("ANKET_API_ADMIN_OR_ANKET_API_STUDENT", policy =>
        policy.Requirements.Add(new AuthServiceRequirement(
            "ANKET_API", ["ANKET_API_ADMIN", "ANKET_API_STUDENT"])));
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