namespace AnketOtomasyonu.Configuration
{
    /// <summary>appsettings.json → "Obis" bölümünden bağlanan SOAP servis ayarları</summary>
    public class ObisOptions
    {
        public const string Section = "Obis";

        /// <summary>SOAP endpoint URL'si. Örn: https://restwebservis.selcuk.edu.tr/obis.asmx</summary>
        public string Endpoint { get; set; } = "https://restwebservis.selcuk.edu.tr/obis.asmx";

        /// <summary>
        /// Merkezi token. Üretimde MERKEZI_TOKEN ortam değişkeninden okunur;
        /// yoksa burası kullanılır. Token asla istemciye gömülmez.
        /// </summary>
        public string? MerkeziToken { get; set; }

        /// <summary>SOAP namespace (xmlns). Örn: http://tempuri.org/</summary>
        public string SoapNamespace { get; set; } = "http://tempuri.org/";
    }
}
