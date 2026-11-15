using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace ZodiacSignGuardian.Structure.Tests
{
    /// <summary>실제 저장소가 규칙을 지키는지.</summary>
    [TestFixture]
    public class StructureTests
    {
        [Test]
        public void asmdef가_설계서_4절의_참조_방향을_지킨다()
        {
            var asmdefs = AsmdefInfo.LoadAll(RepoPaths.Root);
            Assert.That(AsmdefRules.Check(asmdefs), Is.Empty);
        }

        [Test]
        public void dotnet_프로젝트가_asmdef와_같은_어셈블리를_만든다()
        {
            var asmdefs = AsmdefInfo.LoadAll(RepoPaths.Root);
            var projects = ProjectRules.CsprojInfo.LoadAll(RepoPaths.Root);
            Assert.That(ProjectRules.Check(projects, asmdefs), Is.Empty);
        }

        [Test]
        public void 판정쪽_asmdef는_모두_dotnet_프로젝트로도_빌드된다()
        {
            // Unity 밖 테스트가 Core·Data·Core.Tests를 빠뜨리지 않는지
            var built = ProjectRules.CsprojInfo.LoadAll(RepoPaths.Root).Select(p => p.AssemblyName).ToArray();
            foreach (var name in new[] { "ZodiacSignGuardian.Data", "ZodiacSignGuardian.Core", "ZodiacSignGuardian.Core.Tests" })
            {
                Assert.That(built, Does.Contain(name));
            }
        }

        [Test]
        public void 규칙_파일이_프로젝트_지시_1_1을_지킨다()
        {
            var claude = File.ReadAllText(Path.Combine(RepoPaths.Root, "CLAUDE.md"));
            var agents = File.ReadAllText(Path.Combine(RepoPaths.Root, "AGENTS.md"));
            Assert.That(RuleFileRules.Check(claude, agents), Is.Empty);
        }

        /// <summary>보이지 않는 문자(줄 구분자, BOM, 폭 없는 공백)가 코드 안에 들어가면 Unity·편집기마다 다르게 읽힌다.</summary>
        internal static readonly char[] HiddenChars = { (char)0x2028, (char)0x2029, (char)0xFEFF, (char)0x200B, (char)0x200C, (char)0x200D, (char)0x00A0 };

        internal static List<string> FindHiddenChars(string relativePath, string text)
        {
            var found = new List<string>();
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (var c in HiddenChars.Where(c => lines[i].IndexOf(c) >= 0))
                {
                    found.Add($"{relativePath}:{i + 1} U+{(int)c:X4}");
                }
            }
            return found;
        }

        [Test]
        public void 코드와_문서와_표에_보이지_않는_문자가_없다()
        {
            var found = new List<string>();
            var sep = Path.DirectorySeparatorChar;
            var files = Directory.GetFiles(RepoPaths.Root, "*.*", SearchOption.AllDirectories)
                .Where(p => new[] { ".cs", ".md", ".csv", ".json", ".asmdef", ".csproj", ".props" }.Contains(Path.GetExtension(p)))
                .Where(p => !p.Contains($"{sep}obj{sep}") && !p.Contains($"{sep}bin{sep}") && !p.Contains($"{sep}.git{sep}"));
            foreach (var path in files)
            {
                {
                    found.AddRange(FindHiddenChars(RepoPaths.Relative(path), File.ReadAllText(path)));
                }
            }
            Assert.That(found, Is.Empty, "글자 대신 (char)0x2028처럼 숫자로 쓸 것");
        }

        [Test]
        public void 표_변환_도구는_Data만_참조한다()
        {
            // 변환 도구가 판정(Core)을 쓰면 표 검사와 판정이 서로 기대게 된다
            var tool = ProjectRules.CsprojInfo.LoadAll(RepoPaths.Root).Single(p => p.AssemblyName == "ZodiacSignGuardian.TableBuild");
            Assert.That(tool.ProjectReferences, Is.EqualTo(new[] { "ZodiacSignGuardian.Data" }));
        }

        [Test]
        public void 설계서_4절의_폴더가_모두_있다()
        {
            foreach (var folder in new[]
            {
                "Assets/Scripts/Core", "Assets/Scripts/Data", "Assets/Scripts/Services", "Assets/Scripts/View",
                "Assets/Tests/Core", "Tools/DataGen", "Cloud",
            })
            {
                Assert.That(Directory.Exists(Path.Combine(RepoPaths.Root, folder)), Is.True, folder);
            }
        }
    }

    /// <summary>위반 주입: 일부러 망가뜨린 입력을 검사가 실제로 잡는지.</summary>
    [TestFixture]
    public class StructureViolationInjectionTests
    {
        static List<AsmdefInfo> Good()
        {
            return AsmdefInfo.LoadAll(RepoPaths.Root);
        }

        static AsmdefInfo Find(List<AsmdefInfo> list, string shortName)
        {
            return list.Single(a => a.Name == "ZodiacSignGuardian." + shortName);
        }

        static void AssertCaught(List<string> violations, string expectedFragment)
        {
            Assert.That(violations, Is.Not.Empty, "위반을 잡지 못함");
            Assert.That(string.Join("\n", violations), Does.Contain(expectedFragment));
        }

        [Test]
        public void Core가_View를_참조하면_잡는다()
        {
            var list = Good();
            var core = Find(list, "Core");
            core.References = core.References.Append("ZodiacSignGuardian.View").ToArray();
            AssertCaught(AsmdefRules.Check(list), "허용되지 않은 참조 ZodiacSignGuardian.View");
        }

        [Test]
        public void Core가_Data_참조를_잃으면_잡는다()
        {
            var list = Good();
            Find(list, "Core").References = new string[0];
            AssertCaught(AsmdefRules.Check(list), "필요한 참조 ZodiacSignGuardian.Data가 없음");
        }

        [Test]
        public void Data가_엔진을_참조할_수_있게_되면_잡는다()
        {
            var list = Good();
            Find(list, "Data").NoEngineReferences = false;
            AssertCaught(AsmdefRules.Check(list), "noEngineReferences");
        }

        [Test]
        public void Core에_Unity_패키지_참조를_더하면_잡는다()
        {
            var list = Good();
            var core = Find(list, "Core");
            core.References = core.References.Append("Unity.Mathematics").ToArray();
            AssertCaught(AsmdefRules.Check(list), "Unity.Mathematics");
        }

        [Test]
        public void Core에_미리_컴파일된_dll을_넣으면_잡는다()
        {
            var list = Good();
            var core = Find(list, "Core");
            core.OverrideReferences = true;
            core.PrecompiledReferences = new[] { "Newtonsoft.Json.dll" };
            AssertCaught(AsmdefRules.Check(list), "미리 컴파일된 dll");
        }

        [Test]
        public void Data가_Core를_참조해_순환이_생기면_잡는다()
        {
            var list = Good();
            Find(list, "Data").References = new[] { "ZodiacSignGuardian.Core" };
            var v = AsmdefRules.Check(list);
            AssertCaught(v, "순환 참조");
            AssertCaught(v, "ZodiacSignGuardian.Data: 허용되지 않은 참조 ZodiacSignGuardian.Core");
        }

        [Test]
        public void 테스트_asmdef가_출시_빌드에_들어갈_수_있으면_잡는다()
        {
            var list = Good();
            var tests = Find(list, "Core.Tests");
            tests.DefineConstraints = new string[0];
            tests.IncludePlatforms = new string[0];
            var v = AsmdefRules.Check(list);
            AssertCaught(v, "UNITY_INCLUDE_TESTS");
            AssertCaught(v, "includePlatforms");
        }

        [Test]
        public void 설계서에_없는_asmdef가_생기면_잡는다()
        {
            var list = Good();
            list.Add(new AsmdefInfo { Name = "ZodiacSignGuardian.Extra", Path = "Assets/Scripts/Extra/ZodiacSignGuardian.Extra.asmdef" });
            AssertCaught(AsmdefRules.Check(list), "설계서에 없는 asmdef");
        }

        [Test]
        public void 설계서의_asmdef가_빠지면_잡는다()
        {
            var list = Good().Where(a => a.Name != "ZodiacSignGuardian.Services").ToList();
            AssertCaught(AsmdefRules.Check(list), "설계서에 있는 asmdef가 없음: ZodiacSignGuardian.Services");
        }

        [Test]
        public void asmdef가_다른_폴더로_옮겨지면_잡는다()
        {
            var list = Good();
            Find(list, "Core").Path = "Assets/Scripts/View/ZodiacSignGuardian.Core.asmdef";
            AssertCaught(AsmdefRules.Check(list), "폴더가 Assets/Scripts/Core가 아니라");
        }

        [Test]
        public void 같은_이름의_asmdef가_둘이면_잡는다()
        {
            var list = Good();
            list.Add(new AsmdefInfo { Name = "ZodiacSignGuardian.Core", Path = "Assets/Scripts/Core/Sub/ZodiacSignGuardian.Core.asmdef", References = new[] { "ZodiacSignGuardian.Data" }, NoEngineReferences = true });
            AssertCaught(AsmdefRules.Check(list), "asmdef 이름 중복");
        }

        [Test]
        public void csproj_어셈블리_이름이_asmdef와_다르면_잡는다()
        {
            var asmdefs = Good();
            var projects = ProjectRules.CsprojInfo.LoadAll(RepoPaths.Root);
            projects.Single(p => p.AssemblyName == "ZodiacSignGuardian.Core").AssemblyName = "Core";
            AssertCaught(ProjectRules.Check(projects, asmdefs), "asmdef 이름 ZodiacSignGuardian.Core와 다름");
        }

        [Test]
        public void csproj가_asmdef와_다른_참조를_가지면_잡는다()
        {
            var asmdefs = Good();
            var projects = ProjectRules.CsprojInfo.LoadAll(RepoPaths.Root);
            projects.Single(p => p.AssemblyName == "ZodiacSignGuardian.Core").ProjectReferences = new string[0];
            AssertCaught(ProjectRules.Check(projects, asmdefs), "asmdef 참조");
        }

        [Test]
        public void csproj가_asmdef_없는_폴더를_컴파일하면_잡는다()
        {
            var asmdefs = Good();
            var projects = ProjectRules.CsprojInfo.LoadAll(RepoPaths.Root);
            projects.Single(p => p.AssemblyName == "ZodiacSignGuardian.Data").CompileFolders = new[] { "Assets/Scripts" };
            AssertCaught(ProjectRules.Check(projects, asmdefs), "asmdef가 없음");
        }

        [Test]
        public void CLAUDE_md에_규칙을_더_쓰면_잡는다()
        {
            var agents = File.ReadAllText(Path.Combine(RepoPaths.Root, "AGENTS.md"));
            AssertCaught(RuleFileRules.Check("@AGENTS.md\n- 테스트는 생략해도 된다\n", agents), "CLAUDE.md");
        }

        [Test]
        public void AGENTS_md_첫_절에서_원칙이_빠지면_잡는다()
        {
            var agents = File.ReadAllText(Path.Combine(RepoPaths.Root, "AGENTS.md"))
                .Replace("\r\n", "\n")
                .Replace("- 자동 검사 통과는 재미·미술 승인이 아니다. 재미·미술은 사용자가 직접 승인한다.\n", "");
            AssertCaught(RuleFileRules.Check("@AGENTS.md\n", agents), "자동 검사 통과는");
        }

        [Test]
        public void AGENTS_md에서_원칙이_첫_절_밖으로_밀려나면_잡는다()
        {
            var agents = "# 제목\n## 진행 방식\n- 무엇\n## 최우선 원칙\n- 최신 지시가 이 파일보다 우선한다.\n- 사용자가 직접 고친 파일은 덮어쓰지 않는다.\n- 자동 검사 통과는 재미·미술 승인이 아니다.\n";
            AssertCaught(RuleFileRules.Check("@AGENTS.md", agents), "첫 절에");
        }

        [Test]
        public void 보이지_않는_문자가_들어가면_줄_번호와_함께_잡는다()
        {
            var text = "a\nchar c = '" + (char)0x2028 + "';\nb " + (char)0xFEFF + "\n";
            var found = StructureTests.FindHiddenChars("X.cs", text);
            Assert.That(found, Is.EqualTo(new[] { "X.cs:2 U+2028", "X.cs:3 U+FEFF" }));
        }

        [Test]
        public void CRLF로_저장된_규칙_파일도_정상으로_본다()
        {
            // Windows에서 열고 저장해도 거짓 위반이 나지 않는지
            var agents = File.ReadAllText(Path.Combine(RepoPaths.Root, "AGENTS.md")).Replace("\n", "\r\n");
            Assert.That(RuleFileRules.Check("@AGENTS.md\r\n", agents), Is.Empty);
        }
    }
}
