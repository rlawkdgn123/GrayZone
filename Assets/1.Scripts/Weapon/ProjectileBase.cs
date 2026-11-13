using UnityEngine;

/// <summary>
/// 투척기와 포물선 이동기가 공통으로 다루는 투척물 기반 컴포넌트입니다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public abstract class ProjectileBase : MonoBehaviour
{
    [Header("Inventory")]
    [Tooltip("이 투척물을 한 번 사용할 때 1개 소모할 인벤토리 아이템 정의입니다.")]
    [SerializeField] private ItemDefinition m_inventoryItemDefinition;

    [Header("Held Visual")]
    [Tooltip("투척 모드에서 이 투척물을 선택했을 때 손에 표시할 Visual Prefab입니다. 비우면 투척기의 기본 Visual을 사용합니다.")]
    [SerializeField] private GameObject m_heldVisualPrefab;

    [Tooltip("손 장착 소켓 기준 Visual의 로컬 위치입니다.")]
    [SerializeField] private Vector3 m_heldLocalPosition = Vector3.zero;

    [Tooltip("손 장착 소켓 기준 Visual의 로컬 회전입니다.")]
    [SerializeField] private Vector3 m_heldLocalEulerAngles = Vector3.zero;

    [Tooltip("손 장착 소켓 기준 Visual의 로컬 크기입니다.")]
    [SerializeField] private Vector3 m_heldLocalScale = Vector3.one * 0.25f;

    /// <summary>손에 표시할 Visual Prefab입니다. null이면 투척기 기본 Visual을 사용합니다.</summary>
    public GameObject HeldVisualPrefab => m_heldVisualPrefab;

    /// <summary>손 장착 소켓 기준 Visual의 로컬 위치입니다.</summary>
    public Vector3 HeldLocalPosition => m_heldLocalPosition;

    /// <summary>손 장착 소켓 기준 Visual의 로컬 회전입니다.</summary>
    public Quaternion HeldLocalRotation => Quaternion.Euler(m_heldLocalEulerAngles);

    /// <summary>손 장착 소켓 기준 Visual의 로컬 크기입니다.</summary>
    public Vector3 HeldLocalScale => m_heldLocalScale;

    /// <summary>이 투척물과 연결된 인벤토리 아이템 정의입니다.</summary>
    public ItemDefinition InventoryItemDefinition => m_inventoryItemDefinition;

    /// <summary>인벤토리에서 조회하고 소모할 아이템 정의 ID입니다.</summary>
    public string InventoryItemDefinitionId => m_inventoryItemDefinition != null
        ? m_inventoryItemDefinition.ItemDefinitionId
        : string.Empty;

    /// <summary>이 투척물을 던진 캐릭터입니다. 모르면 null입니다.</summary>
    public GameObject Thrower { get; private set; }

    /// <summary>던진 캐릭터를 기록합니다. 피해 정보의 공격자로 씁니다.</summary>
    public void SetThrower(GameObject thrower)
    {
        Thrower = thrower;
    }

    /// <summary>
    /// 이 투척물의 피해 정보 틀입니다. 공격 방식은 투척물, 세부 종류는 아이템 ID(없으면 오브젝트 이름), 공격자는 던진 캐릭터입니다.
    /// </summary>
    /// <param name="amount">피해량입니다. 폭발처럼 대상마다 피해량을 채우는 경로에는 0을 넘깁니다.</param>
    public DamageInfo CreateDamageInfo(float amount = 0.0f)
    {
        string sourceId = !string.IsNullOrEmpty(InventoryItemDefinitionId) ? InventoryItemDefinitionId : name;
        return new DamageInfo(amount, DamageCategory.Throwable, sourceId, Thrower);
    }

    /// <summary>투척 경로를 계산할 최대 비행 시간입니다.</summary>
    public virtual float FlightTimeLimit => float.PositiveInfinity;

    /// <summary>예상 충돌 지점에 표시할 원형 범위의 반지름입니다. 0이면 표시하지 않습니다.</summary>
    public virtual float ImpactPreviewRadius => 0.0f;

    /// <summary>투척 경로와 실제 이동 중 충돌 대상에서 제외할 Layer입니다.</summary>
    public virtual LayerMask ContactExcludeLayers => default;

    /// <summary>
    /// 투척물이 표면에 도달했을 때 고유 효과를 실행합니다.
    /// </summary>
    /// <param name="projectilePosition">충돌 순간 투척물 중심 위치입니다.</param>
    /// <param name="contactPoint">표면과 실제로 맞닿은 위치입니다.</param>
    /// <param name="surfaceNormal">맞닿은 표면의 법선입니다.</param>
    public abstract void ImpactAt(
        Vector3 projectilePosition,
        Vector3 contactPoint,
        Vector3 surfaceNormal);
}
