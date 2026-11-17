using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using ZodiacSignGuardian.Core.Commands;
using ZodiacSignGuardian.Core.Timing;

namespace ZodiacSignGuardian.Core.Tests.Timing
{
    [TestFixture]
    public class ElapsedConverterTests
    {
        static long Total(IEnumerable<double> frames)
        {
            var c = new ElapsedConverter();
            return frames.Sum(f => c.Convert(f));
        }

        [TestCase(0.05, 20)]
        [TestCase(1.0 / 60.0, 60)]
        [TestCase(0.1, 10)]
        [TestCase(1.0, 1)]
        [TestCase(1.0 / 30.0, 30)]
        [TestCase(1.0 / 144.0, 144)]
        public void 설계서의_나눔은_정확히_1초(double frame, int count)
        {
            Assert.That(Total(Enumerable.Repeat(frame, count)), Is.EqualTo(1_000_000L));
        }

        [Test]
        public void 호출마다_따로_반올림하지_않는다()
        {
            // 따로 반올림하면 16,667 × 60 = 1,000,020
            var c = new ElapsedConverter();
            var each = Enumerable.Range(0, 60).Select(_ => c.Convert(1.0 / 60.0)).ToList();
            Assert.That(each.Distinct().OrderBy(x => x), Is.EqualTo(new[] { 16666L, 16667L }));
            Assert.That(each.Sum(), Is.EqualTo(1_000_000L));
            Assert.That(Math.Abs(c.ResidualUs), Is.LessThan(1e-6));
        }

        /// <summary>double 값을 정확한 유리수(µs)로: 분자 / 2^1100</summary>
        static BigInteger ExactMicrosScaled(double d)
        {
            var bits = BitConverter.DoubleToInt64Bits(d);
            var exp = (int)((bits >> 52) & 0x7FF);
            var mant = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else mant |= 1L << 52;
            // d = mant * 2^(exp - 1075)
            return new BigInteger(mant) * 1_000_000 * BigInteger.Pow(2, 1100 + exp - 1075);
        }

        [Test]
        public void 무작위로_나눠도_합계가_정확한_값과_1µs_이내()
        {
            var rng = new System.Random(3);
            var scale = BigInteger.Pow(2, 1100);
            for (var round = 0; round < 200; round++)
            {
                var frames = Enumerable.Range(0, rng.Next(1, 3000)).Select(_ => rng.NextDouble() * (rng.Next(10) == 0 ? 5.0 : 0.05)).ToList();
                var exact = frames.Aggregate(BigInteger.Zero, (s, f) => s + ExactMicrosScaled(f));
                var got = new BigInteger(Total(frames)) * scale;
                Assert.That(BigInteger.Abs(got - exact) <= scale, Is.True, "프레임 " + frames.Count + "개");
            }
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(-1e-9)]
        [TestCase(-1.0)]
        [TestCase(1e9 + 1)]
        public void 잘못된_입력(double sec)
        {
            Assert.That(ElapsedConverter.IsValid(sec), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ElapsedConverter().Convert(sec));
        }

        [TestCase(0.0, 0L)]
        [TestCase(-0.0, 0L)]
        [TestCase(1e9, 1_000_000_000_000_000L)]
        [TestCase(4e-7, 0L)]
        [TestCase(6e-7, 1L)]
        public void 경계값(double sec, long us)
        {
            Assert.That(ElapsedConverter.IsValid(sec), Is.True);
            Assert.That(new ElapsedConverter().Convert(sec), Is.EqualTo(us));
        }

        [Test]
        public void 아주_짧은_프레임도_모이면_시간이_된다()
        {
            // 0.4µs × 5 = 2µs (따로 반올림하면 0)
            Assert.That(Total(Enumerable.Repeat(4e-7, 5)), Is.EqualTo(2L));
        }

        [Test]
        public void Reset은_남는_값을_버린다()
        {
            var c = new ElapsedConverter();
            c.Convert(4e-7);
            Assert.That(c.ResidualUs, Is.Not.EqualTo(0.0));
            c.Reset();
            Assert.That(c.ResidualUs, Is.EqualTo(0.0));
        }
    }

    [TestFixture]
    public class BattleAccumulatorTests
    {
        const long MaxFrame = 1_000_000L;

        [Test]
        public void 삼초_한_번과_일초_세_번()
        {
            // 설계서 시나리오: 3초×1번은 1초만 인정(2초 버림), 1초×3번은 3초
            var a = new BattleAccumulator();
            Assert.That(a.Add(3_000_000, MaxFrame), Is.EqualTo(10));
            Assert.That(a.LastDiscardedUs, Is.EqualTo(2_000_000));
            var b = new BattleAccumulator();
            Assert.That(Enumerable.Range(0, 3).Sum(_ => b.Add(1_000_000, MaxFrame)), Is.EqualTo(30));
            Assert.That(b.LastDiscardedUs, Is.EqualTo(0));
        }

        [Test]
        public void 일초_경계()
        {
            var a = new BattleAccumulator();
            Assert.That(a.Add(1_000_000, MaxFrame), Is.EqualTo(10));
            Assert.That(a.LastDiscardedUs, Is.EqualTo(0), "정확히 1초는 버리지 않음");
            Assert.That(a.Add(1_000_001, MaxFrame), Is.EqualTo(10));
            Assert.That(a.LastDiscardedUs, Is.EqualTo(1), "1초 초과분만 버림");
        }

        [Test]
        public void 영점일초_미만은_다음으로_남는다()
        {
            var a = new BattleAccumulator();
            var conv = new ElapsedConverter();
            long ticks = 0;
            for (var i = 0; i < 60; i++) ticks += a.Add(conv.Convert(1.0 / 60.0), MaxFrame);
            Assert.That(ticks, Is.EqualTo(10));
            Assert.That(a.PendingUs, Is.EqualTo(0));
            Assert.That(a.Add(99_999, MaxFrame), Is.EqualTo(0));
            Assert.That(a.PendingUs, Is.EqualTo(99_999));
            Assert.That(a.Add(1, MaxFrame), Is.EqualTo(1));
            Assert.That(a.PendingUs, Is.EqualTo(0));
        }

        [Test]
        public void 되돌리기는_0이상_0점1초_미만만()
        {
            var a = new BattleAccumulator();
            Assert.That(a.TryRestore(99_999), Is.True);
            Assert.That(a.TryRestore(100_000), Is.False);
            Assert.That(a.TryRestore(-1), Is.False);
            Assert.That(a.PendingUs, Is.EqualTo(99_999));
        }
    }

    [TestFixture]
    public class ClockTests
    {
        static readonly TimeSpan Kst = TimeSpan.FromHours(9);

        static ClockConfig Config(int weekday = 0, int offsetMin = 540) => new ClockConfig(offsetMin, 5, 5, weekday, 5, 10, 5, 1);

        static long KstMs(int y, int mo, int d, int h, int mi, int s, int ms = 0) => new DateTimeOffset(y, mo, d, h, mi, s, ms, Kst).ToUnixTimeMilliseconds();

        static List<GameEvent> Sync(Clock c, long ms, long id)
        {
            var e = new List<GameEvent>();
            Assert.That(c.Sync(ms, id, e), Is.EqualTo(SyncOutcome.Accepted));
            return e;
        }

        static List<GameEvent> Advance(Clock c, long us)
        {
            var e = new List<GameEvent>();
            c.Advance(us, e);
            return e;
        }

        [Test]
        public void 처음_동기화는_유효로_바꾸고_첫_구간을_정한다()
        {
            var c = new Clock(Config());
            Assert.That(c.Status, Is.EqualTo(TimeStatus.Unverified));
            Assert.That(c.DateGate(), Is.EqualTo(ErrorCode.TimeNotVerified));
            Assert.That(c.EstimatedUtcUs, Is.Null);
            var events = Sync(c, KstMs(2026, 11, 16, 12, 0, 0), 1);
            Assert.That(events.OfType<TimeStatusChanged>().Single().Status, Is.EqualTo(TimeStatus.Valid));
            Assert.That(events.OfType<DayReset>().Single().Previous, Is.Null);
            Assert.That(events.OfType<WeekReset>().Single().Previous, Is.Null);
            Assert.That(c.DateGate(), Is.Null);
        }

        [Test]
        public void 오전_4시59분59초와_5시는_다른_날()
        {
            var c = new Clock(Config());
            var before = c.DayIdAt(KstMs(2026, 11, 16, 4, 59, 59, 999) * 1000L);
            var at = c.DayIdAt(KstMs(2026, 11, 16, 5, 0, 0) * 1000L);
            Assert.That(at - before, Is.EqualTo(1));
            Assert.That(c.DayIdAt(KstMs(2026, 11, 17, 4, 59, 59, 999) * 1000L), Is.EqualTo(at), "다음 날 4시 59분까지 같은 날");
            Assert.That(c.DayStartUtcUs(at), Is.EqualTo(KstMs(2026, 11, 16, 5, 0, 0) * 1000L));
        }

        [Test]
        public void 시간이_흘러_5시를_넘으면_하루가_넘어간다()
        {
            var c = new Clock(Config());
            Sync(c, KstMs(2026, 11, 16, 4, 59, 59), 1);
            var day = c.ProcessedDay;
            Assert.That(Advance(c, 999_999).OfType<DayReset>(), Is.Empty, "4시 59분 59.999999초");
            var e = Advance(c, 1);
            Assert.That(e.OfType<DayReset>().Single().Day, Is.EqualTo(day + 1));
            Assert.That(e.OfType<DayReset>().Single().Previous, Is.EqualTo(day));
            Assert.That(Advance(c, 1_000_000).OfType<DayReset>(), Is.Empty, "같은 날은 한 번만");
        }

        [Test]
        public void 월요일_5시에_주가_넘어간다()
        {
            // 2026-11-16은 월요일
            Assert.That(new DateTime(2026, 11, 16).DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
            var c = new Clock(Config(weekday: 0));
            var sunNight = c.WeekIdAt(KstMs(2026, 11, 16, 4, 59, 59) * 1000L);
            var mon = c.WeekIdAt(KstMs(2026, 11, 16, 5, 0, 0) * 1000L);
            Assert.That(mon - sunNight, Is.EqualTo(1));
            Assert.That(c.WeekIdAt(KstMs(2026, 11, 23, 4, 59, 59) * 1000L), Is.EqualTo(mon), "다음 월요일 4시 59분까지 같은 주");
            Assert.That(c.WeekIdAt(KstMs(2026, 11, 23, 5, 0, 0) * 1000L), Is.EqualTo(mon + 1));
            Assert.That(c.WeekIdAt(KstMs(2026, 11, 18, 5, 0, 0) * 1000L), Is.EqualTo(mon), "수요일 5시는 주가 안 바뀜");
        }

        [TestCase(1, 2026, 11, 17)]
        [TestCase(6, 2026, 11, 22)]
        [TestCase(3, 2026, 11, 19)]
        public void 주의_시작_요일은_표의_weekday를_따른다(int weekday, int y, int m, int d)
        {
            var c = new Clock(Config(weekday: weekday));
            var before = c.WeekIdAt(KstMs(y, m, d, 4, 59, 59) * 1000L);
            var at = c.WeekIdAt(KstMs(y, m, d, 5, 0, 0) * 1000L);
            Assert.That(at - before, Is.EqualTo(1));
            Assert.That(c.WeekIdAt(KstMs(y, m, d, 5, 0, 0) * 1000L + 6 * ClockConfig.UsPerDay), Is.EqualTo(at));
            Assert.That(c.WeekIdAt(KstMs(y, m, d, 5, 0, 0) * 1000L + 7 * ClockConfig.UsPerDay), Is.EqualTo(at + 1));
        }

        [Test]
        public void UTC_차이가_음수여도_1970년_근처에서_내림이_맞다()
        {
            var c = new Clock(Config(offsetMin: -300)); // UTC-5
            // UTC 0시 = 현지 전날 19시 → 5시 기준 같은 날(1969-12-31) = -1
            Assert.That(c.DayIdAt(0), Is.EqualTo(-1));
            // UTC 10시 = 현지 5시 → 0
            Assert.That(c.DayIdAt(10 * ClockConfig.UsPerHour), Is.EqualTo(0));
            Assert.That(c.DayIdAt(10 * ClockConfig.UsPerHour - 1), Is.EqualTo(-1));
        }

        [Test]
        public void 여러_날을_건너뛰어도_DayReset은_한_번()
        {
            var c = new Clock(Config());
            Sync(c, KstMs(2026, 11, 16, 12, 0, 0), 1);
            var d0 = c.ProcessedDay;
            var e = Sync(c, KstMs(2026, 11, 19, 12, 0, 0), 2);
            var reset = e.OfType<DayReset>().Single();
            Assert.That(reset.Previous, Is.EqualTo(d0));
            Assert.That(reset.Day, Is.EqualTo(d0 + 3));
        }

        [Test]
        public void 같은_번호가_두_번_오면_두_번째는_버린다()
        {
            var c = new Clock(Config());
            Sync(c, KstMs(2026, 11, 16, 12, 0, 0), 1);
            var before = c.Clone();
            var e = new List<GameEvent>();
            Assert.That(c.Sync(KstMs(2026, 11, 16, 13, 0, 0), 1, e), Is.EqualTo(SyncOutcome.IgnoredStale));
            Assert.That(e, Is.Empty);
            Assert.That(c.SameState(before), Is.True);
        }

        [Test]
        public void 늦게_온_이전_번호는_버린다()
        {
            var c = new Clock(Config());
            Sync(c, KstMs(2026, 11, 16, 12, 0, 0), 7);
            Assert.That(c.Sync(KstMs(2026, 11, 16, 12, 0, 1), 6, new List<GameEvent>()), Is.EqualTo(SyncOutcome.IgnoredStale));
            Assert.That(c.AnchorUtcMs, Is.EqualTo(KstMs(2026, 11, 16, 12, 0, 0)));
        }

        [Test]
        public void 나가기_전_요청의_응답이_복귀_뒤_오면_버린다()
        {
            var c = new Clock(Config());
            Sync(c, KstMs(2026, 11, 16, 12, 0, 0), 3);
            // 4번을 보내고 나감 → 복귀, 다음 요청은 10번부터
            var e = new List<GameEvent>();
            c.BeginUnverified(10, e);
            Assert.That(e.OfType<TimeStatusChanged>().Single().Status, Is.EqualTo(TimeStatus.Unverified));
            Assert.That(c.Sync(KstMs(2026, 11, 16, 12, 0, 5), 4, new List<GameEvent>()), Is.EqualTo(SyncOutcome.IgnoredStale));
            Assert.That(c.Status, Is.EqualTo(TimeStatus.Unverified), "버린 응답으로 유효가 되지 않음");
            Sync(c, KstMs(2026, 11, 16, 14, 0, 0), 10);
            Assert.That(c.Status, Is.EqualTo(TimeStatus.Valid));
        }

        [Test]
        public void 앱을_다시_켜면_1번부터_받는다()
        {
            var c = new Clock(Config());
            Sync(c, KstMs(2026, 11, 16, 12, 0, 0), 500);
            Advance(c, 3_000_000);
            var r = new Clock(Config());
            Assert.That(r.TryRestore(c.AnchorUtcMs, c.SyncElapsedUs, c.ProcessedDay, c.ProcessedWeek), Is.True);
            Assert.That(r.Status, Is.EqualTo(TimeStatus.Unverified));
            Assert.That(r.MinRequestId, Is.EqualTo(1));
            Assert.That(r.DateGate(), Is.EqualTo(ErrorCode.TimeNotVerified));
            var e = Sync(r, KstMs(2026, 11, 16, 12, 30, 0), 1);
            Assert.That(r.Status, Is.EqualTo(TimeStatus.Valid));
            Assert.That(e.OfType<DayReset>(), Is.Empty, "같은 날이라 다시 넘기지 않음");
        }

        [Test]
        public void 짧게_나갔다_와서_같은_시각을_받아도_중복이_없다()
        {
            var c = new Clock(Config());
            Sync(c, KstMs(2026, 11, 16, 12, 0, 0), 1);
            c.BeginUnverified(2, new List<GameEvent>());
            var e = Sync(c, KstMs(2026, 11, 16, 12, 0, 0), 2);
            Assert.That(c.Status, Is.EqualTo(TimeStatus.Valid));
            Assert.That(e.OfType<DayReset>().Concat<GameEvent>(e.OfType<WeekReset>()), Is.Empty);
        }

        [Test]
        public void 추정이_서버보다_8초_앞섰다가_새_응답이_오면_서버_시각을_따르고_중복이_없다()
        {
            var c = new Clock(Config());
            Sync(c, KstMs(2026, 11, 16, 4, 59, 55), 1);
            // 기기 시간이 빨라 추정이 5시를 넘김 → 하루 넘김
            var e1 = Advance(c, 13_000_000);
            Assert.That(e1.OfType<DayReset>().Count(), Is.EqualTo(1));
            var processed = c.ProcessedDay;
            // 서버는 실제로 5시 0분 0초(추정보다 8초 늦음)
            var e2 = Sync(c, KstMs(2026, 11, 16, 5, 0, 0), 2);
            Assert.That(c.EstimatedUtcUs, Is.EqualTo(KstMs(2026, 11, 16, 5, 0, 0) * 1000L));
            Assert.That(e2.OfType<DayReset>(), Is.Empty);
            Assert.That(c.ProcessedDay, Is.EqualTo(processed));
            Assert.That(c.DateGate(), Is.Null);
        }

        [Test]
        public void 서버_시각이_처리한_날보다_이전이면_TimeWentBack이고_따라잡으면_풀린다()
        {
            var c = new Clock(Config());
            // 수요일: 주 경계(월요일 5시)와 겹치지 않게 해서 일일 구간만 뒤로 가게 한다
            Sync(c, KstMs(2026, 11, 18, 5, 0, 3), 1);
            var processed = c.ProcessedDay;
            var week = c.ProcessedWeek;
            Sync(c, KstMs(2026, 11, 18, 4, 59, 58), 2); // 서버가 앞의 날로
            Assert.That(c.WeekIdAt(c.EstimatedUtcUs.Value), Is.EqualTo(week), "주는 그대로");
            Assert.That(c.ProcessedDay, Is.EqualTo(processed), "처리한 구간은 뒤로 가지 않음");
            Assert.That(c.DateGate(), Is.EqualTo(ErrorCode.TimeWentBack));
            var e = Advance(c, 2_000_000);
            Assert.That(c.DateGate(), Is.Null);
            Assert.That(e.OfType<DayReset>(), Is.Empty, "이미 처리한 날");
        }

        [Test]
        public void 주가_뒤로_가도_TimeWentBack()
        {
            var c = new Clock(Config(weekday: 0));
            Sync(c, KstMs(2026, 11, 16, 5, 0, 1), 1); // 월요일 5시 직후
            Sync(c, KstMs(2026, 11, 16, 4, 0, 0), 2); // 일요일 밤(전 주, 전 날)
            Assert.That(c.DateGate(), Is.EqualTo(ErrorCode.TimeWentBack));
        }

        [Test]
        public void 기준점이_10분을_넘으면_재확인_필요_정확히_10분은_유효()
        {
            var c = new Clock(Config());
            Sync(c, KstMs(2026, 11, 16, 12, 0, 0), 1);
            Assert.That(Advance(c, 10 * ClockConfig.UsPerMin), Is.Empty);
            Assert.That(c.Status, Is.EqualTo(TimeStatus.Valid));
            var e = Advance(c, 1);
            Assert.That(e.OfType<TimeStatusChanged>().Single().Status, Is.EqualTo(TimeStatus.NeedsResync));
            Assert.That(c.DateGate(), Is.EqualTo(ErrorCode.TimeNotVerified));
        }

        [Test]
        public void 재확인_필요_중에는_날을_넘기지_않고_새_응답에서_넘긴다()
        {
            var c = new Clock(Config());
            Sync(c, KstMs(2026, 11, 16, 4, 50, 0), 1);
            var e = Advance(c, 20 * ClockConfig.UsPerMin); // 추정 5시 10분이지만 10분 초과
            Assert.That(e.OfType<DayReset>(), Is.Empty);
            Assert.That(c.Status, Is.EqualTo(TimeStatus.NeedsResync));
            var e2 = Sync(c, KstMs(2026, 11, 16, 5, 10, 0), 2);
            Assert.That(e2.OfType<DayReset>().Count(), Is.EqualTo(1));
        }

        [Test]
        public void 미확인_중의_경과도_누적되지만_날은_넘기지_않는다()
        {
            var c = new Clock(Config());
            Sync(c, KstMs(2026, 11, 16, 4, 59, 0), 1);
            c.BeginUnverified(2, new List<GameEvent>());
            var e = Advance(c, 5 * ClockConfig.UsPerMin);
            Assert.That(e.OfType<DayReset>(), Is.Empty);
            Assert.That(c.SyncElapsedUs, Is.EqualTo(5 * ClockConfig.UsPerMin));
        }

        [TestCase(-1L)]
        [TestCase(long.MinValue)]
        [TestCase(Clock.MaxServerUtcMs + 1)]
        [TestCase(long.MaxValue)]
        public void 불가능한_서버_시각은_잘못된_입력(long ms)
        {
            Assert.That(Clock.IsValidServerUtcMs(ms), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => new Clock(Config()).Sync(ms, 1, new List<GameEvent>()));
        }

        [TestCase(0L)]
        [TestCase(Clock.MaxServerUtcMs)]
        public void 서버_시각_경계값은_받는다(long ms)
        {
            Assert.That(Clock.IsValidServerUtcMs(ms), Is.True);
            var c = new Clock(Config());
            Sync(c, ms, 1);
            Assert.That(c.EstimatedUtcUs, Is.EqualTo(ms * 1000L));
            // 추정 시각은 MaxEstimatedUtcUs(9999-12-31 23:59:59.999999)까지
            var room = Clock.MaxEstimatedUtcUs - ms * 1000L;
            Assert.That(c.CanAdvance(room), Is.True);
            Assert.That(c.CanAdvance(room + 1), Is.False);
        }

        [Test]
        public void 넘치는_경과는_CanAdvance가_막는다()
        {
            var c = new Clock(Config());
            Sync(c, Clock.MaxServerUtcMs, 1);
            Assert.That(c.CanAdvance(long.MaxValue), Is.False);
            Assert.That(c.CanAdvance(-1), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => c.Advance(long.MaxValue, new List<GameEvent>()));
        }

        [Test]
        public void 전투_구간을_5시에서_나눈다()
        {
            var c = new Clock(Config());
            var five = KstMs(2026, 11, 16, 5, 0, 0) * 1000L;
            var day = c.DayIdAt(five);
            var parts = c.SplitByDay(five + 300_000, 1_000_000);
            Assert.That(parts.Select(p => (p.Key, p.Value)), Is.EqualTo(new[] { (day - 1, 700_000L), (day, 300_000L) }));
            Assert.That(c.SplitByDay(five + 2_000_000, 1_000_000).Select(p => (p.Key, p.Value)), Is.EqualTo(new[] { (day, 1_000_000L) }));
            Assert.That(c.SplitByDay(five, 1_000_000).Select(p => (p.Key, p.Value)), Is.EqualTo(new[] { (day - 1, 1_000_000L) }), "5시 정각에 끝나면 전날");
            Assert.That(c.SplitByDay(five + 1_000_000, 1_000_000).Select(p => (p.Key, p.Value)), Is.EqualTo(new[] { (day, 1_000_000L) }), "5시 정각에 시작하면 새날");
            Assert.That(c.SplitByDay(five, 0), Is.Empty);
            Assert.That(c.SplitByDay(five + ClockConfig.UsPerHour, 3 * ClockConfig.UsPerDay).Sum(p => p.Value), Is.EqualTo(3 * ClockConfig.UsPerDay));
        }

        [Test]
        public void 되돌리기_검사()
        {
            var c = new Clock(Config());
            Assert.That(c.TryRestore(null, 0, null, null), Is.True, "새 계정");
            Assert.That(c.TryRestore(1000, 5, 3, 1), Is.True);
            var before = c.Clone();
            Assert.That(c.TryRestore(null, 5, null, null), Is.False, "기준 시각 없이 누적");
            Assert.That(c.TryRestore(null, 0, 3, null), Is.False, "기준 시각 없이 처리한 날");
            Assert.That(c.TryRestore(1000, -1, null, null), Is.False, "음수 누적");
            Assert.That(c.TryRestore(-1, 0, null, null), Is.False, "음수 기준 시각");
            Assert.That(c.TryRestore(Clock.MaxServerUtcMs + 1, 0, null, null), Is.False);
            Assert.That(c.TryRestore(Clock.MaxServerUtcMs, long.MaxValue, null, null), Is.False, "추정 시각이 넘침");
            Assert.That(c.SameState(before), Is.True);
        }

        [Test]
        public void 다음_요청_번호는_1_이상()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Clock(Config()).BeginUnverified(0, new List<GameEvent>()));
        }

        [Test]
        public void 설정_범위()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ClockConfig(540, 24, 5, 0, 5, 10, 5, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ClockConfig(540, 5, 5, 7, 5, 10, 5, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ClockConfig(1441, 5, 5, 0, 5, 10, 5, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ClockConfig(540, 5, 5, 0, 5, 0, 5, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ClockConfig(540, 5, 5, 0, 5, 5, 5, 1), "anchor_valid_min = sync_interval_min");
            Assert.That(ClockConfig.WeekdayIndex("mon"), Is.EqualTo(0));
            Assert.That(ClockConfig.WeekdayIndex("sun"), Is.EqualTo(6));
            Assert.Throws<ArgumentException>(() => ClockConfig.WeekdayIndex("Mon"));
        }
    }
}
