# 둥근 UI와 나눔스퀘어 글꼴

변경일: 2026-10-27.

## 적용 범위

- 제공된 `nanum-square.zip`의 나눔스퀘어 OTF 4종(Light·Regular·Bold·ExtraBold)을 수정 없이 WPF 리소스로 포함합니다. Windows 글꼴 설치나 런타임의 네트워크 다운로드가 필요하지 않습니다.
- 버튼·입력창·선택 상자는 10px, 설정 카드는 16px 모서리를 사용합니다. 체크박스·선택 목록·포커스와 비활성 상태도 공통 스타일을 사용합니다.
- 메인 창, 종료 확인 창, YouTube 창의 앱 소유 영역에 같은 글꼴을 적용합니다. Windows가 그리는 제목 표시줄과 YouTube 콘텐츠 자체의 글꼴은 별도입니다.
- 설정 저장·알람 테스트·최대 절전 버튼을 하단에 고정합니다. 기존 설정 패널과 동일한 활성 상태를 바인딩하여 알람 중이나 자동 종료 확인 중 설정 동작이 다시 활성화되지 않게 합니다.
- 알람 상태·정지 버튼과 Wake Timer 취소·자동 종료 취소·로그·전원 확인·S4 테스트 동작을 유지합니다.
- 글꼴 저작권과 SIL OFL 1.1 원문을 내장하고 **글꼴 라이선스** 버튼으로 표시합니다. [출처와 해시](../src/WindowsAlarm/Assets/Fonts/README.md)를 기록했습니다.

전원 API, 예약·복구, 오디오 출력·전환, 볼륨 처리와 자동 종료 판단 로직은 변경하지 않았습니다.

## 나눔스퀘어 가독성 조정

글꼴 교체 후 이전의 작은 크기를 유지해 보조 설명이 가늘고 작게 보이던 부분을 조정했습니다. 아래 크기는 WPF의 DIP 값이며 pt 단위가 아닙니다.

| 용도 | 이전 | 조정 |
| --- | --- | --- |
| 본문·체크박스·상태 | 14 | 16 |
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
- 기존 기본 회귀 검증: 62개 통과·실패 0. 실제 WPF 창의 렌더링·설정 저장·정상 닫기와 종료 확인 창 닫기 시 취소 동작 포함.
- 실제 렌더링 이미지로 모서리, 글꼴, 입력창 높이와 하단 버튼 배치 확인.
- 단일 EXE 진단: 4개 굵기의 실제 글꼴 파일 URI, 굵기, 한국어 글리프와 내장 라이선스 확인. 시스템 글꼴로 대체된 상태를 성공으로 처리하지 않습니다. 기존 WPF·Alerts API·실제 WebView2 연결 진단도 통과.
- 최종 폴더형 실행본·단일 EXE·통합 설치 파일을 모두 갱신했습니다. 설치 파일 전용 검증 4개(동봉 패키지·버전 규칙, 런타임 정책, 설치용 앱 진단, 자기 작업만 제거하는 정리)도 통과했습니다. 실제 시스템 런타임 설치는 실행하지 않았습니다.
- 보안 검사 예외는 정확한 원본 글꼴 Git blob 4개로 한정합니다. 격리된 저장소에서 정상 파일·정상 인덱스 허용, 변조 파일·변조 인덱스 차단, 다른 바이너리 파일·인덱스 차단의 6개 검증을 통과했습니다.

이번 UI 검증에서는 실제 S4 진입·복귀, 잠금 중 소리, 정상 Windows 종료를 실행하지 않았습니다. 해당 실기 결과는 [기존 검증 기록](VALIDATION.md)과 구분합니다.

현재 UI 검증 이미지와 단일 EXE 진단은 Git에서 제외하는 `artifacts/ui-refresh`에 있습니다.
