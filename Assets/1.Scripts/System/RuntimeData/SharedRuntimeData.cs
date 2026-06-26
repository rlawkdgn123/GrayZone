using System;
using UnityEngine;

[Serializable]
public class SharedRuntimeData
{
    //ToDo : 공유 데이터 확인
    [SerializeField] private string lastStageId = string.Empty;
    //셸터 안정도
    [SerializeField] private int shelterStability = 100;
    //플레이어블 캐릭터 수 ( 나중에 리스트 단위로 관리)
    [SerializeField] private int playableCharacterCount;
    //논플레이어블 캐릭터수 ( 나중에 리스트 단위로 관리할수도 있음, npc종류가 다양해질경우 )
    [SerializeField] private int npcCount;

    //창고에 대한 정보
    private ResourceStorage resources;
    //ToDo : Battle쪽 NPC 데이터 넘겨주는 방식에 맞춰야함 (NPC 데이터 정보 )
    private NpcRoster npcRoster;

    public string LastStageId => lastStageId ?? string.Empty;
    public int ShelterStability => Mathf.Clamp(shelterStability, 0, 100);
    public int RosterCount => NpcRoster.Count;
    public int TotalOwnedCharacterCount => PlayableCharacterCount + NonPlayableNpcCount;
    public int PlayableCharacterCount => Mathf.Max(0, playableCharacterCount);
    public int NonPlayableNpcCount => Mathf.Max(0, npcCount);

    public ResourceStorage Resources
    {
        get
        {
            resources ??= new ResourceStorage();
            return resources;
        }
    }

    public NpcRoster NpcRoster
    {
        get
        {
            npcRoster ??= new NpcRoster();
            return npcRoster;
        }
    }

    public void EnsureRuntimeContainers()
    {
        lastStageId ??= string.Empty;
        shelterStability = Mathf.Clamp(shelterStability, 0, 100);
        playableCharacterCount = Mathf.Max(0, playableCharacterCount);
        npcCount = Mathf.Max(0, npcCount);

        _ = Resources;
        _ = NpcRoster;
    }

    public SharedRuntimeData Clone()
    {
        EnsureRuntimeContainers();

        SharedRuntimeData clone = new SharedRuntimeData
        {
            lastStageId = LastStageId,
            shelterStability = ShelterStability,
            playableCharacterCount = PlayableCharacterCount,
            npcCount = NonPlayableNpcCount
        };

        clone.Resources.CopyFrom(Resources);
        foreach (NPCRuntimeData npc in NpcRoster.All)
        {
            clone.NpcRoster.Add(npc?.Clone());
        }

        return clone;
    }

    public void CopyFrom(SharedRuntimeData source)
    {
        if (source == null)
            return;

        source.EnsureRuntimeContainers();
        EnsureRuntimeContainers();

        SetLastStageId(source.LastStageId);
        SetShelterStability(source.ShelterStability);
        SetOwnedCharacterCounts(source.PlayableCharacterCount, source.NonPlayableNpcCount);

        Resources.CopyFrom(source.Resources);

        NpcRoster.Clear();
        foreach (NPCRuntimeData npc in source.NpcRoster.All)
        {
            NpcRoster.Add(npc?.Clone());
        }
    }

    public void SetLastStageId(string stageId)
    {
        lastStageId = stageId ?? string.Empty;
    }

    public void SetShelterStability(int stability)
    {
        shelterStability = Mathf.Clamp(stability, 0, 100);
    }

    public void SetOwnedCharacterCounts(int playableCount, int nonPlayableNpcCount)
    {
        playableCharacterCount = Mathf.Max(0, playableCount);
        npcCount = Mathf.Max(0, nonPlayableNpcCount);
    }

    public void RefreshCountsFromRosterAsPlayable()
    {
        SetOwnedCharacterCounts(NpcRoster.Count, 0);
    }

    public void RemoveNpcReferences(string runtimeId)
    {
        if (string.IsNullOrWhiteSpace(runtimeId))
            return;
    }
}