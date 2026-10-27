# .NET 10 전환 기록

전환일: 2026-10-27. 앱과 검증기를 `net10.0-windows10.0.19041.0`으로 변경했습니다. Windows API 최소 대상은 유지하며, 기존 알람·잠금 상태 Alerts 출력·YouTube PCM 전달·자동 종료 보호 로직은 변경하지 않았습니다.

## SDK와 의존성

- `global.json`: .NET 10.0.100 이상 안정 SDK를 기준으로 `latestFeature` 적용. .NET 8이나 미리 보기 SDK로 잘못 빌드하지 않습니다.
- 검증 SDK는 Microsoft 공식 릴리스 메타데이터의 10.0.403, 런타임은 10.0.14입니다. 다운로드 ZIP의 SHA-512를 공식 값과 비교했습니다.
- 이 PC에는 .NET 10이 시스템 설치되어 있지 않아 무시되는 `artifacts/dotnet10`에만 SDK를 준비했습니다. 기존 시스템 SDK/런타임과 전역 PATH는 변경하지 않았습니다.
- 앱과 테스트 프로젝트, 패키지 잠금 파일의 대상 프레임워크를 함께 변경했습니다. WebView2 패키지 버전과 해시는 유지했습니다.
- NuGet 소스 매핑은 기존 Microsoft 패키지와 SDK가 자체 포함 배포 복원에 요구한 Windows x64용 공식 런타임/호스트 팩만 허용합니다. 실제 배포의 포함 프레임워크는 `Microsoft.NETCore.App`과 `Microsoft.WindowsDesktop.App`입니다.

## 배포 변경

`publish.cmd`는 Windows x64 자체 포함 폴더 배포를 만듭니다. `artifacts/app` 전체를 복사하면 실행 PC에 .NET 10을 별도 설치하지 않아도 됩니다. WebView2 Runtime은 별도로 필요하며 기존 Fallback 처리를 유지합니다.

단일 파일 배포와 트리밍은 사용하지 않습니다. 예약 실행기는 `Assembly.Location`으로 실행 경로를 구하고 WebView2는 네이티브 로더를 사용하므로 실제 파일을 유지합니다. 앱 PDB는 포함하지 않습니다. 다른 아키텍처용 배포와 설치 프로그램은 이번 범위에 포함하지 않습니다.

Windows 작업 스케줄러는 프로젝트 전용 SDK 환경 변수를 상속하는 실행 방식에 의존하지 않습니다. 프로세스 통합 검증에 `--published-app=artifacts\app\WindowsAlarm.exe`를 제공해 자체 포함 배포본을 실제 Windows 작업으로 실행했습니다.

## 검증

| 항목 | 결과 |
| --- | --- |
| Release 빌드 / 자체 포함 배포 | 성공, 경고 0·오류 0 |
| 기본 회귀 검증 | 61개 통과. .NET 8 형식 설정·미래 예약 JSON을 읽고 값/예약을 보존하는 검증 추가 |
| 실제 Windows 작업 등록·조회·삭제 | 통과 |
| 배포본 예약 실행, 실행기 종료 뒤 GUI 유지, 창 종료 뒤 미래 예약 유지와 재실행 | 통과, 임시 작업 삭제 |
| 배포본 직접 실행·중복 실행·정상 닫기 | 통과, 정상 종료 코드 0 |
| 배포본이 로드한 런타임 | 배포 폴더의 `coreclr.dll` 확인. 시스템 전역에는 .NET 6/8만 설치된 환경 |
| 전체 통합 검증 | 63개 통과, 5개 실패. 실패가 있으므로 종료 코드 1 |

실행 명령:

```powershell
.\build.cmd
.\publish.cmd
.\test.cmd --integration --webview-integration --scheduler-process-integration --volume-integration --published-app=artifacts\app\WindowsAlarm.exe artifacts\net10-final-validation
```

### 수정 전 .NET 8과 비교한 실패

직전 커밋 `bdc5c75`를 별도의 무시되는 폴더에서 시스템 .NET 8 SDK 8.0.403으로 빌드하고 같은 통합 옵션을 실행했습니다. .NET 8 결과는 62개 통과·5개 실패입니다. .NET 10은 JSON 호환 검증 1개를 추가했으며 아래 실패 항목 이름 5개가 모두 동일합니다.

- 실제 Windows 볼륨 장치: 기본 출력 장치 조회에서 `0x80070490`.
- 음소거 WPF PCM 출력: `0xC00D11BA`.
- 실제 Alerts 파일 출력: 재생 진행을 확인하지 못함.
- 실제 Alerts PCM 출력: 재생 진행을 확인하지 못함.
- 실제 WebView2 통합: Alerts 출력 초기화 실패 후 조기 Fallback으로 기대한 상태 표본을 받지 못함. 실제 웹 메시지/소리 검증 미완료.

일반 사용자 점검 프로세스에서 출력 장치 수는 0으로 조회됐고 오디오 서비스는 실행 중이었습니다. 이 비교에서는 .NET 전환으로 새로 추가된 실패가 없었으나, 실제 오디오 동작이 검증됐다는 의미는 아닙니다. 실패한 테스트를 건너뛰거나 성공으로 바꾸지 않았습니다.

실제 S4/Windows 종료, 잠금 상태 가청 출력, 실제 YouTube 음원 및 볼륨 복원은 이번 회사 PC 환경에서 확인하지 못했습니다. 이전 .NET 8 버전의 사용자 실기 성공은 .NET 10 실기 결과와 구분합니다. 집 PC에서 [README의 수동 테스트](../README.md#테스트)를 다시 수행해야 합니다.

빌드/배포 로그는 `artifacts/net10-build.txt`, `artifacts/net10-publish.txt`, 최종 검증은 `artifacts/net10-final-tests.txt`, 비교 기록은 `artifacts/net8-baseline-tests.txt`에 있습니다. SDK, 배포본, ZIP, 로그, 테스트 프로필은 Git에서 제외합니다.

## 공식 자료

- [.NET 9 변경 사항](https://learn.microsoft.com/en-us/dotnet/core/compatibility/9.0), [.NET 10 변경 사항](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0): WPF·직렬화·COM 및 배포 관련 항목 검토.
- [자체 포함 배포](https://learn.microsoft.com/en-us/dotnet/core/deploying/): 런타임 포함 배포와 파일 구조. 포함한 런타임의 보안 패치는 재배포로 반영해야 합니다.
- [공식 지원 일정](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core): .NET 10 LTS 지원 종료는 2028-11-14. Windows 버전별 지원 정책도 별도로 확인해야 합니다.
