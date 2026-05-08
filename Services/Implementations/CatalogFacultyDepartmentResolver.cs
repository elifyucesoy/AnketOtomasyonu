using AnketOtomasyonu.Helpers;
using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Services.Interfaces;

namespace AnketOtomasyonu.Services.Implementations
{
    public sealed class CatalogFacultyDepartmentResolver : ICatalogFacultyDepartmentResolver
    {
        private readonly IUnitApiService _unitApiService;

        public CatalogFacultyDepartmentResolver(IUnitApiService unitApiService)
        {
            _unitApiService = unitApiService;
        }

        /// <inheritdoc />
        public async Task<(int? UnitId, string? ReportingFacultyOrUnitName, string? DepartmentName)> ResolveAsync(
            IReadOnlyList<UnitDto> resolvedProfileUnits,
            string? personelBirimHint,
            string? fakulteFromProfile,
            string? bolumFromProfile,
            string? accessToken,
            IReadOnlyList<string>? extraNameHints = null)
        {
            var allUnits = await _unitApiService.GetAllUnitsAsync(accessToken);
            if (allUnits.Count == 0)
                return (null, null, null);

            var picked = PickUnitMatchingClaims(resolvedProfileUnits, personelBirimHint, fakulteFromProfile, bolumFromProfile);
            if (picked == null && resolvedProfileUnits.Count > 0)
                picked = resolvedProfileUnits[0];

            if (picked == null && extraNameHints != null)
            {
                foreach (var un in extraNameHints
                             .Select(s => s?.Trim())
                             .Where(s => !string.IsNullOrEmpty(s))
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    picked = MatchUnitInCatalog(allUnits, un);
                    if (picked != null) break;
                }
            }

            if (picked == null)
            {
                picked = MatchUnitInCatalog(allUnits, personelBirimHint)
                    ?? MatchUnitInCatalog(allUnits, fakulteFromProfile)
                    ?? MatchUnitInCatalog(allUnits, bolumFromProfile);
            }

            if (picked == null)
                return (null, null, null);

            var bolumClaim = string.IsNullOrWhiteSpace(bolumFromProfile) ? null : bolumFromProfile.Trim();

            if (LooksLikeFacultyUnit(picked))
            {
                var birim = picked.Name?.Trim();
                var bolum = string.IsNullOrWhiteSpace(bolumClaim) ? null : bolumClaim;
                return (picked.Id, birim, bolum);
            }

            var bolumOut = picked.Name?.Trim();
            if (string.IsNullOrWhiteSpace(bolumOut))
                bolumOut = string.IsNullOrWhiteSpace(bolumClaim) ? null : bolumClaim;

            var reporting = await FindFacultyReportingUnitAsync(picked, fakulteFromProfile, allUnits, accessToken);
            var birimOut = reporting?.Name?.Trim();
            if (string.IsNullOrWhiteSpace(birimOut))
                birimOut = fakulteFromProfile?.Trim();
            if (string.IsNullOrWhiteSpace(birimOut))
                birimOut = picked.Name?.Trim();

            return (picked.Id, birimOut, bolumOut);
        }

        private async Task<UnitDto?> FindFacultyReportingUnitAsync(
            UnitDto leaf,
            string? fakulteClaim,
            IReadOnlyList<UnitDto> allUnits,
            string? accessToken)
        {
            if (LooksLikeFacultyUnit(leaf))
                return leaf;

            if (!string.IsNullOrEmpty(fakulteClaim))
            {
                var fac = MatchUnitInCatalog(allUnits, fakulteClaim);
                if (fac != null)
                    return fac;
            }

            var current = leaf;
            for (var depth = 0; depth < 12; depth++)
            {
                var parent = await _unitApiService.GetParentUnitAsync(current.Id, accessToken);
                if (parent == null)
                    break;
                if (LooksLikeFacultyUnit(parent))
                    return parent;
                current = parent;
            }

            return await ElevateToReportingUnitAsync(leaf, fakulteClaim, allUnits, accessToken);
        }

        private async Task<UnitDto> ElevateToReportingUnitAsync(
            UnitDto start,
            string? fakulteClaim,
            IReadOnlyList<UnitDto> allUnits,
            string? accessToken)
        {
            if (LooksLikeFacultyUnit(start))
                return start;

            if (!string.IsNullOrEmpty(fakulteClaim))
            {
                var fac = MatchUnitInCatalog(allUnits, fakulteClaim);
                if (fac != null)
                    return fac;
            }

            var current = start;
            for (var depth = 0; depth < 8; depth++)
            {
                var parent = await _unitApiService.GetParentUnitAsync(current.Id, accessToken);
                if (parent == null)
                    break;

                if (!string.IsNullOrEmpty(fakulteClaim) &&
                    string.Equals(parent.Name?.Trim(), fakulteClaim.Trim(), StringComparison.OrdinalIgnoreCase))
                    return parent;

                if (LooksLikeFacultyUnit(parent))
                    return parent;

                current = parent;
            }

            return start;
        }

        private static UnitDto? PickUnitMatchingClaims(
            IReadOnlyList<UnitDto> units,
            string? personelBirim,
            string? fakulteAdi,
            string? bolumAdiClaim)
        {
            foreach (var candidate in new[] { personelBirim, fakulteAdi, bolumAdiClaim })
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                var m = MatchUnitInCatalog(units, candidate);
                if (m != null) return m;
            }

            return null;
        }

        private static UnitDto? MatchUnitInCatalog(IReadOnlyList<UnitDto> all, string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return null;
            var cTrim = candidate.Trim();
            var norm = SurveyUnitMatchHelper.NormalizeBirim(cTrim);
            foreach (var u in all)
            {
                if (string.Equals(u.Name?.Trim(), cTrim, StringComparison.OrdinalIgnoreCase))
                    return u;
            }

            foreach (var u in all)
            {
                if (SurveyUnitMatchHelper.NormalizeBirim(u.Name ?? "") == norm)
                    return u;
            }

            return null;
        }

        private static bool LooksLikeFacultyUnit(UnitDto u)
        {
            var type = u.UnitTypeName ?? "";
            var name = u.Name ?? "";
            if (type.Contains("Fakülte", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Contains("Fakültesi", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Contains("Fakultesi", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
