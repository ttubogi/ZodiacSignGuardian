# 별자리 수호자 키우기 (ZodiacSignGuardian)

2D 도트 방치형 RPG · Unity 6.3 LTS · Android(Google Play).
작업 규칙은 [AGENTS.md](AGENTS.md) 한 곳에 있다.

## 폴더

| 폴더 | 내용 | 참조 | 담당 |
|---|---|---|---|
| `Assets/Scripts/Data` | 표 정의·검사 | 없음 | Claude |
| `Assets/Scripts/Core` | 판정 | Data | Claude |
| `Assets/Tests/Core` | 판정·표 테스트 (Unity·dotnet 양쪽에서 실행) | Core, Data | Claude |
| `Assets/Scripts/Services` | 저장·서버 시각 호출·광고·표 읽기·앱 일시정지 | Core, Data | Codex |
| `Assets/Scripts/View` | 화면 | Core | Codex |
| `Tools/DataGen` | 스테이지·몬스터·보상 표 생성 (Core 참조 안 함) | — | Claude |
| `Cloud` | Cloud Code (서버 시각) | — | Claude |
| `dotnet/` | Unity 밖 빌드·테스트 프로젝트 (소스는 `Assets/`의 파일을 그대로 컴파일) | — | Claude |

참조 방향은 asmdef로 막고, `dotnet/Structure.Tests`가 설계서 4절 표와 대조한다.
Core·Data는 `noEngineReferences: true`라 Unity API를 쓰면 컴파일되지 않는다.

## Unity

- 에디터: **Unity 6.3 LTS** (`ProjectSettings/ProjectVersion.txt`, 지금 6000.3.28f1). 다른 버전으로 열면 구조 테스트가 실패한다.
- 테스트: Window → General → Test Runner → EditMode → Run All. `com.unity.test-framework`(1.6.0, 에디터 내장)가 `Packages/manifest.json`에 있어야 한다.
- `Assets` 아래 파일·폴더를 더하거나 지우면 `.meta`도 같이 커밋한다. 빠지면 구조 테스트가 실패한다.
- Windows 11의 스마트 앱 컨트롤이 켜져 있으면 Unity 빌드 도구(Bee.DotNet.dll)가 막혀 컴파일이 안 된다(오류 0x800711C7).

## 표 (운영 숫자)

| 폴더 | 내용 |
|---|---|
| `Tables/csv` | 원본. 사람이 고치는 곳. 표 12개 = 날짜별 규칙 7개 + 정의 표 5개 (설계서 v0.7 2절) |
| `Assets/Resources/Tables` | 변환 결과 JSON. 손으로 고치지 않는다. Android 빌드에 들어가도록 Assets 안에 둔다 (Services가 `Resources`로 읽음, Codex 확인 대기) |

- CSV는 **UTF-8**만 받는다. Excel에서는 "CSV UTF-8(쉼표로 분리)"로 저장한다. (그냥 "CSV"는 CP949라 거절된다)
- 고친 뒤 변환: `dotnet run --project dotnet/TableBuild` — 검사 10개를 모두 통과해야 JSON을 쓰고, 하나라도 걸리면 아무것도 쓰지 않는다. 새 JSON·폴더의 `.meta`는 없을 때만 만든다.
- 확인만: `dotnet run --project dotnet/TableBuild -- check` — 저장소의 JSON이 CSV와 같은지 본다. (`dotnet test`에도 같은 검사가 있다)
- 문제는 `[검사3] attendance.csv 15줄 amount: ...`처럼 종류·파일·줄·열과 함께 나온다. `형식`은 검사 10개 밖의 모양 문제(머리줄, 칸 수, 값 모양 등)다.
- 정의 표의 값은 아직 임시다. 실제 숫자는 '밸런스 기초 문서'에서 정한다.

## Unity 밖 테스트

.NET 8 SDK가 필요하다.

```
cd dotnet
dotnet test
```

- `Core.Tests`: `Assets/Tests/Core`의 테스트. Unity Test Framework 내장 NUnit과 함께 쓰기 위해 NUnit 3.x, `Assert.That` 형태만 쓴다.
- `Structure.Tests`: asmdef 참조 방향, csproj ↔ asmdef 이름, 규칙 파일(AGENTS.md·CLAUDE.md) 검사. dotnet에서만 돈다.

테스트를 모두 통과해도 재미·미술 승인은 아니다.
