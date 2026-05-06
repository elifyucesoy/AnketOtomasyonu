using AnketOtomasyonu.Models.ApiServices;

namespace AnketOtomasyonu.Services.Interfaces
{
    /// <summary>
    /// apiservices.selcuk.edu.tr Unit / UnitType listeleri — MemoryCache (30 gün),
    /// API çağrıları yalnızca cache boşken veya zorunlu yenilemede.
    /// </summary>
    public interface IUnitCatalogService
    {
        /// <summary>Cache dolu değilse verilen bearer ile API'den doldurur.</summary>
        Task EnsureCatalogAsync(string bearerToken, CancellationToken cancellationToken = default);

        /// <summary>Sistem kullanıcısı ile giriş yapıp katalogu zorla yeniler (senkron / arka plan).</summary>
        Task RefreshCatalogAsync(CancellationToken cancellationToken = default);

        IReadOnlyDictionary<int, ApiUnitItem> GetUnitsById();
        IReadOnlyList<ApiUnitTypeItem> GetUnitTypes();

        Task<IReadOnlyList<string>> GetAllUnitNamesAsync(CancellationToken cancellationToken = default);

        /// <summary>Fakülte/birim adına göre o birime bağlı bölüm adları (UnitType tablosu).</summary>
        Task<IReadOnlyList<string>> GetBolumNamesForUnitNameAsync(string unitName, CancellationToken cancellationToken = default);

        /// <summary>GetProfile unitIds + öğrenci/personel ayrımı ile görünen isimleri üretir.</summary>
        ResolvedLoginProfile ResolveLoginProfile(IReadOnlyList<int> unitIds, bool isStudent);
    }
}
