using AnketOtomasyonu.Data;
using AnketOtomasyonu.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace AnketOtomasyonu.Controllers
{
    [AllowAnonymous]
    public class AuthController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;
        private readonly IWebHostEnvironment _env;
        private readonly ApplicationDbContext _db;

        public AuthController(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<AuthController> logger,
            IWebHostEnvironment env,
            ApplicationDbContext db)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
            _env = env;
            _db = db;
        }

        // ─── SADECE DEVELOPMENT ─────────────────────────────────────────────────
        [HttpGet]
        public IActionResult DevLogin()
        {
            if (!_env.IsDevelopment() || _configuration.GetValue<bool>("DevLogin:Enabled") == false)
                return NotFound();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DevLogin(string role)
        {
            if (!_env.IsDevelopment() || _configuration.GetValue<bool>("DevLogin:Enabled") == false)
                return NotFound();

            string sessionRole;
            int userTypeId;
            string userId = "9999";
            string personelBirim = "";
            string jobRecordType = "";
            string fakulteAdi = "";
            string bolumAdi = "";

            switch (role)
            {
                case "SuperAdmin":
                    sessionRole = "SuperAdmin";
                    userTypeId = 0;
                    personelBirim = "MERKEZ";
                    jobRecordType = "Akademik";
                    break;
                case "Admin":
                    sessionRole = "Admin";
                    userTypeId = 0;
                    userId = "8888";
                    personelBirim = "TEKNOLOJİ FAKÜLTESİ";
                    jobRecordType = "Akademik";
                    break;
                case "AkademikEmployee":
                    sessionRole = "Employee";
                    userTypeId = 0;
                    personelBirim = "BİLGİSAYAR MÜHENDİSLİĞİ";
                    jobRecordType = "Akademik";
                    break;
                case "Idari":
                    sessionRole = "Employee";
                    userTypeId = 0;
                    personelBirim = "PERSONEL DAİRESİ";
                    jobRecordType = "Idari";
                    break;
                default:
                    sessionRole = "Student";
                    userTypeId = 1;
                    fakulteAdi = "TEKNOLOJİ FAKÜLTESİ";
                    bolumAdi = "BİLGİSAYAR MÜHENDİSLİĞİ";
                    break;
            }

            var identity = CreateClaimsIdentity(userId, $"DevUser ({sessionRole})", sessionRole, userTypeId,
                personelBirim, jobRecordType, fakulteAdi, bolumAdi);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            _logger.LogWarning("[DEV-LOGIN] Cookie kuruldu → Role:{R} Birim:{B}", sessionRole, personelBirim);

            if (sessionRole == "SuperAdmin")
                return RedirectToAction("Dashboard", "SuperAdmin");
            if (sessionRole == "Admin")
                return RedirectToAction("Dashboard", "Admin");
            return RedirectToAction("Index", "Home");
        }
        // ────────────────────────────────────────────────────────────────────────

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            bool isSurveyFill = !string.IsNullOrEmpty(returnUrl)
                && returnUrl.Contains("SurveyResponse/Fill", StringComparison.OrdinalIgnoreCase);

            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                if (isSurveyFill)
                {
                    HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
                else
                {
                    if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                        return Redirect(returnUrl);
                    return RedirectToAction("Index", "Home");
                }
            }

            ViewBag.ReturnUrl = returnUrl;
            ViewBag.IsSurveyFill = isSurveyFill;
            return View(new LoginViewModel());
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            return View();
        }

        // ─── SHA256 HASH YARDIMCISI ─────────────────────────────────────────────
        private static string ComputeSha256(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            if (!ModelState.IsValid) return View(model);

            try
            {
                var user = model.Username.Trim();
                var pass = model.Password;
                var stajToken = "tekn0l0j1T00cken";

                bool isNumeric = user.All(char.IsDigit);

                ClaimsIdentity? identity = null;
                string userRole = "Student";
                bool isAdmin = false;

                // ── SUPERADMIN GİRİŞİ (appsettings'deki şifrelenmiş bilgi ile) ──
                var saUsername = _configuration["SuperAdmin:Username"] ?? "";
                var saHash = _configuration["SuperAdmin:PasswordHash"] ?? "";

                if (!isNumeric &&
                    string.Equals(user.Replace("@selcuk.edu.tr", ""), saUsername, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(saHash))
                {
                    if (ComputeSha256(pass) == saHash)
                    {
                        identity = CreateClaimsIdentity(
                            "superadmin_uid",
                            "SuperAdmin",
                            "SuperAdmin", 0,
                            "MERKEZ", "Akademik",
                            "MERKEZ", "");

                        _logger.LogWarning("[SuperAdmin] Başarılı giriş: {U}", saUsername);

                        var principal2 = new ClaimsPrincipal(identity);
                        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal2);

                        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                            return Redirect(returnUrl);
                        return RedirectToAction("Dashboard", "SuperAdmin");
                    }
                    else
                    {
                        ViewBag.Error = "Kullanıcı adı veya şifre hatalı.";
                        return View(model);
                    }
                }

                // ── YENİ AKIŞ: Önce Personel (tcDondurbySifreStaj) ──
                var mailKismi = user.Replace("@selcuk.edu.tr", "").Trim();
                var tcKimlik = await CallTcDondurBySifreStajAsync(mailKismi, pass, stajToken);

                bool isPersonnelSuccess = false;

                if (!string.IsNullOrEmpty(tcKimlik))
                {
                    // ── PERSONEL GİRİŞİ ─────────────────────────────────────
                    var per = await CallPersonnelProfileAsync(tcKimlik, stajToken);
                    if (per != null)
                    {
                        isPersonnelSuccess = true;
                        bool inYetkiTable = await _db.AdminPermissions
                            .AnyAsync(p => p.Username.ToLower() == mailKismi.ToLower() ||
                                           p.Username.ToLower() == (mailKismi + "@selcuk.edu.tr").ToLower());

                        if (inYetkiTable)
                        {
                            userRole = "Admin";
                            isAdmin = true;
                        }
                        else if (string.Equals(per.JobRecordType, "Akademik", StringComparison.OrdinalIgnoreCase))
                        {
                            userRole = "Akademik";
                        }
                        else
                        {
                            // İdari veya Diğer → anket doldurucu (Solvers)
                            userRole = "Employee";
                        }

                        identity = CreateClaimsIdentity(
                            per.TC,
                            $"{per.Ad} {per.Soyad}".Trim(),
                            userRole, 0,
                            per.PersonelBirim, per.JobRecordType,
                            per.PersonelBirim, "");

                        _logger.LogInformation("Personel giriş OK. TC:{TC} Rol:{R} Job:{J} Birim:{B}",
                            per.TC, userRole, per.JobRecordType, per.PersonelBirim);
                    }
                }

                if (!isPersonnelSuccess)
                {
                    // ── ÖĞRENCİ GİRİŞİ ──────────────────────────────────────
                    // "TC geldi mi? HAYIR (veya Profil Yok) -> ÖĞRENCİ"
                    var stu = await CallStudentAuthAsync(user.Trim(), pass, stajToken);
                    if (stu != null)
                    {
                        identity = CreateClaimsIdentity(
                            stu.OgrNo,
                            $"{stu.Ad} {stu.Soyad}".Trim(),
                            "Student", 1,
                            "", "", stu.FakulteAdi, stu.BolumAdi);

                        _logger.LogInformation("Öğrenci giriş OK. No:{N} Fak:{F} Böl:{B}",
                            stu.OgrNo, stu.FakulteAdi, stu.BolumAdi);
                    }
                }

                if (identity == null)
                {
                    ViewBag.Error = "Kullanıcı adı veya şifre hatalı.";
                    return View(model);
                }

                var principal = new ClaimsPrincipal(identity);
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);

                if (isAdmin)
                    return RedirectToAction("Dashboard", "Admin");

                if (userRole == "Akademik")
                    return RedirectToAction("Dashboard", "Akademik");

                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login esnasında sistemsel bir hata oluştu");
                ViewBag.Error = "Sistemsel bir hata oluştu. Lütfen daha sonra tekrar deneyin.";
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Auth");
        }

        // ─── CLAIMS ─────────────────────────────────────────────────────────────
        private ClaimsIdentity CreateClaimsIdentity(
            string userId, string fullName, string role, int userTypeId,
            string personelBirim, string jobRecordType,
            string fakulteAdi, string bolumAdi)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, fullName),
                new Claim(ClaimTypes.Role, role),
                new Claim("UserTypeId", userTypeId.ToString()),
                new Claim("PersonelBirim", personelBirim ?? ""),
                new Claim("JobRecordType", jobRecordType ?? ""),
                new Claim("FakulteAdi", fakulteAdi ?? ""),
                new Claim("BolumAdi", bolumAdi ?? "")
            };
            return new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        }

        // ─── ASMX SOAP METOTLARI ───────────────────────────────────────────────

        private async Task<string?> CallTcDondurBySifreStajAsync(string mail, string pass, string token)
        {
            string soapEnvelope = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <tcDondurbySifreStaj xmlns=""http://tempuri.org/"">
      <mail>{System.Security.SecurityElement.Escape(mail)}</mail>
      <sifre>{System.Security.SecurityElement.Escape(pass)}</sifre>
      <token>{System.Security.SecurityElement.Escape(token)}</token>
    </tcDondurbySifreStaj>
  </soap:Body>
</soap:Envelope>";

            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://restwebservis.selcuk.edu.tr/LDAPAuth.asmx");
            request.Headers.Add("SOAPAction", "\"http://tempuri.org/tcDondurbySifreStaj\"");
            request.Content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml");

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[LDAP-tcDondur] SOAP hata {S}", response.StatusCode);
                return null;
            }

            var xmlString = await response.Content.ReadAsStringAsync();
            var doc = XDocument.Parse(xmlString);
            var result = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "tcDondurbySifreStajResult")?.Value;
            
            return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
        }

        private async Task<PersonnelDetail?> CallPersonnelProfileAsync(string tc, string token)
        {
            string soapEnvelope = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <NetiketPersonelDondurStajyer xmlns=""http://tempuri.org/"">
      <tc>{System.Security.SecurityElement.Escape(tc)}</tc>
      <token>{System.Security.SecurityElement.Escape(token)}</token>
    </NetiketPersonelDondurStajyer>
  </soap:Body>
</soap:Envelope>";

            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://restwebservis.selcuk.edu.tr/kimlik.asmx");
            request.Headers.Add("SOAPAction", "\"http://tempuri.org/NetiketPersonelDondurStajyer\"");
            request.Content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml");

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Kimlik/Personel] SOAP hata {S}", response.StatusCode);
                return null;
            }

            var xmlString = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("[Kimlik/Personel] SOAP yanıtı: {Xml}", xmlString);
            var doc = XDocument.Parse(xmlString);
            var node = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "NetiketPersonelDondurStajyerResult");
            if (node == null) return null;

            string F(string name) => node.Elements()
                .FirstOrDefault(x => string.Equals(x.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value ?? "";

            return new PersonnelDetail
            {
                TC            = F("TCKIMLIK").Length > 0 ? F("TCKIMLIK") : tc,
                Ad            = F("AD"),
                Soyad         = F("SOYAD"),
                Unvan         = F("UNVAN"),
                JobRecordType = F("RECORDTIP"),
                PersonelBirim = F("NETBIRIM")
            };
        }

        private async Task<StudentDetail?> CallStudentAuthAsync(string no, string pass, string token)
        {
            string soapEnvelope = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <OgrenciSifreDogrulaStajyer xmlns=""http://tempuri.org/"">
      <token>{System.Security.SecurityElement.Escape(token)}</token>
      <numara>{System.Security.SecurityElement.Escape(no)}</numara>
      <parola>{System.Security.SecurityElement.Escape(pass)}</parola>
    </OgrenciSifreDogrulaStajyer>
  </soap:Body>
</soap:Envelope>";

            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://restwebservis.selcuk.edu.tr/kimlik.asmx");
            request.Headers.Add("SOAPAction", "\"http://tempuri.org/OgrenciSifreDogrulaStajyer\"");
            request.Content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml");

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Kimlik/Ogrenci] SOAP hata {S}", response.StatusCode);
                return null;
            }

            var xmlString = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("[Kimlik/Ogrenci] SOAP yanıtı: {Xml}", xmlString);
            var doc = XDocument.Parse(xmlString);

            var infoNode = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "YemekhaneLoginInfo");
            if (infoNode == null) return null;

            var hataKodu = infoNode.Elements().FirstOrDefault(x => x.Name.LocalName == "SonucHataKodu")?.Value;
            // "1" = Veri döndü (başarılı)
            if (string.IsNullOrEmpty(hataKodu) || hataKodu == "0")
                return null;

            string F(string name) => infoNode.Elements()
                .FirstOrDefault(x => string.Equals(x.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value ?? "";

            return new StudentDetail
            {
                OgrNo      = F("OGRNO").Length > 0 ? F("OGRNO") : no,
                Ad         = F("AD"),
                Soyad      = F("SOYAD"),
                FakulteAdi = F("FAKULTEADI"),
                BolumAdi   = F("BOLUMADI")
            };
        }

        private class PersonnelDetail
        {
            public string TC { get; set; } = "";
            public string Ad { get; set; } = "";
            public string Soyad { get; set; } = "";
            public string Unvan { get; set; } = "";
            public string JobRecordType { get; set; } = "";
            public string PersonelBirim { get; set; } = "";
        }
        private class StudentDetail
        {
            public string OgrNo { get; set; } = "";
            public string Ad { get; set; } = "";
            public string Soyad { get; set; } = "";
            public string FakulteAdi { get; set; } = "";
            public string BolumAdi { get; set; } = "";
        }
    }
}
