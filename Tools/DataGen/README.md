# DataGen

스테이지 표(`Tables/csv/stages.csv`)를 만드는 도구 (설계서 v0.12 4절, 담당 Claude).

- 입력: `Tools/DataGen/stage_params.csv` (한 행). 값은 [밸런스 기초 문서](https://claude.ai/artifact/DUBAP4vapqn1pTEiUBHZqU) 4절의 공식이다.
  비율은 소수가 아니라 정수 분수로 쓴다 (예: 1.313 → `hp_ratio_num` 1313, `hp_ratio_den` 1000).
- 만들기: `dotnet run --project dotnet/DataGen` → `Tables/csv/stages.csv`를 쓴다. 그다음 `dotnet run --project dotnet/TableBuild`로 검사 1~12를 거쳐 JSON을 만든다.
- 확인만: `dotnet run --project dotnet/DataGen -- check` (저장소 파일과 글자가 다르면 1로 끝남). 구조 테스트(DataGenTests)도 같은 것을 본다.
- **stages.csv를 손으로 고치지 않는다.** 바꾸려면 입력 표를 고친 뒤 다시 만든다.
- **Core·Data를 참조하지 않는다.** 표를 만드는 쪽과 검사하는 쪽이 같은 실수를 함께 놓치지 않게 하기 위해서다. 그래서 큰 수 계산도
  여기서 따로 한다: 정수 분수(BigInteger)로 정확히 계산한 뒤 유효 숫자 15자리로 한 번만 반올림(절반은 올림)해 "가수e지수"로 쓴다.

| 출력 열 | 공식 (g = 전체 스테이지 번호, 1부터) |
| --- | --- |
| monster_hp | hp_base × (hp_ratio_num / hp_ratio_den)^(g − 1) |
| dust_per_kill | dust_base × (dust_ratio_num / dust_ratio_den)^(g − 1) |
| offline_dust_per_min | offline_kills_per_min × dust_per_kill |
| boss_hp (장 마지막 행만) | boss_hp_mul × 그 스테이지의 monster_hp |
| 그 밖 | 입력 표의 값 그대로 (보스 칸은 장 마지막 행에만) |

`Tools/Balance/balance_model.py`는 이 공식으로 목표 일정(바쁜 플레이어 3장 보스 약 16일)을 어림한 모델이다(판정 코드가 아님).
