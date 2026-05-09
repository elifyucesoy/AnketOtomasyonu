namespace AnketOtomasyonu.Models.Obis
{
    /// <summary>OgrenciDersleriniGetir SOAP operasyonu sonucu</summary>
    public class ObisDersListResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>Giriş başarılıysa öğrenci profili (Ad, Soyad, Fakülte, Bölüm…)</summary>
        public ObisStudentProfile Profile { get; set; } = new();

        /// <summary>Öğrencinin kayıtlı dersleri listesi</summary>
        public List<ObisCourseRow> Courses { get; set; } = new();
    }
}
