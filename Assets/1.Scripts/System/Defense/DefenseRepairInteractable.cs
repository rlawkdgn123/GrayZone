using UnityEngine;
using VInspector;

/// <summary>
/// 방어 목표(정문 등)를 바라보고 F를 꾹 누르면 자원을 내고 체력을 회복시키는 수리 상호작용입니다.
/// </summary>
/// <remarks>
/// 같은 오브젝트의 <see cref="DefenseEventHealth"/>를 고칩니다. 조준 레이가 이 오브젝트나 자식의 콜라이더에 맞으면
/// 상호작용 대상이 되고, 안내 패널은 함정 설치와 같은 <see cref="TrapInfoHud"/>가 보여 줍니다.
///
/// 게이지가 한 바퀴 찰 때마다 자원을 <see cref="m_costAmount"/>만큼 내고 최대 체력의 <see cref="m_repairRatio"/>만큼
/// 회복합니다. <see cref="m_repeatWhileHeld"/>가 켜져 있으면 누르고 있는 동안 체력이 다 차거나 자원이 떨어질 때까지 반복합니다.
/// 파괴된 뒤에는 대상이 되지 않습니다. 체력이 가득 찼거나 자원이 모자라면 대상으로는 잡혀 안내 패널에 이유
/// ("체력이 충분합니다", "자원이 부족합니다")를 보여 주고, 게이지는 채우지 않습니다. 다가갔는데 아무 안내도 없으면
/// 수리할 수 있는 대상인지 알 수 없어서입니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class DefenseRepairInteractable : MonoBehaviour,
    IInteractable, IInteractionRequirement, IRepeatingHoldInteractable, IInteractionHudInfo
{
    [Tooltip("수리할 방어 목표 체력입니다. 비워 두면 같은 오브젝트에서 찾습니다.")]
    [SerializeField] private DefenseEventHealth m_health;

    [Header("Cost")]
    [Tooltip("게이지가 한 바퀴 찰 때마다 소모할 자원입니다. 셸터에 정의된 자원 중에서 고릅니다.")]
    [Variants(
        ResourceIds.UpgradeMaterial,
        ResourceIds.CraftingMaterial,
        ResourceIds.TrapMaterial)]
    [SerializeField] private string m_costResourceId = ResourceIds.CraftingMaterial;

    [Tooltip("게이지가 한 바퀴 찰 때마다 소모할 자원 수량입니다. 0이면 자원 없이 수리합니다.")]
    [Min(0)]
    [SerializeField] private int m_costAmount = 1;

    [Tooltip("안내 패널의 비용 아이콘으로 쓸 이미지입니다. 선택 항목이며, 비워 두면 HUD에 배치된 기본 아이콘(함정 재료)을 씁니다.")]
    [SerializeField] private Sprite m_costIcon;

    [Header("Repair")]
    [Tooltip("게이지가 한 바퀴 차는 데 걸리는 홀드 시간(초)입니다.")]
    [Min(0.05f)]
    [SerializeField] private float m_holdDuration = 2.0f;

    [Tooltip("한 바퀴마다 회복할 체력 비율입니다. 최대 체력 기준이며 0.1이면 10%입니다. 최소 1은 회복합니다.")]
    [Range(0.01f, 1.0f)]
    [SerializeField] private float m_repairRatio = 0.1f;

    [Tooltip("켜면 F를 누르고 있는 동안 게이지를 반복해서 채웁니다. 끄면 한 번 누를 때 한 바퀴만 수리합니다.")]
    [SerializeField] private bool m_repeatWhileHeld = true;

    [Header("HUD")]
    [Tooltip("안내 패널 제목입니다.")]
    [SerializeField] private string m_displayName = "정문 수리";

    [Tooltip("안내 패널 설명입니다. {0}에 한 바퀴당 회복 비율(%), {1}에 현재 내구도, {2}에 최대 내구도가 들어갑니다. 현재 내구도는 제목 옆에 자동으로 붙습니다.")]
    [TextArea(2, 4)]
    [SerializeField] private string m_description = "F를 누르고 있으면 내구도를 {0}%씩 회복합니다.";

    [Tooltip("상호작용 프롬프트 문구입니다.")]
    [SerializeField] private string m_prompt = "수리";

    [Tooltip("내구도가 가득 차 수리할 수 없을 때 비용 옆에 붙일 문구입니다.")]
    [SerializeField] private string m_fullHealthMessage = "내구도가 가득 찼습니다";

    /// <summary>한 바퀴에 회복하는 체력입니다.</summary>
    public int RepairAmount => m_health != null
        ? Mathf.Max(1, Mathf.CeilToInt(m_health.MaxHP * m_repairRatio))
        : 0;

    /// <summary>한 바퀴에 소모하는 자원입니다.</summary>
    public ResourceCost Cost => new ResourceCost(m_costResourceId, m_costAmount);

    /// <inheritdoc />
    public float HoldDuration => m_holdDuration;

    /// <inheritdoc />
    public bool RepeatWhileHeld => m_repeatWhileHeld;

    /// <inheritdoc />
    /// <remarks>함정과 같은 방식으로 제목 옆에 현재/최대 내구도를 붙입니다(예: "정문 수리 (내구도 : 800/1000)").</remarks>
    public string HudDisplayName => m_health != null
        ? $"{m_displayName} (내구도 : {m_health.CurrentHP}/{m_health.MaxHP})"
        : m_displayName;

    /// <inheritdoc />
    public string HudDescription => m_health != null
        ? string.Format(m_description, Mathf.RoundToInt(m_repairRatio * 100.0f), m_health.CurrentHP, m_health.MaxHP)
        : m_displayName;

    /// <inheritdoc />
    public string HudActionLabel => m_prompt;

    /// <inheritdoc />
    public ResourceCost HudCost => Cost;

    /// <inheritdoc />
    public bool CanAffordHudCost => CanPayCost();

    /// <inheritdoc />
    public string HudBlockedMessage => IsFullHealth ? m_fullHealthMessage : null;

    /// <inheritdoc />
    public bool IsHudCostWaived => false;

    /// <summary>체력이 가득 차 수리할 것이 없는지 여부입니다.</summary>
    public bool IsFullHealth => m_health != null && m_health.CurrentHPExact >= m_health.MaxHP;

    /// <inheritdoc />
    public Sprite HudCostIcon => m_costIcon;

    /// <inheritdoc />
    public GameObject HudOutlineTarget => gameObject;

    private void Awake()
    {
        if (m_health == null)
        {
            m_health = GetComponent<DefenseEventHealth>();
        }

        if (m_health == null)
        {
            Debug.LogWarning($"[DefenseRepairInteractable] '{name}'에 DefenseEventHealth가 없어 수리할 수 없습니다.", this);
        }
    }

    /// <inheritdoc />
    /// <remarks>파괴됐으면 대상에서 빠집니다. 체력이 가득 찬 것과 자원이 모자란 것은 빼지 않습니다(안내를 보여 주기 위해).</remarks>
    public bool CanInteract(GameObject interactor)
    {
        return m_health != null
            && m_health.isActiveAndEnabled
            && !m_health.IsDead;
    }

    /// <inheritdoc />
    /// <remarks>체력이 가득 찼거나 자원이 모자라면 게이지를 채우지 않습니다. 반복 수리 중에 체력이 다 차면 여기서 멈춥니다.</remarks>
    public bool MeetsInteractionRequirement(GameObject interactor)
    {
        return !IsFullHealth && CanPayCost();
    }

    /// <inheritdoc />
    public string GetPrompt()
    {
        ResourceCost cost = Cost;
        return cost.IsValid ? $"{m_prompt} ({cost.ResourceId} {cost.Amount})" : m_prompt;
    }

    /// <inheritdoc />
    /// <remarks>자원을 먼저 낸 뒤 회복합니다. 자원을 내지 못하면 회복하지 않습니다.</remarks>
    public void Interact(GameObject interactor)
    {
        if (!CanInteract(interactor) || IsFullHealth || !TryPayCost())
        {
            return;
        }

        m_health.Heal(RepairAmount);
    }

    /// <summary>자원을 실제로 내지 않고 지금 낼 수 있는지만 확인합니다.</summary>
    private bool CanPayCost()
    {
        ResourceCost cost = Cost;
        if (!cost.IsValid)
        {
            return true;
        }

        StorageFacility storage = ShelterSceneDataManager.Instance?.Storage;
        return storage != null && storage.CanSpendResource(cost);
    }

    /// <summary>비용을 차감합니다. 모자라면 아무것도 차감하지 않고 false를 돌려줍니다.</summary>
    private bool TryPayCost()
    {
        ResourceCost cost = Cost;
        if (!cost.IsValid)
        {
            return true;
        }

        StorageFacility storage = ShelterSceneDataManager.Instance?.Storage;
        if (storage == null)
        {
            Debug.LogWarning(
                $"[DefenseRepairInteractable] '{name}': 자원 보관소를 찾지 못해 수리할 수 없습니다. " +
                "이 씬에 ShelterSceneDataManager가 있는지 확인하세요.",
                this);
            return false;
        }

        return storage.TrySpendResource(cost);
    }
}
