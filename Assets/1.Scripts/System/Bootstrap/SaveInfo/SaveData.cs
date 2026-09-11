using System;
using System.Collections.Generic;

/// <summary>저장 파일이 오토세이브인지 수동 저장인지 구분합니다.</summary>
public enum SaveSlotType
{
    Auto,
    Manual
}

[Serializable]
public class SaveData
{
    public const int CurrentSchemaVersion = 11;

    public int schemaVersion = CurrentSchemaVersion;
    public string profileId = "default";
    public SaveSlotType slotType = SaveSlotType.Manual;

    public long savedAtUnixTimeUtc;

    public SharedSaveData shared = new SharedSaveData();
    public ShelterSaveData shelter = new ShelterSaveData();
    public FieldResultData lastFieldResult;
    /// <summary>schemaVersion 7 이하 저장 파일의 최근 결과 호환 필드입니다.</summary>
    public FieldResultData lastBattleResult;

    /// <summary>현재 UTC 시각을 저장 파일 생성 시각으로 기록합니다.</summary>
    public void MarkSavedNow()
    {
        savedAtUnixTimeUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    [Serializable]
    public class SharedSaveData
    {
        public string lastStageId = string.Empty;
        public int shelterStability = 100;
        public bool foodShortagePenaltyActive;
        public bool fuelShortagePenaltyActive;
        public int totalFieldKillCount;
        public List<int> fieldKillHistory = new List<int>();
        /// <summary>schemaVersion 6 이하 저장 파일을 읽기 위한 레거시 수량 필드입니다.</summary>
        public int playableCharacterCount;
        /// <summary>schemaVersion 6 이하 저장 파일을 읽기 위한 레거시 수량 필드입니다.</summary>
        public int nonPlayableNpcCount;
        public List<ResourceAmountData> resources = new List<ResourceAmountData>();
        /// <summary>schemaVersion 7 이상에서 사용하는 Field 공용 캐릭터 정본입니다.</summary>
        public List<CharacterSnapshotData> characters = new List<CharacterSnapshotData>();
        /// <summary>제조 시설의 전체 슬롯과 진행 중인 작업 스냅샷입니다.</summary>
        public ManufacturingFacilitySaveData manufacturing = new ManufacturingFacilitySaveData();
        /// <summary>schemaVersion 6 이하 저장 파일을 읽기 위한 레거시 NPC 목록입니다.</summary>
        public List<NpcSaveData> npcs = new List<NpcSaveData>();
    }

    [Serializable]
    public class ShelterSaveData
    {
        public int currentDay = 1;
        /// <summary>schemaVersion 7 이상에서 사용하는 출전 캐릭터 런타임 ID 목록입니다.</summary>
        public List<string> battleSquadRuntimeIds = new List<string>();
        /// <summary>schemaVersion 6 이하 정의 ID 기반 출전 목록입니다.</summary>
        public List<string> battleSquadNpcDefinitionIds = new List<string>();
        public List<CharacterAssignmentSaveData> characterAssignments = new List<CharacterAssignmentSaveData>();
        public List<FacilitySaveData> facilities = new List<FacilitySaveData>();
    }

    [Serializable]
    public class ResourceAmountData
    {
        /// <summary>저장과 런타임에서 사용하는 안정적인 자원 ID입니다.</summary>
        public string resourceId = string.Empty;
        public int amount;
    }

    [Serializable]
    public class NpcSaveData
    {
        /// <summary>캐릭터 기본 수치와 장착 총기 상태를 함께 보존하는 schemaVersion 5 이상 공용 스냅샷입니다.</summary>
        public CharacterSnapshotData characterSnapshot = new CharacterSnapshotData();

        // schemaVersion 4 이하 저장 파일을 불러오기 위한 호환 필드입니다.
        // injuryGauge는 최대값이 건강한 기존 회복 게이지 의미를 유지합니다.
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

    [Serializable]
    public class CharacterAssignmentSaveData
    {
        public string runtimeId = string.Empty;
        public string facilityId = string.Empty;
        public string roomId = string.Empty;
        public FacilityAssignmentKind kind;
    }
}
