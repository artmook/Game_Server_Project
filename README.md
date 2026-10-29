# Game_Server_Project

.NET 10 / Windows / 직접 TCP Socket 기반 서버입니다. A 네트워크와 B 방 관리를 구현했습니다.
C 게임 로직(이동·공격·시작·승패), 방장·준비·매칭·DB는 포함하지 않습니다.

## 파일 구조

```text
GameServerProject.slnx
Server/
  Program.cs         서버 실행, RoomManager 처리기 등록, Ctrl+C 취소
  GameServer.cs      기존 A 접속·송신 API·수신 루프·종료 정리 (변경 없음)
  RoomManager.cs     B 방 상태·명령·연결 종료 정리 (추가)
Shared/
  TcpConnection.cs   기존 UTF-8/LF 규격·부분 송신·송신 잠금 (변경 없음)
TestClient/
  Program.cs         독립 입력·수신 및 EXIT, 방 명령 안내 추가
Tests/
  Program.cs         기존 A 15개 검사, 종료 후 방 검사 호출
  RoomTests.cs       별도 서버의 실제 TCP 및 실제 TestClient 방 검사 (추가)
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
| `INVALID_ROOM_ID` | 양의 int가 아닌 ID. 공백·부호·추가 파이프도 거부 |
| `ALREADY_IN_ROOM` | 방 소속 중 생성 또는 입장 요청(같은 방도 포함) |
| `ROOM_NOT_FOUND` | 없는 방 |
| `ROOM_FULL` | 생성자 포함 4명 정원 도달 |
| `NOT_IN_ROOM` | 미입장 상태의 LEAVE/MEMBERS/SAY |
| `EMPTY_MESSAGE` | SAY의 빈 본문 또는 공백만 있는 본문 |

JOIN은 ID 형식 → 기존 소속 → 방 존재 → 정원 순서로 검사합니다.
SAY는 빈 본문 → 기존 소속 순서입니다. 사용자 명령 오류는 응답 후 연결을 유지합니다.
프레임 규격 위반과 통신 오류는 A의 기존 종료 정책을 따릅니다.

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

C 담당자는 RoomManager.HandleMessageAsync의 명령 분기에서 게임 명령 연결을 시작할 수 있습니다.
MessageHandler는 단일 처리기이므로 방 처리기를 덮어쓰지 말고 명령을 분배해야 합니다.
방 소속을 사용하는 새 로직도 같은 잠금 아래 검증·스냅샷을 만들고 네트워크 대기는 밖에서 하세요.
현재는 요구 범위에 맞춰 게임용 별도 API나 게임 로직을 추가하지 않았습니다.

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

## 제한과 미검증 범위

인증·암호화·하트비트·DB·게임 로직은 없습니다. 연결 상태만으로 플레이어를 식별합니다.
외부 네트워크·Unity 연동·장시간 부하·100명 정원 경계 부하·실제 터미널 Ctrl+C 키 이벤트는
이번 검증 범위에 포함하지 않았습니다. 서버 취소 토큰에 의한 종료 경로는 검사합니다.
물리적인 네트워크 단절을 즉시 감지하지 않으며, 재시작 시 방 상태를 저장하거나 복원하지 않습니다.
부분 송신 반복문은 유지하지만 OS가 부분 송신을 반환하도록 강제하는 검사는 없습니다.
