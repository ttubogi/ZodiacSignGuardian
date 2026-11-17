using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZodiacSignGuardian.Core.Commands;

namespace ZodiacSignGuardian.Core.Tests.Commands
{
    [TestFixture]
    public class CommandResultTests
    {
        sealed class TestEvent : GameEvent
        {
            public int N;
        }

        [Test]
        public void 실패_코드는_설계서_3절의_아홉_가지_그대로()
        {
            // 이름·숫자를 바꾸면 저장·기록과 Codex 쪽 코드가 어긋난다. 바꾸려면 설계서 3절을 먼저 고친다.
            var expected = new Dictionary<string, int>
            {
                ["NotEnoughCurrency"] = 1, ["NotUnlocked"] = 2, ["AlreadyClaimed"] = 3, ["NotReady"] = 4, ["InvalidTarget"] = 5,
                ["StarNotOwned"] = 6, ["TimeNotVerified"] = 7, ["TimeWentBack"] = 8, ["InvalidInput"] = 9,
            };
            var actual = Enum.GetValues(typeof(ErrorCode)).Cast<ErrorCode>().ToDictionary(e => e.ToString(), e => (int)e);
            Assert.That(actual, Is.EquivalentTo(expected));
        }

        [Test]
        public void 성공은_실패_코드가_없다()
        {
            var r = CommandResult.Success();
            Assert.That(r.Ok, Is.True);
            Assert.That(r.Error, Is.Null);
            Assert.That(r.Events, Is.Empty);
        }

        [Test]
        public void 성공_이벤트는_복사해_둔다()
        {
            var list = new List<GameEvent> { new TestEvent { N = 1 }, new TestEvent { N = 2 } };
            var r = CommandResult.Success(list);
            list.Clear();
            Assert.That(r.Events.Count, Is.EqualTo(2));
            Assert.That(r.Events.Cast<TestEvent>().Select(e => e.N), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(r.Events, Is.Not.SameAs(list));
        }

        [Test]
        public void 성공_이벤트에_null은_넣을_수_없다()
        {
            Assert.Throws<ArgumentNullException>(() => CommandResult.Success(null));
            Assert.Throws<ArgumentException>(() => CommandResult.Success(new GameEvent[] { new TestEvent(), null }));
        }

        [TestCase(ErrorCode.NotEnoughCurrency)]
        [TestCase(ErrorCode.TimeNotVerified)]
        [TestCase(ErrorCode.InvalidInput)]
        public void 실패는_이벤트가_없다(ErrorCode code)
        {
            var r = CommandResult.Fail(code);
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Error, Is.EqualTo(code));
            Assert.That(r.Events, Is.Empty);
        }

        [TestCase(0)]
        [TestCase(10)]
        [TestCase(-1)]
        public void 정의되지_않은_실패_코드는_받지_않는다(int raw)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CommandResult.Fail((ErrorCode)raw));
        }
    }
}
