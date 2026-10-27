# 파일 정리 후보와 기본 정리 실행 기록

조사일: 2026-10-27. 기준 커밋: `49f7b1e`.

상태: **기본 후보 A 정리 완료. 추가 후보 B 미실행. 프로그램 소스·실행 파일 변경 없음.**

## 기본 정리 실행 결과

- 실행 완료: 2026-10-27 16:34 JST. 사용자 승인에 따라 **A만 실행**, B는 미실행.
- 삭제: **4,819개 파일, 820,796,891바이트 (782.77MiB)**.
- 정리 직전: 12,440개 파일, 약 2.773GiB. 정리 직후: 7,621개 파일, 약 2.008GiB.
- 위 실측에는 후보 목록 MD가 포함된다. 이후 기록 갱신과 Git 커밋으로 작은 용량 차이가 생길 수 있다.
- 삭제 전후 문서 갱신 직전까지, 실제 저장소의 Git 내부 메타데이터를 제외한 **보관 파일 7,213개의 SHA-256이 모두 동일**함을 확인했다.
- 보관 예외 자료 42개, 최신 배포본 3종, .NET 10 SDK, 최신 staging 및 B의 캐시·도구를 보존했다.
- 삭제 대상 파일 수·바이트 합계가 승인된 A 목록과 정확히 일치했고, 삭제 경로의 절대 위치가 작업 폴더 안에 있는지 확인했다.
- 단일 EXE의 최종본·중간본 해시가 같음을 삭제 전에 확인했다. 정리 당시 프로젝트 관련 앱·빌드 프로세스는 없었다.
- 프로그램 소스와 실행 파일을 수정하지 않았다. 아래 조사 당시 목록은 이력으로 유지하며, 현재 단계를 반영하도록 문서와 자료 위치 안내만 갱신했다.
- 빌드·테스트를 재실행하면 중간 결과물이 재생성되므로 이번 정리는 보관 파일 해시·파일 수·필수 경로를 검증했다. 기존 69개 회귀 검증을 새로 실행했다고 표시하지 않는다.

## 분류 기준

- 개발 과정은 Git 관리 문서와 커밋 이력으로 남긴다.
- 소스, 테스트 원본, 빌드·배포 스크립트, 계정 보호 설정과 글꼴 라이선스를 보존한다.
- 현재 배포본 3종과 설치 검증에서 참조하는 최신 staging을 보존한다.
- 최신 검증 요약과 대표 화면·폰트 비교 자료만 선별 보존한다.
- 반복 생성된 과거 빌드, 테스트 프로필, 복제 저장소와 캐시는 별도로 판단한다.

목록 작성 전 숨김 파일과 하위 폴더를 포함해 12,439개 파일, 2,977,070,686바이트(약 2.773GiB)를 집계했다. 읽기 실패나 제외한 재분석 지점은 없었다. 용량은 파일 크기 합계이며 아래 후보 사이의 중복을 제외했다.

## 요약

| 구분 | 파일 수 | 용량 | 영향 |
| --- | ---: | ---: | --- |
| A. 기본 정리 후보 | 4,819 | 782.77MiB | 보존 예외를 지키면 현재 실행·배포 파일과 소스는 유지. 빌드 중간 파일은 다음 빌드에서 재생성 |
| B. 추가 선택 후보 | 1,053 | 739.38MiB | 실행본에는 필요 없지만 다음 빌드·설치 파일 생성 시 캐시 복원이나 도구 재준비 필요 |
| A+B 합계 | 5,872 | 1.486GiB | 현재 개발용 .NET 10 SDK는 이 합계에서 제외 |

A만 정리하는 경우 파일 크기 합계는 약 2.008GiB, A+B를 정리하면 약 1.286GiB로 예상한다. 이 문서 자체의 크기는 조사 당시 합계에 포함하지 않았다.

## A. 기본 정리 후보

| ID | 대상 | 파일 수 | 용량 | 근거 |
| --- | --- | ---: | ---: | --- |
| A1 | 앱·테스트의 bin/obj 4곳 | 680 | 250.63MiB | 원본이 아닌 컴파일·복원 중간 결과 |
| A2 | `artifacts/single-file-build` | 4 | 70.19MiB | 최종 단일 EXE를 복사하기 전 출력과 XML 설명 파일 |
| A3 | 이전 installer staging 10곳 | 144 | 259.33MiB | 현재 last-build.json이 가리키는 1곳 제외. 빈 폴더 1곳 포함 |
| A4 | 이전 installer-tests 9곳 | 1190 | 55.47MiB | 최신 설치 검증 결과는 별도 선별 보존 |
| A5 | 이전 검증 폴더 7곳 전체 | 1141 | 20.61MiB | 과정·결과는 MD와 대표 로그로 보존. 더 최신의 화면·진단 결과 존재 |
| A6 | 검증 폴더 5곳에서 보존 목록을 제외한 파일 | 1568 | 73.07MiB | 임시 JSON/WAV/플레이어 HTML, WebView2 프로필, 테스트용 EXE와 복제 저장소 등 |
| A7 | 오래된 체크섬·압축 백업·SDK 전체 릴리스 목록 | 3 | 2.26MiB | 상세는 아래 목록. 정리 요약과 선택 SDK 메타데이터는 보관 |
| A8 | 구버전 Windows SDK 참조 캐시 | 89 | 51.22MiB | 현재 프로젝트와 복원 그래프에서 10.0.19041.44를 참조하지 않음 |

### A1: bin/obj만 해당

- `src/WindowsAlarm/bin` — 198.79MiB
- `src/WindowsAlarm/obj` — 19.24MiB
- `tests/WindowsAlarm.Tests/bin` — 31.71MiB
- `tests/WindowsAlarm.Tests/obj` — 0.89MiB

`src`나 `tests` 폴더 전체를 지우는 항목이 아니다. 원본 C#/XAML, 프로젝트 파일과 테스트 코드는 모두 보존한다.

### A2: 단일 EXE 중간 출력

- `artifacts/single-file-build` 전체.
- 최종 `artifacts/single-file/WindowsAlarm.exe`는 보존한다.
- 두 위치의 EXE 해시가 같음을 확인한 뒤 중간 출력을 정리했다.

### A3: 이전 staging의 정확한 경로

보존:

- `artifacts/installer-build/staging/33119ad21364438da2efceb7c444d411`
- `artifacts/installer-build/last-build.json`과 같은 폴더의 빌드·검증 메타데이터.

정리 후보:

- `artifacts/installer-build/staging/02a0443b645a44419ee8d3753a43a677`
- `artifacts/installer-build/staging/2580503f96c4470dbf4422f41ec98e55`
- `artifacts/installer-build/staging/351c024b28914981818f0cb276556a48`
- `artifacts/installer-build/staging/39dbd1d545cb444db5c96a53c26a9cc9`
- `artifacts/installer-build/staging/4e8d1a96ff4e4c33bf46d36d1b216aab`
- `artifacts/installer-build/staging/91bc3155fda548b28e835663d6921b1c`
- `artifacts/installer-build/staging/940df95d9e2d4c34aa47e0842712c3d9`
- `artifacts/installer-build/staging/b8a737b8733e47e2a59a660570387686`
- `artifacts/installer-build/staging/e07ee390d0c74548b85396bdad42263a`
- `artifacts/installer-build/staging/e0a785ef23a943f890dad6dca274d24f`

이 staging 중 최신 1곳은 `test-installer.cmd`가 실제로 참조한다. `staging` 상위 폴더 전체를 지우는 목록이 아니다.

### A4: 이전 설치 검증 폴더의 정확한 경로

- `artifacts/installer-tests/17bdd2db545f4e9d8c5f1672237c017c`
- `artifacts/installer-tests/17fc1c044018460ea0c0670ea583c692`
- `artifacts/installer-tests/58dfa091c85747859f395a7a96848fdb`
- `artifacts/installer-tests/81cbe82c9cb84053881e3f5646bfacc6`
- `artifacts/installer-tests/a161b5edcf6b412f9fd56182fb13c761`
- `artifacts/installer-tests/a5eb0f93098a4826b04ed93e4c4ebd0e`
- `artifacts/installer-tests/c038a38257464e319fc42e86d9d9afcb`
- `artifacts/installer-tests/dba056e83662406b8607f8437c304327`
- `artifacts/installer-tests/dcf898b66fd84b46b78d3c3e17070aef`

최신 `artifacts/installer-tests/b7daea90443143e98cccd1424574e333`는 폴더 전체 삭제 대상에서 제외하고, A6의 보존 목록을 적용한다.

### A5: 폴더 전체가 정리 후보

- `artifacts/footer-layout-review` — 5.17MiB
- `artifacts/net10-final-validation` — 0.35MiB
- `artifacts/single-file-check` — 0.00MiB
- `artifacts/single-file-validation` — 0.35MiB
- `artifacts/typography-review` — 4.83MiB
- `artifacts/ui-controls-final` — 4.95MiB
- `artifacts/ui-controls-review` — 4.95MiB

최신 앱·UI 진단은 `about-review`에 남긴다. .NET 8/10 비교 실패의 상세 결과는 `NET10_MIGRATION.md`와 최종 텍스트 로그를 보존하므로 과거 테스트용 WAV·JSON·프로필을 계속 둘 필요가 작다.

### A6: 일부 파일을 보존한 뒤 나머지만 정리

| 폴더 | 정리 후보 파일 수 | 정리 후보 용량 |
| --- | ---: | ---: |
| `artifacts/about-review` | 318 | 8.88MiB |
| `artifacts/font-rendering-review` | 282 | 8.71MiB |
| `artifacts/footer-label-review` | 222 | 8.90MiB |
| `artifacts/installer-tests/b7daea90443143e98cccd1424574e333` | 145 | 8.93MiB |
| `artifacts/ui-refresh` | 601 | 37.65MiB |

아래 42개 파일을 현재 경로에 남기고, 위 5개 폴더의 나머지 파일을 정리했다. 보존 자료 합계는 약 5.54MiB이다. 보존 파일을 다른 폴더로 이동하거나 압축하지 않았다.

- `artifacts/about-review/about-window.png`
- `artifacts/about-review/check-public-contact-guard.ps1`
- `artifacts/about-review/guard-approved-staged.log`
- `artifacts/about-review/guard-approved-working.log`
- `artifacts/about-review/guard-other-path-staged.log`
- `artifacts/about-review/guard-other-path-working.log`
- `artifacts/about-review/guard-second-address-staged.log`
- `artifacts/about-review/guard-second-address-working.log`
- `artifacts/about-review/main-window.png`
- `artifacts/about-review/main-window-countdown.png`
- `artifacts/about-review/main-window-reserved.png`
- `artifacts/about-review/main-window-reserved-minimum.png`
- `artifacts/about-review/power-status-window.png`
- `artifacts/about-review/public-contact-security.txt`
- `artifacts/about-review/single-file-check/deployment-check.json`
- `artifacts/font-rendering-review/before-after-4x.png`
- `artifacts/font-rendering-review/buttons-4x.png`
- `artifacts/font-rendering-review/checkbox-4x.png`
- `artifacts/font-rendering-review/comparison-native.png`
- `artifacts/font-rendering-review/comparison-wpf.png`
- `artifacts/font-rendering-review/current-ttf-ideal-grayscale-4x.png`
- `artifacts/font-rendering-review/description-4x.png`
- `artifacts/font-rendering-review/guard-checks.txt`
- `artifacts/font-rendering-review/otf/NanumSquareB.otf`
- `artifacts/font-rendering-review/otf/NanumSquareEB.otf`
- `artifacts/font-rendering-review/otf/NanumSquareL.otf`
- `artifacts/font-rendering-review/otf/NanumSquareR.otf`
- `artifacts/font-rendering-review/otf-display-grayscale-4x.png`
- `artifacts/font-rendering-review/otf-ideal-cleartype-4x.png`
- `artifacts/font-rendering-review/otf-ideal-grayscale-4x.png`
- `artifacts/font-rendering-review/probe/Probe.csproj`
- `artifacts/font-rendering-review/probe/Program.cs`
- `artifacts/font-rendering-review/ttf/NanumSquareB.ttf`
- `artifacts/font-rendering-review/ttf/NanumSquareEB.ttf`
- `artifacts/font-rendering-review/ttf/NanumSquareL.ttf`
- `artifacts/font-rendering-review/ttf/NanumSquareR.ttf`
- `artifacts/font-rendering-review/ttf-display-cleartype-4x.png`
- `artifacts/footer-label-review/main-window.png`
- `artifacts/installer-tests/b7daea90443143e98cccd1424574e333/app-check/deployment-check.json`
- `artifacts/installer-tests/b7daea90443143e98cccd1424574e333/setup.log`
- `artifacts/installer-tests/b7daea90443143e98cccd1424574e333/verify.ini`
- `artifacts/ui-refresh/main-window.png`

폰트 비교 도구는 `probe/Program.cs`가 `ttf`와 `otf` 폴더를 읽으므로 입력 글꼴도 함께 보존한다. 앱이 사용하는 원본 글꼴·라이선스는 `src/WindowsAlarm/Assets/Fonts`에 별도로 있으며 정리 대상이 아니다.

여기서 지칭하는 WebView2 프로필은 `artifacts` 아래 테스트·진단용 프로필뿐이다. 실제 앱의 `%LOCALAPPDATA%/WindowsAlarm` 설정·예약·프로필은 조사·삭제 대상에 넣지 않는다.

### A7: 개별 파일

- `artifacts/WindowsAlarm-single-win-x64.sha256`: 현재 단일 EXE와 일치하지 않는 이전 체크섬. 현재 소스·문서·스크립트에서 참조하지 않는다.
- `artifacts/cleanup-history-20261027-110428.zip`: 이전 정리 때 보관한 개별 로그 등 746개 항목의 백업. 과거 정리 요약과 상세 목록은 `artifacts/CLEANUP.md`, `artifacts/cleanup-report.json`에 남기며, 주요 검증 내용은 Git 관리 문서에도 있다.
- `artifacts/sdk-setup/releases-10.json`: 다운로드 당시 전체 SDK 릴리스 목록. 선택한 SDK 버전·URL·해시는 `artifacts/sdk-setup/selected-sdk.json`에 남긴다.

`artifacts/CLEANUP.md`에 후속 정리 사실을 추가했다. 과거 ZIP·프로필 위치는 당시 이력으로 해석하며, 현재 보관 위치는 이 문서의 보존 목록과 최신 자료 위치 안내를 따른다.

### A8: 현재 미참조 버전만 해당

- `artifacts/packages/microsoft.windows.sdk.net.ref/10.0.19041.44` 전체.
- 현재 버전 `10.0.19041.57`은 A의 대상에서 제외한다.
- 구버전 커밋이나 다른 SDK로 과거 빌드를 재현할 경우 이 캐시가 다시 필요할 수 있으며, 그때 복원·다운로드한다.

## B. 추가 선택 후보: 재다운로드·재준비를 허용하는 경우

| ID | 대상 | 용량 | 이후 영향 |
| --- | --- | ---: | --- |
| B1 | `artifacts/packages` 중 A8을 제외한 나머지 | 438.66MiB | 다음 빌드·테스트에서 필요한 NuGet 패키지와 런타임 팩 복원 필요 |
| B2 | `artifacts/installer-build/downloads` | 272.29MiB | .NET·WebView2·Inno 원본 패키지 재다운로드와 해시·서명 확인 필요 |
| B3 | `artifacts/installer-build/tools` | 27.59MiB | 다음 설치 파일 빌드 시 포터블 Inno 컴파일러 재준비 필요 |
| B4 | `artifacts/dotnet-home` | 0.84MiB | 프로젝트 전용 CLI 홈·캐시 재생성 |

A8은 B1 용량에 포함하지 않았으므로 A+B 합계에서 중복 계산하지 않는다. B를 정리해도 보존한 현재 EXE·설치 파일의 내용은 바뀌지 않는다. 네트워크 없이 곧 재빌드할 계획이면 B는 보관하는 편이 편리하다.

패키지 이름에 ASP.NET Core가 있다는 이유만으로 개별 파일을 불필요하다고 판단하지 않았다. 현재 앱의 복원 그래프에도 해당 런타임 팩의 다운로드 항목이 있으며, 앱 자체의 프레임워크 참조는 Core·WPF·Windows SDK이다. 캐시의 임의 DLL을 골라 삭제하는 방식은 제안하지 않는다.

## 삭제 후보에서 제외

- `src`·`tests`의 원본, `docs` 전체와 Zenn 초안, 루트 설정·프로젝트·빌드 스크립트.
- 글꼴 원본 4종과 LICENSE, 내장 HTML/JS, 잠금 중 오디오 및 비상 Fallback 구현.
- `.git`, `.githooks`, `.git-identity.local.json` 등 계정·저장소 보호 구성.
- `artifacts/app`: 폴더형 배포본 전체.
- `artifacts/single-file`: 최종 단일 EXE.
- `artifacts/installer`: 최종 설치 파일과 일치하는 체크섬.
- `artifacts/dotnet10`: 약 770.21MiB인 프로젝트 SDK. 현재 시스템에는 .NET 6/8 SDK만 있어 기존 빌드 명령을 유지하려면 필요하다.
- 최신 staging `artifacts/installer-build/staging/33119ad21364438da2efceb7c444d411` 및 `last-build.json` 등 설치 빌드 메타데이터.
- A6에 명시한 화면·진단·폰트 자료 42개.
- `artifacts/sdk-setup/selected-sdk.json`, `artifacts/CLEANUP.md`, `artifacts/cleanup-report.json`.
- 루트의 `artifacts/net10-build.txt`, `net10-publish.txt`, `net10-final-tests.txt`, `net8-baseline-tests.txt`, `single-file-build.txt`, `single-file-publish.txt`, `single-file-tests.txt`: 작은 대표 원본 로그로 보존.

## 기록이 남는 위치

- [검증 이력](VALIDATION.md), [자체 점검](SELF_REVIEW.md).
- [.NET 10 전환](NET10_MIGRATION.md), [단일 EXE](SINGLE_FILE.md), [설치·제거 검증](INSTALLER.md).
- [UI 변경·검증](UI_STYLE.md), [보안 점검](SECURITY_REVIEW.md).
- `docs/zenn`의 개발 기록 2편과 Git 커밋 이력.

소스·문서에서 기록된 동작과 실패/미검증 범위를 유지한 채 중간 산출물을 정리했다. 이 문서는 조사 당시의 후보 목록과 승인된 A 항목의 실행 결과를 함께 보관하며, B 항목은 삭제하지 않았다.

