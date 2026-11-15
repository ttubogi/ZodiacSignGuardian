using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ZodiacSignGuardian.Structure.Tests
{
    internal sealed class AsmdefInfo
    {
        public string Path = "";            // 저장소 기준 상대 경로
        public string Name = "";
        public string[] References = new string[0];
        public bool NoEngineReferences;
        public bool AutoReferenced = true;
        public bool OverrideReferences;
        public string[] PrecompiledReferences = new string[0];
        public string[] IncludePlatforms = new string[0];
        public string[] DefineConstraints = new string[0];

        public string Folder => System.IO.Path.GetDirectoryName(Path)!.Replace('\\', '/');

        public static AsmdefInfo Load(string fullPath)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(fullPath));
            var root = doc.RootElement;
            return new AsmdefInfo
            {
                Path = RepoPaths.Relative(fullPath),
                Name = root.GetProperty("name").GetString() ?? "",
                References = Strings(root, "references"),
                NoEngineReferences = Bool(root, "noEngineReferences", false),
                AutoReferenced = Bool(root, "autoReferenced", true),
                OverrideReferences = Bool(root, "overrideReferences", false),
                PrecompiledReferences = Strings(root, "precompiledReferences"),
                IncludePlatforms = Strings(root, "includePlatforms"),
                DefineConstraints = Strings(root, "defineConstraints"),
            };
        }

        static string[] Strings(JsonElement root, string key)
        {
            return root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.Array
                ? e.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                : new string[0];
        }

        static bool Bool(JsonElement root, string key, bool fallback)
        {
            return root.TryGetProperty(key, out var e) && (e.ValueKind == JsonValueKind.True || e.ValueKind == JsonValueKind.False)
                ? e.GetBoolean()
                : fallback;
        }

        public static List<AsmdefInfo> LoadAll(string repoRoot)
        {
            return Directory.GetFiles(System.IO.Path.Combine(repoRoot, "Assets"), "*.asmdef", SearchOption.AllDirectories)
                .OrderBy(p => p)
                .Select(Load)
                .ToList();
        }
    }

    /// <summary>
    /// 설계서 v0.7 4절 "코드 구조" 표를 그대로 옮긴 규칙.
    /// 이 표를 바꾸려면 설계서를 먼저 고치고 Claude·Codex가 함께 확인한다.
    /// </summary>
    internal static class AsmdefRules
    {
        internal sealed class Expected
        {
            public string Folder = "";
            public string[] ProjectReferences = new string[0];
            public bool MustBeEngineFree;
            public bool IsEditorTest;
        }

        const string P = "ZodiacSignGuardian.";

        internal static readonly Dictionary<string, Expected> Table = new Dictionary<string, Expected>
        {
            [P + "Data"] = new Expected { Folder = "Assets/Scripts/Data", MustBeEngineFree = true },
            [P + "Core"] = new Expected { Folder = "Assets/Scripts/Core", ProjectReferences = new[] { P + "Data" }, MustBeEngineFree = true },
            // Services는 표를 읽으므로 Data도 참조한다 (C0 검토, 2026-11-15)
            [P + "Services"] = new Expected { Folder = "Assets/Scripts/Services", ProjectReferences = new[] { P + "Core", P + "Data" } },
            [P + "View"] = new Expected { Folder = "Assets/Scripts/View", ProjectReferences = new[] { P + "Core" } },
            [P + "Core.Tests"] = new Expected { Folder = "Assets/Tests/Core", ProjectReferences = new[] { P + "Core", P + "Data" }, IsEditorTest = true },
        };

        /// <summary>테스트 어셈블리가 쓸 수 있는 Unity 쪽 참조.</summary>
        static readonly string[] UnityTestRunnerRefs = { "UnityEngine.TestRunner", "UnityEditor.TestRunner" };

        internal static List<string> Check(IReadOnlyList<AsmdefInfo> asmdefs)
        {
            var v = new List<string>();

            foreach (var dup in asmdefs.GroupBy(a => a.Name).Where(g => g.Count() > 1))
            {
                v.Add($"asmdef 이름 중복: {dup.Key} ({string.Join(", ", dup.Select(a => a.Path))})");
            }

            foreach (var name in Table.Keys.Where(n => asmdefs.All(a => a.Name != n)))
            {
                v.Add($"설계서에 있는 asmdef가 없음: {name}");
            }

            foreach (var a in asmdefs)
            {
                if (!Table.TryGetValue(a.Name, out var e))
                {
                    v.Add($"설계서에 없는 asmdef: {a.Name} ({a.Path}) — 설계서 4절을 먼저 고칠 것");
                    continue;
                }

                if (a.Folder != e.Folder)
                {
                    v.Add($"{a.Name}: 폴더가 {e.Folder}가 아니라 {a.Folder}");
                }

                var projectRefs = a.References.Where(r => r.StartsWith(P)).OrderBy(r => r).ToArray();
                var expectedRefs = e.ProjectReferences.OrderBy(r => r).ToArray();
                foreach (var extra in projectRefs.Except(expectedRefs))
                {
                    v.Add($"{a.Name}: 허용되지 않은 참조 {extra}");
                }
                foreach (var missing in expectedRefs.Except(projectRefs))
                {
                    v.Add($"{a.Name}: 필요한 참조 {missing}가 없음");
                }

                var otherRefs = a.References.Where(r => !r.StartsWith(P)).ToArray();
                if (e.MustBeEngineFree)
                {
                    if (!a.NoEngineReferences)
                    {
                        v.Add($"{a.Name}: noEngineReferences가 true여야 함 (판정은 Unity를 모른다)");
                    }
                    foreach (var r in otherRefs)
                    {
                        v.Add($"{a.Name}: 프로젝트 밖 어셈블리 {r} 참조 금지");
                    }
                    if (a.OverrideReferences || a.PrecompiledReferences.Length > 0)
                    {
                        v.Add($"{a.Name}: 미리 컴파일된 dll 참조 금지");
                    }
                }
                else if (e.IsEditorTest)
                {
                    foreach (var r in otherRefs.Except(UnityTestRunnerRefs))
                    {
                        v.Add($"{a.Name}: 테스트 러너 외 Unity 참조 {r}");
                    }
                    if (!a.DefineConstraints.Contains("UNITY_INCLUDE_TESTS"))
                    {
                        v.Add($"{a.Name}: defineConstraints에 UNITY_INCLUDE_TESTS가 없음 (출시 빌드에 테스트가 들어감)");
                    }
                    if (!(a.IncludePlatforms.Length == 1 && a.IncludePlatforms[0] == "Editor"))
                    {
                        v.Add($"{a.Name}: includePlatforms는 Editor 하나여야 함");
                    }
                    if (a.AutoReferenced)
                    {
                        v.Add($"{a.Name}: autoReferenced는 false여야 함");
                    }
                    if (!a.OverrideReferences || !a.PrecompiledReferences.Contains("nunit.framework.dll"))
                    {
                        v.Add($"{a.Name}: nunit.framework.dll을 명시적으로 참조해야 함");
                    }
                }
            }

            v.AddRange(FindCycles(asmdefs));
            return v;
        }

        static IEnumerable<string> FindCycles(IReadOnlyList<AsmdefInfo> asmdefs)
        {
            var graph = asmdefs.GroupBy(a => a.Name).ToDictionary(g => g.Key, g => g.First().References);
            var state = new Dictionary<string, int>(); // 0 미방문, 1 방문 중, 2 끝
            var cycles = new List<string>();

            void Visit(string node, Stack<string> path)
            {
                state[node] = 1;
                path.Push(node);
                foreach (var next in graph.TryGetValue(node, out var refs) ? refs : new string[0])
                {
                    if (!graph.ContainsKey(next)) continue;
                    state.TryGetValue(next, out var s);
                    if (s == 1)
                    {
                        cycles.Add("asmdef 순환 참조: " + string.Join(" → ", path.Reverse().Append(next)));
                    }
                    else if (s == 0)
                    {
                        Visit(next, path);
                    }
                }
                path.Pop();
                state[node] = 2;
            }

            foreach (var name in graph.Keys)
            {
                if (!state.ContainsKey(name)) Visit(name, new Stack<string>());
            }
            return cycles;
        }
    }
}
