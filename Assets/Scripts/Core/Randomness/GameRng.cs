using System;
using System.Collections.Generic;

namespace ZodiacSignGuardian.Core.Randomness
{
    /// <summary>난수 흐름. 숫자 값은 저장 순서이므로 바꾸지 않는다. (C2 결정 Q3, 2026-11-16)</summary>
    public enum RngStream
    {
        /// <summary>별 뽑기</summary>
        Gacha = 0,
        /// <summary>몬스터 드롭</summary>
        Drop = 1,
        /// <summary>전투(치명타 등)</summary>
        Combat = 2,
    }

    /// <summary>
    /// 판정용 난수. 용도별 세 흐름이 서로 독립이라, 전투에서 난수를 몇 번 쓰든 다음 뽑기 결과가 바뀌지 않는다.
    /// <list type="bullet">
    /// <item>알고리즘: xoshiro256** (https://prng.di.unimi.it/xoshiro256starstar.c 그대로). System.Random은 Mono와 .NET에서 결과가 달라 쓰지 않는다.</item>
    /// <item>시드: 64비트 하나. SplitMix64(https://prng.di.unimi.it/splitmix.c)의 연속 출력 12개를 뽑기 4개 → 드롭 4개 → 전투 4개 순서로 상태에 넣는다.
    ///       시드는 새 계정 때 Services가 보안 난수로 한 번 만들고, Core는 다시 정하지 않는다.</item>
    /// <item>확률은 1만분의 1 단위 정수. Roll(p)는 p가 0이든 10000이든 NextInt(10000)을 한 번 부른다(표의 확률 하나를 바꿔도 뒤의 결과가 밀리지 않게).</item>
    /// <item>저장: 흐름 순서대로 상태 값마다 16진수 소문자 16자리, 모두 12개. 한 흐름의 네 값이 모두 0이면 거부한다(그 상태에서는 0만 나온다).</item>
    /// <item>실패할 명령은 난수를 쓰기 전에 조건을 모두 확인한다(C2 결정 Q4). 테스트는 실패 전후 상태가 같은지 비교한다.</item>
    /// </list>
    /// View의 연출용 난수는 이 흐름을 쓰지 않는다.
    /// </summary>
    public sealed class GameRng
    {
        public const int StreamCount = 3;
        public const int StateWords = 4;
        /// <summary>확률의 단위: 10000 = 100%.</summary>
        public const int ChanceScale = 10000;

        // 흐름마다 64비트 4개. [흐름 * 4 + i]
        readonly ulong[] _s = new ulong[StreamCount * StateWords];

        GameRng()
        {
        }

        /// <summary>시드 하나에서 세 흐름을 만든다. 같은 시드면 어느 기기에서도 같은 결과.</summary>
        public static GameRng FromSeed(ulong seed)
        {
            var rng = new GameRng();
            var x = seed;
            for (var i = 0; i < rng._s.Length; i++) rng._s[i] = SplitMix64(ref x);
            for (var k = 0; k < StreamCount; k++)
            {
                // 2^-256 확률이라 실제로는 일어나지 않지만, 일어나면 그 흐름이 0만 내므로 막는다
                if (rng.IsZeroStream(k)) throw new InvalidOperationException("시드 " + seed + "에서 흐름 " + (RngStream)k + "의 상태가 모두 0");
            }
            return rng;
        }

        static ulong SplitMix64(ref ulong x)
        {
            unchecked
            {
                x += 0x9e3779b97f4a7c15UL;
                var z = x;
                z = (z ^ (z >> 30)) * 0xbf58476d1ce4e5b9UL;
                z = (z ^ (z >> 27)) * 0x94d049bb133111ebUL;
                return z ^ (z >> 31);
            }
        }

        static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

        /// <summary>그 흐름에서 64비트 하나. (xoshiro256** next)</summary>
        public ulong NextUInt64(RngStream stream)
        {
            var o = Offset(stream);
            unchecked
            {
                var result = Rotl(_s[o + 1] * 5, 7) * 9;
                var t = _s[o + 1] << 17;
                _s[o + 2] ^= _s[o + 0];
                _s[o + 3] ^= _s[o + 1];
                _s[o + 1] ^= _s[o + 2];
                _s[o + 0] ^= _s[o + 3];
                _s[o + 2] ^= t;
                _s[o + 3] = Rotl(_s[o + 3], 45);
                return result;
            }
        }

        /// <summary>
        /// [0, n) 범위의 정수. n &lt;= 0이면 ArgumentOutOfRangeException.
        /// 치우침을 없애려고 2^64를 n으로 나눈 나머지보다 작은 값은 버리고 다시 뽑는다 (버릴 확률은 n / 2^64 미만).
        /// </summary>
        public int NextInt(RngStream stream, int n)
        {
            if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n), n, "n은 1 이상");
            var bound = (ulong)n;
            var threshold = unchecked(0UL - bound) % bound; // 2^64 mod n
            while (true)
            {
                var r = NextUInt64(stream);
                if (r >= threshold) return (int)(r % bound);
            }
        }

        /// <summary>
        /// 확률 p / 10000으로 참. p는 0~10000 (밖이면 ArgumentOutOfRangeException).
        /// p와 상관없이 NextInt(10000)을 한 번 부른다.
        /// </summary>
        public bool Roll(RngStream stream, int chance)
        {
            if (chance < 0 || chance > ChanceScale) throw new ArgumentOutOfRangeException(nameof(chance), chance, "확률은 0~" + ChanceScale);
            return NextInt(stream, ChanceScale) < chance;
        }

        static int Offset(RngStream stream)
        {
            var k = (int)stream;
            if (k < 0 || k >= StreamCount) throw new ArgumentOutOfRangeException(nameof(stream), stream, "정의되지 않은 흐름");
            return k * StateWords;
        }

        bool IsZeroStream(int k)
        {
            var o = k * StateWords;
            return _s[o] == 0 && _s[o + 1] == 0 && _s[o + 2] == 0 && _s[o + 3] == 0;
        }

        // ---- 복사·비교 ----

        public GameRng Clone()
        {
            var c = new GameRng();
            Array.Copy(_s, c._s, _s.Length);
            return c;
        }

        /// <summary>세 흐름의 상태가 모두 같은지 (테스트에서 실패한 명령이 난수를 쓰지 않았는지 볼 때).</summary>
        public bool SameState(GameRng other)
        {
            if (other == null) return false;
            for (var i = 0; i < _s.Length; i++)
            {
                if (_s[i] != other._s[i]) return false;
            }
            return true;
        }

        // ---- 저장 ----

        /// <summary>저장용 12개: 뽑기 s0~s3, 드롭 s0~s3, 전투 s0~s3. 값마다 16진수 소문자 16자리.</summary>
        public IReadOnlyList<string> ToSaveStrings()
        {
            var list = new string[_s.Length];
            for (var i = 0; i < _s.Length; i++) list[i] = ToHex16(_s[i]);
            return list;
        }

        /// <summary>ToSaveStrings의 반대. 개수·형식이 틀리거나 한 흐름이 모두 0이면 false.</summary>
        public static bool TryFromSaveStrings(IReadOnlyList<string> values, out GameRng rng)
        {
            rng = null;
            if (values == null || values.Count != StreamCount * StateWords) return false;
            var r = new GameRng();
            for (var i = 0; i < values.Count; i++)
            {
                if (!TryParseHex16(values[i], out r._s[i])) return false;
            }
            for (var k = 0; k < StreamCount; k++)
            {
                if (r.IsZeroStream(k)) return false;
            }
            rng = r;
            return true;
        }

        const string HexDigits = "0123456789abcdef";

        static string ToHex16(ulong v)
        {
            var c = new char[16];
            for (var i = 15; i >= 0; i--)
            {
                c[i] = HexDigits[(int)(v & 0xF)];
                v >>= 4;
            }
            return new string(c);
        }

        static bool TryParseHex16(string s, out ulong v)
        {
            v = 0;
            if (s == null || s.Length != 16) return false;
            foreach (var ch in s)
            {
                int d;
                if (ch >= '0' && ch <= '9') d = ch - '0';
                else if (ch >= 'a' && ch <= 'f') d = ch - 'a' + 10;
                else return false; // 대문자·공백·기호는 받지 않는다 (저장 규격은 소문자 하나뿐)
                v = (v << 4) | (uint)d;
            }
            return true;
        }
    }
}
