namespace AnketOtomasyonu.Models.Obis
{
    /// <summary>OBIS yanıtından çekilen öğrenci profil bilgileri</summary>
    public class ObisStudentProfile
    {
        public string OgrNo      { get; set; } = string.Empty;
        public string? Ad        { get; set; }
        public string? Soyad     { get; set; }
        public string? FakulteKodu { get; set; }
        public string? FakulteAdi  { get; set; }
        public string? BolumKodu   { get; set; }
        public string? BolumAdi    { get; set; }

        /// <summary>"Ad Soyad" — boşluklar temizlenir.</summary>
        public string FullName => $"{Ad} {Soyad}".Trim();
    }
}
