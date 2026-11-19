using System;
using System.Collections.Generic;

[Serializable]
public class SaveData
{
    public const int CurrentSchemaVersion = 15;

    public int schemaVersion = CurrentSchemaVersion;
    public ShelterCheckpointId shelterCheckpointId = ShelterCheckpointId.BeforeDefense1;

    /// <summary>방어전 클리어 횟수입니다. 다음 방어전 회차(클리어 횟수 + 1)와 일차 표시의 기준입니다.</summary>
    public int defenseClearCount;

    /// <summary>새 게임을 시작할 때 고른 난이도입니다.</summary>
    /// <remarks>
    /// 이 값이 없던 저장 파일은 중간(0)으로 읽히므로 스키마 버전을 올리지 않았습니다. 버전을 올리면 기존 저장을 불러올 수 없게 됩니다.
    /// </remarks>
    public GameDifficulty difficulty = GameDifficulty.Normal;

    /// <summary>셸터 안내 진행 단계입니다.</summary>
    public ShelterFlowState shelterFlowState = ShelterFlowState.NotStarted;

    public int trapUpgradeLevel;
    public int spikeUpgradeLevel;
    public int explosiveUpgradeLevel;
    public int shooterUpgradeLevel;
    public int wireUpgradeLevel;

    /// <summary>일반 시설(FacilityManager 관리)의 해금·업그레이드 레벨입니다. 비어 있으면 각 시설 기본값으로 시작합니다.</summary>
    public List<FacilityRuntimeState> facilities = new List<FacilityRuntimeState>();

    public List<ResourceAmountData> resources = new List<ResourceAmountData>();
    public List<CharacterStateSaveData> characters = new List<CharacterStateSaveData>();

    /// <summary>셸터 창고의 제작 아이템(수류탄 등) 보유 수량입니다. 게임오버 복구 때 자원과 함께 되돌립니다.</summary>
    public List<ItemStorageEntry> items = new List<ItemStorageEntry>();

    /// <summary>진행 중인 제조 작업입니다. 복구 후 이전 판의 작업이 남아 자원 없이 완성되지 않도록 함께 저장합니다.</summary>
    public ManufacturingRuntimeData manufacturing = new ManufacturingRuntimeData();

    [Serializable]
    public class ResourceAmountData
    {
        public string resourceId = string.Empty;
        public int amount;
    }

    [Serializable]
    public class CharacterStateSaveData
    {
        public PlayerbleCharacterId characterId;
        public int currentHp;
        public int maxHp = 1;
        public float injurySeverityGauge;
        public float maxInjuryGauge = 100.0f;
        public CharacterInjuryState injuryState;
        public bool isDown;
        public bool isCombatOut;
    }
}
