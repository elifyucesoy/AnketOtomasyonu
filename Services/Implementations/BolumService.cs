using AnketOtomasyonu.Models;
using AnketOtomasyonu.Services.Interfaces;
using System.Globalization;

namespace AnketOtomasyonu.Services.Implementations
{
    /// <summary>
    /// appsettings.json → Bolumler dizisinden bölüm listesini yükler.
    /// Singleton olarak kayıt edilir — uygulama boyunca tek instance.
    /// </summary>
    public class BolumService : IBolumService
    {
        private readonly List<BolumItem> _bolumler;
        private readonly IBirimService _birimService;
        private static readonly CultureInfo TrCulture = new CultureInfo("tr-TR");

        public BolumService(IConfiguration configuration, IBirimService birimService)
        {
            _bolumler = new List<BolumItem>();
            configuration.GetSection("Bolumler").Bind(_bolumler);
            _birimService = birimService;
        }

        public List<BolumItem> GetAll() =>
            _bolumler.OrderBy(b => b.Name).ToList();

        public List<BolumItem> GetByBirimId(int birimId) =>
            _bolumler
                .Where(b => b.BirimId == birimId)
                .OrderBy(b => b.Name)
                .ToList();

        public List<BolumItem> GetByBirimName(string birimName)
        {
            if (string.IsNullOrWhiteSpace(birimName)) return new List<BolumItem>();

            var birimId = _birimService.GetIdByName(birimName.Trim());
            if (birimId == null) return new List<BolumItem>();

            return GetByBirimId(birimId.Value);
        }

        public List<string> GetNames(string? birimName = null)
        {
            if (string.IsNullOrWhiteSpace(birimName))
                return _bolumler.Select(b => b.Name).OrderBy(n => n).ToList();

            return GetByBirimName(birimName)
                .Select(b => b.Name)
                .OrderBy(n => n)
                .ToList();
        }
    }
}
