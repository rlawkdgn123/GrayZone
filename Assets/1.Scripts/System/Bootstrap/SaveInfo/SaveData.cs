using System;
using System.Collections.Generic;

public enum SaveSlotType
{
    Auto,
    Manual
}

[Serializable]
public class SaveData
{
    public const int CurrentSchemaVersion = 4;

    public int schemaVersion = CurrentSchemaVersion;
    public string profileId = "default";
    public SaveSlotType slotType = SaveSlotType.Manual;

    public long savedAtUnixTimeUtc;

    public SharedSaveData shared = new SharedSaveData();
    public ShelterSaveData shelter = new ShelterSaveData();

    public void MarkSavedNow()
    {
        savedAtUnixTimeUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    [Serializable]
    public class SharedSaveData
    {
        public string lastStageId = string.Empty;
        public int shelterStability = 100;
        public int playableCharacterCount;
        public int nonPlayableNpcCount;
        public List<ResourceAmountData> resources = new List<ResourceAmountData>();
        public List<NpcSaveData> npcs = new List<NpcSaveData>();
    }

    [Serializable]
    public class ShelterSaveData
    {
        public int currentDay = 1;
        public List<string> battleSquadNpcDefinitionIds = new List<string>();
        public List<FacilitySaveData> facilities = new List<FacilitySaveData>();
    }

    [Serializable]
    public class ResourceAmountData
    {
        public CurrencyType type;
        public int amount;
    }

    [Serializable]
    public class NpcSaveData
    {
        public string definitionId = string.Empty;
        public NPCType type;
        public int maxHp = 1;
        public int currentHp = 1;
        public bool isAssignedToShelter;
        public string assignedRoomId = string.Empty;
        public float injuryGauge = 100;
        public float maxInjuryGauge = 100;
    }

    [Serializable]
    public class FacilitySaveData
    {
        public string facilityId = string.Empty;
        public bool isUnlocked;
        public int upgradeLevel;
    }
}
