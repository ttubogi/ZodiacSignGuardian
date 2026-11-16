using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZodiacSignGuardian.Core.Commands;
using ZodiacSignGuardian.Core.Economy;
using ZodiacSignGuardian.Core.Numbers;
using ZodiacSignGuardian.Core.Randomness;
using ZodiacSignGuardian.Core.Session;
using ZodiacSignGuardian.Core.Tests.Data;
using ZodiacSignGuardian.Core.Timing;
using ZodiacSignGuardian.Data.Tables;

namespace ZodiacSignGuardian.Core.Tests.Session
{
    [TestFixture]
    public class GameSessionTests
    {
        static readonly TimeSpan Kst = TimeSpan.FromHours(9);
        static long KstMs(int y, int mo, int d, int h, int mi, int s) => new DateTimeOffset(y, mo, d, h, mi, s, Kst).ToUnixTimeMilliseconds();

        static TableSet TestTableSet()
        {
            var r = TableBuilder.BuildFromTexts(TestTables.Good());
            Assert.That(r.Ok, Is.True, TestTables.Describe(r.Issues));
            return r.Tables;
        }

        static TableSet RepoTableSet()
        {
            var r = TableBuilder.LoadJson(RepoFiles.JsonTexts());
            Assert.That(r.Ok, Is.True, TestTables.Describe(r.Issues));
            return r.Tables;
        }

        static readonly GameSessionOptions Checked = new GameSessionOptions { CheckInvariants = true };

        static GameSession New(ulong seed = 1) => GameSessionLoader.NewAccount(TestTableSet(), seed, Checked);

        static void AssertNoViolations(GameSession s) => Assert.That(s.Violations, Is.Empty, string.Join("\n", s.Violations));

        static void AssertFailsUnchanged(GameSession s, Func<CommandResult> command, ErrorCode expected)
        {
            var before = s.Capture();
            var r = command();
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Error, Is.EqualTo(expected));
            Assert.That(r.Events, Is.Empty);
            Assert.That(s.Capture().Same(before), Is.True, "실패한 명령은 상태를 바꾸지 않음");
            AssertNoViolations(s);
        }

        // ---- 만들기 ----

        [Test]
        public void 새_계정은_지금_빌드의_재화가_0이고_정산_대기_미확인()
        {
            var s = New();
            Assert.That(s.CurrencyIds, Is.EqualTo(new[] { "gold", "dust" }), "v1 재화(dungeon_key)는 넣지 않음");
            Assert.That(s.Balance("gold"), Is.EqualTo(CurrencyAmount.Of(0L)));
            Assert.That(s.Balance("dust"), Is.EqualTo(CurrencyAmount.Of(GameNumber.Zero)));
            Assert.That(s.RunState, Is.EqualTo(RunState.Active));
            Assert.That(s.SettlementPending, Is.True);
            Assert.That(s.TimeStatus, Is.EqualTo(TimeStatus.Unverified));
            Assert.That(s.SaveRevision, Is.EqualTo(0));
            Assert.That(s.EstimatedUtcMs, Is.Null);
        }

        [Test]
        public void 저장소의_실제_표로_만들면_설정이_표와_같다()
        {
            var s = GameSessionLoader.NewAccount(RepoTableSet(), 1, Checked);
            Assert.That(s.CurrencyIds, Is.EqualTo(new[] { "stardust", "galaxy_crystal", "star_fragment", "enhance_stone" }));
            var c = s.Clock.Config;
            Assert.That((c.UtcOffsetMin, c.DailyResetHour, c.WeeklyResetHour, c.WeeklyWeekday), Is.EqualTo((540, 5, 5, 0)));
            Assert.That((c.SyncIntervalMin, c.AnchorValidMin, c.MaxRttSec, c.MaxFrameSec), Is.EqualTo((5, 10, 5, 1)));
        }

        // ---- Advance ----

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(-0.001)]
        [TestCase(1e10)]
        public void 잘못된_elapsedSec는_InvalidInput이고_상태는_그대로(double sec)
        {
            var s = New();
            s.Advance(0.3);
            AssertFailsUnchanged(s, () => s.Advance(sec), ErrorCode.InvalidInput);
        }

        [Test]
        public void 일초를_60번에_나눠도_전투_칸은_10번()
        {
            var s = New();
            long ticks = 0;
            for (var i = 0; i < 60; i++)
            {
                Assert.That(s.Advance(1.0 / 60.0).Ok, Is.True);
                ticks += s.LastAdvanceTicks;
            }
            Assert.That(ticks, Is.EqualTo(10));
            Assert.That(s.Clock.SyncElapsedUs, Is.EqualTo(1_000_000));
            Assert.That(s.Battle.PendingUs, Is.EqualTo(0));
            AssertNoViolations(s);
        }

        [Test]
        public void 긴_프레임은_전투에_1초만_인정하고_동기화_누적에는_전부_더한다()
        {
            var s = New();
            s.Advance(3.0);
            Assert.That(s.LastAdvanceTicks, Is.EqualTo(10));
            Assert.That(s.Clock.SyncElapsedUs, Is.EqualTo(3_000_000), "자르기 전 값");
        }

        [Test]
        public void 일시정지_중_Advance는_무시한다()
        {
            var s = New();
            s.Suspend();
            var before = s.Capture();
            var r = s.Advance(5.0);
            Assert.That(r.Ok, Is.True);
            Assert.That(r.Events, Is.Empty);
            Assert.That(s.LastAdvanceTicks, Is.EqualTo(0));
            Assert.That(s.Capture().Same(before), Is.True);
            AssertFailsUnchanged(s, () => s.Advance(double.NaN), ErrorCode.InvalidInput);
        }

        // ---- 동기화 ----

        [Test]
        public void 동기화를_받으면_유효가_되고_날과_주를_정한다()
        {
            var s = New();
            var r = s.SyncServerTime(KstMs(2026, 11, 16, 12, 0, 0), 1);
            Assert.That(r.Ok, Is.True);
            Assert.That(s.LastSyncOutcome, Is.EqualTo(SyncOutcome.Accepted));
            Assert.That(r.Events.OfType<TimeStatusChanged>().Single().Status, Is.EqualTo(TimeStatus.Valid));
            Assert.That(r.Events.OfType<DayReset>().Count(), Is.EqualTo(1));
            Assert.That(r.Events.OfType<WeekReset>().Count(), Is.EqualTo(1));
            Assert.That(s.EstimatedUtcMs, Is.EqualTo(KstMs(2026, 11, 16, 12, 0, 0)));
            s.Advance(1.5);
            Assert.That(s.EstimatedUtcMs, Is.EqualTo(KstMs(2026, 11, 16, 12, 0, 0) + 1500));
            AssertNoViolations(s);
        }

        [TestCase(-1L)]
        [TestCase(Clock.MaxServerUtcMs + 1)]
        public void 불가능한_서버_시각은_InvalidInput(long ms)
        {
            var s = New();
            AssertFailsUnchanged(s, () => s.SyncServerTime(ms, 1), ErrorCode.InvalidInput);
        }

        [Test]
        public void 일시정지_중에_온_응답은_버리고_복귀_뒤_요청으로만_유효()
        {
            var s = New();
            s.SyncServerTime(KstMs(2026, 11, 16, 12, 0, 0), 1);
            s.Suspend();
            var r = s.SyncServerTime(KstMs(2026, 11, 16, 12, 0, 1), 2);
            Assert.That(r.Ok && r.Events.Count == 0, Is.True);
            Assert.That(s.LastSyncOutcome, Is.EqualTo(SyncOutcome.IgnoredPaused));
            s.Resume(5);
            Assert.That(s.TimeStatus, Is.EqualTo(TimeStatus.Unverified));
            s.SyncServerTime(KstMs(2026, 11, 16, 12, 0, 2), 3); // 나가기 전에 보낸 3번이 복귀 뒤 도착
            Assert.That(s.LastSyncOutcome, Is.EqualTo(SyncOutcome.IgnoredStale));
            Assert.That(s.TimeStatus, Is.EqualTo(TimeStatus.Unverified));
            s.SyncServerTime(KstMs(2026, 11, 16, 13, 0, 0), 5);
            Assert.That(s.TimeStatus, Is.EqualTo(TimeStatus.Valid));
            AssertNoViolations(s);
        }

        // ---- 실행 상태 ----

        [Test]
        public void Suspend와_Resume을_두_번_연속하면_두_번째는_아무것도_안_한다()
        {
            var s = New();
            s.SyncServerTime(KstMs(2026, 11, 16, 12, 0, 0), 1);
            s.Suspend();
            var afterFirst = s.Capture();
            Assert.That(s.Suspend().Ok, Is.True);
            Assert.That(s.Capture().Same(afterFirst), Is.True);

            var r = s.Resume(2);
            Assert.That(r.Events.OfType<TimeStatusChanged>().Single().Status, Is.EqualTo(TimeStatus.Unverified));
            Assert.That(s.RunState, Is.EqualTo(RunState.Active));
            Assert.That(s.SettlementPending, Is.True);
            var afterResume = s.Capture();
            var r2 = s.Resume(9);
            Assert.That(r2.Ok && r2.Events.Count == 0, Is.True);
            Assert.That(s.Capture().Same(afterResume), Is.True);
            Assert.That(s.Clock.MinRequestId, Is.EqualTo(2), "두 번째 Resume은 번호도 바꾸지 않음");
            AssertNoViolations(s);
        }

        [TestCase(0L)]
        [TestCase(-5L)]
        public void Resume_번호가_1보다_작으면_InvalidInput(long next)
        {
            var s = New();
            s.Suspend();
            AssertFailsUnchanged(s, () => s.Resume(next), ErrorCode.InvalidInput);
            AssertFailsUnchanged(s, () => { s.Resume(1); s.Suspend(); return s.Resume(next); }, ErrorCode.InvalidInput);
        }

        [Test]
        public void 복귀하면_µs_미만_남는_값을_버린다()
        {
            var s = New();
            s.Advance(4e-7);
            Assert.That(s.Converter.ResidualUs, Is.Not.EqualTo(0.0));
            s.Suspend();
            s.Resume(2);
            Assert.That(s.Converter.ResidualUs, Is.EqualTo(0.0));
        }

        // ---- 저장 ----

        static GameSession Played(TableSet tables, ulong seed)
        {
            var s = GameSessionLoader.NewAccount(tables, seed, Checked);
            s.SyncServerTime(KstMs(2026, 11, 16, 4, 59, 0), 1);
            for (var i = 0; i < 100; i++) s.Advance(0.73);
            var events = new List<GameEvent>();
            s.Wallet.Apply(new[] { CurrencyChange.Of(s.CurrencyIds[0], 12345), CurrencyChange.Of(s.CurrencyIds[1], GameNumber.From(1.25, 40)) }, CurrencyReason.Hunt, events);
            for (var i = 0; i < 17; i++) s.Rng.NextUInt64(RngStream.Drop);
            return s;
        }

        [Test]
        public void 저장하고_불러오면_저장하는_값이_모두_같고_다시_저장한_JSON도_같다()
        {
            var tables = TestTableSet();
            var a = Played(tables, 77);
            var save = a.CreateSave();
            Assert.That(save.SaveRevision, Is.EqualTo(1));

            var loaded = GameSessionLoader.Load(tables, save.Json, Checked);
            Assert.That(loaded.Ok, Is.True, string.Join("\n", loaded.Errors));
            var b = loaded.Session;
            Assert.That(b.Wallet.SameState(a.Wallet), Is.True);
            Assert.That(b.Battle.SameState(a.Battle), Is.True);
            Assert.That(b.Rng.SameState(a.Rng), Is.True);
            Assert.That((b.Clock.AnchorUtcMs, b.Clock.SyncElapsedUs, b.Clock.ProcessedDay, b.Clock.ProcessedWeek),
                Is.EqualTo((a.Clock.AnchorUtcMs, a.Clock.SyncElapsedUs, a.Clock.ProcessedDay, a.Clock.ProcessedWeek)));
            Assert.That(b.SaveRevision, Is.EqualTo(1));

            // 앱 시작 순서: 활성, 정산 대기, 미확인, 최소 허용 번호 1
            Assert.That(b.RunState, Is.EqualTo(RunState.Active));
            Assert.That(b.SettlementPending, Is.True);
            Assert.That(b.TimeStatus, Is.EqualTo(TimeStatus.Unverified));
            Assert.That(b.Clock.MinRequestId, Is.EqualTo(1));

            Assert.That(b.CreateSave().Json, Is.EqualTo(a.CreateSave().Json), "다시 저장해도 같은 JSON");
            // 이어지는 난수도 같다
            Assert.That(b.Rng.NextUInt64(RngStream.Gacha), Is.EqualTo(a.Rng.NextUInt64(RngStream.Gacha)));
            AssertNoViolations(a);
            AssertNoViolations(b);
        }

        [Test]
        public void 저장_JSON에는_소수가_없고_큰_수는_문자열()
        {
            var a = Played(TestTableSet(), 3);
            var json = a.CreateSave().Json;
            Assert.That(json, Does.Contain("\"dust\": \"1.25e40\""));
            Assert.That(json, Does.Contain("\"gold\": 12345"));
            foreach (var token in json.Split(new[] { ' ', ',', ':', '[', ']', '{', '}' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith("\"")) continue;
                Assert.That(token == "null" || long.TryParse(token, out _), Is.True, "따옴표 밖 값은 정수 또는 null: " + token);
            }
        }

        [Test]
        public void 같은_시드_같은_명령이면_같은_저장()
        {
            var tables = TestTableSet();
            Assert.That(Played(tables, 5).CreateSave().Json, Is.EqualTo(Played(tables, 5).CreateSave().Json));
            Assert.That(Played(tables, 5).CreateSave().Json, Is.Not.EqualTo(Played(tables, 6).CreateSave().Json));
        }

        [Test]
        public void 저장_번호는_1씩_오르고_클라우드를_고르지_않으면_클라우드_번호_더하기_1()
        {
            var s = New();
            Assert.That(s.CreateSave().SaveRevision, Is.EqualTo(1));
            Assert.That(s.CreateSave().SaveRevision, Is.EqualTo(2));
            Assert.That(s.AdoptCloudRevision(7).Ok, Is.True);
            Assert.That(s.CreateSave().SaveRevision, Is.EqualTo(8));
            AssertFailsUnchanged(s, () => s.AdoptCloudRevision(-1), ErrorCode.InvalidInput);
            AssertFailsUnchanged(s, () => s.AdoptCloudRevision(long.MaxValue), ErrorCode.InvalidInput);
        }

        [Test]
        public void 요약은_장_별가루_마지막_저장_시각이고_표_없이도_읽는다()
        {
            var tables = RepoTableSet();
            var s = GameSessionLoader.NewAccount(tables, 9, Checked);
            Assert.That(s.CreateSave().Summary.LastSavedUtcMs, Is.Null, "서버 시각을 받기 전");
            s.SyncServerTime(KstMs(2026, 11, 16, 20, 0, 0), 1);
            s.Wallet.Apply(CurrencyChange.Of("stardust", GameNumber.From(3.5, 12)), CurrencyReason.Hunt, new List<GameEvent>());
            s.Advance(0.25);
            var save = s.CreateSave();
            Assert.That(save.Summary.SaveRevision, Is.EqualTo(2));
            Assert.That(save.Summary.Chapter, Is.EqualTo(1));
            Assert.That(save.Summary.Stardust, Is.EqualTo(GameNumber.From(3.5, 12)));
            Assert.That(save.Summary.LastSavedUtcMs, Is.EqualTo(KstMs(2026, 11, 16, 20, 0, 0) + 250));

            Assert.That(GameSessionLoader.TryReadSummary(save.Json, out var read), Is.True);
            Assert.That((read.SaveRevision, read.Chapter, read.Stardust, read.LastSavedUtcMs),
                Is.EqualTo((save.Summary.SaveRevision, save.Summary.Chapter, save.Summary.Stardust, save.Summary.LastSavedUtcMs)));
            Assert.That(GameSessionLoader.TryReadSummary("{}", out _), Is.False);
            Assert.That(GameSessionLoader.TryReadSummary(null, out _), Is.False);
        }

        // ---- 틀린 저장 ----

        static string GoodSave(out TableSet tables)
        {
            tables = TestTableSet();
            return Played(tables, 11).CreateSave().Json;
        }

        static IEnumerable<TestCaseData> BrokenSaves()
        {
            string R(string json, string from, string to)
            {
                Assert.That(json, Does.Contain(from), "바꿀 문구가 있어야 함: " + from);
                return json.Replace(from, to);
            }
            var cases = new List<(string why, Func<string, string> make)>
            {
                ("JSON이 아님", j => "not json"),
                ("빈 문자열", j => ""),
                ("배열", j => "[]"),
                ("소수", j => R(j, "\"saveVersion\": 1", "\"saveVersion\": 1.0")),
                ("long을 넘는 정수", j => R(j, "\"gold\": 12345", "\"gold\": 99999999999999999999")),
                ("모르는 saveVersion", j => R(j, "\"saveVersion\": 1", "\"saveVersion\": 2")),
                ("saveRevision 0", j => R(j, "\"saveRevision\": 1", "\"saveRevision\": 0")),
                ("빠진 키", j => R(j, "\"progress\": {\"chapter\": 1}, ", "")),
                ("남는 키", j => R(j, "\"saveVersion\": 1", "\"saveVersion\": 1, \"cheat\": true")),
                ("겹친 키", j => R(j, "\"saveVersion\": 1", "\"saveVersion\": 1, \"saveVersion\": 1")),
                ("음수 정수 재화", j => R(j, "\"gold\": 12345", "\"gold\": -1")),
                ("음수 큰 수 재화", j => R(j, "\"dust\": \"1.25e40\"", "\"dust\": \"-1.25e40\"")),
                ("정수 재화가 문자열", j => R(j, "\"gold\": 12345", "\"gold\": \"12345\"")),
                ("큰 수 재화가 숫자", j => R(j, "\"dust\": \"1.25e40\"", "\"dust\": 125")),
                ("큰 수 형식 오류", j => R(j, "\"dust\": \"1.25e40\"", "\"dust\": \"12.5e39\"")),
                ("표에 없는 재화", j => R(j, "\"gold\": 12345", "\"gold\": 12345, \"dungeon_key\": 1")),
                ("빠진 재화", j => R(j, "\"gold\": 12345, ", "")),
                ("rng 11개", j => DropLastRng(j)),
                ("rng 대문자", j => System.Text.RegularExpressions.Regex.Replace(j, "\"rng\": \\[\"([0-9a-f])", m => "\"rng\": [\"" + "A")),
                ("rng 전투 흐름이 모두 0", j => ZeroCombat(j)),
                ("전투 누적 0.1초", j => System.Text.RegularExpressions.Regex.Replace(j, "\"battlePendingUs\": \\d+", "\"battlePendingUs\": 100000")),
                ("전투 누적 음수", j => System.Text.RegularExpressions.Regex.Replace(j, "\"battlePendingUs\": \\d+", "\"battlePendingUs\": -1")),
                ("기준 시각 음수", j => System.Text.RegularExpressions.Regex.Replace(j, "\"anchorUtcMs\": \\d+", "\"anchorUtcMs\": -1")),
                ("기준 시각 없이 처리한 날", j => System.Text.RegularExpressions.Regex.Replace(j, "\"anchorUtcMs\": \\d+, \"syncElapsedUs\": \\d+", "\"anchorUtcMs\": null, \"syncElapsedUs\": 0")),
                ("동기화 누적 음수", j => System.Text.RegularExpressions.Regex.Replace(j, "\"syncElapsedUs\": \\d+", "\"syncElapsedUs\": -5")),
                ("장 0", j => R(j, "\"chapter\": 1", "\"chapter\": 0")),
                ("장이 int를 넘음", j => R(j, "\"chapter\": 1", "\"chapter\": 3000000000")),
                ("저장 시각 범위 밖", j => System.Text.RegularExpressions.Regex.Replace(j, "\"lastSavedUtcMs\": \\d+", "\"lastSavedUtcMs\": 253402300800000")),
                ("clock이 배열", j => System.Text.RegularExpressions.Regex.Replace(j, "\"clock\": \\{[^}]*\\}", "\"clock\": []")),
            };
            foreach (var (why, make) in cases) yield return new TestCaseData(why, make).SetName("틀린_저장_" + why);
        }

        static string DropLastRng(string j)
        {
            var start = j.IndexOf("\"rng\": [", StringComparison.Ordinal) + "\"rng\": [".Length;
            var end = j.IndexOf(']', start);
            var items = j.Substring(start, end - start).Split(',').Select(x => x.Trim()).ToList();
            items.RemoveAt(items.Count - 1);
            return j.Substring(0, start) + string.Join(", ", items) + j.Substring(end);
        }

        static string ZeroCombat(string j)
        {
            var start = j.IndexOf("\"rng\": [", StringComparison.Ordinal) + "\"rng\": [".Length;
            var items = j.Substring(start, j.IndexOf(']', start) - start).Split(',').Select(x => x.Trim()).ToList();
            for (var i = 8; i < 12; i++) items[i] = "\"0000000000000000\"";
            return j.Substring(0, start) + string.Join(", ", items) + j.Substring(j.IndexOf(']', start));
        }

        [TestCaseSource(nameof(BrokenSaves))]
        public void 틀린_저장은_불러오지_않고_예외도_없다(string why, Func<string, string> make)
        {
            var good = GoodSave(out var tables);
            Assert.That(GameSessionLoader.Load(tables, good).Ok, Is.True, "고치기 전 저장은 읽힘");
            var bad = make(good);
            Assert.That(bad, Is.Not.EqualTo(good), why + ": 실제로 바뀌어야 함");
            SessionLoadResult r = null;
            Assert.DoesNotThrow(() => r = GameSessionLoader.Load(tables, bad), why);
            Assert.That(r.Ok, Is.False, why);
            Assert.That(r.Session, Is.Null);
            Assert.That(r.Errors, Is.Not.Empty, why);
        }

        [Test]
        public void 저장_키_순서가_달라도_읽는다()
        {
            var good = GoodSave(out var tables);
            // 맨 위 키 순서를 뒤집는다: rng를 맨 앞으로
            var rngStart = good.IndexOf(", \"rng\": [", StringComparison.Ordinal);
            var rngPart = good.Substring(rngStart + 2, good.Length - 1 - (rngStart + 2));
            var reordered = "{" + rngPart + ", " + good.Substring(1, rngStart - 1) + "}";
            Assert.That(reordered, Is.Not.EqualTo(good));
            var r = GameSessionLoader.Load(tables, reordered);
            Assert.That(r.Ok, Is.True, string.Join("\n", r.Errors));
            Assert.That(r.Session.CreateSave().Json, Is.EqualTo(GameSessionLoader.Load(tables, good).Session.CreateSave().Json));
        }

        // ---- 위반 검사 ----

        [Test]
        public void 실패하면서_상태를_바꾼_명령을_잡는다_규칙7()
        {
            var s = New();
            s.Execute("망가진 명령", events =>
            {
                s.Battle.Add(50_000, 1_000_000); // 조건 확인 전에 바꿈
                return ErrorCode.NotReady;
            });
            Assert.That(s.Violations.Select(v => v.Rule), Is.EqualTo(new[] { 7 }));
        }

        [Test]
        public void 음수_재화를_잡는다_규칙1()
        {
            var s = New();
            s.Execute("망가뜨림", events =>
            {
                s.Wallet.CorruptBalanceForTest("gold", CurrencyAmount.Of(-5L));
                return null;
            });
            Assert.That(s.Violations.Select(v => v.Rule), Is.EqualTo(new[] { 1 }));
            Assert.That(s.Violations[0].Message, Does.Contain("gold"));
        }

        [Test]
        public void 처리한_구간이_뒤로_가면_잡는다_규칙10()
        {
            var s = New();
            s.SyncServerTime(KstMs(2026, 11, 18, 12, 0, 0), 1);
            var c = s.Clock;
            s.Execute("망가뜨림", events =>
            {
                c.TryRestore(c.AnchorUtcMs, c.SyncElapsedUs, c.ProcessedDay - 1, c.ProcessedWeek);
                return null;
            });
            Assert.That(s.Violations.Select(v => v.Rule), Is.EqualTo(new[] { 10 }));
        }

        [Test]
        public void 위반_검사를_끄면_보지_않는다()
        {
            var s = GameSessionLoader.NewAccount(TestTableSet(), 1, new GameSessionOptions { CheckInvariants = false });
            s.Execute("망가뜨림", events =>
            {
                s.Wallet.CorruptBalanceForTest("gold", CurrencyAmount.Of(-5L));
                return ErrorCode.NotReady;
            });
            Assert.That(s.Violations, Is.Empty);
        }

        [Test]
        public void 위반_검사_함수는_망가뜨린_값을_모두_잡는다()
        {
            var found = new List<InvariantViolation>();
            Invariants.CheckCurrencies(new[]
            {
                new KeyValuePair<string, CurrencyAmount>("a", CurrencyAmount.Of(-1L)),
                new KeyValuePair<string, CurrencyAmount>("b", CurrencyAmount.Of(GameNumber.From(-2))),
                new KeyValuePair<string, CurrencyAmount>("c", CurrencyAmount.Of(0L)),
            }, found);
            Invariants.CheckPeriodsForward(10, 3, 9, 3, found);
            Invariants.CheckPeriodsForward(10, 3, 10, 2, found);
            Invariants.CheckPeriodsForward(10, 3, null, null, found);
            Invariants.CheckPeriodsForward(null, null, 1, 1, found); // 처음 정함: 문제 없음
            Invariants.CheckPeriodsForward(10, 3, 12, 4, found);     // 앞으로: 문제 없음
            Invariants.CheckFailedCommandUnchanged("x", false, found);
            Invariants.CheckFailedCommandUnchanged("y", true, found);
            Assert.That(found.Select(v => v.Rule), Is.EqualTo(new[] { 1, 1, 10, 10, 10, 10, 7 }));
        }

        [Test]
        public void 무작위_명령_수천_개에도_위반이_없다()
        {
            var s = New(2026);
            var rng = new System.Random(1116);
            long requestId = 0, nextResume = 1;
            var server = KstMs(2026, 11, 16, 3, 0, 0);
            for (var i = 0; i < 5000; i++)
            {
                switch (rng.Next(10))
                {
                    case 0: s.SyncServerTime(server + rng.Next(-5000, 5000), ++requestId); break;
                    case 1: s.Suspend(); break;
                    case 2: nextResume = requestId + 1; s.Resume(nextResume); break;
                    case 3: s.Advance(rng.Next(3) == 0 ? double.NaN : -rng.NextDouble()); break;
                    case 4: s.SyncServerTime(rng.Next(2) == 0 ? -1 : server, Math.Max(1, requestId - rng.Next(3))); break;
                    case 5: s.CreateSave(); break;
                    default:
                        var e = rng.NextDouble() * (rng.Next(20) == 0 ? 120.0 : 0.05);
                        s.Advance(e);
                        server += (long)(e * 1000);
                        break;
                }
            }
            AssertNoViolations(s);
            var reloaded = GameSessionLoader.Load(TestTableSet(), s.CreateSave().Json, Checked);
            Assert.That(reloaded.Ok, Is.True, string.Join("\n", reloaded.Errors));
        }
    }
}
