using AnketOtomasyonu.Models;
using AnketOtomasyonu.Services.Interfaces;

namespace AnketOtomasyonu.Services.Implementations
{
    /// <summary>
    /// appsettings.json → Birimler dizisinden birim listesini yükler.
    /// Singleton olarak kayıt edilir — uygulama boyunca tek instance.
    /// </summary>
    public class BirimService : IBirimService
    {
        private readonly List<BirimItem> _birimler;
        private readonly Dictionary<int, string> _idToName;
        private readonly Dictionary<string, int> _nameToId;

        public BirimService(IConfiguration configuration)
        {
            _birimler = new List<BirimItem>();
            configuration.GetSection("Birimler").Bind(_birimler);

            _idToName = _birimler.ToDictionary(b => b.Id, b => b.Name);
            _nameToId = _birimler.ToDictionary(
                b => b.Name,
                b => b.Id,
                StringComparer.OrdinalIgnoreCase);
        }

        public List<BirimItem> GetAll() => _birimler.OrderBy(b => b.Name).ToList();

        public List<string> GetAllNames() => _birimler.Select(b => b.Name).OrderBy(n => n).ToList();

        public string? GetNameById(int id) => _idToName.TryGetValue(id, out var name) ? name : null;

        public int? GetIdByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return _nameToId.TryGetValue(name.Trim(), out var id) ? id : null;
        }
    }
}
