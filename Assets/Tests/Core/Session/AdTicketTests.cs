using System.Linq;
using NUnit.Framework;
using ZodiacSignGuardian.Core.Ads;
using ZodiacSignGuardian.Core.Commands;
using ZodiacSignGuardian.Core.Numbers;
using ZodiacSignGuardian.Core.Session;
using ZodiacSignGuardian.Core.Timing;
using static ZodiacSignGuardian.Core.Tests.Session.C3Harness;

namespace ZodiacSignGuardian.Core.Tests.Session
{
    /// <summary>광고 번호·ConfirmAd·2배 수령 (설계서 v0.9 3절 "광고", D1~D3, E1·E2, X3).</summary>
    [TestFixture]
    public class AdTicketTests
    {
        static readonly long T0 = KstMs(2026, 11, 18, 12, 0, 0);
        const long Min = 60_000L;

        /// <summary>30분 묶음(금액 300)이 있는 상태.</summary>
        static C3Harness WithBundle()
        {
            var h = New();
            h.Sync(T0);
            h.AwayAndBack(T0 + 30 * Min);
            Assert.That(h.S.OfflineBundle.Amount, Is.EqualTo(GameNumber.From(300)));
            return h;
        }

        [Test]
        public void 광고를_보고_수령하면_받을_때의_합계에_2배()
        {
            var h = WithBundle();
            var t = h.StartAd(AdTarget.Offline);
            Assert.That((t.SessionNonce, t.Serial, t.Target), Is.EqualTo((DefaultNonce, 1L, AdTarget.Offline)));
            var e = h.Ok(h.S.ConfirmAd(t));
            Assert.That(e, Is.Empty, "재화 변화 없음, 화면은 상태를 다시 읽음");
            Assert.That(h.S.OfflineBundle.AdApplied, Is.True);
            Assert.That(h.S.OfflineBundle.Amount, Is.EqualTo(GameNumber.From(300)), "금액은 배율 적용 전");
            h.Ok(h.S.ClaimOffline());
            Assert.That(h.Dust, Is.EqualTo(GameNumber.From(600)));
        }

        [Test]
        public void 묶음이_없거나_금액이_0이면_StartAd는_NotReady()
        {
            var h = New();
            h.Sync(T0);
            h.AssertFailsUnchanged(() => h.S.StartAd(AdTarget.Offline), ErrorCode.NotReady);
        }

        [Test]
        public void 광고_적용이_있는_묶음에_StartAd는_AlreadyClaimed이고_번호를_내지_않는다_X3()
        {
            var h = WithBundle();
            var t = h.StartAd(AdTarget.Offline);
            h.Ok(h.S.ConfirmAd(t));
            h.AssertFailsUnchanged(() => h.S.StartAd(AdTarget.Offline), ErrorCode.AlreadyClaimed);
            Assert.That(h.S.Ads.LastSerial, Is.EqualTo(1));
        }

        [Test]
        public void 같은_번호의_ConfirmAd_두_번째는_AlreadyClaimed이고_2배는_한_번()
        {
            var h = WithBundle();
            var t = h.StartAd(AdTarget.Offline);
            h.Ok(h.S.ConfirmAd(t));
            h.AssertFailsUnchanged(() => h.S.ConfirmAd(t), ErrorCode.AlreadyClaimed);
            h.Ok(h.S.ClaimOffline());
            Assert.That(h.Dust, Is.EqualTo(GameNumber.From(600)));
        }

        [Test]
        public void ConfirmAd는_시간_미확인이어도_된다_수령은_동기화_뒤()
        {
            var h = WithBundle();
            var t = h.StartAd(AdTarget.Offline);
            h.Pause(); // 광고로 생긴 Unity 일시정지도 Suspend (E1)
            h.Resume();
            Assert.That(h.S.TimeStatus, Is.EqualTo(TimeStatus.Unverified));
            h.Ok(h.S.ConfirmAd(t)); // 동기화를 기다리지 않음
            h.AssertFailsUnchanged(() => h.S.ClaimOffline(), ErrorCode.TimeNotVerified);
            h.Sync(T0 + 30 * Min + 40_000);
            h.Ok(h.S.ClaimOffline());
            Assert.That(h.Dust, Is.EqualTo(GameNumber.From(600)));
        }

        [Test]
        public void 일시정지_중에도_ConfirmAd를_받는다()
        {
            var h = WithBundle();
            var t = h.StartAd(AdTarget.Offline);
            h.Pause();
            h.Ok(h.S.ConfirmAd(t));
            Assert.That(h.S.OfflineBundle.AdApplied, Is.True);
        }

        [Test]
        public void 광고_중_일시정지_뒤_복귀하며_묶음에_분이_더해져도_같은_묶음이고_받을_때_합계에_2배()
        {
            var h = WithBundle();
            var t = h.StartAd(AdTarget.Offline);
            h.AwayAndBack(T0 + 50 * Min); // 광고 중 20분 떠남 (홈 버튼)
            Assert.That(h.S.OfflineBundle.Id, Is.EqualTo(1));
            Assert.That(h.S.OfflineBundle.Minutes, Is.EqualTo(50), "광고 중 실제로 떠난 시간도 오프라인");
            h.Ok(h.S.ConfirmAd(t));
            h.Ok(h.S.ClaimOffline());
            Assert.That(h.Dust, Is.EqualTo(GameNumber.From(1000)));
        }

        [Test]
        public void 묶음_A의_광고_알림이_A를_받은_뒤_오면_InvalidTarget이고_B에_붙지_않는다()
        {
            var h = WithBundle();
            var a = h.StartAd(AdTarget.Offline);
            h.Ok(h.S.ClaimOffline()); // A를 그냥 받음 (1배)
            h.AwayAndBack(T0 + 60 * Min); // 묶음 B
            Assert.That(h.S.OfflineBundle.Id, Is.EqualTo(2));
            h.AssertFailsUnchanged(() => h.S.ConfirmAd(a), ErrorCode.InvalidTarget);
            Assert.That(h.S.OfflineBundle.AdApplied, Is.False);
        }

        [Test]
        public void 사용된_번호는_새_번호가_나온_뒤에도_묶음이_끝난_뒤에도_AlreadyClaimed()
        {
            var h = WithBundle();
            var a = h.StartAd(AdTarget.Offline);
            h.Ok(h.S.ConfirmAd(a));
            h.Ok(h.S.ClaimOffline()); // 묶음 종료: 사용됨은 무효로 바뀌지 않음
            h.AssertFailsUnchanged(() => h.S.ConfirmAd(a), ErrorCode.AlreadyClaimed);
            h.AwayAndBack(T0 + 60 * Min);
            h.StartAd(AdTarget.Offline); // 새 번호
            h.AssertFailsUnchanged(() => h.S.ConfirmAd(a), ErrorCode.AlreadyClaimed);
        }

        [Test]
        public void 새로_발급하면_열린_이전_번호는_무효()
        {
            var h = WithBundle();
            var a = h.StartAd(AdTarget.Offline);
            var b = h.StartAd(AdTarget.Offline);
            Assert.That(b.Serial, Is.EqualTo(2));
            h.AssertFailsUnchanged(() => h.S.ConfirmAd(a), ErrorCode.InvalidTarget);
            h.Ok(h.S.ConfirmAd(b));
        }

        [Test]
        public void 판정_순서는_정확한_번호_대상_사용됨_무효_E2()
        {
            var h = WithBundle();
            var hunt = h.StartAd(AdTarget.HuntBuff);
            var off = h.StartAd(AdTarget.Offline);
            h.AssertFailsUnchanged(() => h.S.ConfirmAd(null), ErrorCode.InvalidInput);
            h.AssertFailsUnchanged(() => h.S.ConfirmAd(new AdTicket(DefaultNonce + 1, off.Serial, AdTarget.Offline)), ErrorCode.InvalidTarget); // 다른 세션
            h.AssertFailsUnchanged(() => h.S.ConfirmAd(new AdTicket(DefaultNonce, 99, AdTarget.Offline)), ErrorCode.InvalidTarget); // 낸 적 없음
            h.AssertFailsUnchanged(() => h.S.ConfirmAd(new AdTicket(DefaultNonce, off.Serial, AdTarget.HuntBuff)), ErrorCode.InvalidTarget); // 대상이 발급 기록과 다름
            h.AssertFailsUnchanged(() => h.S.ConfirmAd(hunt), ErrorCode.InvalidTarget); // 다른 대상의 번호
            // 사용된 사냥 버프 번호를 ConfirmAd에 넣어도 대상 검사가 먼저라 InvalidTarget
            h.S.Ads.CorruptStateForTest(hunt.Serial, AdTicketState.Used);
            Assert.That(h.S.ConfirmAd(hunt).Error, Is.EqualTo(ErrorCode.InvalidTarget));
        }

        [Test]
        public void 정의되지_않은_대상은_InvalidInput()
        {
            var h = WithBundle();
            h.AssertFailsUnchanged(() => h.S.StartAd((AdTarget)99), ErrorCode.InvalidInput);
        }

        [Test]
        public void 클라우드_상태로_교체한_새_세션은_이전_번호를_받지_않는다()
        {
            var h = WithBundle();
            var t = h.StartAd(AdTarget.Offline);
            h.Restart(nonce: DefaultNonce ^ 0xffff);
            h.Sync(T0 + 30 * Min);
            h.AssertFailsUnchanged(() => h.S.ConfirmAd(t), ErrorCode.InvalidTarget);
            Assert.That(h.S.SessionNonce, Is.EqualTo(DefaultNonce ^ 0xffff));
        }

        [Test]
        public void 이_기기를_고르면_세션이_그대로라_번호도_그대로()
        {
            var h = WithBundle();
            var t = h.StartAd(AdTarget.Offline);
            h.Ok(h.S.AdoptCloudRevision(10));
            h.S.CreateSave();
            h.Ok(h.S.ConfirmAd(t));
        }

        [Test]
        public void 광고_적용을_저장하고_재시작해도_2배()
        {
            var h = WithBundle();
            h.Ok(h.S.ConfirmAd(h.StartAd(AdTarget.Offline)));
            h.Pause();
            h.Resume(); // 시간 확인 실패
            h.Restart(); // ConfirmAd 결과가 담긴 기기 기록 뒤 종료·재시작
            Assert.That(h.S.OfflineBundle.AdApplied, Is.True);
            Assert.That(h.S.OfflineBundle.Amount, Is.EqualTo(GameNumber.From(300)));
            h.Sync(T0 + 30 * Min + 30_000);
            h.Ok(h.S.ClaimOffline());
            Assert.That(h.Dust, Is.EqualTo(GameNumber.From(600)));
        }

        [Test]
        public void ConfirmAd_뒤_기기_기록_전에_종료되면_광고_적용이_없는_마지막_사본으로_이어간다()
        {
            var h = WithBundle();
            var before = h.S.CreateSave().Json; // 마지막 기기 기록
            h.Ok(h.S.ConfirmAd(h.StartAd(AdTarget.Offline)));
            var r = GameSessionLoader.Load(h.T, before, DefaultNonce);
            Assert.That(r.Session.OfflineBundle.AdApplied, Is.False, "보장 범위 밖 (문서화)");
        }

        [Test]
        public void 사냥_버프_번호는_발급만_된다()
        {
            var h = New();
            var t = h.StartAd(AdTarget.HuntBuff); // 시간·묶음과 상관없음
            Assert.That(t.Target, Is.EqualTo(AdTarget.HuntBuff));
        }
    }
}
