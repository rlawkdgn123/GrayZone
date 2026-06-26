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
    public const int CurrentSchemaVersion = 3;

    public int schemaVersion = CurrentSchemaVersion;
    public string profileId = "default";
    public SaveSlotType slotType = SaveSlotType.Manual;

    //저장 시간 데이터
    public long savedAtUnixTimeUtc;

    public SharedSaveData shared = new SharedSaveData();
    public ShelterSaveData shelter = new ShelterSaveData();

    public void MarkSavedNow()
    {
        //long + UTC + Unix time
        savedAtUnixTimeUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    [Serializable]
    public class SharedSaveData
    {
        public string lastStageId = string.Empty;
        public int shelterStability = 100;
        //ToDo : 추후 변경 필요
        public int playableCharacterCount;
        public int nonPlayableNpcCount;
        public List<ResourceAmountData> resources = new List<ResourceAmountData>();
        public List<NpcSaveData> npcs = new List<NpcSaveData>();
    }

    //셸터 데이터
    [Serializable]
    public class ShelterSaveData
    {
        public int currentDay = 1;
        public List<string> battleSquadNpcRuntimeIds = new List<string>();
        public List<FacilitySaveData> facilities = new List<FacilitySaveData>();
    }

    //자원 인벤토리에 대한 랩핑 클래스
    [Serializable]
    public class ResourceAmountData
    {
        public CurrencyType type;
        public int amount;
    }

    //플레이어블 캐릭터 데이터 랩핑 클래스
    [Serializable]
    public class NpcSaveData
    {
        public string runtimeId = string.Empty;
        public string definitionId = string.Empty;
        public NPCType type;
        public int maxHp = 1;
        public int currentHp = 1;
        public NPCInjuryState injuryState = NPCInjuryState.Healthy;
        public bool isAssignedToShelter;
        public string assignedRoomId = string.Empty;
    }

    //시설 정보에 대한 랩핑 클래스
    [Serializable]
    public class FacilitySaveData
    {
        public string facilityId = string.Empty;
        public bool isUnlocked;
        public int upgradeLevel;
    }
}
