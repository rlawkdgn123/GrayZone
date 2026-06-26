using UnityEngine;

public static class NpcSaveDataMapper
{
    public static SaveData.NpcSaveData FromNpcChar(NPCChar npcChar, string runtimeId = null)
    {
        if (npcChar == null)
        {
            return null;
        }

        string definitionId = ResolveDefinitionId(npcChar);
        int maxHp = Mathf.Max(1, npcChar.MaxHP);

        return new SaveData.NpcSaveData
        {
            runtimeId = ResolveRuntimeId(runtimeId, definitionId),
            definitionId = definitionId,
            type = npcChar.Type,
            maxHp = maxHp,
            currentHp = maxHp,
            injuryState = NPCInjuryState.Healthy,
            isAssignedToShelter = false,
            assignedRoomId = string.Empty
        };
    }

    public static SaveData.NpcSaveData FromRuntime(NPCRuntimeData runtimeData)
    {
        if (runtimeData == null)
        {
            return null;
        }

        return new SaveData.NpcSaveData
        {
            runtimeId = runtimeData.RuntimeId,
            definitionId = runtimeData.DefinitionId,
            type = runtimeData.Type,
            maxHp = runtimeData.MaxHp,
            currentHp = runtimeData.GetCurrentHp(),
            injuryState = runtimeData.GetCurrentInjuryState(),
            isAssignedToShelter = runtimeData.GetIsAssignedToShelter(),
            assignedRoomId = runtimeData.GetAssignedRoomId()
        };
    }

    public static NPCRuntimeData ToRuntime(SaveData.NpcSaveData saveData)
    {
        if (saveData == null)
        {
            return null;
        }

        return new NPCRuntimeData(
            saveData.definitionId,
            saveData.runtimeId,
            saveData.type,
            saveData.maxHp,
            saveData.currentHp,
            saveData.injuryState,
            saveData.isAssignedToShelter,
            saveData.assignedRoomId);
    }

    private static string ResolveDefinitionId(NPCChar npcChar)
    {
        if (!string.IsNullOrWhiteSpace(npcChar.DefinitionId))
        {
            return npcChar.DefinitionId.Trim();
        }

        return npcChar.name ?? string.Empty;
    }

    private static string ResolveRuntimeId(string runtimeId, string definitionId)
    {
        if (!string.IsNullOrWhiteSpace(runtimeId))
        {
            return runtimeId.Trim();
        }

        return definitionId ?? string.Empty;
    }
}
