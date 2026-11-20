using UnityEngine;

namespace GameServer
{
    // 게임 시작 연출(미션 추첨 → 카운트다운) 동안 모든 캐릭터(플레이어·AI·원격)의 조작을 막는다.
    // Character.Update가 이 값을 보고 명령을 읽지 않는다. "Start!"가 뜨는 순간 풀린다.
    public static class GameFlow
    {
        public static bool ControlsLocked;

        // Enter Play Mode 옵션에서 도메인 리로드를 꺼도 이전 플레이의 값이 남지 않게 한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => ControlsLocked = false;
    }
}
