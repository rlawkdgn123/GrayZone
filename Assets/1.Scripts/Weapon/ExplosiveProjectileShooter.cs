using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

/// <summary>
/// 플레이어의 투척 모드와 좌클릭 입력을 받아 폭발탄 경로를 표시하고 투척합니다.
/// </summary>
public class ExplosiveProjectileShooter : MonoBehaviour
{
    private const int MaxTrajectoryPointCount = 65;
    private const int LandingMarkerSegmentCount = 48;
    private const float LandingMarkerSurfaceOffset = 0.03f;
    private const string ExplosionRangeMaterialResourcePath = "ExplosionRange/M_ExplosionRangeSurface";
    private const string ExplosionRangeExclusionPassName = "ExclusionStencil";
    // 기본 Sphere 메시의 면은 실제 구보다 안쪽에 있으므로, 반경 경계가 잘리지 않도록 메시만 조금 키웁니다.
    private const float ExplosionRangeSphereScalePadding = 1.05f;
    private const string GrenadeActionLayerName = "Grenade Action Layer";
    private const string GrenadeEquipSoundEventPath = "event:/World/Throwable/Grenade/Equip";
    private const string MolotovEquipSoundEventPath = "event:/World/Throwable/Molotov/Equip";
    private static readonly int AnimIDGrenadeMode = Animator.StringToHash("IsGrenadeMode");
    private static readonly int AnimIDThrow = Animator.StringToHash("DoThrow");
    private static readonly int AnimStateGrenadeThrow = Animator.StringToHash("Grenade Throw");
    private static readonly int AnimStateGrenadeCrouchThrow = Animator.StringToHash("Grenade Crouch Throw");
    private static readonly int AnimStateGrenadeReady = Animator.StringToHash("Ready");

    // CrosshairController는 분대 전체가 하나를 공유합니다. 대원별 인스턴스에 복원 스냅샷을 두면
    // 조작 대원 전환/사망/비활성화 순서에 따라 이전 대원이 새 대원의 HUD를 덮을 수 있습니다.
    // 따라서 투척 프리셋은 공용 HUD 기준으로 한 명만 소유하고, 그 소유자만 원래 값을 복원합니다.
    private static ExplosiveProjectileShooter s_crosshairOverrideOwner;
    private static CrosshairController s_crosshairOverrideTarget;
    private static CrosshairPreset s_crosshairOverrideBaseline;
    private static bool s_loggedMissingGrenadeEquipSound;
    private static bool s_loggedMissingMolotovEquipSound;

    [System.Serializable]
    private sealed class CrosshairPreset
    {
        [Tooltip("무기 탄퍼짐에 따라 조준선 간격을 변경할지 여부입니다.")]
        [SerializeField] private bool m_useSpreadAccuracy;

        [Tooltip("중앙 표시 형태입니다.")]
        [SerializeField] private CrosshairController.MainShape m_mainShape = CrosshairController.MainShape.Ring;

        [Tooltip("중앙점 크기입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_mainSizePixels = 3.0f;

        [Tooltip("중앙 링 지름입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_mainRingSizePixels = 14.0f;

        [Tooltip("중앙 링 두께입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_mainRingThicknessPixels = 2.0f;

        [Tooltip("중앙 표시 색상입니다.")]
        [SerializeField] private Color m_mainColor = new Color(1.0f, 1.0f, 1.0f, 0.27450982f);

        [Tooltip("중앙 표시 외곽선 두께입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_mainStrokeThicknessPixels = 1.0f;

        [Tooltip("중앙 표시 외곽선 색상입니다.")]
        [SerializeField] private Color m_mainStrokeColor = new Color(0.0f, 0.0f, 0.0f, 0.27450982f);

        [Tooltip("보조 표시 형태입니다.")]
        [SerializeField] private CrosshairController.SubShape m_subShape = CrosshairController.SubShape.RoundedCross;

        [Tooltip("중앙과 보조 표시 사이의 간격입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_centerSpacePixels = 12.0f;

        [Tooltip("보조 점 크기입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_subSizePixels = 2.0f;

        [Tooltip("보조 십자선 길이입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_subWidthPixels = 7.0f;

        [Tooltip("보조 십자선 두께입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_subThicknessPixels = 2.0f;

        [Tooltip("보조 링 지름입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_subRingSizePixels = 20.0f;

        [Tooltip("보조 링 두께입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_subRingThicknessPixels = 2.0f;

        [Tooltip("보조 표시 색상입니다.")]
        [SerializeField] private Color m_subColor = new Color(1.0f, 1.0f, 1.0f, 0.27450982f);

        [Tooltip("보조 표시 외곽선 두께입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_subStrokeThicknessPixels = 1.0f;

        [Tooltip("보조 표시 외곽선 색상입니다.")]
        [SerializeField] private Color m_subStrokeColor = new Color(0.0f, 0.0f, 0.0f, 0.27450982f);

        [Tooltip("Rounded Cross 모서리 반지름입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_cornerRadiusPixels = 2.0f;

        /// <summary>크로스헤어의 현재 값을 그대로 복사한 프리셋을 만듭니다. 투척 모드 해제 시 복원할 원래 값으로 씁니다.</summary>
        public static CrosshairPreset Capture(CrosshairController crosshair)
        {
            return new CrosshairPreset
            {
                m_useSpreadAccuracy = crosshair.SpreadAccuracyEnabled,
                m_mainShape = crosshair.CurrentMainShape,
                m_mainSizePixels = crosshair.MainSizePixels,
                m_mainRingSizePixels = crosshair.MainRingSizePixels,
                m_mainRingThicknessPixels = crosshair.MainRingThicknessPixels,
                m_mainColor = crosshair.MainColor,
                m_mainStrokeThicknessPixels = crosshair.MainStrokeThicknessPixels,
                m_mainStrokeColor = crosshair.MainStrokeColor,
                m_subShape = crosshair.CurrentSubShape,
                m_centerSpacePixels = crosshair.CenterSpacePixels,
                m_subSizePixels = crosshair.SubSizePixels,
                m_subWidthPixels = crosshair.SubWidthPixels,
                m_subThicknessPixels = crosshair.SubThicknessPixels,
                m_subRingSizePixels = crosshair.SubRingSizePixels,
                m_subRingThicknessPixels = crosshair.SubRingThicknessPixels,
                m_subColor = crosshair.SubColor,
                m_subStrokeThicknessPixels = crosshair.SubStrokeThicknessPixels,
                m_subStrokeColor = crosshair.SubStrokeColor,
                m_cornerRadiusPixels = crosshair.CornerRadiusPixels,
            };
        }

        /// <summary>이 프리셋의 값을 크로스헤어에 덮어씁니다.</summary>
        public void Apply(CrosshairController crosshair)
        {
            crosshair.SetSpreadAccuracyEnabled(m_useSpreadAccuracy);
            crosshair.CurrentMainShape = m_mainShape;
            crosshair.MainSizePixels = m_mainSizePixels;
            crosshair.MainRingSizePixels = m_mainRingSizePixels;
            crosshair.MainRingThicknessPixels = m_mainRingThicknessPixels;
            crosshair.MainColor = m_mainColor;
            crosshair.MainStrokeThicknessPixels = m_mainStrokeThicknessPixels;
            crosshair.MainStrokeColor = m_mainStrokeColor;
            crosshair.CurrentSubShape = m_subShape;
            crosshair.CenterSpacePixels = m_centerSpacePixels;
            crosshair.SubSizePixels = m_subSizePixels;
            crosshair.SubWidthPixels = m_subWidthPixels;
            crosshair.SubThicknessPixels = m_subThicknessPixels;
            crosshair.SubRingSizePixels = m_subRingSizePixels;
            crosshair.SubRingThicknessPixels = m_subRingThicknessPixels;
            crosshair.SubColor = m_subColor;
            crosshair.SubStrokeThicknessPixels = m_subStrokeThicknessPixels;
            crosshair.SubStrokeColor = m_subStrokeColor;
            crosshair.CornerRadiusPixels = m_cornerRadiusPixels;
        }

        /// <summary>설정 화면 등 외부에서 쓰는 <see cref="CrosshairStyle"/> 형태로 바꿔 돌려줍니다.</summary>
        public CrosshairStyle ToStyle()
        {
            return new CrosshairStyle
            {
                useSpreadAccuracy = m_useSpreadAccuracy,
                mainShape = m_mainShape,
                mainSizePixels = m_mainSizePixels,
                mainRingSizePixels = m_mainRingSizePixels,
                mainRingThicknessPixels = m_mainRingThicknessPixels,
                mainColor = m_mainColor,
                mainStrokeThicknessPixels = m_mainStrokeThicknessPixels,
                mainStrokeColor = m_mainStrokeColor,
                subShape = m_subShape,
                centerSpacePixels = m_centerSpacePixels,
                subSizePixels = m_subSizePixels,
                subWidthPixels = m_subWidthPixels,
                subThicknessPixels = m_subThicknessPixels,
                subRingSizePixels = m_subRingSizePixels,
                subRingThicknessPixels = m_subRingThicknessPixels,
                subColor = m_subColor,
                subStrokeThicknessPixels = m_subStrokeThicknessPixels,
                subStrokeColor = m_subStrokeColor,
                cornerRadiusPixels = m_cornerRadiusPixels,
            };
        }

        /// <summary>
        /// <see cref="CrosshairStyle"/>로 프리셋을 만듭니다. 탄퍼짐 연동 여부는 스타일 값 대신 <paramref name="useSpreadAccuracy"/>를 씁니다.
        /// </summary>
        public static CrosshairPreset FromStyle(CrosshairStyle style, bool useSpreadAccuracy)
        {
            return new CrosshairPreset
            {
                m_useSpreadAccuracy = useSpreadAccuracy,
                m_mainShape = style.mainShape,
                m_mainSizePixels = style.mainSizePixels,
                m_mainRingSizePixels = style.mainRingSizePixels,
                m_mainRingThicknessPixels = style.mainRingThicknessPixels,
                m_mainColor = style.mainColor,
                m_mainStrokeThicknessPixels = style.mainStrokeThicknessPixels,
                m_mainStrokeColor = style.mainStrokeColor,
                m_subShape = style.subShape,
                m_centerSpacePixels = style.centerSpacePixels,
                m_subSizePixels = style.subSizePixels,
                m_subWidthPixels = style.subWidthPixels,
                m_subThicknessPixels = style.subThicknessPixels,
                m_subRingSizePixels = style.subRingSizePixels,
                m_subRingThicknessPixels = style.subRingThicknessPixels,
                m_subColor = style.subColor,
                m_subStrokeThicknessPixels = style.subStrokeThicknessPixels,
                m_subStrokeColor = style.subStrokeColor,
                m_cornerRadiusPixels = style.cornerRadiusPixels,
            };
        }
    }

    [Tooltip("마우스 휠로 순환 선택할 ProjectileBase Prefab 목록입니다.")]
    [SerializeField] private List<ProjectileBase> m_projectilePrefabs = new List<ProjectileBase>();

    [UnityEngine.Serialization.FormerlySerializedAs("m_projectilePrefab")]
    [SerializeField, HideInInspector] private ProjectileBase m_legacyProjectilePrefab;

    [Tooltip("현재 선택된 투척물 목록 인덱스입니다.")]
    [Min(0)]
    [SerializeField] private int m_selectedProjectileIndex;

    [Tooltip("G 투척 모드에서 현재 선택된 투척물 아이콘을 표시할 UI입니다. 비어 있으면 Scene에서 자동으로 찾습니다.")]
    [SerializeField] private GrenadeSelectionUI m_grenadeSelectionUI;

    [Tooltip("투척물 수량을 조회하고 실제 투척 시 1개를 소모할 스쿼드 공용 인벤토리입니다. 비어 있으면 Scene에서 자동으로 찾습니다.")]
    [SerializeField] private SquadInventoryManager m_inventoryManager;

    [Tooltip("G 투척 모드에서 수치 프리셋을 적용할 크로스헤어입니다. 비어 있으면 AimController 또는 Scene에서 자동으로 찾습니다.")]
    [SerializeField] private CrosshairController m_defaultCrosshair;

    [Tooltip("G 투척 모드에서 기존 크로스헤어에 임시로 적용할 수치 프리셋입니다.")]
    [SerializeField] private CrosshairPreset m_throwCrosshairPreset = new CrosshairPreset();

    [Header("Held Grenade Visual")]
    [Tooltip("선택한 투척물에 Held Visual Prefab이 없을 때 오른손에 표시할 기본 Visual Prefab입니다.")]
    [SerializeField] private GameObject m_heldGrenadeVisualPrefab;

    [Tooltip("수류탄 Visual을 배치할 손 장착 소켓입니다. 비어 있으면 Humanoid 오른손 본을 사용합니다.")]
    [SerializeField] private Transform m_heldGrenadeSocket;

    [Tooltip("오른손 본을 기준으로 한 수류탄 위치입니다.")]
    [SerializeField] private Vector3 m_heldGrenadeLocalPosition = Vector3.zero;

    [Tooltip("오른손 본을 기준으로 한 수류탄 회전입니다.")]
    [SerializeField] private Vector3 m_heldGrenadeLocalEulerAngles = Vector3.zero;

    [Tooltip("손에 표시할 수류탄 Visual의 크기입니다.")]
    [SerializeField] private Vector3 m_heldGrenadeLocalScale = Vector3.one * 0.25f;

    [Tooltip("Grenade Hand IK 소켓이 없을 때만 사용할 Collider 중심 기준 fallback 투척 시작 위치입니다.")]
    [SerializeField] private Vector3 m_throwOriginOffset = new Vector3(0.0f, 0.2f, 1.0f);

    [Tooltip("손 소켓 또는 fallback 투척 원점에 추가할 로컬 X/Y/Z 오프셋입니다. X는 좌우, Y는 높이, Z는 앞뒤이며 궤적 시작점과 실제 투척물 생성 위치에 함께 적용됩니다.")]
    [SerializeField] private Vector3 m_throwStartOffset = Vector3.zero;

    [Tooltip("수평 조준 시 폭탄이 같은 높이로 돌아올 때의 기준 투척 거리입니다. 실제 비행 종료점은 아닙니다.")]
    [UnityEngine.Serialization.FormerlySerializedAs("m_maxThrowDistance")]
    [Min(0.1f)]
    [SerializeField] private float m_referenceThrowDistance = 20.0f;

    [Tooltip("수평 조준 시 투척 시작점보다 올라갈 기준 최고 높이입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_arcHeight = 1.5f;

    [Tooltip("투척 시작부터 최고점까지 포물선을 아래로 휘게 하는 가속도입니다. 낮을수록 상승 구간이 완만해집니다.")]
    [UnityEngine.Serialization.FormerlySerializedAs("m_downwardAcceleration")]
    [Min(0.01f)]
    [SerializeField] private float m_ascentDownwardAcceleration = 8.0f;

    [Tooltip("최고점 이후 포물선을 아래로 휘게 하는 가속도입니다. 높을수록 빠르게 떨어집니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_descentDownwardAcceleration = 24.0f;

    [Tooltip("포물선의 거리와 높이는 유지하면서 실제 비행 속도만 조절합니다. 1은 기본 속도, 2는 두 배 속도입니다.")]
    [InspectorName("Throw Speed")]
    [Min(0.01f)]
    [SerializeField] private float m_throwSpeedMultiplier = 1.25f;

    [Tooltip("켜면 조준 중에 포물선 궤적 선을 함께 그립니다. 끄면 폭발 범위와 착탄 지점 표시만 보입니다.")]
    [SerializeField] private bool m_showTrajectoryLine = false;

    [Tooltip("선택한 투척물이 없을 때 투척을 누르면 조준선 위에 띄울 경고 문구입니다(AimWarningText). 비우면 띄우지 않습니다.")]
    [SerializeField] private string m_outOfProjectileMessage = "투척물이 부족합니다!";

    [Tooltip("LineRenderer로 미리 보여 줄 포물선의 최대 누적 길이입니다. 실제 폭탄 이동은 제한하지 않습니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_trajectoryPreviewDistance = 20.0f;

    [Tooltip("한 번 투척한 뒤 다음 투척 경로를 표시하고 다시 던질 수 있을 때까지의 시간입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_throwCooldown = 1.0f;

    [Tooltip("좌클릭 입력 후 투척물이 실제로 생성될 때까지의 지연 시간(초)입니다. 0이면 입력 즉시 생성합니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_throwReleaseDelay = 0.0f;

    [Tooltip("경로 표시를 구성할 선분 수입니다.")]
    [Range(4, 64)]
    [SerializeField] private int m_trajectorySegments = 24;

    [Tooltip("이동 중 충돌을 검사할 구체 반지름입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_collisionRadius = 0.5f;

    [Tooltip("투척 경로 및 실제 이동 중 충돌을 검사할 Layer입니다.")]
    [SerializeField] private LayerMask m_collisionLayers = ~0;

    [Tooltip("경로 표시 선의 두께입니다.")]
    [Min(0.001f)]
    [SerializeField] private float m_trajectoryWidth = 0.04f;

    [Tooltip("경로 표시 선의 색상입니다.")]
    [SerializeField] private Color m_trajectoryColor = new Color(1.0f, 0.75f, 0.1f, 0.9f);

    [Header("Impact Range Preview")]
    [Tooltip("폭발 범위 구체와 닿은 표면을 칠할 머티리얼입니다(GrayZone/ExplosionRangeSurface). 비워 두면 Resources/ExplosionRange/M_ExplosionRangeSurface를 씁니다.")]
    [SerializeField] private Material m_impactRangeMaterial;

    [Tooltip("폭발 범위 안 표면을 채울 색상입니다.")]
    [SerializeField] private Color m_impactPreviewColor = new Color(1.0f, 0.75f, 0.1f, 0.9f);

    [Tooltip("폭발 범위 안 표면 채움의 투명도입니다. 0은 완전 투명, 1은 완전 불투명입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_impactPreviewFillAlpha = 0.2f;

    [Tooltip("폭발 범위 경계 테두리 색상입니다. 알파가 테두리 투명도입니다.")]
    [SerializeField] private Color m_impactPreviewRimColor = new Color(1.0f, 0.75f, 0.1f, 0.9f);

    [Tooltip("폭발 범위 경계 테두리 두께(m)입니다. 반경 경계에서 안쪽으로 이 거리만큼 테두리 색으로 바뀝니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_impactPreviewRimWidth = 0.08f;

    [Tooltip("폭발 범위 표시를 칠하지 않을 Layer입니다. 예: 아군 캐릭터, 적. 불투명 렌더러에만 적용됩니다.")]
    [SerializeField] private LayerMask m_impactPreviewExcludeLayers = 0;

    [Header("Landing Marker")]
    [Tooltip("켜면 착탄 지점에 폭발 범위와 별개로 작은 원을 표시합니다.")]
    [SerializeField] private bool m_showLandingMarker = true;

    [Tooltip("착탄 지점 원의 반지름(m)입니다. 폭발 범위와 무관한 고정 크기입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_landingMarkerRadius = 0.25f;

    [Tooltip("착탄 지점 원의 색상입니다. 알파가 투명도입니다.")]
    [SerializeField] private Color m_landingMarkerColor = new Color(1.0f, 0.75f, 0.1f, 0.6f);

    private readonly RaycastHit[] m_previewHits = new RaycastHit[16];
    private readonly Vector3[] m_trajectoryPoints = new Vector3[MaxTrajectoryPointCount];

    private PlayerInputController m_input;
    private AimController m_aimController;
    private Animator m_animator;
    private Collider m_sourceCollider;
    private LineRenderer m_trajectoryLine;
    private MeshRenderer m_explosionRangeRenderer;
    private Material m_runtimeExplosionRangeMaterial;
    private ExplosionRangeExclusionPass m_explosionRangeExclusionPass;
    private MeshRenderer m_landingMarkerRenderer;
    private Mesh m_landingMarkerMesh;
    private Material m_runtimeLineMaterial;
    private Material m_runtimeLandingMarkerMaterial;
    private bool m_wasThrowModeActive;
    private bool m_throwWasHeld;
    private Vector3 m_throwStart;
    private Vector3 m_initialVelocity;
    private bool m_hasPlannedCollision;
    private float m_plannedCollisionTime;
    private Vector3 m_plannedCollisionPosition;
    private Vector3 m_plannedContactPoint;
    private Vector3 m_plannedSurfaceNormal;
    private bool m_hasExplosionPreview;
    private Vector3 m_explosionPreviewCenter;
    private int m_trajectoryPointCount;
    private float m_nextThrowReadyTime;
    private float m_pendingThrowTime = -1.0f;
    private ProjectileBase m_pendingProjectile;
    private bool m_crosshairModeInitialized;
    private bool m_throwCrosshairActive;
    private bool m_hasGrenadeModeParameter;
    private bool m_hasThrowParameter;
    private bool m_hasGrenadeReadyState;
    private int m_grenadeActionLayerIndex = -1;
    // 투척물별 손 Visual을 미리 만들어 두고 선택이 바뀌면 활성 대상만 교체합니다.
    private readonly Dictionary<ProjectileBase, GameObject> m_heldVisualInstances = new Dictionary<ProjectileBase, GameObject>();
    private GameObject m_defaultHeldVisualInstance;
    private GameObject m_heldGrenadeVisualInstance;
    private Renderer[] m_weaponRenderers;
    private bool[] m_weaponRendererEnabledStates;
    private bool m_grenadeEquipmentVisible;

    /// <summary>마우스 휠로 순환 선택하는 투척물 Prefab 목록입니다.</summary>
    public IReadOnlyList<ProjectileBase> ProjectilePrefabs => m_projectilePrefabs;

    /// <summary>Play Mode 시작 시 공용 크로스헤어 임시 적용 상태(정적 필드)를 비웁니다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCrosshairOverrideState()
    {
        // Domain Reload를 끈 Play Mode에서도 이전 세션의 Unity 오브젝트 참조를 들고 있지 않습니다.
        s_crosshairOverrideOwner = null;
        s_crosshairOverrideTarget = null;
        s_crosshairOverrideBaseline = null;
    }

    /// <summary>
    /// 같은 오브젝트의 입력·조준·Animator를 연결하고, 손 Visual과 미리보기 오브젝트를 만들어 둡니다.
    /// </summary>
    private void Awake()
    {
        MigrateLegacyProjectile();
        m_input = GetComponent<PlayerInputController>();
        m_aimController = GetComponent<AimController>();
        m_animator = GetComponent<Animator>();
        CacheGrenadeAnimationParameters();
        InitializeGrenadeEquipmentVisuals();
        m_sourceCollider = GetComponent<Collider>();
        ResolveInventoryManager();
        ResolveGrenadeSelectionUI();
        CreateTrajectoryLine();
        ApplyCrosshairMode(false);
    }

    /// <summary>폭발 범위 제외 스텐실 패스를 끼워 넣기 위해 카메라 렌더 시작 이벤트를 구독합니다.</summary>
    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    /// <summary>
    /// 매 프레임 투척 흐름 전체를 진행합니다.
    /// </summary>
    /// <remarks>
    /// 순서: 예약된 투척 생성 → 애니메이션·손 Visual·크로스헤어 전환 → 선택 UI 갱신 →
    /// 휠 선택 반영 → (Ready 상태이고 쿨다운이 끝났으면) 궤적 계산·미리보기 → 좌클릭을 누른 순간이면 투척.
    /// 직접 조작 중이 아닌 대원, 투척 모드가 아닐 때, 달리는 중에는 미리보기를 숨기고 투척하지 않습니다.
    /// 투척 모드에 들어온 첫 프레임의 클릭은 무시합니다.
    /// </remarks>
    private void LateUpdate()
    {
        // 투척 애니메이션이 이미 시작됐으므로 투척 모드 해제나 조작 대원 전환과 무관하게 예약된 투척물은 생성합니다.
        UpdatePendingThrow();

        // 공용 HUD는 현재 직접 조작 중인 캐릭터만 변경합니다. AI가 된 이전 캐릭터가
        // 자기 입력 상태로 수류탄 프리셋을 다시 쓰면 일반 크로스헤어와 값이 섞입니다.
        bool ownsPlayerCrosshair = m_aimController != null && m_aimController.IsPlayerControlled;
        bool throwModeActive = ownsPlayerCrosshair && m_input != null && m_input.ThrowMode;
        SetGrenadeAnimationMode(throwModeActive);
        SetGrenadeEquipmentVisible(throwModeActive);
        SetHeldGrenadeVisible(throwModeActive && !IsGrenadeThrowAnimationActive());
        ApplyCrosshairMode(throwModeActive);

        bool ownsSelectionUI = m_aimController != null
            ? m_aimController.IsPlayerControlled
            : m_input != null && m_input.isActiveAndEnabled;
        if (!ownsSelectionUI)
        {
            UpdateGrenadeSelectionUI(false);
            HideTrajectory();
            m_wasThrowModeActive = false;
            m_throwWasHeld = false;
            return;
        }

        UpdateGrenadeSelectionUI(throwModeActive);

        if (m_input == null || m_aimController == null || !throwModeActive)
        {
            HideTrajectory();
            m_wasThrowModeActive = false;
            m_throwWasHeld = false;
            return;
        }

        bool enteredThrowModeThisFrame = !m_wasThrowModeActive;
        CycleProjectile(m_input.ConsumeThrowSelectionDelta());
        UpdateGrenadeSelectionUI(true);
        if (enteredThrowModeThisFrame)
        {
            PlaySelectedProjectileEquipSound();
        }

        bool throwHeld = m_input.Throw;

        if (m_input.Sprint)
        {
            HideTrajectory();
            m_wasThrowModeActive = true;
            m_throwWasHeld = throwHeld;
            return;
        }

        // 꺼내기 모션 중에는 DoThrow가 Ready까지 보류되어 투척물과 모션이 어긋나므로 Ready에서만 경로 표시와 투척을 허용합니다.
        bool canThrow = Time.time >= m_nextThrowReadyTime && !HasPendingThrow && IsGrenadeReadyToThrow();

        if (canThrow)
        {
            ResolveTrajectory();
            BuildTrajectoryPlan(GetSelectedProjectile());
            DrawTrajectory();

            if (!enteredThrowModeThisFrame && throwHeld && !m_throwWasHeld && TryStartThrow())
            {
                PlayThrowAnimation();
                m_nextThrowReadyTime = Time.time + Mathf.Max(m_throwCooldown, m_throwReleaseDelay);
                HideTrajectory();
            }
        }
        else
        {
            HideTrajectory();
        }

        m_wasThrowModeActive = true;
        m_throwWasHeld = throwHeld;
    }

    /// <summary>
    /// 투척 모드에서 바꿔 둔 애니메이션·총기 표시·UI·크로스헤어를 되돌리고, 미리보기와 예약된 투척을 취소합니다.
    /// </summary>
    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        SetGrenadeAnimationMode(false);
        SetGrenadeEquipmentVisible(false);
        if (m_hasThrowParameter)
        {
            m_animator.ResetTrigger(AnimIDThrow);
        }

        UpdateGrenadeSelectionUI(false);
        ApplyCrosshairMode(false);
        m_crosshairModeInitialized = false;
        HideTrajectory();
        m_wasThrowModeActive = false;
        m_throwWasHeld = false;
        CancelPendingThrow();
    }

    /// <summary>
    /// 공용 크로스헤어를 소유 중이면 원래 값으로 돌려놓고, 런타임에 만든 손 Visual·머티리얼·메시를 지웁니다.
    /// </summary>
    private void OnDestroy()
    {
        ReleaseCrosshairOverrideIfOwned();

        foreach (GameObject heldVisualInstance in m_heldVisualInstances.Values)
        {
            if (heldVisualInstance != null)
            {
                Destroy(heldVisualInstance);
            }
        }

        m_heldVisualInstances.Clear();

        if (m_defaultHeldVisualInstance != null)
        {
            Destroy(m_defaultHeldVisualInstance);
        }

        m_heldGrenadeVisualInstance = null;

        if (m_runtimeLineMaterial != null)
        {
            Destroy(m_runtimeLineMaterial);
        }

        if (m_runtimeExplosionRangeMaterial != null)
        {
            Destroy(m_runtimeExplosionRangeMaterial);
        }

        if (m_runtimeLandingMarkerMaterial != null)
        {
            Destroy(m_runtimeLandingMarkerMaterial);
        }

        if (m_landingMarkerMesh != null)
        {
            Destroy(m_landingMarkerMesh);
        }
    }

    /// <summary>인스펙터에서 값이 바뀔 때 예전 단일 투척물 필드를 목록으로 옮기고 선택 인덱스를 범위 안으로 맞춥니다.</summary>
    private void OnValidate()
    {
        MigrateLegacyProjectile();
    }

    /// <summary>
    /// Animator에 투척용 파라미터(IsGrenadeMode, DoThrow)와 Grenade Action Layer의 Ready 상태가 있는지 미리 확인해 둡니다.
    /// </summary>
    /// <remarks>없는 파라미터를 Set하면 매 프레임 경고가 나므로, 있는 것만 건드리기 위해 캐시합니다.</remarks>
    private void CacheGrenadeAnimationParameters()
    {
        m_hasGrenadeModeParameter = false;
        m_hasThrowParameter = false;
        m_hasGrenadeReadyState = false;
        m_grenadeActionLayerIndex = -1;

        if (m_animator == null)
        {
            return;
        }

        m_grenadeActionLayerIndex = m_animator.GetLayerIndex(GrenadeActionLayerName);
        m_hasGrenadeReadyState = m_grenadeActionLayerIndex >= 0
                                 && m_animator.HasState(m_grenadeActionLayerIndex, AnimStateGrenadeReady);

        foreach (AnimatorControllerParameter parameter in m_animator.parameters)
        {
            if (parameter.nameHash == AnimIDGrenadeMode
                && parameter.type == AnimatorControllerParameterType.Bool)
            {
                m_hasGrenadeModeParameter = true;
            }
            else if (parameter.nameHash == AnimIDThrow
                     && parameter.type == AnimatorControllerParameterType.Trigger)
            {
                m_hasThrowParameter = true;
            }
        }
    }

    /// <summary>Animator의 IsGrenadeMode 값을 바꿔 투척 자세로 들어가거나 빠져나옵니다.</summary>
    private void SetGrenadeAnimationMode(bool active)
    {
        if (m_hasGrenadeModeParameter)
        {
            m_animator.SetBool(AnimIDGrenadeMode, active);
        }
    }

    /// <summary>손에 든 수류탄 Visual을 숨기고 DoThrow 트리거로 투척 모션을 재생합니다.</summary>
    private void PlayThrowAnimation()
    {
        if (m_hasThrowParameter)
        {
            SetHeldGrenadeVisible(false);
            m_animator.SetTrigger(AnimIDThrow);
        }
    }

    /// <summary>
    /// Grenade Action Layer가 서기 또는 웅크리기 투척 상태를 재생 중인지 확인합니다.
    /// </summary>
    private bool IsGrenadeThrowAnimationActive()
    {
        if (m_animator == null || m_grenadeActionLayerIndex < 0
                               || m_grenadeActionLayerIndex >= m_animator.layerCount)
        {
            return false;
        }

        AnimatorStateInfo current = m_animator.GetCurrentAnimatorStateInfo(m_grenadeActionLayerIndex);
        if (IsGrenadeThrowState(current))
        {
            return true;
        }

        return m_animator.IsInTransition(m_grenadeActionLayerIndex)
               && IsGrenadeThrowState(m_animator.GetNextAnimatorStateInfo(m_grenadeActionLayerIndex));
    }

    /// <summary>
    /// Grenade Action Layer가 꺼내기/투척 모션을 마치고 Ready 상태에 머물러 있는지 확인합니다.
    /// Ready 상태가 없는 Animator는 기존처럼 즉시 투척을 허용합니다.
    /// </summary>
    private bool IsGrenadeReadyToThrow()
    {
        if (m_animator == null || !m_hasGrenadeReadyState
                               || m_grenadeActionLayerIndex >= m_animator.layerCount)
        {
            return true;
        }

        return !m_animator.IsInTransition(m_grenadeActionLayerIndex)
               && m_animator.GetCurrentAnimatorStateInfo(m_grenadeActionLayerIndex).shortNameHash
               == AnimStateGrenadeReady;
    }

    /// <summary>주어진 상태가 서기 또는 웅크리기 투척 상태인지 확인합니다.</summary>
    private static bool IsGrenadeThrowState(AnimatorStateInfo state)
    {
        return state.shortNameHash == AnimStateGrenadeThrow
               || state.shortNameHash == AnimStateGrenadeCrouchThrow;
    }

    /// <summary>
    /// 총기 표시 상태는 유지하고 손에 든 수류탄 Visual만 전환합니다.
    /// </summary>
    private void SetHeldGrenadeVisible(bool visible)
    {
        if (m_heldGrenadeVisualInstance != null
            && m_heldGrenadeVisualInstance.activeSelf != visible)
        {
            m_heldGrenadeVisualInstance.SetActive(visible);
        }
    }

    /// <summary>
    /// 손에 표시할 수류탄과 숨길 총기 Renderer를 한 번 준비합니다.
    /// </summary>
    private void InitializeGrenadeEquipmentVisuals()
    {
        // 활성 총을 먼저 찾습니다. 비활성으로 남은 옛 총을 잡으면 투척 자세에서 실제 들고 있는 총이 숨지 않습니다.
        Gun activeWeapon = GetComponentInChildren<Gun>(false);
        Gun weapon = activeWeapon != null ? activeWeapon : GetComponentInChildren<Gun>(true);
        if (weapon != null)
        {
            m_weaponRenderers = weapon.GetComponentsInChildren<Renderer>(true);
            m_weaponRendererEnabledStates = new bool[m_weaponRenderers.Length];
        }

        bool hasProjectileHeldVisual = false;
        for (int i = 0; m_projectilePrefabs != null && i < m_projectilePrefabs.Count; i++)
        {
            if (m_projectilePrefabs[i] != null && m_projectilePrefabs[i].HeldVisualPrefab != null)
            {
                hasProjectileHeldVisual = true;
                break;
            }
        }

        if (m_heldGrenadeVisualPrefab == null && !hasProjectileHeldVisual)
        {
            return;
        }

        Transform heldSocket = m_heldGrenadeSocket;
        if (heldSocket == null && m_animator != null && m_animator.isHuman)
        {
            heldSocket = m_animator.GetBoneTransform(HumanBodyBones.RightHand);
        }

        if (heldSocket == null)
        {
            Debug.LogWarning($"[{name}] 수류탄 Visual을 연결할 손 장착 소켓을 찾지 못했습니다.", this);
            return;
        }

        if (m_heldGrenadeVisualPrefab != null)
        {
            m_defaultHeldVisualInstance = CreateHeldVisual(
                m_heldGrenadeVisualPrefab,
                heldSocket,
                m_heldGrenadeLocalPosition,
                Quaternion.Euler(m_heldGrenadeLocalEulerAngles),
                m_heldGrenadeLocalScale);
        }

        for (int i = 0; m_projectilePrefabs != null && i < m_projectilePrefabs.Count; i++)
        {
            ProjectileBase projectile = m_projectilePrefabs[i];
            if (projectile == null
                || projectile.HeldVisualPrefab == null
                || m_heldVisualInstances.ContainsKey(projectile))
            {
                continue;
            }

            m_heldVisualInstances[projectile] = CreateHeldVisual(
                projectile.HeldVisualPrefab,
                heldSocket,
                projectile.HeldLocalPosition,
                projectile.HeldLocalRotation,
                projectile.HeldLocalScale);
        }

        RefreshHeldVisualSelection();
    }

    /// <summary>손 소켓 아래에 Visual Prefab을 비활성 상태로 생성하고 위치·회전·크기를 맞춥니다.</summary>
    private static GameObject CreateHeldVisual(
        GameObject prefab,
        Transform socket,
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale)
    {
        GameObject instance = Instantiate(prefab, socket, false);
        instance.name = $"{prefab.name} (Held)";

        Transform heldTransform = instance.transform;
        heldTransform.SetLocalPositionAndRotation(localPosition, localRotation);
        heldTransform.localScale = localScale;
        instance.SetActive(false);
        return instance;
    }

    /// <summary>
    /// 현재 선택한 투척물에 맞는 손 Visual로 교체합니다. 투척물 전용 Visual이 없으면 기본 Visual을 사용합니다.
    /// </summary>
    private void RefreshHeldVisualSelection()
    {
        ProjectileBase selectedProjectile = GetSelectedProjectile();
        GameObject nextVisual = null;
        if (selectedProjectile == null
            || !m_heldVisualInstances.TryGetValue(selectedProjectile, out nextVisual))
        {
            nextVisual = m_defaultHeldVisualInstance;
        }

        if (nextVisual == m_heldGrenadeVisualInstance)
        {
            return;
        }

        bool wasVisible = m_heldGrenadeVisualInstance != null && m_heldGrenadeVisualInstance.activeSelf;
        if (m_heldGrenadeVisualInstance != null)
        {
            m_heldGrenadeVisualInstance.SetActive(false);
        }

        m_heldGrenadeVisualInstance = nextVisual;
        if (m_heldGrenadeVisualInstance != null)
        {
            m_heldGrenadeVisualInstance.SetActive(wasVisible);
        }
    }

    /// <summary>
    /// 수류탄 모드에서는 총기 메시를 숨기고 오른손 수류탄 Visual을 표시합니다.
    /// </summary>
    private void SetGrenadeEquipmentVisible(bool visible)
    {
        if (m_grenadeEquipmentVisible == visible)
        {
            return;
        }

        m_grenadeEquipmentVisible = visible;

        if (visible)
        {
            for (int i = 0; m_weaponRenderers != null && i < m_weaponRenderers.Length; i++)
            {
                Renderer weaponRenderer = m_weaponRenderers[i];
                if (weaponRenderer == null)
                {
                    continue;
                }

                m_weaponRendererEnabledStates[i] = weaponRenderer.enabled;
                weaponRenderer.enabled = false;
            }

            if (m_heldGrenadeVisualInstance != null)
            {
                m_heldGrenadeVisualInstance.SetActive(true);
            }

            return;
        }

        if (m_heldGrenadeVisualInstance != null)
        {
            m_heldGrenadeVisualInstance.SetActive(false);
        }

        for (int i = 0; m_weaponRenderers != null && i < m_weaponRenderers.Length; i++)
        {
            Renderer weaponRenderer = m_weaponRenderers[i];
            if (weaponRenderer != null)
            {
                weaponRenderer.enabled = m_weaponRendererEnabledStates[i];
            }
        }
    }

    /// <summary>
    /// 예전 단일 투척물 필드(m_projectilePrefab)에 값이 남아 있고 목록이 비어 있으면 목록으로 옮깁니다.
    /// 선택 인덱스도 목록 범위 안으로 맞춥니다.
    /// </summary>
    private void MigrateLegacyProjectile()
    {
        if (m_projectilePrefabs == null)
        {
            m_projectilePrefabs = new List<ProjectileBase>();
        }

        if (m_projectilePrefabs.Count == 0 && m_legacyProjectilePrefab != null)
        {
            m_projectilePrefabs.Add(m_legacyProjectilePrefab);
            m_legacyProjectilePrefab = null;
        }

        m_selectedProjectileIndex = WrapIndex(m_selectedProjectileIndex, m_projectilePrefabs.Count);
    }

    /// <summary>휠 입력만큼 선택한 투척물을 앞뒤로 넘기고(끝에서 처음으로 순환), 손 Visual도 바꿉니다.</summary>
    private void CycleProjectile(int selectionDelta)
    {
        if (selectionDelta == 0 || m_projectilePrefabs == null || m_projectilePrefabs.Count == 0)
        {
            return;
        }

        m_selectedProjectileIndex = WrapIndex(
            m_selectedProjectileIndex + selectionDelta,
            m_projectilePrefabs.Count);
        RefreshHeldVisualSelection();
    }

    /// <summary>현재 선택한 투척물 Prefab을 돌려줍니다. 목록이 비어 있으면 예전 단일 필드 값을 씁니다.</summary>
    private ProjectileBase GetSelectedProjectile()
    {
        if (m_projectilePrefabs == null || m_projectilePrefabs.Count == 0)
        {
            return m_legacyProjectilePrefab;
        }

        m_selectedProjectileIndex = WrapIndex(m_selectedProjectileIndex, m_projectilePrefabs.Count);
        return m_projectilePrefabs[m_selectedProjectileIndex];
    }

    /// <summary>투척 모드에 들어갈 때 현재 선택한 투척물을 꺼내는 2D 장비음을 한 번 재생합니다.</summary>
    private void PlaySelectedProjectileEquipSound()
    {
        ProjectileBase selectedProjectile = GetSelectedProjectile();
        if (selectedProjectile == null || !SoundManager.IsReady)
        {
            return;
        }

        bool isMolotov = selectedProjectile is TemporaryTrapProjectile;
        string eventPath = isMolotov
            ? MolotovEquipSoundEventPath
            : GrenadeEquipSoundEventPath;

        try
        {
            SoundManager.Instance.PlayOneShot(eventPath);
        }
        catch (FMODUnity.EventNotFoundException exception)
        {
            bool alreadyLogged = isMolotov
                ? s_loggedMissingMolotovEquipSound
                : s_loggedMissingGrenadeEquipSound;
            if (alreadyLogged)
            {
                return;
            }

            if (isMolotov)
            {
                s_loggedMissingMolotovEquipSound = true;
            }
            else
            {
                s_loggedMissingGrenadeEquipSound = true;
            }

            Debug.LogWarning(
                $"[ExplosiveProjectileShooter] FMOD 이벤트를 찾지 못했습니다: {eventPath}\n{exception.Message}",
                this);
        }
    }

    /// <summary>
    /// 투척 경로 충돌에 쓸 Layer입니다. 공통 충돌 Layer에서 투척물이 무시하도록 지정한 Layer와 Ignore Raycast Layer를 뺍니다.
    /// </summary>
    /// <remarks>
    /// 맵 이탈을 막는 투명벽(Map Guard)은 Ignore Raycast Layer에 있고 플레이어블만 막습니다. 경로 표시와 실제 비행이 같은 값을 쓰므로
    /// 둘 다 투명벽을 지나갑니다.
    /// </remarks>
    private int GetProjectileCollisionLayers(ProjectileBase projectile)
    {
        int excludedLayers = projectile != null
            ? projectile.ContactExcludeLayers.value
            : 0;
        return m_collisionLayers.value & ~excludedLayers & ~Physics.IgnoreRaycastLayer;
    }

    /// <summary>인덱스를 0 ~ count-1 범위로 순환시킵니다. 음수도 뒤에서부터 셉니다.</summary>
    private static int WrapIndex(int index, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        return (index % count + count) % count;
    }

    /// <summary>선택 UI가 연결되지 않았으면 Scene에서 찾아 연결합니다.</summary>
    private void ResolveGrenadeSelectionUI()
    {
        if (m_grenadeSelectionUI == null)
        {
            m_grenadeSelectionUI = FindFirstObjectByType<GrenadeSelectionUI>(FindObjectsInactive.Include);
        }
    }

    /// <summary>선택 UI의 표시 여부와, 표시할 투척물 아이콘·남은 수량을 갱신합니다.</summary>
    private void UpdateGrenadeSelectionUI(bool visible)
    {
        ResolveGrenadeSelectionUI();
        if (m_grenadeSelectionUI != null)
        {
            ProjectileBase selectedProjectile = GetSelectedProjectile();
            m_grenadeSelectionUI.SetState(
                this,
                visible,
                selectedProjectile,
                GetProjectileQuantity(selectedProjectile));
        }
    }

    /// <summary>분대 공용 인벤토리가 연결되지 않았으면 Scene에서 찾아 연결합니다.</summary>
    private void ResolveInventoryManager()
    {
        if (m_inventoryManager == null)
        {
            m_inventoryManager = FindFirstObjectByType<SquadInventoryManager>(FindObjectsInactive.Include);
        }
    }

    /// <summary>공용 인벤토리에 남은 해당 투척물 수량입니다. 투척물이나 인벤토리가 없으면 0입니다.</summary>
    private int GetProjectileQuantity(ProjectileBase projectile)
    {
        ResolveInventoryManager();
        return projectile != null && m_inventoryManager != null
            ? m_inventoryManager.CountOf(projectile.InventoryItemDefinitionId)
            : 0;
    }

    /// <summary>
    /// 투척 모드에 들어가면 공용 크로스헤어에 투척 프리셋을 적용하고, 빠져나오면 원래 값으로 돌려놓습니다.
    /// </summary>
    /// <remarks>
    /// 처음 적용할 때 원래 값을 정적 필드에 저장하고 이 대원을 소유자로 기록합니다. 다른 대원이 소유 중이었다면
    /// 그 값을 먼저 복원한 뒤 넘겨받습니다. 상태가 이미 원하는 대로면 매 프레임 다시 적용하지 않습니다.
    /// </remarks>
    private void ApplyCrosshairMode(bool throwModeActive)
    {
        if (m_crosshairModeInitialized && m_throwCrosshairActive == throwModeActive)
        {
            bool ownsExpectedOverride = ReferenceEquals(s_crosshairOverrideOwner, this)
                && s_crosshairOverrideTarget == m_defaultCrosshair;
            if ((throwModeActive && ownsExpectedOverride)
                || (!throwModeActive && !ReferenceEquals(s_crosshairOverrideOwner, this)))
            {
                return;
            }
        }

        ResolveDefaultCrosshair();
        if (throwModeActive)
        {
            if (m_defaultCrosshair != null && m_throwCrosshairPreset != null)
            {
                bool alreadyOwnsTarget = ReferenceEquals(s_crosshairOverrideOwner, this)
                    && s_crosshairOverrideTarget == m_defaultCrosshair;
                if (!alreadyOwnsTarget)
                {
                    RestoreAndClearCrosshairOverride();
                    s_crosshairOverrideOwner = this;
                    s_crosshairOverrideTarget = m_defaultCrosshair;
                    s_crosshairOverrideBaseline = CrosshairPreset.Capture(m_defaultCrosshair);
                }

                m_throwCrosshairPreset.Apply(m_defaultCrosshair);
            }
        }
        else
        {
            ReleaseCrosshairOverrideIfOwned();
        }

        m_throwCrosshairActive = throwModeActive
            && ReferenceEquals(s_crosshairOverrideOwner, this);
        m_crosshairModeInitialized = true;
    }

    /// <summary>
    /// 이 대원이 소유한 투척 조준선 임시값을 즉시 해제하고, 적용 전 공용 HUD 값으로 복원합니다.
    /// </summary>
    /// <remarks>
    /// 조작권을 다음 대원에게 넘기기 전에 호출해야 새 대원의 프로필 위로 이전 대원의 스냅샷이
    /// 뒤늦게 덮이지 않습니다. 소유자가 아닌 대원은 공용 HUD를 변경하지 않습니다.
    /// </remarks>
    public void ReleaseCrosshairOverrideIfOwned()
    {
        if (ReferenceEquals(s_crosshairOverrideOwner, this))
        {
            RestoreAndClearCrosshairOverride();
        }

        m_throwCrosshairActive = false;
        m_crosshairModeInitialized = true;
    }

    /// <summary>투척 모드에서 쓰는 조준선 모양 값의 복사본입니다.</summary>
    public CrosshairStyle ThrowCrosshairStyle => m_throwCrosshairPreset != null
        ? m_throwCrosshairPreset.ToStyle()
        : null;

    /// <summary>
    /// 투척 모드 조준선 모양을 바꿉니다. 이 대원이 지금 투척 모드면 공용 HUD에도 바로 적용합니다.
    /// </summary>
    /// <remarks>탄퍼짐 연동 여부는 설정 화면에서 다루지 않으므로 이 컴포넌트의 기존 값을 유지합니다.</remarks>
    public void SetThrowCrosshairStyle(CrosshairStyle style)
    {
        if (style == null)
        {
            return;
        }

        bool useSpreadAccuracy = m_throwCrosshairPreset != null && m_throwCrosshairPreset.ToStyle().useSpreadAccuracy;
        m_throwCrosshairPreset = CrosshairPreset.FromStyle(style, useSpreadAccuracy);
        if (ReferenceEquals(s_crosshairOverrideOwner, this) && s_crosshairOverrideTarget != null)
        {
            m_throwCrosshairPreset.Apply(s_crosshairOverrideTarget);
        }
    }

    /// <summary>
    /// 투척 모드가 공용 HUD를 덮고 있으면, 투척 모드가 끝난 뒤 복원할 값을 바꿉니다.
    /// </summary>
    /// <remarks>
    /// 투척 모드 중에 캐릭터 조준선 설정이 바뀌면 지금 화면은 그대로 두고, 끝날 때 새 값으로 돌아가게 하려는 것입니다.
    /// 복원 값의 탄퍼짐 연동 여부는 투척 모드 진입 전 값을 유지합니다.
    /// </remarks>
    /// <returns>투척 모드가 <paramref name="target"/>을 덮고 있어 복원 값을 바꿨으면 true입니다.</returns>
    public static bool TryReplaceCrosshairBaseline(CrosshairController target, CrosshairStyle style)
    {
        if (target == null || style == null || ReferenceEquals(s_crosshairOverrideOwner, null)
            || s_crosshairOverrideTarget != target || s_crosshairOverrideBaseline == null)
        {
            return false;
        }

        bool useSpreadAccuracy = s_crosshairOverrideBaseline.ToStyle().useSpreadAccuracy;
        s_crosshairOverrideBaseline = CrosshairPreset.FromStyle(style, useSpreadAccuracy);
        return true;
    }

    /// <summary>
    /// 소유자가 누구든 공용 크로스헤어를 저장해 둔 원래 값으로 되돌리고, 소유 기록을 지웁니다.
    /// </summary>
    private static void RestoreAndClearCrosshairOverride()
    {
        CrosshairController target = s_crosshairOverrideTarget;
        CrosshairPreset baseline = s_crosshairOverrideBaseline;
        ExplosiveProjectileShooter previousOwner = s_crosshairOverrideOwner;

        // 복원 중 다른 코드가 현재 소유자를 조회해도 이미 임시 레이어가 끝난 상태로 보이게 합니다.
        s_crosshairOverrideOwner = null;
        s_crosshairOverrideTarget = null;
        s_crosshairOverrideBaseline = null;

        if (target != null && baseline != null)
        {
            baseline.Apply(target);
        }

        if (!ReferenceEquals(previousOwner, null))
        {
            previousOwner.m_throwCrosshairActive = false;
            previousOwner.m_crosshairModeInitialized = true;
        }
    }

    /// <summary>크로스헤어가 연결되지 않았으면 AimController에서, 그래도 없으면 Scene에서 찾아 연결합니다.</summary>
    private void ResolveDefaultCrosshair()
    {
        if (m_defaultCrosshair != null)
        {
            return;
        }

        if (m_aimController != null)
        {
            m_defaultCrosshair = m_aimController.CrosshairController;
        }

        if (m_defaultCrosshair == null)
        {
            m_defaultCrosshair = FindFirstObjectByType<CrosshairController>(FindObjectsInactive.Include);
        }
    }

    /// <summary>
    /// 손 위치와 조준점으로 투척 시작점(<see cref="m_throwStart"/>)과 초기 속도(<see cref="m_initialVelocity"/>)를 정합니다.
    /// </summary>
    /// <remarks>
    /// 던지는 속력은 항상 같습니다. 수평으로 조준했을 때 <see cref="m_referenceThrowDistance"/>만큼 날아가고
    /// <see cref="m_arcHeight"/>만큼 솟도록 속력과 기본 발사각을 구한 뒤, 기본 발사각에 조준 상하각을 더해
    /// ±80도로 자릅니다. 그래서 착탄 거리는 조준각에 따라 달라지며, 발사각 45도 근처에서 가장 멉니다.
    /// 착탄 지점을 정해 두고 역산하는 방식이 아닙니다.
    /// </remarks>
    private void ResolveTrajectory()
    {
        m_throwStart = ResolveThrowStartPosition();

        Vector3 aimDirection = m_aimController.CurrentAimPoint - m_throwStart;
        if (aimDirection.sqrMagnitude < 0.0001f)
        {
            aimDirection = transform.forward;
        }

        aimDirection.Normalize();

        Vector3 horizontalDirection = Vector3.ProjectOnPlane(aimDirection, Vector3.up);
        if (horizontalDirection.sqrMagnitude < 0.0001f)
        {
            horizontalDirection = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        }

        horizontalDirection.Normalize();

        float ascentDownwardAcceleration = Mathf.Max(0.01f, m_ascentDownwardAcceleration);
        float descentDownwardAcceleration = Mathf.Max(0.01f, m_descentDownwardAcceleration);
        float upwardSpeed = m_arcHeight > 0.0f
            ? Mathf.Sqrt(2.0f * ascentDownwardAcceleration * m_arcHeight)
            : 0.0f;
        float ascentTime = upwardSpeed > 0.0f
            ? upwardSpeed / ascentDownwardAcceleration
            : 0.0f;
        float descentTime = m_arcHeight > 0.0f
            ? Mathf.Sqrt(2.0f * m_arcHeight / descentDownwardAcceleration)
            : 0.0f;
        float referenceFlightTime = Mathf.Max(0.01f, ascentTime + descentTime);
        float horizontalSpeed = m_referenceThrowDistance / referenceFlightTime;
        float launchSpeed = Mathf.Sqrt(horizontalSpeed * horizontalSpeed + upwardSpeed * upwardSpeed);
        float baseLaunchAngle = Mathf.Atan2(upwardSpeed, horizontalSpeed);
        float aimPitch = Mathf.Asin(Mathf.Clamp(aimDirection.y, -1.0f, 1.0f));
        float launchAngle = Mathf.Clamp(
            baseLaunchAngle + aimPitch,
            -80.0f * Mathf.Deg2Rad,
            80.0f * Mathf.Deg2Rad);

        m_initialVelocity =
            horizontalDirection * (Mathf.Cos(launchAngle) * launchSpeed) +
            Vector3.up * (Mathf.Sin(launchAngle) * launchSpeed);
    }

    /// <summary>
    /// 투척 시작점입니다. 손 소켓이 있으면 그 위치를, 없으면 Collider 중심에 fallback 오프셋을 더한 위치를 쓰고,
    /// 마지막으로 <see cref="m_throwStartOffset"/>을 더합니다.
    /// </summary>
    private Vector3 ResolveThrowStartPosition()
    {
        Vector3 origin = m_heldGrenadeSocket != null
            ? m_heldGrenadeSocket.position
            : (m_sourceCollider != null ? m_sourceCollider.bounds.center : transform.position)
                + transform.TransformDirection(m_throwOriginOffset);

        return origin + transform.TransformDirection(m_throwStartOffset);
    }

    /// <summary>계산해 둔 궤적 점으로 궤적 선(켜져 있을 때만)과 폭발 범위·착탄 마커를 그립니다.</summary>
    private void DrawTrajectory()
    {
        // 궤적 선은 토글이 켜졌을 때만 그립니다. 기본은 착탄 지점의 원형 표시만 씁니다.
        if (m_showTrajectoryLine)
        {
            m_trajectoryLine.enabled = true;
            m_trajectoryLine.positionCount = m_trajectoryPointCount;

            for (int i = 0; i < m_trajectoryPointCount; i++)
            {
                m_trajectoryLine.SetPosition(i, m_trajectoryPoints[i]);
            }
        }
        else
        {
            m_trajectoryLine.enabled = false;
            m_trajectoryLine.positionCount = 0;
        }

        DrawExplosionPreview();
    }

    /// <summary>
    /// 현재 초기 속도로 포물선을 따라가며 궤적 점을 모으고, 처음 부딪히는 지점(예정 충돌)과 폭발 미리보기 위치를 정합니다.
    /// </summary>
    /// <remarks>
    /// 구간마다 길이가 대략 일정하도록 속도에 맞춰 시간 간격을 바꿔 가며 샘플링하고, 구간마다 SphereCast로 충돌을 봅니다.
    /// 다음 중 먼저 오는 곳에서 멈춥니다.
    /// <list type="bullet">
    /// <item>충돌: 예정 충돌 정보를 기록하고 그 위치에 폭발 미리보기를 둡니다. 실제 투척물은 이 정보를 받아 같은 곳에서 터집니다.</item>
    /// <item>신관 시간 끝: 그 위치에 폭발 미리보기를 둡니다.</item>
    /// <item>누적 경로 길이가 <see cref="m_trajectoryPreviewDistance"/>에 닿음: 폭발 미리보기 없이 멈춥니다.
    /// 이때는 착탄 마커와 폭발 범위가 표시되지 않고, 이후 충돌은 실제 비행 중에 판정합니다.</item>
    /// </list>
    /// 신관 시간에 투척 속도 배율을 곱하는 이유는 궤적 시간 축이 배율 적용 전 기준이기 때문입니다.
    /// </remarks>
    private void BuildTrajectoryPlan(ProjectileBase projectile)
    {
        int segmentCount = Mathf.Clamp(m_trajectorySegments, 4, MaxTrajectoryPointCount - 1);
        m_trajectoryPointCount = 1;
        m_trajectoryPoints[0] = m_throwStart;
        m_hasPlannedCollision = false;
        m_plannedCollisionTime = 0.0f;
        m_plannedCollisionPosition = m_throwStart;
        m_plannedContactPoint = m_throwStart;
        m_plannedSurfaceNormal = Vector3.up;
        m_hasExplosionPreview = false;
        m_explosionPreviewCenter = m_throwStart;

        Vector3 previous = m_throwStart;
        float previousTime = 0.0f;
        float previewDistance = Mathf.Max(0.1f, m_trajectoryPreviewDistance);
        float targetSegmentLength = previewDistance / segmentCount;
        float accumulatedDistance = 0.0f;
        float throwSpeedMultiplier = Mathf.Max(0.01f, m_throwSpeedMultiplier);
        int collisionLayers = GetProjectileCollisionLayers(projectile);
        float trajectoryTimeLimit = projectile != null
            ? Mathf.Max(0.0f, projectile.FlightTimeLimit) * throwSpeedMultiplier
            : float.PositiveInfinity;

        if (trajectoryTimeLimit <= 0.0f)
        {
            m_hasExplosionPreview = true;
            return;
        }

        for (int i = 1; i <= segmentCount; i++)
        {
            Vector3 currentVelocity = ParabolicProjectileMover.EvaluateVelocity(
                m_initialVelocity,
                m_ascentDownwardAcceleration,
                m_descentDownwardAcceleration,
                previousTime);
            float sampleInterval = Mathf.Clamp(
                targetSegmentLength / Mathf.Max(currentVelocity.magnitude, 1.0f),
                0.01f,
                0.25f);
            float currentTime = Mathf.Min(previousTime + sampleInterval, trajectoryTimeLimit);

            if (currentTime <= previousTime)
            {
                return;
            }

            Vector3 next = ParabolicProjectileMover.EvaluatePosition(
                m_throwStart,
                m_initialVelocity,
                m_ascentDownwardAcceleration,
                m_descentDownwardAcceleration,
                currentTime);
            Vector3 segment = next - previous;
            float segmentDistance = segment.magnitude;
            float remainingPreviewDistance = previewDistance - accumulatedDistance;
            bool reachedPreviewLimit = segmentDistance >= remainingPreviewDistance;

            if (reachedPreviewLimit && segmentDistance > 0.0001f)
            {
                float previewFraction = Mathf.Clamp01(remainingPreviewDistance / segmentDistance);
                currentTime = Mathf.Lerp(previousTime, currentTime, previewFraction);
                next = ParabolicProjectileMover.EvaluatePosition(
                    m_throwStart,
                    m_initialVelocity,
                    m_ascentDownwardAcceleration,
                    m_descentDownwardAcceleration,
                    currentTime);
                segment = next - previous;
                segmentDistance = segment.magnitude;
            }

            if (TryGetBlockingHit(previous, next, collisionLayers, out RaycastHit hit))
            {
                float hitFraction = segmentDistance > 0.0001f
                    ? Mathf.Clamp01(hit.distance / segmentDistance)
                    : 0.0f;

                m_hasPlannedCollision = true;
                m_plannedCollisionTime = Mathf.Lerp(previousTime, currentTime, hitFraction);
                m_plannedCollisionPosition = ParabolicProjectileMover.EvaluatePosition(
                    m_throwStart,
                    m_initialVelocity,
                    m_ascentDownwardAcceleration,
                    m_descentDownwardAcceleration,
                    m_plannedCollisionTime);
                m_plannedContactPoint = hit.point;
                m_plannedSurfaceNormal = hit.normal;
                m_hasExplosionPreview = true;
                m_explosionPreviewCenter = m_plannedCollisionPosition;

                if ((m_plannedCollisionPosition - previous).sqrMagnitude > 0.000001f)
                {
                    m_trajectoryPoints[m_trajectoryPointCount] = m_plannedCollisionPosition;
                    m_trajectoryPointCount++;
                }

                return;
            }

            m_trajectoryPoints[m_trajectoryPointCount] = next;
            m_trajectoryPointCount++;
            accumulatedDistance += segmentDistance;

            if (currentTime >= trajectoryTimeLimit)
            {
                m_hasExplosionPreview = true;
                m_explosionPreviewCenter = next;
                return;
            }

            if (reachedPreviewLimit)
            {
                return;
            }

            previous = next;
            previousTime = currentTime;
        }
    }

    /// <summary>지연 생성을 기다리는 투척이 있는지 여부입니다.</summary>
    private bool HasPendingThrow => m_pendingThrowTime >= 0.0f;

    /// <summary>
    /// 좌클릭 입력을 처리합니다. 지연 시간이 없으면 즉시 생성하고, 있으면 생성을 예약합니다.
    /// 예약된 투척의 도착 지점은 생성 순간의 조준으로 정해, 던지는 동안 돌아간 캐릭터 모션과 방향을 맞춥니다.
    /// </summary>
    private bool TryStartThrow()
    {
        ProjectileBase selectedProjectile = GetSelectedProjectile();
        if (!CanThrowProjectile(selectedProjectile))
        {
            return false;
        }

        if (m_throwReleaseDelay <= 0.0f)
        {
            return ThrowProjectile(selectedProjectile);
        }

        m_pendingProjectile = selectedProjectile;
        m_pendingThrowTime = Time.time + m_throwReleaseDelay;
        return true;
    }

    /// <summary>예약된 투척의 생성 시각이 되면 재고를 다시 확인하고, 그 순간의 조준으로 궤적을 다시 계산해 생성합니다.</summary>
    private void UpdatePendingThrow()
    {
        if (!HasPendingThrow || Time.time < m_pendingThrowTime)
        {
            return;
        }

        ProjectileBase projectile = m_pendingProjectile;
        CancelPendingThrow();

        // 대기 중 다른 대원이 같은 재고를 소모했을 수 있으므로 생성 직전에 다시 확인합니다.
        if (!CanThrowProjectile(projectile))
        {
            return;
        }

        // 클릭 이후 화면을 돌렸으면 캐릭터도 그쪽을 보고 있으므로, 생성 순간의 조준과 손 위치로 경로를 다시 정합니다.
        // 미리보기 상태도 함께 덮어쓰지만 다음 프레임 BuildTrajectoryPlan에서 다시 계산됩니다.
        ResolveTrajectory();
        BuildTrajectoryPlan(projectile);
        ThrowProjectile(projectile);
    }

    /// <summary>예약된 투척을 지웁니다.</summary>
    private void CancelPendingThrow()
    {
        m_pendingThrowTime = -1.0f;
        m_pendingProjectile = null;
    }

    /// <summary>
    /// 투척물 Prefab과 인벤토리 아이템 연결, 공용 인벤토리가 모두 있고 수량이 1개 이상인지 확인합니다.
    /// 설정이 빠진 경우에는 경고를 남깁니다.
    /// </summary>
    private bool CanThrowProjectile(ProjectileBase selectedProjectile)
    {
        if (selectedProjectile == null)
        {
            Debug.LogWarning($"[{name}] 투척할 Projectile Prefab이 없습니다.", this);
            return false;
        }

        if (string.IsNullOrWhiteSpace(selectedProjectile.InventoryItemDefinitionId))
        {
            Debug.LogWarning($"[{name}] '{selectedProjectile.name}'에 Inventory Item Definition이 연결되지 않았습니다.", this);
            return false;
        }

        ResolveInventoryManager();
        if (m_inventoryManager == null)
        {
            Debug.LogWarning($"[{name}] SquadInventoryManager를 찾지 못해 투척물을 사용할 수 없습니다.", this);
            return false;
        }

        if (!SquadInventoryManager.FreeUse && !SquadInventoryManager.UseWhenEmpty && GetProjectileQuantity(selectedProjectile) <= 0)
        {
            // 투척 시도마다 조준선 위에 경고를 다시 띄웁니다. 경고 문구가 없는 씬에서는 아무 일도 없습니다.
            AimWarningText.Show(m_outOfProjectileMessage);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 투척물을 시작점에 생성하고 <see cref="ParabolicProjectileMover"/>를 붙여 계산해 둔 궤적대로 날려 보낸 뒤, 재고를 1개 소모합니다.
    /// </summary>
    /// <remarks>
    /// 이동은 물리 시뮬레이션이 아니라 Mover가 직접 하므로 Rigidbody는 Kinematic으로, Collider는 Trigger로 바꿉니다.
    /// 재고 소모에 실패하면 생성한 투척물을 지우고 false를 돌려줍니다.
    /// </remarks>
    private bool ThrowProjectile(ProjectileBase selectedProjectile)
    {
        Quaternion rotation = m_initialVelocity.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(m_initialVelocity.normalized, Vector3.up)
            : transform.rotation;

        ProjectileBase projectile = Instantiate(selectedProjectile, m_throwStart, rotation);
        Collider[] projectileColliders = projectile.GetComponentsInChildren<Collider>(true);
        foreach (Collider projectileCollider in projectileColliders)
        {
            projectileCollider.isTrigger = true;
        }

        Rigidbody projectileRigidbody = projectile.GetComponent<Rigidbody>();
        projectileRigidbody.useGravity = false;
        projectileRigidbody.isKinematic = true;
        projectileRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        projectileRigidbody.linearVelocity = Vector3.zero;
        projectileRigidbody.angularVelocity = Vector3.zero;

        ParabolicProjectileMover mover = projectile.gameObject.AddComponent<ParabolicProjectileMover>();
        mover.Initialize(
            projectile,
            m_throwStart,
            m_initialVelocity,
            m_ascentDownwardAcceleration,
            m_descentDownwardAcceleration,
            m_throwSpeedMultiplier,
            m_hasPlannedCollision,
            m_plannedCollisionTime,
            m_plannedCollisionPosition,
            m_plannedContactPoint,
            m_plannedSurfaceNormal,
            m_collisionRadius,
            GetProjectileCollisionLayers(selectedProjectile),
            transform);

        if (!m_inventoryManager.TryConsumeOne(selectedProjectile.InventoryItemDefinitionId))
        {
            Destroy(projectile.gameObject);
            return false;
        }

        // 지연 생성 중 투척 모드가 끝났거나 조작 대원이 바뀌었으면 UI 표시는 이후 LateUpdate 흐름에 맡깁니다.
        if (m_aimController != null && m_aimController.IsPlayerControlled
                                    && m_input != null && m_input.ThrowMode)
        {
            UpdateGrenadeSelectionUI(true);
        }

        return true;
    }

    /// <summary>
    /// 두 점 사이를 <see cref="m_collisionRadius"/> 구체로 훑어 가장 가까운 충돌을 찾습니다. 던진 캐릭터 자신은 무시합니다.
    /// </summary>
    /// <remarks><see cref="ParabolicProjectileMover"/>에도 같은 역할의 메서드가 있습니다. 판정 규칙을 바꿀 때는 둘을 함께 바꿔야 미리보기와 실제 비행이 어긋나지 않습니다.</remarks>
    private bool TryGetBlockingHit(Vector3 start, Vector3 end, int collisionLayers, out RaycastHit nearestHit)
    {
        nearestHit = default;
        Vector3 movement = end - start;
        float distance = movement.magnitude;

        if (distance <= 0.0001f)
        {
            return false;
        }

        int hitCount = Physics.SphereCastNonAlloc(
            start,
            m_collisionRadius,
            movement / distance,
            m_previewHits,
            distance,
            collisionLayers,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit candidate = m_previewHits[i];
            if (candidate.collider == null || IsOwnedByPlayer(candidate.collider.transform))
            {
                continue;
            }

            if (candidate.distance < nearestDistance)
            {
                nearestDistance = candidate.distance;
                nearestHit = candidate;
                found = true;
            }
        }

        return found;
    }

    /// <summary>맞은 대상이 이 캐릭터 자신이거나 그 자식인지 확인합니다.</summary>
    private bool IsOwnedByPlayer(Transform hitTransform)
    {
        return hitTransform == transform || hitTransform.IsChildOf(transform);
    }

    /// <summary>
    /// 미리보기 오브젝트를 모두 만듭니다. 궤적 선, 폭발 범위 구체, 착탄 마커를 만들고 선과 마커에 쓸 머티리얼을 붙입니다.
    /// </summary>
    private void CreateTrajectoryLine()
    {
        GameObject lineObject = new GameObject("ThrowTrajectoryPreview");
        lineObject.transform.SetParent(transform, false);

        m_trajectoryLine = lineObject.AddComponent<LineRenderer>();
        ConfigurePreviewLine(m_trajectoryLine, false);

        CreateExplosionRangePreview();
        CreateLandingMarker();

        Shader lineShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (lineShader != null)
        {
            m_runtimeLineMaterial = new Material(lineShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            m_runtimeLineMaterial.SetColor("_BaseColor", m_trajectoryColor);
            m_trajectoryLine.sharedMaterial = m_runtimeLineMaterial;

            m_runtimeLandingMarkerMaterial = new Material(lineShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            ConfigureTransparentPreviewMaterial(m_runtimeLandingMarkerMaterial);
            m_runtimeLandingMarkerMaterial.SetFloat("_Cull", (float)CullMode.Off);
            // 같은 반투명 큐의 폭발 범위보다 나중에 그려 범위 색에 묻히지 않게 합니다.
            m_runtimeLandingMarkerMaterial.renderQueue = (int)RenderQueue.Transparent + 1;
            m_landingMarkerRenderer.sharedMaterial = m_runtimeLandingMarkerMaterial;
        }
    }

    /// <summary>
    /// 폭발 반경 크기의 구체를 만듭니다. 구체는 셰이더를 실행할 화면 영역만 정하고,
    /// 실제로 칠할 픽셀은 셰이더가 장면 표면과 폭발 중심의 거리로 고릅니다.
    /// </summary>
    private void CreateExplosionRangePreview()
    {
        Material sourceMaterial = m_impactRangeMaterial != null
            ? m_impactRangeMaterial
            : Resources.Load<Material>(ExplosionRangeMaterialResourcePath);
        if (sourceMaterial == null)
        {
            Debug.LogWarning($"[{name}] 폭발 범위 머티리얼을 찾지 못해 범위 표시를 생략합니다. Resources/{ExplosionRangeMaterialResourcePath}를 확인하세요.", this);
            return;
        }

        GameObject sphereObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphereObject.name = "ExplosionRangeSphere";
        // 미리보기 구체가 궤적 SphereCast나 조준 레이캐스트에 걸리지 않도록 같은 프레임 안에 Collider를 없앱니다.
        DestroyImmediate(sphereObject.GetComponent<Collider>());
        sphereObject.transform.SetParent(transform, false);

        m_explosionRangeRenderer = sphereObject.GetComponent<MeshRenderer>();
        m_explosionRangeRenderer.shadowCastingMode = ShadowCastingMode.Off;
        m_explosionRangeRenderer.receiveShadows = false;
        m_explosionRangeRenderer.lightProbeUsage = LightProbeUsage.Off;
        m_explosionRangeRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        m_explosionRangeRenderer.enabled = false;

        m_runtimeExplosionRangeMaterial = new Material(sourceMaterial)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        m_explosionRangeRenderer.sharedMaterial = m_runtimeExplosionRangeMaterial;

        m_explosionRangeExclusionPass = new ExplosionRangeExclusionPass();
    }

    /// <summary>
    /// 착탄 지점에 붙일 고정 크기 원판을 만듭니다.
    /// </summary>
    private void CreateLandingMarker()
    {
        GameObject markerObject = new GameObject("LandingMarker");
        markerObject.transform.SetParent(transform, false);

        MeshFilter markerFilter = markerObject.AddComponent<MeshFilter>();
        m_landingMarkerRenderer = markerObject.AddComponent<MeshRenderer>();
        m_landingMarkerRenderer.shadowCastingMode = ShadowCastingMode.Off;
        m_landingMarkerRenderer.receiveShadows = false;
        m_landingMarkerRenderer.enabled = false;

        m_landingMarkerMesh = CreateLandingMarkerMesh();
        markerFilter.sharedMesh = m_landingMarkerMesh;
    }

    /// <summary>반지름 1인 수평 원판 메시를 만듭니다. 실제 크기는 Transform 스케일로 맞춥니다.</summary>
    private static Mesh CreateLandingMarkerMesh()
    {
        Vector3[] vertices = new Vector3[LandingMarkerSegmentCount + 1];
        Vector3[] normals = new Vector3[vertices.Length];
        int[] triangles = new int[LandingMarkerSegmentCount * 3];

        vertices[0] = Vector3.zero;
        normals[0] = Vector3.up;

        for (int i = 0; i < LandingMarkerSegmentCount; i++)
        {
            float angle = 2.0f * Mathf.PI * i / LandingMarkerSegmentCount;
            vertices[i + 1] = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
            normals[i + 1] = Vector3.up;

            int triangleIndex = i * 3;
            triangles[triangleIndex] = 0;
            triangles[triangleIndex + 1] = ((i + 1) % LandingMarkerSegmentCount) + 1;
            triangles[triangleIndex + 2] = i + 1;
        }

        Mesh mesh = new Mesh
        {
            name = "Landing Marker Mesh",
            hideFlags = HideFlags.HideAndDontSave,
            vertices = vertices,
            normals = normals,
            triangles = triangles,
        };
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>URP Unlit 머티리얼을 깊이를 쓰지 않는 알파 블렌딩 반투명으로 설정합니다.</summary>
    private static void ConfigureTransparentPreviewMaterial(Material material)
    {
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetFloat("_Surface", 1.0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0.0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    /// <summary>미리보기 선의 두께·색과 그림자 설정을 맞추고, 처음에는 꺼 둡니다.</summary>
    private void ConfigurePreviewLine(LineRenderer lineRenderer, bool loop)
    {
        lineRenderer.useWorldSpace = true;
        lineRenderer.loop = loop;
        lineRenderer.widthMultiplier = m_trajectoryWidth;
        lineRenderer.startColor = m_trajectoryColor;
        lineRenderer.endColor = m_trajectoryColor;
        lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.enabled = false;
    }

    /// <summary>
    /// 폭발 미리보기 위치가 정해졌으면 착탄 마커를 그리고, 선택한 투척물의 폭발 반경만큼 범위 구체를 띄웁니다.
    /// 위치가 없으면(미리보기 거리 안에서 착탄하지 않음) 둘 다 숨깁니다.
    /// </summary>
    private void DrawExplosionPreview()
    {
        ProjectileBase selectedProjectile = GetSelectedProjectile();
        if (!m_hasExplosionPreview || selectedProjectile == null)
        {
            HideExplosionPreview();
            return;
        }

        DrawLandingMarker();

        float radius = Mathf.Max(0.0f, selectedProjectile.ImpactPreviewRadius);
        if (radius <= 0.0f || m_explosionRangeRenderer == null || m_runtimeExplosionRangeMaterial == null)
        {
            SetExplosionRangeVisible(false);
            return;
        }

        // 폭발 중심은 충돌 순간의 투척물 중심입니다. 표면 위치는 셰이더가 깊이로 복원하므로 높이 보정이 필요 없습니다.
        Vector3 center = m_explosionPreviewCenter;
        ApplyImpactPreviewAppearance();
        m_runtimeExplosionRangeMaterial.SetVector(
            "_SphereCenterRadius",
            new Vector4(center.x, center.y, center.z, radius));

        Transform sphereTransform = m_explosionRangeRenderer.transform;
        sphereTransform.SetPositionAndRotation(center, Quaternion.identity);
        // 기본 Sphere 메시는 지름이 1입니다.
        sphereTransform.localScale = Vector3.one * (radius * 2.0f * ExplosionRangeSphereScalePadding);
        SetExplosionRangeVisible(true);
    }

    /// <summary>
    /// 착탄 지점에 고정 크기 원판을 표시합니다. 맞은 표면 방향과 관계없이 항상 수평으로 둡니다.
    /// </summary>
    private void DrawLandingMarker()
    {
        if (m_landingMarkerRenderer == null || !m_showLandingMarker || m_landingMarkerColor.a <= 0.0f)
        {
            if (m_landingMarkerRenderer != null)
            {
                m_landingMarkerRenderer.enabled = false;
            }

            return;
        }

        Transform markerTransform = m_landingMarkerRenderer.transform;
        markerTransform.SetPositionAndRotation(
            m_explosionPreviewCenter + Vector3.up * LandingMarkerSurfaceOffset,
            Quaternion.identity);
        markerTransform.localScale = new Vector3(m_landingMarkerRadius, 1.0f, m_landingMarkerRadius);

        if (m_runtimeLandingMarkerMaterial != null)
        {
            m_runtimeLandingMarkerMaterial.SetColor("_BaseColor", m_landingMarkerColor);
        }

        m_landingMarkerRenderer.enabled = true;
    }

    /// <summary>인스펙터의 채움 색·투명도와 테두리 색·두께를 폭발 범위 머티리얼에 넣습니다. 플레이 중 값을 바꿔도 바로 반영됩니다.</summary>
    private void ApplyImpactPreviewAppearance()
    {
        if (m_runtimeExplosionRangeMaterial == null)
        {
            return;
        }

        Color fillColor = m_impactPreviewColor;
        fillColor.a = Mathf.Clamp01(m_impactPreviewFillAlpha);
        m_runtimeExplosionRangeMaterial.SetColor("_BaseColor", fillColor);
        m_runtimeExplosionRangeMaterial.SetColor("_RimColor", m_impactPreviewRimColor);
        m_runtimeExplosionRangeMaterial.SetFloat("_RimWidth", m_impactPreviewRimWidth);
    }

    /// <summary>폭발 범위 구체를 켜거나 끕니다.</summary>
    private void SetExplosionRangeVisible(bool visible)
    {
        if (m_explosionRangeRenderer != null)
        {
            m_explosionRangeRenderer.enabled = visible;
        }
    }

    /// <summary>폭발 범위 구체와 착탄 마커를 숨깁니다.</summary>
    private void HideExplosionPreview()
    {
        SetExplosionRangeVisible(false);

        if (m_landingMarkerRenderer != null)
        {
            m_landingMarkerRenderer.enabled = false;
        }
    }

    /// <summary>
    /// 폭발 범위가 보이고 제외 Layer가 지정돼 있으면, 이번 카메라 렌더에 제외 영역 스텐실 패스를 끼워 넣습니다.
    /// </summary>
    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera renderingCamera)
    {
        if (m_explosionRangeExclusionPass == null
            || m_impactPreviewExcludeLayers.value == 0
            || m_explosionRangeRenderer == null
            || !m_explosionRangeRenderer.enabled
            || (renderingCamera.cameraType != CameraType.Game && renderingCamera.cameraType != CameraType.SceneView))
        {
            return;
        }

        // Scene 뷰 카메라에는 이 컴포넌트가 없을 수 있어 확장 메서드로 가져옵니다(없으면 붙입니다).
        UniversalAdditionalCameraData cameraData = renderingCamera.GetUniversalAdditionalCameraData();
        if (cameraData == null || cameraData.scriptableRenderer == null)
        {
            return;
        }

        m_explosionRangeExclusionPass.Setup(
            m_runtimeExplosionRangeMaterial,
            m_runtimeExplosionRangeMaterial.FindPass(ExplosionRangeExclusionPassName),
            m_impactPreviewExcludeLayers);
        cameraData.scriptableRenderer.EnqueuePass(m_explosionRangeExclusionPass);
    }

    /// <summary>궤적 선과 폭발 미리보기를 모두 숨깁니다.</summary>
    private void HideTrajectory()
    {
        if (m_trajectoryLine != null)
        {
            m_trajectoryLine.enabled = false;
            m_trajectoryLine.positionCount = 0;
        }

        HideExplosionPreview();
    }
}
