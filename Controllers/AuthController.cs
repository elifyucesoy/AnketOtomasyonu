using AnketOtomasyonu.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
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

        public AuthController(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<AuthController> logger,
            IWebHostEnvironment env)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
            _env = env;
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

            switch (role)
            {
                case "SuperAdmin":
                    sessionRole = "SuperAdmin";
                    userTypeId = 0;
                    break;
                case "Admin":
                    sessionRole = "Admin";
                    userTypeId = 0;
                    userId = "8888"; // Fakülte Admini ID'si
                    break;
                case "Employee":
                    sessionRole = "Employee";
                    userTypeId = 0;
                    break;
                default: 
                    sessionRole = "Student";
                    userTypeId = 1;
                    break;
            }

            var identity = CreateClaimsIdentity(userId, $"DevUser ({sessionRole})", sessionRole, userTypeId);
            var principal = new ClaimsPrincipal(identity);
            
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            _logger.LogWarning("[DEV-LOGIN] Cookie Session kuruldu → Role:{R} Type:{T}", sessionRole, userTypeId);

            return (sessionRole == "Admin" || sessionRole == "SuperAdmin")
                ? RedirectToAction("Dashboard", "Admin")
                : RedirectToAction("Index", "Home");
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
                    // Farklı ankete giderken temizle
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
                string fullName = user;
                string userId = "0";
                bool isAdmin = false;
                int userTypeId = 1;

                if (isNumeric)
                {
                    // ÖĞRENCİ Girişi
                    var studentXml = await CallStudentAuthAsync(user, pass, stajToken);
                    if (studentXml != null) 
                    {
                        userId = studentXml.OgrNo;
                        fullName = $"{studentXml.Ad} {studentXml.Soyad}".Trim();
                        userRole = "Student";
                        userTypeId = 1;
                        identity = CreateClaimsIdentity(userId, fullName, userRole, userTypeId);
                        _logger.LogInformation("Öğrenci girişi başarılı. OgrNo: {OgrNo}", userId);
                    }
                }
                else
                {
                    // PERSONEL Girişi
                    var mailKismi = user.Replace("@selcuk.edu.tr", "");
                    var tcKimlik = await CallLdapAuthAsync(mailKismi, pass, stajToken);
                    
                    if (!string.IsNullOrEmpty(tcKimlik))
                    {
                        var personnelXml = await CallPersonnelProfileAsync(tcKimlik, stajToken);
                        if (personnelXml != null)
                        {
                            userId = personnelXml.TC; 
                            fullName = $"{personnelXml.Ad} {personnelXml.Soyad}".Trim();
                            userTypeId = 0;

                            // Admin kontrolü
                            var admins = _configuration.GetSection("FacultyAdmins").Get<string[]>() ?? Array.Empty<string>();
                            var superAdmins = _configuration.GetSection("SuperAdminUsernames").Get<string[]>() ?? Array.Empty<string>();
                            
                            if (superAdmins.Contains(mailKismi, StringComparer.OrdinalIgnoreCase))
                            {
                                userRole = "SuperAdmin";
                                isAdmin = true;
                            }
                            else if (admins.Contains(mailKismi, StringComparer.OrdinalIgnoreCase) || admins.Contains("*")) 
                            {
                                // Eğer appsettings'te FacultyAdmins: ["*"] ayarlıysa tüm personeller admin sayılır. Değilse sadece listedekiler.
                                userRole = "Admin";
                                isAdmin = true;
                            }
                            else
                            {
                                userRole = "Employee"; 
                                isAdmin = false;
                            }

                            identity = CreateClaimsIdentity(userId, fullName, userRole, userTypeId);
                            _logger.LogInformation("Personel girişi başarılı. TC: {TC}, Rol: {Rol}", userId, userRole);
                        }
                    }
                }

                if (identity == null)
                {
                    ViewBag.Error = "Kullanıcı adı veya şifre hatalı.";
                    return View(model);
                }

                // Cookie SignIn
                var principal = new ClaimsPrincipal(identity);
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);

                if (isAdmin)
                    return RedirectToAction("Dashboard", "Admin");

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

        private ClaimsIdentity CreateClaimsIdentity(string userId, string fullName, string role, int userTypeId)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, fullName),
                new Claim(ClaimTypes.Role, role),
                new Claim("UserTypeId", userTypeId.ToString())
            };
            return new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        }

        // ─── ASMX YARDIMCI METOTLARI ───

        private async Task<string?> CallLdapAuthAsync(string mail, string pass, string token)
        {
            string soapEnvelope = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <MailSifreStajToken xmlns=""http://tempuri.org/"">
      <mail>{System.Security.SecurityElement.Escape(mail)}</mail>
      <sifre>{System.Security.SecurityElement.Escape(pass)}</sifre>
      <token>{System.Security.SecurityElement.Escape(token)}</token>
    </MailSifreStajToken>
  </soap:Body>
</soap:Envelope>";

            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://restwebservis.selcuk.edu.tr/LDAPAuth.asmx");
            request.Headers.Add("SOAPAction", "\"http://tempuri.org/MailSifreStajToken\"");
            request.Content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml");

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("[LDAP] SOAP hata {Status}: {Body}", response.StatusCode, err);
                return null;
            }

            var xmlString = await response.Content.ReadAsStringAsync();
            var doc = XDocument.Parse(xmlString);
            var result = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "MailSifreStajTokenResult")?.Value;
            return string.IsNullOrWhiteSpace(result) ? null : result;
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
                var err = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("[Kimlik/Personel] SOAP hata {Status}: {Body}", response.StatusCode, err);
                return null;
            }

            var xmlString = await response.Content.ReadAsStringAsync();
            var doc = XDocument.Parse(xmlString);
            var resultNode = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "NetiketPersonelDondurStajyerResult");
            if (resultNode == null) return null;

            return new PersonnelDetail
            {
                TC = resultNode.Elements().FirstOrDefault(x => x.Name.LocalName == "TCKIMLIK")?.Value ?? tc,
                Ad = resultNode.Elements().FirstOrDefault(x => x.Name.LocalName == "AD")?.Value ?? "",
                Soyad = resultNode.Elements().FirstOrDefault(x => x.Name.LocalName == "SOYAD")?.Value ?? "",
                Unvan = resultNode.Elements().FirstOrDefault(x => x.Name.LocalName == "UNVAN")?.Value ?? ""
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
                var err = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("[Kimlik/Ogrenci] SOAP hata {Status}: {Body}", response.StatusCode, err);
                return null;
            }

            var xmlString = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("[Kimlik/Ogrenci] SOAP yanıtı: {Xml}", xmlString);
            var doc = XDocument.Parse(xmlString);

            var infoNode = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "YemekhaneLoginInfo");
            if (infoNode == null) return null;

            // SonucHataKodu: "0" = başarılı, diğerleri = hata
            var hataKodu = infoNode.Elements().FirstOrDefault(x => x.Name.LocalName == "SonucHataKodu")?.Value;
            if (!string.IsNullOrEmpty(hataKodu) && hataKodu != "0")
                return null;

            return new StudentDetail
            {
                OgrNo = infoNode.Elements().FirstOrDefault(x => x.Name.LocalName == "OGRNO")?.Value ?? no,
                Ad = infoNode.Elements().FirstOrDefault(x => x.Name.LocalName == "AD")?.Value ?? "",
                Soyad = infoNode.Elements().FirstOrDefault(x => x.Name.LocalName == "SOYAD")?.Value ?? "",
                Fakulte = infoNode.Elements().FirstOrDefault(x => x.Name.LocalName == "FAKULTEADI")?.Value ?? ""
            };
        }

        private class PersonnelDetail { public string TC{get;set;}="" ; public string Ad{get;set;}=""; public string Soyad{get;set;}=""; public string Unvan{get;set;}="" ; }
        private class StudentDetail { public string OgrNo{get;set;}=""; public string Ad{get;set;}=""; public string Soyad{get;set;}=""; public string Fakulte{get;set;}=""; }
    }
}