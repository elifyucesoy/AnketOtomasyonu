using AnketOtomasyonu.Models.DTOs;

namespace AnketOtomasyonu.Services.Interfaces
{
    /// <summary>
    /// GetProfile → UnitList/UnitById ile çözülen birimlerden fakülte (raporlama) ve bölüm adlarını türetir.
    /// </summary>
    public interface ICatalogFacultyDepartmentResolver
    {
        /// <param name="resolvedProfileUnits">ResolveProfileUnitIdsAsync çıktısı (sıralı, öncelikli).</param>
        /// <param name="extraNameHints">İsteğe bağlı ek adlar (ör. cookie UnitName claim’leri).</param>
        /// <returns>
        /// <see cref="ValueTuple.Item1"/>: seçilen yaprak veya birim Id;
        /// Item2: fakülte/üst raporlama birimi adı (anket kaydındaki birim);
        /// Item3: bölüm adı (yaprak).
        /// </returns>
        Task<(int? UnitId, string? ReportingFacultyOrUnitName, string? DepartmentName)> ResolveAsync(
            IReadOnlyList<UnitDto> resolvedProfileUnits,
            string? personelBirimHint,
            string? fakulteFromProfile,
            string? bolumFromProfile,
            string? accessToken,
            IReadOnlyList<string>? extraNameHints = null);
    }
}
