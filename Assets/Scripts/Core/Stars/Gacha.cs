using System;
using System.Collections.Generic;
using ZodiacSignGuardian.Core.Commands;
using ZodiacSignGuardian.Core.Randomness;

namespace ZodiacSignGuardian.Core.Stars
{
    /// <summary>뽑기 비용을 무엇으로 내는가. 숫자는 Services·저장 기록과 이어지므로 바꾸지 않는다.</summary>
    public enum DrawPayment
    {
        /// <summary>뽑기 재화(은하수정). 날짜 의존 기능이 아니다.</summary>
        Crystal = 1,
        /// <summary>하루 무료 뽑기. 날짜 의존 기능이고 count는 1만.</summary>
        Free = 2,
    }

    /// <summary>
    /// 별 뽑기 (설계서 v0.12 "별 뽑기"). 한 번 뽑기: 뽑기 난수 흐름에서 NextInt(10000)으로 희귀도(common → rare → legend 순서로
    /// draw_weight를 쌓은 구간), 이어서 그 희귀도의 별을 star_id 서수 순서로 세운 목록에서 NextInt(별 수)로 하나.
    /// 확률이 0인 희귀도는 구간이 비어 나오지 않는다.
    /// </summary>
    internal static class Gacha
    {
        public static bool IsValidCount(int count) => count == 1 || count == 10;

        /// <summary>rng에서 count번 뽑은 star_id들. rng를 실제로 진행시키므로 명령은 사본에서 먼저 정한다.</summary>
        public static List<string> Draw(GameRng rng, StarCatalog catalog, int count)
        {
            var result = new List<string>(count);
            for (var i = 0; i < count; i++)
            {
                var roll = rng.NextInt(RngStream.Gacha, GameRng.ChanceScale);
                var rarity = Rarity.Legend;
                var acc = 0;
                foreach (Rarity r in Enum.GetValues(typeof(Rarity)))
                {
                    acc += catalog.Rarity(r).DrawWeight;
                    if (roll < acc) { rarity = r; break; }
                }
                var pool = catalog.Pool(rarity);
                result.Add(pool[rng.NextInt(RngStream.Gacha, pool.Count)]);
            }
            return result;
        }
    }

    /// <summary>
    /// 하루 무료 뽑기 기록: 쓴 일일 구간과 그 구간에 쓴 수. 하루 초기화(05시)에 따로 비우지 않고, 처리한 일일 구간이 바뀌면 남은 수가 다시 찬다.
    /// </summary>
    internal sealed class FreeDraws
    {
        /// <summary>마지막으로 무료 뽑기를 쓴 일일 구간. 쓴 적이 없으면 null.</summary>
        public long? Day { get; private set; }
        /// <summary>Day 구간에 쓴 수.</summary>
        public int Used { get; private set; }

        /// <summary>그 구간에 남은 무료 뽑기 수.</summary>
        public int LeftOn(long day, int perDay) => Day == day ? Math.Max(0, perDay - Used) : perDay;

        /// <summary>그 구간에 쓸 수 있으면 null, 다 썼으면(또는 하루 0번이면) AlreadyClaimed.</summary>
        public ErrorCode? Check(long day, int perDay) => LeftOn(day, perDay) > 0 ? (ErrorCode?)null : ErrorCode.AlreadyClaimed;

        public void Use(long day, int perDay)
        {
            if (Check(day, perDay) != null) throw new InvalidOperationException("남은 무료 뽑기가 없음");
            if (Day == day) Used++;
            else { Day = day; Used = 1; }
        }

        /// <summary>저장한 값으로. day가 없으면 used는 0, 있으면 1~perDay. 틀리면 errors에 남기고 false.</summary>
        public bool TryRestore(long? day, long used, int perDay, List<string> errors)
        {
            if (day == null ? used != 0 : used < 1 || used > perDay)
            {
                errors.Add("freeDraw.used: " + used + "가 맞지 않음 (쓴 날이 없으면 0, 있으면 1~" + perDay + ")");
                return false;
            }
            Day = day;
            Used = (int)used;
            return true;
        }

        public bool SameState(FreeDraws o) => o != null && o.Day == Day && o.Used == Used;
        public FreeDraws Clone() => new FreeDraws { Day = Day, Used = Used };
    }
}
