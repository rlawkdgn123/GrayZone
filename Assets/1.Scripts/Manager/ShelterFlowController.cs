using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 셸터 목표의 현재 단계와 단계 전이만 관리하는 씬 흐름 컨트롤러입니다.
/// 시설 UI, 방어전, 목표 표시의 세부 동작은 각 시스템에 위임합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class ShelterFlowController : MonoBehaviour
{
    [Serializable]
    private sealed class ObjectiveTarget
    {
        [Tooltip("목표를 안내하는 화살표 컨트롤러")]
        [SerializeField] private ObjectiveIndicatorController m_indicator;

        public void SetIndicatorVisible(bool visible)
        {
            if (m_indicator == null)
                return;

            if (visible)
            {
                m_indicator.gameObject.SetActive(true);
                m_indicator.SetVisible(true);
                return;
            }

            m_indicator.SetVisible(false);
            m_indicator.gameObject.SetActive(false);
        }
    }

    [Header("Objective Steps")]
    [FormerlySerializedAs("m_firstFacilityObjective")]
    [SerializeField] private ObjectiveTarget m_manufacturingObjective = new();
    [FormerlySerializedAs("m_returnFacilityObjective")]
    [SerializeField] private ObjectiveTarget m_operationObjective = new();
    [SerializeField] private ObjectiveTarget m_medicalObjective = new();

    [Header("Startup")]
    [Tooltip("Scene 시작 시 제조 시설 안내 단계부터 자동으로 시작함")]
    [SerializeField] private bool m_startAutomatically = true;

    private bool m_hasStarted;
    private ShelterFlowState m_currentState;
    private ShelterSceneDataManager m_shelterDataManager;

    /// <summary>현재 셸터 목표 진행 단계입니다.</summary>
    public ShelterFlowState CurrentState => m_currentState;

    /// <summary>진행 단계가 변경되었을 때 발생합니다.</summary>
    public event Action<ShelterFlowState> StateChanged;

    private void Awake()
    {
        CacheShelterDataManager();
    }

    private void OnEnable()
    {
        if (m_hasStarted)
            ApplyCurrentState();
    }

    private void Start()
    {
        ShelterFlowState savedState = CacheShelterDataManager() != null
            ? m_shelterDataManager.FlowState
            : ShelterFlowState.NotStarted;

        if (savedState != ShelterFlowState.NotStarted)
        {
            m_hasStarted = true;
            if (savedState == ShelterFlowState.WaitingForFirstDefenseResult
                && HasSuccessfulFieldResult())
            {
                NotifyFirstDefenseSucceededAndReturned();
            }
            else
            {
                EnterState(savedState, true);
            }

            return;
        }

        if (m_startAutomatically && !m_hasStarted)
            StartFlow();
    }

    /// <summary>컷신이 없는 현재 흐름에서 제조 시설 안내 단계부터 시작하거나 초기화합니다.</summary>
    public void StartFlow()
    {
        m_hasStarted = true;
        EnterState(ShelterFlowState.GuideToManufacturing, true);
    }

    /// <summary>컷신 또는 첫 상호작용이 끝났음을 통지합니다.</summary>
    public void NotifyIntroCompleted()
    {
        if (m_hasStarted && m_currentState != ShelterFlowState.NotStarted)
            return;

        StartFlow();
    }

    /// <summary>플레이어가 제조 시설 안내 Trigger에 도착했음을 통지합니다.</summary>
    public void NotifyManufacturingFacilityReached()
    {
        if (!m_hasStarted || m_currentState != ShelterFlowState.GuideToManufacturing)
            return;

        EnterState(ShelterFlowState.WaitingForFirstCraft);
    }

    /// <summary>첫 제작이 성공했음을 통지합니다.</summary>
    public void NotifyFirstCraftCompleted()
    {
        if (!m_hasStarted || m_currentState != ShelterFlowState.WaitingForFirstCraft)
            return;

        EnterState(ShelterFlowState.GuideToOperation);
    }

    /// <summary>플레이어가 출격 시설 안내 Trigger에 도착했음을 통지합니다.</summary>
    public void NotifyOperationFacilityReached()
    {
        if (!m_hasStarted || m_currentState != ShelterFlowState.GuideToOperation)
            return;

        EnterState(ShelterFlowState.WaitingForFirstDefenseResult);
    }

    /// <summary>첫 방어전 성공 결과가 반영된 뒤 셸터에 귀환했음을 통지합니다.</summary>
    public void NotifyFirstDefenseSucceededAndReturned()
    {
        m_hasStarted = true;
        EnterState(ShelterFlowState.GuideToMedical, true);
    }

    /// <summary>플레이어가 의료 시설 안내 Trigger에 도착했음을 통지합니다.</summary>
    public void NotifyMedicalFacilityReached()
    {
        if (!m_hasStarted || m_currentState != ShelterFlowState.GuideToMedical)
            return;

        EnterState(ShelterFlowState.Completed);
    }

    private void EnterState(ShelterFlowState nextState, bool forceApply = false)
    {
        if (!forceApply && m_currentState == nextState)
            return;

        m_currentState = nextState;
        CacheShelterDataManager()?.SetFlowState(nextState);
        ApplyCurrentState();
        StateChanged?.Invoke(m_currentState);
    }

    private void ApplyCurrentState()
    {
        m_manufacturingObjective.SetIndicatorVisible(false);
        m_operationObjective.SetIndicatorVisible(false);
        m_medicalObjective.SetIndicatorVisible(false);

        switch (m_currentState)
        {
            case ShelterFlowState.GuideToManufacturing:
                m_manufacturingObjective.SetIndicatorVisible(true);
                break;

            case ShelterFlowState.WaitingForFirstCraft:
                break;

            case ShelterFlowState.GuideToOperation:
                m_operationObjective.SetIndicatorVisible(true);
                break;

            case ShelterFlowState.WaitingForFirstDefenseResult:
                break;

            case ShelterFlowState.GuideToMedical:
                m_medicalObjective.SetIndicatorVisible(true);
                break;

            case ShelterFlowState.NotStarted:
            case ShelterFlowState.Completed:
                break;
        }
    }

    private ShelterSceneDataManager CacheShelterDataManager()
    {
        if (m_shelterDataManager == null)
            m_shelterDataManager = ShelterSceneDataManager.Instance;

        if (m_shelterDataManager == null)
            m_shelterDataManager = FindFirstObjectByType<ShelterSceneDataManager>();

        return m_shelterDataManager;
    }

    private static bool HasSuccessfulFieldResult()
    {
        FieldResultData result = GameDataManager.Instance?.CreateLastFieldResultSnapshot();
        return result != null && result.Outcome == FieldOutcome.Success;
    }
}
