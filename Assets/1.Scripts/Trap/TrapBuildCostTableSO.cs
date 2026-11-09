using System;
using UnityEngine;
using VInspector;

/// <summary>
/// 가격 표에서 함정을 가리키는 종류입니다.
/// </summary>
/// <remarks>
/// 순서를 바꾸지 마세요. 직렬화는 정수로 되므로 중간에 끼우면 이미 저장된 표의 의미가 바뀝니다. 새 종류는 끝에 붙입니다.
/// </remarks>
public enum TrapKind
{
    /// <summary>가격 표를 쓰지 않는 함정입니다(화염 지대 등). 프리팹 값을 그대로 씁니다.</summary>
    None,
    Mine,
    Claymore,
    Wire,
    Spike,
    FireBarrel,
}

/// <summary>
/// 함정 종류별 설치 가격 표입니다. 기획자가 함정 가격을 한곳에서 조절합니다.
/// </summary>
/// <remarks>
/// 함정은 이 표에 자기 종류가 있으면 표 값을 쓰고, 없으면 프리팹에 적힌 값을 씁니다.
/// 표는 <c>Resources/Trap/TrapBuildCostTable</c>에서 자동으로 불러옵니다. 씬마다 연결하지 않아도 셸터·방어전 어디서든 같은 값이 쓰입니다.
/// 디버그로 무료 설치가 필요하면 표를 고치지 말고 F9 방어전 탭의 "트랩 전체 무료"를 씁니다.
/// </remarks>
[CreateAssetMenu(fileName = "TrapBuildCostTable", menuName = "GrayZone/Defense/Trap Build Cost Table")]
public sealed class TrapBuildCostTableSO : ScriptableObject
{
    /// <summary>표를 불러오는 Resources 경로입니다.</summary>
    public const string ResourcePath = "Trap/TrapBuildCostTable";

    [Serializable]
    public struct Entry
    {
        [Tooltip("가격을 정할 함정 종류입니다.")]
        public TrapKind Kind;

        [Tooltip("설치할 때 소모할 자원입니다.")]
        [Variants(
            ResourceIds.UpgradeMaterial,
            ResourceIds.CraftingMaterial,
            ResourceIds.TrapMaterial)]
        public string ResourceId;

        [Tooltip("소모할 수량입니다. 0이면 무료로 설치됩니다.")]
        [Min(0)]
        public int Amount;

        [Tooltip("이미 설치된 함정을 수리할 때 소모할 수량입니다. 자원 종류는 설치와 같습니다. 0이면 무료로 수리됩니다. " +
                 "수리는 내구도를 최대치로 채우는 것으로, 새것으로 바꿔 설치하는 것과 같습니다. 수리할 수 있는 함정(철조망·스파이크)만 씁니다.")]
        [Min(0)]
        public int RepairAmount;
    }

    [Tooltip("함정 종류별 가격입니다. 같은 종류가 여러 번 있으면 위의 것을 씁니다.")]
    [SerializeField] private Entry[] m_entries =
    {
        new Entry { Kind = TrapKind.Mine, ResourceId = ResourceIds.TrapMaterial, Amount = 0 },
        new Entry { Kind = TrapKind.Claymore, ResourceId = ResourceIds.TrapMaterial, Amount = 0 },
        new Entry { Kind = TrapKind.Wire, ResourceId = ResourceIds.TrapMaterial, Amount = 0 },
        new Entry { Kind = TrapKind.Spike, ResourceId = ResourceIds.TrapMaterial, Amount = 0 },
        new Entry { Kind = TrapKind.FireBarrel, ResourceId = ResourceIds.TrapMaterial, Amount = 0 },
    };

    /// <summary>지정한 종류의 가격을 찾습니다. 표에 없거나 종류가 <see cref="TrapKind.None"/>이면 <c>false</c>입니다.</summary>
    public bool TryGetCost(TrapKind kind, out ResourceCost cost)
    {
        cost = default;
        if (!TryGetEntry(kind, out Entry entry))
        {
            return false;
        }

        cost = new ResourceCost(entry.ResourceId, Mathf.Max(0, entry.Amount));
        return true;
    }

    /// <summary>지정한 종류의 수리 가격을 찾습니다. 표에 없거나 종류가 <see cref="TrapKind.None"/>이면 <c>false</c>입니다.</summary>
    public bool TryGetRepairCost(TrapKind kind, out ResourceCost cost)
    {
        cost = default;
        if (!TryGetEntry(kind, out Entry entry))
        {
            return false;
        }

        cost = new ResourceCost(entry.ResourceId, Mathf.Max(0, entry.RepairAmount));
        return true;
    }

    private bool TryGetEntry(TrapKind kind, out Entry entry)
    {
        entry = default;
        if (kind == TrapKind.None || m_entries == null)
        {
            return false;
        }

        for (int i = 0; i < m_entries.Length; i++)
        {
            if (m_entries[i].Kind == kind)
            {
                entry = m_entries[i];
                return true;
            }
        }

        return false;
    }
}
