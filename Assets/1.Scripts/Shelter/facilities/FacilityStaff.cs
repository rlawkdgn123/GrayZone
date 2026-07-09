using System.Collections.Generic;

public class FacilityStaff
{
    private readonly List<NPCRuntimeData> assigned = new List<NPCRuntimeData>();
    private readonly StaffAssignment slots;

    public FacilityStaff(int maxStaff)
    {
        slots = new StaffAssignment(maxStaff);
    }

    /// <summary>현재 배치된 NPC(읽기 전용). 조율자가 타입별 집계 등에 사용한다.</summary>
    public IReadOnlyList<NPCRuntimeData> Assigned => assigned;

    public int CurrentCount => assigned.Count;
    public int MaxCount => slots.MaxPeople;

    /// <summary>현재 시설에 배치 빈자리가 있는지</summary>
    public bool HasSpace => slots.CanAssign(assigned.Count, 1);

    public bool Contains(NPCRuntimeData staff)
    {
        return staff != null && assigned.Contains(staff);
    }

    /// <summary>
    /// 스태프를 배치한다.
    /// 반환값은 "명단이 실제로 바뀌었는지(= 새로 추가됐는지)"를 뜻한다.
    /// 이미 배치돼 있거나(중복), 정원 초과거나, null이면 false.
    /// → 조율자는 true일 때만 보너스/이벤트 같은 부수효과를 1회 적용하면 된다.
    /// </summary>
    public bool TryAssign(NPCRuntimeData staff)
    {
        if (staff == null) return false;
        if (assigned.Contains(staff)) return false;
        if (!slots.CanAssign(assigned.Count, 1)) return false;

        assigned.Add(staff);
        return true;
    }

    /// <summary>
    /// 스태프를 해제한다. 실제로 명단에서 제거됐을 때만 true.
    /// </summary>
    public bool TryRelease(NPCRuntimeData staff)
    {
        return staff != null && assigned.Remove(staff);
    }

    /// <summary>정원 상향(업그레이드). 늘어났으면 true.</summary>
    public bool TryUpgradeCapacity(int amount)
    {
        return slots.TryUpgradeCapacity(amount);
    }
}
