namespace AnketOtomasyonu.Models
{
    /// <summary>
    /// appsettings.json'daki Birimler listesindeki her bir birim kaydı.
    /// BirimDondur SOAP servisinden alınan verilerin ID'li hali.
    /// </summary>
    public class BirimItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
