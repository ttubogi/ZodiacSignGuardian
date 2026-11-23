using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZodiacSignGuardian.Core.Commands;
using ZodiacSignGuardian.Core.Session;
using ZodiacSignGuardian.Core.Stars;
using ZodiacSignGuardian.Core.Tests.Data;
using ZodiacSignGuardian.Data.Tables;
using static ZodiacSignGuardian.Core.Tests.Session.C4;

namespace ZodiacSignGuardian.Core.Tests.Session
{
    /// <summary>
    /// C4 뒤 보강 (2026-11-22).
    /// ① 무료 뽑기 횟수 정책: 한도를 낮춘 표로 그보다 많이 쓴 저장도 불러오고, 그날 남은 수만 0 (설계서 v0.16 X7, 사용자 결정).
    /// ② Codex C4 검토 권고: 같은 저장을 다른 세션 nonce로 불러와도 뽑히는 별 ID 순서가 같다, 지금 세 판 밖의 연결 모양도 따로 계산한 가장 좋은 배치와 같다.
    /// </summary>
    [TestFixture]
    public class C4FollowupTests
    {
        static Dictionary<string, string> FreePerDay(int perDay) =>
            Texts().Replace("star_rules", "gold,100,900,1,shard,", "gold,100,900," + perDay + ",shard,");

        /// <summary>하루 무료 뽑기가 perDay번인 표에서 used번 쓰고 만든 저장.</summary>
        static string SaveWithFreeUsed(int perDay, int used)
        {
            var h = New(FreePerDay(perDay));
            for (var i = 0; i < used; i++) h.Ok(h.S.DrawStar(1, DrawPayment.Free));
            Assert.That(h.S.FreeDrawsLeftToday, Is.EqualTo(perDay - used));
            var json = h.S.CreateSave().Json;
            Assert.That(json, Does.Contain("\"used\": " + used));
            return json;
        }

        static C3Harness LoadWith(Dictionary<string, string> texts, string json)
        {
            var h = New(texts, sync: false);
            h.LoadJson(json);
            return h;
        }

        // ---- ① 무료 뽑기 횟수 정책 ----

        [TestCase(3, 3, 1)]
        [TestCase(10, 7, 5)]
        [TestCase(10, 10, 1)]
        [TestCase(2, 1, 0)]
        [TestCase(10, 10, 0)]
        public void 한도를_낮춘_표로_더_많이_쓴_저장도_불러오고_그날_남은_수만_0이다(int oldPerDay, int used, int newPerDay)
        {
            var json = SaveWithFreeUsed(oldPerDay, used);
            var h = LoadWith(FreePerDay(newPerDay), json);

            // 불러온 직후(시간 미확인)에도 저장된 구간의 값을 보여 준다
            Assert.That(h.S.FreeDrawsLeftToday, Is.EqualTo(0));
            h.Sync(T0 + Min);
            Assert.That(h.S.FreeDrawsLeftToday, Is.EqualTo(0));
            h.AssertFailsUnchanged(() => h.S.DrawStar(1, DrawPayment.Free), ErrorCode.AlreadyClaimed);

            // 쓴 수는 줄이지 않고 그대로 저장한다. 다시 불러와도 된다
            var again = h.S.CreateSave().Json;
            Assert.That(again, Does.Contain("\"used\": " + used));
            var h2 = LoadWith(FreePerDay(newPerDay), again);
            Assert.That(h2.S.FreeDrawsLeftToday, Is.EqualTo(0));

            // 다음 날은 지금 표의 한도만큼 다시 찬다
            h.Sync(T0 + Day);
            Assert.That(h.S.FreeDrawsLeftToday, Is.EqualTo(newPerDay));
            if (newPerDay == 0)
            {
                h.AssertFailsUnchanged(() => h.S.DrawStar(1, DrawPayment.Free), ErrorCode.AlreadyClaimed);
                return;
            }
            h.Ok(h.S.DrawStar(1, DrawPayment.Free));
            Assert.That(h.S.FreeDrawsLeftToday, Is.EqualTo(newPerDay - 1));
            Assert.That(h.S.CreateSave().Json, Does.Contain("\"used\": 1"), "새날 첫 사용은 1부터");
        }

        [Test]
        public void 한도를_올린_표로_불러오면_그날_남은_수가_늘어난다()
        {
            var h = LoadWith(FreePerDay(3), SaveWithFreeUsed(1, 1));
            h.Sync(T0 + Min);
            Assert.That(h.S.FreeDrawsLeftToday, Is.EqualTo(2));
        }

        [Test]
        public void 불러올_때_받는_쓴_수의_상한은_표_열_범위의_상한과_같다()
        {
            Assert.That(FreeDraws.MaxStoredUsed, Is.EqualTo(10));
            Assert.That(TableBuilder.BuildFromTexts(FreePerDay(FreeDraws.MaxStoredUsed)).Ok, Is.True, "free_draws_per_day 10은 표 검사 통과");
            Assert.That(TableBuilder.BuildFromTexts(FreePerDay(FreeDraws.MaxStoredUsed + 1)).Ok, Is.False, "free_draws_per_day 11은 표 검사 실패");
        }

        static SessionLoadResult LoadRaw(Dictionary<string, string> texts, string json)
        {
            SessionLoadResult r = null;
            Assert.DoesNotThrow(() => r = GameSessionLoader.Load(C3Harness.Tables(texts), json, C3Harness.DefaultNonce));
            return r;
        }

        static long ProcessedDay(string json) =>
            long.Parse(System.Text.RegularExpressions.Regex.Match(json, "\"processedDay\": (\\d+)").Groups[1].Value);

        [TestCase(11)]
        [TestCase(12)]
        [TestCase(2147483648L)]
        [TestCase(long.MaxValue)]
        public void 쓴_수가_표_열_범위_상한을_넘으면_불러오지_않는다(long used)
        {
            var json = R(SaveWithFreeUsed(10, 10), "\"used\": 10", "\"used\": " + used);
            var r = LoadRaw(FreePerDay(10), json);
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors.Any(e => e.StartsWith("freeDraw.used")), Is.True, string.Join("\n", r.Errors));
        }

        [Test]
        public void 쓴_구간이_처리한_구간보다_뒤면_불러오지_않는다()
        {
            var json = SaveWithFreeUsed(1, 1);
            var day = ProcessedDay(json);
            Assert.That(LoadRaw(FreePerDay(1), json).Ok, Is.True, "고치기 전: 쓴 구간 = 처리한 구간");
            var bad = R(json, "\"freeDraw\": {\"day\": " + day, "\"freeDraw\": {\"day\": " + (day + 1));
            var r = LoadRaw(FreePerDay(1), bad);
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors.Any(e => e.StartsWith("freeDraw.day")), Is.True, string.Join("\n", r.Errors));
        }

        [Test]
        public void 쓴_구간이_처리한_구간보다_앞인_저장은_불러온다()
        {
            var json = SaveWithFreeUsed(1, 1);
            var day = ProcessedDay(json);
            var older = R(json, "\"freeDraw\": {\"day\": " + day, "\"freeDraw\": {\"day\": " + (day - 3));
            var h = LoadWith(FreePerDay(1), older);
            h.Sync(T0 + Min);
            Assert.That(h.S.FreeDrawsLeftToday, Is.EqualTo(1), "지난 구간에 쓴 것은 오늘 수에 들어가지 않는다");
        }

        [Test]
        public void 처리한_구간이_없는데_쓴_구간이_있으면_불러오지_않는다()
        {
            var synced = SaveWithFreeUsed(1, 1);
            var day = ProcessedDay(synced);
            var fresh = New(FreePerDay(1), sync: false).S.CreateSave().Json; // 첫 동기화 전: 처리한 구간 없음
            Assert.That(fresh, Does.Contain("\"processedDay\": null"));
            var bad = R(fresh, "\"freeDraw\": {\"day\": null, \"used\": 0}", "\"freeDraw\": {\"day\": " + day + ", \"used\": 1}");
            var r = LoadRaw(FreePerDay(1), bad);
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors.Any(e => e.StartsWith("freeDraw.day")), Is.True, string.Join("\n", r.Errors));
        }

        [Test]
        public void 시계를_읽지_못하면_무료_뽑기_구간_비교로_연쇄_문제를_더하지_않는다()
        {
            var json = SaveWithFreeUsed(1, 1);
            var bad = System.Text.RegularExpressions.Regex.Replace(json, "\"syncElapsedUs\": \\d+", "\"syncElapsedUs\": -1");
            Assert.That(bad, Is.Not.EqualTo(json));
            var r = LoadRaw(FreePerDay(1), bad);
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors.Any(e => e.StartsWith("clock")), Is.True, string.Join("\n", r.Errors));
            Assert.That(r.Errors.Any(e => e.StartsWith("freeDraw")), Is.False, string.Join("\n", r.Errors));
        }

        [Test]
        public void 시계를_읽지_못해도_쓴_수_자체의_오류는_따로_잡는다()
        {
            // Codex v0.16 검토 권고: 시계에 기대는 비교만 건너뛰고, 쓴 수 범위 검사는 그대로 한다
            var json = R(SaveWithFreeUsed(1, 1), "\"used\": 1", "\"used\": 11");
            var bad = System.Text.RegularExpressions.Regex.Replace(json, "\"syncElapsedUs\": \\d+", "\"syncElapsedUs\": -1");
            Assert.That(bad, Is.Not.EqualTo(json));
            var r = LoadRaw(FreePerDay(1), bad);
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors.Any(e => e.StartsWith("clock")), Is.True, string.Join("\n", r.Errors));
            Assert.That(r.Errors.Any(e => e.StartsWith("freeDraw.used")), Is.True, string.Join("\n", r.Errors));
            Assert.That(r.Errors.Any(e => e.StartsWith("freeDraw.day")), Is.False, string.Join("\n", r.Errors));
        }

        [Test]
        public void 무료를_쓴_뒤_서버가_이전_날로_보정된_저장도_처리한_날_기준으로_불러온다()
        {
            // Codex v0.16 검토 권고: 비교 기준은 지금 추정한 날짜가 아니라 처리한 일일 구간이다
            var h = New(FreePerDay(1));
            h.Sync(T0 + Day);                                  // 다음 날 처리
            h.Ok(h.S.DrawStar(1, DrawPayment.Free));           // 그날 무료 1번
            h.Sync(T0);                                        // 서버가 하루 뒤로 감 → TimeWentBack
            h.AssertFailsUnchanged(() => h.S.DrawStar(1, DrawPayment.Free), ErrorCode.TimeWentBack);
            var json = h.S.CreateSave().Json;

            var h2 = LoadWith(FreePerDay(1), json);
            Assert.That(h2.S.FreeDrawsLeftToday, Is.EqualTo(0), "처리한 날(쓴 날)의 값");
            h2.Sync(T0);
            h2.AssertFailsUnchanged(() => h2.S.DrawStar(1, DrawPayment.Free), ErrorCode.TimeWentBack);
            h2.Sync(T0 + Day + Min);                           // 따라잡아도 같은 날이면 다 씀
            h2.AssertFailsUnchanged(() => h2.S.DrawStar(1, DrawPayment.Free), ErrorCode.AlreadyClaimed);
            h2.Sync(T0 + 2 * Day);                             // 그다음 날은 다시 1
            Assert.That(h2.S.FreeDrawsLeftToday, Is.EqualTo(1));
        }

        // ---- ② Codex C4 검토 권고 ----

        static string[] DrawnIds(IEnumerable<GameEvent> events) =>
            events.Select(e => e is StarObtained o ? o.StarId : e is StarDuplicated d ? d.StarId : null).Where(id => id != null).ToArray();

        static string[] DrawSequence(C3Harness h)
        {
            Give(h, "gold", 100_000);
            var ids = new List<string>();
            ids.AddRange(DrawnIds(h.Ok(h.S.DrawStar(10, DrawPayment.Crystal))));
            ids.AddRange(DrawnIds(h.Ok(h.S.DrawStar(1, DrawPayment.Free))));
            ids.AddRange(DrawnIds(h.Ok(h.S.DrawStar(1, DrawPayment.Crystal))));
            ids.AddRange(DrawnIds(h.Ok(h.S.DrawStar(10, DrawPayment.Crystal))));
            return ids.ToArray();
        }

        [Test]
        public void 같은_저장을_다른_세션_nonce로_불러와도_뽑히는_별_ID_순서가_같다()
        {
            // 저장 전에 뽑기를 해서 난수 상태가 시작값이 아니게 한다
            var src = New();
            Give(src, "gold", 1000);
            src.Ok(src.S.DrawStar(1, DrawPayment.Crystal));
            src.Ok(src.S.DrawStar(1, DrawPayment.Free));
            var json = src.S.CreateSave().Json;

            var a = New(sync: false);
            a.LoadJson(json, 0x0000_0000_0000_0001UL);
            var b = New(sync: false);
            b.LoadJson(json, 0xFFFF_FFFF_FFFF_FFFEUL);

            // 다음 날로 동기화해 무료 뽑기가 다시 찬 상태에서 같은 순서로 뽑는다. nonce가 실제로 다르게 들어갔는지도 본다
            a.Sync(T0 + Day);
            b.Sync(T0 + Day);
            Assert.That(a.S.SessionNonce, Is.Not.EqualTo(b.S.SessionNonce));

            var sa = DrawSequence(a);
            var sb = DrawSequence(b);
            Assert.That(sa.Length, Is.EqualTo(22));
            Assert.That(sb, Is.EqualTo(sa));
            Assert.That(b.S.CreateSave().Json, Is.EqualTo(a.S.CreateSave().Json), "뽑은 뒤 저장(난수 상태 포함)도 같다");
        }

        /// <summary>1장 판의 선을 바꾼 표. 원래 1장은 한 줄 1-2, 2-3, 3-4, 4-5.</summary>
        static Dictionary<string, string> Shape(params (int A, int B)[] links) =>
            Texts().Replace("boards", "1,cassiopeia,1,2,m1\n1,cassiopeia,2,3,m1\n1,cassiopeia,3,4,m1\n1,cassiopeia,4,5,m1",
                string.Join("\n", links.Select(l => "1,cassiopeia," + l.A + "," + l.B + ",m1")));

        static IEnumerable<TestCaseData> Shapes()
        {
            var shapes = new (string, (int, int)[])[]
            {
                ("칸 3이 가운데인 별 모양", new[] { (1, 3), (2, 3), (3, 4), (3, 5) }),
                ("다섯 칸 한 바퀴", new[] { (1, 2), (2, 3), (3, 4), (4, 5), (1, 5) }),
                ("모든 칸이 서로 이어짐", new[] { (1, 2), (1, 3), (1, 4), (1, 5), (2, 3), (2, 4), (2, 5), (3, 4), (3, 5), (4, 5) }),
                ("칸 번호와 다른 순서의 한 줄", new[] { (1, 3), (3, 5), (2, 5), (2, 4) }),
                ("세모와 꼬리", new[] { (1, 2), (2, 3), (1, 3), (3, 4), (4, 5) }),
                ("두 가운데", new[] { (1, 2), (2, 3), (2, 4), (4, 5), (3, 4) }),
            };
            foreach (var (name, links) in shapes)
            {
                foreach (var seed in new[] { 1, 2, 3 }) yield return new TestCaseData(links, seed).SetName("연결_모양_" + name.Replace(' ', '_') + "_시드" + seed);
            }
        }

        [TestCaseSource(nameof(Shapes))]
        public void 지금_세_판_밖의_연결_모양도_따로_계산한_가장_좋은_배치와_같다((int A, int B)[] links, int seed)
        {
            var rnd = new Random(seed);
            var h = New(Shape(links));
            Assert.That(Board(h, 1).Links.Count, Is.EqualTo(links.Length), "표의 선이 바뀌었는지");
            var pick = h.S.StarDefinitions.Select(s => s.StarId).OrderBy(_ => rnd.Next()).Take(5 + rnd.Next(6)).ToList();
            foreach (var id in pick) Own(h, id);
            var json = h.S.CreateSave().Json;
            foreach (var s in h.S.OwnedStars)
            {
                json = R(json, "\"starId\": \"" + s.StarId + "\", \"grade\": 1, \"skillLevel\": 1", "\"starId\": \"" + s.StarId + "\", \"grade\": 1, \"skillLevel\": " + (1 + rnd.Next(30)));
            }
            h.LoadJson(json);
            h.Sync(T0);
            h.Ok(h.S.AutoArrange(1));
            Assert.That(Slots(h, 1), Is.EqualTo(C4BoardTests.Reference(h, 1, h.S.OwnedStars.ToList())));
        }
    }
}
