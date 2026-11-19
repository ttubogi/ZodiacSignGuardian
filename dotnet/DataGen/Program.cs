using System;
using System.IO;
using System.Linq;
using System.Text;

namespace ZodiacSignGuardian.DataGen
{
    /// <summary>
    /// Tools/DataGen/stage_params.csv → Tables/csv/stages.csv.
    /// 인자: [check] [--params 파일] [--out 파일]
    /// check: 다시 만들어 저장소 파일과 글자가 같은지만 본다(다르면 1). 그다음 TableBuild로 검사·JSON 변환을 한다.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            var root = FindRepoRoot();
            var paramsPath = Arg(args, "--params") ?? Path.Combine(root, "Tools", "DataGen", "stage_params.csv");
            var outPath = Arg(args, "--out") ?? Path.Combine(root, "Tables", "csv", "stages.csv");
            var check = args.Contains("check");

            if (!File.Exists(paramsPath)) { Console.Error.WriteLine("입력 표가 없음: " + paramsPath); return 2; }
            if (!StageGen.TryGenerate(File.ReadAllText(paramsPath, Encoding.UTF8), out var text, out var error))
            {
                Console.Error.WriteLine("입력 표 오류: " + error);
                return 1;
            }
            if (check)
            {
                var same = File.Exists(outPath) && File.ReadAllText(outPath, Encoding.UTF8) == text;
                if (!same)
                {
                    Console.Error.WriteLine("stages.csv가 입력 표에서 만든 것과 다름 — 'dotnet run --project dotnet/DataGen'으로 다시 만드세요 (손으로 고치지 않음).");
                    return 1;
                }
                Console.WriteLine("stages.csv가 입력 표와 같음.");
                return 0;
            }
            File.WriteAllText(outPath, text, new UTF8Encoding(false));
            Console.WriteLine("stages.csv를 썼습니다: " + outPath);
            return 0;
        }

        static string? Arg(string[] args, string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) dir = dir.Parent;
            return dir?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
}
