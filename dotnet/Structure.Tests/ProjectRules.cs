using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace ZodiacSignGuardian.Structure.Tests
{
    /// <summary>
    /// dotnet/ 아래 csproj가 Assets/의 asmdef와 같은 어셈블리를 만드는지 본다.
    /// 이름이 어긋나면 Unity와 dotnet에서 InternalsVisibleTo·참조 검사가 서로 다르게 동작한다.
    /// </summary>
    internal static class ProjectRules
    {
        internal sealed class CsprojInfo
        {
            public string Path = "";
            public string AssemblyName = "";
            public string[] CompileFolders = new string[0];  // 저장소 기준, 예: Assets/Scripts/Core
            public string[] ProjectReferences = new string[0]; // 참조하는 csproj의 AssemblyName

            public static CsprojInfo Load(string fullPath, IReadOnlyDictionary<string, string> assemblyNameByCsprojPath)
            {
                var doc = XDocument.Load(fullPath);
                var dir = System.IO.Path.GetDirectoryName(fullPath)!;
                return new CsprojInfo
                {
                    Path = RepoPaths.Relative(fullPath),
                    AssemblyName = doc.Descendants("AssemblyName").Select(e => e.Value).FirstOrDefault() ?? "",
                    CompileFolders = doc.Descendants("Compile")
                        .Select(e => (string?)e.Attribute("Include") ?? "")
                        .Where(i => i.StartsWith("$(RepoRoot)/"))
                        .Select(i => i.Substring("$(RepoRoot)/".Length))
                        .Select(i => i.EndsWith("/**/*.cs") ? i.Substring(0, i.Length - "/**/*.cs".Length) : i)
                        .ToArray(),
                    ProjectReferences = doc.Descendants("ProjectReference")
                        .Select(e => System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, (string?)e.Attribute("Include") ?? "")))
                        .Select(p => assemblyNameByCsprojPath.TryGetValue(p, out var n) ? n : "(없는 프로젝트) " + p)
                        .ToArray(),
                };
            }

            public static List<CsprojInfo> LoadAll(string repoRoot)
            {
                var files = Directory.GetFiles(System.IO.Path.Combine(repoRoot, "dotnet"), "*.csproj", SearchOption.AllDirectories)
                    .Where(p => !p.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}"))
                    .OrderBy(p => p)
                    .ToList();
                var names = files.ToDictionary(
                    p => System.IO.Path.GetFullPath(p),
                    p => XDocument.Load(p).Descendants("AssemblyName").Select(e => e.Value).FirstOrDefault() ?? "");
                return files.Select(p => Load(p, names)).ToList();
            }
        }

        internal static List<string> Check(IReadOnlyList<CsprojInfo> projects, IReadOnlyList<AsmdefInfo> asmdefs)
        {
            var v = new List<string>();
            var asmdefByFolder = asmdefs.GroupBy(a => a.Folder).ToDictionary(g => g.Key, g => g.First());
            var asmdefByName = asmdefs.GroupBy(a => a.Name).ToDictionary(g => g.Key, g => g.First());

            foreach (var p in projects)
            {
                foreach (var folder in p.CompileFolders)
                {
                    if (!asmdefByFolder.TryGetValue(folder, out var asmdef))
                    {
                        v.Add($"{p.Path}: 컴파일 폴더 {folder}에 asmdef가 없음");
                        continue;
                    }
                    if (asmdef.Name != p.AssemblyName)
                    {
                        v.Add($"{p.Path}: AssemblyName {p.AssemblyName}가 asmdef 이름 {asmdef.Name}와 다름");
                    }
                }

                // Unity 쪽 asmdef와 대응하는 프로젝트라면 참조도 같아야 한다 (테스트 프레임워크 패키지는 제외).
                if (p.CompileFolders.Length > 0 && asmdefByName.TryGetValue(p.AssemblyName, out var match))
                {
                    var asmdefRefs = match.References.Where(r => r.StartsWith("ZodiacSignGuardian.")).OrderBy(r => r);
                    var projRefs = p.ProjectReferences.OrderBy(r => r);
                    if (!asmdefRefs.SequenceEqual(projRefs))
                    {
                        v.Add($"{p.Path}: 참조 [{string.Join(", ", projRefs)}]가 asmdef 참조 [{string.Join(", ", asmdefRefs)}]와 다름");
                    }
                }
            }
            return v;
        }
    }
}
