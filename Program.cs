using AnketOtomasyonu.Authorization;
using AnketOtomasyonu.Authorization.Models;
using AnketOtomasyonu.Configuration;
using AnketOtomasyonu.Data;
using AnketOtomasyonu.Repositories.Implementations;
using AnketOtomasyonu.Repositories.Interfaces;
using AnketOtomasyonu.Services.Implementations;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
// SET GLOBAL CULTURE TO TURKISH
var cultureInfo = new System.Globalization.CultureInfo("tr-TR");
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

// SET TURKISH TIMEZONE (UTC+3)
// Tüm DateTime.Now çağrıları Türkiye saatini kullanacak şekilde ortam değişkeni ayarlanır
try
{
    var tzId = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
        System.Runtime.InteropServices.OSPlatform.Windows)
        ? "Turkey Standard Time"
        : "Europe/Istanbul";
    var turkeyTz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
    // Helper: Türkiye saati ile şimdiki zaman
    // Kullanım: TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, turkeyTz)
}
catch { /* timezone bulunamazsa atla */ }

// DATABASE
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// MVC
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options => {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });

// TURKISH CHARACTER SUPPORT
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(options =>
{
    options.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All);
});

// HTTP
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();

// SESSION
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// AUTH SERVICE HANDLER — uzak servis token doğrulama ve izin kontrolü
builder.Services.AddScoped<IAuthServiceHandler, AuthServiceHandler>();
builder.Services.AddScoped<IAuthorizationHandler, AuthServicePermissionHandler>();

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

// Policy → uzak HasPermission (AuthServicePermissionHandler)
builder.Services.AddAuthorization(options =>
{
    static void addSingle(AuthorizationOptions o, string policyName, string code) =>
        o.AddPolicy(policyName, p =>
            p.Requirements.Add(new AuthServiceRequirement(
                AnketPermissions.GroupCode, new List<string> { code }, Operations.Or)));

    addSingle(options, AnketPermissions.Student, AnketPermissions.Student);
    addSingle(options, AnketPermissions.Admin, AnketPermissions.Admin);
    addSingle(options, AnketPermissions.SuperAdmin, AnketPermissions.SuperAdmin);
    addSingle(options, AnketPermissions.Idari, AnketPermissions.Idari);
    addSingle(options, AnketPermissions.Akademik, AnketPermissions.Akademik);

    // Sonuç görüntüleme — anlamlı policy adları (HasPermission aynı kodlara gider)
    options.AddPolicy(AnketPermissions.PolicySurveyResultsEntry, p =>
        p.Requirements.Add(new AuthServiceRequirement(
            AnketPermissions.GroupCode,
            AnketPermissions.AllCodes.ToList(),
            Operations.Or)));

    addSingle(options, AnketPermissions.PolicySurveyResultsFullAccess, AnketPermissions.SuperAdmin);
    addSingle(options, AnketPermissions.PolicySurveyResultsUnitAdmin, AnketPermissions.Admin);

    // Birim Admin paneli — yalnızca ANKET_API_ADMIN (SuperAdmin ayrı route: /SuperAdmin)
    options.AddPolicy(AnketPermissions.PolicyAdminArea, p =>
        p.Requirements.Add(new AuthServiceRequirement(
            AnketPermissions.GroupCode,
            new List<string> { AnketPermissions.Admin },
            Operations.Or)));
});

// REPOSITORIES
builder.Services.AddScoped<ISurveyRepository, SurveyRepository>();
builder.Services.AddScoped<ISurveyResponseRepository, SurveyResponseRepository>();

// SERVICES
builder.Services.AddScoped<ISurveyService, SurveyService>();
builder.Services.AddScoped<ISurveyResponseService, SurveyResponseService>();
builder.Services.AddSingleton<IBirimService, BirimService>();     // appsettings fallback
builder.Services.AddSingleton<IBolumService, BolumService>();
builder.Services.AddScoped<IKaliteApiService, KaliteApiService>(); // Kalite API (fakülte + bölüm)
builder.Services.AddScoped<IUnitApiService, UnitApiService>();     // apiservices Unit + UnitType (7 gün cache)
builder.Services.AddScoped<ICatalogFacultyDepartmentResolver, CatalogFacultyDepartmentResolver>();

// ── OBIS SOAP SERVİSİ (Ders Değerlendirme Anketi) ─────────────────────────
builder.Services.Configure<ObisOptions>(builder.Configuration.GetSection(ObisOptions.Section));
builder.Services.Configure<CourseEvaluationOptions>(
    builder.Configuration.GetSection(CourseEvaluationOptions.Section));
// ObisSoapService için ayrı HttpClient — timeout ve SSL ayarları buradan yönetilir
builder.Services.AddHttpClient<IObisSoapService, ObisSoapService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    // Üretimde sertifika doğrulaması aktif; self-signed sertifika yoksa true bırakın
    ServerCertificateCustomValidationCallback =
        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
});

// System User — apiservices.selcuk.edu.tr auth (HasPermission vb.)
builder.Services.AddSingleton<IApiServicesAuthService, ApiServicesAuthService>();
// Eski UnitCatalogService kayıtlı kaldı; birim senkronu tek kaynak: UnitSyncBackgroundService + CachedUnits + UnitApiService DB önceliği
builder.Services.AddSingleton<IUnitCatalogService, UnitCatalogService>();

// BACKGROUND JOBS
builder.Services.AddHostedService<AnketOtomasyonu.Services.SurveyExpirationWorker>();         // Süresi dolan anketleri pasife al
builder.Services.AddHostedService<AnketOtomasyonu.Services.UnitSyncBackgroundService>();       // Haftalık System User → UnitList → CachedUnits (+ bellek önbelleği)

var app = builder.Build();

// Startup: CachedUnits ve Survey sütunlarını güvenli SQL ile oluştur/kontrol et
using (var startupScope = app.Services.CreateScope())
{
    var db = startupScope.ServiceProvider.GetRequiredService<AnketOtomasyonu.Data.ApplicationDbContext>();
    var logger = startupScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        // CachedUnits tablosu
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CachedUnits')
            CREATE TABLE CachedUnits (
                Id           INT           NOT NULL,
                Name         NVARCHAR(300) NOT NULL,
                ParentId     INT           NULL,
                UnitTypeId   INT           NULL,
                UnitTypeName NVARCHAR(200) NULL,
                IsActive     BIT           NOT NULL DEFAULT 1,
                LastSyncedAt DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
                CONSTRAINT PK_CachedUnits PRIMARY KEY (Id)
            )");

        // Survey tablosuna UnitId / UnitName sütunları
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Surveys' AND COLUMN_NAME='UnitId')
                ALTER TABLE Surveys ADD UnitId INT NULL;
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Surveys' AND COLUMN_NAME='UnitName')
                ALTER TABLE Surveys ADD UnitName NVARCHAR(300) NULL;
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='SurveyResponses' AND COLUMN_NAME='RespondentUnitId')
                ALTER TABLE SurveyResponses ADD RespondentUnitId INT NULL;
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='SurveyResponses' AND COLUMN_NAME='BirimAdi')
                ALTER TABLE SurveyResponses ADD BirimAdi NVARCHAR(300) NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SurveyResponses_SurveyId' AND object_id = OBJECT_ID(N'SurveyResponses'))
                CREATE NONCLUSTERED INDEX IX_SurveyResponses_SurveyId ON dbo.SurveyResponses(SurveyId);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Questions_SurveyId' AND object_id = OBJECT_ID(N'Questions'))
                CREATE NONCLUSTERED INDEX IX_Questions_SurveyId ON dbo.Questions(SurveyId);");

        logger.LogInformation("[Startup] DB şeması hazır.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[Startup] DB hazırlık hatası — uygulama çalışmaya devam ediyor.");
    }
}

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

app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var log = app.Services.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
            .CreateLogger("SuperAdminRoutes");
        foreach (var ds in app.Services.GetServices<Microsoft.AspNetCore.Routing.EndpointDataSource>())
        {
            foreach (var ep in ds.Endpoints)
            {
                if (ep is Microsoft.AspNetCore.Routing.RouteEndpoint re)
                {
                    var raw = re.RoutePattern.RawText ?? "";
                    if (raw.Contains("SuperAdmin", StringComparison.OrdinalIgnoreCase)
                        && raw.Contains("CreateSurvey", StringComparison.OrdinalIgnoreCase))
                        log.LogInformation("CreateSurvey route: {Pattern}", raw);
                }
            }
        }
    });
}

app.Run();