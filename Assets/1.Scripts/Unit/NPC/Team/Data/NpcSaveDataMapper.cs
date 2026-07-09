using UnityEngine;

public static class NpcSaveDataMapper
{
    public static SaveData.NpcSaveData FromNpcChar(NPCChar npcChar)
    {
        if (npcChar == null)
        {
            return null;
        }

        string definitionId = ResolveDefinitionId(npcChar);
        int maxHp = Mathf.Max(1, npcChar.MaxHP);

        return new SaveData.NpcSaveData
        {
            definitionId = definitionId,
            type = npcChar.Type,
            maxHp = maxHp,
            currentHp = maxHp,
            injuryGauge = npcChar.InjuryGauge,
            maxInjuryGauge = npcChar.MaxInjuryGauge,
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
            definitionId = runtimeData.DefinitionId,
            type = runtimeData.Type,
            maxHp = runtimeData.MaxHp,
            currentHp = runtimeData.GetCurrentHp(),
            injuryGauge = runtimeData.InjuryGauge,
            maxInjuryGauge = runtimeData.MaxInjuryGauge,
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
            saveData.type,
            saveData.maxHp,
            saveData.currentHp,
            saveData.isAssignedToShelter,
            saveData.assignedRoomId,
            saveData.injuryGauge,
            saveData.maxInjuryGauge
            );
    }

    private static string ResolveDefinitionId(NPCChar npcChar)
    {
        if (!string.IsNullOrWhiteSpace(npcChar.DefinitionId))
        {
            return npcChar.DefinitionId.Trim();
        }

        return npcChar.name ?? string.Empty;
    }
}
