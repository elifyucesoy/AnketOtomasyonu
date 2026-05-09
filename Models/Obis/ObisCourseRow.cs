namespace AnketOtomasyonu.Models.Obis
{
    /// <summary>OBIS'ten gelen tek bir ders satırı</summary>
    public class ObisCourseRow
    {
        /// <summary>Benzersiz anahtar: "{DersNo}|{Yil}|{Donem}|{DersAdi}" biçiminde oluşturulur.</summary>
        public string Key { get; set; } = string.Empty;

        public string? DersNo  { get; set; }
        public string? DersAdi { get; set; }
        public string? Yil     { get; set; }
        public string? Donem   { get; set; }

        /// <summary>Kullanıcıya gösterilecek okunabilir satır: "MAT101 — Matematik — 2025"</summary>
        public string DisplayLine =>
            string.Join(" — ",
                new[] { DersNo, DersAdi, Yil }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
    }
}
