using AnketOtomasyonu.Models;

namespace AnketOtomasyonu.Services.Interfaces
{
    /// <summary>
    /// appsettings.json'dan birim (fakülte/birim) listesini yükler ve yardımcı metotlar sunar.
    /// </summary>
    public interface IBirimService
    {
        /// <summary>Tüm birimleri döner (Id + Name)</summary>
        List<BirimItem> GetAll();

        /// <summary>Sadece birim adlarını döner (alfabetik sıralı)</summary>
        List<string> GetAllNames();

        /// <summary>Id'ye göre birim adını döner; bulamazsa null</summary>
        string? GetNameById(int id);

        /// <summary>Birim adına göre Id döner; bulamazsa null</summary>
        int? GetIdByName(string name);
    }
}
