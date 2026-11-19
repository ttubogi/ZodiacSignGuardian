using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using ZodiacSignGuardian.Core.Commands;
using ZodiacSignGuardian.Core.Economy;
using ZodiacSignGuardian.Core.Numbers;
using ZodiacSignGuardian.Core.Rewards;
using ZodiacSignGuardian.Core.Session;
using ZodiacSignGuardian.Core.Timing;
using static ZodiacSignGuardian.Core.Tests.Session.C3Harness;

namespace ZodiacSignGuardian.Core.Tests.Session
{
    /// <summary>오프라인 정산·수령 (설계서 v0.9 3절 "오프라인 보상", 5절 시나리오).</summary>
    [TestFixture]
    public class OfflineRewardTests
    {
        static readonly long T0 = KstMs(2026, 11, 18, 12, 0, 0);
        const long Min = 60_000L;

        static C3Harness Started()
        {
            var h = New();
            h.Sync(T0); // 새 계정 첫 동기화: 기준 시점만
            return h;
        }

        [Test]
        public void 새_계정_첫_동기화는_기준_시점만_만들고_활성_경과_0_정산_대기_끔()
        {
            var h = New();
            h.Advance(30); // 동기화 전 활성 경과도 쌓인다
            Assert.That(h.S.Offline.ActiveElapsedUs, Is.EqualTo(30_000_000));
            var e = h.Sync(T0);
            Assert.That(e.OfType<OfflineRewardReady>(), Is.Empty);
            Assert.That(h.S.Offline.SettlementPointUtcMs, Is.EqualTo(T0));
            Assert.That(h.S.Offline.ActiveElapsedUs, Is.EqualTo(0));
            Assert.That(h.S.SettlementPending, Is.False);
            Assert.That(h.S.OfflineBundle, Is.Null);
        }

        [Test]
        public void 두_시간_비우면_떠날_때_스테이지_수급률로_120분()
        {
            var h = Started();
            var e = h.AwayAndBack(T0 + 120 * Min);
            var ready = e.OfType<OfflineRewardReady>().Single();
            Assert.That(ready.Minutes, Is.EqualTo(120));
            Assert.That(ready.Amount, Is.EqualTo(GameNumber.From(1200)));
            var b = h.S.OfflineBundle;
            Assert.That((b.Id, b.Minutes, b.AdApplied), Is.EqualTo((1L, 120L, false)));
            Assert.That(h.S.Offline.SettlementPointUtcMs, Is.EqualTo(T0 + 120 * Min));
        }

        [TestCase(59_999, 0)]
        [TestCase(60_000, 1)]
        [TestCase(119_999, 1)]
        [TestCase(120_000, 2)]
        public void 일분_미만은_버리고_정산된_것으로_본다(long awayMs, long minutes)
        {
            var h = Started();
            h.AwayAndBack(T0 + awayMs);
            Assert.That(h.S.OfflineBundle?.Minutes ?? 0, Is.EqualTo(minutes));
            Assert.That(h.S.Offline.SettlementPointUtcMs, Is.EqualTo(T0 + awayMs), "버린 시간도 정산 완료");
            h.AwayAndBack(T0 + awayMs + 30_000);
            Assert.That(h.S.OfflineBundle?.Minutes ?? 0, Is.EqualTo(minutes), "버린 59초가 다음 정산에 더해지지 않음");
        }

        [TestCase(720, 720)]
        [TestCase(721, 720)]
        [TestCase(100000, 720)]
        public void 합계는_720분까지(long awayMin, long expected)
        {
            var h = Started();
            h.AwayAndBack(T0 + awayMin * Min);
            Assert.That(h.S.OfflineBundle.Minutes, Is.EqualTo(expected));
            h.AwayAndBack(T0 + (awayMin + 60) * Min);
            Assert.That(h.S.OfflineBundle.Minutes, Is.EqualTo(720), "더 비워도 늘지 않음");
        }

        [Test]
        public void 미수령_700분_1장_더하기_새_60분_2장이면_20분만_2장으로()
        {
            var h = Started();
            h.AwayAndBack(T0 + 700 * Min);
            h.S.SetStageForTest(2);
            h.AwayAndBack(T0 + 760 * Min);
            Assert.That(h.S.OfflineBundle.Minutes, Is.EqualTo(720));
            Assert.That(h.S.OfflineBundle.Amount, Is.EqualTo(GameNumber.From(700 * 10 + 20 * 100)));
            Assert.That(h.S.OfflineBundle.Id, Is.EqualTo(1), "같은 묶음");
        }

        [Test]
        public void 화면이_켜진_채_멈춘_시간은_활성이라_빠진다()
        {
            var h = Started();
            h.Advance(60); // 화면이 켜진 채 60초
            h.Pause();
            h.Resume();
            h.Sync(T0 + 60_000 + 3 * Min);
            Assert.That(h.S.OfflineBundle.Minutes, Is.EqualTo(3));
        }

        [Test]
        public void 복귀_뒤_동기화가_늦는_동안_스테이지를_올려도_떠날_때_스테이지로()
        {
            var h = Started();
            h.Pause();
            h.Resume();
            h.S.SetStageForTest(2);
            h.Sync(T0 + 60 * Min);
            Assert.That(h.S.OfflineBundle.Amount, Is.EqualTo(GameNumber.From(600)), "1장 수급률 10/분");
        }

        [Test]
        public void 정산_실패로_여러_번_나갔다_와도_처음_고정한_스테이지로_사이의_플레이는_빠진다()
        {
            var h = Started();
            h.Pause(); // 1장에서 떠남
            h.Resume(); // 2시간 뒤 돌아왔지만 동기화 실패
            h.S.SetStageForTest(2);
            h.Advance(10 * 60); // 10분 플레이
            h.Pause(); // 정산 대기 중이라 떠날 때 스테이지를 덮어쓰지 않음
            h.Resume();
            h.Sync(T0 + (120 + 10 + 60) * Min);
            Assert.That(h.S.OfflineBundle.Minutes, Is.EqualTo(180));
            Assert.That(h.S.OfflineBundle.Amount, Is.EqualTo(GameNumber.From(1800)));
        }

        [Test]
        public void 동기화_전_자동_저장_뒤_재시작해도_비운_시간만_한_번()
        {
            var h = Started();
            h.Pause();
            h.Resume();
            h.Advance(5); // 동기화 전 5초 플레이
            h.Restart(); // 저장 → 앱 종료 → 다시 시작
            Assert.That(h.S.SettlementPending, Is.True);
            h.Sync(T0 + 60 * Min + 5_000);
            Assert.That(h.S.OfflineBundle.Minutes, Is.EqualTo(60), "5초 플레이는 빠짐");
            h.Restart();
            h.Sync(T0 + 60 * Min + 10_000);
            Assert.That(h.S.OfflineBundle.Minutes, Is.EqualTo(60), "같은 시간을 두 번 쓰지 않음");
        }

        [Test]
        public void 불러오면_정산_대기를_켜고_떠날_때_스테이지는_저장값_그대로()
        {
            var h = Started();
            h.S.SetStageForTest(2);
            h.Pause(); // 정산 대기가 아니므로 떠날 때 스테이지 = 2
            h.Resume();
            h.Sync(T0 + Min);
            Assert.That(h.S.SettlementPending, Is.False);
            h.S.SetStageForTest(3);
            var json = h.S.CreateSave().Json; // 저장할 때 떠날 때 스테이지 = 3 (정산 대기가 아님)
            var r = GameSessionLoader.Load(h.T, json, DefaultNonce);
            Assert.That(r.Session.SettlementPending, Is.True);
            Assert.That(r.Session.Offline.LeaveStage, Is.EqualTo(3));
            Assert.That(r.Session.Stage, Is.EqualTo(3));
        }

        [Test]
        public void 분당_보상이_0이면_정산만_끝내고_묶음을_만들지_않는다_E4()
        {
            var h = Started();
            h.S.SetStageForTest(3); // 손 표에 없음 → 0
            h.Pause();
            h.Resume();
            var e = h.Sync(T0 + 180 * Min);
            Assert.That(e.OfType<OfflineRewardReady>(), Is.Empty);
            Assert.That(h.S.OfflineBundle, Is.Null);
            Assert.That(h.S.Offline.SettlementPointUtcMs, Is.EqualTo(T0 + 180 * Min));
            Assert.That(h.S.SettlementPending, Is.False);
            h.AssertFailsUnchanged(() => h.S.StartAd(Ads.AdTarget.Offline), ErrorCode.NotReady);
            h.AssertFailsUnchanged(() => h.S.ClaimOffline(), ErrorCode.NotReady);
        }

        [Test]
        public void 분당_보상이_0인_정산은_있는_묶음에_더하지도_한도를_쓰지도_않는다()
        {
            var h = Started();
            h.AwayAndBack(T0 + 30 * Min);
            h.S.SetStageForTest(3);
            h.AwayAndBack(T0 + 90 * Min);
            Assert.That(h.S.OfflineBundle.Minutes, Is.EqualTo(30));
        }

        [Test]
        public void 실제_게임은_스테이지_표의_분당_보상을_쓴다()
        {
            var r = GameSessionLoader.NewAccount(Tables(), 1, DefaultNonce);
            var s = r.Session; // OfflineRateForStage를 넣지 않음 → stages.csv (C4)
            s.SyncServerTime(T0, 1);
            s.Suspend();
            s.Resume(2);
            s.SyncServerTime(T0 + 300 * Min, 2);
            Assert.That(s.OfflineBundle, Is.Not.Null);
            // 1스테이지 offline_dust_per_min 2.4e1 × 300분
            Assert.That(s.OfflineBundle.Amount.ToSaveString(), Is.EqualTo("7.2e3"));
        }

        [Test]
        public void 서버_시각이_뒤로_갔다가_바로잡히면_보상이_줄_수_있다_E3()
        {
            var h = Started(); // 정산 기준 12:00
            h.Pause();
            h.Resume();
            h.Sync(T0 - 60 * Min); // 11:00으로 보정: 기준 시점은 12:00 그대로, 활성 경과 0
            Assert.That(h.S.Offline.SettlementPointUtcMs, Is.EqualTo(T0));
            h.Advance(3600); // 60분 플레이 → 서버 12:00
            h.AwayAndBack(T0 + 10 * Min); // 10분 비우고 12:10
            Assert.That(h.S.OfflineBundle, Is.Null, "12:10 − 12:00 − 활성 60분 = 0 (보수적 정책)");
        }

        [Test]
        public void 일초만_뒤로_가도_60초_비운_것이_0분이_될_수_있다()
        {
            var h = Started();
            h.Pause();
            h.Resume();
            h.Sync(T0 - 1000); // 1초 뒤로
            h.AwayAndBack(T0 - 1000 + 60_000); // 60초 비움
            Assert.That(h.S.OfflineBundle, Is.Null, "59초로 계산되어 0분 (감소량이 보정 초 수와 비례하지 않음)");
        }

        [Test]
        public void 짧은_일시정지가_정산_없이_여러_번_쌓이면_합산한다()
        {
            var h = Started();
            // 광고 일시정지 40초 × 3번, 사이에 동기화 실패 (E1: 광고 일시정지도 보통 Suspend·Resume)
            for (var i = 0; i < 3; i++)
            {
                h.Pause();
                h.Resume();
            }
            h.Sync(T0 + 120_000);
            Assert.That(h.S.OfflineBundle.Minutes, Is.EqualTo(2));
        }

        [Test]
        public void 통신_없이_전투하다_재연결해도_그_시간은_오프라인이_아니다()
        {
            var h = Started();
            h.Advance(1);
            for (var i = 0; i < 600; i++) h.Advance(1); // 10분 전투, 동기화 없음(유효 끝)
            Assert.That(h.S.TimeStatus, Is.EqualTo(TimeStatus.NeedsResync));
            h.Sync(T0 + 601_000);
            Assert.That(h.S.OfflineBundle, Is.Null, "정산 대기가 아니므로 정산하지 않음");
        }

        // ---- 비활성 시간 계산은 넘치지 않는다 (Codex v0.9 검토 7-가) ----

        [Test]
        public void 비활성_계산은_서버가_크게_뒤로_가고_활성_경과가_커도_넘치지_않는다()
        {
            Assert.That(OfflineRewards.InactiveUs(0, Clock.MaxServerUtcMs, long.MaxValue), Is.EqualTo(0));
            Assert.That(OfflineRewards.InactiveUs(Clock.MaxServerUtcMs, 0, 0), Is.EqualTo(Clock.MaxServerUtcMs * 1000L));
            Assert.That(OfflineRewards.InactiveUs(Clock.MaxServerUtcMs, 0, long.MaxValue), Is.EqualTo(0));
            Assert.That(OfflineRewards.InactiveUs(10, 10, 0), Is.EqualTo(0));
        }

        [Test]
        public void 비활성_계산은_정확한_큰_정수_계산과_같다()
        {
            var rng = new Random(20261118);
            long[] edges = { 0, 1, 2, 999, 1000, 60_000, Clock.MaxServerUtcMs - 1, Clock.MaxServerUtcMs };
            long[] activeEdges = { 0, 1, 999_999, 1_000_000, long.MaxValue - 1, long.MaxValue, Clock.MaxServerUtcMs * 1000L, Clock.MaxServerUtcMs * 1000L + 1 };
            for (var i = 0; i < 20000; i++)
            {
                var a = i < 64 ? edges[i % edges.Length] : (long)(rng.NextDouble() * Clock.MaxServerUtcMs);
                var p = i < 64 ? edges[(i / edges.Length) % edges.Length] : (long)(rng.NextDouble() * Clock.MaxServerUtcMs);
                var act = i % 3 == 0 ? activeEdges[i % activeEdges.Length] : (long)(rng.NextDouble() * long.MaxValue);
                var exact = (new BigInteger(a) - p) * 1000 - act;
                var expected = exact < 0 ? 0 : (long)exact;
                Assert.That(OfflineRewards.InactiveUs(a, p, act), Is.EqualTo(expected), a + " " + p + " " + act);
            }
        }

        [Test]
        public void 활성_경과가_넘치려는_Advance는_InvalidInput()
        {
            var h = New();
            h.S.Offline.TryRestore(null, long.MaxValue - 5, 1, 1, 0, 0, GameNumber.Zero, false, new System.Collections.Generic.List<string>());
            h.AssertFailsUnchanged(() => h.S.Advance(0.00001), ErrorCode.InvalidInput); // 10µs
            h.Ok(h.S.Advance(0.000005)); // 5µs는 됨
            Assert.That(h.S.Offline.ActiveElapsedUs, Is.EqualTo(long.MaxValue));
        }

        // ---- 수령 ----

        [Test]
        public void 수령은_1배를_별가루로_주고_묶음을_끝내고_임무를_센다()
        {
            var h = Started();
            h.AwayAndBack(T0 + 30 * Min);
            var e = h.Ok(h.S.ClaimOffline());
            var c = e.OfType<CurrencyChanged>().Single();
            Assert.That((c.CurrencyId, c.Reason), Is.EqualTo((GameSession.StardustId, CurrencyReason.OfflineReward)));
            Assert.That(h.Dust, Is.EqualTo(GameNumber.From(300)));
            Assert.That(h.S.OfflineBundle, Is.Null);
            Assert.That(h.MissionProgress("claim_offline_once"), Is.EqualTo(1));
            Assert.That(e.OfType<MissionCompleted>().Single().MissionId, Is.EqualTo("claim_offline_once"));
            h.AssertFailsUnchanged(() => h.S.ClaimOffline(), ErrorCode.NotReady);
            h.AwayAndBack(T0 + 60 * Min);
            Assert.That(h.S.OfflineBundle.Id, Is.EqualTo(2), "새 묶음 번호");
        }

        [Test]
        public void 수령은_시간_조건이_먼저다()
        {
            var h = Started();
            h.AwayAndBack(T0 + 30 * Min);
            h.Pause();
            h.Resume(); // 미확인
            h.AssertFailsUnchanged(() => h.S.ClaimOffline(), ErrorCode.TimeNotVerified);
            h.Sync(T0 - 24 * 60 * Min); // 어제로 뒤로 감
            h.AssertFailsUnchanged(() => h.S.ClaimOffline(), ErrorCode.TimeWentBack);
            var fresh = Started();
            fresh.Pause();
            fresh.Resume();
            fresh.AssertFailsUnchanged(() => fresh.S.ClaimOffline(), ErrorCode.TimeNotVerified); // 묶음 없음보다 시간이 먼저
        }

        [Test]
        public void 보상_확정_뒤_재시작해도_같은_구간을_두_번_주지_않는다()
        {
            var h = Started();
            h.AwayAndBack(T0 + 30 * Min);
            h.Ok(h.S.ClaimOffline());
            h.Restart();
            h.Sync(T0 + 30 * Min);
            Assert.That(h.S.OfflineBundle, Is.Null);
            Assert.That(h.Dust, Is.EqualTo(GameNumber.From(300)));
        }

        // ---- 아주 큰 값: 예외 없이 명령 결과로 끝나고 저장은 다시 읽힌다 ----

        static void AssertSaveLoads(C3Harness h, string where)
        {
            var r = GameSessionLoader.Load(h.T, h.S.CreateSave().Json, DefaultNonce ^ 2);
            Assert.That(r.Ok, Is.True, where + ": " + string.Join("\n", r.Errors));
        }

        [Test]
        public void 정산_금액이_GameNumber_범위를_넘으면_그_정산분은_버리고_정산은_끝낸다()
        {
            var h = Started();
            h.S.OfflineRateForStage = _ => GameNumber.From(1.0, long.MaxValue); // 분당 1e(long 끝): 30분이면 범위 밖
            System.Collections.Generic.List<GameEvent> e = null;
            Assert.DoesNotThrow(() => e = h.AwayAndBack(T0 + 30 * Min));
            Assert.That(e.OfType<OfflineRewardReady>(), Is.Empty);
            Assert.That(h.S.OfflineBundle, Is.Null);
            Assert.That(h.S.Offline.SettlementPointUtcMs, Is.EqualTo(T0 + 30 * Min), "정산은 끝남");
            Assert.That(h.S.SettlementPending, Is.False);
            AssertSaveLoads(h, "버린 뒤");
        }

        // ---- 넘침 정책 (설계서 v0.10, Codex C3 검토 질문 2 A안: 묶음 금액 × ad_multiplier도 표현 가능) ----

        [Test]
        public void 배율_값이_넘칠_새_묶음은_만들지_않고_정산만_끝낸다_경계의_양쪽()
        {
            // ad_multiplier 2. 4.99999999999999e(끝) × 2 = 9.99999999999998e(끝)는 범위 안, 5e(끝) × 2 = 1e(끝+1)은 범위 밖
            var fits = Started();
            fits.S.OfflineRateForStage = _ => GameNumber.From(4.99999999999999, long.MaxValue);
            fits.AwayAndBack(T0 + 1 * Min);
            Assert.That(fits.S.OfflineBundle.Amount, Is.EqualTo(GameNumber.From(4.99999999999999, long.MaxValue)), "경계 안쪽은 묶음이 생김");
            fits.Ok(fits.S.ConfirmAd(fits.StartAd(ZodiacSignGuardian.Core.Ads.AdTarget.Offline)));
            fits.Ok(fits.S.ClaimOffline());
            Assert.That(fits.Dust, Is.EqualTo(GameNumber.From(9.99999999999998, long.MaxValue)), "2배로 받음");
            AssertSaveLoads(fits, "경계 안쪽 수령 뒤");

            var over = Started();
            over.S.OfflineRateForStage = _ => GameNumber.From(5.0, long.MaxValue);
            System.Collections.Generic.List<GameEvent> e = null;
            Assert.DoesNotThrow(() => e = over.AwayAndBack(T0 + 1 * Min));
            Assert.That(e.OfType<OfflineRewardReady>(), Is.Empty);
            Assert.That(over.S.OfflineBundle, Is.Null, "경계 바깥은 묶음을 만들지 않음");
            Assert.That(over.S.Offline.SettlementPointUtcMs, Is.EqualTo(T0 + 1 * Min), "정산은 끝남");
            Assert.That(over.S.Offline.NextBundleId, Is.EqualTo(1), "번호를 쓰지 않음");
            AssertSaveLoads(over, "경계 바깥");
        }

        [Test]
        public void 광고_적용_뒤_다음_정산으로_배율_값이_넘치면_그_정산분은_버리고_묶음은_2배로_받는다()
        {
            // 분당 1e(끝-1): 30분이면 3e(끝)(2배 6e(끝)는 범위 안), 다시 30분을 더하면 6e(끝)(2배는 범위 밖)
            var h = Started();
            h.S.OfflineRateForStage = _ => GameNumber.From(1.0, long.MaxValue - 1);
            h.AwayAndBack(T0 + 30 * Min);
            var amount = GameNumber.From(3.0, long.MaxValue);
            Assert.That(h.S.OfflineBundle.Amount, Is.EqualTo(amount));
            h.Ok(h.S.ConfirmAd(h.StartAd(ZodiacSignGuardian.Core.Ads.AdTarget.Offline)));
            var id = h.S.OfflineBundle.Id;

            System.Collections.Generic.List<GameEvent> e = null;
            Assert.DoesNotThrow(() => e = h.AwayAndBack(T0 + 60 * Min));
            Assert.That(e.OfType<OfflineRewardReady>(), Is.Empty, "넘치는 정산분은 더하지 않음");
            var b = h.S.OfflineBundle;
            Assert.That((b.Id, b.Minutes, b.Amount, b.AdApplied), Is.EqualTo((id, 30L, amount, true)), "묶음과 광고 적용은 그대로");
            Assert.That(h.S.Offline.SettlementPointUtcMs, Is.EqualTo(T0 + 60 * Min), "정산은 끝남(버린 시간은 다시 계산하지 않음)");
            Assert.That(h.S.Violations, Is.Empty, string.Join("\n", h.S.Violations));
            AssertSaveLoads(h, "광고 적용 묶음 + 버린 정산분");

            // 저장 → 불러오기 → 동기화 → 수령까지 한 경로로 확인한다 (Codex C3 재검토 권고, 2026-11-19)
            var r = GameSessionLoader.Load(h.T, h.S.CreateSave().Json, DefaultNonce ^ 4);
            Assert.That(r.Ok, Is.True, string.Join("\n", r.Errors));
            var loaded = r.Session;
            loaded.OfflineRateForStage = h.S.OfflineRateForStage;
            Assert.That(loaded.SyncServerTime(T0 + 60 * Min, 1).Ok, Is.True, "불러온 뒤 첫 동기화(새로 정산할 시간 없음)");
            var lb = loaded.OfflineBundle;
            Assert.That((lb.Id, lb.Minutes, lb.Amount, lb.AdApplied), Is.EqualTo((id, 30L, amount, true)), "불러와도 묶음과 광고 적용 유지");
            var claim = loaded.ClaimOffline();
            Assert.That(claim.Ok, Is.True, claim.Error?.ToString());
            Assert.That(loaded.Wallet.Balance(GameSession.StardustId).ToString(), Is.EqualTo(GameNumber.From(6.0, long.MaxValue).ToString()),
                "불러온 세션에서 받을 수 없는 묶음이 생기지 않고 2배로 받음");
            Assert.That(loaded.OfflineBundle, Is.Null);
            Assert.That(loaded.Violations, Is.Empty, string.Join("\n", loaded.Violations));
        }

        [Test]
        public void 저장의_묶음_금액에_배율을_곱해_넘치면_불러오기_실패()
        {
            var h = Started();
            h.S.OfflineRateForStage = _ => GameNumber.From(1.0, long.MaxValue - 1);
            h.AwayAndBack(T0 + 30 * Min);
            var json = h.S.CreateSave().Json;
            var good = "\"" + GameNumber.From(3.0, long.MaxValue).ToSaveString() + "\"";
            Assert.That(json, Does.Contain(good));
            Assert.That(GameSessionLoader.Load(h.T, json, DefaultNonce ^ 5).Ok, Is.True, "3e(끝)은 됨");
            var bad = json.Replace(good, "\"" + GameNumber.From(6.0, long.MaxValue).ToSaveString() + "\"");
            SessionLoadResult r = null;
            Assert.DoesNotThrow(() => r = GameSessionLoader.Load(h.T, bad, DefaultNonce ^ 5));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors.Any(x => x.Contains("ad_multiplier배")), Is.True, string.Join("\n", r.Errors));
        }

        [Test]
        public void 다음_묶음_번호가_long_끝에_닿으면_새_묶음을_만들지_않고_정산만_끝낸다()
        {
            var h = Started();
            var errors = new System.Collections.Generic.List<string>();
            Assert.That(h.S.Offline.TryRestore(T0, 0, 1, long.MaxValue - 1, 0, 0, GameNumber.Zero, false, errors), Is.True, string.Join(",", errors));

            h.AwayAndBack(T0 + 30 * Min);
            Assert.That(h.S.OfflineBundle.Id, Is.EqualTo(long.MaxValue - 1), "끝 바로 앞 번호는 쓴다");
            Assert.That(h.S.Offline.NextBundleId, Is.EqualTo(long.MaxValue));
            h.AwayAndBack(T0 + 40 * Min);
            Assert.That(h.S.OfflineBundle.Minutes, Is.EqualTo(40), "있는 묶음에는 더한다");
            AssertSaveLoads(h, "다음 번호 끝");
            h.Ok(h.S.ClaimOffline());

            System.Collections.Generic.List<GameEvent> e = null;
            Assert.DoesNotThrow(() => e = h.AwayAndBack(T0 + 70 * Min));
            Assert.That(e.OfType<OfflineRewardReady>(), Is.Empty);
            Assert.That(h.S.OfflineBundle, Is.Null, "새 번호를 낼 수 없음");
            Assert.That(h.S.Offline.SettlementPointUtcMs, Is.EqualTo(T0 + 70 * Min));
            Assert.That(h.S.Offline.NextBundleId, Is.EqualTo(long.MaxValue), "넘쳐 음수가 되지 않음");
            AssertSaveLoads(h, "번호를 못 낸 뒤");
        }
    }
}
