/// <summary>씬 전환과 체크포인트 복구를 거쳐 유지되는 셸터 안내 진행 단계입니다.</summary>
public enum ShelterFlowState
{
    NotStarted = 0,
    GuideToManufacturing = 1,
    WaitingForFirstCraft = 2,
    GuideToOperation = 3,
    WaitingForFirstDefenseResult = 4,
    GuideToMedical = 5,
    Completed = 6,
}
