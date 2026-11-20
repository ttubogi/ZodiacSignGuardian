using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ZodiacSignGuardian.Data.Tables;

namespace ZodiacSignGuardian.TableBuild
{
    /// <summary>
    /// Tables/csv의 CSV 15개를 검사하고, 모두 통과하면 Assets/Resources/Tables에 JSON 15개를 쓴다.
    /// 하나라도 걸리면 아무것도 쓰지 않고 1로 끝난다.
    /// 출력 폴더가 Assets 안이면, 없는 .meta(폴더·JSON)만 새 GUID로 만든다. 있는 .meta는 건드리지 않는다.
    /// 인자: [check] [--csv 폴더] [--json 폴더]
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            bool checkOnly = args.Contains("check");
            string root = FindRepoRoot();
            string csvDir = Arg(args, "--csv") ?? Path.Combine(root, "Tables", "csv");
            string jsonDir = Arg(args, "--json") ?? Path.Combine(root, "Assets", "Resources", "Tables");

            if (!Directory.Exists(csvDir))
            {
                Console.Error.WriteLine("CSV 폴더가 없음: " + csvDir);
                return 2;
            }

            var files = Directory.GetFiles(csvDir, "*.csv")
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToDictionary(p => Path.GetFileName(p), File.ReadAllBytes);

            var result = TableBuilder.Build(files);
            if (!result.Ok)
            {
                foreach (var issue in result.Issues) Console.Error.WriteLine(issue);
                Console.Error.WriteLine($"문제 {result.Issues.Count}개. JSON을 쓰지 않았습니다.");
                return 1;
            }

            if (checkOnly)
            {
                var stale = result.Outputs
                    .Where(o => !File.Exists(Path.Combine(jsonDir, o.Key)) || File.ReadAllText(Path.Combine(jsonDir, o.Key), Encoding.UTF8) != o.Value)
                    .Select(o => o.Key)
                    .ToList();
                if (stale.Count > 0)
                {
                    Console.Error.WriteLine("CSV와 다른 JSON: " + string.Join(", ", stale) + " — 'dotnet run --project dotnet/TableBuild'로 다시 만드세요.");
                    return 1;
                }
                Console.WriteLine($"검사 통과, JSON {result.Outputs.Count}개가 CSV와 같음.");
                return 0;
            }

            Directory.CreateDirectory(jsonDir);
            // 모두 임시 파일로 먼저 쓰고 나서 바꿔 넣는다 (중간에 멈춰도 반쯤 바뀐 상태를 줄이기 위해)
            var temps = new List<(string temp, string target)>();
            foreach (var output in result.Outputs)
            {
                var target = Path.Combine(jsonDir, output.Key);
                var temp = target + ".tmp";
                File.WriteAllText(temp, output.Value, new UTF8Encoding(false));
                temps.Add((temp, target));
            }
            foreach (var (temp, target) in temps) File.Move(temp, target, overwrite: true);

            var createdMetas = UnityMeta.EnsureMetas(root, jsonDir, result.Outputs.Keys.Select(k => Path.Combine(jsonDir, k)));
            foreach (var meta in createdMetas) Console.WriteLine("새 .meta: " + Path.GetRelativePath(root, meta));

            Console.WriteLine($"검사 통과, JSON {result.Outputs.Count}개를 썼습니다: {jsonDir}");
            return 0;
        }

        static string? Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md")) && Directory.Exists(Path.Combine(dir.FullName, "Tables")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            return Directory.GetCurrentDirectory();
        }
    }
}
