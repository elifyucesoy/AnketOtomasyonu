namespace AnketOtomasyonu.Authorization.Models
{
    /// <summary>
    /// Çoklu izin kontrolünde VE/VEYA mantığı.
    /// And = Tüm izinlere sahip olmalı
    /// Or = En az birine sahip olmalı
    /// </summary>
    public enum Operations
    {
        And = 0,
        Or = 1
    }
}