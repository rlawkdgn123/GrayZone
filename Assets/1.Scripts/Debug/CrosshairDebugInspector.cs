using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 조준선 전용 런타임 인스펙터입니다. 지정한 키로 창을 열어 <see cref="CrosshairController"/>의 값을 플레이 중에 조절합니다.
/// </summary>
/// <remarks>
/// <para>
/// 조준선은 캐릭터가 아니라 화면에 하나뿐이라 대상 선택이 필요 없고, 조절 항목은 그 자체로 수십 개입니다.
/// 캐릭터·무기·스폰을 다루는 <see cref="RuntimeDebugTrainer"/> 안에 함께 두면 그 창이 조준선 항목에 묻히므로 따로 뺐습니다.
/// 창 골격(크기 조절, 글자 배율, 개발 모드 게이트)은 트레이너와 같은 방식입니다.
/// </para>
/// <para>
/// 씬에 배치할 필요가 없습니다. 실행 시 스스로 하나 만들어 씬을 넘어 살아남습니다.
/// 동작 여부는 <see cref="GameDevMode.DebugFeaturesEnabled"/>가 정하며, 정식 빌드에서는 창이 열리지 않습니다.
/// </para>
/// <para>
/// 여기서 바꾼 값은 씬에 있는 컴포넌트의 런타임 값이라 플레이를 끝내면 사라집니다.
/// 마음에 드는 값을 찾았으면 프리팹/씬의 <see cref="CrosshairController"/> 인스펙터에 직접 옮겨 적어야 합니다.
/// </para>
/// </remarks>
[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
public class CrosshairDebugInspector : MonoBehaviour
{
    private static CrosshairDebugInspector s_instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        s_instance = null;
    }

    /// <summary>
    /// 씬 배치 없이 스스로 하나를 만듭니다.
    /// </summary>
    /// <remarks>
    /// 여기서 개발 모드를 보고 만들지 말지 정하지 않습니다.
    /// 개발 모드는 <see cref="GameManager"/>가 자기 Awake에서 켜는데, 그 시점이 이 호출보다 뒤일 수 있습니다.
    /// 한 번 만들지 않기로 하면 뒤에 켜져도 다시 만들어지지 않으므로, 만들어 두고 매 프레임 개발 모드를 확인합니다.
    /// </remarks>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (s_instance != null)
        {
            return;
        }

        GameObject holder = new GameObject("[CrosshairDebugInspector]");
        holder.AddComponent<CrosshairDebugInspector>();
        DontDestroyOnLoad(holder);
    }

    // ─────────────────────────────────────────────────────────────
    // 상태
    // ─────────────────────────────────────────────────────────────

    [Header("Activation")]
    [SerializeField, Tooltip("개발 모드와 독립적으로 이 창과 토글 입력만 활성화합니다.")]
    private bool m_inspectorEnabled = true;

    [SerializeField, Tooltip("조준선 인스펙터를 열고 닫을 키입니다. None이면 키 입력으로 열 수 없습니다.")]
    private Key m_toggleKey = Key.F10;

    /// <summary>개발 모드 상태를 바꾸지 않고 이 창과 입력만 켜거나 끕니다.</summary>
    public bool InspectorEnabled
    {
        get => m_inspectorEnabled;
        set
        {
            m_inspectorEnabled = value;
            if (!m_inspectorEnabled && m_open)
            {
                SetOpen(false);
            }
        }
    }

    /// <summary>창을 열고 닫는 키입니다.</summary>
    public Key ToggleKey
    {
        get => m_toggleKey;
        set => m_toggleKey = value;
    }

    private bool m_open;
    private Vector2 m_scroll;

    /// <summary>
    /// 이 창이 지금 열려 있는지 여부입니다.
    /// </summary>
    /// <remarks>
    /// <see cref="RuntimeDebugTrainer"/>가 닫힐 때 이 창이 아직 열려 있는지 보고,
    /// 열려 있으면 게임플레이로 되돌리지 않게 하기 위한 것입니다.
    /// </remarks>
    public static bool IsWindowOpen => s_instance != null && s_instance.m_open;

    // 펼침 상태. 항목이 많아 기본은 자주 쓰는 것만 열어 둡니다.
    private bool m_showShape = true;
    private bool m_showSub = true;
    private bool m_showSpread = true;
    private bool m_showShotRecoil;
    private bool m_showReload;
    private bool m_showAmmoGauge;
    private bool m_showHitMarker;
    private bool m_showKillSkull;
    private bool m_showBlockMarker;

    // 색은 슬라이더 네 줄을 차지하므로 따로 접어 둡니다.
    private bool m_showMainColor;
    private bool m_showMainStrokeColor;
    private bool m_showSubColor;
    private bool m_showSubStrokeColor;
    private bool m_showAmmoGaugeColor;
    private bool m_showLowAmmoGaugeColor;
    private bool m_showReloadAmmoGaugeColor;
    private bool m_showSpecialAmmoGaugeColor;
    private bool m_showAmmoGaugeBackgroundColor;
    private bool m_showHitMarkerColorBody;
    private bool m_showHitMarkerColorHead;
    private bool m_showKillSkullTint;
    private bool m_showBlockMarkerColor;

    // IMGUI 런타임 창은 기본 리사이즈 핸들이 없으므로 테두리를 잡아 크기를 바꿉니다.
    private Rect m_windowRect = new Rect(520f, 10f, 460f, 640f);
    private Vector2 m_resizeStartMouse;
    private Rect m_resizeStartRect;
    private bool m_isResizing;
    private int m_resizeEdgeX;
    private int m_resizeEdgeY;

    private const float MinWindowWidth = 380f;
    private const float MinWindowHeight = 320f;
    private const float WindowMargin = 10f;
    private const float ResizeGripSize = 20f;
    private const float ResizeBorderSize = 6f;

    /// <summary>글자 배율 1을 적용할 기준 창 너비입니다.</summary>
    private const float ReferenceWindowWidth = 460f;

    private const float MinUiScale = 0.75f;
    private const float MaxUiScale = 2.0f;

    private float m_appliedUiScale = -1f;
    private GUISkin m_scaledSkin;
    private float m_scaledSkinScale = -1f;
    private GUISkin m_previousSkin;

    private GUIStyle m_headerStyle;

    /// <summary>지금 조절 중인 조준선입니다. 씬이 바뀌면 Unity의 null 판정으로 걸러져 다시 찾습니다.</summary>
    private CrosshairController m_crosshair;

    private void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            Destroy(gameObject);
            return;
        }

        s_instance = this;
    }

    private void OnDestroy()
    {
        if (s_instance == this)
        {
            s_instance = null;
        }
    }

    private void OnDisable()
    {
        if (m_open)
        {
            m_open = false;
            ReleaseInputTakeover();
        }
    }

    private void Update()
    {
        // ShelterScene_Jung에서는 조준선 인스펙터를 열지 않음
        if (!m_inspectorEnabled || !GameDevMode.DebugFeaturesEnabled || ShelterClearCountDebugToggle.IsTargetSceneActive)
        {
            if (m_open)
            {
                SetOpen(false);
            }
            return;
        }

        if (m_toggleKey != Key.None
            && Keyboard.current != null
            && Keyboard.current[m_toggleKey].wasPressedThisFrame)
        {
            SetOpen(!m_open);
        }

        // 열린 동안 커서가 다시 잠기면(캐릭터 전환 등) 매 프레임 풀어 둡니다.
        if (m_open)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void SetOpen(bool open)
    {
        if (m_open == open)
        {
            return;
        }

        m_open = open;

        if (m_open)
        {
            AcquireInputTakeover();
        }
        else
        {
            ReleaseInputTakeover();
        }
    }

    /// <summary>
    /// 창을 조작할 수 있도록 커서를 풀고 플레이어 입력을 끕니다.
    /// </summary>
    /// <remarks>
    /// 씬이 입력 모드 계약을 제공하면 그 구현이 커서와 플레이어 입력을 소유합니다.
    /// 트레이너와 같은 경로를 쓰는 이유는, 서로 다른 방식으로 잡으면 한쪽이 잠근 것을
    /// 다른 쪽이 모르는 채로 풀어 버려 커서와 입력 상태가 어긋나기 때문입니다.
    /// </remarks>
    private void AcquireInputTakeover()
    {
        CombatSceneManager fieldManager = CombatSceneManager.Instance;
        if (fieldManager != null)
        {
            fieldManager.SetInputMode(InputMode.UI);
            return;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        foreach (PlayerInput playerInput in FindObjectsByType<PlayerInput>(FindObjectsSortMode.None))
        {
            playerInput.GetComponent<PlayerInputController>()?.ResetInputState();

            if (playerInput.inputIsActive)
            {
                playerInput.DeactivateInput();
            }
        }
    }

    /// <summary>
    /// 커서와 플레이어 입력을 게임플레이 상태로 되돌립니다.
    /// </summary>
    /// <remarks>
    /// 트레이너가 아직 열려 있으면 되돌리지 않습니다. 그쪽이 계속 커서를 쓰고 있기 때문입니다.
    /// 반대로 트레이너가 이미 닫혔다면, 그쪽은 이 창이 열려 있는 것을 보고 되돌리기를 건너뛰었을 것이므로
    /// 처음 잡은 쪽이 누구였는지와 무관하게 마지막으로 닫히는 이 창이 되돌려야 합니다.
    /// </remarks>
    private void ReleaseInputTakeover()
    {
        if (RuntimeDebugTrainer.IsMenuOpen)
        {
            return;
        }

        CombatSceneManager fieldManager = CombatSceneManager.Instance;
        if (fieldManager != null)
        {
            fieldManager.SetInputMode(InputMode.Gameplay);
            return;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        SquadManager squadManager = FindFirstObjectByType<SquadManager>();
        SquadMemberController player = squadManager != null ? squadManager.PlayerSquadMember : null;
        if (player == null)
        {
            return;
        }

        PlayerInput input = player.GetComponent<PlayerInput>();
        if (input != null)
        {
            input.ActivateInput();
            input.SwitchCurrentActionMap("Player");
        }
    }

    /// <summary>씬의 조준선을 찾아 캐시합니다. 비활성 오브젝트에 있어도 찾습니다.</summary>
    private CrosshairController ResolveCrosshair()
    {
        if (m_crosshair != null)
        {
            return m_crosshair;
        }

        m_crosshair = FindFirstObjectByType<CrosshairController>(FindObjectsInactive.Include);
        return m_crosshair;
    }

    // ─────────────────────────────────────────────────────────────
    // GUI
    // ─────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (!m_open || !m_inspectorEnabled || !GameDevMode.DebugFeaturesEnabled || MissionOverlayVisibility.IsShown)
        {
            return;
        }

        // 스타일 적용은 창을 실제로 그릴 때만 합니다.
        // 전역 스킨을 바꾼 뒤 그리지 않고 빠져나가면 되돌릴 곳이 없어 다른 IMGUI 창이 이 배율을 물려받습니다.
        EnsureStyles();

        ClampWindowRectToScreen();

        try
        {
            Rect moved = GUI.Window(GetInstanceID(), m_windowRect, DrawWindow, "조준선 인스펙터");
            m_windowRect.position = moved.position;
        }
        finally
        {
            // 전역 스킨은 반드시 되돌립니다. 남겨 두면 다른 IMGUI 창이 이 배율을 물려받습니다.
            RestoreSkin();
        }
    }

    private void DrawWindow(int windowId)
    {
        CrosshairController crosshair = ResolveCrosshair();
        if (crosshair == null)
        {
            GUILayout.Label("CrosshairController를 찾을 수 없습니다. 조준선이 있는 씬에서 열어야 합니다.");
            if (GUILayout.Button("닫기"))
            {
                SetOpen(false);
            }
            DrawWindowChrome(windowId);
            return;
        }

        GUILayout.Label($"{m_toggleKey} 또는 아래 버튼으로 닫기. 바꾼 값은 플레이를 끝내면 사라집니다.");

        m_scroll = GUILayout.BeginScrollView(m_scroll);

        DrawShapeSection(crosshair);
        GUILayout.Space(6);
        DrawSubShapeSection(crosshair);
        GUILayout.Space(6);
        DrawSpreadSection(crosshair);
        GUILayout.Space(6);
        DrawShotRecoilSection(crosshair);
        GUILayout.Space(6);
        DrawReloadSection(crosshair);
        GUILayout.Space(6);
        DrawAmmoGaugeSection(crosshair);
        GUILayout.Space(6);
        DrawHitMarkerSection(crosshair);
        GUILayout.Space(6);
        DrawKillSkullSection(crosshair);
        GUILayout.Space(6);
        DrawBlockMarkerSection(crosshair);

        GUILayout.EndScrollView();

        GUILayout.Space(4);
        if (GUILayout.Button("닫기", GUILayout.Height(26)))
        {
            SetOpen(false);
        }

        DrawWindowChrome(windowId);
    }

    private void DrawShapeSection(CrosshairController crosshair)
    {
        if (!SectionHeader("■ 중앙 표시", ref m_showShape))
        {
            return;
        }

        crosshair.CurrentMainShape = (CrosshairController.MainShape)GUILayout.SelectionGrid(
            (int)crosshair.CurrentMainShape,
            new[] { "없음", "점", "링" },
            3);

        crosshair.MainSizePixels = SliderRow("점 크기", crosshair.MainSizePixels, 0f, 40f);
        crosshair.MainRingSizePixels = SliderRow("링 지름", crosshair.MainRingSizePixels, 0f, 80f);
        crosshair.MainRingThicknessPixels = SliderRow("링 두께", crosshair.MainRingThicknessPixels, 0f, 20f);
        crosshair.MainStrokeThicknessPixels = SliderRow("테두리 두께", crosshair.MainStrokeThicknessPixels, 0f, 10f);
        crosshair.CornerRadiusPixels = SliderRow("모서리 반경", crosshair.CornerRadiusPixels, 0f, 20f);

        crosshair.MainColor = ColorRow("중앙 색", crosshair.MainColor, ref m_showMainColor);
        crosshair.MainStrokeColor = ColorRow("중앙 테두리 색", crosshair.MainStrokeColor, ref m_showMainStrokeColor);
    }

    private void DrawSubShapeSection(CrosshairController crosshair)
    {
        if (!SectionHeader("■ 보조 표시(팔)", ref m_showSub))
        {
            return;
        }

        crosshair.CurrentSubShape = (CrosshairController.SubShape)GUILayout.SelectionGrid(
            (int)crosshair.CurrentSubShape,
            new[] { "없음", "둥근 십자", "사각 십자", "링", "점" },
            3);

        crosshair.CenterSpacePixels = SliderRow("중앙 간격", crosshair.CenterSpacePixels, 0f, 60f);
        crosshair.SubSizePixels = SliderRow("팔 길이", crosshair.SubSizePixels, 0f, 60f);
        crosshair.SubWidthPixels = SliderRow("팔 폭", crosshair.SubWidthPixels, 0f, 30f);
        crosshair.SubThicknessPixels = SliderRow("팔 두께", crosshair.SubThicknessPixels, 0f, 30f);
        crosshair.SubRingSizePixels = SliderRow("보조 링 지름", crosshair.SubRingSizePixels, 0f, 200f, "0");
        crosshair.SubRingThicknessPixels = SliderRow("보조 링 두께", crosshair.SubRingThicknessPixels, 0f, 20f);
        crosshair.SubStrokeThicknessPixels = SliderRow("테두리 두께", crosshair.SubStrokeThicknessPixels, 0f, 10f);

        crosshair.SubColor = ColorRow("보조 색", crosshair.SubColor, ref m_showSubColor);
        crosshair.SubStrokeColor = ColorRow("보조 테두리 색", crosshair.SubStrokeColor, ref m_showSubStrokeColor);
    }

    private void DrawSpreadSection(CrosshairController crosshair)
    {
        if (!SectionHeader("■ 탄퍼짐 반영", ref m_showSpread))
        {
            return;
        }

        crosshair.SetSpreadAccuracyEnabled(
            GUILayout.Toggle(crosshair.UseSpreadAccuracy, " 실제 탄퍼짐을 조준선에 반영"));
        crosshair.SetClampToMaxGap(
            GUILayout.Toggle(crosshair.ClampToMaxGap, " 최대 벌어짐으로 제한"));

        crosshair.SetMaxGapPixels(SliderRow("최대 벌어짐", crosshair.MaxGapPixels, 0f, 400f, "0"));
        crosshair.SetSpreadLerpSpeed(SliderRow("벌어짐 보간", crosshair.SpreadLerpSpeed, 0f, 40f));

        // 같은 탄퍼짐이라도 어느 반경을 가리키느냐에 따라 넓이가 달라집니다.
        // 넓은 것부터 좁은 것 순서(열거형 선언 순서)라 위에서 아래로 갈수록 조준선이 좁아집니다.
        GUILayout.Label("표시 기준");
        crosshair.SetSpreadDisplayBasis(
            (CrosshairController.SpreadDisplayBasis)GUILayout.SelectionGrid(
                (int)crosshair.CurrentSpreadDisplayBasis,
                new[] { "콘 경계", "대부분", "통상", "밀집 코어" },
                4));
    }

    private void DrawShotRecoilSection(CrosshairController crosshair)
    {
        if (!SectionHeader("■ 사격 반동 펄스", ref m_showShotRecoil))
        {
            return;
        }

        // 펄스 곡선(AnimationCurve)은 IMGUI로 다루기 어려워 인스펙터에서만 고칩니다.
        crosshair.SetShotRecoilPulseEnabled(
            GUILayout.Toggle(crosshair.ShotRecoilPulseEnabled, " 발사마다 조준선 벌어짐"));
        crosshair.SetShotRecoilPulseAmplitudePixels(
            SliderRow("최대 벌어짐", crosshair.ShotRecoilPulseAmplitudePixels, 0f, 60f, "0"));
        crosshair.SetShotRecoilPulseDuration(
            SliderRow("길이(초)", crosshair.ShotRecoilPulseDuration, 0f, 1f));
    }

    private void DrawReloadSection(CrosshairController crosshair)
    {
        if (!SectionHeader("■ 재장전", ref m_showReload))
        {
            return;
        }

        crosshair.SwapCrosshairOnReload =
            GUILayout.Toggle(crosshair.SwapCrosshairOnReload, " 재장전 중 탄약 아이콘으로 교체");
        crosshair.ReloadBulletSizePixels = SliderRow("아이콘 크기", crosshair.ReloadBulletSizePixels, 0f, 200f, "0");
        crosshair.ReloadBlinkSpeed = SliderRow("깜빡임 속도", crosshair.ReloadBlinkSpeed, 0f, 15f);
        crosshair.ReloadBlinkMinAlpha = SliderRow("깜빡임 최소 투명도", crosshair.ReloadBlinkMinAlpha, 0f, 1f);
    }

    private void DrawAmmoGaugeSection(CrosshairController crosshair)
    {
        if (!SectionHeader("■ 탄약 게이지", ref m_showAmmoGauge))
        {
            return;
        }

        crosshair.ShowAmmoGauge = GUILayout.Toggle(crosshair.ShowAmmoGauge, " 게이지 표시");
        crosshair.AmmoGaugeAlwaysVisible = GUILayout.Toggle(crosshair.AmmoGaugeAlwaysVisible, " 항상 보이기");

        crosshair.AmmoGaugeSizePixels = SliderRow("게이지 크기", crosshair.AmmoGaugeSizePixels, 0f, 300f, "0");
        crosshair.AmmoGaugeThicknessPixels = SliderRow("게이지 두께", crosshair.AmmoGaugeThicknessPixels, 0f, 30f);
        crosshair.AmmoGaugeSweepDegrees = SliderRow("차지 각도", crosshair.AmmoGaugeSweepDegrees, 0f, 360f, "0");
        crosshair.AmmoGaugeStartAngleDegrees = SliderRow("시작 각도", crosshair.AmmoGaugeStartAngleDegrees, -180f, 180f, "0");
        crosshair.AmmoGaugeDiagonalOffset = SliderRow("대각 거리", crosshair.AmmoGaugeDiagonalOffset, -200f, 200f, "0");

        GUILayout.Label("채우는 방향");
        crosshair.CurrentAmmoGaugeFillDirection =
            (CrosshairController.AmmoGaugeFillDirection)GUILayout.SelectionGrid(
                (int)crosshair.CurrentAmmoGaugeFillDirection,
                new[] { "시계", "반시계" },
                2);

        crosshair.ShowAmmoGaugeBackground =
            GUILayout.Toggle(crosshair.ShowAmmoGaugeBackground, " 배경 표시");
        crosshair.AmmoGaugeBackgroundAlpha = SliderRow("배경 투명도", crosshair.AmmoGaugeBackgroundAlpha, 0f, 1f);
        crosshair.AmmoGaugeSegmentGapDegrees = SliderRow("칸 간격(도)", crosshair.AmmoGaugeSegmentGapDegrees, 0f, 20f);

        crosshair.AmmoGaugeColor = ColorRow("게이지 색", crosshair.AmmoGaugeColor, ref m_showAmmoGaugeColor);
        crosshair.LowAmmoGaugeColor = ColorRow("잔탄 부족 색", crosshair.LowAmmoGaugeColor, ref m_showLowAmmoGaugeColor);
        crosshair.ReloadAmmoGaugeColor = ColorRow("재장전 진행 색", crosshair.ReloadAmmoGaugeColor, ref m_showReloadAmmoGaugeColor);
        crosshair.SpecialAmmoGaugeColor = ColorRow("특수탄 색", crosshair.SpecialAmmoGaugeColor, ref m_showSpecialAmmoGaugeColor);
        crosshair.AmmoGaugeBackgroundColor = ColorRow("배경 색", crosshair.AmmoGaugeBackgroundColor, ref m_showAmmoGaugeBackgroundColor);
    }

    private void DrawHitMarkerSection(CrosshairController crosshair)
    {
        if (!SectionHeader("■ 적중 표시", ref m_showHitMarker))
        {
            return;
        }

        crosshair.HitMarkerEnabled = GUILayout.Toggle(crosshair.HitMarkerEnabled, " 히트마커 표시");
        crosshair.HitMarkerLengthPixels = SliderRow("길이", crosshair.HitMarkerLengthPixels, 0f, 100f, "0");
        crosshair.HitMarkerBaseLengthPixels = SliderRow("밑변 길이", crosshair.HitMarkerBaseLengthPixels, 0f, 60f, "0");
        crosshair.HitMarkerCenterGapPixels = SliderRow("중앙 간격", crosshair.HitMarkerCenterGapPixels, 0f, 60f, "0");
        crosshair.HitMarkerFadeDuration = SliderRow("사라짐(초)", crosshair.HitMarkerFadeDuration, 0f, 2f);

        crosshair.HitMarkerDamageScaleEnabled =
            GUILayout.Toggle(crosshair.HitMarkerDamageScaleEnabled, " 피해량에 비례한 길이");
        if (crosshair.HitMarkerDamageScaleEnabled)
        {
            crosshair.HitMarkerMinDamage = SliderRow("최소 피해", crosshair.HitMarkerMinDamage, 0f, 200f, "0");
            crosshair.HitMarkerMaxDamage = SliderRow("최대 피해", crosshair.HitMarkerMaxDamage, 0f, 500f, "0");
            crosshair.HitMarkerLengthAtMinDamagePixels =
                SliderRow("최소 피해 길이", crosshair.HitMarkerLengthAtMinDamagePixels, 0f, 100f, "0");
            crosshair.HitMarkerLengthAtMaxDamagePixels =
                SliderRow("최대 피해 길이", crosshair.HitMarkerLengthAtMaxDamagePixels, 0f, 100f, "0");
        }

        crosshair.HitMarkerRollBaseDegrees = SliderRow("기울기(도)", crosshair.HitMarkerRollBaseDegrees, -90f, 90f, "0");
        crosshair.HitMarkerRandomRollEnabled =
            GUILayout.Toggle(crosshair.HitMarkerRandomRollEnabled, " 발마다 무작위로 기울이기");
        if (crosshair.HitMarkerRandomRollEnabled)
        {
            crosshair.HitMarkerRollRandomRangeDegrees =
                SliderRow("무작위 범위(도)", crosshair.HitMarkerRollRandomRangeDegrees, 0f, 45f, "0");
        }

        crosshair.HitMarkerColorBody = ColorRow("몸통 적중 색", crosshair.HitMarkerColorBody, ref m_showHitMarkerColorBody);
        crosshair.HitMarkerColorHead = ColorRow("약점 적중 색", crosshair.HitMarkerColorHead, ref m_showHitMarkerColorHead);
    }

    private void DrawKillSkullSection(CrosshairController crosshair)
    {
        if (!SectionHeader("■ 처치 표시", ref m_showKillSkull))
        {
            return;
        }

        if (GUILayout.Button("처치 효과 미리보기", GUILayout.Height(24)))
        {
            crosshair.ShowKill();
        }

        crosshair.KillPulseEnabled = GUILayout.Toggle(crosshair.KillPulseEnabled, " 조준선 펄스");
        crosshair.CurrentKillPulseDirection = (CrosshairController.KillPulseDirection)GUILayout.SelectionGrid(
            (int)crosshair.CurrentKillPulseDirection,
            new[] { "튀어나감", "모임" },
            2);
        crosshair.KillPulseAmplitudePixels = SliderRow("펄스 크기", crosshair.KillPulseAmplitudePixels, 0f, 60f, "0");
        crosshair.KillPulseDuration = SliderRow("펄스 길이(초)", crosshair.KillPulseDuration, 0f, 1f);

        GUILayout.Space(4);
        crosshair.ShowKillSkull = GUILayout.Toggle(crosshair.ShowKillSkull, " 해골 표시");
        crosshair.KillSkullSizePixels = SliderRow("크기", crosshair.KillSkullSizePixels, 0f, 200f, "0");
        crosshair.KillSkullHoldDuration = SliderRow("유지(초)", crosshair.KillSkullHoldDuration, 0f, 3f);
        crosshair.KillSkullFadeDuration = SliderRow("사라짐(초)", crosshair.KillSkullFadeDuration, 0f, 3f);

        crosshair.KillSkullTint = ColorRow("색조", crosshair.KillSkullTint, ref m_showKillSkullTint);
    }

    private void DrawBlockMarkerSection(CrosshairController crosshair)
    {
        if (!SectionHeader("■ 차단 마커", ref m_showBlockMarker))
        {
            return;
        }

        crosshair.BlockMarkerEnabled = GUILayout.Toggle(crosshair.BlockMarkerEnabled, " 막혔을 때 실제 탄착점 표시");
        crosshair.BlockMarkerRingSizePixels = SliderRow("링 지름", crosshair.BlockMarkerRingSizePixels, 0f, 100f, "0");
        crosshair.BlockMarkerRingThicknessPixels = SliderRow("링 두께", crosshair.BlockMarkerRingThicknessPixels, 0f, 20f);
        crosshair.BlockMarkerDotSizePixels = SliderRow("가운데 점", crosshair.BlockMarkerDotSizePixels, 0f, 40f);

        crosshair.DimCrosshairWhileBlockMarker =
            GUILayout.Toggle(crosshair.DimCrosshairWhileBlockMarker, " 표시 중 조준선 흐리게");
        crosshair.BlockMarkerCrosshairAlpha = SliderRow("흐릴 때 투명도", crosshair.BlockMarkerCrosshairAlpha, 0f, 1f);
        crosshair.BlockMarkerCrosshairFadeDuration =
            SliderRow("전환 시간(초)", crosshair.BlockMarkerCrosshairFadeDuration, 0f, 1f);

        crosshair.BlockMarkerColor = ColorRow("마커 색", crosshair.BlockMarkerColor, ref m_showBlockMarkerColor);
    }

    // ─────────────────────────────────────────────────────────────
    // 창 골격
    // ─────────────────────────────────────────────────────────────

    private void DrawWindowChrome(int windowId)
    {
        HandleWindowResize();
        GUI.DragWindow(new Rect(
            ResizeBorderSize,
            ResizeBorderSize,
            m_windowRect.width - ResizeBorderSize * 2f - ResizeGripSize,
            24f));
    }

    /// <summary>창 테두리를 잡아 크기를 바꿉니다. 네 변과 네 모서리 어디를 잡아도 동작합니다.</summary>
    private void HandleWindowResize()
    {
        Rect inner = new Rect(0f, 0f, m_windowRect.width, m_windowRect.height);

        bool onLeft = Event.current.mousePosition.x <= ResizeBorderSize;
        bool onRight = Event.current.mousePosition.x >= inner.width - ResizeBorderSize;
        bool onTop = Event.current.mousePosition.y <= ResizeBorderSize;
        bool onBottom = Event.current.mousePosition.y >= inner.height - ResizeBorderSize;
        bool onEdge = (onLeft || onRight || onTop || onBottom) && inner.Contains(Event.current.mousePosition);

        GUI.Label(new Rect(inner.width - ResizeGripSize, inner.height - ResizeGripSize,
                           ResizeGripSize, ResizeGripSize), "↘");

        Event currentEvent = Event.current;
        switch (currentEvent.type)
        {
            case EventType.MouseDown when onEdge:
                m_isResizing = true;
                m_resizeEdgeX = onLeft ? -1 : (onRight ? 1 : 0);
                m_resizeEdgeY = onTop ? -1 : (onBottom ? 1 : 0);
                m_resizeStartMouse = GUIUtility.GUIToScreenPoint(currentEvent.mousePosition);
                m_resizeStartRect = m_windowRect;
                currentEvent.Use();
                break;

            case EventType.MouseDrag when m_isResizing:
                ApplyResize(GUIUtility.GUIToScreenPoint(currentEvent.mousePosition) - m_resizeStartMouse);
                GUI.changed = true;
                currentEvent.Use();
                break;

            case EventType.MouseUp when m_isResizing:
                m_isResizing = false;
                m_resizeEdgeX = 0;
                m_resizeEdgeY = 0;
                currentEvent.Use();
                break;
        }
    }

    /// <summary>잡은 테두리에 따라 창의 위치와 크기를 갱신합니다.</summary>
    private void ApplyResize(Vector2 delta)
    {
        Rect r = m_resizeStartRect;

        if (m_resizeEdgeX > 0)
        {
            float maxWidth = Mathf.Max(MinWindowWidth, Screen.width - r.x - WindowMargin);
            m_windowRect.width = Mathf.Clamp(r.width + delta.x, MinWindowWidth, maxWidth);
        }
        else if (m_resizeEdgeX < 0)
        {
            float right = r.x + r.width;
            float maxWidth = Mathf.Max(MinWindowWidth, right - WindowMargin);
            m_windowRect.width = Mathf.Clamp(r.width - delta.x, MinWindowWidth, maxWidth);
            m_windowRect.x = right - m_windowRect.width;
        }

        if (m_resizeEdgeY > 0)
        {
            float maxHeight = Mathf.Max(MinWindowHeight, Screen.height - r.y - WindowMargin);
            m_windowRect.height = Mathf.Clamp(r.height + delta.y, MinWindowHeight, maxHeight);
        }
        else if (m_resizeEdgeY < 0)
        {
            float bottom = r.y + r.height;
            float maxHeight = Mathf.Max(MinWindowHeight, bottom - WindowMargin);
            m_windowRect.height = Mathf.Clamp(r.height - delta.y, MinWindowHeight, maxHeight);
            m_windowRect.y = bottom - m_windowRect.height;
        }
    }

    private void ClampWindowRectToScreen()
    {
        float maxWidth = Mathf.Max(MinWindowWidth, Screen.width - WindowMargin * 2f);
        float maxHeight = Mathf.Max(MinWindowHeight, Screen.height - WindowMargin * 2f);
        m_windowRect.width = Mathf.Clamp(m_windowRect.width, MinWindowWidth, maxWidth);
        m_windowRect.height = Mathf.Clamp(m_windowRect.height, MinWindowHeight, maxHeight);
        m_windowRect.x = Mathf.Clamp(m_windowRect.x, WindowMargin - m_windowRect.width, Screen.width - WindowMargin);
        m_windowRect.y = Mathf.Clamp(m_windowRect.y, WindowMargin, Screen.height - WindowMargin);
    }

    // ─────────────────────────────────────────────────────────────
    // 헬퍼
    // ─────────────────────────────────────────────────────────────

    /// <summary>접었다 펼 수 있는 구역 제목을 그립니다.</summary>
    /// <returns>펼쳐져 있어 내용을 그려야 하면 <c>true</c>입니다.</returns>
    private bool SectionHeader(string label, ref bool expanded)
    {
        expanded = GUILayout.Toggle(expanded, expanded ? $" ▼ {label}" : $" ▶ {label}", m_headerStyle);
        return expanded;
    }

    private float SliderRow(string label, float value, float min, float max, string format = "0.##")
    {
        GUILayout.BeginHorizontal();
        float labelWidth = Mathf.Clamp(m_windowRect.width * 0.30f, 120f, 200f);
        GUILayout.Label(label, GUILayout.Width(labelWidth));
        float result = GUILayout.HorizontalSlider(value, min, max, GUILayout.MinWidth(120f), GUILayout.ExpandWidth(true));
        GUILayout.Label(result.ToString(format), GUILayout.Width(56));
        GUILayout.EndHorizontal();
        return result;
    }

    /// <summary>
    /// 색을 RGBA 네 줄로 조절합니다.
    /// </summary>
    /// <remarks>
    /// IMGUI에는 색 선택기가 없어 채널 슬라이더로 대신합니다.
    /// 네 줄을 차지하므로 접어 두고, 접힌 상태에서도 현재 색을 사각형으로 보여 줍니다.
    /// </remarks>
    private Color ColorRow(string label, Color value, ref bool expanded)
    {
        GUILayout.BeginHorizontal();
        expanded = GUILayout.Toggle(expanded, expanded ? $" ▼ {label}" : $" ▶ {label}");

        Rect swatch = GUILayoutUtility.GetRect(28f, 14f, GUILayout.Width(28f), GUILayout.ExpandWidth(false));
        Color previous = GUI.color;
        GUI.color = value;
        GUI.DrawTexture(swatch, Texture2D.whiteTexture);
        GUI.color = previous;
        GUILayout.EndHorizontal();

        if (!expanded)
        {
            return value;
        }

        value.r = SliderRow("  R", value.r, 0f, 1f);
        value.g = SliderRow("  G", value.g, 0f, 1f);
        value.b = SliderRow("  B", value.b, 0f, 1f);
        value.a = SliderRow("  A", value.a, 0f, 1f);
        return value;
    }

    private void EnsureStyles()
    {
        // 창 크기를 바꾸면 글자와 위젯도 같이 커지고 작아져야 인스펙터처럼 보입니다.
        float scale = Mathf.Clamp(m_windowRect.width / ReferenceWindowWidth, MinUiScale, MaxUiScale);

        if (m_headerStyle != null && Mathf.Approximately(scale, m_appliedUiScale))
        {
            ApplyScaledSkin(scale);
            return;
        }

        m_appliedUiScale = scale;

        m_headerStyle = new GUIStyle(GUI.skin.toggle)
        {
            fontSize = Mathf.RoundToInt(GUI.skin.toggle.fontSize <= 0 ? 12f * scale : GUI.skin.toggle.fontSize * scale),
            fontStyle = FontStyle.Bold,
        };

        ApplyScaledSkin(scale);
    }

    /// <summary>
    /// 이번 창에 쓸 기본 위젯 크기를 배율에 맞게 바꿉니다.
    /// </summary>
    /// <remarks>
    /// <see cref="GUI.skin"/>은 전역이라 창을 그리는 동안만 바꾸고 <see cref="RestoreSkin"/>으로 되돌립니다.
    /// 글꼴 크기가 0인 스타일은 스킨 기본값을 쓰겠다는 뜻이라, 그런 경우에만 기준값을 곱해 넣습니다.
    /// 0이 아닌 값을 그대로 곱하면 매 프레임 누적되어 글자가 계속 커집니다.
    /// </remarks>
    private void ApplyScaledSkin(float scale)
    {
        if (m_scaledSkin == null || !Mathf.Approximately(scale, m_scaledSkinScale))
        {
            m_scaledSkin = Instantiate(GUI.skin);
            m_scaledSkinScale = scale;

            int baseSize = Mathf.RoundToInt(12f * scale);

            foreach (GUIStyle style in m_scaledSkin.customStyles)
            {
                ScaleStyle(style, scale, baseSize);
            }

            ScaleStyle(m_scaledSkin.label, scale, baseSize);
            ScaleStyle(m_scaledSkin.button, scale, baseSize);
            ScaleStyle(m_scaledSkin.box, scale, baseSize);
            ScaleStyle(m_scaledSkin.textField, scale, baseSize);
            ScaleStyle(m_scaledSkin.toggle, scale, baseSize);
            ScaleStyle(m_scaledSkin.window, scale, baseSize);

            m_scaledSkin.horizontalSlider.fixedHeight = GUI.skin.horizontalSlider.fixedHeight * scale;
            m_scaledSkin.horizontalSliderThumb.fixedWidth = GUI.skin.horizontalSliderThumb.fixedWidth * scale;
            m_scaledSkin.horizontalSliderThumb.fixedHeight = GUI.skin.horizontalSliderThumb.fixedHeight * scale;
        }

        m_previousSkin = GUI.skin;
        GUI.skin = m_scaledSkin;

        static void ScaleStyle(GUIStyle style, float s, int baseSize)
        {
            if (style == null)
            {
                return;
            }

            style.fontSize = style.fontSize <= 0 ? baseSize : Mathf.RoundToInt(style.fontSize * s);

            if (style.fixedHeight > 0f)
            {
                style.fixedHeight *= s;
            }
        }
    }

    /// <summary>창을 다 그린 뒤 전역 스킨을 되돌립니다.</summary>
    private void RestoreSkin()
    {
        if (m_previousSkin != null)
        {
            GUI.skin = m_previousSkin;
            m_previousSkin = null;
        }
    }
}
