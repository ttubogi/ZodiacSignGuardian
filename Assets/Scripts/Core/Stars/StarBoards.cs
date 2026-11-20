using System;
using System.Collections.Generic;
using ZodiacSignGuardian.Core.Commands;

namespace ZodiacSignGuardian.Core.Stars
{
    /// <summary>판의 연결선 하나 (칸 번호 1~5, SlotA &lt; SlotB).</summary>
    public readonly struct BoardLink : IEquatable<BoardLink>
    {
        public int SlotA { get; }
        public int SlotB { get; }

        public BoardLink(int a, int b)
        {
            SlotA = Math.Min(a, b);
            SlotB = Math.Max(a, b);
        }

        public bool Equals(BoardLink other) => SlotA == other.SlotA && SlotB == other.SlotB;
        public override bool Equals(object obj) => obj is BoardLink l && Equals(l);
        public override int GetHashCode() => SlotA * 8 + SlotB;
        public override string ToString() => SlotA + "-" + SlotB;
    }

    /// <summary>켜진 연결선과 그 종류(같은 속성·상생).</summary>
    public readonly struct ActiveLink : IEquatable<ActiveLink>
    {
        public BoardLink Link { get; }
        public LinkKind Kind { get; }
        public ActiveLink(BoardLink link, LinkKind kind) { Link = link; Kind = kind; }
        public bool Equals(ActiveLink other) => Link.Equals(other.Link) && Kind == other.Kind;
        public override bool Equals(object obj) => obj is ActiveLink l && Equals(l);
        public override int GetHashCode() => Link.GetHashCode() * 3 + (int)Kind;
        public override string ToString() => Link + "(" + Kind + ")";
    }

    /// <summary>판 하나의 정의 (boards.csv의 지금 빌드 장).</summary>
    public sealed class BoardDefinition
    {
        public int Chapter { get; }
        public IReadOnlyList<BoardLink> Links { get; }

        public BoardDefinition(int chapter, IEnumerable<BoardLink> links)
        {
            if (chapter < 1) throw new ArgumentOutOfRangeException(nameof(chapter));
            var list = new List<BoardLink>();
            foreach (var l in links)
            {
                if (l.SlotA < 1 || l.SlotB > StarBoards.SlotCount || l.SlotA == l.SlotB) throw new ArgumentException("칸 번호가 맞지 않는 선: " + l);
                if (list.Contains(l)) throw new ArgumentException("같은 선이 둘: " + l);
                list.Add(l);
            }
            list.Sort((x, y) => x.SlotA != y.SlotA ? x.SlotA.CompareTo(y.SlotA) : x.SlotB.CompareTo(y.SlotB));
            Chapter = chapter;
            Links = list;
        }
    }

    /// <summary>판의 배치가 바뀜 (그 판마다 한 번).</summary>
    public sealed class BoardChanged : GameEvent
    {
        public int Chapter { get; }
        public BoardChanged(int chapter) { Chapter = chapter; }
        public override string ToString() => "BoardChanged " + Chapter;
    }

    /// <summary>판의 켜진 연결선 목록이 바뀜. Links는 바뀐 뒤의 전체 목록.</summary>
    public sealed class LinkBonusChanged : GameEvent
    {
        public int Chapter { get; }
        public IReadOnlyList<ActiveLink> Links { get; }
        public LinkBonusChanged(int chapter, IReadOnlyList<ActiveLink> links) { Chapter = chapter; Links = links; }
        public override string ToString() => "LinkBonusChanged " + Chapter + " [" + string.Join(", ", Links) + "]";
    }

    /// <summary>판이 미완성에서 완성(다섯 칸 모두 참)으로 바뀜.</summary>
    public sealed class BoardCompleted : GameEvent
    {
        public int Chapter { get; }
        public BoardCompleted(int chapter) { Chapter = chapter; }
        public override string ToString() => "BoardCompleted " + Chapter;
    }

    /// <summary>View가 읽는 판 하나의 상태.</summary>
    public sealed class BoardStatus
    {
        public int Chapter { get; }
        public bool IsOpen { get; }
        /// <summary>칸 1~5의 별 인스턴스 번호(인덱스 0이 칸 1). 빈 칸은 0.</summary>
        public IReadOnlyList<long> Slots { get; }
        public IReadOnlyList<BoardLink> Links { get; }
        public IReadOnlyList<ActiveLink> ActiveLinks { get; }
        public bool IsComplete { get; }

        public BoardStatus(int chapter, bool isOpen, IReadOnlyList<long> slots, IReadOnlyList<BoardLink> links, IReadOnlyList<ActiveLink> activeLinks, bool isComplete)
        {
            Chapter = chapter;
            IsOpen = isOpen;
            Slots = slots;
            Links = links;
            ActiveLinks = activeLinks;
            IsComplete = isComplete;
        }
    }

    /// <summary>
    /// 별자리 판 (설계서 v0.12 "별자리 판"). 지금 빌드의 장마다 칸 다섯 개. 판 N은 진행 중인 장이 N 이상이면 열려 있다(B4).
    /// 켜진 연결선 목록은 배치가 바뀔 때마다 다시 계산해 둔다(위반 규칙 6이 배치에서 다시 계산한 값과 비교한다).
    /// 상태를 바꾸는 함수는 조건을 이미 확인한 명령만 부른다.
    /// </summary>
    internal sealed class StarBoards
    {
        public const int SlotCount = 5;

        readonly StarCatalog _catalog;
        readonly List<BoardDefinition> _defs = new List<BoardDefinition>();
        readonly Dictionary<int, long[]> _slots = new Dictionary<int, long[]>();
        readonly Dictionary<int, List<ActiveLink>> _active = new Dictionary<int, List<ActiveLink>>();

        public StarBoards(StarCatalog catalog, IEnumerable<BoardDefinition> boards)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            foreach (var b in boards)
            {
                if (_slots.ContainsKey(b.Chapter)) throw new ArgumentException("같은 장의 판이 둘: " + b.Chapter);
                _defs.Add(b);
                _slots[b.Chapter] = new long[SlotCount];
                _active[b.Chapter] = new List<ActiveLink>();
            }
            _defs.Sort((x, y) => x.Chapter.CompareTo(y.Chapter));
        }

        public IReadOnlyList<BoardDefinition> Definitions => _defs;
        public bool Has(int chapter) => _slots.ContainsKey(chapter);
        public BoardDefinition Definition(int chapter) => _defs.Find(d => d.Chapter == chapter);
        public IReadOnlyList<long> SlotsOf(int chapter) => _slots[chapter];
        public IReadOnlyList<ActiveLink> ActiveLinksOf(int chapter) => _active[chapter];
        public bool IsComplete(int chapter) => Array.IndexOf(_slots[chapter], 0L) < 0;
        public static bool IsOpen(int boardChapter, int currentChapter) => boardChapter <= currentChapter;

        /// <summary>별 인스턴스가 놓인 곳. 없으면 false.</summary>
        public bool TryLocate(long instanceId, out int chapter, out int slot)
        {
            foreach (var d in _defs)
            {
                var s = _slots[d.Chapter];
                for (var i = 0; i < SlotCount; i++)
                {
                    if (s[i] == instanceId) { chapter = d.Chapter; slot = i + 1; return true; }
                }
            }
            chapter = 0;
            slot = 0;
            return false;
        }

        /// <summary>
        /// 켜진 연결선 계산: 선의 두 칸에 모두 별이 있고 같은 속성이거나 상생 쌍이면 켜짐. 별 정보는 inventory에서 찾는다.
        /// 위반 규칙 6도 이 함수가 아니라 따로 계산한다(Invariants.CheckBoards).
        /// </summary>
        public static List<ActiveLink> ComputeActive(BoardDefinition def, IReadOnlyList<long> slots, Func<long, Element?> elementOf)
        {
            var list = new List<ActiveLink>();
            foreach (var l in def.Links)
            {
                long a = slots[l.SlotA - 1], b = slots[l.SlotB - 1];
                if (a == 0 || b == 0) continue;
                var ea = elementOf(a);
                var eb = elementOf(b);
                if (ea == null || eb == null) continue;
                var kind = StarCatalog.LinkOf(ea.Value, eb.Value);
                if (kind != null) list.Add(new ActiveLink(l, kind.Value));
            }
            return list;
        }

        /// <summary>
        /// 장마다 새 배치를 한꺼번에 넣고, 바뀐 판마다 BoardChanged → (켜진 선이 바뀌면) LinkBonusChanged → (미완성에서 완성이면) BoardCompleted를
        /// 장 순서대로 낸다. 바뀐 판이 있으면 true.
        /// </summary>
        public bool Apply(IReadOnlyDictionary<int, long[]> next, StarInventory stars, List<GameEvent> events)
        {
            var changed = false;
            foreach (var d in _defs)
            {
                if (!next.TryGetValue(d.Chapter, out var slots)) continue;
                var cur = _slots[d.Chapter];
                if (SameSlots(cur, slots)) continue;
                var wasComplete = IsComplete(d.Chapter);
                Array.Copy(slots, cur, SlotCount);
                var active = ComputeActive(d, cur, id => ElementOf(stars, id));
                var linksChanged = !SameLinks(_active[d.Chapter], active);
                _active[d.Chapter] = active;
                events.Add(new BoardChanged(d.Chapter));
                if (linksChanged) events.Add(new LinkBonusChanged(d.Chapter, active.ToArray()));
                if (!wasComplete && IsComplete(d.Chapter)) events.Add(new BoardCompleted(d.Chapter));
                changed = true;
            }
            return changed;
        }

        /// <summary>지금 배치의 사본(장 → 칸 5개). 명령이 바꿀 배치를 만들 때 쓴다.</summary>
        public Dictionary<int, long[]> CopySlots()
        {
            var d = new Dictionary<int, long[]>();
            foreach (var kv in _slots) d[kv.Key] = (long[])kv.Value.Clone();
            return d;
        }

        internal static Element? ElementOf(StarInventory stars, long instanceId)
        {
            if (!stars.TryFind(instanceId, out var s)) return null;
            var info = stars.Catalog.Find(s.StarId);
            return info?.Element;
        }

        // ---- 추천 배치 ----

        /// <summary>
        /// 별의 힘 = skill_coef_bp × (10000 + skill_step_bp × (스킬 레벨 − 1)). 표 범위(계수 ≤ 100000, 단계 ≤ 10000, 레벨 ≤ 1000)에서
        /// 1e12 이하이고, 연결 보너스(칸마다 선 4개 이하 × 10000)를 곱해도 5칸 합이 long 안에 든다.
        /// </summary>
        public long Power(OwnedStar s)
        {
            var coef = (long)_catalog.RarityOf(s.StarId).SkillCoefBp;
            var mult = 10000L + (long)_catalog.Rules.SkillStepBp * (s.SkillLevel - 1);
            return coef * mult;
        }

        /// <summary>
        /// AutoArrange의 결과 배치. 후보(다른 판에 놓이지 않은 별 + 이 판의 별)에서 ∑ 별의 힘 × (10000 + 그 별의 연결 보너스 bp)가 가장 큰 배치.
        /// 후보가 5개 이상이면 다섯 칸을 모두, 적으면 후보를 모두 놓는다. 같은 점수면 칸 1부터 차례로 star_id가 작은 별(빈 칸은 가장 뒤)인 배치.
        /// <para>
        /// 모든 배치를 훑지 않고 같은 답을 낸다 (2026-11-20, 별 60종이면 모든 배치가 약 6.5억 가지라 바꿈).
        /// 1) 속성마다 (힘 내림차순, star_id 오름차순)으로 앞 5개만 남긴다. 답에 그 밖의 별 X가 있으면, 같은 속성에서 쓰이지 않은 남긴 별 Y가
        ///    있다(칸이 5개뿐). Y의 힘이 더 크면 X 자리에 Y를 넣어 점수가 커지고(칸 배율 ≥ 10000, 힘 &gt; 0), 같으면 star_id가 더 작아 같은 점수에서 앞선다.
        ///    연결 보너스는 속성으로만 정해지므로 바꿔 넣어도 변하지 않는다.
        /// 2) 칸마다 속성(또는 빈 칸)을 정한 '모양' 6^5 = 7,776가지를 훑는다. 모양이 정해지면 칸마다 배율(10000 + 보너스)이 정해지고,
        ///    속성마다 그 속성 칸에 남긴 별 중 앞의 것들을 배율이 큰 칸부터 힘이 큰 순서로 놓는 것이 가장 크다(재배열 부등식).
        /// 3) 같은 모양 안에서 같은 점수인 배치는, 배율이 같은 칸 묶음마다 받는 힘의 모음이 정해져 있다. 칸 1부터 그 모음에 맞는 별 중
        ///    star_id가 가장 작은 별을 고르면 그 모양에서 가장 앞선 배치다. 모양마다의 답 중 (점수, 칸 1부터 star_id)로 가장 앞선 것을 고른다.
        /// 따로 쓴 '모든 경우 비교'와 같은지는 테스트가 대조한다(C4BoardTests).
        /// </para>
        /// </summary>
        public long[] BestArrangement(int chapter, IReadOnlyList<OwnedStar> candidates)
        {
            const int ElementCount = 5;
            const int EmptyCell = ElementCount;          // 모양에서 빈 칸
            const int KeepPerElement = SlotCount;
            var def = Definition(chapter);

            // 1) 속성마다 앞 5개
            var groups = new List<(OwnedStar star, long power)>[ElementCount];
            for (var e = 0; e < ElementCount; e++) groups[e] = new List<(OwnedStar, long)>();
            foreach (var c in candidates) groups[(int)_catalog.Find(c.StarId).Element].Add((c, Power(c)));
            var kept = new List<(OwnedStar star, long power, int element)>();
            for (var e = 0; e < ElementCount; e++)
            {
                groups[e].Sort((x, y) => x.power != y.power ? y.power.CompareTo(x.power) : string.CompareOrdinal(x.star.StarId, y.star.StarId));
                for (var i = 0; i < groups[e].Count && i < KeepPerElement; i++) kept.Add((groups[e][i].star, groups[e][i].power, e));
            }
            // 순위 = 남긴 별을 star_id 순서로 세운 번호. 빈 칸은 가장 뒤.
            kept.Sort((x, y) => string.CompareOrdinal(x.star.StarId, y.star.StarId));
            var n = kept.Count;
            var place = Math.Min(n, SlotCount);
            const int EmptyRank = int.MaxValue;
            // 속성마다 남긴 별의 순위를 (힘 내림차순, 순위 오름차순)으로
            var byElement = new List<int>[ElementCount];
            for (var e = 0; e < ElementCount; e++) byElement[e] = new List<int>();
            for (var r = 0; r < n; r++) byElement[kept[r].element].Add(r);
            for (var e = 0; e < ElementCount; e++) byElement[e].Sort((a, b) => kept[a].power != kept[b].power ? kept[b].power.CompareTo(kept[a].power) : a.CompareTo(b));

            var linkBp = new long[3]; // LinkKind 숫자(Same = 1, Generate = 2)로 찾는다
            linkBp[(int)LinkKind.Same] = _catalog.Rules.LinkSameBp;
            linkBp[(int)LinkKind.Generate] = _catalog.Rules.LinkGenerateBp;

            var shape = new int[SlotCount];
            var used = new int[ElementCount];
            var mult = new long[SlotCount];
            var ranks = new int[SlotCount];
            var bestRanks = new int[SlotCount];
            long bestScore = -1;
            var taken = new bool[n];
            var consumed = new bool[ElementCount][];
            for (var e = 0; e < ElementCount; e++) consumed[e] = new bool[SlotCount];

            void Evaluate()
            {
                for (var s = 0; s < SlotCount; s++) mult[s] = 10000;
                foreach (var l in def.Links)
                {
                    int a = shape[l.SlotA - 1], b = shape[l.SlotB - 1];
                    if (a == EmptyCell || b == EmptyCell) continue;
                    var kind = StarCatalog.LinkOf((Element)a, (Element)b);
                    if (kind == null) continue;
                    mult[l.SlotA - 1] += linkBp[(int)kind.Value];
                    mult[l.SlotB - 1] += linkBp[(int)kind.Value];
                }
                // 점수: 속성마다 그 속성 칸을 배율 내림차순으로, 앞의 별을 힘 내림차순으로 짝짓는다
                long score = 0;
                var slotsOf = new List<int>[ElementCount];
                for (var e = 0; e < ElementCount; e++)
                {
                    if (used[e] == 0) continue;
                    var slots = new List<int>();
                    for (var s = 0; s < SlotCount; s++) if (shape[s] == e) slots.Add(s);
                    slots.Sort((x, y) => mult[y].CompareTo(mult[x]));
                    for (var i = 0; i < slots.Count; i++) score += kept[byElement[e][i]].power * mult[slots[i]];
                    slotsOf[e] = slots;
                }
                if (score < bestScore) return;
                // 같은 모양에서 가장 앞선 배치: 칸 1부터, 그 칸의 배율 묶음이 받을 힘에 맞는 별 중 순위가 가장 작은 별
                // 짝지은 자리 i(배율 내림차순 i번째 칸이 받는 힘)를 묶음에서 하나씩 지운다. 같은 힘의 별끼리는 바꿔도 점수가 같다.
                Array.Clear(taken, 0, n);
                for (var e = 0; e < ElementCount; e++) if (used[e] > 0) Array.Clear(consumed[e], 0, SlotCount);
                for (var s = 0; s < SlotCount; s++)
                {
                    var e = shape[s];
                    if (e == EmptyCell) { ranks[s] = EmptyRank; continue; }
                    var slots = slotsOf[e];
                    int choice = -1, choicePos = -1;
                    for (var i = 0; i < slots.Count; i++)
                    {
                        if (consumed[e][i] || mult[slots[i]] != mult[s]) continue;
                        var want = kept[byElement[e][i]].power; // 이 배율 묶음이 아직 받을 힘 하나
                        for (var j = 0; j < slots.Count; j++)
                        {
                            var q = byElement[e][j];
                            if (taken[q] || kept[q].power != want) continue;
                            if (choice < 0 || q < choice) { choice = q; choicePos = i; }
                        }
                    }
                    taken[choice] = true;
                    consumed[e][choicePos] = true;
                    ranks[s] = choice;
                }
                if (score > bestScore || Earlier(ranks, bestRanks))
                {
                    bestScore = score;
                    Array.Copy(ranks, bestRanks, SlotCount);
                }
            }

            void Fill(int slot, int placed)
            {
                if (slot == SlotCount)
                {
                    if (placed == place) Evaluate();
                    return;
                }
                if (place - placed > SlotCount - slot) return; // 남은 칸에 다 놓을 수 없음
                for (var e = 0; e < ElementCount; e++)
                {
                    if (placed == place || used[e] == byElement[e].Count) continue;
                    used[e]++;
                    shape[slot] = e;
                    Fill(slot + 1, placed + 1);
                    used[e]--;
                }
                shape[slot] = EmptyCell;
                Fill(slot + 1, placed);
            }

            Fill(0, 0);
            var result = new long[SlotCount];
            for (var s = 0; s < SlotCount; s++) result[s] = bestRanks[s] == EmptyRank ? 0 : kept[bestRanks[s]].star.InstanceId;
            return result;
        }

        static bool Earlier(int[] a, int[] b)
        {
            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return a[i] < b[i];
            }
            return false;
        }

        // ---- 저장 ----

        /// <summary>
        /// 저장한 배치로. 키는 판의 장과 정확히 같고, 칸마다 0 또는 가진 별, 한 별은 한 칸에만, 닫힌 판(장 &gt; currentChapter)은 비어 있어야 한다.
        /// 틀리면 errors에 남기고 false(상태는 그대로).
        /// </summary>
        public bool TryRestore(IReadOnlyDictionary<int, long[]> saved, StarInventory stars, int currentChapter, List<string> errors)
        {
            var ok = true;
            foreach (var d in _defs)
            {
                if (!saved.ContainsKey(d.Chapter)) { errors.Add("boards: " + d.Chapter + "장 판이 없음"); ok = false; }
            }
            var seen = new HashSet<long>();
            foreach (var kv in saved)
            {
                if (!_slots.ContainsKey(kv.Key)) { errors.Add("boards: 지금 빌드에 없는 판 " + kv.Key + "장"); ok = false; continue; }
                if (kv.Value == null || kv.Value.Length != SlotCount) { errors.Add("boards." + kv.Key + ": 칸이 " + SlotCount + "개가 아님"); ok = false; continue; }
                for (var i = 0; i < SlotCount; i++)
                {
                    var id = kv.Value[i];
                    if (id == 0) continue;
                    if (!stars.TryFind(id, out _)) { errors.Add("boards." + kv.Key + " 칸 " + (i + 1) + ": 가지지 않은 별 " + id); ok = false; }
                    if (!seen.Add(id)) { errors.Add("boards: 별 " + id + "가 두 칸에 있음"); ok = false; }
                    if (!IsOpen(kv.Key, currentChapter)) { errors.Add("boards." + kv.Key + ": 닫힌 판에 별이 있음"); ok = false; }
                }
            }
            if (!ok) return false;
            foreach (var d in _defs)
            {
                Array.Copy(saved[d.Chapter], _slots[d.Chapter], SlotCount);
                _active[d.Chapter] = ComputeActive(d, _slots[d.Chapter], id => ElementOf(stars, id));
            }
            return true;
        }

        /// <summary>테스트 전용: 규칙을 어긴 배치를 만든다(위반 검사 확인용). 켜진 선은 다시 계산하지 않는다.</summary>
        internal void CorruptSlotForTest(int chapter, int slot, long instanceId) => _slots[chapter][slot - 1] = instanceId;

        static bool SameSlots(long[] a, long[] b)
        {
            for (var i = 0; i < SlotCount; i++) if (a[i] != b[i]) return false;
            return true;
        }

        internal static bool SameLinks(IReadOnlyList<ActiveLink> a, IReadOnlyList<ActiveLink> b)
        {
            if (a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++) if (!a[i].Equals(b[i])) return false;
            return true;
        }

        public bool SameState(StarBoards o)
        {
            if (o == null || o._slots.Count != _slots.Count) return false;
            foreach (var kv in _slots)
            {
                if (!o._slots.TryGetValue(kv.Key, out var other) || !SameSlots(kv.Value, other)) return false;
                if (!SameLinks(_active[kv.Key], o._active[kv.Key])) return false;
            }
            return true;
        }

        public StarBoards Clone()
        {
            var c = new StarBoards(_catalog, _defs);
            foreach (var kv in _slots)
            {
                Array.Copy(kv.Value, c._slots[kv.Key], SlotCount);
                c._active[kv.Key] = new List<ActiveLink>(_active[kv.Key]);
            }
            return c;
        }
    }
}
