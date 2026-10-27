# 단일 EXE 배포

구현일: 2026-10-27. `publish-single.cmd`로 Windows x64용 `artifacts/single-file/WindowsAlarm.exe`를 만듭니다. 배포 대상은 이 파일 하나입니다. 폴더형 배포는 `publish.cmd`로 계속 만들 수 있습니다.

## 포함한 구성 요소와 기능

- .NET 10 런타임과 WPF, WinRT 프로젝션, 앱 코드, WebView2 관리 DLL과 네이티브 로더를 포함합니다. .NET 별도 설치가 필요하지 않습니다.
- YouTube HTML/PCM 전달 JavaScript는 기존 내장 리소스로 포함합니다. Windows에 설치된 WebView2 Runtime을 사용합니다. 브라우저 엔진 자체를 이 EXE에 넣지는 않습니다.
- 트리밍과 Native AOT를 사용하지 않아 WPF 리소스·리플렉션·COM·WinRT 기능을 제거하지 않습니다. 단일 파일 분석에 필요한 `Microsoft.NET.ILLink.Tasks`는 공식 SDK 빌드 도구이며 트리밍 활성화를 의미하지 않습니다.
- S4 Wake 예약/취소/복구, 로컬 MP3/경고음, 잠금 상태 Alerts 출력, YouTube PCM 전달과 Fallback, 볼륨 복원, 자동 종료 카운트다운/사용자 활동 보호 코드는 유지합니다.
- 설정, 예약, 로그와 브라우저 프로필은 `%LOCALAPPDATA%\WindowsAlarm`을 계속 사용합니다. 선택한 MP3 파일은 별도로 있어야 합니다.

## 단일 파일에 맞춘 경로 처리

`Assembly.Location` 대신 `Environment.ProcessPath`를 사용해 실제 실행한 EXE를 찾습니다. Windows 작업의 실행 경로와 예약 실행기의 GUI 재실행에 같은 처리를 적용했습니다. 파일명을 바꾸거나 다른 폴더로 복사한 EXE도 재실행할 수 있습니다. 테스트 호스트와 `dotnet` 개발 실행은 `AppContext.BaseDirectory`의 앱 호스트를 사용합니다.

`IncludeNativeLibrariesForSelfExtract=true`로 네이티브 의존성을 포함합니다. 실행 시 .NET이 사용자 임시 캐시에 필요한 파일을 자동으로 추출합니다. 모든 관리 파일을 디스크에 푸는 `IncludeAllContentForSelfExtract` 호환 모드는 사용하지 않습니다. 사용자가 별도 DLL을 직접 복사할 필요는 없습니다.

예약한 뒤 EXE를 옮기거나 이름을 바꾸면 Windows 예약 작업에는 이전 경로가 남습니다. 이때 앱을 새 위치에서 실행하고 다시 예약해야 합니다. EXE의 삭제나 잠긴 캐시 폴더까지 해결하는 방식은 아닙니다.

## 실행 파일 자체에서 수행한 검증

공백·한글·일본어가 있는 빈 폴더에 EXE 하나만 복사하고 파일명도 바꿔 실행했습니다. 다른 DLL과 설정 파일을 옆에 두지 않았습니다.

- 실제 WPF 창과 .NET 10.0.14 실행.
- 원래 EXE 경로 식별: 추출 캐시나 빌드 폴더를 실행 경로로 사용하지 않음.
- 내장 HTML/JavaScript 확인.
- Windows Alerts MediaPlayer API 생성 및 범주 설정. 소리는 재생하지 않음.
- 실제 WebView2 네이티브 로더·브라우저 컨트롤 초기화·페이지 → 앱 메시지 왕복. YouTube 서비스/오디오 없이 로컬 페이지로 검증.
- 실제 Windows 작업으로 이름을 바꾼 EXE를 두 번 실행. 실행기 종료 후 GUI 유지, 정상 닫기 후 미래 예약 유지, 다음 실행에서 복구, 임시 작업 삭제.

패키징 검사는 `--deployment-check --data-dir <전용 테스트 폴더>`로 실행합니다. `deployment-check.json`을 기록하고 성공은 0, 실패는 2로 종료합니다. 이 모드에서는 알람 복구·재생·볼륨 조절·전원 변경을 실행하지 않습니다. 보고서에는 실행 경로 등 로컬 정보가 있으므로 공개 소스나 배포 파일에 넣지 않습니다.

## 회귀 검증 결과

기본 62개 통과. 이름 변경·단일 파일 실행 경로 회귀 검증을 추가했습니다. 전체 통합 검증은 64개 통과·5개 실패입니다. 기존 .NET 8/10 폴더형 비교에서 확인한 오디오 장치/재생과 오디오 초기화에 영향을 받는 WebView2 통합 실패 5개와 같은 항목입니다. 실패를 무시하거나 성공으로 바꾸지 않습니다.

실제 S4 진입/복귀, 잠금 화면의 MP3·YouTube 가청 출력, 실제 종료는 이번 작업에서 수행하지 않았습니다. 단일 EXE에서 실제 WebView2 연결이 통과한 결과는 오디오 출력 성공을 뜻하지 않습니다. 집 PC에서 알람 시간을 현재보다 2~3분 뒤로 설정하고 일반 최대 절전 버튼으로 다시 확인해야 합니다.

검증 로그: `artifacts/single-file-build.txt`, `artifacts/single-file-publish.txt`, `artifacts/single-file-tests.txt`를 보관합니다. 당시 EXE 단독 진단 경로는 `artifacts/single-file-check/deployment-check.json`이었으며, 2026-10-27 기본 산출물 정리 후 최신 진단은 `artifacts/about-review/single-file-check/deployment-check.json`에 남겼습니다. 과거 프로필과 중간 출력은 [정리 기록](CLEANUP_CANDIDATES.md)에 따라 제거했고, 이 산출물은 Git에서 제외합니다.

공식 자료: [.NET 단일 파일 배포](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview), [.NET 10 네이티브 라이브러리 탐색](https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/10.0/native-library-search), [WebView2 배포](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution).
