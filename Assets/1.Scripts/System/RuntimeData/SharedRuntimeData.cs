using System;
using UnityEngine;

[Serializable]
public class SharedRuntimeData
{
    [SerializeField] private string lastStageId = string.Empty;
    [SerializeField] private int shelterStability = 100;
    [SerializeField] private int playableCharacterCount;
    [SerializeField] private int npcCount;

    private ResourceStorage resources;
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

    public void RemoveNpcReferences(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return;
    }
}