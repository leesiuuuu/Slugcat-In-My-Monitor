# SlugcatInMyMonitor

<p align="center">
  <a href="README.md">English</a> | <strong>한국어</strong>
</p>

<p align="center">
  <img src="docs/media/icon.png" alt="SlugcatInMyMonitor 아이콘" width="96">
</p>

**Rain World의 Slugcat이 Windows 바탕화면을 돌아다니는 데스크톱 펫입니다.**
Slugcat은 모니터와 실제 창의 경계를 바닥이나 벽처럼 이용하고, 걷기·점프·낙하·벽
오르기·휴식 같은 행동을 스스로 선택합니다. 마우스로 잡아 옮기거나 던질 수 있으며,
최대 8마리를 동시에 실행할 수 있습니다.

![여러 Slugcat이 바탕화면을 돌아다니는 모습](docs/media/readme/example-preview.gif)

> [!IMPORTANT]
> **Rain World를 구매하여 PC에 설치한 사용자만 정상적으로 사용할 수 있습니다.**
> 이 프로그램은 Slugcat 기본 외형과 스킨을 자체적으로 제공하지 않고, 사용자의 로컬
> Rain World 설치 폴더에서 원본 atlas와 설치된 모드 정보를 읽습니다. 유효한
> `RainWorld.exe`와 `RainWorld_Data` 폴더를 찾지 못하면 프로그램이 시작되지 않으며,
> 게임 파일이 없거나 호환되지 않으면 외형이 정상적으로 표시되지 않을 수 있습니다.

Rain World, Steam 또는 Unity를 동시에 실행할 필요는 없습니다. 게임 실행 파일이나 DLL을
불러오는 방식이 아니라 필요한 자산을 로컬 설치본에서 읽기 전용으로 사용하며, Rain World
및 커뮤니티 스킨 자산은 저장소와 배포 파일에 포함하지 않습니다.

## 주요 기능

- **바탕화면 지형:** 창과 모니터를 이용하는 자율 이동, 점프, 벽 오르기 및 휴식
- **여러 Slugcat:** 생성·선택·삭제와 캐릭터별 이동 능력 및 특수 능력
- **직접 상호작용:** 마우스로 잡기와 던지기
- **먹이 주기:** 트레이에서 푸른 열매 또는 알벌레 알을 주고, 포만감에 따른 섭취·거절 관찰
- **원본 외형:** Rain World 원본 atlas 기반 그래픽
- **DMS 스킨:** 파츠별 스킨 선택 및 색상 편집(실험적)
- **GPU 렌더링:** DirectComposition 기반 다중 표면 합성과 연기·폭발 이펙트
- **안정적인 움직임:** 모니터 주사율에 맞춘 보간 렌더링과 40Hz 고정 시뮬레이션

런타임 사운드 기능은 성능과 안정성을 위해 현재 제공하지 않습니다.

## 지원 Slugcat

아래 캐릭터는 단순 색상 스킨이 아니라 각각의 이동 능력치와 현재 구현된 특수 능력을
사용합니다. Rain World의 방, 생물, 아이템 시스템이 필요한 상호작용은 데스크톱 환경에
맞게 축소되거나 제외됩니다.

| Slugcat | 실행 이름 | 현재 구현된 특징 |
|---|---|---|
| Survivor | `white` | 표준 이동 및 기본 능력치 |
| Monk | `yellow` | 가벼운 몸과 완만한 이동 특성 |
| Hunter | `red` | 빠른 이동과 높은 신체 능력치 |
| Gourmand | `gourmand` | 무게·지구력, 구르기와 배밀이 |
| Artificer | `artificer` | 폭발 도약, 충격파와 자폭 이펙트 |
| SpearMaster | `spearmaster` | 바늘 창 생성 및 투척 |
| Rivulet | `rivulet` | 빠른 달리기·점프·기어오르기 |
| Saint | `saint` | 혀와 로프 이동 |

세부 구현 범위와 원작과의 차이는 [Slugcat 능력 대응 문서](docs/SlugcatAbilityParity.md)를
참고하세요.

## 요구 사항

### 필수

- **64비트 Windows 10 또는 Windows 11**
- **Microsoft .NET Framework 4.8 Runtime**
- **PC에 설치된 정품 Rain World**
  - 설치 폴더에 `RainWorld.exe`와 `RainWorld_Data`가 있어야 합니다.
  - 프로그램이 원본 Slugcat atlas를 읽을 수 있도록 게임 파일이 온전해야 합니다.
- **Direct3D 11을 지원하는 그래픽 장치와 드라이버**
  - DirectComposition과 DirectX 구성 요소는 Windows 10/11에 포함되므로 일반적으로
    별도 프로그램을 설치할 필요가 없습니다.

Visual Studio, .NET SDK, Unity, BepInEx는 배포판 실행에 필요하지 않습니다. 네이티브
렌더러는 정적 C++ 런타임으로 빌드되므로 Visual C++ 재배포 패키지도 별도로 요구하지
않습니다.

### 외부 DMS 스킨 사용 시 추가 요구 사항

- Rain World에 설치되고 Remix 메뉴에서 활성화된 **Dress My Slugcat(DMS)** 모드
- DMS 형식의 스킨 모드
- Rain World의 Remix 메뉴에서 활성화된 해당 DMS 스킨 모드
- Steam Workshop에서 스킨을 받을 경우 **Steam 클라이언트**

Steam은 Workshop 설치 위치를 자동으로 찾을 때만 필요합니다. Rain World와 모드를
직접 설치하고 경로를 지정한 경우, 이 프로그램을 실행하는 동안 Steam을 켜둘 필요는
없습니다. 스킨 활성화 상태는 Rain World의 설정 파일을 기준으로 판단하므로, DMS와
스킨 모드를 설치·활성화한 뒤 Rain World를 한 번 정상 종료하는 것을 권장합니다.
Downpour DLC는 프로그램 자체의 필수 구성 요소는 아니지만, 선택한 스킨 모드가
Downpour 자산을 요구한다면 해당 DLC도 설치해야 합니다.

## 설치 및 실행

1. **[GitHub Releases](https://github.com/leesiuuuu/Slugcat-In-My-Monitor/releases)에서 다운로드:**
   최신 `win-x64.zip` 파일을 내려받습니다.
2. **압축 해제:** ZIP 파일의 내용을 한 폴더에 모두 압축 해제합니다.
3. **프로그램 실행:** `SlugcatInMyMonitor.exe`를 실행합니다.
4. **Rain World 경로 선택:** 설치 경로를 자동으로 찾지 못하면 표시되는 폴더 선택 창에서
   `RainWorld.exe`가 있는 폴더를 선택합니다.

실행 파일과 함께 배포되는 `SlugcatInMyMonitor.DirectComposition.dll`을 삭제하거나
다른 위치로 옮기면 렌더링이 시작되지 않습니다.

명령줄에서 시작 캐릭터나 Rain World 설치 경로를 지정할 수도 있습니다.

```powershell
# Gourmand로 실행
.\SlugcatInMyMonitor.exe --slugcat gourmand

# 선택 가능: white, yellow, red, gourmand, artificer,
#            spearmaster, rivulet, saint

# Rain World 설치 경로 직접 지정
.\SlugcatInMyMonitor.exe `
  --rain-world "C:\Program Files (x86)\Steam\steamapps\common\Rain World"
```

한 번 확인된 Rain World 경로는
`%LOCALAPPDATA%\SlugcatInMyMonitor\rain-world-path.txt`에 저장됩니다.

## 설정과 조작

![SlugcatInMyMonitor 설정 패널](docs/media/readme/settingPanel-rework.png)

시스템 트레이의 Slugcat 아이콘을 왼쪽 클릭하면 설정 패널이 열립니다.

- Slugcat 추가, 선택 및 삭제
- 캐릭터와 능력 변경
- UI 언어 선택(한국어/English, 재시작 후 적용)
- 스킨 편집기 열기
- 디버그 표시와 전체 일시 정지
- Workshop 모드 새로 고침
- 렌더링 재시도 및 프로그램 종료

기본 조작은 다음과 같습니다.

- **Slugcat 위에서 마우스 왼쪽 버튼:** 잡기
- **잡은 채 이동한 후 놓기:** 던지기
- **잡고 있는 동안:** 바탕화면 선택 영역 등 다른 마우스 드래그 입력 차단
- **Slugcat 클릭 또는 잡기:** 설정할 Slugcat 선택
- **트레이 아이콘 왼쪽 클릭:** 설정 패널 열기
- **트레이 아이콘 우클릭 → 먹이 주기:** 푸른 열매 또는 알벌레 알을 선택한 Slugcat 주변에 떨어뜨리기
- **모든 모니터 밖으로 벗어남:** 약 1초 후 안전한 바닥으로 자동 복귀

시스템 단축키와 충돌하지 않도록 전역 단축키는 등록하지 않습니다. 먹이와 Slugcat
오버레이는 일반 데스크톱 클릭을 가로채지 않으며, Slugcat을 직접 잡을 때만 왼쪽 드래그
입력을 사용합니다. 트레이 우클릭 메뉴는 설정 창을 열 수 없을 때 사용할 수 있는 보조
경로입니다.

음식 업데이트의 구현 범위와 새 아이템 확장 방법은
[음식 업데이트 상세 보고서](docs/FoodUpdateReport.ko.md)를 참고하세요.

## 스킨 편집기 (Experimental)

> [!WARNING]
> 스킨 편집기는 개발 중인 실험적 기능입니다. UI, 프리셋 형식, 지원 범위와 결과가
> 이후 버전에서 변경될 수 있습니다. 외부 스킨은 **Dress My Slugcat(DMS) 형식만**
> 지원하며, SlugBase 캐릭터·지역·게임플레이 코드·모드 DLL은 실행하지 않습니다.

![실험적 Slugcat 스킨 패널](docs/media/readme/skinPanel-rework.png)

스킨 편집기에서는 머리, 얼굴, 몸, 팔, 엉덩이, 다리, 꼬리와 The Mark 파츠를
개별적으로 선택하고 색상을 변경할 수 있습니다. 서로 다른 DMS 스킨의 파츠를 섞거나
설정을 프리셋으로 저장하고 다시 불러올 수도 있습니다.

DMS 스킨이 목록에 나타나려면 다음 조건을 모두 확인하세요.

1. **설치본 확인:** Rain World 설치본을 프로그램이 올바르게 찾았는지 확인합니다.
2. **모드 설치:** Dress My Slugcat 모드와 사용할 DMS 스킨 모드를 Rain World에 설치합니다.
3. **모드 활성화:** Rain World의 Remix 메뉴에서 DMS와 해당 스킨 모드를 모두 활성화하고 게임을 정상
   종료합니다.
4. **목록 갱신:** 프로그램의 **Workshop 모드 새로 고침**을 실행하거나 프로그램을 다시 시작합니다.

프로그램은 Rain World의 `mods`, `mergedmods` 및 발견된 Steam Workshop 폴더에서
`metadata.json`과 PNG/TXT atlas 쌍을 검색합니다. 손상되었거나 필수 프레임이 부족한
파츠, 비활성화된 스킨 모드, DMS가 아닌 모드는 선택 목록에서 제외됩니다.

## 문제 해결

- **Rain World 경로를 찾지 못함**: `RainWorld.exe`가 들어 있는 최상위 설치 폴더를
  직접 선택하거나 `--rain-world` 옵션으로 지정하세요.
- **기본 외형이 깨지거나 절차형 외형만 표시됨**: Steam 등에서 Rain World 게임 파일
  무결성을 검사한 뒤 다시 실행하세요.
- **DMS 스킨이 보이지 않음**: DMS 설치 여부, 스킨 모드의 Remix 활성화 상태와
  PNG/TXT atlas 쌍을 확인하고 Workshop 모드를 새로 고침하세요.
- **화면이 멈추거나 렌더링이 사라짐**: 트레이 메뉴의 **렌더링 재시도**를 사용하고
  그래픽 드라이버를 업데이트하세요.
- **오류 확인**: `%LOCALAPPDATA%\SlugcatInMyMonitor\errors.log`와
  `%LOCALAPPDATA%\SlugcatInMyMonitor\workshop.log`를 확인하세요.

## 개발

개발 빌드에는 다음 도구가 필요합니다.

- PowerShell 5.1 이상
- Visual Studio 2022 C++ 데스크톱 빌드 도구(v143)
- Windows 10/11 SDK

`.NET Framework 4.8` 참조 어셈블리는 빌드 스크립트가 필요할 때 내려받습니다.
Release 빌드와 전체 테스트는 다음 명령으로 실행합니다.

```powershell
.\build.ps1 -Configuration Release
```

DirectComposition 브리지는 Windows SDK의 Direct3D 11, DXGI, DirectComposition
라이브러리를 사용합니다. 일반 변경은 `feature/*` 또는 `fix/*`에서 작업해 `develop`으로
합치고, 배포할 변경은 `develop`에서 `main`으로 PR을 만듭니다. 자세한 절차는
[CONTRIBUTING.ko.md](CONTRIBUTING.ko.md)를 참고하세요.

구현 세부사항은 다음 문서에 정리되어 있습니다.

- [전체 구조](docs/Architecture.md)
- [원작 동작 대응표](docs/RainWorldBehaviorMap.md)
- [Slugcat 능력 대응표](docs/SlugcatAbilityParity.md)
- [Slugcat 그래픽 프로필](docs/SlugcatGraphicsProfiles.md)
- [Workshop 및 DMS 호환성](docs/WorkshopCompatibility.md)
- [로컬 자산 조사](docs/analysis/AssetFindings.md)
- [DLL 조사](docs/analysis/DllFindings.md)
- [원작 충실도 보강 기록](docs/analysis/RainWorldFidelityOverhaul.md)

## 에셋, 라이선스 및 상표

이 저장소는 Rain World, Dress My Slugcat 또는 커뮤니티 스킨의 이미지와 게임 에셋을
배포하지 않습니다. 사용자는 Rain World와 스킨을 정당하게 소유하고 각 에셋의 이용
조건을 따라야 합니다. 자세한 내용은
[THIRD_PARTY_TEST_ASSETS.md](THIRD_PARTY_TEST_ASSETS.md)를 참고하세요.

이 프로젝트는 비공식 팬 프로젝트이며 Videocult 또는 Akupara Games와 제휴하거나
승인받은 프로젝트가 아닙니다. Rain World 및 관련 명칭과 자산의 권리는 각 권리자에게
있습니다. 프로젝트 코드는 [MIT License](LICENSE)로 배포되며 제3자 자산에는 이
라이선스가 적용되지 않습니다.
