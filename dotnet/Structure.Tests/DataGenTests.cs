using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using NUnit.Framework;
using ZodiacSignGuardian.DataGen;

namespace ZodiacSignGuardian.Structure.Tests
{
    /// <summary>스테이지 표 생성 도구 (설계서 v0.12 4절). Core·Data를 참조하지 않고, 저장소의 stages.csv는 입력 표에서 만든 그대로다.</summary>
    [TestFixture]
    public class DataGenTests
    {
        static string ParamsPath => Path.Combine(RepoPaths.Root, "Tools", "DataGen", "stage_params.csv");
        static string StagesPath => Path.Combine(RepoPaths.Root, "Tables", "csv", "stages.csv");

        [Test]
        public void 생성_도구는_아무_프로젝트도_참조하지_않는다()
        {
            var tool = ProjectRules.CsprojInfo.LoadAll(RepoPaths.Root).Single(p => p.AssemblyName == "ZodiacSignGuardian.DataGen");
            Assert.That(tool.ProjectReferences, Is.Empty);
            Assert.That(tool.CompileFolders, Is.Empty, "Assets의 코드를 가져다 쓰지 않음");
        }

        [Test]
        public void 저장소의_stages_csv는_입력_표에서_다시_만든_것과_글자가_같다()
        {
            Assert.That(StageGen.TryGenerate(File.ReadAllText(ParamsPath, Encoding.UTF8), out var text, out var error), Is.True, error);
            Assert.That(File.ReadAllText(StagesPath, Encoding.UTF8), Is.EqualTo(text), "stages.csv를 손으로 고치지 말고 'dotnet run --project dotnet/DataGen'으로 만들 것");
        }

        [Test]
        public void 같은_입력이면_같은_글자()
        {
            var input = File.ReadAllText(ParamsPath, Encoding.UTF8);
            StageGen.TryGenerate(input, out var a, out _);
            StageGen.TryGenerate(input, out var b, out _);
            Assert.That(a, Is.EqualTo(b));
        }

        // 기대값은 파이썬 fractions로 따로 계산했다 (15자리, 절반은 올림)
        [TestCase("2", "3", "6.66666666666667e-1")]
        [TestCase("9999999999999995", "1000000000000000", "1e1")]
        [TestCase("1000000000000005", "1000000000000000", "1.00000000000001e0")]
        [TestCase("1000000000000004", "1000000000000000", "1e0")]
        [TestCase("123456789012345678", "1", "1.23456789012346e17")]
        [TestCase("5", "1000", "5e-3")]
        [TestCase("1", "1", "1e0")]
        public void 분수를_15자리로_한_번_반올림한다(string n, string d, string expected)
        {
            Assert.That(StageGen.Format(BigInteger.Parse(n), BigInteger.Parse(d)), Is.EqualTo(expected));
        }

        [Test]
        public void 거듭제곱은_정확히_계산한_뒤_반올림한다()
        {
            // 20 × 1.313^59, 2 × 1.16^59 × 12 (밸런스 기초 문서 4절 60번째 스테이지). 파이썬 fractions로 따로 계산한 값
            Assert.That(StageGen.Format(BigInteger.Pow(1313, 59) * 20, BigInteger.Pow(1000, 59)), Is.EqualTo("1.89954171324052e8"));
            Assert.That(StageGen.Format(BigInteger.Pow(116, 59) * 24, BigInteger.Pow(100, 59)), Is.EqualTo("1.52486924798271e5"));
        }

        [TestCase("1.313", "hp_ratio_num", Description = "소수는 받지 않음")]
        [TestCase("0", "hp_ratio_num")]
        [TestCase("-1", "chapters")]
        [TestCase("101", "chapters")]
        [TestCase("99999999999999999999", "hp_base")]
        [TestCase("Gold", "first_clear_currency")]
        public void 틀린_입력은_만들지_않는다(string value, string column)
        {
            var lines = File.ReadAllText(ParamsPath, Encoding.UTF8).Replace("\r\n", "\n").Split('\n');
            var header = lines[0].Split(',');
            var values = lines[1].Split(',');
            values[System.Array.IndexOf(header, column)] = value;
            var bad = lines[0] + "\n" + string.Join(",", values) + "\n";
            Assert.That(StageGen.TryGenerate(bad, out _, out var error), Is.False);
            Assert.That(error, Does.Contain(column));
        }
    }
}
