using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 셸터 HUD의 ItemList 칸마다 창고 자원·아이템 보유 수량을 표시합니다.
/// </summary>
/// <remarks>
/// 레이아웃은 씬에 미리 배치돼 있고, 이 컴포넌트는 <see cref="ShelterSceneDataManager.Storage"/>의
/// 변경 이벤트를 받아 각 칸의 수량 텍스트와 흐림만 갱신합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class ShelterItemListHud : MonoBehaviour
{
    /// <summary>칸이 읽어올 창고 데이터 종류입니다.</summary>
    public enum SlotSource
    {
        /// <summary>자원 ID(<see cref="ResourceIds"/>)로 조회합니다.</summary>
        Resource,
        /// <summary>아이템 정의 ID(예: Potion_01)로 조회합니다.</summary>
        Item
    }

    [Serializable]
    private sealed class Slot
    {
        [Tooltip("수량을 읽어올 데이터 종류입니다.")]
        public SlotSource Source = SlotSource.Item;

        [Tooltip("Resource면 자원 ID(upgrade_material 등), Item이면 아이템 정의 ID(Potion_01 등)입니다.")]
        public string Id;

        [Tooltip("칸 아이콘입니다. 수량이 0이면 흐리게 표시합니다. 선택 사항입니다.")]
        public Image Icon;

        [Tooltip("보유 수량 텍스트입니다.")]
        public TMP_Text CountText;
    }

    [Header("Runtime Data")]
    [Tooltip("비어 있으면 ShelterSceneDataManager.Instance를 사용합니다.")]
    [SerializeField] private ShelterSceneDataManager m_shelterDataManager;

    [Header("Slots")]
    [SerializeField] private Slot[] m_slots =
    {
        new Slot { Source = SlotSource.Resource, Id = ResourceIds.CraftingMaterial },
        new Slot { Source = SlotSource.Resource, Id = ResourceIds.UpgradeMaterial },
        new Slot { Source = SlotSource.Resource, Id = ResourceIds.TrapMaterial },
        new Slot { Source = SlotSource.Resource, Id = ResourceIds.TrapUpgradeMaterial },
        new Slot { Source = SlotSource.Item, Id = "Potion_01" },
        new Slot { Source = SlotSource.Item, Id = "Grenade_01" },
        new Slot { Source = SlotSource.Item, Id = "Firebomb_01" }
    };

    [Header("Display")]
    [Tooltip("수량이 0일 때 아이콘과 수량의 불투명도(0~1)입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_emptyAlpha = 0.35f;

    private StorageFacility m_boundStorage;

    private void OnEnable()
    {
        BindStorage();
        Refresh();
    }

    private void Start()
    {
        // ShelterSceneDataManager가 이 컴포넌트보다 늦게 활성화된 경우를 위해 한 번 더 바인딩합니다.
        BindStorage();
        Refresh();
    }

    private void OnDisable()
    {
        UnbindStorage();
    }

    /// <summary>현재 창고 수량으로 모든 칸을 다시 그립니다.</summary>
    public void Refresh()
    {
        if (m_boundStorage == null || m_slots == null)
        {
            return;
        }

        for (int i = 0; i < m_slots.Length; i++)
        {
            Slot slot = m_slots[i];
            if (slot == null || string.IsNullOrWhiteSpace(slot.Id))
            {
                continue;
            }

            int count = slot.Source == SlotSource.Resource
                ? m_boundStorage.GetResourceAmount(slot.Id)
                : m_boundStorage.GetItemQuantity(slot.Id);

            if (slot.CountText != null)
            {
                slot.CountText.text = count.ToString();
            }

            float alpha = count > 0 ? 1.0f : m_emptyAlpha;
            SetAlpha(slot.Icon, alpha);
            SetAlpha(slot.CountText, alpha);
        }
    }

    private void BindStorage()
    {
        if (m_shelterDataManager == null)
        {
            m_shelterDataManager = ShelterSceneDataManager.Instance;
        }

        StorageFacility storage = m_shelterDataManager != null ? m_shelterDataManager.Storage : null;
        if (ReferenceEquals(m_boundStorage, storage))
        {
            return;
        }

        UnbindStorage();
        m_boundStorage = storage;
        if (m_boundStorage == null)
        {
            return;
        }

        m_boundStorage.ResourcesChanged += Refresh;
        m_boundStorage.ItemsChanged += Refresh;
    }

    private void UnbindStorage()
    {
        if (m_boundStorage == null)
        {
            return;
        }

        m_boundStorage.ResourcesChanged -= Refresh;
        m_boundStorage.ItemsChanged -= Refresh;
        m_boundStorage = null;
    }

    private static void SetAlpha(Graphic graphic, float alpha)
    {
        if (graphic == null)
        {
            return;
        }

        Color color = graphic.color;
        color.a = alpha;
        graphic.color = color;
    }
}
