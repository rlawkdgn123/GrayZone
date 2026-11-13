using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 출격 중인 스쿼드가 공동으로 쓰는 필드 인벤토리를 소유하는 매니저입니다.
/// </summary>
/// <remarks>
/// <para>
/// 공용 `인벤토리 시스템` §2·§3.1에 따라 인벤토리는 <b>스쿼드 전체에 하나</b>입니다. 캐릭터별로 따로 두지 않고,
/// 조작 멤버를 교체해도 내용을 옮기거나 재배치하지 않습니다. 그래서 이 컴포넌트는 멤버 프리팹이 아니라
/// <see cref="FieldManager"/> 자식에 하나만 존재하며, <see cref="NoiseManager"/>가 필드의 소음 성질을
/// 한 자리에서 소유하는 것과 같은 구조입니다.
/// </para>
/// <para>
/// 슬롯 판정 자체는 이 매니저가 아니라 <see cref="SlotContainer{T}"/>가 소유합니다. 여기가 맡는 것은
/// 필드 쪽 배선뿐입니다 — 슬롯 수, 아이템 정의 조회, 스택 한도 주입, 획득 진입점.
/// </para>
/// <para>
/// 내용물 타입이 <see cref="ItemDefinition"/> 참조가 아니라 <b>아이템 정의 ID 문자열</b>인 이유:
/// §3.4가 "아이템 ID가 같으면 동일한 아이템"으로 스택 조건을 정의하고, 저장과 셸터 창고
/// (<see cref="ItemStorageEntry"/>)가 이미 같은 ID를 정본으로 쓰기 때문입니다. 아이콘·표시명은
/// 표시 시점에 <see cref="ItemDefinitionCatalog"/>로 조회합니다.
/// </para>
/// <para>
/// <b>범위</b>: 이 슬라이스는 보유와 획득까지입니다. 인벤토리 UI(§5·§7·§8), 인벤토리 박스 연동(§5.2),
/// 버리기와 임시 드롭 박스(§14·§15), 귀환 정산(§17.3), 저장(아이템 세이브 스키마 미확정)은 아직 없습니다.
/// </para>
/// </remarks>
[DisallowMultipleComponent]
public sealed class SquadInventoryManager : MonoBehaviour
{
    [Tooltip("스쿼드 공용 인벤토리의 총 슬롯 수입니다. 공용 문서 §18에서 밸런스 값으로 정의될 예정이라 확정 전까지 여기서 조정합니다.")]
    [Min(1)]
    [SerializeField] private int m_slotCount = 20;

    [Tooltip("아이템 ID로 정적 정의를 찾는 카탈로그입니다. 비어 있으면 스택 한도를 알 수 없어 모든 아이템이 1개씩만 쌓입니다.")]
    [SerializeField] private ItemDefinitionCatalog m_itemCatalog;

    [Header("Debug")]
    [Tooltip("획득 결과(성공·부분·공간부족)를 콘솔에 남깁니다. 파밍 중에는 획득마다 한 줄씩 늘어납니다.")]
    [SerializeField] private bool m_debugLogAcquire;

    private SlotContainer<string> m_inventory;

    /// <summary>스쿼드 공용 인벤토리입니다. 최초 접근 시 만들어집니다.</summary>
    /// <remarks>
    /// 컨테이너를 그대로 노출하는 이유: 조회와 정리·이동은 이미 코어의 공개 API이고, 여기서 한 겹 더
    /// 감싸면 같은 판정이 두 곳에 생깁니다. UI와 드래그 앤 드롭은 이 컨테이너에 확장 메서드로 붙습니다.
    /// </remarks>
    public SlotContainer<string> Inventory => m_inventory ??= CreateInventory();

    /// <summary>아이템 ID로 정적 정의를 찾는 카탈로그입니다. 배선되지 않았으면 <c>null</c>입니다.</summary>
    public ItemDefinitionCatalog ItemCatalog => m_itemCatalog;

    /// <summary>
    /// 켜면 <see cref="TryConsumeOne"/>이 수량을 줄이지 않고 성공하고, 수량이 0이어도 쓸 수 있습니다.
    /// 튜토리얼의 "아이템 소모 없이 사용" 이벤트가 켜고 끕니다. 표시 수량은 바뀌지 않습니다.
    /// </summary>
    public static bool FreeUse { get; set; }

    /// <summary>Play를 다시 시작할 때 소모 없이 사용을 끕니다. 도메인 리로드를 끈 설정에서도 이전 값이 남지 않게 합니다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetFreeUse()
    {
        FreeUse = false;
    }

    /// <summary>인벤토리 칸 하나가 바뀔 때 그 인덱스와 함께 발생합니다.</summary>
    public event Action<int> SlotChanged;

    /// <summary>여러 칸이 한꺼번에 바뀔 때 발생합니다.</summary>
    public event Action BulkChanged;

    private void Awake()
    {
        // 첫 획득이 아니라 시작 시점에 만들어, 인벤토리 구독자가 Awake 순서에 상관없이 같은 인스턴스를 받게 합니다.
        m_inventory ??= CreateInventory();

        InitializeFromGameDataManager();

        if (m_itemCatalog == null)
        {
            Debug.LogWarning(
                "[SquadInventoryManager] ItemDefinitionCatalog가 배선되지 않았습니다. 아이템별 최대 스택 수량을 알 수 없어 모든 아이템이 슬롯당 1개씩만 쌓입니다.",
                this);
        }
    }

    /// <summary>필드 시작 시 전역 셸터 창고의 아이템 수량을 스쿼드 공용 인벤토리로 복사합니다.</summary>
    private void InitializeFromGameDataManager()
    {
        if (GameDataManager.Instance == null)
            return;

        IReadOnlyList<ItemStorageEntry> startingItems = GameDataManager.Instance.ItemStorageEntries;
        for (int i = 0; i < startingItems.Count; i++)
        {
            ItemStorageEntry entry = startingItems[i];
            if (entry == null || !entry.IsValid)
                continue;

            TryAcquire(entry.ItemDefinitionId, entry.Quantity, out int leftover);
            if (leftover > 0)
            {
                Debug.LogWarning(
                    $"[SquadInventoryManager] 시작 아이템 {entry.ItemDefinitionId} x{entry.Quantity} 중 {leftover}개를 넣지 못했습니다.",
                    this);
            }
        }
    }

    /// <summary>
    /// 아이템을 인벤토리에 넣습니다. 같은 아이템의 미완성 스택을 먼저 채우고, 남으면 앞쪽 빈 칸부터 사용합니다.
    /// </summary>
    /// <remarks>
    /// 배치 순서는 §11.3, 공간이 부족할 때 수용 가능한 만큼만 넣고 나머지를 돌려주는 것은 §13입니다.
    /// 공간 부족을 이유로 획득 전체를 취소하지 않습니다.
    /// </remarks>
    /// <param name="itemDefinitionId">넣을 아이템의 정의 ID입니다.</param>
    /// <param name="quantity">넣으려는 수량입니다. 0이면 아무 일도 하지 않습니다.</param>
    /// <param name="leftover">공간이 없어 넣지 못한 수량입니다. 호출부가 이 수량을 원래 자리에 남겨야 합니다.</param>
    /// <returns>
    /// 전부 넣었으면 <see cref="SlotOpResult.Success"/>, 일부만 넣었거나 한 개도 못 넣었으면
    /// <see cref="SlotOpResult.StackLimitReached"/>입니다. 두 경우의 구분은 <paramref name="leftover"/>로 합니다.
    /// </returns>
    public SlotOpResult TryAcquire(string itemDefinitionId, int quantity, out int leftover)
    {
        leftover = quantity;
        if (string.IsNullOrWhiteSpace(itemDefinitionId))
        {
            Debug.LogWarning("[SquadInventoryManager] 아이템 ID가 비어 있어 획득을 무시했습니다.", this);
            return SlotOpResult.ContentMismatch;
        }

        if (quantity <= 0)
        {
            leftover = 0;
            return SlotOpResult.NoChange;
        }

        SlotOpResult result = Inventory.TryAdd(itemDefinitionId.Trim(), quantity, out leftover);

        if (m_debugLogAcquire)
        {
            Debug.Log(
                $"[SquadInventoryManager] 획득 {itemDefinitionId} x{quantity} -> {result}, 남은 수량 {leftover}",
                this);
        }

        return result;
    }

    /// <summary>해당 아이템을 인벤토리 전체에서 몇 개 들고 있는지 셉니다.</summary>
    public int CountOf(string itemDefinitionId)
    {
        return string.IsNullOrWhiteSpace(itemDefinitionId)
            ? 0
            : Inventory.CountOf(itemDefinitionId.Trim());
    }

    /// <summary>해당 아이템을 앞쪽의 사용 가능한 슬롯에서 1개 소모합니다.</summary>
    public bool TryConsumeOne(string itemDefinitionId)
    {
        if (string.IsNullOrWhiteSpace(itemDefinitionId))
            return false;

        if (FreeUse)
            return true;

        string normalizedId = itemDefinitionId.Trim();
        for (int i = 0; i < Inventory.SlotCount; i++)
        {
            if (!Inventory.IsUnlocked(i)
                || Inventory.IsContentLocked(i)
                || Inventory.IsEmpty(i)
                || !string.Equals(Inventory.GetContent(i), normalizedId, StringComparison.Ordinal))
            {
                continue;
            }

            return Inventory.TryRemove(i, 1) == SlotOpResult.Success;
        }

        return false;
    }

    /// <summary>슬롯에 들어 있는 아이템의 정적 정의를 찾습니다. 빈 칸이거나 카탈로그에 없으면 <c>false</c>입니다.</summary>
    public bool TryGetSlotDefinition(int slotIndex, out ItemDefinition definition)
    {
        definition = null;
        if (m_itemCatalog == null || Inventory.IsEmpty(slotIndex))
            return false;

        return m_itemCatalog.TryGetDefinition(Inventory.GetContent(slotIndex), out definition);
    }

    private SlotContainer<string> CreateInventory()
    {
        var inventory = new SlotContainer<string>(Mathf.Max(1, m_slotCount), ResolveStackLimit);
        inventory.SlotChanged += index => SlotChanged?.Invoke(index);
        inventory.BulkChanged += () => BulkChanged?.Invoke();
        return inventory;
    }

    /// <summary>아이템별 최대 스택 수량을 카탈로그에서 읽습니다.</summary>
    /// <remarks>
    /// 카탈로그가 없거나 ID를 못 찾으면 1을 돌려줍니다. 기본값을 크게 잡으면 배선이 빠진 상태에서도
    /// 인벤토리가 그럴듯하게 동작해 누락을 늦게 발견합니다. 슬롯당 1개는 눈에 바로 띕니다.
    /// </remarks>
    private int ResolveStackLimit(string itemDefinitionId)
    {
        if (m_itemCatalog != null
            && m_itemCatalog.TryGetDefinition(itemDefinitionId, out ItemDefinition definition)
            && definition != null)
        {
            return definition.MaxStackQuantity;
        }

        return 1;
    }
}
