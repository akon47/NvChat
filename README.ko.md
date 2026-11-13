<div align="center">

# NvChat

**[build.nvidia.com](https://build.nvidia.com) 의 무료 LLM 들을 골라 채팅하는 Windows 데스크톱 앱**

스트리밍 채팅 · 대화 저장 · 실시간 마크다운/코드 렌더링 · 추론(reasoning) 표시 · 이미지 첨부 · 인앱 자동 업데이트 — 단일 `.exe` 하나로 실행

[English](README.md) · **한국어**

[![build](https://github.com/akon47/NvChat/actions/workflows/build.yml/badge.svg)](https://github.com/akon47/NvChat/actions/workflows/build.yml)
[![release](https://img.shields.io/github/v/release/akon47/NvChat?label=release&color=76B900)](https://github.com/akon47/NvChat/releases/latest)
[![downloads](https://img.shields.io/github/downloads/akon47/NvChat/total?color=76B900)](https://github.com/akon47/NvChat/releases)
[![stars](https://img.shields.io/github/stars/akon47/NvChat?style=flat&color=76B900)](https://github.com/akon47/NvChat/stargazers)
[![license](https://img.shields.io/github/license/akon47/NvChat)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![platform](https://img.shields.io/badge/platform-Windows-0078D6)

<img src="docs/screenshot.ko.png" alt="NvChat 스크린샷" width="880">

<br>

<img src="docs/demo.ko.gif" alt="실시간 마크다운 스트리밍" width="720">

<sub>답변이 스트리밍되는 도중에도 마크다운이 실시간으로 렌더링됩니다.</sub>

</div>

---

## ✨ 특징

- **모델 선택** — `/v1/models` 로 실제 사용 가능한 모델 목록을 불러와 상단에서 선택(실패 시 대표 모델로 폴백)
- **스트리밍 채팅** — 토큰 단위 실시간 표시, **중단** 버튼으로 즉시 취소
- **스트리밍 중 실시간 마크다운** — 답변이 오는 도중에도(클로드/챗GPT처럼) 서식이 바로 렌더링, 완료 후가 아님
- **추론(reasoning) 표시** — `reasoning_content` / `<think>` 지원 모델(deepseek-r1 등)의 사고 과정을 접이식으로 표시. 본문에 섞여 오는 `<think>` 도 스트리밍 중에 바로 갈라낸다
- **응답 버전** — 다시 생성하거나 메시지를 편집해도 이전 답변이 사라지지 않는다. `‹ 2 / 3 ›` 로 버전을 오가고, 버전마다 그 뒤에 이어진 대화도 함께 보존
- **동시 대화** — 한 대화에서 답변을 받는 동안 다른 대화에서 계속 채팅. 대화마다 따로 스트리밍
- **이미지 첨부(비전 모델)** — 이미지를 첨부해 비전 모델(llama-3.2-vision 등)에 질문 (자동 축소 후 전송)
- **인앱 자동 업데이트** — GitHub Releases 를 확인해 새 `.exe` 를 내려받고 SHA-256 검증 후 자기 자신을 교체(단일 파일 방식, 설치 관리자 불필요). 체크섬을 확인할 수 없으면 교체하지 않는다
- **대화별 사용량** — 요청 수와 입력/출력 토큰을 **모델별로** 집계. 대화 도중 모델을 바꿔도 모델마다 얼마나 썼는지 확인 가능. 답변마다 어느 모델이 썼는지, 걸린 시간과 초당 토큰도 표시
- **컨텍스트 한도** — 대화가 길어져 모델의 컨텍스트 창을 넘길 것 같으면 가장 오래된 메시지부터 요청에서 빼고(저장된 대화는 그대로) 몇 개를 뺐는지 알려 준다
- **데스크톱 런처** — 전역 단축키(기본 `Ctrl+Shift+Space`, 설정에서 원하는 조합을 직접 눌러 지정)로 어디서든 뜨는 **빠른 채팅 미니창** + **시스템 트레이**(닫으면 트레이로 최소화), **Windows 시작 시 자동 실행**(선택), 창을 보고 있지 않을 때 답변이 끝나면 알림
- **개인화** — 모든 대화에 자동 적용되는 **커스텀 지침**(나에 대해 / 응답 방식) + 재사용 **프롬프트 프리셋**
- **검색 가능한 모델 선택기** — 100개+ 모델을 타이핑으로 필터링해 선택
- **메시지 액션** — 응답 **다시 생성**, 사용자 메시지 **편집 후 재생성**, 개별 **삭제**(**실행 취소** 가능), **복사**. 실패한 요청은 배너에서 바로 **다시 시도**(요청 한도에 걸렸으면 카운트다운 표시)
- **선택 가능한 마크다운** — 제목/중첩·번호 목록(항목 안의 코드블록 포함)/체크리스트/인용/**표**/링크·맨 URL, **수식**(`$…$`, `\(…\)`, `$$…$$` 를 읽기 쉬운 유니코드로), **구문 강조된 코드블록**(언어별 규칙, 복사·줄바꿈 전환). 모든 메시지 텍스트를 드래그로 선택 가능
- **안전한 링크** — 모델 출력의 링크는 `http(s)`/`mailto` 만 눌린다. `file:`·UNC·사용자 지정 프로토콜 링크는 일반 텍스트로 표시
- **대화 내 찾기** — `Ctrl+F` 로 일치하는 곳을 강조하고 차례로 이동
- **대화 관리** — 모든 대화를 훑는 사이드바 **검색**, **날짜별 그룹**(오늘/어제/…), **고정**, **이름 변경**, 자동 저장, 모델 기반 **제목 자동 생성**, 대화별 **입력 초안**, 삭제 **실행 취소**
- **대화별 설정** — 시스템 프롬프트, Temperature/Top P/Max Tokens/Penalty 조절
- **내보내기·백업** — 대화 전체 복사 / Markdown 파일로 저장(추론·이미지 포함), 모든 대화를 JSON 으로 백업하고 다시 가져오기
- **이미지 붙여넣기·드롭** — `Ctrl+V` 로 스크린샷을 붙여넣거나 이미지 파일을 채팅 영역에 끌어다 놓으면 첨부
- **편의** — 대화 우클릭 메뉴(고정 · 이름 변경 · 삭제), 복사/내보내기 토스트, 닫을 수 있는 상태 배너, 맨 아래로 스크롤 버튼, 창 크기/위치 기억, 사이드바 접기, 설정 창에 정리된 단축키 목록, 트레이 최초 안내, 주기적 업데이트 확인
- **안전한 저장** — API 키는 Windows DPAPI 로 암호화(설정 창에서는 가려서 표시), 대화마다 한 파일로 백그라운드에서 원자적으로 저장, 손상된 파일은 지우지 않고 따로 보존
- **다국어** — 영어 · 한국어 UI, 설정에서 전환 (첫 실행 시 Windows 언어로 자동 선택)
- **테마·접근성** — **다크 / 라이트 / 고대비**(또는 Windows 설정 따르기), **확대/축소** 80~160%(`Ctrl+=` / `Ctrl+-`), 아이콘 버튼의 화면 읽기 프로그램용 이름, 모든 동작에 키보드로 접근 가능
- **UI** — DWM 둥근 모서리·Windows 11 스냅 레이아웃·키보드 포커스 링을 갖춘 커스텀 타이틀바, NVIDIA 그린 액센트, 챗GPT/클로드식 레이아웃(말풍선은 내 메시지에만), 끌어서 폭을 바꾸고 좁은 창에서 자동으로 접히는 사이드바, 부드러운 스크롤과 전환, 대화상자·메뉴·트레이 메뉴까지 테마 일관 적용

## 📦 다운로드

[**Releases**](https://github.com/akon47/NvChat/releases/latest) 에서 `NvChat.exe` **파일 하나**만 내려받아 실행하면 됩니다.
.NET 런타임이 없어도 실행되는 self-contained 단일 실행 파일입니다. (Windows x64)

> **첫 실행:** 코드 서명이 없어 Windows SmartScreen 이 "Windows의 PC 보호" 창을 띄울 수 있습니다. **추가 정보 → 실행**을 누르세요.

> 자동 업데이트가 포함된 버전부터는 새 버전이 앱 안에서 안내되므로 다시 수동으로 받을 필요가 없습니다.

## 🔑 API 키 발급

1. [build.nvidia.com](https://build.nvidia.com) 에 로그인
2. 아무 모델 페이지에서 **Get API Key** 로 `nvapi-...` 키 발급
3. 앱 첫 실행 시 뜨는 **설정** 창(또는 우상단 ⚙)에 키를 붙여넣고 **연결 테스트 → 저장**

> 키는 **이 PC + 이 Windows 계정에만** DPAPI 로 암호화되어 저장됩니다(`%APPDATA%\NvChat\settings.json`). 다른 PC/계정에서는 복호화되지 않습니다.

## ⌨️ 단축키

| 키 | 동작 |
|---|---|
| `Enter` | 전송 (설정에서 `Ctrl+Enter` 전송으로 변경 가능) |
| `Shift+Enter` | 줄바꿈 |
| `Ctrl+V` | 클립보드 이미지 붙여넣기 (이미지 파일을 채팅 영역에 끌어다 놓아도 첨부) |
| `Esc` | 생성 중단 |
| `Ctrl+N` | 새 대화 |
| `Ctrl+F` | 이 대화에서 찾기 |
| `Ctrl+Shift+F` | 모든 대화에서 검색 |
| `F2` | 선택한 대화 이름 변경 |
| `Ctrl+B` | 사이드바 토글 |
| `Ctrl+M` | 모델 선택 열기 |
| `Ctrl+R` | 마지막 응답 다시 생성 |
| `Ctrl+Shift+C` | 대화 전체 복사 |
| `Ctrl+E` | Markdown 으로 내보내기 |
| `Ctrl+=` / `Ctrl+-` / `Ctrl+0` | 확대 / 축소 / 원래 크기 |
| `Ctrl+,` | 설정 |
| `Ctrl+Shift+Space` | 어디서든 빠른 채팅 (전역 · 설정에서 변경) |

## 🛠️ 소스에서 빌드

```powershell
# 개발 실행
dotnet run --project NvChat/NvChat.csproj

# 단일 exe 배포 빌드 (파일 하나 생성)
dotnet publish NvChat/NvChat.csproj -c Release
# → NvChat/bin/Release/net10.0-windows/win-x64/publish/NvChat.exe
```

요구 사항: .NET 10 SDK (Windows).

```powershell
# 단위 테스트 (마크다운 분할, 구문 강조, 제목 정리, <think> 분리, 컨텍스트 계산, 링크 검증 등)
dotnet test NvChat.Tests/NvChat.Tests.csproj
```

실제 데이터를 건드리지 않고 빌드를 시험하려면 데이터 폴더를 따로 지정한다: `$env:NVCHAT_DATA_DIR = "C:\temp\nvchat-test"`.

## 🚀 릴리스 (자동 배포)

`v*` 형식의 태그를 푸시하면 GitHub Actions([`release.yml`](.github/workflows/release.yml))가
단일 `NvChat.exe` 와 `NvChat.exe.sha256` 을 빌드해 해당 태그의 Release 에 자동 업로드합니다.
앱의 인앱 업데이터가 이 체크섬으로 다운로드를 검증합니다.

```bash
git tag v1.0.0
git push origin v1.0.0
```

## 🧩 기술

.NET 10 / WPF (`net10.0-windows`). CommunityToolkit.Mvvm 소스 생성기 기반 MVVM(코드비하인드 이벤트 핸들러 없이 첨부 동작으로 구성), 커스텀 `WindowView`(WindowChrome 다크 타이틀바 + DWM 둥근 모서리 + Windows 11 스냅 레이아웃),
바꿔 끼우는 다크/라이트/고대비 팔레트, 자체 마크다운 렌더러/구문 강조기를 외부 UI 라이브러리 없이 구현했습니다.
메시지 목록은 가상화되어 있고 마크다운은 바뀐 블록만 다시 그려서, 긴 대화와 긴 스트리밍 답변에서도 가볍게 동작합니다.

- 엔드포인트: `https://integrate.api.nvidia.com/v1` (OpenAI 호환, 설정에서 변경 가능)
- 데이터 저장: `%APPDATA%\NvChat\` — `settings.json` 과 `conversations\{id}.json`(대화마다 한 파일). 1.8 이하의 `conversations.json` 은 첫 실행 때 옮겨지고 `conversations.legacy.json` 으로 남습니다.
- 무료 티어에는 NVIDIA 측 요청 한도가 있어, 429 응답 시 배너에 다시 시도할 수 있는 시점이 표시됩니다.

## 📄 라이선스

[MIT](LICENSE) — 자유롭게 사용/수정/배포할 수 있습니다.
