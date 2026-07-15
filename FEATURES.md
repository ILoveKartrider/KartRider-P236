# 기능 및 검증 상태

이 문서는 현재 저장소의 소스를 기준으로 한 기능 목록입니다. 대상은
**프로토콜 236, 한국 2005-12-14 클라이언트**이며, 5136 또는 현대 클라이언트
호환성을 의미하지 않습니다.

게임 클라이언트, 패킷 캡처, 디컴파일 결과와 그 밖의 분석 자료는 이 저장소에
포함되지 않습니다.

## 상태 표기

| 표기 | 의미 |
| --- | --- |
| `자동 검증` | 저장소의 자동 테스트가 해당 동작을 직접 검사합니다. |
| `개발 중 수동 확인` | 기능 개발 당시 실제 P236 클라이언트로 확인했습니다. 현재 공개용 저장소와 게시 패키지를 대상으로 다시 수행한 인수 테스트는 아닙니다. |
| `정적 구현` | 요청 파싱, 상태 변경과 응답 경로가 코드에 있지만 실제 클라이언트 회귀 테스트가 없습니다. |
| `부분 구현` | 호환에 필요한 최소 응답, 중계 또는 임시 상태만 구현되어 있습니다. |
| `미구현` | 클라이언트 경로는 확인되지만 서버가 처리하지 않거나 명시적으로 무시합니다. |

`구현`과 `검증`은 별개입니다. 예를 들어 방 생성 코드는 구현되어 있고 개발 중
실제 클라이언트로 확인했지만, 현재 자동 테스트는 방 패킷을 실행하지 않습니다.

## 구현된 기능

### 접속기

- 한국어 WinForms UI와 설정 전용 `--configure` 명령행 모드
- 사용자가 선택한 설치 경로의 필수 파일·폴더 존재 확인과 `KartRider.exe`,
  `KartRider.pin`, `KartRider.xml` 검증
- 알려진 원본 P236 실행 파일의 SHA-256 일치 여부 확인
- 원본 format-v1 PIN 읽기 및 format-v2 승격
- 기존 format-v2 PIN 갱신
- PIN의 압축·암호화 플래그, 암호 키와 공통 헤더를 유지하면서 로그인 IPv4,
  TCP 포트와 문서 저장 루트 변경
- PIN과 `KartRider.xml`을 함께 갱신하고, 실패 또는 중단 시 백업에서 복구하는
  트랜잭션
- 게임 실행 경로에서 `Profile/launcher.xml`의 username 생성·갱신
- 인스턴스별 문서 저장 루트 지정. `clients/clientN` 관례에서는
  `카트라이더_236_clientN`을 추천하고 알려진 sibling의 중복 루트를 차단
- 필요한 문서 하위 폴더 생성, active HKCU gamepath와 관례 기반 인스턴스
  레지스트리 기록
- 접속기 폴더 자체와 직계 `clients/*` 자동 검색, `connector-instances.json`의
  이동 가능한 상대 경로 목록 저장과 마지막 선택 복원
- 검증된 클라이언트 프로세스에서 object name이 `\CR-KartRider`로 끝나는
  mutex handle만 해제
- 실행 중 프로세스의 경로·사용자 identity를 안전하게 확인할 수 없으면 변경 전에
  중단하는 fail-closed 동작. 개별 handle을 읽지 못한 경우에는 그 handle만 건너뜀
- `KartRider.exe -profile:launcher` 실행과 portable catalog 우선·레지스트리 fallback
  방식의 준비 인스턴스 검색

합성 PIN의 v1→v2 변환·멱등성·백업 정리, 반쪽 커밋 복구, portable catalog의
상대 경로 이동·중복 우선순위·손상 파일 fallback과 일부 fail-closed guard는 자동
검증됩니다. 실제 원본 PIN, WinForms UI, 레지스트리, 관리자 권한 mutex 해제와 게임
실행을 연결한 현재 배포본의 end-to-end 테스트는 아직 없습니다.

접속기는 클라이언트 사본을 만들지 않고 사용자가 미리 준비한 설치본만 선택합니다.
`설정만 저장` UI와 `--configure`는 PIN/XML의 endpoint·저장 루트만 갱신하며,
username·레지스트리·mutex·게임 실행은 처리하지 않습니다. 또한 설정 트랜잭션의
rollback 범위는 PIN/XML이고, username profile·레지스트리·mutex와 process start는
포함되지 않습니다.

### 서버 런처와 아이템 확률 설정

- 기존 headless 서버 호스트와 별도로 Windows x64 WinForms 서버 런처 제공
- bind/advertise IPv4, TCP/UDP 포트, 데이터·로그 디렉터리와 packet trace 설정 및
  in-process 서버 시작·중지
- 개인 아이템전, 팀 아이템전과 개인·팀 플래그 공용 표의 `highrank`, `midrank`,
  `lowrank` 상대 가중치를 `data/item-probabilities.json`에 저장
- 개인·팀 아이템전의 별도 bonus 표를 단일 상대 가중치로 저장
- 사용자가 선택한 P236 `Data`에서 아이템 ID·이름·현재 가중치를 가져오고, 편집한
  다섯 표를 `item.rho`와 `aaa.pk`에 명시적으로 함께 적용
- 대상 archive의 표·행 집합 검증, 원자적 임시 파일 교체, 백업과 중단 복구 marker를
  이용한 두 파일 트랜잭션

P236 추첨은 클라이언트 로컬 동작이므로 서버 시작만으로 JSON이 적용되지는 않습니다.
모든 참가 클라이언트가 같은 설정을 적용하고 다시 시작해야 합니다. bonus 표가 두 번째
추첨 bank로 로드되는 것은 정적으로 확인했지만, 클라이언트가 이 bank를 선택하는 정확한
조건은 확인되지 않았습니다. JSON 검증과 합성 RHO/BML 갱신은 자동 검증 대상이지만,
개발 중 실제 원본 P236 두 파일의 임시 복사본에도 적용·백업·전체 semantic 재검증과
재가져오기를 통과했습니다. 실제 클라이언트 부팅과 게임 내 분포는 아직 수동 검증이
필요합니다.

### 접속·프로필·채널

- P236 첫 메시지, IV, 암호화 frame과 checksum 처리
- 로그인 profile에서 username을 읽어 JSON 계정에 연결
- ping, machine-info ACK, rider profile과 고정 호환 인벤토리 응답
- username을 키로 하는 UserNo, 닉네임, 라이선스, RP/Lucci와 장착품 JSON 저장
- 임시 파일, flush와 원자적 교체를 이용한 저장 및 손상 JSON 격리
- 라이선스 level 0~4(4=L1)와 단계별 여섯 개 completion mask를 JSON에 저장하고 profile에 반영
- 캐릭터, 페인트, 카트, 번호판, 고글, 풍선과 머리띠 장착 저장 및 방에 반영
- novice, rookie-intro, rookie, L3, L2, arena와 event 채널 목록 및 속도 preset
- TCP/UDP 세션의 UserNo 결합, UDP echo와 클라이언트가 요청하는 time-sync 응답
- 기본 패킷 trace는 끄고 `--trace`에서만 제한된 길이로 기록

frame codec, 제한된 로그인 profile 파싱, JSON 기본 round-trip, 라이선스 level 0~4와
여섯 개 completion mask의 저장·profile 반영, 서버 lifecycle은 자동 검증됩니다.
실제 L1 UI와 재접속 유지, 전체 장착 필드와 실제 UDP 초기화 순서는 자동 테스트 대상이
아닙니다. 기본 P236 클라이언트에는 완전한 L1 UI·트랙 자산이 없어 호환 RHO 패치가
필요합니다.

### 방과 대기실

- 채널별 실제 방 목록과 페이지당 8개 pagination
- 방 생성, 비밀번호, 입장, 퇴장과 빈 방 정리
- 8개 rider 슬롯과 8개 observer 슬롯
- 방 session/slot 초기화, 다른 rider의 프로필·장착·UDP endpoint 전파
- 준비, 준비 취소, 치장 상태, 맵 변경과 팀 변경
- 팀 모드 입장 시 red/blue 자동 균형 배정 및 팀당 최대 4명 제한
- 방장, 최소 2명, 일반 rider 전원 준비와 팀 인원 동수 확인 후 시작
- observer를 준비·최소 인원·순위 계산에서 제외

방 생성·목록·입장·퇴장, 두 클라이언트의 같은 방 입장, 준비/취소, 장착·맵 변경과
방장 시작은 개발 중 실제 클라이언트로 확인했습니다. 비밀번호 방, pagination,
최대 인원과 접속 중단 조합은 별도 검증이 필요합니다.

### 경기 공통 흐름

- 개인 스피드, 개인 아이템, 팀 스피드와 팀 아이템 방 흐름
- 모든 rider의 로딩 완료를 기다린 뒤 동기화 시작
- 시작 컷신 4초와 클라이언트 기본 3초 카운트다운을 합친 start tick
- 출발 grid 배정, 완주 시간 수집, 10초 결과 대기와 결과 화면 전환
- 개인 순위, 팀 스피드 순위 점수, 팀 아이템 첫 완주 팀 판정
- 결과 순서를 다음 경기 grid에 적용
- observer가 없으면 접속 중인 최고 순위 rider에게 경기 후 방장 이전
- 경기 종료 후 같은 방으로 복귀하고 TCP 연결 유지

두 클라이언트의 입장, 로딩 대기 해제, 경기 시작·완주·결과 후 방 복귀는 개발 중
수동 확인했습니다. 정확한 4초 타이밍, 경기 후 방장 이전과 다음 grid 순서는 마지막
변경 뒤 별도로 재검증하지 않았습니다.

### 아이템전과 팀 부스터

- 아이템 박스 충돌 event를 송신자에게도 되돌려 클라이언트의 로컬 추첨 시작
- Cloud, Banana, Shield, Siren, SirenShield, AreaUfo, ForceZone, Rocket,
  Waterfly, Magnet, Waterbomb, Ufo, Devil, Angel, Emp, Timebomb, Balloon과
  HeadBand 동작을 대상 클라이언트에 중계
- 클라이언트가 카트 효과로 생성하는 특수 `CloudBlack` 동작을 같은 경로로 중계
- 플래그전 전용 Ghost, Mine과 Rollingbomb 동작을 같은 검증 경로로 중계
- sender slot, 경기 상태, 수신 대상 mask와 item mode 여부 기본 검증
- 클라이언트가 보고한 최대 2개 보유 아이템을 진단용 shadow state로 유지
- 팀 스피드전 booster 기여량을 팀 인원으로 정규화하고 양 팀 gauge 동기화

아이템 박스 획득, 일부 공격·설치 아이템의 상대 클라이언트 표시와 효과, 팀 booster는
개발 중 수동 확인했습니다. 서버 런처 설정을 적용하면 각 클라이언트의 로컬 추첨
가중치를 바꿀 수 있지만 서버가 아이템을 직접 추첨하거나 효과를 시뮬레이션하지는
않습니다. 따라서 서로 다른 클라이언트 설정을 서버가 교정하지 못하며, 보유 아이템과
사용 요청의 일치도 강제하지 않습니다.

### 플래그전

- 개인 플래그전과 팀 플래그전 방·시작 흐름
- flag pickup, drop/return, 소유자 shadow state와 개인 점수 중계
- 팀 점수 동기화, 180초 종료와 결과 정산
- 동점 시 native state 6 연장전 전환과 연장 득점 후 정산
- 개인 플래그 점수 기반 순위와 팀 플래그 승리 팀 결과

방 생성·시작, 아이템과 최초 flag 획득은 개발 중 수동 확인했습니다. flag 뺏기,
재생성 flag, 개인 순위, 팀전 시간 종료·결과 화면과 연장전은 개발 중 여러 차례
수정된 영역이며, 현재 소스의 전체 흐름은 다시 회귀 테스트해야 합니다.

### 옵저버

- `data/observers.json`의 username 목록과 기본 `observer` 계정
- observer 로그인 flag와 전용 슬롯 8~15
- 기존 방에 들어온 observer의 방장 권한 획득
- 준비, 팀 인원과 경기 순위에서 제외
- 진행 중 경기의 start/flag score 동기화와 아이템 event 수신
- 경기 종료 후 남아 있는 observer의 방장 권한 우선 유지

이 경로는 정적으로 구현되어 있지만 마지막 방장·준비 규칙 변경 뒤 실제 클라이언트로
전체 흐름을 확인하지 않았습니다.

## 자동 검증 범위

현재 자동 테스트는 총 59개 case입니다.

- 접속기 12개: 합성 format-v1 PIN의 flags `0/1/2/3` 변환, 헤더·암호 키 보존,
  BOM 없는 레거시 XML과 원본 PIN/XML endpoint 불일치의 최초 준비, endpoint·저장 루트
  갱신, 멱등성·백업 정리, 반쪽 커밋 복구, portable 상대 경로 이동·중복 우선순위·
  손상 catalog fallback과 접근 불가 프로세스 fail-closed
- 서버 18개: clear/encrypted frame round-trip, IV 진행, 잘못된 checksum 거부,
  제한된 username 파싱, 라이선스 level 0~4와 여섯 개 completion mask의 저장·profile
  반영, JSON reopen, loopback 기본값, IPv6 거부, TCP/UDP 시작·종료, 동시 start/stop,
  disconnect cleanup과 플래그전 전용 아이템 GameSlot relay
- 아이템 확률 29개: JSON round-trip·검증, 다섯 확률표 import/apply, 전체 RHO/aaa
  semantic 보존, KRData 암호화 mode/key 보존, 원본 pair·외부 변경 fail-closed,
  원자적 백업·멱등성·중단 복구
- 별도 공개 경계 검사: 클라이언트·분석물·비밀정보·대형/바이너리 파일 차단과
  publish 출력 경로·reparse point 방어

새 relay 테스트는 team-flag room과 두 socket session을 만들고, 실제 로그에서 확인한
CloudBlack, Ghost, Mine 두 형태와 Rollingbomb envelope가 byte 단위로 상대에게만
전달되는지와 잘못된 base type 차단을 검사합니다. 실제 게임 클라이언트의 item 효과, observer,
UDP echo/time-sync packet과 접속기 UI·mutex·registry·process launch는 자동 테스트에서
실행하지 않습니다.

## 아직 검증되지 않았거나 검증이 약한 점

- 공개용 저장소로 분리한 현재 소스와 게시 패키지에 대한 실제 P236 클라이언트
  end-to-end 인수 테스트
- 실제 원본 format-v1 PIN 승격, 관리자 권한이 다른 실행 프로세스의 mutex 해제,
  WinForms UI를 통한 실제 게임 실행과 CLI를 통한 실제 설정 적용
- 서버 런처의 실제 시작·중지 UI와 GUI apply, 적용한 원본 P236 Data로 클라이언트
  부팅, 상위·중위·하위 rank별 게임 내 추첨 분포 및 여러 PC의 동일 설정 적용
- 개인·팀 bonus 추첨 bank가 실제로 선택되는 조건과 그 가중치의 게임 내 효과
- 독립된 기존 format-v2 fixture의 미지 필드 보존과 실제 중단 복구·rollback
- 각 인스턴스의 username, PIN endpoint와 문서 루트가 실제 게임에서 동시에 분리되는지
  확인하는 3개 이상 클라이언트 테스트
- L3/L2 채널별 속도 차이 전체 비교; L2 한 경기만 약하게 확인됨
- 호환 RHO 패치를 적용한 클라이언트에서 L1 UI, 여섯 개 completion mask와 재접속 유지
- 비밀번호 방, 8명 rider/8명 observer, pagination과 방장 연결 종료
- 시작 컷신 4초, 우승자 방장 이전과 직전 결과 기반 다음 grid
- 로딩 중·경기 중 연결 종료, 재접속, 아무도 완주하지 않는 경기의 처리
- 지원 목록의 모든 아이템을 개별 사용한 교차 클라이언트 검증
- 새 Ghost/Mine/Rollingbomb relay의 상대 표시·효과와 중복 생성 여부
- Ufo/우주선 피격자의 실제 속도 저하
- 플래그 뺏기·재생성·개인 순위·팀전 종료·연장전의 연속 두 경기 회귀 테스트
- observer 입장·준비 제외·진행 중 관전·경기 후 방장 유지의 전체 흐름
- 장착품을 변경한 뒤 게임과 서버를 모두 재시작했을 때의 실제 클라이언트 표시
- NAT, 인터넷, 서로 다른 서브넷과 방화벽 환경; 현재 설계는 신뢰하는 로컬/LAN용
- 깨끗한 Windows PC에서 .NET 8 Desktop Runtime 설치 후 framework-dependent 공개
  패키지 실행, 비-Windows 서버 실행

접속기는 정확히 한 종류의 원본 packed P236 실행 파일 hash만 허용합니다. unpacked
실행 파일, 다른 P236 배포 변형과 다른 버전은 지원하지 않습니다. 또한 드문
`실행 중인 형제 인스턴스 + 남아 있는 중단 transaction marker` 조합에서는 다른
인스턴스 설정을 저장하는 과정의 형제 복구가 실행 중 PIN/XML을 건드릴 수 있는
알려진 제한이 있습니다. 이 경우 모든 sibling 클라이언트를 종료한 뒤 설정해야 합니다.

## 클라이언트 경로는 있지만 부분 구현 또는 미구현인 기능

### 명확한 미구현

- **대기실 채팅:** rider talk 요청을 방 구성원에게 중계하는 handler가 없습니다.
- **방 닫기와 강퇴:** close 요청은 응답 없이 무시되며 kick 요청 handler가 없습니다.
- **Grand Prix:** 현재 GP 조회는 0만 반환하며 입장, 준비와 경기 흐름이 없습니다.
- **상점과 경제:** 상점 입장은 무시됩니다. 구매, 선물, 쿠폰, 업그레이드, 재고,
  cash/Lucci 차감과 소유 인벤토리는 구현되지 않았습니다.
- **타임어택 기록 서비스:** 시작·완주 packet의 일부 값을 메모리에 보관할 뿐
  트랙별 기록 저장, 조회, 랭킹, 검증과 보상이 없습니다.
- **경기 성장·보상:** 결과 순위는 만들지만 RP/Lucci, 경험치, 보상과 결과 효과
  필드는 갱신하지 않습니다.
- **경기 중 이탈 알림:** 경기 중 rider 연결 종료를 상대에게 전하는 leave notice가
  없습니다.
- **팩토리 전용 부비트랩 규칙:** 서버에는 선택된 트랙·테마에 따른 아이템 추첨
  분기가 없습니다. P236 기본 확률표에도 별도 부비트랩 행이 없어, 이 효과가
  확률표 아이템인지 트랙 로컬 장애물인지부터 추가 분석과 실제 클라이언트 검증이
  필요합니다.
- **왕문어 먹물구름 변환:** 장착 카트에 따른 변환은 클라이언트가 수행하며, 실제
  캡처에서 `GopCloudBlack`(`0x225A04FA`)/`GoItemCloudBlack`(`0x32F90619`)으로
  확인했습니다. 이 exact relay와 잘못된 base type 차단은 구현·자동 검증했지만,
  변경 후 두 클라이언트 표시 여부는 아직 다시 확인하지 않았습니다. 서버가 장착
  카트를 보고 일반 Cloud를 CloudBlack으로 직접 바꾸지는 않습니다. 정적 자료의
  `octopus1`(id 28), `octopus2`(id 57) 중 어느 카트가 왕문어인지는 미확인입니다.

### 최소 응답 또는 부분 구현

- 인증은 password나 token을 검증하지 않고 username만 신뢰합니다. 잘못된 login
  profile은 임시 GUID username으로 대체되어 신규 JSON 계정으로 저장될 수 있습니다.
- rider inventory는 계정 소유 목록이 아니라 고정 전체 카탈로그와 임시 수량입니다.
- gift 목록은 비어 있고 cash는 0이며 event/license reward는 지급하지 않습니다.
- 다른 rider 정보 조회는 자신의 nickname에만 성공합니다.
- 동적 채널 상태와 command 응답은 비어 있거나 0입니다.
- 서버 시간 응답은 실제 현재 시간이 아닌 고정 호환 값입니다.
- 라이선스 완료 값은 저장하지만 성적·proof 검증과 보상 지급은 없습니다.
- 방 생성·입장·경기 종료 proof 값은 대부분 저장 또는 로그만 하고 검증하지 않습니다.
- 아이템과 flag 충돌·효과는 서버 권위형 simulation이 아니라 클라이언트 요청을
  검증·중계하는 shadow model입니다.
- 아이템 확률 JSON은 서버 권위형 추첨 규칙이 아닙니다. 선택한 로컬 클라이언트
  Data에 명시적으로 적용해야 하며, 접속 시 서버가 설정 일치 여부를 검증하지 않습니다.
- 일반 경기와 개인 플래그에는 서버 측 전체 경기 timeout이 없고, 모든 경기의
  로딩 완료 timeout도 없습니다.
- 경기 결과의 progression/effect 영역은 대부분 0이며 결과로 계정 DB를 갱신하지
  않습니다.
- 방·경기·점수는 메모리 전용이라 서버 재시작 시 사라집니다. JSON에는 계정,
  라이선스와 장착 정보만 남습니다.
- UDP 서버는 초기 echo/time-sync만 담당합니다. 경기 이동은 P2P endpoint를 사용하며
  NAT relay나 STUN을 제공하지 않습니다.
- UDP의 accountId/sessionToken을 별도로 인증하지 않아 공개망에서는 endpoint
  spoofing을 방어할 수 없습니다.
- 등록되지 않은 TCP/UDP packet과 incoming `GameControl` state 0·2 이외의 값은
  로그만 남기거나 무시합니다.

친구·메신저·웹 랭킹처럼 외부 Nexon 구성요소에 의존했을 가능성이 있는 기능은
현재 근거만으로 P236 게임 서버의 미구현 항목이라고 단정하지 않습니다. 서버는
라이선스 level 0~4(4=L1)와 여섯 개 completion mask를 처리하지만, 기본 P236
클라이언트에는 완전한 L1 UI·트랙 자산이 없어 호환 RHO 패치가 필요하며 실제 L1 UI
end-to-end 동작은 수동 검증 전입니다. PRO(level 5)는 미지원·미검증입니다.

## 공개 전 권장 검증 순서

1. `scripts/Build.ps1`로 source-boundary, 빌드와 59개 테스트를 실행합니다.
2. `scripts/Publish.ps1`로 .NET 런타임을 포함하지 않는 깨끗한 framework-dependent
   게시 폴더를 만듭니다.
3. 새 데이터 디렉터리에서 서버를 시작하고 실제 원본 P236 설치본 두 개를 접속기로
   각각 준비합니다.
4. 방·개인/팀 스피드·아이템·플래그·옵저버 시나리오를 체크리스트로 반복합니다.
5. 캡처나 개인 데이터는 저장소에 올리지 않고, 통과한 소스 commit과 환경만 release
   note에 기록합니다.
