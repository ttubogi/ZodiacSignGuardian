using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZodiacSignGuardian.Core.Economy;
using ZodiacSignGuardian.Core.Stars;
using ZodiacSignGuardian.Data.Tables;
using ZodiacSignGuardian.Core.Tests.Data;
using static ZodiacSignGuardian.Core.Tests.Session.C3Harness;

namespace ZodiacSignGuardian.Core.Tests.Session
{
    /// <summary>
    /// C4 테스트용 표와 도우미. C3 표에 더해 2장 판(시그너스, 칸 2가 가운데)과 2장 스테이지를 m1로 바꾸고,
    /// draw_star·place_star 임무를 넣는다. 뽑기 재화는 gold, 별조각은 shard (TestTables).
    /// </summary>
    internal static class C4
    {
        internal static readonly long T0 = KstMs(2026, 11, 19, 12, 0, 0);
        internal const long Min = 60_000L;
        internal const long Day = 24 * 60 * Min;

        internal static Dictionary<string, string> Texts()
        {
            return C3Harness.Texts()
                .Replace("boards", "2,cygnus,1,2,v1\n2,cygnus,2,3,v1\n2,cygnus,2,4,v1\n2,cygnus,2,5,v1", "2,cygnus,1,2,m1\n2,cygnus,2,3,m1\n2,cygnus,2,4,m1\n2,cygnus,2,5,m1")
                .Replace("stages", "gold,300,,,,,v1", "gold,300,,,,,m1")
                .Replace("stages", "3.6e2,30,dungeon_key,5,v1", "3.6e2,30,gold,5,m1")
                .AppendRow("daily_missions", "draw_any,daily,draw_star,100,gold,1,m1")
                .AppendRow("daily_missions", "place_any,daily,place_star,100,gold,1,m1");
        }

        /// <summary>서버 시각을 받은 새 계정 (시드 7).</summary>
        internal static C3Harness New(Dictionary<string, string> texts = null, bool sync = true)
        {
            var h = C3Harness.New(Tables(texts ?? Texts()));
            if (sync) h.Sync(T0);
            return h;
        }

        internal static void Give(C3Harness h, string currency, long amount) => h.S.Wallet.CorruptBalanceForTest(currency, CurrencyAmount.Of(amount));
        internal static long Bal(C3Harness h, string currency) => h.S.Balance(currency).Integer;

        internal static long Own(C3Harness h, string starId)
        {
            h.S.GainStarForTest(starId);
            return h.S.OwnedStars.Single(s => s.StarId == starId).InstanceId;
        }

        internal static OwnedStar Star(C3Harness h, long instanceId) => h.S.OwnedStars.Single(s => s.InstanceId == instanceId);

        internal static BoardStatus Board(C3Harness h, int chapter) => h.S.BoardStatuses.Single(b => b.Chapter == chapter);

        internal static long[] Slots(C3Harness h, int chapter) => Board(h, chapter).Slots.ToArray();

        /// <summary>저장 글의 한 부분을 바꾼다. 바꿀 글이 없으면 테스트 실패(바꾸지 않은 채 통과하지 않게).</summary>
        internal static string R(string json, string from, string to)
        {
            Assert.That(json.Contains(from), Is.True, "변형할 글이 없음: " + from + "\n" + json);
            return json.Replace(from, to);
        }
    }

    // 이벤트 목록 요약 (순서 확인용)
    internal static class EventText
    {
        internal static string[] Names(IEnumerable<ZodiacSignGuardian.Core.Commands.GameEvent> events) => events.Select(e => e.GetType().Name).ToArray();
    }
}

