using AnketOtomasyonu.Models;

namespace AnketOtomasyonu.Services.Interfaces
{
    /// <summary>
    /// appsettings.json'dan bölüm listesini yükler ve yardımcı metotlar sunar.
    /// </summary>
    public interface IBolumService
    {
        /// <summary>Tüm bölümleri döner.</summary>
        List<BolumItem> GetAll();

        /// <summary>Belirli bir birime (BirimId) ait bölümleri döner.</summary>
        List<BolumItem> GetByBirimId(int birimId);

        /// <summary>Birim adına göre bölümleri döner (büyük/küçük harf duyarsız).</summary>
        List<BolumItem> GetByBirimName(string birimName);

        /// <summary>Sadece bölüm adlarını döner (alfabetik). Birim adı verilmezse tümünü döner.</summary>
        List<string> GetNames(string? birimName = null);
    }
}
