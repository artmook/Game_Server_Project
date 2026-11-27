using UnityEngine;

namespace GameServer
{
    // 게임 시작 연출(미션 추첨 → 카운트다운) 동안 모든 캐릭터(플레이어·AI·원격)의 조작을 막는다.
    // Character.Update가 이 값을 보고 명령을 읽지 않는다. "Start!"가 뜨는 순간 풀린다.
    // RoundOver: 라운드가 끝나 결과 화면이 떠 있는 동안 true. 조작을 막고 마우스를 풀어 버튼을 누를 수 있게 한다.
    public static class GameFlow
    {
        public static bool ControlsLocked;
        public static bool RoundOver;

        // Enter Play Mode 옵션에서 도메인 리로드를 꺼도 이전 플레이의 값이 남지 않게 한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            ControlsLocked = false;
            RoundOver = false;
            RoundRoles.Tagger = null;
        }
    }

    // 이번 라운드의 술래 캐릭터. 승패 판정(도망자가 모두 잡혔는지)에 쓴다.
    // - 내가 술래면 MissionIntro가 내 캐릭터를 넣는다.
    // - 지금 서버는 술래 정보를 보내지 않으므로, 내가 도망자면 누가 술래인지 모른다(null).
    //   그때는 다른 플레이어 전원을 도망자로 센다. 나중에 서버가 술래를 알려 주면 여기에 넣으면 된다.
    // - Test는 "Player 2 (술래)" 봇을 넣는다.
    public static class RoundRoles
    {
        public static Character Tagger;

        public static bool IsTagger(Character c) => c != null && Tagger != null && c == Tagger;
    }
}
