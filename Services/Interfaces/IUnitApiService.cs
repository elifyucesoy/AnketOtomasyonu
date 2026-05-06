using AnketOtomasyonu.Models.DTOs;

namespace AnketOtomasyonu.Services.Interfaces
{
    /// <summary>
    /// Selçuk Üniversitesi apiservices üzerinden birim (Unit) ve bölüm (UnitType) bilgilerini yönetir.
    /// Tüm veriler 30 günlük MemoryCache'de tutulur; API'ye gereksiz istek atılmaz.
    /// </summary>
    public interface IUnitApiService
    {
        /// <summary>Tüm birimleri döner (cache'den). Cache boşsa API'den çeker.</summary>
        Task<List<UnitDto>> GetAllUnitsAsync(string? bearerToken = null);

        /// <summary>Tüm bölüm tiplerini döner (cache'den). Cache boşsa API'den çeker.</summary>
        Task<List<UnitTypeDto>> GetAllUnitTypesAsync(string? bearerToken = null);

        /// <summary>Verilen unitId'lere ait birimleri döner (cache üzerinden).</summary>
        Task<List<UnitDto>> GetUnitsByIdsAsync(IEnumerable<int> unitIds, string? bearerToken = null);

        /// <summary>Tüm birim adlarını string listesi olarak döner (dropdown vb. için).</summary>
        Task<List<string>> GetAllUnitNamesAsync(string? bearerToken = null);

        /// <summary>
        /// Cache'i temizler ve API'den yeniden çeker.
        /// SuperAdmin "Senkronize Et" butonu ve aylık background job tarafından çağrılır.
        /// </summary>
        Task<(int unitCount, int unitTypeCount)> ForceRefreshAsync(string bearerToken);

        /// <summary>Cache doluluk bilgisi: true ise veriler cache'de mevcut.</summary>
        bool IsCached();
    }
}
