"""밸런스 기초 문서 v0.1 계산 모델.

판정 코드(Core)와 따로 둔 어림 모델이다. 목적은 '표의 숫자로 목표 일정이 나오는가'를 보는 것.
- 전투: 한 번에 몬스터 하나, 처치 뒤 0.5초 뒤 다음 몬스터. 기본 공격 1초에 1번.
- 처치 시간은 0.1초 칸 단위로 올림.
- 스킬 피해는 '초당 평균'으로만 본다(쿨타임 세부는 C5).
"""
import math, random, json, sys

P = dict(
    stages_per_chapter=20, chapters=3, kills_per_stage=15, spawn_gap=0.5, stage_time_limit=60.0,
    # 몬스터·보상 (스테이지 표, DataGen)
    hp0=20.0, hp_ratio=1.313,          # 몬스터 체력 = hp0 × hp_ratio^(g-1)
    dust0=2.0, dust_ratio=1.16,        # 처치당 별가루 = dust0 × dust_ratio^(g-1)
    offline_kpm=12,                    # 분당 오프라인 = offline_kpm × 처치당 별가루
    boss_hp_mul=6.0, boss_time=30.0,  # 보스 체력 = 그 장 마지막 스테이지 몬스터 × boss_hp_mul
    # 능력치 (C5)
    atk0=10.0, atk_ratio=1.05, atk_cost0=10.0, atk_cost_ratio=1.09,
    crit_step=0.005, crit_max_lv=100, crit_cost0=50.0, crit_cost_ratio=1.10,
    cdmg0=1.5, cdmg_step=0.02, cdmg_max_lv=100, cdmg_cost0=50.0, cdmg_cost_ratio=1.09,
    # 별
    skill_mul={'common': 0.3, 'rare': 0.5, 'hero': 0.65, 'legend': 0.8},
    rarities=('common', 'rare', 'hero', 'legend'),
    per_rarity=3,                      # 속성마다 희귀도별 별 수 (2026-11-20: 60종 = 5속성 × 4희귀도 × 3)
    # own_bonus: 별 60종이면 0.02(등급 합 × 2%)로는 3장 보스가 중간값 10일째(목표 16일). 0.008이면 16일 (2026-11-20 계산, 값은 C5에서 정함)
    grade_step=0.0, own_bonus=0.02, board_complete=0.10, link_avg=0.35,
    rate={'common': 7000, 'rare': 2000, 'hero': 900, 'legend': 100},
    draw_cost=100, draw10_cost=900, free_per_day=1,
    dup_shard={'common': 5, 'rare': 20, 'hero': 50, 'legend': 100},
    grade_cost={'common': [10, 20, 40, 80], 'rare': [20, 40, 80, 160], 'hero': [30, 60, 120, 240], 'legend': [40, 80, 160, 320]},
    # 은하수정
    mission_gems=1750, attend_gems=400, attend_star_days={7: 'rare', 14: 'rare', 21: 'rare', 28: 'legend'},
    boss_first_gems=2000, stage_first_gems=300,
    # 스킬 레벨 (B3: 등급과 따로)
    skill_lv_max=30, skill_lv_step=0.10, skill_cost_base={'common': 2, 'rare': 5, 'hero': 7, 'legend': 10},
    # 지팡이 (C5 가안)
    first_session_min=15,
    pouch_kills=600, staff_grade_p=[5000, 3000, 1500, 400, 100], staff_step=0.5,
)

PLAYERS = {
    'busy': dict(name='바쁜 플레이어', sessions=[(8*60, 3), (12*60+30, 5), (19*60, 5), (23*60, 3)]),
    'idle': dict(name='켜두는 플레이어', sessions=[(8*60, 10), (12*60, 60), (18*60, 120), (22*60, 30)]),
}


def total_stars():
    return 5 * P['per_rarity'] * len(P['rarities'])


def stage_hp(g):
    return P['hp0'] * P['hp_ratio'] ** (g - 1)


def stage_dust(g):
    return P['dust0'] * P['dust_ratio'] ** (g - 1)


def boss_hp(ch):
    return stage_hp(ch * P['stages_per_chapter']) * P['boss_hp_mul']


class Player:
    def __init__(self, seed):
        self.rng = random.Random(seed)
        self.dust = 0.0
        self.gems = 0
        self.shards = 0
        self.atk_lv = self.crit_lv = self.cdmg_lv = 0
        self.g = 1                # 현재 전체 스테이지 번호
        self.chapter_cleared = 0
        self.stage_kills = 0
        self.stage_time = 0.0
        self.stars = {}           # star_id -> (rarity, grade)
        self.skill = {}           # star_id -> skill level
        self.best_stage = 1
        self.kills_total = 0
        self.pouch = 0
        self.staff = 0            # 0 = 없음, 1~5
        self.log = []
        self.complete_day = None

    # ---------- 전투력 ----------
    def atk(self):
        return P['atk0'] * P['atk_ratio'] ** self.atk_lv

    def dps(self, atk_lv=None, crit_lv=None, cdmg_lv=None):
        a = P['atk0'] * P['atk_ratio'] ** (self.atk_lv if atk_lv is None else atk_lv)
        cr = P['crit_step'] * (self.crit_lv if crit_lv is None else crit_lv)
        cd = P['cdmg0'] + P['cdmg_step'] * (self.cdmg_lv if cdmg_lv is None else cdmg_lv)
        key = (len(self.stars), sum(g for _, g in self.stars.values()), sum(self.skill.values()), self.staff, self.chapter_cleared)
        if getattr(self, '_k', None) != key:
            own = 1 + P['own_bonus'] * sum(gr for _, gr in self.stars.values())
            staff = 1 + P['staff_step'] * self.staff if self.staff else 1
            boards_open = min(self.chapter_cleared + 1, P['chapters'])
            pw = lambda st: P['skill_mul'][st[0]] * (1 + P['skill_lv_step'] * (self.skill.get(st, 1) - 1))
            placed = sorted(self.stars, key=lambda st: -pw(st))[:5 * boards_open]
            skill = sum(pw(st) * (1 + P['link_avg']) for st in placed)
            complete = len(placed) // 5
            self._m = own * staff * (1 + P['board_complete'] * complete) * (1 + skill)
            self._k = key
        return a * self._m * (1 + cr * (cd - 1))

    # ---------- 성장 ----------
    def spend(self):
        while True:
            base = self.dps()
            opts = []
            c = P['atk_cost0'] * P['atk_cost_ratio'] ** self.atk_lv
            opts.append(((self.dps(atk_lv=self.atk_lv + 1) - base) / c, c, 'atk'))
            if self.crit_lv < P['crit_max_lv']:
                c = P['crit_cost0'] * P['crit_cost_ratio'] ** self.crit_lv
                opts.append(((self.dps(crit_lv=self.crit_lv + 1) - base) / c, c, 'crit'))
            if self.cdmg_lv < P['cdmg_max_lv']:
                c = P['cdmg_cost0'] * P['cdmg_cost_ratio'] ** self.cdmg_lv
                opts.append(((self.dps(cdmg_lv=self.cdmg_lv + 1) - base) / c, c, 'cdmg'))
            opts.sort(reverse=True)
            _, c, k = opts[0]
            if c > self.dust:
                return
            self.dust -= c
            setattr(self, k + '_lv', getattr(self, k + '_lv') + 1)

    def draw(self, n):
        for _ in range(n):
            x = self.rng.randrange(10000)
            acc = 0
            for r in P['rarities']:
                acc += P['rate'][r]
                if x < acc:
                    break
            self.give_star(r, self.rng.randrange(5 * P['per_rarity']))

    def give_star(self, rarity, idx):
        sid = (rarity, idx)
        if sid in self.stars:
            self.shards += P['dup_shard'][rarity]
        else:
            self.stars[sid] = (rarity, 1)

    def upgrade_stars(self):
        while True:
            base = self.dps(); best = None
            for sid, (r, gr) in self.stars.items():
                if gr < 5:
                    c = P['grade_cost'][r][gr - 1]
                    self.stars[sid] = (r, gr + 1); self._k = None
                    g = (self.dps() - base) / c
                    self.stars[sid] = (r, gr); self._k = None
                    if c <= self.shards and (best is None or g > best[0]): best = (g, 'grade', sid, c)
                lv = self.skill.get(sid, 1)
                if lv < P['skill_lv_max']:
                    c = P['skill_cost_base'][r] * lv
                    self.skill[sid] = lv + 1; self._k = None
                    g = (self.dps() - base) / c
                    self.skill[sid] = lv; self._k = None
                    if c <= self.shards and (best is None or g > best[0]): best = (g, 'skill', sid, c)
            if not best:
                return
            _, kind, sid, c = best
            self.shards -= c
            if kind == 'grade':
                r, gr = self.stars[sid]; self.stars[sid] = (r, gr + 1)
            else:
                self.skill[sid] = self.skill.get(sid, 1) + 1
            self._k = None

    def upgrade_stars_old(self):
        changed = True
        while changed:
            changed = False
            best = None
            for sid, (r, gr) in self.stars.items():
                if gr >= 5:
                    continue
                c = P['grade_cost'][r][gr - 1]
                gain = P['skill_mul'][r] * P['grade_step'] / c
                if c <= self.shards and (best is None or gain > best[0]):
                    best = (gain, sid, c)
            if best:
                _, sid, c = best
                r, gr = self.stars[sid]
                self.stars[sid] = (r, gr + 1)
                self.shards -= c
                changed = True

    def daily(self, day):
        self.gems += P['mission_gems']
        att = (day - 1) % 28 + 1
        if att in P['attend_star_days']:
            self.give_star(P['attend_star_days'][att], self.rng.randrange(5 * P['per_rarity']))
        else:
            self.gems += P['attend_gems']
        self.draw(P['free_per_day'])
        while self.gems >= P['draw10_cost']:
            self.gems -= P['draw10_cost']
            self.draw(10)
        self.upgrade_stars()

    # ---------- 사냥 ----------
    def hunt(self, seconds, t_label):
        t = 0.0
        while t < seconds:
            last = self.g % P['stages_per_chapter'] == 0
            ch = (self.g - 1) // P['stages_per_chapter'] + 1
            d = self.dps()
            if last and self.stage_kills >= P['kills_per_stage'] and ch > self.chapter_cleared:
                # 보스 도전: 제한 시간 안에 잡을 수 있으면 클리어
                if d * P['boss_time'] >= boss_hp(ch):
                    self.chapter_cleared = ch
                    self.gems += P['boss_first_gems']
                    self.log.append((t_label, f'{ch}장 보스'))
                    if ch < P['chapters']:
                        self.g += 1
                        self.stage_kills = 0
                    continue
            ttk = math.ceil(stage_hp(self.g) / d * 10) / 10
            step = max(ttk, 0.1) + P['spawn_gap']
            if t + step > seconds:
                break
            t += step
            self.dust += stage_dust(self.g)
            self.kills_total += 1
            self.pouch += 1
            if self.pouch >= P['pouch_kills']:
                self.pouch = 0
                grades = [self.pick_staff_grade() for _ in range(3)]
                self.staff = max(self.staff, max(grades))
            self.stage_kills += 1
            self.stage_time += step
            if self.stage_kills >= P['kills_per_stage'] and not last:
                if self.stage_time <= P['stage_time_limit']:
                    self.g += 1
                    if self.g > self.best_stage:
                        self.best_stage = self.g
                        self.gems += P['stage_first_gems']
                self.stage_kills = 0
                self.stage_time = 0.0
            elif self.stage_kills >= P['kills_per_stage'] and last:
                # 마지막 스테이지: 제한 시간 안이면 보스 도전 가능 표시, 아니면 다시
                if self.stage_time > P['stage_time_limit']:
                    self.stage_kills = 0
                self.stage_time = 0.0 if self.stage_kills == 0 else self.stage_time
            if self.kills_total % 50 == 0:
                self.spend()

    def pick_staff_grade(self):
        x = self.rng.randrange(10000)
        acc = 0
        for i, p in enumerate(P['staff_grade_p']):
            acc += p
            if x < acc:
                return i + 1
        return 5


def run(kind, days=30, seed=1, ad_offline=False):
    pl = Player(seed)
    sessions = PLAYERS[kind]['sessions']
    last_leave = None
    rows = []
    for day in range(1, days + 1):
        for i, (start, minutes) in enumerate(sessions):
            if day == 1 and i == 0:
                minutes = max(minutes, P['first_session_min'])
            now = (day - 1) * 1440 + start
            if last_leave is not None:
                away = now - last_leave
                mins = min(away, 720)
                if mins >= 1:
                    pl.dust += mins * P['offline_kpm'] * stage_dust(pl.g) * (2 if ad_offline else 1)
            if i == 0:
                pl.daily(day)
            pl.spend()
            pl.hunt(minutes * 60, (day, i))
            pl.spend()
            last_leave = now + minutes
        if pl.complete_day is None and len(pl.stars) == total_stars():
            pl.complete_day = day
        rows.append(dict(day=day, stage=pl.g, cleared=pl.chapter_cleared, atk_lv=pl.atk_lv,
                         stars=len(pl.stars), dps=pl.dps(), staff=pl.staff,
                         grades=sum(g for _, g in pl.stars.values())))
    return pl, rows


def summary(kind, seeds=range(1, 21), ad=False):
    clears = {1: [], 2: [], 3: []}
    finals = []
    for s in seeds:
        pl, rows = run(kind, seed=s, ad_offline=ad)
        for (day, sess), what in pl.log:
            clears[int(what[0])].append(day + sess / 4)
        finals.append(rows[-1])
    out = {}
    for ch, v in clears.items():
        v.sort()
        out[ch] = (len(v), v[len(v) // 2] if v else None, v[0] if v else None, v[-1] if v else None)
    return out, finals


if __name__ == '__main__':
    for kind in PLAYERS:
        for ad in (False, True):
            out, finals = summary(kind, ad=ad)
            print(PLAYERS[kind]['name'], '광고2배' if ad else '광고없음')
            for ch, (n, med, lo, hi) in out.items():
                print(f'  {ch}장 보스: {n}/20명, 중앙 {med}, 범위 {lo}~{hi}일')
            f = finals[0]
            print('  30일째 예:', {k: (round(v, 1) if isinstance(v, float) else v) for k, v in f.items()})
