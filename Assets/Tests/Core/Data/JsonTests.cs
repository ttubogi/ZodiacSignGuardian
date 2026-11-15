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
            Assert.That(table, Is.Not.Null, "Read는 null을 돌려주지 않는다 (깨진 표는 Complete=false)");
            Assert.That(issues, Is.Not.Empty, "망가진 JSON을 문제없이 받아들임");
            Assert.That(issues.All(i => i.Source == TableSource.Json), Is.True, "JSON 문제는 JSON 위치로 표시");
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

        static Dictionary<string, string> GoodJson()
        {
            return new Dictionary<string, string>(TableBuilder.BuildFromTexts(TestTables.Good()).Outputs);
        }

        static List<TableIssue> LoadBroken(Dictionary<string, string> json, string file, string from, string to)
        {
            Assert.That(json[file].Contains(from), Is.True, file + "에 '" + from + "'가 없음 (변형 실패)");
            json[file] = json[file].Replace(from, to);
            var r = TableBuilder.LoadJson(json);
            Assert.That(r.Ok, Is.False, "망가진 JSON 묶음을 받아들임");
            Assert.That(r.Tables, Is.Null, "문제가 있으면 표 묶음을 주지 않는다");
            return r.Issues.ToList();
        }

        [Test]
        public void JSON_묶음이_통과하면_표_묶음을_준다()
        {
            var r = TableBuilder.LoadJson(GoodJson());
            TestTables.AssertNone(r.Issues.ToList());
            Assert.That(r.Ok, Is.True);
            Assert.That(r.Tables, Is.Not.Null);
            Assert.That(r.Tables.Tables.Count(), Is.EqualTo(12));
        }

        [Test]
        public void JSON만_손으로_고쳐도_표_사이_검사에_걸린다()
        {
            var issues = LoadBroken(GoodJson(), "daily_missions.json", "\"reward_currency\": \"gold\"", "\"reward_currency\": \"gem\"");
            TestTables.AssertOnly(issues, TableIssueKind.Check2);
        }

        // ---- Codex 검토(2026-11-15)가 찾은 세 사례: CSV 경로에만 있던 검사 ----

        [Test]
        public void JSON_설정_표의_행이_0개면_잡는다()
        {
            var issues = LoadBroken(GoodJson(), "offline_reward.json",
                "\"rows\": [\n    {\"max_minutes\": 720, \"min_minutes\": 1, \"ad_multiplier\": 2, \"max_frame_sec\": 1}\n  ]", "\"rows\": []");
            TestTables.AssertOnly(issues, TableIssueKind.Format);
            Assert.That(issues.Single().Message, Does.Contain("정확히 1개"));
        }

        [Test]
        public void JSON_daily_규칙에_reset_hour가_없으면_잡는다()
        {
            var json = GoodJson();
            json["timed_events.json"] = "{\n  \"table\": \"timed_events\",\n  \"columns\": [\"event_id\", \"weekdays\", \"start_time\", \"end_time\", \"reward_currency\"],\n  \"rows\": []\n}\n";
            var issues = LoadBroken(json, "reset_rules.json", "\"period\": \"daily\", \"reset_hour\": 5", "\"period\": \"daily\", \"reset_hour\": null");
            TestTables.AssertOnly(issues, TableIssueKind.Format);
            Assert.That(issues.Single().Column, Is.EqualTo("reset_hour"));
            Assert.That(issues.Single().Message, Does.Contain("period=daily이면 값이 필요함"));
        }

        [Test]
        public void JSON_min_minutes가_max_minutes보다_크면_잡는다()
        {
            var issues = LoadBroken(GoodJson(), "offline_reward.json", "\"min_minutes\": 1", "\"min_minutes\": 721");
            TestTables.AssertOnly(issues, TableIssueKind.Format);
            Assert.That(issues.Single().Column, Is.EqualTo("min_minutes"));
        }

        // ---- 읽지 못한 칸이 공통 검사까지 가도 예외 없이 실패 (Codex 재검토 2026-11-15) ----
        // TableJson.Read만이 아니라 LoadJson 전체를 거쳐, 표 사이 검사가 그 칸을 건너뛰는지 본다.

        [TestCase("daily_missions.json", "\"reset_rule\": \"daily\"", "\"reset_rule\": 5", "reset_rule")]
        [TestCase("daily_missions.json", "\"reward_currency\": \"gold\"", "\"reward_currency\": [\"gold\"]", "reward_currency")]
        [TestCase("attendance.json", "\"reward_kind\": \"star\"", "\"reward_kind\": {\"x\": 1}", "reward_kind")]
        [TestCase("attendance.json", "\"day\": 2", "\"day\": \"2\"", "day")]
        [TestCase("reset_rules.json", "\"period\": \"daily\"", "\"period\": 1", "period")]
        [TestCase("reset_rules.json", "\"period\": \"cycle\"", "\"period\": true", "period")]
        [TestCase("reset_rules.json", "\"reset_hour\": 5, \"weekday\": \"mon\"", "\"reset_hour\": \"5\", \"weekday\": \"mon\"", "reset_hour")]
        [TestCase("boards.json", "\"slot_a\": 1", "\"slot_a\": null", "slot_a")]
        [TestCase("stars.json", "\"element\": \"fire\"", "\"element\": 3", "element")]
        public void 값_종류가_틀린_칸은_공통_검사까지_가도_형식_문제로만_실패한다(string file, string from, string to, string column)
        {
            List<TableIssue> issues = null;
            Assert.DoesNotThrow(() => issues = LoadBroken(GoodJson(), file, from, to));
            var table = file.Replace(".json", "");
            Assert.That(issues.Any(i => i.Table == table && i.Column == column), Is.True, TestTables.Describe(issues));
            Assert.That(issues.All(i => i.Source == TableSource.Json), Is.True);
            // 연쇄 문제가 하나도 없는지는 여기서 보지 않는다. day·element·slot을 못 읽으면 검사 4·8·9가
            // 그 값을 모른 채 셈해 문제를 하나 더 낸다(2026-11-15 확인, 고칠지는 사용자 결정 대기).
        }

        // ---- 묶음 단위 ----

        [Test]
        public void JSON_파일이_빠지거나_null이거나_모르는_파일이면_알린다()
        {
            var json = GoodJson();
            json.Remove("boards.json");
            json["stats.json"] = null;
            json["typo.json"] = "{}";
            var r = TableBuilder.LoadJson(json);
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Tables, Is.Null);
            Assert.That(r.Issues.Any(i => i.Table == "boards" && i.Message.Contains("JSON 파일이 없음")), Is.True);
            Assert.That(r.Issues.Any(i => i.Table == "stats" && i.Message.Contains("JSON 파일이 없음")), Is.True);
            Assert.That(r.Issues.Any(i => i.Table == "typo" && i.Message.Contains("알 수 없는")), Is.True);
        }

        // ---- 키 순서 (Codex 검토: 이름으로만 확인) ----

        [Test]
        public void 키_순서가_달라도_같은_표로_읽는다()
        {
            var json = GoodJson();
            json["stats.json"] =
                "{\"rows\": [{\"build\": \"m1\", \"max_level\": 0, \"name_ko\": \"공격력\", \"stat_id\": \"attack\"}], " +
                "\"table\": \"stats\", \"columns\": [\"stat_id\", \"name_ko\", \"max_level\", \"build\"]}";
            var r = TableBuilder.LoadJson(json);
            TestTables.AssertNone(r.Issues.ToList());
            Assert.That(r.Tables[Schemas.Stats].SameValues(GoodSet()[Schemas.Stats]), Is.True);
        }

        [Test]
        public void 행에_키가_빠지거나_남으면_그_행을_알린다()
        {
            var issues = LoadBroken(GoodJson(), "stats.json", "\"max_level\": 0, ", "\"level\": 0, ");
            var issue = issues.First(i => i.Message.Contains("행의 키"));
            Assert.That(issue.Message, Does.Contain("빠진 키: max_level").And.Contain("남는 키: level"));
            Assert.That(issue.Line, Is.EqualTo(1));
        }

        [Test]
        public void 요일_목록_요소_안에_구분자를_숨기면_받지_않는다()
        {
            Assert.That(ReadBroken(Schemas.TimedEvents, "[\"mon\", \"wed\"]", "[\"mon|wed\"]"), Is.Not.Empty);
        }

        // ---- 위치 표시 (Codex 검토: JSON은 데이터 행 번호) ----

        [Test]
        public void JSON_문제는_json_파일과_데이터_행_번호로_표시된다()
        {
            var issues = LoadBroken(GoodJson(), "attendance.json", "{\"day\": 2, \"reward_kind\": \"currency\", \"reward_id\": \"dust\", \"amount\": 1000}",
                "{\"day\": 2, \"reward_kind\": \"currency\", \"reward_id\": \"dust\", \"amount\": 0}");
            var issue = issues.Single();
            Assert.That(issue.Source, Is.EqualTo(TableSource.Json));
            Assert.That(issue.ToString(), Does.StartWith("[검사3] attendance.json 데이터 2행 amount:"));
        }

        [Test]
        public void 표_사이_검사의_문제도_JSON_위치로_표시된다()
        {
            var issues = LoadBroken(GoodJson(), "daily_missions.json", "\"reward_currency\": \"gold\"", "\"reward_currency\": \"gem\"");
            Assert.That(issues.Single().ToString(), Does.StartWith("[검사2] daily_missions.json 데이터 1행 reward_currency:"));
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
