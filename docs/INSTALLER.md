# 통합 오프라인 설치 파일

구현·검증일: 2026-10-27. 배포 파일은 `artifacts/installer/WindowsAlarm-Setup-win-x64.exe`입니다. 앱 EXE 안에 런타임을 넣는 방식과 별도로, 설치 파일 하나에 공식 런타임 설치 프로그램을 함께 담았습니다. 인터넷이 없는 PC에서도 설치 패키지를 별도로 내려받을 필요가 없습니다. YouTube 재생에는 인터넷이 필요합니다.

## 포함 구성 요소와 버전

| 구성 요소 | 정책 |
| --- | --- |
| Windows Alarm | .NET 10, Windows x64 폴더형 앱. 실행 파일에 .NET 런타임을 포함하지 않음 |
| .NET Desktop Runtime x64 | Microsoft 공식 10.0.14 오프라인 설치 파일. Core/WindowsDesktop/host 10.0.14 이상인 10.0 패치 버전이 있으면 건너뜀 |
| WebView2 Evergreen x64 | Microsoft 공식 오프라인 설치 패키지. 검증한 브라우저 엔진 155.0.4283.62 이상이 있으면 건너뜀. 더 최신 버전을 낮추지 않음 |
| Inno Setup 6.7.3 | 빌드 도구로만 사용. 사용자 PC에 컴파일러를 설치하지 않음 |

WebView2 설치 프로그램 자체의 파일 버전 `1.3.303.7`, 브라우저 엔진 버전 `155.x/156.x`, 앱에서 사용하는 SDK 패키지 버전 `1.0.4191.47`은 서로 다른 번호입니다. Evergreen은 Microsoft 보안 업데이트를 계속 받을 수 있도록 정확한 구버전으로 고정하지 않습니다. 제공할 오프라인 설치 파일 자체는 `installer/dependencies.json`의 고정 Microsoft CDN 주소·SHA-256으로 고정합니다.

.NET은 설치용 앱의 `runtimeconfig.json`에 Core와 WindowsDesktop 최소 `10.0.14`, `LatestPatch`를 명시합니다. .NET 8 또는 .NET 11만 있는 PC는 이 앱의 .NET 10 요구 조건을 만족한 것으로 처리하지 않습니다. SDK나 ASP.NET Core Runtime은 설치하지 않습니다.

## 설치와 권한

1. Setup 파일을 실행합니다. 기본 설치 경로는 `%LOCALAPPDATA%\Programs\WindowsAlarm`입니다.
2. 설치 준비 화면에서 .NET과 WebView2가 이미 있는지 확인할 수 있습니다.
3. 필요한 경우 동봉한 Microsoft 설치 프로그램을 실행합니다. .NET 시스템 설치에는 Windows 관리자 승인 또는 관리자 자격 증명이 필요합니다. 거절하면 설치를 중단합니다.
4. WebView2는 일반 사용자 문맥에서 공식 설치 프로그램을 실행합니다. 기존 Microsoft Edge Updater 구성에 따라 Microsoft 설치 프로그램의 설치 범위가 결정됩니다.
5. 시작 메뉴와 선택한 경우 바탕 화면 바로가기를 만듭니다. 프로그램은 일반 사용자 권한으로 실행합니다.

Setup 자체는 `PrivilegesRequired=lowest`이며 런타임이 이미 있으면 불필요하게 관리자 권한을 요청하지 않습니다. .NET 설치는 `/install /quiet /norestart`로 실행합니다. 재부팅 필요 코드 3010이면 설치를 멈추고 Windows 재시작 후 Setup 재실행을 안내하며 강제 재부팅하지 않습니다. 설치 실패나 버전 확인 실패를 성공으로 표시하지 않습니다.

Windows x64용입니다. ARM64와 32비트 Windows는 이번 설치 파일의 대상이 아닙니다. Windows N의 Media Feature Pack, 오디오 드라이버·출력 장치, S4 지원, 최대 절전 활성화와 Wake Timer/펌웨어 설정은 별도 환경 조건입니다. Setup은 전원 계획, BIOS, 로그인 정책을 바꾸지 않습니다.

## 업데이트와 제거

같은 Setup을 실행하면 동일한 앱 경로에 갱신합니다. 사용 중인 앱은 설치 마법사에서 정상적으로 닫도록 안내하며 강제 종료 옵션을 사용하지 않습니다. 설정과 음악 경로는 기존 `%LOCALAPPDATA%\WindowsAlarm`을 사용합니다.

Windows 설정의 설치된 앱에서 **Windows Alarm**을 제거합니다. 제거 스크립트는 설치한 EXE만 정상 닫고, 기본 예약 상태의 자동 종료를 취소하고, 현재 사용자 SID와 설치 EXE 경로가 모두 일치하는 Windows Alarm 작업만 삭제합니다. 다른 경로의 작업은 건드리지 않습니다. 정리가 실패하면 제거를 중단하고 재시도를 안내합니다.

설정과 음악 파일은 보존합니다. .NET과 WebView2는 다른 프로그램도 사용하는 공유 구성 요소이므로 앱 제거 시 삭제하지 않습니다. 테스트용 별도 프로필의 제거 검증에는 `/ALARMDATADIR=<테스트 데이터 경로>`를 사용할 수 있습니다.

## 빌드와 검증

```powershell
.\build-installer.cmd
.\test-installer.cmd
```

- 빌드 시 .NET 패키지는 Microsoft 공식 SHA-512와 SHA-256을 확인합니다. WebView2/Inno 패키지도 고정 SHA-256과 신뢰할 수 있는 게시자 서명을 확인합니다. 불일치 시 다운로드를 임의로 신뢰하지 않고 중단합니다.
- Inno 도구는 공식 `/PORTABLE=1 /CURRENTUSER` 모드로 `artifacts` 안에만 준비합니다. 전역 도구 설치, 파일 연결, 컴파일러 바로가기는 만들지 않습니다.
- 매 빌드는 새 staging 폴더를 사용해 이전 자체 포함 배포의 런타임 DLL이 섞이지 않게 합니다.
- 실제 Setup의 `/VERIFYONLY /VERIFYREPORT=<경로>`는 동봉된 두 오프라인 설치 파일을 꺼내 해시를 검사하고 버전 경계 검증을 실행합니다. 런타임·앱·바로가기·제거 등록은 만들지 않습니다. 이 모드는 초기화 단계에서 의도적으로 종료하므로 프로세스 코드는 1이며, 보고서의 `Verified=1`로 성공을 확인합니다.

실행한 설치 전용 검증 4개는 모두 통과했습니다.

1. 실제 Setup의 동봉 오프라인 패키지 추출·해시 확인, 버전 경계, 설치 상태 무변경. 이 PC 판정은 .NET 설치 필요 / WebView2 호환 버전 있음이었습니다.
2. 설치용 앱이 공유 Core/WindowsDesktop 10.0.14와 `LatestPatch`를 요청하며 런타임을 내장하지 않는지 확인.
3. 프로젝트 전용 SDK 환경으로 설치용 앱을 실행해 WPF, 내장 HTML/JS, Alerts API, 실제 WebView2 컨트롤과 메시지 연결 확인. 시스템 .NET 설치 시험과는 다릅니다.
4. 실제 임시 Windows 작업으로 제거 스크립트 검증. 자기 EXE의 작업만 삭제, 다른 작업 유지, 예약 취소 상태 저장, 설정 파일 원본 보존. 임시 작업은 마지막에 정리했습니다.

이 회사 PC에는 .NET 6/8만 시스템 설치되어 있으며 .NET 10 SDK는 프로젝트 전용 폴더에 있습니다. **시스템 런타임 설치 프로그램은 실제 실행하지 않았습니다.** 초기화한 PC/가상 머신에서 실제 관리자 승인·공유 런타임 설치·재부팅 분기·앱 설치/제거 UI를 포함한 전체 시험은 남아 있습니다. 이 빌드는 코드 서명이 없는 개발 배포본입니다. 실제 S4 및 잠금 상태 소리 검증도 기존 수동 순서에 따라 수행해야 합니다.

로그는 `artifacts/installer-build/build.txt`, `artifacts/installer-build/tests.txt`, 개별 보고서는 `artifacts/installer-tests`에 있습니다. 설치 로그에는 로컬 경로가 포함될 수 있으므로 공개 저장소에 넣지 않습니다. Setup 옆에 생성되는 `.sha256`으로 배포 파일을 확인할 수 있습니다.

공식 자료: [.NET Windows 설치/종료 코드](https://learn.microsoft.com/en-us/dotnet/core/install/windows), [WebView2 오프라인/사용자별 배포](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution), [Inno 이벤트 함수](https://jrsoftware.org/ishelp/topic_scriptevents.htm), [Inno 포터블 모드](https://jrsoftware.org/ishelp/topic_technotes.htm).
