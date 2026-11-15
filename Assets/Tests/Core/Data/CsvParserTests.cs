using System.Linq;
using NUnit.Framework;
using ZodiacSignGuardian.Data.Csv;

namespace ZodiacSignGuardian.Core.Tests.Data
{
    [TestFixture]
    public class CsvParserTests
    {
        static string[][] Fields(string text)
        {
            var r = CsvParser.Parse(text);
            Assert.That(r.Error, Is.Null, r.Error);
            return r.Records.Select(x => x.Fields.ToArray()).ToArray();
        }

        [Test]
        public void 쉼표로_칸을_나눈다()
        {
            var f = Fields("a,b,c\n1,2,3\n");
            Assert.That(f.Length, Is.EqualTo(2));
            Assert.That(f[1], Is.EqualTo(new[] { "1", "2", "3" }));
        }

        [Test]
        public void 따옴표_안의_쉼표와_두_따옴표와_줄바꿈을_받는다()
        {
            var r = CsvParser.Parse("a,b\n\"x, y\",\"그가 \"\"안녕\"\"\"\n\"첫줄\r\n둘째줄\",z\nlast,row\n");
            Assert.That(r.Error, Is.Null);
            Assert.That(r.Records[1].Fields, Is.EqualTo(new[] { "x, y", "그가 \"안녕\"" }));
            Assert.That(r.Records[2].Fields[0], Is.EqualTo("첫줄\n둘째줄"));
            // 칸 안 줄바꿈 뒤의 행은 실제 줄 번호를 가진다
            Assert.That(r.Records[2].Line, Is.EqualTo(3));
            Assert.That(r.Records[3].Line, Is.EqualTo(5));
        }

        [Test]
        public void CRLF와_LF와_CR만_있는_줄바꿈이_같은_결과다()
        {
            var lf = Fields("a,b\n1,2\n");
            Assert.That(Fields("a,b\r\n1,2\r\n"), Is.EqualTo(lf));
            Assert.That(Fields("a,b\r1,2\r"), Is.EqualTo(lf));
        }

        [Test]
        public void 맨_앞_BOM을_버리고_마지막_줄바꿈이_없어도_된다()
        {
            Assert.That(Fields(((char)0xFEFF) + "a,b\n1,2"), Is.EqualTo(Fields("a,b\n1,2\n")));
        }

        [Test]
        public void 빈_줄은_행이_아니고_줄_번호는_유지된다()
        {
            var r = CsvParser.Parse("a\n\n1\n\n\n2\n");
            Assert.That(r.Records.Select(x => x.Line).ToArray(), Is.EqualTo(new[] { 1, 3, 6 }));
        }

        [Test]
        public void 빈_칸과_빈_따옴표_칸()
        {
            Assert.That(Fields(",,\n")[0], Is.EqualTo(new[] { "", "", "" }));
            Assert.That(Fields("\"\",x\n")[0], Is.EqualTo(new[] { "", "x" }));
        }

        [Test]
        public void 앞뒤_공백은_지우지_않는다()
        {
            Assert.That(Fields(" 5,a \n")[0], Is.EqualTo(new[] { " 5", "a " }));
        }

        [TestCase("a,\"bc\n", 1, "닫히지 않은")]
        [TestCase("a\nb,\"c\nd\n", 2, "닫히지 않은")]
        [TestCase("a,b\"c\n", 1, "따옴표로 감싸지 않은")]
        [TestCase("a\n\"x\"y,z\n", 2, "닫는 큰따옴표 뒤")]
        [TestCase("\"\"\"\n", 1, "닫히지 않은")]
        public void 잘못된_따옴표는_줄_번호와_함께_오류(string text, int line, string message)
        {
            var r = CsvParser.Parse(text);
            Assert.That(r.Error, Does.Contain(message));
            Assert.That(r.ErrorLine, Is.EqualTo(line));
        }
    }
}
