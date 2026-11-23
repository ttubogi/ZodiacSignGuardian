using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZodiacSignGuardian.Core.Commands;
using ZodiacSignGuardian.Core.Session;
using ZodiacSignGuardian.Core.Stars;
using ZodiacSignGuardian.Core.Tests.Data;
using static ZodiacSignGuardian.Core.Tests.Session.C4;

namespace ZodiacSignGuardian.Core.Tests.Session
{
    /// <summary>C4 저장 칸(별 등급·스킬, 판, 무료 뽑기), 불러올 때 검사, 위반 규칙 5·6 주입, 무작위 명령 (설계서 v0.12 3·5절).</summary>
    [TestFixture]
    public class C4SaveTests
    {
        static void AssertRoundTrip(C3Harness h, string where)
        {
            var save = h.S.CreateSave();
            var r = GameSessionLoader.Load(h.T, save.Json, C3Harness.DefaultNonce ^ 1);
            Assert.That(r.Ok, Is.True, where + ": " + string.Join("\n", r.Errors));
            Assert.That(SaveFormat.Write(r.Session, save.SaveRevision), Is.EqualTo(save.Json), where);
            // 저장하지 않는 켜진 선 목록은 불러온 배치에서 다시 계산해 같다
            Assert.That(r.Session.BoardStatuses.Select(b => string.Join(",", b.ActiveLinks)), Is.EqualTo(h.S.BoardStatuses.Select(b => string.Join(",", b.ActiveLinks))), where);
            Assert.That(r.Session.FreeDrawsLeftToday, Is.EqualTo(h.S.FreeDrawsLeftToday), "처리한 일일 구간은 저장되므로 남은 무료 수도 같음");
        }

        [Test]
        public void 허용하는_모든_C4_상태에서_저장하고_불러오면_같다()
        {
            var h = New(sync: false);
            AssertRoundTrip(h, "새 계정, 동기화 전");
            Give(h, "gold", 100_000);
            h.Ok(h.S.DrawStar(10, DrawPayment.Crystal));
            AssertRoundTrip(h, "동기화 전 뽑기");
            h.Ok(h.S.AutoArrange(1));
            AssertRoundTrip(h, "동기화 전 배치");
            h.Sync(T0);
            h.Ok(h.S.DrawStar(1, DrawPayment.Free));
            AssertRoundTrip(h, "무료 뽑기 씀");
            var id = h.S.OwnedStars[0].InstanceId;
            Give(h, "shard", 1_000_000);
            for (var i = 0; i < 4; i++) h.Ok(h.S.UpgradeStar(id));
            for (var i = 0; i < 29; i++) h.Ok(h.S.UpgradeSkill(id));
            AssertRoundTrip(h, "등급 5·스킬 30 (끝값)");
            h.S.SetChapterForTest(2);
            h.Ok(h.S.PlaceStar(2, 2, id));
            AssertRoundTrip(h, "두 판에 걸친 배치");
            h.Ok(h.S.RemoveStar(2, 2));
            AssertRoundTrip(h, "뺀 뒤");
            h.Sync(T0 + Day);
            AssertRoundTrip(h, "무료 뽑기 다음 날 (기록은 어제)");
        }

        [Test]
        public void 무료_뽑기_기록은_재시작해도_남는다()
        {
            var h = New();
            h.Ok(h.S.DrawStar(1, DrawPayment.Free));
            h.Restart();
            h.Sync(T0 + Min);
            Assert.That(h.S.FreeDrawsLeftToday, Is.EqualTo(0));
            h.AssertFailsUnchanged(() => h.S.DrawStar(1, DrawPayment.Free), ErrorCode.AlreadyClaimed);
        }

        [Test]
        public void 같은_시드면_재시작_뒤에도_뽑기_순서가_이어진다()
        {
            var a = New();
            Give(a, "gold", 1000);
            a.Ok(a.S.DrawStar(1, DrawPayment.Crystal));
            a.Restart();
            Give(a, "gold", 1000);
            var after = a.Ok(a.S.DrawStar(1, DrawPayment.Crystal));
            var b = New();
            Give(b, "gold", 1000);
            b.Ok(b.S.DrawStar(1, DrawPayment.Crystal));
            var straight = b.Ok(b.S.DrawStar(1, DrawPayment.Crystal));
            // 재시작 뒤에는 시간 미확인이라 임무 이벤트만 다르다. 뽑힌 별은 같다
            Assert.That(string.Join(",", after.Where(e => e is StarObtained || e is StarDuplicated)), Is.EqualTo(string.Join(",", straight.Where(e => e is StarObtained || e is StarDuplicated))));
        }

        // ---- 틀린 저장 ----

        static string BaseSave()
        {
            var h = New();
            h.S.SetChapterForTest(1);
            var w1 = Own(h, "wood_1");
            var f1 = Own(h, "fire_1");
            Own(h, "earth_2");
            h.Ok(h.S.PlaceStar(1, 1, w1));
            h.Ok(h.S.PlaceStar(1, 2, f1));
            h.Ok(h.S.DrawStar(1, DrawPayment.Free)); // 시드 7 첫 뽑기 wood_1 → 중복
            return h.S.CreateSave().Json;
        }

        const string Board1 = "\"1\": [1, 2, 0, 0, 0]";
        const string Board2 = "\"2\": [0, 0, 0, 0, 0]";

        static IEnumerable<TestCaseData> BadSaves()
        {
            var cases = new (string, Func<string, string>)[]
            {
                ("같은 별 둘", j => R(j, "\"starId\": \"earth_2\"", "\"starId\": \"wood_1\"")),
                ("등급 0", j => R(j, "\"starId\": \"earth_2\", \"grade\": 1", "\"starId\": \"earth_2\", \"grade\": 0")),
                ("등급 6", j => R(j, "\"starId\": \"earth_2\", \"grade\": 1", "\"starId\": \"earth_2\", \"grade\": 6")),
                ("등급 long 끝", j => R(j, "\"starId\": \"earth_2\", \"grade\": 1", "\"starId\": \"earth_2\", \"grade\": 9223372036854775807")),
                ("등급 int로 바꾸면 1이 되는 값", j => R(j, "\"starId\": \"earth_2\", \"grade\": 1", "\"starId\": \"earth_2\", \"grade\": 4294967297")),
                ("스킬 0", j => R(j, "\"grade\": 1, \"skillLevel\": 1}]", "\"grade\": 1, \"skillLevel\": 0}]")),
                ("스킬 31", j => R(j, "\"grade\": 1, \"skillLevel\": 1}]", "\"grade\": 1, \"skillLevel\": 31}]")),
                ("스킬 문자열", j => R(j, "\"grade\": 1, \"skillLevel\": 1}]", "\"grade\": 1, \"skillLevel\": \"1\"}]")),
                ("등급 키 빠짐", j => R(j, "\"starId\": \"earth_2\", \"grade\": 1, ", "\"starId\": \"earth_2\", ")),
                ("한 별이 두 칸", j => R(j, Board1, "\"1\": [1, 1, 0, 0, 0]")),
                ("한 별이 두 판", j => R(j, Board2, "\"2\": [1, 0, 0, 0, 0]")),
                ("가지지 않은 별", j => R(j, Board1, "\"1\": [1, 2, 9, 0, 0]")),
                ("음수 번호", j => R(j, Board1, "\"1\": [1, 2, -1, 0, 0]")),
                ("닫힌 판에 별", j => R(j, Board2, "\"2\": [0, 3, 0, 0, 0]")),
                ("칸 4개", j => R(j, Board1, "\"1\": [1, 2, 0, 0]")),
                ("칸 6개", j => R(j, Board1, "\"1\": [1, 2, 0, 0, 0, 0]")),
                ("칸이 문자열", j => R(j, Board1, "\"1\": [1, 2, 0, 0, \"0\"]")),
                ("판 빠짐", j => R(j, ", " + Board2, "")),
                ("없는 판", j => R(j, Board2, Board2 + ", \"3\": [0, 0, 0, 0, 0]")),
                ("판 키 앞 0", j => R(j, Board2, "\"02\": [0, 0, 0, 0, 0]")),
                ("판 키 0", j => R(j, Board2, "\"0\": [0, 0, 0, 0, 0]")),
                ("판 키 int 밖", j => R(j, Board2, "\"2147483648\": [0, 0, 0, 0, 0]")),
                ("판 키 같은 것 둘", j => R(j, Board2, Board1)),
                ("boards 없음", j => System.Text.RegularExpressions.Regex.Replace(j, ", \"boards\": \\{[^}]*\\}", "")),
                ("무료 11번 (표 열 범위 상한 10)", j => R(j, "\"used\": 1", "\"used\": 11")),
                ("무료 음수", j => R(j, "\"used\": 1", "\"used\": -1")),
                ("무료 쓴 날 없는데 1", j => System.Text.RegularExpressions.Regex.Replace(j, "\"freeDraw\": \\{\"day\": \\d+", "\"freeDraw\": {\"day\": null")),
                ("무료 쓴 날 0번", j => R(j, "\"used\": 1", "\"used\": 0")),
                ("무료 날 범위 밖", j => System.Text.RegularExpressions.Regex.Replace(j, "\"freeDraw\": \\{\"day\": \\d+", "\"freeDraw\": {\"day\": 9223372036854775807")),
                ("freeDraw 키 남음", j => R(j, "\"used\": 1", "\"used\": 1, \"x\": 0")),
                ("스테이지가 표에 없음", j => R(j, "\"stage\": 1", "\"stage\": 6")),
                ("떠날 때 스테이지가 표에 없음", j => R(j, "\"leaveStage\": 1", "\"leaveStage\": 6")),
                ("스테이지 int 끝", j => R(j, "\"stage\": 1", "\"stage\": 2147483647")),
            };
            foreach (var (why, make) in cases) yield return new TestCaseData(why, make).SetName("틀린_C4_저장_" + why.Replace(' ', '_'));
        }

        [TestCaseSource(nameof(BadSaves))]
        public void 틀린_C4_저장은_불러오지_않고_예외도_없다(string why, Func<string, string> make)
        {
            var good = BaseSave();
            var tables = C3Harness.Tables(Texts());
            Assert.That(GameSessionLoader.Load(tables, good, C3Harness.DefaultNonce).Ok, Is.True, "고치기 전 저장은 읽힘: " + good);
            var bad = make(good);
            SessionLoadResult r = null;
            Assert.DoesNotThrow(() => r = GameSessionLoader.Load(tables, bad, C3Harness.DefaultNonce), why);
            Assert.That(r.Ok, Is.False, why);
            Assert.That(r.Errors, Is.Not.Empty);
        }

        [Test]
        public void 판이_열린_뒤의_저장은_그_장에서만_읽힌다()
        {
            var h = New();
            h.S.SetChapterForTest(2);
            h.Ok(h.S.PlaceStar(2, 1, Own(h, "wood_1")));
            var json = h.S.CreateSave().Json;
            Assert.That(GameSessionLoader.Load(h.T, json, 1).Ok, Is.True);
            Assert.That(GameSessionLoader.Load(h.T, R(json, "\"chapter\": 2", "\"chapter\": 1"), 1).Ok, Is.False, "장을 1로 바꾸면 2장 판이 닫혀 있어야 함");
        }

        // ---- 위반 규칙 5·6 주입 ----

        [Test]
        public void 규칙5_같은_별_둘을_잡는다()
        {
            var h = New();
            Own(h, "wood_1");
            h.S.Stars.CorruptForTest(new OwnedStar(99, "wood_1"));
            h.S.Advance(0.01);
            Assert.That(h.S.Violations.Select(v => v.Rule), Has.Member(5));
        }

        [Test]
        public void 규칙5_두_칸의_별_가지지_않은_별_닫힌_판의_별을_잡는다()
        {
            foreach (var (why, corrupt) in new (string, Action<C3Harness, long>)[]
            {
                ("두 칸", (h, id) => { h.S.Boards.CorruptSlotForTest(1, 1, id); h.S.Boards.CorruptSlotForTest(1, 3, id); }),
                ("가지지 않은 별", (h, id) => h.S.Boards.CorruptSlotForTest(1, 2, id + 100)),
                ("닫힌 판", (h, id) => h.S.Boards.CorruptSlotForTest(2, 1, id)),
                ("두 판", (h, id) => { h.S.Boards.CorruptSlotForTest(1, 1, id); h.S.Boards.CorruptSlotForTest(2, 1, id); }),
            })
            {
                var h = New();
                var id = Own(h, "metal_1");
                corrupt(h, id);
                h.S.Advance(0.01);
                Assert.That(h.S.Violations.Select(v => v.Rule), Has.Member(5), why);
            }
        }

        [Test]
        public void 규칙5_등급_범위_밖을_잡는다()
        {
            var h = New();
            h.S.Stars.CorruptForTest(new OwnedStar(50, "water_3", 6, 1));
            h.S.Advance(0.01);
            Assert.That(h.S.Violations.Select(v => v.Rule), Has.Member(5));
        }

        [Test]
        public void 규칙6_켜진_선이_배치와_다르면_잡는다()
        {
            var h = New();
            var w1 = Own(h, "wood_1");
            var m1 = Own(h, "metal_1");
            h.Ok(h.S.PlaceStar(1, 1, w1));
            // 선 계산 없이 칸만 바꿈: 쇠는 나무와 상생이 아니므로 켜진 선은 없어야 하는데 목록은 그대로(비어 있음) → 바꿔 칸 2에 나무를 넣으면 선이 켜져야 함
            h.S.Boards.CorruptSlotForTest(1, 2, Own(h, "wood_2"));
            h.S.Advance(0.01);
            Assert.That(h.S.Violations.Select(v => v.Rule), Has.Member(6));
            Assert.That(h.S.Violations.Select(v => v.Rule), Has.No.Member(5));
        }

        [Test]
        public void 정상_명령에는_규칙_5_6_위반이_없다()
        {
            var h = New();
            Give(h, "gold", 100_000);
            h.Ok(h.S.DrawStar(10, DrawPayment.Crystal));
            h.Ok(h.S.AutoArrange(1));
            h.AssertNoViolations();
        }

        // ---- 무작위 명령 ----

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(20261119)]
        public void 무작위_C4_명령에도_위반이_없고_실패는_상태를_바꾸지_않고_언제_저장해도_다시_불러온다(int seed)
        {
            var rnd = new Random(seed);
            var h = New();
            var now = T0;
            var lastRequest = 1L;
            var changed = 0;
            for (var i = 0; i < 2000; i++)
            {
                var before = h.S.Capture();
                CommandResult r;
                long Pick() => rnd.Next(5) == 0 ? rnd.Next(-1, 40) : h.S.OwnedStars.Count == 0 ? 1 : h.S.OwnedStars[rnd.Next(h.S.OwnedStars.Count)].InstanceId;
                switch (rnd.Next(13))
                {
                    case 0:
                        if (rnd.Next(3) == 0) Give(h, "gold", rnd.Next(0, 5000));
                        before = h.S.Capture(); // 준비(재화 넣기)는 명령 전 상태에 넣는다
                        r = h.S.DrawStar(rnd.Next(4) == 0 ? rnd.Next(-1, 12) : rnd.Next(2) == 0 ? 1 : 10, (DrawPayment)rnd.Next(0, 4));
                        break;
                    case 1:
                        if (rnd.Next(3) == 0) Give(h, "shard", rnd.Next(0, 500));
                        before = h.S.Capture();
                        r = h.S.UpgradeStar(Pick());
                        break;
                    case 2:
                        if (rnd.Next(3) == 0) Give(h, "shard", rnd.Next(0, 500));
                        before = h.S.Capture();
                        r = h.S.UpgradeSkill(Pick());
                        break;
                    case 3:
                    case 4:
                        r = h.S.PlaceStar(rnd.Next(0, 4), rnd.Next(0, 7), Pick());
                        break;
                    case 5:
                        r = h.S.RemoveStar(rnd.Next(0, 4), rnd.Next(0, 7));
                        break;
                    case 6:
                        r = h.S.AutoArrange(rnd.Next(0, 4));
                        break;
                    case 7:
                        if (h.S.Chapter < 2 && rnd.Next(10) == 0) h.S.SetChapterForTest(2); // 장은 C5 전까지 오르기만
                        before = h.S.Capture();
                        r = h.S.ClaimAttendance();
                        break;
                    case 8:
                        now += rnd.Next(3) == 0 ? -rnd.Next(0, 3_600_000) : rnd.Next(0, 8 * 3_600_000);
                        if (now < 0) now = 0;
                        r = h.S.SyncServerTime(now, ++lastRequest);
                        break;
                    case 9:
                        r = h.S.Advance(rnd.NextDouble() * 0.5);
                        if (r.Ok && h.S.RunState == RunState.Active) now += 250;
                        break;
                    case 10:
                        r = h.S.ClaimMission(rnd.Next(2) == 0 ? "draw_any" : "place_any");
                        break;
                    case 11:
                        AssertRoundTrip(h, "무작위 " + i);
                        r = CommandResult.Success();
                        break;
                    default:
                        if (rnd.Next(5) == 0)
                        {
                            h.Restart((ulong)rnd.Next() << 16);
                            lastRequest = 0;
                        }
                        r = CommandResult.Success();
                        break;
                }
                if (!r.Ok)
                {
                    Assert.That(r.Events, Is.Empty, "명령 " + i);
                    Assert.That(h.S.Capture().Same(before), Is.True, "실패한 명령 " + i + ": " + r);
                }
                else if (!h.S.Capture().Same(before)) changed++;
                Assert.That(h.S.Violations, Is.Empty, "명령 " + i + ": " + string.Join("\n", h.S.Violations));
            }
            Assert.That(changed, Is.GreaterThan(300), "무작위 명령이 실제로 상태를 바꿈");
            Assert.That(h.S.OwnedStars.Count, Is.GreaterThan(5));
        }
    }
}
