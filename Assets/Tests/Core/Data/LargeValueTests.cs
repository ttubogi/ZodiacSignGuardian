using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using ZodiacSignGuardian.Data.Json;
using ZodiacSignGuardian.Data.Tables;
using static ZodiacSignGuardian.Core.Tests.Data.TestTables;

namespace ZodiacSignGuardian.Core.Tests.Data
{
    /// <summary>
    /// 칸 값이 아주 커도 검사가 그 크기만큼 돌지 않는지 (Codex 검토 2026-11-15, 출석 검사 4).
    /// 예전 코드는 1일부터 마지막 날까지 하나씩 세어 day 하나가 10억이면 멈추거나 메모리가 바닥났다.
    /// 멈춘 코드에서도 테스트가 끝나도록 정해진 시간 안에 끝나지 않으면 실패로 본다.
    /// </summary>
    [TestFixture]
    public class LargeValueTests
    {
        static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

        static T WithinLimit<T>(Func<T> work, string what)
        {
            var task = Task.Run(work);
            Assert.That(task.Wait(Limit), Is.True, what + "이(가) " + Limit.TotalSeconds + "초 안에 끝나지 않음 (값 크기만큼 도는 반복이 있는지 확인)");
            return task.Result;
        }

        static Dictionary<string, string> WithDay(long day)
        {
            return Good().Replace("attendance", "3,star,wood_1,1", day + ",star,wood_1,1");
        }

        [TestCase(1_000_000_000L)]
        [TestCase(long.MaxValue)]
        public void 출석일이_아주_커도_CSV_변환은_빨리_실패한다(long day)
        {
            var result = WithinLimit(() => TableBuilder.BuildFromTexts(WithDay(day)), "CSV 변환");
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Tables, Is.Null, "실패하면 표 묶음을 주지 않는다");
            Assert.That(result.Outputs, Is.Empty, "실패하면 JSON을 내지 않는다");
            AssertOnly(result.Issues.ToList(), TableIssueKind.Check4);
            Assert.That(result.Issues.Any(i => i.Message.EndsWith("1~" + day + "일 중 빠진 날: 3~" + (day - 1))), Is.True, Describe(result.Issues));
            Assert.That(result.Issues.Any(i => i.Message.Contains("마지막 날 " + day + "일이")), Is.True, Describe(result.Issues));
        }

        [TestCase(1_000_000_000L)]
        [TestCase(long.MaxValue)]
        public void 출석일이_아주_커도_JSON_읽기는_빨리_실패한다(long day)
        {
            // 같은 표를 JSON으로 옮긴다 (읽기 단계에는 문제가 없다: 값은 양의 정수)
            var parseIssues = new List<TableIssue>();
            var parsed = TableLoader.LoadTexts(WithDay(day), parseIssues);
            Assert.That(parseIssues, Is.Empty, Describe(parseIssues));
            var json = parsed.Tables.ToDictionary(t => t.Schema.JsonFileName, t => TableJson.Write(t));
            Assert.That(json["attendance.json"], Does.Contain("\"day\": " + day));

            var result = WithinLimit(() => TableBuilder.LoadJson(json), "JSON 읽기");
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Tables, Is.Null, "실패하면 표 묶음을 주지 않는다");
            AssertOnly(result.Issues.ToList(), TableIssueKind.Check4);
            Assert.That(result.Issues.All(i => i.Source == TableSource.Json), Is.True);
            Assert.That(result.Issues.Any(i => i.Message.EndsWith("1~" + day + "일 중 빠진 날: 3~" + (day - 1))), Is.True, Describe(result.Issues));
        }

        [Test]
        public void 첫날과_마지막_날이_long_최대값_근처여도_넘치지_않는다()
        {
            // 1일 다음이 long.MaxValue - 1, 그 다음이 long.MaxValue: 빈 구간은 2~(최대값-2) 하나
            var texts = Good()
                .Replace("attendance", "2,currency,dust,1000", (long.MaxValue - 1) + ",currency,dust,1000")
                .Replace("attendance", "3,star,wood_1,1", long.MaxValue + ",star,wood_1,1");
            var result = WithinLimit(() => TableBuilder.BuildFromTexts(texts), "CSV 변환");
            var gap = result.Issues.Where(i => i.Message.Contains("빠진 날")).ToList();
            Assert.That(gap.Count, Is.EqualTo(1), Describe(result.Issues));
            Assert.That(gap[0].Message, Does.EndWith("빠진 날: 2~" + (long.MaxValue - 2)));
        }
    }
}
