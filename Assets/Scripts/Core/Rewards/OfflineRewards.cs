using System;
using System.Collections.Generic;
using ZodiacSignGuardian.Core.Commands;
using ZodiacSignGuardian.Core.Numbers;

namespace ZodiacSignGuardian.Core.Rewards
{
    /// <summary>offline_reward 표의 값.</summary>
    public sealed class OfflineConfig
    {
        public int MaxMinutes { get; }
        public int MinMinutes { get; }
        public int AdMultiplier { get; }

        public OfflineConfig(int maxMinutes, int minMinutes, int adMultiplier)
        {
            if (maxMinutes < 1) throw new ArgumentOutOfRangeException(nameof(maxMinutes), maxMinutes, "1 이상");
            if (minMinutes < 1) throw new ArgumentOutOfRangeException(nameof(minMinutes), minMinutes, "1 이상");
            if (adMultiplier < 1) throw new ArgumentOutOfRangeException(nameof(adMultiplier), adMultiplier, "1 이상");
            MaxMinutes = maxMinutes;
            MinMinutes = minMinutes;
            AdMultiplier = adMultiplier;
        }
    }

    /// <summary>미수령 묶음에 새 분이 더해졌을 때. 값은 지금 미수령 합계(분, 배율 적용 전 금액)다.</summary>
    public sealed class OfflineRewardReady : GameEvent
    {
        public long Minutes { get; }
        public GameNumber Amount { get; }
        public OfflineRewardReady(long minutes, GameNumber amount) { Minutes = minutes; Amount = amount; }
        public override string ToString() => "OfflineRewardReady " + Minutes + "분 " + Amount;
    }

    /// <summary>화면이 읽는 미수령 묶음.</summary>
    public sealed class OfflineBundle
    {
        public long Id { get; }
        public long Minutes { get; }
        /// <summary>배율 적용 전 금액(별가루).</summary>
        public GameNumber Amount { get; }
        public bool AdApplied { get; }

        public OfflineBundle(long id, long minutes, GameNumber amount, bool adApplied)
        {
            Id = id;
            Minutes = minutes;
            Amount = amount;
            AdApplied = adApplied;
        }
    }

    /// <summary>
    /// 오프라인 보상의 정산·미수령 묶음 (설계서 v0.9 3절 "오프라인 보상").
    /// <list type="bullet">
    /// <item>활성 경과(µs)는 정산 기준 시점 뒤의 경과 합(자르기 전)이다. 기준 시각이 없어도 쌓는다.</item>
    /// <item>정산은 정산 대기 중 동기화를 받아들일 때만 한다(부르는 쪽이 판단).</item>
    /// <item>분당 보상이 0이면 정산만 끝내고 미수령 분·묶음을 만들지 않는다(E4).</item>
    /// <item>묶음이 있으면 분은 1 이상 max_minutes 이하, 금액은 0보다 크다(위반 규칙 4).</item>
    /// </list>
    /// </summary>
    internal sealed class OfflineRewards
    {
        public OfflineConfig Config { get; }

        /// <summary>정산 기준 시점(ms). 새 계정은 첫 유효 동기화 전까지 null.</summary>
        public long? SettlementPointUtcMs { get; private set; }
        public long ActiveElapsedUs { get; private set; }
        /// <summary>떠날 때 스테이지(수급률을 고정하는 값).</summary>
        public int LeaveStage { get; private set; } = 1;
        /// <summary>미수령 묶음 번호. 없으면 0.</summary>
        public long BundleId { get; private set; }
        public long BundleMinutes { get; private set; }
        public GameNumber BundleAmount { get; private set; } = GameNumber.Zero;
        public bool BundleAdApplied { get; private set; }
        public long NextBundleId { get; private set; } = 1;

        public OfflineRewards(OfflineConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public bool HasBundle => BundleId != 0;
        public OfflineBundle Bundle => HasBundle ? new OfflineBundle(BundleId, BundleMinutes, BundleAmount, BundleAdApplied) : null;

        public bool CanAddActive(long us) => us >= 0 && ActiveElapsedUs <= long.MaxValue - us;

        public void AddActive(long us)
        {
            if (!CanAddActive(us)) throw new ArgumentOutOfRangeException(nameof(us), us, "CanAddActive를 먼저 확인해야 함");
            ActiveElapsedUs += us;
        }

        public void SetLeaveStage(int stage)
        {
            if (stage < 1) throw new ArgumentOutOfRangeException(nameof(stage), stage, "1 이상");
            LeaveStage = stage;
        }

        /// <summary>
        /// 비활성 µs (설계서 v0.9 3절 정산 2단계, Codex v0.9 검토 7-가). 빼기 전에 비교해 넘치지 않는다.
        /// anchorUtcMs·SettlementPointUtcMs는 0 ~ 9999년이라 차이 × 1000은 long 안이다.
        /// </summary>
        public static long InactiveUs(long anchorUtcMs, long settlementPointUtcMs, long activeElapsedUs)
        {
            var diffUs = (anchorUtcMs - settlementPointUtcMs) * 1000L;
            if (diffUs <= 0 || activeElapsedUs >= diffUs) return 0;
            return diffUs - activeElapsedUs;
        }

        /// <summary>
        /// 정산 (정산 대기 중 동기화를 받아들였을 때). rate는 떠날 때 스테이지의 분당 별가루.
        /// 새 분이 묶음에 더해졌으면 OfflineRewardReady. 정산 기준 시점은 뒤로 가지 않는다(E3: 서버가 뒤로 가면 보상이 줄 수 있음).
        /// </summary>
        public void Settle(long anchorUtcMs, GameNumber rate, List<GameEvent> events)
        {
            if (SettlementPointUtcMs == null)
            {
                // 새 계정: 기준 시점만 만들고 보상은 없다
                SettlementPointUtcMs = anchorUtcMs;
                ActiveElapsedUs = 0;
                return;
            }

            var inactive = InactiveUs(anchorUtcMs, SettlementPointUtcMs.Value, ActiveElapsedUs);
            var minutes = inactive / (60L * 1_000_000L);
            if (minutes < Config.MinMinutes) minutes = 0;
            var room = Config.MaxMinutes - BundleMinutes;
            if (minutes > room) minutes = room;

            if (minutes > 0 && rate.Sign > 0)
            {
                GameNumber added;
                bool ok;
                try
                {
                    added = BundleAmount + rate * minutes;
                    ok = true;
                }
                catch (OverflowException)
                {
                    // 지수가 long을 넘는 경우(사실상 없음): 이 정산분은 더하지 않고 정산만 끝낸다
                    added = BundleAmount;
                    ok = false;
                }
                if (ok)
                {
                    if (!HasBundle)
                    {
                        BundleId = NextBundleId;
                        NextBundleId++;
                        BundleAdApplied = false;
                    }
                    BundleMinutes += minutes;
                    BundleAmount = added;
                    events.Add(new OfflineRewardReady(BundleMinutes, BundleAmount));
                }
            }

            if (anchorUtcMs > SettlementPointUtcMs.Value) SettlementPointUtcMs = anchorUtcMs;
            ActiveElapsedUs = 0;
        }

        /// <summary>광고 적용을 참으로 (ConfirmAd). 묶음이 있어야 한다.</summary>
        public void ApplyAd()
        {
            if (!HasBundle) throw new InvalidOperationException("묶음이 없는데 광고 적용");
            BundleAdApplied = true;
        }

        /// <summary>받을 금액(배율 적용). 묶음이 없거나 금액이 0이면 null.</summary>
        public GameNumber? PayoutOrNull()
        {
            if (!HasBundle || BundleAmount.Sign <= 0) return null;
            return BundleAdApplied ? BundleAmount * Config.AdMultiplier : BundleAmount;
        }

        /// <summary>받은 뒤 묶음을 끝낸다.</summary>
        public void ClearBundle()
        {
            BundleId = 0;
            BundleMinutes = 0;
            BundleAmount = GameNumber.Zero;
            BundleAdApplied = false;
        }

        public bool TryRestore(long? settlementPointUtcMs, long activeElapsedUs, long leaveStage, long nextBundleId,
            long bundleId, long bundleMinutes, GameNumber bundleAmount, bool adApplied, List<string> errors)
        {
            var ok = true;
            if (settlementPointUtcMs != null && !Timing.Clock.IsValidServerUtcMs(settlementPointUtcMs.Value)) { errors.Add("offline.settlementPointUtcMs: 범위 밖"); ok = false; }
            if (activeElapsedUs < 0) { errors.Add("offline.activeElapsedUs: 0 이상"); ok = false; }
            if (leaveStage < 1 || leaveStage > int.MaxValue) { errors.Add("offline.leaveStage: 1 이상"); ok = false; }
            if (nextBundleId < 1) { errors.Add("offline.nextBundleId: 1 이상"); ok = false; }
            if (bundleId != 0)
            {
                if (bundleId < 1 || bundleId >= nextBundleId) { errors.Add("offline.bundle.id: 1 이상 nextBundleId 미만"); ok = false; }
                if (bundleMinutes < 1 || bundleMinutes > Config.MaxMinutes) { errors.Add("offline.bundle.minutes: 1~" + Config.MaxMinutes); ok = false; }
                if (bundleAmount.Sign <= 0) { errors.Add("offline.bundle.amount: 0보다 커야 함"); ok = false; }
            }
            if (!ok) return false;
            SettlementPointUtcMs = settlementPointUtcMs;
            ActiveElapsedUs = activeElapsedUs;
            LeaveStage = (int)leaveStage;
            NextBundleId = nextBundleId;
            BundleId = bundleId;
            BundleMinutes = bundleId == 0 ? 0 : bundleMinutes;
            BundleAmount = bundleId == 0 ? GameNumber.Zero : bundleAmount;
            BundleAdApplied = bundleId != 0 && adApplied;
            return true;
        }

        public bool SameState(OfflineRewards o)
        {
            return o != null && o.SettlementPointUtcMs == SettlementPointUtcMs && o.ActiveElapsedUs == ActiveElapsedUs && o.LeaveStage == LeaveStage
                && o.BundleId == BundleId && o.BundleMinutes == BundleMinutes && o.BundleAmount.Equals(BundleAmount)
                && o.BundleAdApplied == BundleAdApplied && o.NextBundleId == NextBundleId;
        }

        public OfflineRewards Clone()
        {
            return new OfflineRewards(Config)
            {
                SettlementPointUtcMs = SettlementPointUtcMs, ActiveElapsedUs = ActiveElapsedUs, LeaveStage = LeaveStage,
                BundleId = BundleId, BundleMinutes = BundleMinutes, BundleAmount = BundleAmount, BundleAdApplied = BundleAdApplied,
                NextBundleId = NextBundleId,
            };
        }
    }
}
