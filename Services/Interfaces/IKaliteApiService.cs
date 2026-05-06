namespace AnketOtomasyonu.Services.Interfaces
{
    /// <summary>
    /// Selçuk Üniversitesi Kalite API'sinden fakülte ve bölüm listelerini çeker.
    /// Sonuçlar önbellekte tutulur; API erişilemezse appsettings.json verileri kullanılır.
    /// </summary>
    public interface IKaliteApiService
    {
        /// <summary>Sadece akademik fakülte adlarını döner (API → appsettings fallback).</summary>
        Task<List<string>> GetFakulteNamesAsync();

        /// <summary>
        /// Tüm üniversite birimlerini döner: fakülteler, MYO'lar, yüksekokullar,
        /// enstitüler ve idari birimler dahil (~110 birim).
        /// API'den fakülteler çekilir; eksik idari/diğer birimler appsettings statik listesinden eklenir.
        /// </summary>
        Task<List<string>> GetAllBirimlerAsync();

        /// <summary>Belirtilen fakültenin bölüm adlarını döner.</summary>
        Task<List<string>> GetBolumNamesAsync(string fakulteAdi);
    }
}
