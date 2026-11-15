using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using ZodiacSignGuardian.Data.Json;
using ZodiacSignGuardian.Data.Tables;

namespace ZodiacSignGuardian.Core.Tests.Data
{
    [TestFixture]
    public class MiniJsonTests
    {
        static string Write(object v)
        {
            var sb = new StringBuilder();
            MiniJson.WriteValue(sb, v);
            return sb.ToString();
        }

        [Test]
        public void 문자열의_특수_문자를_이스케이프한다()
        {
            var s = "따옴표\" 역슬래시\\ 줄\n탭\t제어" + (char)1 + " 줄구분" + (char)0x2028;
            Assert.That(Write(s), Is.EqualTo("\"따옴표\\\" 역슬래시\\\\ 줄\\n탭\\t제어\\u0001 줄구분\\u2028\""));
            Assert.That(MiniJson.Parse(Write(s)), Is.EqualTo(s));
        }

        [Test]
        public void 한글은_이스케이프하지_않고_그대로_쓴다()
        {
            Assert.That(Write("별가루"), Is.EqualTo("\"별가루\""));
        }

        [Test]
        public void 정수_경계와_음수()
        {
            Assert.That(MiniJson.Parse("9223372036854775807"), Is.EqualTo(long.MaxValue));
            Assert.That(MiniJson.Parse("-9223372036854775808"), Is.EqualTo(long.MinValue));
            Assert.That(MiniJson.Parse("0"), Is.EqualTo(0L));
            Assert.That(Write(long.MinValue), Is.EqualTo("-9223372036854775808"));
        }

        [Test]
        public void 객체는_키_순서를_지킨다()
        {
            var o = (MiniJson.Obj)MiniJson.Parse("{\"b\": 1, \"a\": [true, null, \"x\"]}");
            Assert.That(o.Members.Select(m => m.Key).ToArray(), Is.EqualTo(new[] { "b", "a" }));
            Assert.That(Write(o), Is.EqualTo("{\"b\": 1, \"a\": [true, null, \"x\"]}"));
        }

        [TestCase("[1, 2,]")]
        [TestCase("{\"a\": 1,}")]
        [TestCase("1.5")]
        [TestCase("1e3")]
        [TestCase("01")]
        [TestCase("-")]
        [TestCase("9223372036854775808")]
        [TestCase("{\"a\": 1, \"a\": 2}")]
        [TestCase("\"닫히지 않음")]
        [TestCase("\"줄\n바꿈\"")]
        [TestCase("\"\\x\"")]
        [TestCase("\"\\u12\"")]
        [TestCase("{a: 1}")]
        [TestCase("[1] [2]")]
        [TestCase("")]
        [TestCase("tru")]
        public void 잘못된_JSON은_읽지_않는다(string json)
        {
            Assert.Throws<MiniJson.ParseException>(() => MiniJson.Parse(json));
        }

        [Test]
        public void 너무_깊은_중첩은_읽지_않는다()
        {
            Assert.Throws<MiniJson.ParseException>(() => MiniJson.Parse(new string('[', 40) + new string(']', 40)));
            Assert.DoesNotThrow(() => MiniJson.Parse(new string('[', 10) + new string(']', 10)));
        }
    }

    [TestFixture]
    public class TableJsonTests
    {
        static TableSet GoodSet()
        {
            var r = TableBuilder.BuildFromTexts(TestTables.Good());
            TestTables.AssertNone(r.Issues);
            return r.Tables;
        }

        [Test]
        public void 정해진_모양으로_쓴다()
        {
            var json = TableJson.Write(GoodSet()[Schemas.OfflineReward]);
            Assert.That(json, Is.EqualTo(
                "{\n" +
                "  \"table\": \"offline_reward\",\n" +
                "  \"columns\": [\"max_minutes\", \"min_minutes\", \"ad_multiplier\", \"max_frame_sec\"],\n" +
                "  \"rows\": [\n" +
                "    {\"max_minutes\": 720, \"min_minutes\": 1, \"ad_multiplier\": 2, \"max_frame_sec\": 1}\n" +
                "  ]\n" +
                "}\n"));
        }

        [Test]
        public void 빈_표와_빈_칸과_요일_목록()
        {
            var set = TableBuilder.BuildFromTexts(TestTables.Good().HeaderOnly("weekly_challenge")).Tables;
            Assert.That(TableJson.Write(set[Schemas.WeeklyChallenge]), Does.EndWith("\"rows\": []\n}\n"));
            var rules = TableJson.Write(set[Schemas.ResetRules]);
            Assert.That(rules, Does.Contain("\"weekday\": null"));
            Assert.That(TableJson.Write(set[Schemas.TimedEvents]), Does.Contain("\"weekdays\": [\"mon\", \"wed\"]"));
        }

        [Test]
        public void 모든_표가_JSON을_거쳐_같은_값으로_돌아온다()
        {
            var set = GoodSet();
            foreach (var table in set.Tables)
            {
                var issues = new List<TableIssue>();
                var back = TableJson.Read(table.Schema, TableJson.Write(table), issues);
                TestTables.AssertNone(issues);
                Assert.That(back.SameValues(table), Is.True, table.Schema.Name);
                Assert.That(TableJson.Write(back), Is.EqualTo(TableJson.Write(table)), table.Schema.Name + " 다시 쓴 글이 같음");
            }
        }

        [Test]
        public void 같은_CSV는_항상_같은_JSON이_된다()
        {
            var a = TableBuilder.BuildFromTexts(TestTables.Good()).Outputs;
            var b = TableBuilder.BuildFromTexts(TestTables.Good()).Outputs;
            Assert.That(a.Keys.OrderBy(k => k), Is.EqualTo(b.Keys.OrderBy(k => k)));
            foreach (var k in a.Keys) Assert.That(a[k], Is.EqualTo(b[k]), k);
        }

        static List<TableIssue> ReadBroken(TableSchema schema, string from, string to)
        {
            var json = TableJson.Write(GoodSet()[schema]);
            Assert.That(json.Contains(from), Is.True, "변형할 글이 없음: " + from);
            var issues = new List<TableIssue>();
            var table = TableJson.Read(schema, json.Replace(from, to), issues);
            Assert.That(table, Is.Null, "망가진 JSON을 표로 받아들임");
            return issues;
        }

        [TestCase("\"table\": \"stats\"", "\"table\": \"stars\"")]
        [TestCase("[\"stat_id\", \"name_ko\"", "[\"name_ko\", \"stat_id\"")]
        [TestCase("\"max_level\": 0, ", "")]
        [TestCase("\"max_level\": 0", "\"max_level\": \"0\"")]
        [TestCase("\"max_level\": 0", "\"max_level\": null")]
        [TestCase("\"max_level\": 0", "\"max_level\": -1")]
        [TestCase("\"build\": \"m1\"", "\"build\": \"m2\"")]
        [TestCase("\"stat_id\": \"attack\"", "\"stat_id\": \"Attack\"")]
        [TestCase("\"rows\": [", "\"rows\": {\"x\": [")]
        public void 모양이나_값이_스키마와_다른_JSON은_받지_않는다(string from, string to)
        {
            Assert.That(ReadBroken(Schemas.Stats, from, to), Is.Not.Empty);
        }

        [Test]
        public void JSON의_양의_정수_위반은_검사3으로_표시된다()
        {
            var issues = ReadBroken(Schemas.AdBuffs, "\"duration_min\": 30", "\"duration_min\": 0");
            Assert.That(issues.Single().Kind, Is.EqualTo(TableIssueKind.Check3));
        }

        [Test]
        public void JSON의_요일_목록이_틀리면_받지_않는다()
        {
            Assert.That(ReadBroken(Schemas.TimedEvents, "[\"mon\", \"wed\"]", "[\"mon\", \"mon\"]"), Is.Not.Empty);
            Assert.That(ReadBroken(Schemas.TimedEvents, "[\"mon\", \"wed\"]", "[]"), Is.Not.Empty);
            Assert.That(ReadBroken(Schemas.TimedEvents, "[\"mon\", \"wed\"]", "\"mon|wed\""), Is.Not.Empty);
        }

        [Test]
        public void JSON_묶음을_읽으면_검사를_다시_돈다()
        {
            var outputs = TableBuilder.BuildFromTexts(TestTables.Good()).Outputs;
            var issues = new List<TableIssue>();
            TableBuilder.LoadJson(outputs, issues);
            TestTables.AssertNone(issues);

            // JSON만 손으로 고쳐 검사를 피하려 해도 잡힌다 (검사 2)
            var tampered = new Dictionary<string, string>(outputs);
            tampered["daily_missions.json"] = tampered["daily_missions.json"].Replace("\"reward_currency\": \"gold\"", "\"reward_currency\": \"gem\"");
            issues.Clear();
            TableBuilder.LoadJson(tampered, issues);
            TestTables.AssertOnly(issues, TableIssueKind.Check2);
        }

        [Test]
        public void JSON_파일이_빠지면_알린다()
        {
            var outputs = TableBuilder.BuildFromTexts(TestTables.Good()).Outputs;
            outputs.Remove("boards.json");
            var issues = new List<TableIssue>();
            TableBuilder.LoadJson(outputs, issues);
            Assert.That(issues.Any(i => i.Table == "boards" && i.Message.Contains("JSON 파일이 없음")), Is.True);
        }
    }

    [TestFixture]
    public class TableBuilderTests
    {
        [Test]
        public void 통과하면_표_12개의_JSON을_낸다()
        {
            var r = TableBuilder.BuildFromTexts(TestTables.Good());
            Assert.That(r.Ok, Is.True, TestTables.Describe(r.Issues));
            Assert.That(r.Outputs.Keys.OrderBy(k => k).ToArray(), Is.EqualTo(Schemas.All.Select(s => s.Name + ".json").OrderBy(k => k).ToArray()));
        }

        [Test]
        public void 문제가_하나라도_있으면_JSON을_하나도_내지_않는다()
        {
            var r = TableBuilder.BuildFromTexts(TestTables.Good().Replace("stats", "attack,공격력,0,m1", "attack,공격력,-1,m1"));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Outputs, Is.Empty);
        }

        [Test]
        public void 문제_문구에_종류_파일_줄_열이_들어간다()
        {
            var issue = TableBuilder.BuildFromTexts(TestTables.Good().Replace("attendance", "1,currency,gold,50", "1,currency,gold,0")).Issues.Single();
            Assert.That(issue.ToString(), Does.StartWith("[검사3] attendance.csv 2줄 amount:"));
        }
    }
}
