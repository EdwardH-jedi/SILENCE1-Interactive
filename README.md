# SILENCE1 Interactive — 하루의 방문

방음 슬라이딩 창호 **SILENCE1**을 하루의 시간대와 주변 환경 속에서 들어 보는 Unity 인터랙티브입니다.
같은 창 앞을 지나가는 소리를 **기존 창호**와 **SILENCE1**로 번갈아 들으며 차이를 비교합니다.

[![하루의 방문 화면: 철도 주변, 기존 창호와 SILENCE1 비교](docs/visit.jpg)](https://github.com/EdwardH-jedi/SILENCE1-Interactive/releases/latest)

## ⬇ 실행하기 (Windows)

1. [Releases](https://github.com/EdwardH-jedi/SILENCE1-Interactive/releases/latest)에서 **`SILENCE1-Visit-Windows.zip`**(약 160MB)을 받습니다.
2. 압축을 모두 푼 뒤 **`SILENCE1 Visit.exe`**를 실행합니다. 압축을 푼 폴더 안의 파일을 함께 두어야 실행됩니다.
3. 서명되지 않은 실행 파일이라 "Windows의 PC 보호" 창이 뜰 수 있습니다. **추가 정보 → 실행**을 누르세요.

Windows 10/11 64비트에서 실행되고, 헤드폰이나 스피커로 들으면 차이가 잘 들립니다.

## 장면

- 시간대: 02:00 배달 스쿠터 · 06:30 첫 버스 · 14:00 초등학생 하굣길 · 16:40 중학생 하굣길 · 19:00 직장인 귀갓길 · 23:00 가게 문 닫는 시간
- 주변 환경: 고가도로 앞 · 철도 주변 · 활주로 주변

화면의 **기존 창호 / SILENCE1** 비교는 연출된 음향입니다. 측정값을 재생하는 것이 아닙니다.

## 조작법

| 입력 | 동작 |
|---|---|
| 시계 다이얼 드래그 · 위쪽 탭 클릭 | 시간대 · 주변 환경 고르기 |
| `A` / `S` | 기존 창호 / SILENCE1로 바꿔 듣기 |
| `R` | 같은 순간 다시 듣기 |
| `PageUp` / `PageDown` | 이전 · 다음 시간대 |
| `M` | 소리 끄기 · 켜기 |
| `C` | 자막 끄기 · 켜기 |
| `Tab` / `Shift+Tab` | 버튼 사이 이동 |

## Unity에서 열기

- **Unity 6000.6.10f1** (URP), **Windows Build Support** 모듈. Unity Hub에서 이 폴더를 *Add project from disk*로 추가합니다.
- 씬: `Assets/Scenes/Silence1Visit.unity`
- 상단 메뉴 **SILENCE1 → Create visit scene**으로 씬을 다시 구성하고, **SILENCE1 → Build Windows visit**으로 `Build/Visit/SILENCE1 Visit.exe`를 만듭니다.

```
Assets/Silence1/Visit/
├─ Scripts/   NightVisit.cs, TimeDial.cs — 시간대 전환 · 음향 비교 · 자막
├─ Editor/    VisitBuilder.cs — 시간대별 장면 · 자막 타이밍 · 빌드
├─ Video/     시간대 · 주변 환경 영상
├─ Audio/     바깥 · 실내 · 구조음 레이어
├─ Stills/    각 영상의 첫 프레임
└─ Fonts/     IBM Plex Sans KR
```
