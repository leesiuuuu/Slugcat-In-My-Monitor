# 기여 가이드

<p align="center">
  <a href="CONTRIBUTING.md">English</a> | <strong>한국어</strong>
</p>

SlugcatInMyMonitor에 관심을 가져주셔서 감사합니다. 이 문서는 이슈 제안부터 Pull Request(PR) 병합까지의 기본 규칙을 설명합니다.

English contributions are welcome. Before opening a PR, please target `develop`, keep the change focused, run `.\build.ps1 -Configuration Release`, disclose unrun checks and meaningful AI assistance, and confirm that every dependency and third-party asset can be redistributed. By submitting a contribution, you confirm that you have the right to provide it under this repository's MIT License. The sections below are the authoritative contribution policy.

## 기여 전에 확인할 사항

- 버그나 기능 제안은 먼저 기존 이슈와 PR을 검색해 주세요.
- 큰 기능, 구조 변경, 새 의존성 도입은 구현 전에 이슈에서 범위와 방향을 논의해 주세요.
- 보안 취약점이나 공개하면 악용될 수 있는 문제는 공개 이슈로 재현 절차를 올리지 말고, 저장소 소유자에게 비공개로 알려 주세요. 공개된 비공개 연락 수단이 없다면 민감한 내용 없이 연락 방법만 묻는 이슈를 생성해 주세요.
- 모든 참여자는 [행동 강령](CODE_OF_CONDUCT.ko.md)을 따라야 합니다.

## 개발 환경과 검증

개발에는 Windows, PowerShell 5.1 이상, Visual Studio 2022 C++ 데스크톱 빌드 도구(v143), Windows 10/11 SDK가 필요합니다. 전체 Release 빌드와 테스트는 다음 명령으로 실행합니다.

```powershell
.\build.ps1 -Configuration Release
```

문서나 JavaScript 도구만 수정한 경우에도 관련 검증을 실행해 주세요.

```powershell
npm test
node --check tools\validate-dms-template.mjs
```

실행하지 못한 검증이 있다면 PR 본문에 명령, 이유, 예상 위험을 적어 주세요. 동작이나 렌더링이 바뀌었다면 가능한 경우 스크린샷이나 짧은 영상을 첨부해 주세요.

## 브랜치와 커밋

이 저장소는 가벼운 Git Flow를 사용합니다.

- `main`: 배포 가능한 코드
- `develop`: 다음 배포를 위한 통합 브랜치
- `feature/<name>`, `fix/<name>`: `develop`에서 분기하고 `develop`으로 PR 생성
- `release/<version>`: 별도 안정화가 필요할 때만 `develop`에서 분기
- `hotfix/<name>`: `main`에서 분기하고 `main`에 병합한 뒤 `develop`에도 반영

일반 변경은 `develop`을 대상으로 하고, 배포 PR만 `develop`에서 `main`으로 생성합니다. 커밋과 PR 제목에는 가능하면 Conventional Commit 형식을 사용해 주세요.

```text
feat: add a new user-facing capability
fix: prevent food from leaving the monitor
docs: clarify local asset requirements
```

지원하는 대표 접두사는 `feat`, `fix`, `docs`, `build`, `ci`, `refactor`, `test`, `chore`입니다. 한 PR은 하나의 목적에 집중하고, 기능 변경과 관계없는 대규모 서식 변경은 분리해 주세요.

## 코드와 테스트 기준

- 기존 구조와 명명 방식을 따르고, 공개 API나 새 결합 지점은 필요한 범위로 제한해 주세요.
- 버그 수정에는 가능하면 수정 전 실패하고 수정 후 통과하는 회귀 테스트를 추가해 주세요.
- 기능 추가에는 정상 경로뿐 아니라 제한값, 실패 경로, 정리 및 수명 주기를 검증하는 테스트를 포함해 주세요.
- 사용자 동작, 설정, 설치 방법 또는 호환성이 바뀌면 `README.md`나 관련 `docs/` 문서를 함께 갱신해 주세요.
- 디버그 출력, 로컬 경로, 자격 증명, 개인 정보, 빌드 산출물과 임시 파일을 커밋하지 마세요.
- 새로운 외부 의존성은 필요성, 출처, 버전, 라이선스와 배포 영향을 PR에 설명해 주세요.

## 호환성 조사와 공개 저장소 경계

Rain World와의 호환성 작업은 공개 저장소에서 **관찰 가능한 동작과 프로젝트 자체 구현**을 중심으로 기록합니다. 자세한 원칙은 [행동 호환성 및 소스 경계](docs/BehaviorCompatibility.ko.md)를 참고해 주세요.

- 공개 PR이나 문서에 디컴파일된 소스, 복원한 제3자 메서드 본문, IL/ILDASM 출력, 메서드 토큰, RVA, 바이너리 오프셋 또는 디컴파일러 덤프를 포함하지 마세요.
- Rain World의 DLL, 실행 파일, 추출한 텍스처·오디오 데이터 또는 기타 독점 게임 파일을 저장소에 추가하지 마세요.
- 조사 결과는 사용자에게 보이는 동작 요구사항, 프로젝트 회귀 테스트 또는 이 프로젝트의 구조에 맞춘 독립적인 구현 명세로 줄여서 제출해 주세요.
- 로컬 분석 산출물과 상세 역분석 노트는 git에서 제외된 개인 작업 디렉터리에 보관하세요.
- 제3자 구현의 표현을 그대로 유지하는 소스 형태의 의사코드나 주석을 공개 문서에 옮기지 마세요.

이 규칙은 저장소 위생과 출처 관리 기준이며, 특정 코드의 법적 상태를 단정하는 의견은 아닙니다.

## AI 보조 도구 사용

AI 보조 도구 사용 자체는 금지하지 않습니다. 다만 제출자는 생성된 코드와 문서를 직접 검토하고, 동작·보안·라이선스에 대한 책임을 집니다.

- AI가 의미 있는 부분을 작성하거나 설계했다면 PR의 해당 항목에 도구와 사용 범위를 적어 주세요.
- 이해하거나 검증하지 못한 생성 결과를 그대로 제출하지 마세요.
- 비공개 코드, 개인 정보, 자격 증명, 재배포 권한이 없는 에셋을 외부 AI 서비스에 입력하지 마세요.
- AI 사용 공개는 리뷰 강도를 정하기 위한 정보이며, 테스트나 설명을 대신하지 않습니다.

## 에셋, 저작권과 라이선스

Rain World, Dress My Slugcat(DMS), Workshop 모드와 커뮤니티 스킨의 이미지·오디오·바이너리는 권리자의 명시적인 재배포 허가 없이 저장소에 추가할 수 없습니다. 테스트 자료는 [THIRD_PARTY_TEST_ASSETS.md](THIRD_PARTY_TEST_ASSETS.md)의 규칙을 따라야 합니다.

PR을 제출함으로써 기여자는 다음을 확인합니다.

- 제출한 코드와 자료를 제공할 권리가 있습니다.
- 기여 내용이 저장소의 [MIT License](LICENSE)로 배포되는 것에 동의합니다.
- 제3자 코드를 포함했다면 원 출처, 저작권 고지, 라이선스와 변경 여부를 명시했습니다.

출처가 불분명하거나 프로젝트 라이선스와 호환되지 않는 자료는 병합하지 않습니다.

## Pull Request 작성

PR 템플릿의 모든 관련 항목을 작성해 주세요. 특히 다음 내용이 리뷰어가 재현할 수 있을 정도로 구체적이어야 합니다.

1. 해결하려는 문제와 사용자에게 보이는 결과
2. 변경 범위와 의도적으로 제외한 사항
3. 관련 이슈(`Closes #123` 등)
4. 실행한 검증 명령과 결과, 실행하지 못한 검증
5. 시각적 변경의 전후 자료
6. 새 의존성·제3자 자료·AI 보조 도구 사용 여부
7. 알려진 제한과 후속 작업

Draft PR은 방향을 일찍 공유할 때 사용할 수 있습니다. 리뷰를 요청하기 전에는 최신 `develop`을 반영하고, 충돌을 해결하고, CI가 통과하며, 자체 리뷰와 체크리스트를 마쳐 주세요. 리뷰 의견에 대응할 때는 수정한 커밋이나 근거를 남기고, 해결되지 않은 대화를 임의로 닫지 마세요.

## 리뷰와 병합 기준

유지관리자는 정확성, 회귀 위험, 테스트, 문서, 보안, 성능, 접근성, 라이선스와 프로젝트 범위를 기준으로 검토합니다. CI 통과는 필수 조건이지만 병합을 보장하지는 않습니다. 다음 경우 변경을 요청하거나 PR을 닫을 수 있습니다.

- 재현이나 검증에 필요한 설명이 부족한 경우
- 요청한 수정이 해결되지 않았거나 범위가 계속 확장되는 경우
- 권한 없는 제3자 자료, 비밀 정보 또는 악성 코드가 포함된 경우
- 프로젝트 방향과 맞지 않거나 유지 비용이 이점보다 큰 경우
- 장기간 응답이 없고 최신 브랜치와 통합하기 어려운 경우

최종 병합 방식과 시점은 저장소 유지관리자가 결정합니다. 보통 기능 PR은 `develop`에 병합하고, `develop`에서 `main`으로의 배포 PR이 병합되면 Release Drafter와 배포 워크플로가 실행됩니다.
