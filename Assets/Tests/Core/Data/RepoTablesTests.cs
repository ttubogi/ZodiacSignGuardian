using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZodiacSignGuardian.Data.Tables;

namespace ZodiacSignGuardian.Core.Tests.Data
{
    /// <summary>저장소의 실제 Tables/csv와 Assets/Resources/Tables(JSON). 값은 임시지만 검사는 실제로 통과해야 한다.</summary>
    [TestFixture]
    public class RepoTablesTests
    {
        [Test]
        public void 저장소의_CSV_12개가_모든_검사를_통과한다()
        {
            var r = TableBuilder.Build(RepoFiles.CsvBytes());
            TestTables.AssertNone(r.Issues);
            Assert.That(r.Outputs.Count, Is.EqualTo(12));
        }

        [Test]
        public void 저장소의_JSON이_CSV에서_만든_것과_같다()
        {
            // CSV만 고치고 변환을 잊으면 여기서 걸린다
            var built = TableBuilder.Build(RepoFiles.CsvBytes()).Outputs;
            var committed = RepoFiles.JsonTexts();
            Assert.That(committed.Keys.OrderBy(k => k).ToArray(), Is.EqualTo(built.Keys.OrderBy(k => k).ToArray()), "Assets/Resources/Tables의 JSON 파일 목록");
            foreach (var k in built.Keys)
            {
                Assert.That(committed[k], Is.EqualTo(built[k]), k + "가 CSV와 다름 — dotnet run --project dotnet/TableBuild로 다시 만들 것");
            }
        }

        [Test]
        public void 저장소의_JSON을_읽어도_검사를_통과한다()
        {
            var r = TableBuilder.LoadJson(RepoFiles.JsonTexts());
            TestTables.AssertNone(r.Issues.ToList());
            Assert.That(r.Tables, Is.Not.Null);
        }

        [Test]
        public void m1_범위가_기획서와_맞다()
        {
            var set = TableBuilder.Build(RepoFiles.CsvBytes()).Tables;
            // 기획서 v0.8: 1개월 빌드 = 1~3장, 별 15종, 재화 4종
            var m1Chapters = set[Schemas.Boards].Rows.Where(r => r.Build == Schemas.M1).Select(r => r.Long("chapter")).Distinct().OrderBy(x => x);
            Assert.That(m1Chapters.ToArray(), Is.EqualTo(new long[] { 1, 2, 3 }));
            Assert.That(set[Schemas.Stars].Rows.Count(r => r.Build == Schemas.M1), Is.EqualTo(15));
            Assert.That(set[Schemas.Stars].Rows.Count, Is.EqualTo(30));
            Assert.That(set[Schemas.Currencies].Rows.Count(r => r.Build == Schemas.M1), Is.EqualTo(4));
            Assert.That(set[Schemas.Currencies].Rows.Where(r => r.Str("number_type") == "game_number").Select(r => r.Str("currency_id")).ToArray(),
                Is.EqualTo(new[] { "stardust" }), "설계서: 별가루만 game_number");
            Assert.That(set[Schemas.DailyMissions].Rows.Count, Is.EqualTo(5), "설계서: 일일 임무 5개");
        }

        // ---- 실제 파일을 망가뜨려도 잡는지 ----

        static List<TableIssue> RunRepo(string table, string from, string to)
        {
            var t = RepoFiles.CsvTexts();
            return TestTables.Run(t.Replace(table, from, to));
        }

        [Test]
        public void 실제_출석표에서_14일을_빼면_잡는다()
        {
            TestTables.AssertOnly(RunRepo("attendance", "14,star,fire_1,1\n", ""), TableIssueKind.Check4);
        }

        [Test]
        public void 실제_별_표에서_m1_별을_v1로_바꾸면_검사8과_출석의_검사7이_잡는다()
        {
            // wood_1은 7일째 출석 보상이라 검사 7도 걸린다
            var issues = RunRepo("stars", "wood_1,wood,나무 별 1,m1", "wood_1,wood,나무 별 1,v1");
            TestTables.AssertHas(issues, TableIssueKind.Check8);
            TestTables.AssertHas(issues, TableIssueKind.Check7);
        }

        [Test]
        public void 실제_판에서_선_하나를_빼면_잡는다()
        {
            TestTables.AssertOnly(RunRepo("boards", "3,cygnus,2,5,m1\n", ""), TableIssueKind.Check9);
        }

        [Test]
        public void 실제_규칙표에서_한_행의_시차를_바꾸면_잡는다()
        {
            TestTables.AssertOnly(RunRepo("reset_rules", "weekly,weekly,5,mon,,540", "weekly,weekly,5,mon,,0"), TableIssueKind.Check10);
        }
    }
}
