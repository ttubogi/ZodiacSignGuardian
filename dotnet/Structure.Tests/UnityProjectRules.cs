using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ZodiacSignGuardian.Structure.Tests
{
    /// <summary>
    /// Unity 프로젝트 파일 규칙.
    /// - Assets 아래 모든 파일·폴더에 .meta가 있고, 짝 없는 .meta가 없다 (.meta가 빠지면 Unity가 새 GUID를 만들어 참조가 끊긴다)
    /// - .meta의 GUID는 겹치지 않는다
    /// - 에디터 버전은 Unity 6.3 LTS (설계서 결정: 개발·출시·운영을 한 버전으로)
    /// - Core.Tests가 쓰는 com.unity.test-framework가 패키지 목록에 있다
    /// </summary>
    internal static class UnityProjectRules
    {
        internal const string RequiredEditorPrefix = "6000.3.";
        internal const string TestFrameworkPackage = "com.unity.test-framework";

        /// <param name="entries">Assets 기준 상대 경로. 폴더는 끝에 '/'를 붙인다.</param>
        internal static List<string> CheckMetaPairs(IEnumerable<string> entries)
        {
            var v = new List<string>();
            var all = new HashSet<string>(entries);
            foreach (var e in all.Where(e => !e.EndsWith(".meta")))
            {
                var target = e.TrimEnd('/');
                if (!all.Contains(target + ".meta")) v.Add($".meta 없음: Assets/{target}");
            }
            foreach (var m in all.Where(e => e.EndsWith(".meta")))
            {
                var target = m.Substring(0, m.Length - ".meta".Length);
                if (!all.Contains(target) && !all.Contains(target + "/")) v.Add($"짝 없는 .meta: Assets/{m}");
            }
            return v;
        }

        static readonly Regex GuidLine = new Regex(@"^guid:\s*([0-9a-f]{32})\s*$", RegexOptions.Multiline);

        /// <param name="metas">.meta 경로 → 내용</param>
        internal static List<string> CheckGuids(IReadOnlyDictionary<string, string> metas)
        {
            var v = new List<string>();
            var seen = new Dictionary<string, string>();
            foreach (var pair in metas.OrderBy(p => p.Key))
            {
                var m = GuidLine.Match(pair.Value);
                if (!m.Success)
                {
                    v.Add($"GUID를 읽을 수 없음: {pair.Key}");
                    continue;
                }
                var guid = m.Groups[1].Value;
                if (seen.TryGetValue(guid, out var other)) v.Add($"GUID 겹침 {guid}: {other}, {pair.Key}");
                else seen[guid] = pair.Key;
            }
            return v;
        }

        internal static List<string> CheckEditorVersion(string projectVersionText)
        {
            var m = Regex.Match(projectVersionText, @"^m_EditorVersion:\s*(\S+)", RegexOptions.Multiline);
            if (!m.Success) return new List<string> { "ProjectVersion.txt에서 m_EditorVersion을 찾지 못함" };
            return m.Groups[1].Value.StartsWith(RequiredEditorPrefix)
                ? new List<string>()
                : new List<string> { $"에디터 버전 {m.Groups[1].Value}는 Unity 6.3 LTS({RequiredEditorPrefix}x)가 아님" };
        }

        internal static List<string> CheckManifest(string manifestText)
        {
            return manifestText.Contains($"\"{TestFrameworkPackage}\"")
                ? new List<string>()
                : new List<string> { $"Packages/manifest.json에 {TestFrameworkPackage}가 없음 (Core.Tests가 NUnit을 찾지 못함)" };
        }

        internal static List<string> AssetEntries(string repoRoot)
        {
            var assets = Path.Combine(repoRoot, "Assets");
            string Rel(string p) => Path.GetRelativePath(assets, p).Replace('\\', '/');
            return Directory.GetDirectories(assets, "*", SearchOption.AllDirectories).Select(d => Rel(d) + "/")
                .Concat(Directory.GetFiles(assets, "*", SearchOption.AllDirectories).Select(Rel))
                .ToList();
        }
    }
}
