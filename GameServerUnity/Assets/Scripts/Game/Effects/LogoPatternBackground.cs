/*
[ 코드 설명 ]

LogoPatternBackground (로고 반복 스크롤 배경)

1. 로고 그림을 엇갈린 줄로 화면 가득 반복해 깔고, 정해진 방향으로 천천히 흘려보낸다. (술래 가리개 배경)
2. 셰이더(GameServer/UI/LogoPattern)가 그림을 그리고, 이 컴포넌트는 Inspector 값을 셰이더에 넘긴다.
   - 배경색, 로고 색(원래 색 ↔ 한 가지 색 선화), 로고 진하기, 로고 크기, 칸 간격, 줄 엇갈림, 흐르는 방향·속도, 기울기
3. 게임을 실행하지 않아도 Scene/Game 화면에서 흘러가는 모습이 보인다(Animate In Edit Mode).
4. CanvasGroup 페이드(술래 가리개가 나타났다 사라지는 효과)를 그대로 따른다.
*/

using UnityEngine;
using UnityEngine.UI;

namespace GameServer
{

    [ExecuteAlways]
    [RequireComponent(typeof(CanvasRenderer))]
    public class LogoPatternBackground : MaskableGraphic
    {
        [Header("그림")]
        [SerializeField] private Texture2D logo;
        [SerializeField] private Shader shader;

        [Header("색")]
        [SerializeField] private Color backgroundColor = new Color(0.95f, 0.95f, 0.96f, 1f);
        [Tooltip("로고 선 색. A(투명도)가 로고 진하기")]
        [SerializeField] private Color logoTint = new Color(122f / 255f, 198f / 255f, 1f, 0.85f);
        [Tooltip("0 = 로고 원래 색 그대로, 1 = 한 가지 색 선화(어두운 선은 Logo Tint, 밝은 면은 배경색)")]
        [SerializeField, Range(0f, 1f)] private float tintAmount = 1f;
        [Tooltip("선화 모드에서 이 밝기보다 어두우면 진한 선, 위쪽 값보다 밝으면 배경으로 녹는다")]
        [SerializeField] private Vector2 lineRange = new Vector2(0.3f, 0.8f);

        [Header("배치 (UI 단위, 1920x1080 기준)")]
        [Tooltip("로고 높이. 너비는 그림 비율대로 정해진다.")]
        [SerializeField, Min(8f)] private float logoHeight = 110f;
        [Tooltip("로고 사이 빈 공간(가로, 세로)")]
        [SerializeField] private Vector2 spacing = new Vector2(180f, 130f);
        [Tooltip("줄마다 옆으로 밀리는 정도(0.5 = 반 칸씩 엇갈림)")]
        [SerializeField, Range(0f, 1f)] private float rowShift = 0.5f;
        [Tooltip("무늬 전체 기울기(도)")]
        [SerializeField, Range(-45f, 45f)] private float angle = 0f;

        [Header("움직임")]
        [Tooltip("흐르는 방향(x 오른쪽+, y 위+)")]
        [SerializeField] private Vector2 direction = new Vector2(1f, -0.6f);
        [Tooltip("초당 움직이는 거리(UI 단위)")]
        [SerializeField, Min(0f)] private float speed = 45f;
        [SerializeField] private bool animateInEditMode = true;

        private Material runtimeMaterial;
        private float scrollTime;

        private static readonly int BgColorId = Shader.PropertyToID("_BgColor");
        private static readonly int LogoTintId = Shader.PropertyToID("_LogoTint");
        private static readonly int TintAmountId = Shader.PropertyToID("_TintAmount");
        private static readonly int LineRangeId = Shader.PropertyToID("_LineRange");
        private static readonly int RectSizeId = Shader.PropertyToID("_RectSize");
        private static readonly int CellSizeId = Shader.PropertyToID("_CellSize");
        private static readonly int LogoSizeId = Shader.PropertyToID("_LogoSize");
        private static readonly int VelocityId = Shader.PropertyToID("_Velocity");
        private static readonly int RowShiftId = Shader.PropertyToID("_RowShift");
        private static readonly int AngleId = Shader.PropertyToID("_Angle");
        private static readonly int ScrollTimeId = Shader.PropertyToID("_ScrollTime");

        public override Texture mainTexture => logo != null ? logo : s_WhiteTexture;
        // 재질은 씬에 저장하지 않고 실행할 때 만든다(Material 칸은 비워 둔다).
        public override Material defaultMaterial => runtimeMaterial != null ? runtimeMaterial : base.defaultMaterial;

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureMaterial();
            ApplyProperties();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorTick;
            UnityEditor.EditorApplication.update += EditorTick;
#endif
        }

        protected override void OnDisable()
        {
            base.OnDisable();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorTick;
#endif
        }

#if UNITY_EDITOR
        // 게임을 실행하지 않을 때도 흘러가게: 에디터 시간으로 움직이고 Scene/Game 화면을 다시 그린다.
        private double lastEditorTime;
        private void EditorTick()
        {
            if (Application.isPlaying || !animateInEditMode || this == null || !isActiveAndEnabled) return;
            double now = UnityEditor.EditorApplication.timeSinceStartup;
            float dt = lastEditorTime > 0 ? (float)System.Math.Min(0.1, now - lastEditorTime) : 0f;
            lastEditorTime = now;
            Advance(dt);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
#endif

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (runtimeMaterial != null)
            {
                if (Application.isPlaying) Destroy(runtimeMaterial);
                else DestroyImmediate(runtimeMaterial);
            }
        }

        private void EnsureMaterial()
        {
            if (runtimeMaterial != null) return;
            Shader s = shader != null ? shader : Shader.Find("GameServer/UI/LogoPattern");
            if (s == null) return;
            runtimeMaterial = new Material(s) { name = "LogoPattern (Runtime)", hideFlags = HideFlags.HideAndDontSave };
            SetMaterialDirty();
        }

        private void Update()
        {
            if (Application.isPlaying) Advance(Time.unscaledDeltaTime);   // 일시정지(timeScale 0)에도 흐른다
        }

        private void Advance(float dt)
        {
            scrollTime += dt;
            if (scrollTime > 100000f) scrollTime = 0f;
            if (runtimeMaterial != null) runtimeMaterial.SetFloat(ScrollTimeId, scrollTime);
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            ApplyProperties();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            EnsureMaterial();
            ApplyProperties();
        }
#endif

        private void ApplyProperties()
        {
            if (runtimeMaterial == null) return;
            float aspect = logo != null && logo.height > 0 ? (float)logo.width / logo.height : 2f;
            var logoSize = new Vector2(logoHeight * aspect, logoHeight);
            var cell = logoSize + spacing;
            Rect r = rectTransform.rect;
            Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.zero;

            runtimeMaterial.SetColor(BgColorId, backgroundColor);
            runtimeMaterial.SetColor(LogoTintId, logoTint);
            runtimeMaterial.SetFloat(TintAmountId, tintAmount);
            runtimeMaterial.SetVector(LineRangeId, new Vector4(lineRange.x, Mathf.Max(lineRange.x + 0.01f, lineRange.y), 0f, 0f));
            runtimeMaterial.SetVector(RectSizeId, new Vector4(Mathf.Max(1f, r.width), Mathf.Max(1f, r.height), 0f, 0f));
            runtimeMaterial.SetVector(CellSizeId, new Vector4(Mathf.Max(1f, cell.x), Mathf.Max(1f, cell.y), 0f, 0f));
            runtimeMaterial.SetVector(LogoSizeId, new Vector4(logoSize.x, logoSize.y, 0f, 0f));
            runtimeMaterial.SetVector(VelocityId, new Vector4(dir.x * speed, dir.y * speed, 0f, 0f));
            runtimeMaterial.SetFloat(RowShiftId, rowShift);
            runtimeMaterial.SetFloat(AngleId, angle * Mathf.Deg2Rad);
            runtimeMaterial.SetFloat(ScrollTimeId, scrollTime);
        }
    }
}
