using System;
using ZodiacSignGuardian.Core.Numbers;

namespace ZodiacSignGuardian.Core.Economy
{
    /// <summary>재화의 수 종류. currencies 표의 number_type(integer / game_number)과 같다.</summary>
    public enum CurrencyKind
    {
        /// <summary>정확해야 하는 재화(은하수정·별조각·강화석): long</summary>
        Integer = 0,
        /// <summary>아주 커질 수 있는 재화(별가루): GameNumber</summary>
        GameNumber = 1,
    }

    /// <summary>
    /// 재화의 양. 정수 재화와 GameNumber 재화를 구분해 담는다 (Codex 검토 2026-11-16: 둘을 double로 합치지 않는다).
    /// 이벤트에서는 변화량(부호 있음)과 바뀐 뒤 잔액에 쓴다.
    /// </summary>
    public readonly struct CurrencyAmount : IEquatable<CurrencyAmount>
    {
        public CurrencyKind Kind { get; }
        readonly long _integer;
        readonly GameNumber _number;

        CurrencyAmount(CurrencyKind kind, long integer, GameNumber number)
        {
            Kind = kind;
            _integer = integer;
            _number = number;
        }

        public static CurrencyAmount Of(long value) => new CurrencyAmount(CurrencyKind.Integer, value, GameNumber.Zero);
        public static CurrencyAmount Of(GameNumber value) => new CurrencyAmount(CurrencyKind.GameNumber, 0, value);
        public static CurrencyAmount ZeroOf(CurrencyKind kind) => kind == CurrencyKind.Integer ? Of(0L) : Of(GameNumber.Zero);

        /// <summary>정수 재화의 값. GameNumber 재화에서 부르면 InvalidOperationException.</summary>
        public long Integer => Kind == CurrencyKind.Integer ? _integer : throw new InvalidOperationException("GameNumber 재화를 정수로 읽음");

        /// <summary>GameNumber 재화의 값. 정수 재화에서 부르면 InvalidOperationException.</summary>
        public GameNumber Number => Kind == CurrencyKind.GameNumber ? _number : throw new InvalidOperationException("정수 재화를 GameNumber로 읽음");

        public int Sign => Kind == CurrencyKind.Integer ? Math.Sign(_integer) : _number.Sign;
        public bool IsZero => Sign == 0;

        public bool Equals(CurrencyAmount other) => Kind == other.Kind && _integer == other._integer && _number.Equals(other._number);
        public override bool Equals(object obj) => obj is CurrencyAmount a && Equals(a);
        public override int GetHashCode() => ((int)Kind * 397) ^ _integer.GetHashCode() ^ _number.GetHashCode();
        public static bool operator ==(CurrencyAmount a, CurrencyAmount b) => a.Equals(b);
        public static bool operator !=(CurrencyAmount a, CurrencyAmount b) => !a.Equals(b);

        public override string ToString() => Kind == CurrencyKind.Integer ? _integer.ToString(System.Globalization.CultureInfo.InvariantCulture) : _number.ToString();
    }
}
