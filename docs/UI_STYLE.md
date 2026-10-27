# 둥근 UI와 나눔스퀘어 글꼴

변경일: 2026-10-27.

## 적용 범위

- 제공된 `nanum-square.zip`의 나눔스퀘어 OTF 4종(Light·Regular·Bold·ExtraBold)을 수정 없이 WPF 리소스로 포함합니다. Windows 글꼴 설치나 런타임의 네트워크 다운로드가 필요하지 않습니다.
- 버튼·입력창·선택 상자는 10px, 설정 카드는 16px 모서리를 사용합니다. 토글·선택 목록·포커스와 비활성 상태도 공통 스타일을 사용합니다.
- 메인 창, 종료 확인 창, YouTube 창의 앱 소유 영역에 같은 글꼴을 적용합니다. Windows가 그리는 제목 표시줄과 YouTube 콘텐츠 자체의 글꼴은 별도입니다.
- 하단에는 알람 테스트·설정 저장을 나란히 두고, 최대 절전 버튼을 그 아래 전체 너비로 고정합니다. 기존 설정 패널과 동일한 활성 상태를 바인딩하여 알람 중이나 자동 종료 확인 중 설정 동작이 다시 활성화되지 않게 합니다.
- 알람 상태·정지 버튼과 알람 예약 취소·PC 자동 종료 취소·로그·알람 테스트 동작을 유지합니다. 헤더 보조 문구와 개발용 2분 예약/S4 테스트 버튼은 제거했습니다.
- 전원 상태 확인은 상단에 두고, 로그 폴더·프로그램 정보·글꼴 라이선스는 상단 **⋯ (더보기)** 메뉴에 모읍니다. 프로그램 정보는 제작자 JaehongLIM2, 프로젝트 GitHub와 공개 요청된 문의 메일을 표시하며 링크 클릭 시 기본 브라우저·메일 앱을 엽니다. 글꼴 저작권과 SIL OFL 1.1 원문은 앱에 내장합니다. [출처와 해시](../src/WindowsAlarm/Assets/Fonts/README.md)를 기록했습니다.

최대 절전·정상 종료를 요청하는 API와 예약·복구, 오디오 출력·전환, 볼륨 처리, 자동 종료 판단 로직은 변경하지 않았습니다.

## 예약 상태와 취소 버튼

예약 상태는 스크롤 영역에서 하단 고정 영역으로 옮겼습니다. 알람 예약과 PC 자동 종료를 각각 상태 문구·취소 버튼 한 쌍으로 표시하고, 해당 예약이 없으면 행 전체를 숨깁니다. 일반 안내와 오류 메시지는 행이 숨겨져도 남깁니다. 안전 동작에 대한 설명은 자동 종료 설정 카드에 배치합니다.

표시는 미저장 입력값이 아닌 실제 예약 스냅샷과 종료 컨트롤러를 기준으로 합니다. 미래 알람에 자동 종료가 설정되어 있으면 알람 시작 후 몇 분 뒤 종료 확인이 시작되는지 표시합니다. 알람 재생이 끝나도 자동 종료가 남아 있으면 취소 버튼을 유지하고, 설정이 비활성화되어도 두 취소 버튼은 사용할 수 있습니다. 종료 시각은 즉시 전원이 꺼지는 시각으로 오해하지 않도록 **종료 확인 예정**으로 표시합니다.

파일 저장이나 Windows 작업 삭제가 실패하면 실패 상태를 노출하여 취소 버튼을 유지합니다. 재시도 성공 후에만 숨기며, 취소된 작업의 복구 중 삭제가 실패한 경우에도 다시 시도할 수 있습니다. Windows에 실제 종료를 요청한 뒤에는 앱 내 취소 버튼 대신 Windows 종료 화면 안내를 표시합니다.

## 토글과 전원 상태 안내

볼륨 자동 설정과 자동 종료를 기본 WPF `ToggleButton` 기반 스위치로 표시합니다. 상태 문구·색상·손잡이 위치로 켜짐/꺼짐을 구분하며 키보드와 UI Automation Toggle 패턴을 유지합니다. 기존 JSON 설정 항목은 그대로 사용하고, 볼륨 자동 설정이 꺼지면 종료 후 볼륨 선택도 기존처럼 비활성화됩니다.

전원 상태 모달은 읽기 전용 Win32 조회로 S4·전체 최대 절전 파일·AC/DC 복귀 타이머·RTC 지원 정보를 보여줍니다. 현재 사용하는 전원의 타이머를 기준으로 판단하며, 전원/배터리 두 설정을 함께 안내합니다. '중요한 타이머만', 빠른 시작 전용 파일, 조회 실패나 알려지지 않은 값은 준비 완료로 표시하지 않습니다. 펌웨어의 실제 복귀까지 검증했다는 뜻은 아닙니다.

모달은 최대 절전 활성화 명령과 Wake Timer 설정 경로를 표시하고, 사용자 클릭으로 Windows 전원 옵션을 여는 기능과 새로고침을 제공합니다. 명령은 자동 실행하지 않으며 전원 계획·레지스트리·BIOS 설정도 바꾸지 않습니다.

공식 자료: [전원 공급 상태](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-system_power_status), [S4·RTC 지원 정보](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-system_power_capabilities), [AC 설정 조회](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powerreadacvalueindex), [DC 설정 조회](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powerreaddcvalueindex), [최대 절전 설정 명령](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options).

## 나눔스퀘어 가독성 조정

글꼴 교체 후 이전의 작은 크기를 유지해 보조 설명이 가늘고 작게 보이던 부분을 조정했습니다. 아래 크기는 WPF의 DIP 값이며 pt 단위가 아닙니다.

| 용도 | 이전 | 조정 |
| --- | --- | --- |
| 본문·토글·상태 | 14 | 16 |
| 필드 라벨 | 14 Bold | 15 Bold |
| 입력값 | 14 | 15 |
| 보조 설명 | 12 / 줄 높이 18 | 14 / 줄 높이 21 |
| 일반 버튼 | 14 Regular | 15 Bold |
| 하단 보조 버튼 | 12 | 13 |
| 제목 / 시·분 | 30 / 26 Regular | 28 Bold / 28 Bold |

글자 크기에 맞춰 입력창과 일반 버튼의 최소 높이는 44로, 카드 안쪽 여백은 20으로 조정했습니다. 제목과 본문의 크기 차이도 줄였습니다.

첫 조정에서는 `Ideal`·`Grayscale`을 적용했지만, 이후 사용자 스크린샷을 4배로 확대해 획의 명암이 고르지 않고 일부가 끊겨 보이는 증상을 다시 확인했습니다. 크기·배치만으로 설명할 수 없었으며, 글꼴 파일 손상으로 단정하지 않았습니다.

동일 압축 파일의 원본 TTF·OTF를 14·15·16 DIP와 같은 문구로 비교했습니다. `Ideal/Display`와 `Grayscale/ClearType`의 8개 조합을 실제 WPF 진단 창에서 그리고 해당 창만 Win32 `PrintWindow`로 캡처했습니다. 비교 창의 DPI는 96이었습니다. 이 환경에서 더 또렷하게 보인 **OTF·Ideal·ClearType**을 채택하고 정적 텍스트의 `TextHintingMode=Fixed`, 레이아웃 반올림·장치 픽셀 맞춤을 적용했습니다. 이전 단계에서 맞춘 글자 크기와 배치는 유지합니다.

OTF의 내부 글꼴 이름은 `NanumSquareOTF`입니다. 글꼴 자체를 수정하거나 다른 글꼴로 대체하지 않습니다. 위 비교는 특정 환경의 시각 확인이며, 모든 모니터에서의 선호나 모든 크기의 출력을 보장하는 결과는 아닙니다. Windows의 전역 ClearType·디스플레이 배율·글꼴 설정도 변경하지 않습니다.

원본 확대와 설정별 비교·진단 코드는 Git 제외 대상인 `artifacts/font-rendering-review`에 있습니다. [WPF 글꼴 크기 단위](https://learn.microsoft.com/en-us/dotnet/api/system.windows.documents.textelement.fontsize), [레이아웃 방식](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.textformattingmode), [텍스트 렌더링 모드](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.textrenderingmode), [정적 텍스트 힌팅](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.texthintingmode)

## 검증

- Release 빌드: 경고 0·오류 0.
- 기본 회귀 검증: 69개 통과·실패 0. 실제 WPF 창의 렌더링·토글 Toggle 패턴과 저장·정상 닫기, 전원 조건의 AC/DC·부분 파일·조회 실패 분기, 전원 모달 새로고침과 실패 표시, 종료 확인 창 닫기 시 취소 동작 포함. 예약·무예약·소리 정지 후 종료 대기·취소 실패와 재시도, 최소 창 크기(560×640)에서 설정 비활성화·스크롤 중 취소 버튼의 접근성도 확인합니다.
- 실제 렌더링 이미지로 모서리, 글꼴, 입력창 높이와 하단 버튼 배치 확인.
- 프로그램 정보 창의 실제 제작자 문구·GitHub·문의 메일 바인딩과 링크 대상을 확인했습니다. 외부 브라우저·메일 앱은 테스트에서 실행하지 않습니다. 보안 검사에서는 제작자가 공개를 요청한 정확한 문의 주소만 `AppInfo.cs`에서 허용하고, 동일 파일의 다른 주소와 다른 파일의 주소는 계속 차단합니다.
- 이 메일 예외는 격리된 저장소에서 작업 파일·인덱스 각각의 허용 주소 통과, 같은 파일의 추가 주소 차단, 다른 경로의 동일 주소 차단까지 6개 검증을 통과했습니다. 프로그램 정보 화면과 관련 검증 기록은 Git 제외 대상인 `artifacts/about-review`에 있습니다.
- 단일 EXE 진단: 4개 굵기의 실제 글꼴 파일 URI, 굵기, 한국어 글리프와 내장 라이선스 확인. 시스템 글꼴로 대체된 상태를 성공으로 처리하지 않습니다. 기존 WPF·Alerts API·실제 WebView2 연결 진단도 통과.
- 최종 폴더형 실행본·단일 EXE·통합 설치 파일을 모두 갱신했습니다. 설치 파일 전용 검증 4개(동봉 패키지·버전 규칙, 런타임 정책, 설치용 앱 진단, 자기 작업만 제거하는 정리)도 통과했습니다. 실제 시스템 런타임 설치는 실행하지 않았습니다.
- 보안 검사 예외는 정확한 원본 글꼴 Git blob 4개로 한정합니다. 격리된 저장소에서 정상 파일·정상 인덱스 허용, 변조 파일·변조 인덱스 차단, 다른 바이너리 파일·인덱스 차단의 6개 검증을 통과했습니다.

이번 UI 검증에서는 실제 S4 진입·복귀, 잠금 중 소리, 정상 Windows 종료를 실행하지 않았습니다. 해당 실기 결과는 [기존 검증 기록](VALIDATION.md)과 구분합니다.

현재 UI와 전원 모달 검증 이미지는 Git에서 제외하는 `artifacts/about-review`, 단일 EXE 진단은 같은 경로의 `single-file-check/deployment-check.json`에 있습니다. 대표 이전 화면은 `artifacts/ui-refresh/main-window.png`, `artifacts/footer-label-review/main-window.png`에 남깁니다. 기본 산출물 정리에서 나머지 과거 프로필·중간 결과를 제거했으며, 보관한 폰트 비교 자료와 정확한 정리 범위는 [정리 기록](CLEANUP_CANDIDATES.md)에 있습니다.
