# Windows Alarm

Windows 10/11용 개인 알람 프로그램입니다. **C# / .NET 10 LTS / WPF**를 사용합니다. 내장 YouTube 플레이어에 Microsoft 공식 `Microsoft.Web.WebView2` 패키지 하나를 사용하며, 별도 서버·웹앱·DB는 없습니다.

알람 예약 → Windows 작업 스케줄러 Wake Timer 등록 → S4 최대 절전 → 예약 시각에 알람 → 선택한 시간 뒤 종료 확인 순서로 동작합니다. 기본 자동 종료 설정은 **사용 안 함**입니다.

## 화면과 글꼴

버튼·입력창은 10px, 설정 카드는 16px의 둥근 모서리를 사용합니다. 하단에는 **알람 테스트 / 설정 저장**, 그 아래에는 전체 너비의 **알람 설정 후 최대 절전** 버튼을 고정합니다. 알람 재생이나 종료 확인 중에는 설정 작업이 비활성화됩니다. 예약이 있으면 하단에 상태와 해당 취소 버튼이 함께 나타납니다. PC 자동 종료 취소는 소리 정지 후에도 예약이 남아 있는 동안 표시되며, 설정 비활성화나 스크롤의 영향을 받지 않습니다. 취소 처리 실패 시에는 재시도 버튼을 유지합니다.

볼륨 자동 설정과 자동 종료는 켜짐·꺼짐이 표시되는 토글을 사용합니다. 상단 **전원 상태 확인**은 현재 전원 연결/배터리 상태, 최대 절전(S4), 전체 최대 절전 파일, 복귀 타이머와 Windows가 보고한 RTC 복귀 지원을 모달로 보여줍니다. 필요한 설정 방법, Windows 전원 옵션 열기, 상태 새로고침도 제공합니다. 조회 실패는 '확인 불가'로 표시하고 Windows 설정을 자동으로 변경하지 않습니다. **로그 폴더**와 **글꼴 라이선스**는 상단 **⋯ (더보기)** 메뉴에서 엽니다.

제공된 나눔스퀘어(NanumSquare)의 Light·Regular·Bold·ExtraBold 글꼴을 앱 리소스에 포함합니다. Windows 글꼴 설치나 온라인 다운로드 없이 표시되며, 상단 **⋯ → 글꼴 라이선스**에서 저작권·SIL OFL 1.1을 확인할 수 있습니다. [원본과 파일 해시](src/WindowsAlarm/Assets/Fonts/README.md)를 기록했습니다.

## 빌드와 실행

일반 사용자용으로는 **`WindowsAlarm-Setup-win-x64.exe` 통합 오프라인 설치 파일**을 제공합니다. 앱과 별도로 .NET Desktop Runtime 10.0.14, WebView2 Runtime 설치 패키지를 함께 넣었으며 호환 버전이 이미 있으면 건너뜁니다. 설치 파일은 `artifacts\installer`에 생성됩니다. [설치·제거·검증 안내](docs/INSTALLER.md)를 참고하세요.

```powershell
.\build-installer.cmd
.\test-installer.cmd
```

앱은 현재 사용자의 프로그램 폴더에 설치합니다. 필요한 .NET 시스템 런타임 설치 때만 관리자 승인을 요청하며, 앱 실행 자체는 일반 권한입니다. 설치 후 시작 메뉴의 **Windows Alarm**으로 실행합니다. 기본 최대 절전·Wake Timer 설정은 변경하지 않습니다.

빌드 환경: Windows 10 2004(빌드 19041) 이상 또는 Windows 11, **.NET 10 SDK**. [공식 다운로드](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)에서 설치합니다. `global.json`은 안정 버전 .NET 10 SDK만 선택하며, 설치된 10.0 SDK 중 최신 기능 밴드를 허용합니다.

`publish-single.cmd`는 **Windows x64용 EXE 하나**를 만듭니다. .NET 10 런타임, WPF, WinRT와 WebView2 로더를 포함하므로 실행 PC에 .NET SDK나 Desktop Runtime을 따로 설치할 필요가 없습니다. 기존 폴더형 배포도 `publish.cmd`로 만들 수 있습니다. `dotnet run` 등 개발 실행에는 .NET 10 SDK/Desktop Runtime이 필요합니다.

내장 YouTube 재생에는 **Microsoft Edge WebView2 Runtime**도 필요합니다. Runtime이 없거나 초기화에 실패하면 로컬 음악으로 전환합니다. [Microsoft WebView2 배포 안내](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)를 참고하세요. 개인 브라우저의 로그인·쿠키를 가져오지 않는 앱 전용 프로필을 사용합니다.

프로젝트 폴더의 터미널에서:

```powershell
.\build.cmd
.\test.cmd
.\publish-single.cmd
.\artifacts\single-file\WindowsAlarm.exe
```

단일 파일 배포는 **`artifacts\single-file\WindowsAlarm.exe` 하나만 복사**하면 됩니다. 실행 때 .NET이 필요한 네이티브 파일을 사용자 임시 캐시에 자동으로 준비하며, 설정·예약·로그·WebView2 프로필은 기존 `%LOCALAPPDATA%\WindowsAlarm`에 저장합니다. 선택한 로컬 MP3는 별도로 존재해야 합니다. EXE 파일명 변경과 공백·한글 경로도 지원합니다.

`publish.cmd`의 폴더형 배포는 `artifacts\app` 전체를 복사해야 합니다. `start-alarm.cmd`는 단일 EXE가 있으면 우선 실행하며, 없으면 폴더형을 실행합니다. **알람을 예약한 뒤 EXE를 이동·이름 변경·삭제하면 Windows 작업이 이전 경로를 찾습니다.** 이동이나 이름 변경은 예약 전에 하거나, 변경 후 앱에서 다시 예약하세요.

스크립트 없이도 사용할 수 있습니다.

```powershell
dotnet build WindowsAlarm.sln -c Release
dotnet run --project src/WindowsAlarm
dotnet run --project tests/WindowsAlarm.Tests -c Release
dotnet publish src/WindowsAlarm -c Release -r win-x64 --self-contained true -o artifacts/app -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugSymbols=false -p:DebugType=None
dotnet publish src/WindowsAlarm -c Release -r win-x64 --self-contained true -o artifacts/single-file-build -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:EnableCompressionInSingleFile=true -p:DebugSymbols=false -p:DebugType=None
```

CMD 스크립트는 .NET 도구 캐시를 프로젝트의 `artifacts` 아래에 두므로 PowerShell 실행 정책을 변경할 필요가 없습니다. `artifacts\dotnet10\dotnet.exe`가 있으면 해당 프로젝트 전용 SDK를 사용합니다. 그 외에는 설치된 SDK를 사용합니다. 전역 PATH와 기존 SDK 설치는 변경하지 않습니다.

첫 빌드/배포에서는 NuGet.org에서 Microsoft WebView2 패키지, Windows SDK 참조 팩, Windows x64용 .NET 런타임 팩을 복원합니다. `NuGet.Config`의 소스 매핑은 필요한 Microsoft 패키지만 허용합니다. WebView2 버전과 콘텐츠 해시는 `packages.lock.json`으로 고정합니다. 의존성이 캐시에 있으면 이후 오프라인 빌드도 가능합니다. 자체 포함된 .NET의 보안 패치를 반영하려면 최신 .NET 10 SDK/런타임 팩으로 다시 배포해야 합니다.

## 사용 방법

.NET 10 전환 범위와 실제 검증 결과는 [NET10_MIGRATION.md](docs/NET10_MIGRATION.md)에 있습니다.
단일 EXE의 포함 범위·실제 검증·제한 사항은 [SINGLE_FILE.md](docs/SINGLE_FILE.md)에 있습니다.

1. 시/분, YouTube URL(선택), 로컬 MP3(권장)를 설정합니다.
2. 자동 종료를 원하면 토글을 켜고 알람 시작 이후 몇 분인지 입력합니다. 범위는 1~1440분입니다.
3. **설정 저장**으로 저장한 뒤 **알람 테스트**로 소리와 출력 장치를 확인합니다.
4. **알람 설정 후 최대 절전**을 누릅니다. 선택한 시각이 이미 지났다면 다음 날로 예약합니다.
5. 재생 중에는 **알람 정지**로 내장 YouTube·로컬 음악·경고음을 모두 멈춥니다.

**Windows 볼륨:** 기본으로 알람 시작 시 현재 기본 출력 장치의 볼륨을 100으로 올리고 음소거를 해제합니다. `알람 종료 후 볼륨`에서 원래 볼륨·음소거 상태 복원 또는 **볼륨 0 (음소거하지 않음)**을 선택할 수 있습니다. 설정 저장과 예약만으로는 볼륨을 바꾸지 않습니다. YouTube/MP3/경고음 전환 중에는 유지하고, 알람 정지·YouTube 창 닫기·정상 앱 종료 시 적용합니다. 자동 종료 취소만으로는 음악이나 볼륨을 바꾸지 않습니다. `알람 테스트`에도 같은 볼륨 설정을 적용합니다. 알람 도중 기본 출력 장치가 바뀌어도 복원은 시작 시의 장치에만 적용합니다. 강제 프로세스 종료·전원 차단 시 즉시 복원은 보장하지 않습니다.

예약은 한 번만 실행됩니다. 매일 자동 반복하지 않으므로 다음 날 사용할 때 다시 예약하세요. 설정을 바꿔 저장해도 이미 예약한 알람은 예약 당시 설정을 사용합니다. 변경한 설정으로 예약 버튼을 다시 눌러야 반영됩니다.

- **알람 예약 취소**: 미래 알람 예약과 해당 Windows 작업을 취소합니다. 이미 시작한 소리와 자동 종료는 별도의 버튼으로 취소합니다.
- **PC 자동 종료 취소**: 카운트다운/종료 예약만 취소합니다. 미래 알람이 있다면 그 알람의 자동 종료도 취소합니다. 음악은 계속 재생됩니다.
- **알람 정지**: 내장 YouTube와 로컬 소리를 모두 정지합니다. 이미 시작된 자동 종료 예약은 유지됩니다. YouTube 창의 정지 버튼이나 창 닫기도 같은 동작입니다.
- **창 닫기**: 재생과 진행 중인 자동 종료를 취소합니다. 미래 알람과 그 설정은 유지되며 Windows 작업이 앱을 다시 실행합니다.
- **전원 상태 확인**: S4, 최대 절전 파일, 현재 전원 계획의 Wake Timer 상태를 읽기 전용으로 확인합니다.

## S4 최대 절전 준비

프로그램은 `GetPwrCapabilities`로 S4 지원과 전체 최대 절전 파일을 확인합니다. 사용할 수 없으면 다음 문구를 표시하고 S4 진입을 중단합니다.

> Windows 최대 절전 모드가 활성화되어 있지 않습니다.

사용자가 **관리자 터미널**에서 한 번 설정해야 하는 경우:

```powershell
powercfg /hibernate on
powercfg /h /type full
powercfg /a
```

빠른 시작에만 사용하는 reduced 최대 절전 파일은 충분하지 않습니다. `powercfg /a`에서 최대 절전을 사용할 수 있어야 합니다. 앱은 이 설정을 직접 바꾸거나 관리자 권한을 요청하지 않습니다. [Microsoft의 전원 상태 및 최대 절전 파일 설명](https://learn.microsoft.com/en-us/windows/win32/power/system-power-states)

실제 진입은 `SetSuspendState(true, false, false)`로 요청합니다. 첫 번째 인수는 S4이고 마지막 인수는 Wake 이벤트를 유지합니다. 현재 토큰에 있는 `SeShutdownPrivilege`만 잠시 활성화하고 원래 상태로 돌립니다. 권한이 없으면 오류를 표시하며 S3 Sleep으로 대체하지 않습니다. [SetSuspendState 공식 문서](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-setsuspendstate)

## Wake Timer 설정과 설계

제어판 → 전원 옵션 → 현재 전원 관리 옵션 설정 변경 → 고급 전원 관리 옵션 설정 변경 → **절전 → 절전 모드 해제 타이머 허용 → 사용**으로 설정합니다. AC 전원/배터리 항목을 각각 확인하세요. **중요한 타이머만**은 이 앱에 충분하지 않습니다.

앱은 현재 전원 공급에 해당하는 값을 읽고, 비활성·중요한 타이머만·조회 실패 상태라면 S4 진입을 중단합니다. 이후 전원 계획을 바꾸거나 AC 어댑터를 분리하면 조건이 달라질 수 있습니다. 전원 계획과 레지스트리는 변경하지 않습니다.

확인 명령:

```powershell
powercfg /a
powercfg /query SCHEME_CURRENT SUB_SLEEP RTCWAKE
powercfg /waketimers
powercfg /lastwake
```

명령에 따라 관리자 터미널이 필요할 수 있습니다. [powercfg 공식 문서](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options)

### 작업 스케줄러를 선택한 이유

`SetWaitableTimer`의 프로세스 소유 핸들에만 의존하면 프로세스 종료/재부팅에 대한 별도 복구가 필요합니다. 대신 Windows 작업 스케줄러 COM API를 직접 호출합니다. 별도 스케줄러 라이브러리를 사용하지 않습니다.

- `WakeToRun=true`: Windows가 예약 시각에 PC를 깨우도록 요청합니다.
- 시간 트리거와 사용자 로그인 트리거를 함께 등록합니다. 로그인 트리거는 알람 시각부터 활성화됩니다.
- 알람 시각 이후 2시간 동안 1분 간격으로 실행기를 재시도하여 앱 비정상 종료에 대응합니다.
- `StartWhenAvailable=true`, 시간 트리거의 종료 경계, 실패 재시작 설정을 사용합니다. 놓친 작업 실행은 Windows 정책에 따라 지연될 수 있습니다.
- `InteractiveToken` / `LeastPrivilege`: 현재 사용자의 로그인 세션에서 암호 저장·권한 상승 없이 실행합니다.
- 배터리 전환이나 네트워크 미연결 때문에 작업을 중단하지 않습니다.
- 예약 실행기는 기존 앱에 신호를 보내거나 GUI를 실행한 후 끝납니다. GUI는 자체 재생 중 절전 방지를 관리합니다.
- 사용자 SID와 데이터 폴더에 해당하는 고유 작업 하나만 갱신/삭제합니다. 다른 프로그램의 작업을 건드리지 않습니다.

작업 등록 및 읽기 확인이 실패하면 **최대 절전에 진입하지 않습니다**. 작업 등록만 성공했다고 실제 하드웨어 Wake를 보장하지는 않습니다. [WakeToRun](https://learn.microsoft.com/en-us/windows/win32/taskschd/tasksettings-waketorun), [작업 실행 보안 컨텍스트](https://learn.microsoft.com/en-us/windows/win32/taskschd/security-contexts-for-running-tasks), [StartWhenAvailable의 지연 및 적용 조건](https://learn.microsoft.com/en-us/windows/win32/taskschd/tasksettings-startwhenavailable)

### 로그인·재부팅·펌웨어 제한

S4는 사용자 세션과 앱 상태를 보존합니다. **로그인한 상태에서 최대 절전**을 사용하세요. 잠금 상태와 로그아웃은 다릅니다. InteractiveToken 방식은 로그인 세션이 필요하므로 Windows 업데이트 등으로 재부팅되고 로그인하지 않은 상태에서는 음악을 재생할 수 없습니다. 다음 로그인 때 예약 상태를 복구합니다. 비밀번호를 저장하거나 자동 로그인을 설정하지 않습니다.

일부 PC는 BIOS/UEFI의 RTC Alarm, Wake from S4, Resume by Alarm 등 제조사별 설정이 필요합니다. 제조사 설명서를 확인하세요. 앱은 BIOS/UEFI/NVRAM을 수정하지 않습니다. RTC Wake를 지원하지 않는 하드웨어, 전원 공급 차단, 완전 종료(S5), BitLocker 부팅 전 인증 등은 앱으로 해결할 수 없습니다. 실제 S4 Wake 가능 여부는 해당 PC에서 확인해야 합니다.

## 소리와 Fallback

YouTube URL은 HTTPS·정확한 호스트·11자리 동영상 ID를 검증하고 앱의 별도 내장 플레이어 창으로 엽니다. 일반 브라우저를 띄우지 않습니다. YouTube 공식 IFrame Player API의 상태/시간/음소거/볼륨과 실제 PCM 오디오 수신 및 Windows Alerts 플레이어의 재생 상태를 함께 확인합니다.

내장 YouTube 프레임의 영상에서 디코딩된 오디오를 Web Audio로 받아, 메모리에서 Windows `MediaPlayer`의 `Alerts` 스트림으로 전달합니다. WebView2의 일반 미디어 출력은 무음으로 두어 이중 재생을 방지합니다. PCM은 48kHz·스테레오·16비트이며 대기 큐는 최대 12패킷으로 제한합니다. 오디오 파일 저장, 마이크 접근, 다른 앱의 소리 캡처는 하지 않습니다. 프레임 출처, 동영상 ID, 실행 식별자와 패킷 길이를 확인하고 정지 시 두 출력 경로를 모두 닫습니다. YouTube 측 변경이나 Web Audio 제한으로 전달이 실패하면 로컬 MP3/경고음으로 전환합니다.

```text
YouTube 시작 → 최대 20초 동안 재생 확인
  재생 확인 성공 → YouTube만 재생, 로컬 음악은 대기
  오류·자동재생 차단·시간 초과 → YouTube 음소거 및 플레이어 폐기 → 로컬 음악 시작
```

`PLAYING` 상태만으로 성공 처리하지 않습니다. 음소거가 아니고 볼륨이 0보다 크며, 직전 2초 내에 무음이 아닌 PCM이 수신되고 Alerts 플레이어가 재생 중인 연속 표본에서 동영상 시간이 1초 이상 진행되어야 합니다. 재생 확인 후에도 감시하며 10초 이상 진행/출력을 확인하지 못하면 로컬로 전환합니다. 일시정지·음소거·긴 무음 구간도 이 조건에 해당할 수 있습니다. 영상은 반복 재생합니다.

로컬 전환 전에 앱 소유 WebView2를 음소거하고 폐기합니다. 이후 늦게 도착한 재생·오류 이벤트는 무시하며 YouTube를 자동으로 다시 켜지 않습니다. 정지/새 알람 시작 때도 이전 초기화와 감시를 취소합니다. 이전의 5초 뒤 무조건 MP3를 추가하던 동작은 제거했습니다. [YouTube IFrame API](https://developers.google.com/youtube/iframe_api_reference), [WebView2 오디오 상태](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.isdocumentplayingaudio)

MP3는 Windows `MediaPlayer`의 `Alerts` 오디오 범주로 반복 재생합니다. 파일 없음·디코딩 실패·10초 이상 재생 진행을 확인하지 못한 경우 코드로 생성한 PCM WAV 경고음도 같은 범주로 재생합니다. 이 출력 경로가 실패하면 `SoundPlayer`, Windows 경고음 순서로 재시도합니다. 시스템이 보고하는 오디오 `SoundLevel`과 잠금/잠금 해제 세션 이벤트를 로그에 남깁니다. API 재생 진행은 실제 스피커 소리를 보장하지 않으므로 S4 복귀 후 잠금을 해제하지 않은 상태의 가청 여부는 해당 PC에서 별도로 확인해야 합니다. Windows Hello나 잠금 정책은 변경하지 않습니다.

**스피커 전원 꺼짐과 출력 장치 없음은 소프트웨어 Fallback으로 해결할 수 없습니다.** Windows 볼륨 자동 설정을 끈 경우 시스템 음소거와 0 볼륨도 직접 확인해야 합니다. 위 재생 확인은 실제 스피커 소리를 측정하는 기능이 아닙니다. Windows 마스터 볼륨 변경은 같은 장치를 사용하는 다른 앱의 소리에도 영향을 주지만 앱별 볼륨 설정은 바꾸지 않습니다. Windows N 에디션은 Media Feature Pack이 필요할 수 있습니다. YouTube 자동재생 정책·영상의 임베드 제한·로그인 필요·네트워크 오류는 로컬 전환 사유입니다. 별도 프로필이므로 기존 브라우저의 YouTube 로그인 상태는 사용하지 않습니다. 다른 브라우저 프로세스와 세션은 종료하지 않습니다.

WebView2는 전용 로컬 HTML 폴더만 HTTPS 가상 호스트로 연결하여 Referer를 제공하고, 상태 메시지의 출처와 실행별 식별자를 확인합니다. 웹 콘텐츠에 네이티브 객체를 제공하지 않으며 파일 다운로드, 권한 요청, 새 창, 최상위 외부 페이지 이동을 허용하지 않습니다. 기존 브라우저의 보안 설정이나 Windows 전역 자동재생 정책을 바꾸지 않습니다.

## 자동 종료 안전 동작

실제 알람 시작 시각을 기준으로 N분 뒤 종료 확인을 시작합니다. 예약 시간이 지났다가 늦게 실행되더라도 지난 종료 시각을 즉시 실행하지 않습니다.

1. 키보드/마우스 입력을 알람 시작부터 매초 관찰합니다. 한 번이라도 이후 입력이 감지되면 종료 시까지 그 사실을 유지합니다.
2. 활동이 없고 최근 5분간 입력도 없다면 60초 종료 카운트다운을 표시합니다.
3. 입력이 있었거나 조회 실패·다른 사용자 세션 등 불확실성이 있으면 사용 중 확인 창을 표시합니다. **60초 동안 응답이 없으면 자동 종료를 취소**합니다.
4. 일반 카운트다운 중 새 입력이 들어오면 사용 중 확인 모드로 바꾸고 취소를 기본값으로 둡니다. 실제 종료 직전에도 새로 조회합니다.
5. 확인 창 닫기, Escape, 기본 Enter 동작은 취소입니다. 명시적으로 **지금 종료**를 누를 때만 확인 없이 정상 종료를 요청합니다.
6. 이미 진행 중인 자동 종료는 앱 재시작, 시스템 절전 진입·복귀, 세션 변경, 시계 변경·15초 넘는 처리 공백, 예기치 않은 UI 오류에서 취소합니다. 알람이 막 시작된 정상 Wake 통지는 새 종료 예약을 취소하지 않도록 구분합니다.

`GetLastInputInfo`는 해당 세션만 볼 수 있으므로 콘솔 세션과 다른 사용자 세션도 확인합니다. 다른 세션 조회 권한이 없으면 안전한 쪽으로 처리합니다. [GetLastInputInfo 공식 문서](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getlastinputinfo)

카운트다운 동안 OS에 별도 종료 명령을 예약하지 않습니다. 최종 시점에만 **강제 옵션 없는 `ExitWindowsEx(EWX_POWEROFF)`**를 호출하므로 앱이 응답하지 않거나 종료되면 카운트다운도 진행하지 않습니다. 다른 앱의 미저장 작업은 Windows 종료를 막을 수 있습니다. API 호출 뒤에는 Windows 종료 화면에서 취소해야 합니다. [ExitWindowsEx 공식 문서](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-exitwindowsex)

사용자 활동 감지는 작업 보존을 완전히 보장하지 않습니다. 입력 없이 오래 실행하는 작업은 감지할 수 없으므로 그런 용도로 PC를 쓸 때는 자동 종료 체크를 끄거나 예약을 취소하세요.

## 저장과 복구

기본 저장 위치는 `%LOCALAPPDATA%\WindowsAlarm`입니다.

```text
settings.json                  사용자 설정
schedule.json                  예약 ID/시각/설정 스냅샷/실행·종료 상태
*.corrupt-날짜                  읽지 못한 JSON의 보존 사본
logs\yyyy-MM-dd.log            시작, Wake, 재생, 오류, 활동, 종료 취소 기록
webview2\                     앱 전용 WebView2 프로필/캐시
youtube-content\              앱 내장 플레이어 페이지
```

JSON은 임시 파일에 flush한 뒤 교체합니다. 재생 시점에 저장이 실패해도 가능한 알람은 계속 시작합니다. 설정 스냅샷이 없으면 저장된 설정 또는 내장 경고음을 사용하고 자동 종료는 비활성화합니다.

미실행 예약은 2시간 이내라면 즉시 알람을 시작하고, 2시간을 넘겼으면 만료합니다. 비정상 종료된 재생 상태는 같은 유예 시간 안에서 소리만 복구하며 자동 종료는 취소합니다. 정지·완료·취소 상태는 다시 울리지 않습니다. JSON 자체가 해석 불가능해 예약 시각을 알 수 없으면 로그와 기본 설정을 표시하므로 다시 예약해야 합니다.

실행 경로를 바꾸려면 기존 폴더에서 **알람 예약 취소** 후 이동하고 새 위치에서 예약하세요. 이미 등록된 Windows 작업은 이전 실행 경로를 참조합니다. 앱 종료만으로 예약을 제거하지 않으므로 제거/삭제 전에도 알람 예약을 취소하세요.

별도 진단 프로필을 사용할 수 있습니다. 이 경로도 예약 작업 인수에 함께 저장됩니다.

```powershell
.\artifacts\app\WindowsAlarm.exe --data-dir "C:\AlarmDiagnostics"
```

여러 Windows 세션에서 동일 프로필로 동시에 설정하는 사용 방식은 지원하지 않습니다. 설정 파일에 URL과 경로가 평문 저장되므로 비밀번호나 인증 토큰을 넣지 마세요.

## 개인 계정으로 커밋·푸시

회사/개인 GitHub 계정이 함께 있는 환경을 위해 이 저장소의 작성자·SSH 키·원격 주소를 확인하는 훅과 게시 스크립트를 제공합니다. `JaehongLIM2` 인증 확인, 개인정보 점검, 커밋, main 푸시, 원격 SHA 확인을 순서대로 수행합니다. 전역 설정은 바꾸지 않습니다.

```powershell
.\scripts\publish-personal.cmd
```

설정 방법, 이메일 공개 범위, 점검 내용과 현재 환경의 실행 제한은 [보안 점검 기록](docs/SECURITY_REVIEW.md)에 설명했습니다. 저장소의 Windows 소유자가 다른 경우도 게시 스크립트 실행 중 해당 폴더만 임시로 신뢰합니다. 소유권 검사가 통과하는 환경에서는 일반 `git commit`, `git push`에도 저장소 훅이 적용됩니다.

## 테스트

자동 테스트는 외부 테스트 패키지 없이 실행하는 C# 콘솔 검증기입니다.

```powershell
.\test.cmd
.\test.cmd --integration
.\test.cmd --webview-integration
.\test.cmd --scheduler-process-integration
```

기본 검증에는 시간/DST 계산, URL·파일 검증, JSON 손상, 예약 실패 시 S4 차단, 알람 중복 방지, 복구, YouTube 실패/대기 중 정지, 자동 종료의 활동/무응답/예외 분기, WPF 창 렌더링·설정 저장이 포함됩니다. 전원/종료는 테스트 대역으로 검증하며 실제 PC를 종료하거나 절전시키지 않습니다.

`--integration`은 추가로 음소거 PCM 재생과 임시 Windows 예약 작업의 등록·조회·삭제를 시도합니다. 정상 오디오 장치와 작업 스케줄러 접근 권한이 필요합니다. 자체 고유 작업만 사용하며 실제 S4/종료는 수행하지 않습니다. 실패를 성공으로 간주하지 않고 종료 코드 1을 반환합니다.

`--webview-integration`은 실제 WebView2 Runtime과 내장 페이지를 사용하되 YouTube API 응답만 테스트 코드로 대체합니다. 상태 메시지, HTTPS 출처/Referer, 로컬 재생 전 플레이어 종료, 정지를 검증합니다. 실제 YouTube 서비스의 자동재생 성공이나 스피커 출력을 확인하는 검사는 아닙니다. Runtime이 제한된 샌드박스에서는 정상 사용자 세션에서 실행해야 합니다.

`--live-youtube-audio`는 앱에 저장된 YouTube URL을 읽어 실제 서비스를 사용합니다. 음소거한 테스트 전용 Alerts 플레이어까지 무음이 아닌 PCM이 전달되고 재생 시간이 진행되는지 확인합니다. 기본 테스트에서는 실행하지 않으며 인터넷과 재생 가능한 URL이 필요합니다. 실제 S4 진입·잠금·스피커 가청 여부는 확인하지 않습니다.

`--volume-integration`은 실제 기본 출력 장치에서 100 설정, 원래 볼륨·음소거 복원, 볼륨 0 및 음소거 해제를 검증합니다. 오디오를 재생하지 않으며 테스트 종료 시 기존 볼륨·음소거 상태로 복원합니다. 기본 테스트는 가짜 장치로 검증하여 Windows 볼륨을 변경하지 않습니다.

`--scheduler-process-integration`은 고유한 테스트 프로필에 미래 예약을 만들고 실제 Windows 작업을 두 번 실행합니다. 실행기 종료 후 GUI 유지, 창을 닫은 뒤 예약 유지와 다음 실행의 상태 복구를 확인하고 임시 작업을 삭제합니다. 알람 재생·최대 절전·PC 종료는 수행하지 않습니다.

시스템에 .NET 10이 없고 프로젝트 전용 SDK만 사용하는 환경에서는 먼저 `publish.cmd`를 실행한 뒤 아래처럼 자체 포함된 배포본을 지정해 예약 실행을 검증합니다. `--published-app`은 이 프로세스 통합 테스트의 실행 파일만 지정합니다.

```powershell
.\test.cmd --scheduler-process-integration --published-app=artifacts\app\WindowsAlarm.exe
```

수동 확인 순서:

1. **알람 테스트**: 실제 YouTube/MP3/내장 경고음과 알람 정지를 확인합니다. 자동 종료는 실행하지 않습니다.
2. **전원 상태 확인**: 현재 전원 조건과 필요한 설정을 확인합니다. Windows에서 설정을 바꿨다면 모달의 **새로고침**을 누릅니다.
3. 실제 S4 복귀를 확인하려면 작업을 저장하고, 자동 종료 토글을 끈 뒤 알람 시간을 현재보다 2~3분 뒤로 맞춰 **알람 설정 후 최대 절전**을 누릅니다. 실제 S4 진입·예약 시각 Wake·잠금 해제 전 소리를 확인합니다. 전원 상태에 따라 몇 초 이상 지연될 수 있습니다. 개발용 2분 예약 버튼은 일반 화면에서 제거했습니다.
4. Wake가 안 되면 직접 켠 뒤 `powercfg /lastwake`, `/waketimers`, 앱 로그, Windows 작업 스케줄러의 작업 실행 기록을 확인합니다.
5. 종료 흐름은 실제 알람 예약에서 자동 종료를 켜고 1분으로 설정해 확인합니다. 입력 후 확인 창의 무응답 취소와 수동 취소부터 검증하세요. 실제 종료까지 확인하려면 작업을 저장하고 별도 테스트 상태에서 진행하세요.

현재 환경의 실제 검증 결과와 남은 확인 항목은 [docs/VALIDATION.md](docs/VALIDATION.md)에 기록했습니다.

## 프로젝트 구조와 확장

```text
src/WindowsAlarm/
  App.xaml(.cs)                 시작, 단일 인스턴스, 예약 실행기
  MainWindow.xaml(.cs)          설정/상태/알람 UI
  ShutdownWindow.xaml(.cs)      기본 취소인 종료 확인 UI
  PowerStatusWindow.xaml(.cs)   읽기 전용 전원 상태·필수 설정 안내 모달
  Models/                       설정, 예약 상태, 시간 계산
  Services/AlarmCoordinator.cs 예약·복구·재생·종료 조정
  Services/TaskSchedulerWakeService.cs  작업 스케줄러 COM/XML
  Services/WindowsPowerService.cs       Win32 S4/정상 종료
  Services/WindowsActivityMonitor.cs    입력/세션 조회
  Services/ShutdownController.cs        종료 안전 상태 머신
  Services/AlarmPlaybackService.cs      YouTube 확인과 배타적인 로컬 전환
  Services/WebViewYouTubePlayer.cs      앱 소유 WebView2/상태 메시지
  Services/YouTubePlaybackHealth.cs     재생 진행/오디오 확인 및 시간 초과
  Assets/YouTubePlayer.html             공식 YouTube IFrame API 페이지
  Services/LocalAlarmPlayer.cs          MP3/내장 WAV/재시도
  Services/AppStore.cs                  JSON/로그
tests/WindowsAlarm.Tests/       실제 전원 동작 없는 검증기
```

설정과 실행 예약은 분리된 레코드이며 예약에 GUID가 있습니다. 요일/평일·주말/여러 알람은 시간 계산 정책과 예약 컬렉션, 작업 이름 정책을 확장하는 방식으로 추가할 수 있습니다. 현재 버전에는 반복 예약 UI가 없습니다.
