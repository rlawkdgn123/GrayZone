using UnityEngine;
using UnityEngine.Serialization;
using VInspector;

/// <summary>
/// 함정이 피해를 주는 방식입니다.
/// </summary>
public enum TrapDamageMode
{
    /// <summary>범위에 들어온 순간 한 번만 피해를 줍니다. 계속 머물러도 추가 피해는 없습니다.</summary>
    Once,

    /// <summary>들어온 순간 한 번 주고, 머무는 동안 틱 간격마다 반복해서 더 줍니다.</summary>
    Tick,
}

/// <summary>
/// 다 쓴 함정을 언제 다시 설치할 수 있는지에 대한 규칙입니다.
/// </summary>
/// <remarks>
/// "다 썼다"는 것은 함정마다 다릅니다. 지뢰는 터진 순간이고, 철조망은 내구도가 다 닳은 순간입니다.
/// 어느 쪽이든 그 뒤 언제 다시 청사진으로 돌아오는지를 이 값이 정합니다.
///
/// 순서를 바꾸지 마세요. 이 필드가 생기기 전에 저장된 프리팹·씬에는 값이 적혀 있지 않아, 불러올 때
/// 첫 번째 값(0)으로 들어옵니다. C# 필드 초기값은 그 경우를 덮지 못합니다. 그래서 첫 번째 값이
/// 곧 "적어 두지 않았을 때의 값"이고, <c>m_rebuildPolicy</c>의 초기값도 여기에 맞춰 두었습니다.
/// </remarks>
public enum TrapRebuildPolicy
{
    /// <summary>전투 도중에도 곧바로 다시 설치할 수 있습니다. 자원만 있으면 웨이브 중에 복구됩니다.</summary>
    /// <remarks>
    /// 열거형은 순서(정수)로 직렬화되므로 이름만 바꾸면 이미 저장된 값은 그대로 이 항목을 가리킵니다.
    /// 순서를 바꾸거나 사이에 새 항목을 끼우면 저장된 값의 의미가 달라집니다.
    /// </remarks>
    Always,

    /// <summary>웨이브 사이 휴식 구간(방어전 시작 전 포함)에만 설치할 수 있습니다. 전투 중에는 복구할 수 없습니다.</summary>
    OnRest,

    // 예전의 NextDefense(이번 방어전에는 다시 설치 불가)는 없앴습니다(2026-10-01). 같은 동작은
    // OnRest + 방어전당 설치 횟수 1로 표현합니다(Trap.m_buildChargesPerDefense). 정수 2를 쓰는 에셋이 없음을 확인했습니다.
}

/// <summary>
/// 설치형 함정의 공통 기반입니다.
/// </summary>
/// <remarks>
/// 모든 함정이 공통으로 갖는 것만 둡니다. 내구도와 피해량, 피해를 주는 방식, 그리고 다 쓴 뒤
/// 언제 다시 설치할 수 있는지입니다. 무엇을 어떻게 감지하고 피해 외에 어떤 효과를 주는지는
/// 파생 함정이 정합니다.
///
/// 내구도가 0이 되면 <see cref="Deplete"/>가 함정을 청사진 상태로 되돌립니다. 언제 다시 설치할 수
/// 있는지는 <see cref="TrapRebuildPolicy"/>가 정합니다. 무엇이 내구도를 깎는지는 파생이 정합니다.
/// 지뢰는 터질 때 한 번에 다 쓰고, 철조망은 적이 들어올 때마다 조금씩 깎입니다.
///
/// 아직 <c>IDamageable</c>은 구현하지 않았습니다. 총에 맞아 부서지는 규칙이 정해지면 이 클래스에
/// 구현해 파생 전체가 한 번에 따라오게 하는 것이 자연스럽습니다.
/// </remarks>
public abstract class Trap : MonoBehaviour, IInteractable, IInteractionRequirement, IInteractionHudInfo
{
    [Header("Build")]
    [Tooltip("켜면 시작할 때 이미 설치된 상태입니다. 끄면 청사진 상태로 시작하며, 자원을 내고 설치하기 전까지 작동하지 않습니다.")]
    [FormerlySerializedAs("m_startAsBlueprint")]
    [SerializeField] private bool m_startPlaced = false;

    [Tooltip("설치할 때 소모할 자원입니다. 셸터에 이미 정의된 자원 중에서 고릅니다.")]
    [Variants(
        ResourceIds.UpgradeMaterial,
        ResourceIds.CraftingMaterial,
        ResourceIds.TrapMaterial)]
    [SerializeField] private string m_buildCostResourceId = ResourceIds.TrapMaterial;

    [Tooltip("설치에 소모할 자원의 수량입니다. 0이면 자원 없이 설치됩니다. 함정 가격 표(Resources/Trap/TrapBuildCostTable)에 이 함정 종류가 있으면 표 값이 우선합니다.")]
    [Min(0)]
    [SerializeField] private int m_buildCostAmount = 1;

    [Tooltip("청사진 상태에서 UI에 표시할 문구입니다. 비용은 뒤에 자동으로 붙습니다.")]
    [SerializeField] private string m_buildPrompt = "설치";

    [Tooltip("이미 설치된 함정을 수리할 때 소모할 자원 수량입니다. 자원 종류는 설치와 같습니다. 0이면 자원 없이 수리됩니다. " +
             "함정 가격 표(Resources/Trap/TrapBuildCostTable)에 이 함정 종류가 있으면 표 값이 우선합니다. 수리할 수 있는 함정(철조망·스파이크)만 씁니다.")]
    [Min(0)]
    [SerializeField] private int m_repairCostAmount = 1;

    [Tooltip("설치된 함정에서 UI에 표시할 수리 문구입니다.")]
    [SerializeField] private string m_repairPrompt = "수리";

    [Tooltip("내구도가 가득 차 수리할 수 없을 때 비용 옆에 붙일 문구입니다.")]
    [SerializeField] private string m_fullDurabilityMessage = "내구도가 가득 찼습니다";

    [Tooltip("설치에 필요한 홀드 시간(초)입니다. 0이면 누르는 즉시 설치됩니다. " +
             "기본 1초인 이유: 튜토리얼 넘김이 상호작용 단일 탭이라, 설치를 홀드로 두어 두 입력을 구분합니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_buildHoldDuration = 0.3f;

    [Header("Display")]
    [Tooltip("함정 안내 HUD에 표시할 이름입니다. 비워 두면 함정 종류의 기본 이름을 씁니다.")]
    [SerializeField] private string m_displayName;

    [Tooltip("함정 안내 HUD에 표시할 한 줄 설명입니다. 비워 두면 함정 종류의 기본 설명을 씁니다.")]
    [SerializeField] private string m_description;

    [Foldout("Build Visual")]
    [Tooltip("청사진 상태 동안 이 함정의 모든 렌더러에 입힐 머테리얼입니다. 비워 두면 외형이 바뀌지 않습니다.")]
    [SerializeField] private Material m_blueprintMaterial;

    [Tooltip("청사진 상태에서 그림자를 끌지 여부입니다. 반투명한 설계도가 진한 그림자를 드리우면 어색합니다.")]
    [SerializeField] private bool m_disableShadowWhileBlueprint = true;

    [Tooltip("청사진 상태에서만 켜 둘 오브젝트입니다. 머테리얼 교체로 부족할 때만 씁니다.")]
    [SerializeField] private GameObject m_blueprintVisualRoot;

    [Tooltip("설치된 뒤에만 켜 둘 오브젝트입니다. 머테리얼 교체로 부족할 때만 씁니다.")]
    [SerializeField] private GameObject m_builtVisualRoot;
    [EndFoldout]

    /// <summary>머테리얼을 되돌리기 위해 기억해 두는 원래 구성입니다.</summary>
    /// <remarks>
    /// 렌더러마다 슬롯 수가 다를 수 있어 배열째 보관합니다. 청사진 머테리얼을 덮어쓰기 전에 한 번만 잡습니다.
    /// 이미 덮어쓴 뒤에 다시 잡으면 청사진 머테리얼이 원본으로 기억되어 설치해도 파랗게 남습니다.
    ///
    /// <c>Material[][]</c> 대신 <see cref="MaterialSlots"/> 배열로 둡니다. 배열의 배열은 Unity가 직렬화하지 못해,
    /// Play 중 스크립트가 다시 컴파일되면 이 값만 null로 사라지고 설치가 NullReferenceException으로 끊깁니다.
    /// </remarks>
    private Renderer[] m_visualRenderers;
    private MaterialSlots[] m_originalMaterials;
    private UnityEngine.Rendering.ShadowCastingMode[] m_originalShadowModes;

    /// <summary>렌더러의 원래 켜짐 여부입니다. 청사진을 숨겼다가 다시 보일 때 원래부터 꺼져 있던 렌더러를 켜지 않기 위해서입니다.</summary>
    private bool[] m_originalRendererEnabled;

    /// <summary>
    /// 지금 함정을 설치할 수 있는 구간인지 여부입니다. 방어전 매니저가 알려 줍니다.
    /// </summary>
    /// <remarks>
    /// 방어전 매니저가 없는 씬(셸터 등)에서는 바뀌지 않으므로 기본값을 열림으로 둡니다. 그래야 예전처럼 언제든 설치됩니다.
    /// </remarks>
    private bool m_buildWindowOpen = true;

    [Header("Trap")]
    [Tooltip("이 함정의 최대 내구도입니다. 0 이하로 닳으면 다 쓴 것으로 보고 청사진 상태로 돌아갑니다. 다시 설치하면 이 값으로 회복됩니다.")]
    [Min(0)]
    [FormerlySerializedAs("m_health")]
    [SerializeField] protected int m_maxHealth = 100;

    [Tooltip("지금 남은 내구도입니다. 확인용이라 여기서 고쳐도 설치·피해 경로를 거치지 않아 상태가 어긋납니다.")]
    [ReadOnly]
    [SerializeField] private int m_currentHealth;

    [Tooltip("언제 설치할 수 있는지입니다. Always는 전투 중에도 언제든, OnRest는 휴식 구간(방어전 시작 전 포함)에만 설치할 수 있습니다.")]
    [SerializeField] protected TrapRebuildPolicy m_rebuildPolicy = TrapRebuildPolicy.Always;

    [Tooltip("방어전 한 판 동안 이 자리에 설치할 수 있는 횟수입니다. 다 쓰면 그 방어전에서는 다시 설치할 수 없고, 다음 방어전에서 다시 채워집니다. " +
             "0이면 제한이 없습니다. 처음부터 설치된 상태(Start Placed)는 횟수를 쓰지 않습니다.")]
    [Min(0)]
    [SerializeField] private int m_buildChargesPerDefense = 0;

    [Tooltip("이번 방어전에서 남은 설치 횟수입니다. 제한이 없으면 -1로 표시합니다. 확인용입니다.")]
    [ReadOnly]
    [SerializeField] private int m_remainingBuildCharges = -1;

    [Tooltip("한 번에 주는 피해량입니다. Tick 방식이면 1틱당 피해량입니다.")]
    [Min(0)]
    [SerializeField] protected int m_damage = 1;

    [Tooltip("피해를 주는 방식입니다. 어느 쪽이든 들어온 순간 한 번은 줍니다. Tick은 머무는 동안 추가로 반복합니다.")]
    [SerializeField] protected TrapDamageMode m_damageMode = TrapDamageMode.Tick;

    [Tooltip("Tick 방식에서 피해를 주는 간격(초)입니다. 0.5면 0.5초마다 한 번씩 m_damage만큼 들어갑니다. 진입 시 1회는 이 값과 무관하게 항상 들어갑니다.")]
    [ShowIf(nameof(m_damageMode), TrapDamageMode.Tick)]
    [Min(0.01f)]
    [FormerlySerializedAs("m_damageTickRate")]
    [SerializeField] protected float m_damageTickInterval = 1.0f;
    [EndIf]

    /// <summary>지금 남은 내구도입니다. 다시 설치하면 <see cref="MaxHealth"/>로 회복됩니다.</summary>
    public int Health => m_currentHealth;

    /// <summary>업그레이드로 최대 내구도에 더한 값입니다. <see cref="ApplyUpgrade"/>가 정합니다.</summary>
    private int m_upgradeBonusHealth;

    /// <summary>업그레이드로 피해량에 곱하는 배율입니다. <see cref="ApplyUpgrade"/>가 정합니다.</summary>
    private float m_upgradeDamageMultiplier = 1.0f;

    /// <summary>다시 설치했을 때 회복되는 최대 내구도입니다. 업그레이드로 더한 값이 들어 있습니다.</summary>
    public int MaxHealth => m_maxHealth + m_upgradeBonusHealth;

    /// <summary>한 번에 주는 피해량입니다. 업그레이드 배율을 곱하고 올림한 값입니다.</summary>
    public int Damage => Mathf.CeilToInt(m_damage * m_upgradeDamageMultiplier);

    /// <summary>이 함정이 피해를 주는 방식입니다.</summary>
    public TrapDamageMode DamageMode => m_damageMode;

    /// <summary>Tick 방식에서 피해 사이의 간격(초)입니다.</summary>
    public float DamageInterval => Mathf.Max(0.01f, m_damageTickInterval);

    /// <summary>Tick 방식에서 1초당 피해 횟수입니다. 이전 API 호환용으로 유지합니다.</summary>
    public float DamageTickRate => 1.0f / DamageInterval;

    /// <summary>이 함정이 아직 멀쩡한지 여부입니다.</summary>
    public bool IsAlive => m_currentHealth > 0;

    /// <summary>설치가 끝나 실제로 작동하는 상태인지 여부입니다.</summary>
    public bool IsBuilt { get; private set; }

    /// <summary>아직 자원을 내지 않은 청사진 상태인지 여부입니다.</summary>
    public bool IsBlueprint => !IsBuilt;

    /// <summary>
    /// 다 써서 아직 다시 설치할 수 없는 상태인지 여부입니다.
    /// </summary>
    /// <remarks>
    /// 청사진과 다릅니다. 청사진은 자원만 내면 지을 수 있지만, 이 상태는 <see cref="TrapRebuildPolicy"/>가
    /// 정한 시점이 올 때까지 자원이 있어도 지을 수 없습니다.
    /// </remarks>
    public bool IsDepleted { get; private set; }

    /// <summary>
    /// 지금 자원만 있으면 설치할 수 있는 청사진인지 여부입니다. 이 값이 거짓인 청사진은 보이지도 않습니다.
    /// </summary>
    /// <remarks>
    /// 재설치 정책이 <see cref="TrapRebuildPolicy.Always"/>면 구간과 관계없이 언제든 설치할 수 있습니다(상시 설치).
    /// 그 밖의 정책은 방어전 매니저가 연 설치 구간(방어전 시작 전, 휴식 구간)에만 설치할 수 있습니다.
    /// 다 써서 복구 시점을 기다리는 함정(<see cref="IsDepleted"/>)은 설치할 수 없습니다.
    /// </remarks>
    public bool IsBuildable => IsBlueprint && !IsDepleted && HasBuildCharge
        && (m_rebuildPolicy == TrapRebuildPolicy.Always || m_buildWindowOpen);

    /// <summary>방어전당 설치 횟수에 제한이 있는지 여부입니다. 0이면 제한이 없습니다.</summary>
    private bool HasBuildChargeLimit => m_buildChargesPerDefense > 0;

    /// <summary>이번 방어전에서 아직 설치할 수 있는 횟수가 남았는지 여부입니다. 제한이 없으면 항상 참입니다.</summary>
    public bool HasBuildCharge => !HasBuildChargeLimit || m_remainingBuildCharges > 0;

    /// <summary>이번 방어전에서 남은 설치 횟수입니다. 제한이 없으면 -1입니다.</summary>
    public int RemainingBuildCharges => HasBuildChargeLimit ? m_remainingBuildCharges : -1;

    /// <summary>설치 횟수를 방어전 한 판의 처음 값으로 채웁니다.</summary>
    private void ResetBuildCharges()
    {
        m_remainingBuildCharges = HasBuildChargeLimit ? m_buildChargesPerDefense : -1;
    }

    /// <summary>설치 횟수를 처음 값으로 다시 채우고 외형을 맞춥니다. 디버그 트레이너 전용입니다.</summary>
    public void DebugRefillBuildCharges()
    {
        ResetBuildCharges();
        ApplyBuildStateVisual();
    }

    /// <summary>
    /// 설치 가능 구간이 열리거나 닫혔음을 알립니다. 방어전 매니저가 구간이 바뀔 때마다 부릅니다.
    /// </summary>
    /// <param name="isOpen">지금 함정을 설치할 수 있는 구간이면 true입니다.</param>
    /// <remarks>
    /// 설치된 함정에는 영향이 없습니다. 청사진의 표시와 설치 가능 여부만 바뀝니다.
    /// </remarks>
    public void SetBuildWindowOpen(bool isOpen)
    {
        if (m_buildWindowOpen == isOpen)
        {
            return;
        }

        m_buildWindowOpen = isOpen;
        ApplyBuildStateVisual();
    }


    /// <summary>휴식/다음 방어전 알림을 받기 위해 구독해 둔 방어전 매니저입니다.</summary>
    private DefenseManager m_defenseManager;

    /// <summary>모든 함정이 함께 쓰는 방어전 매니저 참조입니다.</summary>
    /// <remarks>
    /// 한 씬에 함정이 수십 개 깔리는데 각자 <c>FindFirstObjectByType</c>을 부르면 같은 탐색을 그만큼
    /// 반복합니다. 씬이 바뀌어 매니저가 사라지면 Unity의 null 비교가 참이 되므로 다시 찾습니다.
    /// </remarks>
    private static DefenseManager s_sharedDefenseManager;

    /// <summary>F9 디버그의 "트랩 전체 무료" 토글 값입니다. 켜면 자원을 내지 않고 설치합니다.</summary>
    private static bool s_debugFreeBuild;

    /// <summary>
    /// 디버그로 모든 함정을 무료로 설치할지 여부입니다. 디버그 기능이 켜진 빌드에서만 효과가 있습니다.
    /// </summary>
    /// <remarks>함정 가격 값 자체는 바꾸지 않습니다. 끄면 원래 가격으로 바로 돌아옵니다.</remarks>
    public static bool DebugFreeBuild
    {
        get => s_debugFreeBuild;
        set => s_debugFreeBuild = value;
    }

    /// <summary>지금 디버그 무료 설치가 적용되는지 여부입니다.</summary>
    public static bool IsDebugFreeBuild => s_debugFreeBuild && GameDevMode.DebugFeaturesEnabled;

    /// <summary>Play를 다시 시작할 때 디버그 토글을 끕니다. 도메인 리로드를 끈 설정에서도 이전 값이 남지 않게 합니다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDebugFreeBuild()
    {
        s_debugFreeBuild = false;
        s_costTable = null;
        s_costTableLoaded = false;
    }

    /// <summary>함정 가격 표입니다. 처음 쓸 때 Resources에서 한 번 불러옵니다.</summary>
    private static TrapBuildCostTableSO s_costTable;

    /// <summary>가격 표를 불러오려 시도했는지 여부입니다. 표가 없어도 매번 다시 찾지 않게 합니다.</summary>
    private static bool s_costTableLoaded;

    /// <summary>가격 표에서 이 함정을 가리키는 종류입니다. 파생이 정하고, 기본은 표를 쓰지 않습니다.</summary>
    protected virtual TrapKind CostKind => TrapKind.None;

    private static TrapBuildCostTableSO CostTable
    {
        get
        {
            if (!s_costTableLoaded)
            {
                s_costTable = Resources.Load<TrapBuildCostTableSO>(TrapBuildCostTableSO.ResourcePath);
                s_costTableLoaded = true;
            }

            return s_costTable;
        }
    }

    /// <summary>설치 비용입니다. 수량이 0이면 비용이 없는 것으로 봅니다.</summary>
    /// <remarks>
    /// 가격 표(<see cref="TrapBuildCostTableSO"/>)에 이 함정 종류가 있으면 표 값을, 없으면 프리팹에 적힌 값을 씁니다.
    /// 기획자는 표에서 종류별로 한 번에 조절합니다.
    /// </remarks>
    public ResourceCost BuildCost
    {
        get
        {
            TrapBuildCostTableSO table = CostTable;
            return table != null && table.TryGetCost(CostKind, out ResourceCost tableCost)
                ? tableCost
                : new ResourceCost(m_buildCostResourceId, m_buildCostAmount);
        }
    }

    /// <summary>수리 비용입니다. 자원 종류는 설치와 같고, 수량은 가격 표의 수리 수량이 있으면 그 값을, 없으면 프리팹 값을 씁니다.</summary>
    public ResourceCost RepairCost
    {
        get
        {
            TrapBuildCostTableSO table = CostTable;
            return table != null && table.TryGetRepairCost(CostKind, out ResourceCost tableCost)
                ? tableCost
                : new ResourceCost(m_buildCostResourceId, m_repairCostAmount);
        }
    }

    /// <summary>함정 안내 HUD에 표시할 이름입니다. 인스펙터 값이 비어 있으면 <see cref="DefaultDisplayName"/>입니다.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(m_displayName) ? DefaultDisplayName : m_displayName;

    /// <summary>함정 안내 HUD에 표시할 설명입니다. 인스펙터 값이 비어 있으면 <see cref="DefaultDescription"/>입니다.</summary>
    public string Description => string.IsNullOrWhiteSpace(m_description) ? DefaultDescription : m_description;

    /// <inheritdoc />
    string IInteractionHudInfo.HudDisplayName
    {
        get
        {
            string suffix = HudNameSuffix;
            return string.IsNullOrEmpty(suffix) ? DisplayName : $"{DisplayName} {suffix}";
        }
    }

    /// <summary>안내 HUD에서 이름 뒤에 붙일 문구입니다. 비어 있으면 이름만 표시합니다.</summary>
    /// <remarks>기본은 설치된 함정에만 남은 내구도를 붙입니다(예: "윤형 철조망 (내구도 : 80/100)").</remarks>
    protected virtual string HudNameSuffix => IsBuilt ? DurabilityLabel(m_currentHealth) : null;

    /// <summary>이름 뒤에 붙이는 내구도 문구를 "현재/최대" 형태로 만듭니다.</summary>
    protected string DurabilityLabel(int durability) => $"(내구도 : {durability}/{MaxHealth})";

    /// <inheritdoc />
    string IInteractionHudInfo.HudDescription => Description;

    /// <inheritdoc />
    string IInteractionHudInfo.HudActionLabel => IsRepairTarget ? m_repairPrompt : m_buildPrompt;

    /// <inheritdoc />
    ResourceCost IInteractionHudInfo.HudCost => IsRepairTarget ? RepairCost : BuildCost;

    /// <inheritdoc />
    bool IInteractionHudInfo.CanAffordHudCost => IsRepairTarget ? CanPayCost(RepairCost) : CanAffordBuild;

    /// <inheritdoc />
    string IInteractionHudInfo.HudBlockedMessage => IsRepairTarget && IsFullDurability ? m_fullDurabilityMessage : null;

    /// <inheritdoc />
    bool IInteractionHudInfo.IsHudCostWaived => IsDebugFreeBuild;

    /// <inheritdoc />
    Sprite IInteractionHudInfo.HudCostIcon => null;

    /// <inheritdoc />
    GameObject IInteractionHudInfo.HudOutlineTarget => gameObject;

    /// <summary>함정 종류의 기본 이름입니다. 파생이 덮어쓰지 않으면 오브젝트 이름을 씁니다.</summary>
    /// <remarks>프리팹마다 문구를 넣지 않아도 HUD가 비지 않게 하려고 종류별 기본값을 둡니다.</remarks>
    protected virtual string DefaultDisplayName => gameObject.name;

    /// <summary><see cref="SetDamageSource"/>로 바꾼 피해 정보 틀입니다. 바꾸지 않았으면 기본값입니다.</summary>
    private DamageInfo m_damageSourceOverride;

    /// <summary>
    /// 이 함정이 주는 피해의 공격 방식과 공격자를 바꿉니다. 화염병처럼 투척물이 만든 함정이 자기 투척물 정보를 남길 때 씁니다.
    /// </summary>
    public void SetDamageSource(DamageInfo source)
    {
        m_damageSourceOverride = source;
    }

    /// <summary>
    /// 이 함정이 주는 피해 정보입니다. 기본은 공격 방식 함정, 세부 종류는 함정 타입 이름(MineTrap 등), 공격자는 이 함정입니다.
    /// </summary>
    /// <param name="amount">피해량입니다. 폭발처럼 대상마다 피해량을 채우는 경로에는 0을 넘깁니다.</param>
    protected DamageInfo CreateDamageInfo(float amount = 0.0f)
    {
        return m_damageSourceOverride.IsValid
            ? m_damageSourceOverride.WithAmount(amount)
            : new DamageInfo(amount, DamageCategory.Trap, GetType().Name, gameObject, DisplayName);
    }

    /// <summary>함정 종류의 기본 설명입니다. 파생이 덮어쓰지 않으면 빈 문자열입니다.</summary>
    protected virtual string DefaultDescription => string.Empty;

    /// <summary>설치된 뒤 플레이어가 수리할 수 있는 함정인지 여부입니다. 기본은 수리할 수 없습니다.</summary>
    /// <remarks>
    /// 수리는 내구도를 최대치로 채우는 것으로, 새것으로 바꿔 설치하는 것과 같습니다. 내구도가 조금씩 닳는 함정(철조망·스파이크)만 켭니다.
    /// 한 번 터지면 끝나는 함정(지뢰, 화염 드럼통 등)은 수리할 것이 없습니다.
    /// </remarks>
    protected virtual bool IsRepairable => false;

    /// <summary>지금 이 함정을 바라보면 수리 안내가 떠야 하는지 여부입니다. 설치된 수리 가능 함정입니다.</summary>
    private bool IsRepairTarget => IsBuilt && IsRepairable;

    /// <summary>
    /// 지금 수리할 수 있는 구간인지 여부입니다. 수리는 새로 설치하는 것과 같으므로 설치와 같은 구간 규칙을 따릅니다.
    /// </summary>
    private bool IsRepairWindowOpen => m_rebuildPolicy == TrapRebuildPolicy.Always || m_buildWindowOpen;

    /// <summary>내구도가 가득 차 수리할 것이 없는지 여부입니다.</summary>
    private bool IsFullDurability => m_currentHealth >= MaxHealth;

    protected virtual void Awake()
    {
        CacheVisuals();
        m_currentHealth = MaxHealth;
        ResetBuildCharges();
        IsBuilt = m_startPlaced;
        ApplyBuildStateVisual();
    }

    /// <summary>방어전 업그레이드 레벨을 적용합니다.</summary>
    /// <remarks>
    /// Awake가 아니라 Start에서 하는 이유는, 방어전 데이터 매니저가 입장 데이터(업그레이드 레벨)를 자기 Start에서 만들기 때문입니다.
    /// 데이터 매니저는 실행 순서가 -100이라 함정의 Start보다 먼저 돕니다.
    /// </remarks>
    protected virtual void Start()
    {
        ApplyUpgrade();
    }

    /// <summary>
    /// 이번 방어전의 업그레이드 레벨을 다시 읽어 강화값을 적용합니다.
    /// </summary>
    /// <remarks>
    /// 방어전 데이터 매니저나 업그레이드 표가 없는 씬에서는 기본값으로 되돌립니다. 여러 번 불러도 결과가 같습니다.
    /// 최대 내구도가 바뀌면 남은 내구도도 같은 양만큼 옮깁니다. 이미 깎인 양은 그대로 둡니다.
    /// </remarks>
    public void ApplyUpgrade()
    {
        int previousMaxHealth = MaxHealth;

        m_upgradeBonusHealth = 0;
        m_upgradeDamageMultiplier = 1.0f;

        DefenseSceneDataManager data = DefenseSceneDataManager.Instance;
        TrapUpgradeTableSO table = data != null ? data.TrapUpgradeTable : null;
        OnApplyUpgrade(table, data);

        int delta = MaxHealth - previousMaxHealth;
        if (delta != 0 && !IsDepleted)
        {
            m_currentHealth = Mathf.Max(1, m_currentHealth + delta);
        }
    }

    /// <summary>
    /// 파생 함정이 자기 업그레이드 강화값을 적용할 자리입니다. 기본은 아무것도 하지 않습니다.
    /// </summary>
    /// <param name="table">업그레이드 표입니다. 없으면 null입니다.</param>
    /// <param name="data">방어전 데이터 매니저입니다. 방어전 씬이 아니면 null입니다.</param>
    /// <remarks>
    /// 이 함수가 불리기 전에 내구도 보너스와 피해 배율은 기본값으로 되돌려져 있습니다.
    /// 파생은 <see cref="SetUpgradeBonus"/>로 값을 넣고, 자기만의 강화값(범위, 감속 등)도 여기서 다시 계산합니다.
    /// </remarks>
    protected virtual void OnApplyUpgrade(TrapUpgradeTableSO table, DefenseSceneDataManager data)
    {
    }

    /// <summary>업그레이드로 늘어난 최대 내구도와 피해 배율을 정합니다.</summary>
    /// <param name="bonusHealth">최대 내구도에 더할 값입니다.</param>
    /// <param name="damageMultiplier">피해량에 곱할 배율입니다.</param>
    protected void SetUpgradeBonus(int bonusHealth, float damageMultiplier)
    {
        m_upgradeBonusHealth = Mathf.Max(0, bonusHealth);
        m_upgradeDamageMultiplier = Mathf.Max(0.0f, damageMultiplier);
    }

    /// <summary>
    /// 방어전 매니저의 휴식/시작 알림을 구독합니다.
    /// </summary>
    /// <remarks>
    /// 매니저가 없는 씬(셸터 등)에서도 함정은 동작해야 하므로 없으면 조용히 넘어갑니다. 대신 그런
    /// 씬에서는 <see cref="TrapRebuildPolicy.OnRest"/> 함정이 다 쓴 뒤 영영 복구되지 않고, 설치 횟수도 다시 채워지지 않습니다.
    /// 그 씬에서는 <see cref="TrapRebuildPolicy.Always"/>와 횟수 제한 없음(0)을 쓰세요.
    /// </remarks>
    protected virtual void OnEnable()
    {
        if (s_sharedDefenseManager == null)
        {
            s_sharedDefenseManager = FindFirstObjectByType<DefenseManager>(FindObjectsInactive.Include);
        }

        m_defenseManager = s_sharedDefenseManager;
        if (m_defenseManager == null)
        {
            return;
        }

        m_defenseManager.OnRestStarted += HandleRestStarted;
        m_defenseManager.OnDefenseStarted += HandleDefenseStarted;

        // 방어전 도중에 켜진 함정도 지금 구간에 맞는 상태로 시작하도록 한 번 맞춥니다.
        SetBuildWindowOpen(m_defenseManager.IsTrapBuildWindowOpen);
    }

    protected virtual void OnDisable()
    {
        if (m_defenseManager == null)
        {
            return;
        }

        m_defenseManager.OnRestStarted -= HandleRestStarted;
        m_defenseManager.OnDefenseStarted -= HandleDefenseStarted;
    }

    /// <summary>휴식이 시작되면 그때 복구하기로 한 함정만 되살립니다.</summary>
    private void HandleRestStarted()
    {
        if (m_rebuildPolicy == TrapRebuildPolicy.OnRest)
        {
            Rearm();
        }
    }

    /// <summary>새 방어전이 시작되면 설치 횟수를 다시 채우고, 다 쓴 함정을 모두 되살립니다.</summary>
    /// <remarks>
    /// 방어전이 새로 시작되면 어떤 정책이든 이전 전투에서 쓴 결과를 끌고 갈 이유가 없습니다.
    /// 횟수를 다 써서 숨겨져 있던 청사진도 여기서 다시 보일 수 있게 됩니다.
    /// </remarks>
    private void HandleDefenseStarted()
    {
        ResetBuildCharges();
        Rearm();
        ApplyBuildStateVisual();
    }

    /// <summary>렌더러 하나의 머테리얼 슬롯 묶음입니다. 직렬화 가능한 형태로 감싸 재컴파일 뒤에도 남게 합니다.</summary>
    [System.Serializable]
    private struct MaterialSlots
    {
        public Material[] Materials;
    }

    /// <summary>청사진 머테리얼로 덮기 전에 원래 머테리얼과 그림자 설정을 기억해 둡니다.</summary>
    private void CacheVisuals()
    {
        m_visualRenderers = GetComponentsInChildren<Renderer>(true);
        m_originalMaterials = new MaterialSlots[m_visualRenderers.Length];
        m_originalShadowModes = new UnityEngine.Rendering.ShadowCastingMode[m_visualRenderers.Length];
        m_originalRendererEnabled = new bool[m_visualRenderers.Length];

        for (int i = 0; i < m_visualRenderers.Length; i++)
        {
            m_originalMaterials[i] = new MaterialSlots { Materials = m_visualRenderers[i].sharedMaterials };
            m_originalShadowModes[i] = m_visualRenderers[i].shadowCastingMode;
            m_originalRendererEnabled[i] = m_visualRenderers[i].enabled;
        }
    }

    // ===== IInteractable =====

    /// <inheritdoc />
    public float HoldDuration => m_buildHoldDuration;

    /// <inheritdoc />
    /// <remarks>
    /// 이미 설치됐거나, 다 써서 아직 복구 시점이 오지 않았거나, 설치 구간이 아니면(상시 설치가 아닐 때) 후보에서 빠집니다.
    /// 자원이 모자란 것은 후보에서 빼지 않습니다. 빼면 안내 HUD가 뜨지 않아 왜 설치가 안 되는지 알 수 없습니다.
    /// 자원은 <see cref="MeetsInteractionRequirement"/>가 따로 막습니다.
    ///
    /// 설치된 수리 가능 함정(철조망·스파이크)은 설치와 같은 구간 규칙 안에서 수리 대상이 됩니다. 내구도가 가득 차도
    /// 후보에서 빼지 않습니다. 안내가 떠야 남은 내구도를 볼 수 있고, 수리는 <see cref="MeetsInteractionRequirement"/>가 막습니다.
    /// </remarks>
    public bool CanInteract(GameObject interactor)
    {
        return IsBuildable || (IsRepairTarget && IsRepairWindowOpen);
    }

    /// <summary>설치 비용을 지금 낼 수 있는지 여부입니다. 안내 HUD가 "자원이 부족합니다"를 띄울 때 씁니다.</summary>
    public bool CanAffordBuild => CanPayCost(BuildCost);

    /// <inheritdoc />
    /// <remarks>자원이 모자라거나 수리할 내구도가 없으면 홀드 게이지를 채우지 않습니다.</remarks>
    public bool MeetsInteractionRequirement(GameObject interactor)
    {
        if (IsRepairTarget)
        {
            return !IsFullDurability && CanPayCost(RepairCost);
        }

        return CanPayCost(BuildCost);
    }

    /// <inheritdoc />
    public string GetPrompt()
    {
        bool repair = IsRepairTarget;
        string prompt = repair ? m_repairPrompt : m_buildPrompt;
        ResourceCost cost = repair ? RepairCost : BuildCost;
        return cost.IsValid
            ? $"{prompt} ({cost.ResourceId} {cost.Amount})"
            : prompt;
    }

    /// <inheritdoc />
    public void Interact(GameObject interactor)
    {
        if (IsRepairTarget)
        {
            TryRepair(interactor);
            return;
        }

        // 홀드 도중에 전투가 시작돼 설치 구간이 닫혔으면 자원을 내기 전에 멈춥니다.
        if (!IsBuildable || !TryPayCost(BuildCost))
        {
            return;
        }

        // 상호작용 판정과 같은 콜라이더 표면을 설치음 위치로 씁니다. 긴 함정의 루트 중앙에서
        // 재생하면 플레이어가 끝부분을 설치할 때 실제 거리보다 멀게 계산되어 소리가 작아집니다.
        Vector3 installSoundPosition = ResolveInstallSoundPosition(interactor);

        Build();
        PlayInstallSound(installSoundPosition);
    }

    /// <summary>수리 비용을 내고 내구도를 최대치로 채웁니다. 새것으로 바꿔 설치한 것과 같게 다룹니다.</summary>
    /// <remarks>
    /// 방어전당 설치 횟수는 쓰지 않습니다. 횟수는 청사진에서 새로 짓는 것만 셉니다.
    /// 홀드 도중에 구간이 닫히거나 내구도가 이미 가득 찼으면 자원을 내기 전에 멈춥니다.
    /// </remarks>
    private void TryRepair(GameObject interactor)
    {
        if (!IsRepairTarget || !IsRepairWindowOpen || IsFullDurability || !TryPayCost(RepairCost))
        {
            return;
        }

        Vector3 soundPosition = ResolveInstallSoundPosition(interactor);
        m_currentHealth = MaxHealth;
        OnRepaired();
        PlayInstallSound(soundPosition);
    }

    /// <summary>수리로 내구도가 다시 가득 찬 순간 파생 함정이 자기 상태를 맞추도록 부릅니다.</summary>
    protected virtual void OnRepaired()
    {
    }

    /// <summary>플레이어와 가장 가까운 설치 콜라이더 표면의 월드 위치를 구합니다.</summary>
    /// <remarks>
    /// <see cref="InteractionController"/>가 상호작용 거리를 재는 규칙과 같아야 화면에서 보이는 거리와
    /// 사운드 감쇠 거리가 어긋나지 않습니다. 콜라이더나 상호작용자가 없으면 함정 루트를 사용합니다.
    /// </remarks>
    private Vector3 ResolveInstallSoundPosition(GameObject interactor)
    {
        if (interactor == null)
        {
            return transform.position;
        }

        Collider interactionCollider = GetComponentInChildren<Collider>();
        return interactionCollider != null
            ? interactionCollider.ClosestPoint(interactor.transform.position)
            : transform.position;
    }

    /// <summary>플레이어가 청사진 설치를 완료했을 때 함정 종류에 맞는 3D 설치음을 재생합니다.</summary>
    /// <remarks>
    /// <see cref="Build"/> 안에서 재생하지 않는 이유는 투척형 임시 함정과 화염 지대도 같은 메서드를
    /// 사용하기 때문입니다. 상호작용으로 비용을 지불하고 설치한 경우에만 소리가 나야 합니다.
    /// 클레이모어는 전용 음원이 정해질 때까지 의도적으로 재생하지 않습니다.
    /// </remarks>
    private void PlayInstallSound(Vector3 position)
    {
        string eventPath;

        switch (CostKind)
        {
            case TrapKind.Wire:
                eventPath = "event:/World/Trap/Install/Wire";
                break;
            case TrapKind.Spike:
                eventPath = "event:/World/Trap/Install/Spike";
                break;
            case TrapKind.Mine:
                eventPath = "event:/World/Trap/Install/Mine";
                break;
            case TrapKind.FireBarrel:
                eventPath = "event:/World/Trap/Install/FireBarrel";
                break;
            default:
                return;
        }

        try
        {
            SoundManager.Instance?.PlayOneShot(eventPath, position);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning(
                $"[Trap] '{name}': 설치 사운드를 재생하지 못했습니다 ({eventPath}). {exception.Message}",
                this);
        }
    }

    /// <summary>
    /// 자원을 실제로 내지 않고 지금 낼 수 있는지만 확인합니다.
    /// </summary>
    /// <remarks>
    /// 확인과 차감을 나눈 이유는 UI가 버튼을 미리 비활성화할 수 있어야 하고, 실행 도중 모자라도
    /// 일부만 빠져나간 상태가 남지 않아야 하기 때문입니다. <c>IFacilityService</c>가 쓰는 방식과 같습니다.
    /// </remarks>
    private bool CanPayCost(ResourceCost cost)
    {
        if (!cost.IsValid || IsDebugFreeBuild || SquadInventoryManager.FreeUse)
        {
            return true;
        }

        StorageFacility storage = ShelterSceneDataManager.Instance?.Storage;
        return storage != null && storage.CanSpendResource(cost);
    }

    /// <summary>설치·수리 비용을 차감합니다. 모자라면 아무것도 차감하지 않고 <c>false</c>를 돌려줍니다.</summary>
    private bool TryPayCost(ResourceCost cost)
    {
        if (!cost.IsValid || IsDebugFreeBuild || SquadInventoryManager.FreeUse)
        {
            return true;
        }

        StorageFacility storage = ShelterSceneDataManager.Instance?.Storage;
        if (storage == null)
        {
            Debug.LogWarning(
                $"[Trap] '{name}': 자원 보관소를 찾지 못해 설치·수리할 수 없습니다. " +
                "이 씬에 ShelterSceneDataManager가 있는지 확인하세요.",
                this);
            return false;
        }

        return storage.TrySpendResource(cost);
    }

    /// <summary>청사진을 설치 완료 상태로 바꿉니다. 비용은 이미 치른 뒤에 부릅니다.</summary>
    public void Build()
    {
        if (IsBuilt)
        {
            return;
        }

        IsBuilt = true;

        // 설치할 때마다 이번 방어전의 남은 횟수를 하나 씁니다. 다 쓰면 파괴된 뒤 이 방어전에서는 다시 보이지 않습니다.
        if (HasBuildChargeLimit && m_remainingBuildCharges > 0)
        {
            m_remainingBuildCharges--;
        }

        ApplyBuildStateVisual();
        OnBuilt();
    }

    /// <summary>설치가 끝난 순간 파생 함정이 자기 효과를 켜도록 부릅니다.</summary>
    protected virtual void OnBuilt()
    {
    }

    /// <summary>
    /// 내구도를 깎습니다. 0 이하가 되면 <see cref="Deplete"/>로 이어집니다.
    /// </summary>
    /// <param name="amount">깎을 양입니다. 0 이하면 아무 일도 하지 않습니다.</param>
    /// <returns>이번 호출로 함정이 다 쓰였으면 <c>true</c>입니다.</returns>
    /// <remarks>
    /// 설치되지 않은 함정은 깎이지 않습니다. 청사진을 밟는다고 닳으면 안 됩니다.
    /// </remarks>
    public bool TakeDurabilityDamage(int amount)
    {
        if (amount <= 0 || !IsBuilt || IsDepleted)
        {
            return false;
        }

        DamageNumberDebug.Report(this, Mathf.Min(amount, m_currentHealth), DamageNumberDebug.TargetKind.Trap);
        m_currentHealth -= amount;
        if (m_currentHealth > 0)
        {
            return false;
        }

        m_currentHealth = 0;
        Deplete();
        return true;
    }

    /// <summary>
    /// 이 함정을 다 쓴 것으로 처리합니다. 청사진 상태로 돌아가고, 재설치 시점은 정책이 정합니다.
    /// </summary>
    /// <remarks>
    /// 내구도와 무관하게 한 번에 다 쓰는 함정(지뢰처럼 터지면 끝)도 이 메서드를 직접 부릅니다.
    /// <see cref="TrapRebuildPolicy.Always"/>면 이 자리에서 바로 복구되므로
    /// <see cref="IsDepleted"/>가 남지 않습니다.
    /// </remarks>
    public void Deplete()
    {
        if (IsDepleted)
        {
            return;
        }

        IsDepleted = true;
        m_currentHealth = 0;
        IsBuilt = false;
        ApplyBuildStateVisual();
        OnDepleted();

        // OnRest는 휴식이 "시작될 때" 복구됩니다. 이미 휴식(준비 시간) 중에 다 쓰면 그 알림이 다시 오지 않아
        // 이번 휴식 내내 청사진이 숨겨집니다. 설치 구간이 열려 있으면 그 자리에서 되돌립니다.
        if (m_rebuildPolicy == TrapRebuildPolicy.Always
            || (m_rebuildPolicy == TrapRebuildPolicy.OnRest && m_buildWindowOpen))
        {
            Rearm();
        }
    }

    /// <summary>다 쓴 함정을 다시 설치할 수 있는 상태로 되돌립니다. 내구도도 최대치로 회복됩니다.</summary>
    /// <remarks>설치까지 해 주지는 않습니다. 자원을 내고 짓는 것은 여전히 플레이어의 몫입니다.</remarks>
    public void Rearm()
    {
        if (!IsDepleted)
        {
            return;
        }

        IsDepleted = false;
        m_currentHealth = MaxHealth;
        ApplyBuildStateVisual();
        OnRearmed();
    }

    /// <summary>다 쓴 순간 파생 함정이 자기 효과를 끄도록 부릅니다.</summary>
    /// <remarks>
    /// 이 시점에 이미 <see cref="IsBuilt"/>는 거짓입니다. 걸어 둔 지속 효과를 여기서 풀어야 합니다.
    /// 풀지 않으면 함정이 꺼졌는데 적이 계속 느려진 채로 남습니다.
    /// </remarks>
    protected virtual void OnDepleted()
    {
    }

    /// <summary>다시 설치할 수 있게 된 순간 파생 함정이 한 번만 쓰는 상태를 초기화하도록 부릅니다.</summary>
    protected virtual void OnRearmed()
    {
    }

    /// <summary>
    /// 현재 상태에 맞는 외형을 적용합니다.
    /// </summary>
    /// <remarks>
    /// 머테리얼을 통째로 갈아 끼우는 이유는, 불투명 머테리얼을 <c>MaterialPropertyBlock</c>으로
    /// 반투명하게 만들 수 없기 때문입니다. 블렌드 모드·ZWrite·렌더 큐는 머테리얼에 속한 값이라
    /// 프로퍼티만 바꿔서는 통하지 않습니다. 같은 메시를 그대로 쓰므로 별도 청사진 메시를 만들 필요는 없습니다.
    ///
    /// 설치할 수 없는 청사진(<see cref="IsBuildable"/>이 거짓)은 렌더러를 꺼서 숨깁니다. 설치 구간이 아닐 때 청사진이
    /// 보이면 플레이어가 설치할 수 있다고 오해하기 때문입니다. 설치된 함정은 항상 보입니다.
    /// </remarks>
    private void ApplyBuildStateVisual()
    {
        bool isVisible = IsBuilt || IsBuildable;

        if (m_blueprintMaterial != null && m_visualRenderers != null)
        {
            for (int i = 0; i < m_visualRenderers.Length; i++)
            {
                Renderer renderer = m_visualRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (IsBlueprint)
                {
                    // 슬롯 수를 원본과 맞춰야 서브메시가 빠지지 않습니다.
                    var swapped = new Material[m_originalMaterials[i].Materials.Length];
                    for (int slot = 0; slot < swapped.Length; slot++)
                    {
                        swapped[slot] = m_blueprintMaterial;
                    }

                    renderer.sharedMaterials = swapped;
                    if (m_disableShadowWhileBlueprint)
                    {
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
                }
                else
                {
                    renderer.sharedMaterials = m_originalMaterials[i].Materials;
                    renderer.shadowCastingMode = m_originalShadowModes[i];
                }
            }
        }

        if (m_visualRenderers != null)
        {
            for (int i = 0; i < m_visualRenderers.Length; i++)
            {
                if (m_visualRenderers[i] != null)
                {
                    m_visualRenderers[i].enabled = isVisible && m_originalRendererEnabled[i];
                }
            }
        }

        if (m_blueprintVisualRoot != null)
        {
            m_blueprintVisualRoot.SetActive(IsBlueprint && isVisible);
        }

        if (m_builtVisualRoot != null)
        {
            m_builtVisualRoot.SetActive(IsBuilt);
        }
    }

    [Header("Debug")]
    [Tooltip("켜면 Scene View에 함정 범위를 그립니다. 대상이 범위 안에 있으면 색이 바뀝니다. 표시 전용이라 동작에는 영향이 없습니다.")]
    [SerializeField] private bool m_drawRangeGizmo = true;

    [Tooltip("범위 안에 아무도 없을 때의 색입니다.")]
    [ShowIf(nameof(m_drawRangeGizmo))]
    [SerializeField] private Color m_idleGizmoColor = new Color(0.2f, 0.9f, 1.0f, 0.5f);

    [Tooltip("범위 안에 대상이 있을 때의 색입니다.")]
    [SerializeField] private Color m_activeGizmoColor = new Color(1.0f, 0.15f, 0.1f, 0.8f);
    [EndIf]

    /// <summary>
    /// 지금 이 함정이 대상을 물고 있는지 여부입니다. 기즈모 색을 정하는 데 씁니다.
    /// </summary>
    /// <remarks>
    /// 무엇을 "밟은 상태"로 볼지는 함정마다 다르므로 파생이 답합니다. 기본은 항상 거짓입니다.
    /// </remarks>
    protected virtual bool HasTargetInRange => false;

    /// <summary>
    /// 파생 함정이 자기만의 범위를 덧그릴 자리입니다. 기본은 아무것도 그리지 않습니다.
    /// </summary>
    /// <remarks>
    /// <c>OnDrawGizmos</c>를 파생에서 다시 선언하면 기반과 파생 중 어느 쪽이 불릴지 보장되지 않습니다.
    /// 그래서 Unity가 부르는 진입점은 기반이 하나만 갖고, 파생은 이 훅을 재정의합니다.
    /// 감지 범위(콜라이더)와 효과 범위가 다른 함정이 있어서 필요합니다. 예를 들어 지뢰는 밟는 판정이
    /// 콜라이더이고 피해 범위는 그보다 훨씬 넓습니다.
    /// </remarks>
    protected virtual void OnDrawTrapGizmos()
    {
    }

    /// <summary>
    /// 함정 범위를 그립니다. 공통 콜라이더 범위를 먼저 그리고 파생 함정의 범위를 덧그립니다.
    /// </summary>
    /// <remarks>
    /// <c>OnDrawGizmosSelected</c>가 아니라 <c>OnDrawGizmos</c>인 이유는, 밟히는 순간을 보려면 선택하지 않은
    /// 상태에서도 보여야 하기 때문입니다. 함정은 여러 개를 깔아 두고 한꺼번에 지켜보게 됩니다.
    /// </remarks>
    private void OnDrawGizmos()
    {
        if (m_drawRangeGizmo)
        {
            DrawColliderRangeGizmo();
        }

        OnDrawTrapGizmos();
    }

    /// <summary>
    /// 함정이 가진 콜라이더 모양 그대로 범위를 그립니다.
    /// </summary>
    /// <remarks>
    /// <c>bounds</c>(월드 AABB) 대신 콜라이더의 <c>center</c>/<c>size</c>와 트랜스폼 행렬을 쓰는 이유는
    /// 회전 때문입니다. AABB로 그리면 비스듬히 놓인 함정의 실제 범위와 그림이 어긋납니다.
    /// </remarks>
    private void DrawColliderRangeGizmo()
    {
        bool active = HasTargetInRange;
        Color color = active ? m_activeGizmoColor : m_idleGizmoColor;
        Matrix4x4 previous = Gizmos.matrix;

        foreach (Collider collider in GetComponents<Collider>())
        {
            if (collider == null || !collider.enabled)
            {
                continue;
            }

            Gizmos.color = color;

            if (collider is BoxCollider box)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireCube(box.center, box.size);

                // 밟힌 순간은 선만으로는 눈에 잘 안 띄어서 안쪽도 옅게 채웁니다.
                if (active)
                {
                    Gizmos.color = new Color(color.r, color.g, color.b, color.a * 0.25f);
                    Gizmos.DrawCube(box.center, box.size);
                }
            }
            else if (collider is SphereCollider sphere)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireSphere(sphere.center, sphere.radius);
            }
            else
            {
                // 그 밖의 모양은 정확히 그릴 수단이 없어 월드 AABB로 대신합니다.
                Gizmos.matrix = Matrix4x4.identity;
                Gizmos.DrawWireCube(collider.bounds.center, collider.bounds.size);
            }
        }

        Gizmos.matrix = previous;
    }
}
