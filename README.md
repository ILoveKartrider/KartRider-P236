# KartRider P236

프로토콜 236(한국 2005-12-14 빌드)을 위한 소스 전용 접속기와 독립 서버입니다.
서버는 원본 게임 파일 없이 빌드·실행되며, 접속기는 사용자가 직접 선택한
로컬 설치본의 접속 설정만 갱신합니다.

이 프로젝트는 [yanygm/Launcher_V2](https://github.com/yanygm/Launcher_V2)
(AFL-3.0)의 프로토콜·패킷·네트워크 구현 일부를 수정·재사용하고, P236용
서버·접속기와 배포 구조를 새로 구성한 파생 프로젝트입니다. 원본 Git 이력을
계승한 단순 포크는 아닙니다.

이 저장소에는 게임 클라이언트, 수정 실행 파일, PIN/RHO 데이터, 추출 리소스,
패킷 캡처, 디컴파일 결과, 디버거 데이터베이스, 개인 계정 DB와 실행 로그가
포함되지 않습니다. 그러한 파일은 이슈나 배포본에도 첨부하지 마세요.

## 구성

```text
src/
  KartRider.P236.Connector/     Windows 접속기
  KartRider.P236.Server/        프로토콜 236 서버 모듈
  KartRider.P236.Server.Host/   독립 실행 서버 호스트
tests/                          소스 경계 및 프로토콜 회귀 테스트
scripts/                        빌드·게시·공개 경계 검사
```

## 기능과 지원 상태

현재 구현 기능, 자동·수동 검증 범위, 아직 검증되지 않은 항목과 P236 클라이언트에
존재하지만 서버가 구현하지 않은 기능은 [`FEATURES.md`](FEATURES.md)에 구분해
정리되어 있습니다. 이 프로젝트의 대상은 프로토콜 236 한국 2005-12-14 빌드이며,
5136 또는 현대 클라이언트 지원을 포함하지 않습니다.

## 요구 사항

- 빌드: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- 접속기 실행: Windows x64와 .NET 8 Windows Desktop Runtime
- 서버 실행: 게시 대상 운영체제의 .NET 8 Runtime

클라이언트는 제공하지 않습니다. 접속기를 사용할 때는 본인이 적법하게 보유한
지원 설치본만 직접 선택해야 합니다.

## 빌드

PowerShell 7 또는 Windows PowerShell에서 저장소 루트를 기준으로 실행합니다.

```powershell
./scripts/Build.ps1
```

스크립트는 먼저 금지 파일과 로컬 비밀정보를 검사한 후 `restore`, `build`,
`test`를 차례로 수행합니다.

## 배포 패키지 만들기

```powershell
./scripts/Publish.ps1
```

기본 출력은 다음과 같습니다.

```text
artifacts/publish/connector/
artifacts/publish/server/
```

게시 결과는 framework-dependent입니다. 게임 클라이언트나 런타임 계정 데이터는
어느 패키지에도 복사되지 않습니다.

.NET 런타임까지 포함한 Windows x64 패키지가 필요하면 다음처럼 게시할 수 있습니다.

```powershell
./scripts/Publish.ps1 -SelfContained
```

프로젝트는 코드 서명 인증서나 개인 키를 포함하지 않습니다. 공개 바이너리를 배포하는
운영자는 자신의 코드 서명 절차를 적용하고, 사용자는 신뢰하는 소스에서 직접 빌드하거나
배포자의 서명을 확인해야 합니다.

## 서버 실행

게시된 서버 폴더에서 다음처럼 실행합니다. 같은 PC에서 접속할 때는 기본값만으로도
실행할 수 있습니다.

```powershell
./KartRider.P236.Server.Host.exe
```

기본 TCP/UDP 포트는 모두 `39312`이며, 기본 수신 주소는 `127.0.0.1`입니다. 신뢰하는
LAN에서 다른 장치의 접속을 허용하려면 수신 주소와 클라이언트에 알릴 주소를 모두
명시하고 운영체제 방화벽을 직접 구성해야 합니다.

```powershell
./KartRider.P236.Server.Host.exe --bind 0.0.0.0 --advertise 192.0.2.10 `
  --data ./data --logs ./logs
```

`--bind`, `--advertise`, `--tcp-port`, `--udp-port`, `--data`, `--logs`를 지원하며
`--help`에서 전체 목록을 볼 수 있습니다. 전체 패킷 hex 로그는 민감할 수 있으므로
기본적으로 꺼져 있고, 명시적으로 `--trace`를 전달할 때만 기록됩니다. 런타임
프로필과 옵저버 설정은 `data/` 아래에 생성되고 Git에서 제외됩니다.

이 호환 프로토콜에는 현대적인 인증·암호화·접속 제한이 없습니다. 서버를 인터넷에
직접 노출하지 말고, 로컬 또는 신뢰하는 격리 LAN에서만 사용하세요.

## 접속기 사용

접속기에서 본인이 보유한 클라이언트 디렉터리, 서버 IPv4 주소, 로그인 TCP
포트, 사용자명과 인스턴스별 문서 저장 루트를 지정합니다. 접속기는 PIN/XML
갱신을 트랜잭션으로 수행한 뒤 나머지 실행 준비를 진행합니다.

1. 지원되는 실행 파일과 PIN 형식을 검증합니다.
2. PIN/XML의 서버 endpoint와 문서 저장 루트를 갱신합니다.
3. `Profile/launcher.xml`에 사용자명을 기록합니다.
4. 선택한 인스턴스와 일치하는 mutex만 해제한 뒤 게임을 실행합니다.
5. PIN/XML 갱신 검증에 실패하면 두 파일을 백업에서 복구합니다.

지원되는 원본 format-v1 PIN은 첫 설정 저장 시 접속기가 format-v2로 승격합니다.
암호화·압축 플래그와 공통 헤더는 유지하며, 실제 PIN 파일을 저장소나 이슈에
첨부할 필요가 없습니다. 이미 준비된 format-v2 설치본은 같은 경로에서 갱신됩니다.

더 높은 권한으로 실행된 게임 프로세스의 handle을 검사할 수 없다는 오류가 나면
접속기를 관리자 권한으로 다시 실행해야 할 수 있습니다. 접속기는 기본적으로
현재 사용자 권한(`asInvoker`)으로 실행됩니다.

설정만 저장하는 명령행 모드도 제공합니다.

```powershell
./KartRider.P236.Connector.exe --configure `
  <client-root> <server-ipv4> <login-port> <storage-root>
```

## 데이터와 업데이트

- 계정은 접속기에서 지정한 `username`을 키로 하여 JSON에 영속 저장됩니다.
- `data/`, `logs/`, `Profile/`은 런타임 상태이므로 커밋하지 않습니다.
- 기능 변경 전후에는 `scripts/Test-SourceBoundary.ps1`과 전체 테스트를 실행하세요.

## 라이선스와 고지

이 저장소의 소스는 Academic Free License 3.0(AFL-3.0)으로 배포됩니다.
자세한 출처와 수정 고지는
[`NOTICE.md`](NOTICE.md), [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md),
[`LICENSE.md`](LICENSE.md)를 확인하세요. 콘텐츠 정책은 [`LEGAL.md`](LEGAL.md)에
정리되어 있습니다.
