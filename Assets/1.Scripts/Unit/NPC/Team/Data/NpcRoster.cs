using System.Collections.Generic;

public class NpcRoster
{
    private readonly List<NPCRuntimeData> npcs = new();
    private readonly Dictionary<string, NPCRuntimeData> byDefinitionId = new();

    public IReadOnlyList<NPCRuntimeData> All => npcs;
    public int Count => npcs.Count;

    public bool TryAdd(NPCChar npcData, out NPCRuntimeData runtimeData)
    {
        runtimeData = null;

        if (npcData == null)
        {
            return false;
        }

        NPCRuntimeData newRuntimeData = new NPCRuntimeData(npcData);
        if (!Add(newRuntimeData))
        {
            return false;
        }

        runtimeData = newRuntimeData;
        return true;
    }

    public bool Add(NPCRuntimeData runtimeData)
    {
        if (runtimeData == null || string.IsNullOrWhiteSpace(runtimeData.DefinitionId))
        {
            return false;
        }

        string definitionId = runtimeData.DefinitionId.Trim();
        if (byDefinitionId.ContainsKey(definitionId))
        {
            return false;
        }

        npcs.Add(runtimeData);
        byDefinitionId[definitionId] = runtimeData;
        return true;
    }

    public bool Remove(string definitionId)
    {
        if (!TryGet(definitionId, out NPCRuntimeData runtimeData))
        {
            return false;
        }

        byDefinitionId.Remove(runtimeData.DefinitionId.Trim());
        npcs.Remove(runtimeData);
        return true;
    }

    public bool Remove(NPCRuntimeData runtimeData)
    {
        if (runtimeData == null)
        {
            return false;
        }

        return Remove(runtimeData.DefinitionId);
    }

    public bool Contains(string definitionId)
    {
        return !string.IsNullOrWhiteSpace(definitionId) && byDefinitionId.ContainsKey(definitionId.Trim());
    }

    public bool TryGet(string definitionId, out NPCRuntimeData runtimeData)
    {
        runtimeData = null;

        if (string.IsNullOrWhiteSpace(definitionId))
        {
            return false;
        }

        return byDefinitionId.TryGetValue(definitionId.Trim(), out runtimeData);
    }

    public bool TryGetFirstByDefinitionId(string definitionId, out NPCRuntimeData runtimeData)
    {
        return TryGet(definitionId, out runtimeData);
    }

    public void Clear()
    {
        npcs.Clear();
        byDefinitionId.Clear();
    }
}