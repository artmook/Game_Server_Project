/*
[ 코드 설명 ]

GameStatusView (남은 시간 + 게임 결과 화면)

1. 화면 위 가운데에 서버가 알려 준 남은 시간(TIME)을 mm:ss로 보여 준다.
   - 클라이언트는 시간을 세지 않는다. 서버 값이 올 때마다 바꾼다(서버는 초가 바뀔 때마다 보낸다).
   - 30초 이하는 빨간색, 0초가 되면 "시간 종료"를 띄우고 서버의 END를 기다린다.
2. 서버 END가 오면 승리 팀(RUNNER/TAGGER)에 맞춰 결과 화면을 띄운다.
   - "도망자 승리!" 또는 "술래 승리!", 내 승패, 끝난 이유
   - "확인"을 누르면 NetworkGameManager.ConfirmResult로 결과를 닫고 대기실(방 화면)로 돌아간다.
3. UI는 실행할 때 코드로 만든다. 씬을 고치지 않아도 되도록 NetworkGameManager가 있는 씬에 자동으로 붙는다.
   한글이 보이게 씬에 있는 TMP 글꼴(대기실 글꼴)을 빌려 쓴다.
*/

using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameServer
{
    public class GameStatusView : MonoBehaviour
    {
        private const int LowTimeSeconds = 30;
        private static readonly Color RunnerColor = new Color(1f, 0.62f, 0.25f);   // LobbyView AccentHex
        private static readonly Color TaggerColor = new Color(1f, 0.35f, 0.31f);   // LobbyView TaggerHex
        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.6f);

        [SerializeField] private NetworkGameManager network;

        private TMP_FontAsset font;
        private GameObject timerRoot;
        private TMP_Text timerText;
        private GameObject resultRoot;
        private TMP_Text resultTitle;
        private TMP_Text resultDetail;
        private TMP_Text resultReason;
        private int shownSeconds = int.MinValue;

        // 씬에 NetworkGameManager가 있으면 자동으로 하나 만든다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            EnsureCreated();
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureCreated();

        private static void EnsureCreated()
        {
            if (FindFirstObjectByType<GameStatusView>() != null) return;
            var network = FindFirstObjectByType<NetworkGameManager>();
            if (network == null) return;
            var go = new GameObject("GameStatusView (Runtime)");
            go.AddComponent<GameStatusView>().network = network;
        }

        private void Awake()
        {
            if (network == null) network = FindFirstObjectByType<NetworkGameManager>();
            font = FindSceneFont();
            BuildUI();
            timerRoot.SetActive(false);
            resultRoot.SetActive(false);
        }

        private void OnEnable()
        {
            if (network == null) return;
            network.RemainingTimeChanged += HandleTimeChanged;
            network.GameResultReceived += ShowResult;
        }

        private void OnDisable()
        {
            if (network == null) return;
            network.RemainingTimeChanged -= HandleTimeChanged;
            network.GameResultReceived -= ShowResult;
        }

        private void Update()
        {
            if (network == null) return;
            bool showTimer = network.InGame && network.RemainingSeconds >= 0;
            if (timerRoot.activeSelf != showTimer) timerRoot.SetActive(showTimer);
            if (showTimer && network.RemainingSeconds != shownSeconds) HandleTimeChanged(network.RemainingSeconds);

            // 새 게임이 시작되거나 다른 곳에서 결과를 닫으면 결과 화면도 닫는다.
            if (resultRoot.activeSelf && !network.ResultPending) resultRoot.SetActive(false);
            else if (!resultRoot.activeSelf && network.ResultPending) ShowResult(network.LastResult);

            // 시간이 얼마 남지 않으면 살짝 커졌다 작아진다.
            float scale = showTimer && shownSeconds > 0 && shownSeconds <= LowTimeSeconds
                ? 1f + Mathf.Abs(Mathf.Sin(Time.unscaledTime * Mathf.PI)) * 0.06f : 1f;
            timerRoot.transform.localScale = Vector3.one * scale;
        }

        // ───────────── 남은 시간 ─────────────

        private void HandleTimeChanged(int seconds)
        {
            shownSeconds = seconds;
            if (seconds <= 0)
            {
                timerText.text = "<size=70%>시간 종료</size>";
                timerText.color = TaggerColor;
                return;
            }
            timerText.text = $"<size=55%><color=#FFFFFFAA>남은 시간</color></size>  {seconds / 60:00}:{seconds % 60:00}";
            timerText.color = seconds <= LowTimeSeconds ? TaggerColor : Color.white;
        }

        // ───────────── 결과 ─────────────

        private void ShowResult(NetworkGameManager.GameResult result)
        {
            bool runnersWon = result.WinnerTeam == NetworkGameManager.RunnerTeam;
            Color teamColor = runnersWon ? RunnerColor : TaggerColor;
            resultTitle.text = runnersWon ? "도망자 승리!" : "술래 승리!";
            resultTitle.color = teamColor;
            string role = result.LocalWasTagger ? "술래" : "도망자";
            resultDetail.text = result.LocalWon
                ? $"<color=#{ColorUtility.ToHtmlStringRGB(teamColor)}>승리</color>했습니다  <size=75%><color=#FFFFFFAA>(내 역할: {role})</color></size>"
                : $"<color=#FFFFFFAA>패배</color>했습니다  <size=75%><color=#FFFFFFAA>(내 역할: {role})</color></size>";
            resultReason.text = result.Reason switch
            {
                "MISSION_CLEAR" => "살아 있는 도망자가 모두 미션을 완료했습니다",
                "ALL_CAUGHT" => "술래가 도망자를 모두 잡았습니다",
                "TIME_OVER" => "제한시간 안에 미션을 완료하지 못했습니다",
                "TAGGER_LEFT" => "술래가 게임을 떠났습니다",
                _ => "",
            };
            resultRoot.SetActive(true);
            resultRoot.transform.SetAsLastSibling();
        }

        private void Confirm()
        {
            resultRoot.SetActive(false);
            if (network != null) network.ConfirmResult();
        }

        // ───────────── UI 만들기 ─────────────

        private static TMP_FontAsset FindSceneFont()
        {
            // 대기실 글꼴이 한글을 담고 있다. 없으면 TMP 기본 글꼴을 쓴다.
            LobbyView lobby = FindFirstObjectByType<LobbyView>(FindObjectsInactive.Include);
            if (lobby != null)
            {
                TMP_Text t = lobby.GetComponentInChildren<TMP_Text>(true);
                if (t != null && t.font != null) return t.font;
            }
            foreach (TMP_Text t in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.font != null) return t.font;
            return TMP_Settings.defaultFontAsset;
        }

        private void BuildUI()
        {
            var canvasGo = new GameObject("GameStatusCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;   // HUD·대기실 위
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)canvasGo.transform;

            // 남은 시간 (위 가운데)
            RectTransform timer = CreateImage("Timer", root, PanelColor, raycast: false);
            timer.anchorMin = timer.anchorMax = timer.pivot = new Vector2(0.5f, 1f);
            timer.anchoredPosition = new Vector2(0f, -20f);
            timer.sizeDelta = new Vector2(340f, 72f);
            timerText = CreateText("Text", timer, 46f, FontStyles.Bold);
            Stretch(timerText.rectTransform);
            timerRoot = timer.gameObject;

            // 결과 화면 (화면 전체를 덮어 뒤의 클릭을 막는다)
            RectTransform dim = CreateImage("Result", root, new Color(0f, 0f, 0f, 0.7f), raycast: true);
            Stretch(dim);
            resultRoot = dim.gameObject;

            RectTransform card = CreateImage("Card", dim, new Color(0.08f, 0.08f, 0.1f, 0.95f), raycast: true);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(720f, 440f);

            resultTitle = CreateText("Title", card, 84f, FontStyles.Bold);
            Place(resultTitle.rectTransform, 140f, 120f);
            resultDetail = CreateText("Detail", card, 40f, FontStyles.Normal);
            Place(resultDetail.rectTransform, 40f, 60f);
            resultReason = CreateText("Reason", card, 30f, FontStyles.Normal);
            resultReason.color = new Color(1f, 1f, 1f, 0.75f);
            Place(resultReason.rectTransform, -20f, 50f);

            RectTransform buttonRect = CreateImage("ConfirmButton", card, RunnerColor, raycast: true);
            buttonRect.anchorMin = buttonRect.anchorMax = buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.anchoredPosition = new Vector2(0f, -140f);
            buttonRect.sizeDelta = new Vector2(280f, 80f);
            var button = buttonRect.gameObject.AddComponent<Button>();
            button.targetGraphic = buttonRect.GetComponent<Image>();
            button.onClick.AddListener(Confirm);
            TMP_Text label = CreateText("Label", buttonRect, 40f, FontStyles.Bold);
            label.text = "확인";
            label.color = new Color(0.1f, 0.1f, 0.1f);
            Stretch(label.rectTransform);
        }

        private static RectTransform CreateImage(string name, Transform parent, Color color, bool raycast)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return (RectTransform)go.transform;
        }

        private TMP_Text CreateText(string name, Transform parent, float size, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.color = Color.white;
            return text;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // 카드 안에서 가로로 꽉 채우고, 가운데 기준 y 위치와 높이를 정한다.
        private static void Place(RectTransform rt, float y, float height)
        {
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(-40f, height);
        }
    }
}
