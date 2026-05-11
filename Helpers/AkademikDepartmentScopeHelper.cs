using System.Security.Claims;
using Microsoft.Extensions.Logging;
using AnketOtomasyonu.Models.DTOs;
using AnketOtomasyonu.Models.Entities;
using AnketOtomasyonu.Services.Interfaces;

namespace AnketOtomasyonu.Helpers
{
    /// <summary>
    /// Yalnızca <see cref="Controllers.AkademikController"/> listesi: GetProfile <c>UnitId</c> zinciri +
    /// <c>UnitAllChildById</c> yanıtındaki alt birimlerden kullanıcının bölümünü çıkarır;
    /// anketin hedefleri bu bölümle (ID veya normalize ad) örtüşmüyorsa fakülte düzeyindeki geniş eşleşmeler listelenmez.
    /// </summary>
    public static class AkademikDepartmentScopeHelper
    {
        public sealed record DepartmentScope(bool Resolved, HashSet<int> UnitIds, HashSet<string> NormalizedNames);

        /// <summary>
        /// Token yoksa veya API başarısızsa <see cref="DepartmentScope.Resolved"/> false döner (çağıran eski filtreye düşer).
        /// </summary>
        public static async Task<DepartmentScope> TryResolveAsync(
            ClaimsPrincipal user,
            IUnitApiService unitApi,
            string? accessToken,
            ILogger? logger = null)
        {
            var profileIds = user.FindAll("UnitId")
                .Select(c => int.TryParse(c.Value, out var id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (profileIds.Count == 0)
                return new DepartmentScope(false, new HashSet<int>(), new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            var token = string.IsNullOrWhiteSpace(accessToken) ? null : accessToken;
            if (string.IsNullOrEmpty(token))
            {
                logger?.LogWarning("[AkademikDept] AccessToken yok; bölüm kapsamı atlanıyor.");
                return new DepartmentScope(false, new HashSet<int>(), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }

            var bolumRaw = user.FindFirstValue("BolumAdi");
            var bolumN = SurveyUnitMatchHelper.NormalizeBirim(bolumRaw);

            var deptIds = new HashSet<int>();
            var deptNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var treeCache = new Dictionary<int, UnitSubtreeScan?>();
            async Task<UnitSubtreeScan?> TreeAsync(int pid)
            {
                if (!treeCache.TryGetValue(pid, out var t))
                {
                    t = await unitApi.GetUnitAllChildrenSubtreeAsync(pid, token);
                    treeCache[pid] = t;
                }

                return t;
            }

            // 1) Profildeki birimlerden biri diğerinin altında mı? (ör. fakülte + bölüm id'leri)
            foreach (var pid in profileIds)
            {
                var tree = await TreeAsync(pid);
                if (tree == null || tree.AllIds.Count == 0)
                    continue;

                foreach (var q in profileIds)
                {
                    if (q != pid && tree.AllIds.Contains(q))
                        deptIds.Add(q);
                }
            }

            // 2) ParentId başka bir profile id ise → alt birim (bölüm) adayı
            foreach (var pid in profileIds)
            {
                var u = await unitApi.GetUnitByIdAsync(pid, token);
                if (u?.ParentId is int p && profileIds.Contains(p))
                    deptIds.Add(pid);
            }

            // 3) Bölüm adı ile eşleşen id'ler (tüm profile ağaçlarındaki isimler)
            if (!string.IsNullOrEmpty(bolumN))
            {
                foreach (var pid in profileIds)
                {
                    var tree = await TreeAsync(pid);
                    if (tree?.IdToNormalizedName == null)
                        continue;
                    foreach (var kv in tree.IdToNormalizedName)
                    {
                        if (kv.Value == bolumN)
                            deptIds.Add(kv.Key);
                    }
                }
            }

            foreach (var id in deptIds)
            {
                var u = await unitApi.GetUnitByIdAsync(id, token);
                if (!string.IsNullOrWhiteSpace(u?.Name))
                    deptNames.Add(SurveyUnitMatchHelper.NormalizeBirim(u.Name));
            }

            if (!string.IsNullOrEmpty(bolumN))
                deptNames.Add(bolumN);

            if (deptIds.Count == 0 && deptNames.Count == 0)
            {
                logger?.LogWarning("[AkademikDept] Bölüm id/ad çıkarılamadı (profil: {Ids}).", string.Join(",", profileIds));
                return new DepartmentScope(false, new HashSet<int>(), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }

            return new DepartmentScope(true, deptIds, deptNames);
        }

        /// <summary>
        /// Anket kullanıcının bölümüne göre mi hedeflenmiş? (fakülte geneli tek başına yetmez.)
        /// </summary>
        public static bool SurveyMatchesDepartmentScope(
            Survey survey,
            string? surveyExtraUnitNameFromApi,
            DepartmentScope scope)
        {
            if (!scope.Resolved || (scope.UnitIds.Count == 0 && scope.NormalizedNames.Count == 0))
                return false;

            foreach (var id in scope.UnitIds)
            {
                if (survey.UnitId == id)
                    return true;
            }

            bool RowMatches(string? csv)
            {
                if (string.IsNullOrWhiteSpace(csv)) return false;
                foreach (var raw in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var n = SurveyUnitMatchHelper.NormalizeBirim(raw);
                    if (!string.IsNullOrEmpty(n) && scope.NormalizedNames.Contains(n))
                        return true;
                }

                return false;
            }

            if (RowMatches(survey.TargetDepartments))
                return true;

            if (survey.TargetUnits != null)
            {
                foreach (var row in survey.TargetUnits)
                {
                    if (RowMatches(row.Birim))
                        return true;
                }
            }

            if (RowMatches(survey.UnitName))
                return true;

            var merged = SurveyFillAccessHelper.BuildAccessCsv(survey, surveyExtraUnitNameFromApi);
            return RowMatches(merged);
        }

        /// <summary>
        /// API/token yokken veya bölüm id çıkarılamazken: yalnızca oturumdaki FakulteAdi, BolumAdi, PersonelBirim
        /// metinleri ile anket hedef CSV’lerini eşleştirir (geniş UnitId zinciri kullanılmaz).
        /// </summary>
        public static bool SurveyMatchesAkademikClaimsFallback(
            Survey survey,
            string? surveyExtraUnitNameFromApi,
            ClaimsPrincipal user)
        {
            var fak = SurveyUnitMatchHelper.NormalizeBirim(user.FindFirstValue("FakulteAdi"));
            var bol = SurveyUnitMatchHelper.NormalizeBirim(user.FindFirstValue("BolumAdi"));
            var pb = SurveyUnitMatchHelper.NormalizeBirim(user.FindFirstValue("PersonelBirim"));

            var scopeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var x in new[] { fak, bol, pb })
            {
                if (!string.IsNullOrEmpty(x))
                    scopeNames.Add(x);
            }

            if (scopeNames.Count == 0)
                return false;

            var fake = new DepartmentScope(true, new HashSet<int>(), scopeNames);
            return SurveyMatchesDepartmentScope(survey, surveyExtraUnitNameFromApi, fake);
        }

        /// <summary>
        /// Hedef birim/satır hiç yoksa (tamamen açık iç kartlar) eski davranışa izin ver.
        /// </summary>
        public static bool SurveyHasNoUnitTargeting(Survey s) =>
            string.IsNullOrWhiteSpace(s.TargetFaculties)
            && string.IsNullOrWhiteSpace(s.TargetDepartments)
            && (s.TargetUnits == null || !s.TargetUnits.Any())
            && !s.UnitId.HasValue
            && string.IsNullOrWhiteSpace(s.UnitName);
    }
}
