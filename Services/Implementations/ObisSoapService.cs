using System.Linq;
using System.Security;
using System.Text;
using System.Xml;
using AnketOtomasyonu.Configuration;
using AnketOtomasyonu.Models.Obis;
using AnketOtomasyonu.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace AnketOtomasyonu.Services.Implementations
{
    public sealed class ObisSoapService : IObisSoapService
    {
        private readonly HttpClient _http;
        private readonly IOptionsMonitor<ObisOptions> _options;
        private readonly ILogger<ObisSoapService> _logger;
        private readonly IConfiguration _configuration;

        public ObisSoapService(
            HttpClient http,
            IOptionsMonitor<ObisOptions> options,
            ILogger<ObisSoapService> logger,
            IConfiguration configuration)
        {
            _http = http;
            _options = options;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<ObisDersListResult> GetOgrenciDersleriAsync(
            string ogrNo, string parola, CancellationToken cancellationToken = default)
        {
            var opt = _options.CurrentValue;
            var token = _configuration["Obis:MerkeziToken"]; if (string.IsNullOrEmpty(token))
                token = opt.MerkeziToken?.Trim();

            if (string.IsNullOrEmpty(token))
            {
                return new ObisDersListResult
                {
                    Success = false,
                    ErrorMessage = "Sunucu yapılandırması eksik (merkezi token)."
                };
            }

            if (string.IsNullOrWhiteSpace(opt.Endpoint))
            {
                return new ObisDersListResult
                {
                    Success = false,
                    ErrorMessage = "Sunucu yapılandırması eksik (OBIS endpoint)."
                };
            }

            var ns = string.IsNullOrWhiteSpace(opt.SoapNamespace)
                ? "http://tempuri.org/"
                : opt.SoapNamespace.Trim();
            if (!ns.EndsWith('/'))
                ns += "/";

            // WSDL (obis.asmx?WSDL): elementFormDefault="qualified" — tüm gövde elemanları hedef namespace'te olmalı.
            // xmlns:tem + öneksiz <merkeziToken> çocukları boş namespace'te kalır; ASMX parametreleri görmez, boş Response döner.
            // Çözüm: kök istek elemanında varsayılan xmlns (PHP SoapClient ile aynı desen); çocuklar namespace'i miras alır.
            var soapAction = $"{ns.TrimEnd('/')}/OgrenciDersleriniGetir";
            var body = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <OgrenciDersleriniGetir xmlns=""{ns}"">
      <merkeziToken>{XmlEsc(token)}</merkeziToken>
      <ogrNo>{XmlEsc(ogrNo)}</ogrNo>
      <parola>{XmlEsc(parola)}</parola>
    </OgrenciDersleriniGetir>
  </soap:Body>
</soap:Envelope>";

            try
            {
                _logger.LogInformation("OBIS SOAP isteği gönderiliyor: Endpoint={Endpoint}, OgrNo={OgrNo}", opt.Endpoint, ogrNo);

                using var req = new HttpRequestMessage(HttpMethod.Post, opt.Endpoint);
                req.Content = new StringContent(body, Encoding.UTF8, "text/xml");
                req.Headers.TryAddWithoutValidation("SOAPAction", $"\"{soapAction}\"");

                using var resp = await _http.SendAsync(req, cancellationToken);
                var xml = await resp.Content.ReadAsStringAsync(cancellationToken);

                _logger.LogInformation("OBIS HTTP Status: {Code}, Response length: {Len}", (int)resp.StatusCode, xml?.Length ?? 0);

                // İlk 2000 karakter logla (debug için)
                if (!string.IsNullOrEmpty(xml))
                    _logger.LogDebug("OBIS Raw Response (ilk 2000): {Xml}", xml.Length > 2000 ? xml[..2000] : xml);

                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("OBIS HTTP hata: {Code}, Body: {Body}", (int)resp.StatusCode, xml?.Length > 500 ? xml[..500] : xml);
                    return new ObisDersListResult
                    {
                        Success = false,
                        ErrorMessage = "Servis geçici olarak yanıt veremedi. Lütfen sonra tekrar deneyin."
                    };
                }

                return ParseSoap(xml, ogrNo);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OBIS SOAP çağrısı başarısız");
                return new ObisDersListResult
                {
                    Success = false,
                    ErrorMessage = "Servis hatası. Lütfen sonra tekrar deneyin."
                };
            }
        }

        private static string XmlEsc(string? s) => SecurityElement.Escape(s ?? "") ?? "";

        private ObisDersListResult ParseSoap(string xml, string ogrNo)
        {
            var doc = new XmlDocument { XmlResolver = null };
            try
            {
                doc.LoadXml(xml);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OBIS yanıtı XML parse edilemedi");
                return Fail("Servis yanıtı işlenemedi.");
            }

            var fault = FindFirstByLocalName(doc.DocumentElement, "Fault");
            if (fault != null)
            {
                var reason = GetDescendantText(fault, "faultstring")
                    ?? GetDescendantText(fault, "FaultString")
                    ?? fault.InnerText?.Trim();
                if (!string.IsNullOrEmpty(reason))
                    _logger.LogWarning("OBIS SOAP Fault: {Reason}", reason);
                return Fail("Servis hatası. Lütfen sonra tekrar deneyin.");
            }

            var resultEl = FindDescendantByLocalName(doc.DocumentElement, "OgrenciDersleriniGetirResult")
                ?? FindDescendantByLocalName(doc.DocumentElement, "ogrenciDersleriniGetirResult");
            if (resultEl == null)
            {
                // Debug: hangi element'ler var?
                var allNames = new List<string>();
                foreach (XmlElement e in doc.DocumentElement!.GetElementsByTagName("*"))
                    allNames.Add(e.LocalName);
                _logger.LogWarning("OBIS: OgrenciDersleriniGetirResult bulunamadı. Mevcut element'ler: {Elements}",
                    string.Join(", ", allNames.Distinct().Take(30)));
                return Fail("Geçersiz öğrenci numarası veya şifre.");
            }

            _logger.LogInformation("OBIS: OgrenciDersleriniGetirResult bulundu. InnerXml length={Len}", resultEl.InnerXml?.Length ?? 0);

            var profile = ReadProfile(resultEl, ogrNo);
            var courseParents = FindAllDescendantsByLocalName(resultEl, "OgrenciDers");
            var courses = new List<ObisCourseRow>();
            foreach (var row in courseParents)
            {
                var c = ReadCourseRow(row);
                if (c != null)
                    courses.Add(c);
            }

            _logger.LogInformation("OBIS: OgrenciDers element sayısı={Raw}, parse edilen ders sayısı={Parsed}",
                courseParents.Count, courses.Count);

            if (courses.Count == 0)
            {
                // Belki aldigidersler altında farklı isimde element var
                var childNames = new List<string>();
                foreach (XmlElement e in resultEl.GetElementsByTagName("*"))
                    childNames.Add(e.LocalName);
                _logger.LogWarning("OBIS: 0 ders bulundu. Result altındaki element'ler: {Elements}",
                    string.Join(", ", childNames.Distinct().Take(30)));
                return Fail("Geçersiz öğrenci numarası veya şifre.");
            }

            profile.OgrNo = string.IsNullOrWhiteSpace(profile.OgrNo) ? ogrNo.Trim() : profile.OgrNo.Trim();

            return new ObisDersListResult
            {
                Success = true,
                Profile = profile,
                Courses = courses
            };
        }

        private static ObisDersListResult Fail(string msg) => new()
        {
            Success = false,
            ErrorMessage = msg
        };

        private static ObisStudentProfile ReadProfile(XmlElement resultRoot, string ogrNoFallback)
        {
            var p = new ObisStudentProfile { OgrNo = ogrNoFallback.Trim() };
            // WSDL (tns:ogrenci) alanları: ogrencifakulteadi, ogrencibolumadi, ogrenciadsoyad, …
            p.Ad = FirstTextByLocalNames(resultRoot, "OGRADI", "ogrAdi", "OgrAdi");
            p.Soyad = FirstTextByLocalNames(resultRoot, "OGRSOYADI", "ogrSoyadi", "OgrSoyadi");
            if (string.IsNullOrEmpty(p.Ad) && string.IsNullOrEmpty(p.Soyad))
            {
                var birlesik = FirstTextByLocalNames(resultRoot, "ogrenciadsoyad", "OgrenciAdSoyad");
                if (!string.IsNullOrEmpty(birlesik))
                {
                    var parcalar = birlesik.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    p.Ad = parcalar.ElementAtOrDefault(0);
                    p.Soyad = parcalar.Length > 1 ? parcalar[1] : null;
                }
            }

            p.FakulteKodu = FirstTextByLocalNames(resultRoot, "FAKULTEKODU", "fakulteKodu", "ogrencifakultekodu", "OgrenciFakulteKodu");
            p.FakulteAdi = FirstTextByLocalNames(resultRoot, "FAKULTEADI", "fakulteAdi", "ogrencifakulteadi", "OgrenciFakulteAdi");
            p.BolumKodu = FirstTextByLocalNames(resultRoot, "BOLUMKODU", "bolumKodu", "ogrencibolumkodu", "OgrenciBolumKodu");
            p.BolumAdi = FirstTextByLocalNames(resultRoot, "BOLUMADI", "bolumAdi", "ogrencibolumadi", "OgrenciBolumAdi");
            var oNo = FirstTextByLocalNames(resultRoot, "ogrencino", "OgrenciNo", "OGRNO", "ogrNo");
            if (!string.IsNullOrEmpty(oNo))
                p.OgrNo = oNo.Trim();
            return p;
        }

        private static ObisCourseRow? ReadCourseRow(XmlElement ogrenciDers)
        {
            var dersNo = FirstTextByLocalNames(ogrenciDers, "DERSNO", "dersNo", "DersNo");
            var adi = FirstTextByLocalNames(ogrenciDers, "ADI", "DERSADI", "dersAdi", "DersAdi");
            var yil = FirstTextByLocalNames(ogrenciDers, "YIL", "yil");
            var donem = FirstTextByLocalNames(ogrenciDers, "DONEM", "donem", "Donem");

            if (string.IsNullOrWhiteSpace(dersNo) && string.IsNullOrWhiteSpace(adi))
                return null;

            var key = $"{dersNo?.Trim() ?? ""}|{yil?.Trim() ?? ""}|{donem?.Trim() ?? ""}|{adi?.Trim() ?? ""}";
            key = key.Trim('|');
            if (string.IsNullOrEmpty(key))
                key = Guid.NewGuid().ToString("N");

            return new ObisCourseRow
            {
                Key = key,
                DersNo = dersNo?.Trim(),
                DersAdi = adi?.Trim(),
                Yil = yil?.Trim(),
                Donem = donem?.Trim()
            };
        }

        private static string? FirstTextByLocalNames(XmlElement root, params string[] localNames)
        {
            var set = new HashSet<string>(localNames, StringComparer.OrdinalIgnoreCase);
            foreach (XmlElement el in root.GetElementsByTagName("*"))
            {
                if (set.Contains(el.LocalName))
                {
                    var t = el.InnerText?.Trim();
                    if (!string.IsNullOrEmpty(t))
                        return t;
                }
            }

            return null;
        }

        private static string? GetDescendantText(XmlElement root, string localName)
        {
            foreach (XmlElement el in root.GetElementsByTagName("*"))
            {
                if (string.Equals(el.LocalName, localName, StringComparison.OrdinalIgnoreCase))
                    return el.InnerText?.Trim();
            }

            return null;
        }

        private static XmlElement? FindFirstByLocalName(XmlElement? root, string localName)
        {
            if (root == null) return null;
            if (string.Equals(root.LocalName, localName, StringComparison.OrdinalIgnoreCase))
                return root;
            foreach (XmlNode n in root.ChildNodes)
            {
                if (n is XmlElement e)
                {
                    var f = FindFirstByLocalName(e, localName);
                    if (f != null) return f;
                }
            }

            return null;
        }

        private static XmlElement? FindDescendantByLocalName(XmlElement? root, string localName)
        {
            if (root == null) return null;
            foreach (XmlElement el in root.GetElementsByTagName("*"))
            {
                if (string.Equals(el.LocalName, localName, StringComparison.OrdinalIgnoreCase))
                    return el;
            }

            return null;
        }

        private static List<XmlElement> FindAllDescendantsByLocalName(XmlElement? root, string localName)
        {
            var list = new List<XmlElement>();
            if (root == null) return list;
            foreach (XmlElement el in root.GetElementsByTagName("*"))
            {
                if (string.Equals(el.LocalName, localName, StringComparison.OrdinalIgnoreCase))
                    list.Add(el);
            }

            return list;
        }
    }
}
