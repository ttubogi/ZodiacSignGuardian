using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using ZodiacSignGuardian.Data.Tables;

namespace ZodiacSignGuardian.Core.Tests.Data
{
    [TestFixture]
    public class CellParserTests
    {
        static bool Ok(ColumnSpec col, string raw) => CellParser.TryParse(col, raw, out _, out _);
        static object Value(ColumnSpec col, string raw)
        {
            Assert.That(CellParser.TryParse(col, raw, out var v, out var e), Is.True, e);
            return v;
        }

        static readonly ColumnSpec Positive = new ColumnSpec("n", ColumnKind.PositiveInt);
        static readonly ColumnSpec ResetHour = Schemas.ResetRules.Columns[Schemas.ResetRules.IndexOf("reset_hour")];
        static readonly ColumnSpec UtcOffset = Schemas.ResetRules.Columns[Schemas.ResetRules.IndexOf("utc_offset_min")];
        static readonly ColumnSpec Start = Schemas.TimedEvents.Columns[Schemas.TimedEvents.IndexOf("start_time")];
        static readonly ColumnSpec End = Schemas.TimedEvents.Columns[Schemas.TimedEvents.IndexOf("end_time")];
        static readonly ColumnSpec Days = Schemas.TimedEvents.Columns[Schemas.TimedEvents.IndexOf("weekdays")];

        [TestCase("1", true)]
        [TestCase("9223372036854775807", true)]   // long 최댓값
        [TestCase("0", false)]
        [TestCase("-1", false)]
        [TestCase("1.5", false)]
        [TestCase(" 1", false)]
        [TestCase("1 ", false)]
        [TestCase("+1", false)]
        [TestCase("1e3", false)]
        [TestCase("", false)]
        [TestCase("-", false)]
        [TestCase("9223372036854775808", false)]  // long 넘침
        [TestCase("١٢", false)]                   // 다른 문자권의 숫자
        public void 양의_정수(string raw, bool ok)
        {
            Assert.That(Ok(Positive, raw), Is.EqualTo(ok));
        }

        [Test]
        public void 정수는_long으로_읽는다()
        {
            Assert.That(Value(Positive, "720"), Is.EqualTo(720L));
            Assert.That(Value(Positive, "007"), Is.EqualTo(7L)); // 앞의 0은 허용 (Excel이 붙이는 경우)
        }

        [TestCase("0", true)]
        [TestCase("23", true)]
        [TestCase("24", false)]
        [TestCase("-1", false)]
        public void reset_hour는_0에서_23(string raw, bool ok)
        {
            Assert.That(Ok(ResetHour, raw), Is.EqualTo(ok));
        }

        [TestCase("-720", true)]
        [TestCase("840", true)]
        [TestCase("540", true)]
        [TestCase("-721", false)]
        [TestCase("841", false)]
        public void utc_offset_min은_음수도_된다(string raw, bool ok)
        {
            Assert.That(Ok(UtcOffset, raw), Is.EqualTo(ok));
        }

        [TestCase("a", true)]
        [TestCase("kill_monsters_2", true)]
        [TestCase("Kill", false)]
        [TestCase("1abc", false)]
        [TestCase("a-b", false)]
        [TestCase("a b", false)]
        [TestCase("_a", false)]
        [TestCase("별", false)]
        public void id는_소문자_숫자_밑줄(string raw, bool ok)
        {
            Assert.That(Ok(new ColumnSpec("id", ColumnKind.Id), raw), Is.EqualTo(ok));
        }

        [TestCase("별가루", true)]
        [TestCase("치명타 확률", true)]
        [TestCase(" 별가루", false)]
        [TestCase("별가루 ", false)]
        [TestCase("두\n줄", false)]
        public void 글은_앞뒤_공백과_줄바꿈이_없다(string raw, bool ok)
        {
            Assert.That(Ok(new ColumnSpec("t", ColumnKind.Text), raw), Is.EqualTo(ok));
        }

        [TestCase("00:00", true, false)]
        [TestCase("05:00", true, true)]
        [TestCase("23:59", true, true)]
        [TestCase("24:00", false, true)]
        [TestCase("24:01", false, false)]
        [TestCase("5:00", false, false)]
        [TestCase("05:60", false, false)]
        [TestCase("05-00", false, false)]
        public void 시각(string raw, bool okAsStart, bool okAsEnd)
        {
            Assert.That(Ok(Start, raw), Is.EqualTo(okAsStart), "시작");
            Assert.That(Ok(End, raw), Is.EqualTo(okAsEnd), "끝");
        }

        [Test]
        public void 요일_목록()
        {
            Assert.That(Value(Days, "mon|wed"), Is.EqualTo(new[] { "mon", "wed" }));
            Assert.That(Ok(Days, "sun"), Is.True);
            Assert.That(Ok(Days, "mon|mon"), Is.False);
            Assert.That(Ok(Days, "mon|"), Is.False);
            Assert.That(Ok(Days, "Mon"), Is.False);
            Assert.That(Ok(Days, "mon,wed"), Is.False);
        }

        [Test]
        public void 빈_칸은_선택_열에서만_null()
        {
            Assert.That(Value(ResetHour, ""), Is.Null);
            Assert.That(Ok(Positive, ""), Is.False);
        }
    }

    [TestFixture]
    public class TableLoaderTests
    {
        [Test]
        public void 테스트용_표_묶음은_문제가_없다()
        {
            TestTables.AssertNone(TestTables.Run(TestTables.Good()));
        }

        [Test]
        public void v1_표는_머리줄만_있어도_된다()
        {
            var t = TestTables.Good().HeaderOnly("weekday_dungeons").HeaderOnly("timed_events").HeaderOnly("weekly_challenge");
            TestTables.AssertNone(TestTables.Run(t));
        }

        [Test]
        public void 머리줄의_열_순서가_다르면_형식_문제()
        {
            var t = TestTables.Good().Replace("stats", "stat_id,name_ko,max_level,build", "stat_id,max_level,name_ko,build");
            TestTables.AssertOnly(TestTables.Run(t), TableIssueKind.Format);
        }

        [Test]
        public void 머리줄도_없는_빈_파일은_형식_문제()
        {
            var t = TestTables.Good();
            t["weekly_challenge.csv"] = "";
            var issues = TestTables.Run(t);
            TestTables.AssertOnly(issues, TableIssueKind.Format);
            Assert.That(issues[0].Message, Does.Contain("머리줄"));
        }

        [Test]
        public void 파일이_빠지거나_모르는_파일이_있으면_형식_문제()
        {
            var t = TestTables.Good();
            t.Remove("ad_buffs.csv");
            t["typo_tabel.csv"] = "a\n";
            var issues = TestTables.Run(t);
            TestTables.AssertOnly(issues, TableIssueKind.Format);
            Assert.That(issues.Any(i => i.Table == "ad_buffs" && i.Message.Contains("없음")), Is.True);
            Assert.That(issues.Any(i => i.Table == "typo_tabel"), Is.True);
        }

        [Test]
        public void 칸_수가_다른_행은_줄_번호와_함께_형식_문제()
        {
            var issues = TestTables.Run(TestTables.Good().AppendRow("stats", "max_hp,체력,0"));
            TestTables.AssertOnly(issues, TableIssueKind.Format);
            Assert.That(issues[0].Line, Is.EqualTo(3));
        }

        [Test]
        public void 양의_정수가_아니면_검사3으로_표시된다()
        {
            var issues = TestTables.Run(TestTables.Good().Replace("attendance", "1,currency,gold,50", "1,currency,gold,0"));
            TestTables.AssertOnly(issues, TableIssueKind.Check3);
            Assert.That(issues[0].Column, Is.EqualTo("amount"));
            Assert.That(issues[0].Line, Is.EqualTo(2));
        }

        [Test]
        public void 설정_표는_행이_정확히_하나()
        {
            TestTables.AssertOnly(TestTables.Run(TestTables.Good().AppendRow("offline_reward", "600,1,2,1")), TableIssueKind.Format);
            TestTables.AssertOnly(TestTables.Run(TestTables.Good().HeaderOnly("offline_reward")), TableIssueKind.Format);
        }

        [Test]
        public void 오프라인_최소가_최대보다_크면_형식_문제()
        {
            var t = TestTables.Good().Replace("offline_reward", "720,1,2,1", "720,721,2,1");
            TestTables.AssertOnly(TestTables.Run(t), TableIssueKind.Format);
            TestTables.AssertNone(TestTables.Run(TestTables.Good().Replace("offline_reward", "720,1,2,1", "720,720,2,1")));
        }

        [Test]
        public void reset_rules는_period에_맞는_칸만_채운다()
        {
            TestTables.AssertOnly(TestTables.Run(TestTables.Good().Replace("reset_rules", "weekly,weekly,5,mon,", "weekly,weekly,5,,")), TableIssueKind.Format);
            TestTables.AssertOnly(TestTables.Run(TestTables.Good().Replace("reset_rules", "daily,daily,5,,,", "daily,daily,5,,7,")), TableIssueKind.Format);
            TestTables.AssertOnly(TestTables.Run(TestTables.Good().Replace("reset_rules", "attendance_cycle,cycle,,,3,", "attendance_cycle,cycle,,,,")), TableIssueKind.Format);
        }

        [Test]
        public void UTF8이_아닌_파일은_Excel_저장_방법과_함께_알려준다()
        {
            var files = TestTables.Good().ToDictionary(p => p.Key, p => Encoding.UTF8.GetBytes(p.Value));
            // "금화"를 CP949로 쓴 바이트 (B1 DD C8 AD) — UTF-8로는 해독되지 않는다
            var cp949 = new List<byte>(Encoding.ASCII.GetBytes("currency_id,name_ko,number_type,build\ngold,"));
            cp949.AddRange(new byte[] { 0xB1, 0xDD, 0xC8, 0xAD });
            cp949.AddRange(Encoding.ASCII.GetBytes(",integer,m1\n"));
            files["currencies.csv"] = cp949.ToArray();

            var issues = TableBuilder.Build(files).Issues;
            Assert.That(issues.Any(i => i.Table == "currencies" && i.Message.Contains("CSV UTF-8")), Is.True, TestTables.Describe(issues));
            Assert.That(issues.Any(i => i.Table == "currencies" && i.Message.Contains("파일이 없음")), Is.False, "해독 오류를 '파일 없음'으로 두 번 알리지 않음");
        }

        [Test]
        public void BOM이_붙은_UTF8은_받는다()
        {
            var files = TestTables.Good().ToDictionary(p => p.Key, p => new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(p.Value)).ToArray());
            TestTables.AssertNone(TableBuilder.Build(files).Issues);
        }
    }
}
