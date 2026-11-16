using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZodiacSignGuardian.Core.Commands;
using ZodiacSignGuardian.Core.Economy;
using ZodiacSignGuardian.Core.Numbers;

namespace ZodiacSignGuardian.Core.Tests.Economy
{
    [TestFixture]
    public class WalletTests
    {
        const string Dust = "stardust", Crystal = "galaxy_crystal", Stone = "enhance_stone";

        static Wallet New() => new Wallet(new[]
        {
            new KeyValuePair<string, CurrencyKind>(Dust, CurrencyKind.GameNumber),
            new KeyValuePair<string, CurrencyKind>(Crystal, CurrencyKind.Integer),
            new KeyValuePair<string, CurrencyKind>(Stone, CurrencyKind.Integer),
        });

        static List<GameEvent> Give(Wallet w, params CurrencyChange[] changes)
        {
            var events = new List<GameEvent>();
            Assert.That(w.Check(changes), Is.Null, "확인 통과");
            w.Apply(changes, CurrencyReason.MissionReward, events);
            return events;
        }

        /// <summary>실패하는 확인은 상태를 바꾸지 않는다 (위반 규칙 7)</summary>
        static void AssertFails(Wallet w, ErrorCode expected, params CurrencyChange[] changes)
        {
            var before = w.Clone();
            Assert.That(w.Check(changes), Is.EqualTo(expected));
            Assert.That(w.SameState(before), Is.True, "Check는 상태를 바꾸지 않음");
            Assert.Throws<InvalidOperationException>(() => w.Apply(changes, CurrencyReason.StatUpgrade, new List<GameEvent>()));
            Assert.That(w.SameState(before), Is.True, "확인에 실패한 Apply는 상태를 바꾸지 않음");
        }

        [Test]
        public void 처음_잔액은_모두_0이고_종류가_맞다()
        {
            var w = New();
            Assert.That(w.CurrencyIds, Is.EqualTo(new[] { Dust, Crystal, Stone }));
            Assert.That(w.Balance(Dust), Is.EqualTo(CurrencyAmount.Of(GameNumber.Zero)));
            Assert.That(w.Balance(Crystal), Is.EqualTo(CurrencyAmount.Of(0L)));
            Assert.That(w.KindOf(Dust), Is.EqualTo(CurrencyKind.GameNumber));
        }

        [Test]
        public void 받고_쓰면_이벤트에_부호_있는_변화량과_바뀐_뒤_잔액이_남는다()
        {
            var w = New();
            var e1 = Give(w, CurrencyChange.Of(Crystal, 300), CurrencyChange.Of(Dust, GameNumber.From(1.5, 20)));
            var e2 = Give(w, CurrencyChange.Of(Crystal, -120));
            Assert.That(w.Balance(Crystal), Is.EqualTo(CurrencyAmount.Of(180L)));
            Assert.That(w.Balance(Dust).Number, Is.EqualTo(GameNumber.From(1.5, 20)));
            var c = (CurrencyChanged)e2.Single();
            Assert.That(c.CurrencyId, Is.EqualTo(Crystal));
            Assert.That(c.Delta, Is.EqualTo(CurrencyAmount.Of(-120L)));
            Assert.That(c.Balance, Is.EqualTo(CurrencyAmount.Of(180L)));
            Assert.That(c.Reason, Is.EqualTo(CurrencyReason.MissionReward));
            Assert.That(e1.Cast<CurrencyChanged>().Select(x => x.CurrencyId), Is.EqualTo(new[] { Crystal, Dust }), "요청 순서대로");
        }

        [Test]
        public void 잔액을_정확히_다_쓰면_0()
        {
            var w = New();
            Give(w, CurrencyChange.Of(Crystal, 50), CurrencyChange.Of(Dust, GameNumber.From(24)));
            Give(w, CurrencyChange.Of(Crystal, -50), CurrencyChange.Of(Dust, -(GameNumber.From(21) + GameNumber.From(3))));
            Assert.That(w.Balance(Crystal).Integer, Is.EqualTo(0));
            Assert.That(w.Balance(Dust).Number, Is.EqualTo(GameNumber.Zero));
        }

        [Test]
        public void 잔액보다_많이_쓰면_NotEnoughCurrency()
        {
            var w = New();
            Give(w, CurrencyChange.Of(Crystal, 10), CurrencyChange.Of(Dust, GameNumber.From(10)));
            AssertFails(w, ErrorCode.NotEnoughCurrency, CurrencyChange.Of(Crystal, -11));
            AssertFails(w, ErrorCode.NotEnoughCurrency, CurrencyChange.Of(Dust, -GameNumber.From(10.0000000000001)));
            AssertFails(w, ErrorCode.NotEnoughCurrency, CurrencyChange.Of(Crystal, long.MinValue));
        }

        [Test]
        public void 여러_재화는_모두_바뀌거나_하나도_안_바뀐다()
        {
            var w = New();
            Give(w, CurrencyChange.Of(Crystal, 100), CurrencyChange.Of(Stone, 5));
            // 첫째는 되지만 둘째가 모자람
            AssertFails(w, ErrorCode.NotEnoughCurrency, CurrencyChange.Of(Crystal, -30), CurrencyChange.Of(Stone, -6));
            Assert.That(w.Balance(Crystal).Integer, Is.EqualTo(100));
        }

        [Test]
        public void 정수_재화가_long을_넘으면_InvalidInput()
        {
            var w = New();
            Give(w, CurrencyChange.Of(Crystal, long.MaxValue - 1));
            AssertFails(w, ErrorCode.InvalidInput, CurrencyChange.Of(Crystal, 2));
            Give(w, CurrencyChange.Of(Crystal, 1));
            Assert.That(w.Balance(Crystal).Integer, Is.EqualTo(long.MaxValue), "최대값까지는 됨");
            AssertFails(w, ErrorCode.InvalidInput, CurrencyChange.Of(Crystal, 1));
            AssertFails(w, ErrorCode.InvalidInput, CurrencyChange.Of(Crystal, long.MaxValue));
        }

        [Test]
        public void GameNumber_재화의_지수가_long을_넘으면_InvalidInput()
        {
            var w = New();
            Give(w, CurrencyChange.Of(Dust, GameNumber.From(9.0, long.MaxValue)));
            AssertFails(w, ErrorCode.InvalidInput, CurrencyChange.Of(Dust, GameNumber.From(5.0, long.MaxValue)));
        }

        [Test]
        public void 넘침과_부족이_같이_있으면_요청_순서의_첫_실패()
        {
            var w = New();
            Give(w, CurrencyChange.Of(Crystal, long.MaxValue));
            AssertFails(w, ErrorCode.NotEnoughCurrency, CurrencyChange.Of(Stone, -1), CurrencyChange.Of(Crystal, 1));
            AssertFails(w, ErrorCode.InvalidInput, CurrencyChange.Of(Crystal, 1), CurrencyChange.Of(Stone, -1));
        }

        [Test]
        public void 변화량_0은_이벤트가_없다()
        {
            var w = New();
            var events = Give(w, CurrencyChange.Of(Crystal, 0), CurrencyChange.Of(Dust, GameNumber.Zero));
            Assert.That(events, Is.Empty);
        }

        [Test]
        public void 코드_잘못은_예외()
        {
            var w = New();
            Assert.Throws<ArgumentException>(() => w.Check(CurrencyChange.Of("gold", 1)), "정의되지 않은 재화");
            Assert.Throws<ArgumentException>(() => w.Check(CurrencyChange.Of(Dust, 1)), "GameNumber 재화에 정수");
            Assert.Throws<ArgumentException>(() => w.Check(CurrencyChange.Of(Crystal, GameNumber.One)), "정수 재화에 GameNumber");
            Assert.Throws<ArgumentException>(() => w.Check(new[] { CurrencyChange.Of(Crystal, 1), CurrencyChange.Of(Crystal, 2) }), "같은 재화 두 번");
            Assert.Throws<ArgumentOutOfRangeException>(() => w.Apply(CurrencyChange.Of(Crystal, 1), (CurrencyReason)0, new List<GameEvent>()), "정의되지 않은 이유");
            Assert.Throws<ArgumentOutOfRangeException>(() => w.Apply(CurrencyChange.Of(Crystal, 1), (CurrencyReason)999, new List<GameEvent>()));
            Assert.Throws<ArgumentNullException>(() => w.Apply(CurrencyChange.Of(Crystal, 1), CurrencyReason.Hunt, null));
            Assert.That(w.Balance(Crystal).Integer, Is.EqualTo(0), "예외가 나도 상태는 그대로");
            Assert.Throws<ArgumentException>(() => new Wallet(new[] { new KeyValuePair<string, CurrencyKind>("a", CurrencyKind.Integer), new KeyValuePair<string, CurrencyKind>("a", CurrencyKind.Integer) }));
            Assert.Throws<ArgumentException>(() => new Wallet(new[] { new KeyValuePair<string, CurrencyKind>("", CurrencyKind.Integer) }));
        }

        [Test]
        public void 이유_번호는_고정()
        {
            var expected = new Dictionary<string, int>
            {
                ["Hunt"] = 1, ["BossReward"] = 2, ["MissionReward"] = 3, ["AttendanceReward"] = 4,
                ["OfflineReward"] = 5, ["StatUpgrade"] = 6, ["StarDraw"] = 7, ["StaffEnhance"] = 8,
            };
            var actual = Enum.GetValues(typeof(CurrencyReason)).Cast<CurrencyReason>().ToDictionary(e => e.ToString(), e => (int)e);
            Assert.That(actual, Is.EquivalentTo(expected), "번호를 바꾸거나 다시 쓰지 않는다. 새 이유는 끝에 새 번호로");
        }

        [Test]
        public void 되돌리기는_정의된_재화가_모두_있고_음수가_아닐_때만()
        {
            var w = New();
            var good = new Dictionary<string, CurrencyAmount>
            {
                [Dust] = CurrencyAmount.Of(GameNumber.From(3.0, 50)), [Crystal] = CurrencyAmount.Of(7L), [Stone] = CurrencyAmount.Of(0L),
            };
            Assert.That(w.TryRestore(good), Is.True);
            Assert.That(w.Balance(Dust).Number, Is.EqualTo(GameNumber.From(3.0, 50)));

            var before = w.Clone();
            var bad = new List<(string why, Dictionary<string, CurrencyAmount> v)>
            {
                ("null", null),
                ("빠진 재화", good.Where(kv => kv.Key != Stone).ToDictionary(kv => kv.Key, kv => kv.Value)),
                ("모르는 재화", good.Concat(new[] { new KeyValuePair<string, CurrencyAmount>("gold", CurrencyAmount.Of(1L)) }).ToDictionary(kv => kv.Key, kv => kv.Value)),
                ("종류 틀림", good.ToDictionary(kv => kv.Key, kv => kv.Key == Crystal ? CurrencyAmount.Of(GameNumber.From(7)) : kv.Value)),
                ("음수 정수", good.ToDictionary(kv => kv.Key, kv => kv.Key == Stone ? CurrencyAmount.Of(-1L) : kv.Value)),
                ("음수 GameNumber", good.ToDictionary(kv => kv.Key, kv => kv.Key == Dust ? CurrencyAmount.Of(GameNumber.From(-1)) : kv.Value)),
            };
            foreach (var (why, v) in bad)
            {
                Assert.That(w.TryRestore(v), Is.False, why);
                Assert.That(w.SameState(before), Is.True, why + ": 실패하면 그대로");
            }
        }

        [Test]
        public void 무작위로_받고_써도_잔액은_음수가_되지_않고_합계가_맞다()
        {
            var w = New();
            var rng = new System.Random(11);
            long crystal = 0;
            for (var i = 0; i < 20000; i++)
            {
                long d = rng.Next(-500, 1000);
                var change = CurrencyChange.Of(Crystal, d);
                var error = w.Check(change);
                if (crystal + d < 0)
                {
                    Assert.That(error, Is.EqualTo(ErrorCode.NotEnoughCurrency));
                    continue;
                }
                Assert.That(error, Is.Null);
                w.Apply(change, CurrencyReason.Hunt, new List<GameEvent>());
                crystal += d;
                Assert.That(w.Balance(Crystal).Integer, Is.EqualTo(crystal));
            }
        }

        [Test]
        public void CurrencyAmount는_종류를_구분한다()
        {
            Assert.That(CurrencyAmount.Of(0L), Is.Not.EqualTo(CurrencyAmount.Of(GameNumber.Zero)));
            Assert.Throws<InvalidOperationException>(() => { var _ = CurrencyAmount.Of(5L).Number; });
            Assert.Throws<InvalidOperationException>(() => { var _ = CurrencyAmount.Of(GameNumber.One).Integer; });
            Assert.That(CurrencyAmount.Of(-3L).Sign, Is.EqualTo(-1));
            Assert.That(CurrencyAmount.Of(GameNumber.From(-3)).Sign, Is.EqualTo(-1));
            Assert.That(CurrencyAmount.ZeroOf(CurrencyKind.GameNumber).IsZero, Is.True);
        }
    }
}
