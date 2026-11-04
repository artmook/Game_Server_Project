# Game_Server_Project

.NET 10 / Windows / 직접 TCP Socket 기반 서버입니다. A 네트워크, B 방 관리, C 게임 로직(시작·이동·공격·승패)을 구현했습니다.
방장·준비·매칭·DB는 포함하지 않습니다.

## 파일 구조

```text
GameServerProject.slnx
Server/
  Program.cs         서버 실행, RoomManager 처리기 등록, Ctrl+C 취소
  GameServer.cs      기존 A 접속·송신 API·수신 루프·종료 정리 (변경 없음)
  RoomManager.cs     B 방 상태·명령·연결 종료 정리, GAME 명령 분배
  RoomManager.Game.cs C 게임 시작·이동/행동 중계·공격 판정·승패 (추가)
Shared/
  TcpConnection.cs   기존 UTF-8/LF 규격·부분 송신·송신 잠금 (변경 없음)
TestClient/
  Program.cs         독립 입력·수신 및 EXIT, 방 명령 안내 추가
Tests/
  Program.cs         기존 A 15개 검사, 종료 후 방 검사 호출
  RoomTests.cs       별도 서버의 실제 TCP 및 실제 TestClient 방 검사
  GameTests.cs       별도 서버의 실제 TCP 게임 검사, 수동 시계로 쿨타임 확인 (추가)
```

프로젝트 파일과 솔루션 구성은 유지합니다. bin/obj 및 테스트 로그는 생성물입니다.

## 실행 및 재현

.NET 10 SDK가 필요합니다. PowerShell에서:

```powershell
cd C:\Users\cyh47\Github\Game_Server_Project
dotnet build
dotnet run --project Server --no-build
```

별도 터미널 3~4개에서 각각 실행합니다.

```powershell
cd C:\Users\cyh47\Github\Game_Server_Project
dotnet run --project TestClient --no-build
```

첫 클라이언트에서 `ROOM|CREATE`, 나머지는 응답의 방 ID로 `ROOM|JOIN|1` 등을 입력합니다.
`ROOM|MEMBERS`, `ROOM|SAY|안녕하세요|팀원`, `ROOM|LEAVE`, `EXIT`를 확인합니다.

자동 검사 전에 수동 서버를 종료하세요. 검사도 7777 포트를 사용합니다.

```powershell
dotnet build
dotnet run --project Tests/NetworkTests.csproj --no-build
```

제한 환경에서 사용자 프로필의 .dotnet 쓰기가 차단되면 쓰기 가능한 폴더를
`DOTNET_CLI_HOME`으로 지정합니다. 단일 프로세스 빌드는 다음과 같습니다.

```powershell
dotnet build -m:1 --disable-build-servers
```

## 기존 A 통신 규격

- 서버: 모든 IPv4 인터페이스 7777, TestClient: 127.0.0.1:7777.
- BOM 없는 UTF-8 + LF. SendAsync가 LF를 붙이므로 문자열에 직접 넣지 않습니다.
- 본문 CR/LF 금지, CRLF 미지원. 잘못된 UTF-8·과도한 크기·미완성 EOF는 해당 연결 종료.
- 입력 최대 4096바이트(명령 접두사 포함), 공통 메시지 최대 8192바이트. 마지막 LF 제외.
- 동시 접속 최대 100개, 송신 제한시간 10초(송신 잠금 대기 포함).
- 접속: `SERVER|CONNECTED|{clientId}`.
- `HELLO` 및 일반 문자열: `SERVER|RECEIVED|{message}`. 방 처리기 등록 후에도 유지.
- `BROADCAST|본문`: 전체 접속자에게 `SERVER|BROADCAST|{clientId}|본문`. 방과 무관한 A 테스트 명령.
- TestClient는 입력 대기 중에도 수신. `EXIT`는 서버에 보내지 않고 로컬 종료하며 대소문자 무관.
- 서버 Ctrl+C는 취소 후 전체 연결과 작업을 기다립니다.

## B 방 명령과 응답

명령은 대소문자를 구분하며, 성공 응답은 요청자에게만 보냅니다.
SAY는 별도 성공 ACK 없이 송신자를 포함한 같은 방 수신자에게 MESSAGE를 보냅니다.

| 요청 | 응답 예시 |
|---|---|
| `ROOM\|CREATE` | `SERVER\|ROOM\|CREATED\|1` |
| `ROOM\|JOIN\|1` | `SERVER\|ROOM\|JOINED\|1` |
| `ROOM\|LEAVE` | `SERVER\|ROOM\|LEFT\|1` |
| `ROOM\|MEMBERS` | `SERVER\|ROOM\|MEMBERS\|1\|1,2,3` |
| `ROOM\|SAY\|안녕\|팀원` | `SERVER\|ROOM\|MESSAGE\|1\|2\|안녕\|팀원` |

방 ID는 서버가 부여하는 양의 int이며 삭제 후 재사용하지 않습니다(동일 관리자 기준).
생성자는 자동 입장합니다. 한 클라이언트는 하나의 방에만 소속되며 생성자 포함 최대 4명입니다.
다른 방으로 옮기려면 먼저 LEAVE해야 합니다. 재접속한 새 clientId는 미입장 상태입니다.
MEMBERS의 ID는 오름차순이며 쉼표로 구분합니다. MESSAGE는 방 ID, 서버가 아는 송신자 ID,
원본 본문 순서입니다. 본문의 ID는 신원으로 사용하지 않습니다.
본문은 `ROOM|SAY|` 뒤 전체를 그대로 보존하고 한글·이모지·파이프를 허용합니다.
빈 본문과 공백만 있는 본문은 거부합니다. 접두사 포함 4096바이트 규격을 적용합니다.

오류 응답은 `SERVER|ROOM|ERROR|{code}`이며, 실패에는 방 ID를 붙이지 않습니다.
소속 상태나 잘못된 ID에 따라 유효한 방을 결정할 수 없기 때문입니다.

| 코드 | 의미 |
|---|---|
| `INVALID_COMMAND` | 알 수 없는 ROOM 명령 또는 인수 개수 오류 |
| `GAME_IN_PROGRESS` | 게임이 진행 중인 방에 입장 요청 (중간 참가 불가) |
| `INVALID_ROOM_ID` | 양의 int가 아닌 ID. 공백·부호·추가 파이프도 거부 |
| `ALREADY_IN_ROOM` | 방 소속 중 생성 또는 입장 요청(같은 방도 포함) |
| `ROOM_NOT_FOUND` | 없는 방 |
| `ROOM_FULL` | 생성자 포함 4명 정원 도달 |
| `NOT_IN_ROOM` | 미입장 상태의 LEAVE/MEMBERS/SAY |
| `EMPTY_MESSAGE` | SAY의 빈 본문 또는 공백만 있는 본문 |

JOIN은 ID 형식 → 기존 소속 → 방 존재 → 게임 진행 → 정원 순서로 검사합니다.
SAY는 빈 본문 → 기존 소속 순서입니다. 사용자 명령 오류는 응답 후 연결을 유지합니다.
프레임 규격 위반과 통신 오류는 A의 기존 종료 정책을 따릅니다.

## C 게임 명령과 응답

Unity `GameServerUnity`(HYH 브랜치)의 게임에 맞춘 규격입니다. 플레이어는 AI 동물 30마리 사이에 숨어
다른 플레이어를 찾아 한 번에 처치하며, 마지막 생존자가 승리합니다.
게임은 방 단위이며 방 멤버 전원이 참가합니다. 방장 개념이 없어 방 멤버 누구나 시작할 수 있습니다.

| 요청 | 서버 처리 | 받는 사람 |
|---|---|---|
| `GAME\|START` | 방 인원 2명 이상이면 시작 → `SERVER\|GAME\|STARTED\|{방}\|{게임번호}\|{AI시드}\|{ID:스폰번호,...}` | 방 전원 |
| `GAME\|MOVE\|x\|y\|z\|회전Y\|속도` | 값 검사·위치 저장 → `SERVER\|GAME\|MOVE\|{ID}\|x\|y\|z\|회전\|속도` | 송신자 제외 |
| `GAME\|ACTION\|1~4` | `SERVER\|GAME\|ACTION\|{ID}\|{번호}` | 송신자 제외 |
| `GAME\|JUMP` | `SERVER\|GAME\|JUMP\|{ID}` | 송신자 제외 |
| `GAME\|ATTACK` | 빗나감·AI 공격. 쿨타임만 검사 → `SERVER\|GAME\|ATTACK\|{ID}` | 송신자 제외 |
| `GAME\|ATTACK\|{대상ID}` | 위 ATTACK 중계 후 대상 판정 → 성공 시 `SERVER\|GAME\|KILL\|{게임번호}\|{공격자}\|{대상}\|{남은 수}` | KILL은 방 전원 |
| `GAME\|DIE` | 자기 화면의 AI에게 맞음 → `SERVER\|GAME\|KILL\|{게임번호}\|0\|{ID}\|{남은 수}` | 방 전원 |
| (게임 중 LEAVE·접속 종료) | `SERVER\|GAME\|OUT\|{게임번호}\|{ID}\|{남은 수}` | 남은 방 멤버 |
| (생존자 1명 이하) | `SERVER\|GAME\|END\|{게임번호}\|{승자ID, 없으면 0}` 후 방은 대기 상태 | 방 전원 |

- 숫자는 `CultureInfo.InvariantCulture`로 보냅니다(소수점 `.`, 공백·쉼표 불가). 회전은 0 이상 360 미만으로 바꿔 중계합니다.
- 게임번호는 서버 전체에서 증가합니다. 서로 다른 명령의 응답 도착 순서는 보장하지 않으므로
  클라이언트는 현재 게임번호와 다른 KILL/OUT/END를 무시해야 합니다.
- AI 시드는 AI 초기 배치용입니다. AI는 각 클라이언트가 자기 화면에서만 움직이므로 PC마다 위치가 달라질 수 있고,
  서버는 플레이어끼리의 공격만 판정합니다. 스폰번호는 Unity `spawnPoints` 인덱스로 쓰며 배열 길이로 나머지 연산합니다.
- 공격 판정: 쿨타임 2초(지연 여유 0.2초), 공격자·대상 생존, 마지막 보고 위치 사이 거리 3m + 여유 1.5m.
  공격 판정을 쓰려면 게임 시작 직후와 이동 중에 MOVE를 주기적으로(권장 초당 10회) 보내야 합니다.
- 이동 검사: 좌표 ±10000 이내, 속도 0~20, 바닥 평면 이동량이 `8m/s × 경과 시간 + 1.5m`를 넘으면 거부합니다.

오류는 `SERVER|GAME|ERROR|{code}`이며 요청자에게만 보냅니다. 오류 후에도 연결은 유지합니다.

| 코드 | 의미 |
|---|---|
| `INVALID_COMMAND` | 알 수 없는 GAME 명령 또는 인수 개수 오류 |
| `NOT_IN_ROOM` | 방에 들어가지 않음 |
| `NOT_ENOUGH_PLAYERS` | 방 인원 2명 미만에서 START |
| `ALREADY_STARTED` | 진행 중에 START |
| `NOT_STARTED` | 게임 시작 전 명령 |
| `DEAD` | 탈락한 플레이어의 명령 |
| `INVALID_VALUE` | MOVE 숫자·범위 오류, ACTION 번호 오류 |
| `TOO_FAST` | 경과 시간에 비해 너무 먼 이동 (위치 미반영) |
| `COOLDOWN` | 2초 쿨타임 중 공격 (중계 없음) |
| `INVALID_TARGET` | 자기 자신, 게임에 없는 ID, 이미 죽은 대상 (공격 동작은 중계·쿨타임 소모) |
| `OUT_OF_RANGE` | 거리 초과 또는 위치 미보고 (공격 동작은 중계·쿨타임 소모) |

## API 연결·동시성·C 담당 진입점

Server/Program.cs가 서버 시작 전에 RoomManager를 생성하고
`MessageHandler = rooms.HandleMessageAsync`,
`ClientDisconnected += rooms.HandleClientDisconnected`로 연결합니다.

RoomManager는 방별 HashSet과 ID별 소속 Dictionary를 단일 lock 안에서 함께 관리합니다.
정원 검사·추가·삭제·빈 방 제거와 메시지 수신자 스냅샷 생성은 모두 같은 잠금 안입니다.
잠금을 푼 뒤 전달된 CancellationToken으로 SendToClientAsync/SendToClientsAsync를 await합니다.
소켓 직접 송수신이나 추가 수신 루프는 없습니다. 상태 변경 후 송신 실패가 나도 상태를 되돌리지 않으며,
전송 API가 연결 오류를 기록·정리합니다. 네트워크 전달 완료를 영속적으로 보장하지 않습니다.
종료 이벤트는 동기 제거만 수행합니다. 이미 퇴장한 ID도 안전하며 종료자에게 응답하지 않습니다.
async void나 관찰하지 않는 정리 Task는 없습니다.

수신자는 명령 처리 시 잠금 안에서 확정한 멤버입니다. 동시에 퇴장하더라도 이미 확정된
메시지가 뒤늦게 도착할 수 있으며, 서로 다른 명령의 응답 도착 순서는 보장하지 않습니다.

C 게임 로직은 RoomManager의 partial 파일(RoomManager.Game.cs)이며 `GAME|` 명령을 같은 잠금 안에서 처리합니다.
처리 결과는 (메시지, 수신자) 목록으로 만들고 잠금 밖에서 만든 순서대로 송신합니다.
게임 중 접속 종료는 동기 이벤트에서 상태만 바꾸고, OUT/END 송신은 추적 작업으로 실행합니다.
이 작업은 예외를 모두 기록하며 Program.cs가 서버 종료 후 `WaitForNoticesAsync`로 기다립니다.

## 실제 검증 결과

2026-10-29, Windows / .NET SDK 10.0.301.
변경 전 A 검사: `ALL PASS (15)`.
기본 병렬 `dotnet build`는 이 제한 환경에서 오류 상세 없이 실패했습니다. 단일 프로세스 빌드(`-m:1 --disable-build-servers`)는 경고 0 / 오류 0으로 성공했습니다. 변경 후: 기존 A 15개 + 새 방 검사 8개 = `ALL PASS (23)`.
서버 상세 로그: `Tests/bin/Debug/net10.0/network-test-server.log`.

기존 15개 검사를 삭제하거나 완화하지 않았습니다. 기존 검사 서버를 종료한 후 별도 서버와
명시적으로 연결한 RoomManager로 방 검사를 실행하므로 확장 처리기 검사와 충돌하지 않습니다.
방 검사에는 생성·자동 소속, 정원 및 마지막 자리 경합, 방 간 격리, 한글·파이프·크기 경계,
오류 후 연결 유지, 퇴장·FIN·RST·재접속, 동시 입퇴장·송신, 실제 콘솔 EXIT·강제 종료,
소속자가 있는 서버 취소 종료 및 재시작 후 빈 방 삭제 확인이 포함됩니다.
실제 TestClient 프로세스는 창을 띄우지 않고 표준 입력·출력을 연결해 자동 검사합니다.

C 게임 검사(GameTests.cs, 7개)는 작성 환경에 .NET SDK가 없어 아직 실행하지 않았습니다.
Windows에서 위 검사 명령을 실행하면 기존 23개 + 게임 7개 = `ALL PASS (30)`이 나와야 합니다.

## 제한과 미검증 범위

인증·암호화·하트비트·DB는 없습니다. AI 위치 동기화와 Unity 클라이언트 연동 코드는 C 범위에 포함하지 않았습니다. 연결 상태만으로 플레이어를 식별합니다.
외부 네트워크·Unity 연동·장시간 부하·100명 정원 경계 부하·실제 터미널 Ctrl+C 키 이벤트는
이번 검증 범위에 포함하지 않았습니다. 서버 취소 토큰에 의한 종료 경로는 검사합니다.
물리적인 네트워크 단절을 즉시 감지하지 않으며, 재시작 시 방 상태를 저장하거나 복원하지 않습니다.
부분 송신 반복문은 유지하지만 OS가 부분 송신을 반환하도록 강제하는 검사는 없습니다.
