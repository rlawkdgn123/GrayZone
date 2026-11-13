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

        [Tooltip("이 단계에서 화살표가 가리킬 월드 타깃")]
        [SerializeField] private Transform m_target;

        [Tooltip("이 단계에서 Onscreen Marker에 사용할 스프라이트. 비워 두면 기본 스프라이트를 사용함")]
        [SerializeField] private Sprite m_onscreenMarkerSprite;

        public void SetIndicatorVisible(bool visible)
        {
            if (m_indicator == null)
                return;

            if (visible)
            {
                if (m_target != null)
                    m_indicator.SetTarget(m_target);

                m_indicator.SetOnscreenMarkerSprite(m_onscreenMarkerSprite);

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

    [Header("Flow Sources")]
    [SerializeField] private ManufacturingManager m_manufacturingManager;

    [Tooltip("비워 두면 씬에서 자동으로 찾습니다. 환자를 한 명이라도 완치하면 의료 시설 안내를 끝냅니다.")]
    [SerializeField] private MedicalManager m_medicalManager;

    [Header("Startup")]
    [Tooltip("Scene 시작 시 제조 시설 안내 단계부터 자동으로 시작함")]
    [SerializeField] private bool m_startAutomatically = true;

    [Tooltip("선택 사항입니다. 연결하면 저장된 진행 단계가 없는 첫 진입에서만 컷신을 재생하고, 끝난 뒤 흐름을 시작합니다.")]
    [SerializeField] private ShelterIntroCutscene m_introCutscene;

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
        CacheManufacturingManager();
        CacheMedicalManager();
    }

    private void OnEnable()
    {
        ManufacturingManager manufacturingManager = CacheManufacturingManager();
        if (manufacturingManager != null)
            manufacturingManager.CraftCompleted += HandleCraftCompleted;

        MedicalManager medicalManager = CacheMedicalManager();
        if (medicalManager != null)
            medicalManager.OnPatientHealed += HandlePatientHealed;

        if (m_hasStarted)
            ApplyCurrentState();
    }

    private void OnDisable()
    {
        if (m_manufacturingManager != null)
            m_manufacturingManager.CraftCompleted -= HandleCraftCompleted;

        if (m_medicalManager != null)
            m_medicalManager.OnPatientHealed -= HandlePatientHealed;
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

        if (m_hasStarted)
            return;

        // 첫 진입: 컷신이 있으면 재생하고, 끝나거나 스킵되면 NotifyIntroCompleted로 흐름을 시작함
        if (m_introCutscene != null && m_introCutscene.HasClip)
        {
            ApplyCurrentState();
            m_introCutscene.Play(NotifyIntroCompleted);
            return;
        }

        if (m_startAutomatically)
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

    /// <summary>
    /// 첫 제작이 성공했음을 통지합니다.
    /// 제조 시설 도착 Trigger 통과 여부와 관계없이 물품을 하나라도 제작하면 출격 시설 안내로 넘어갑니다.
    /// </summary>
    public void NotifyFirstCraftCompleted()
    {
        if (!m_hasStarted
            || (m_currentState != ShelterFlowState.GuideToManufacturing
                && m_currentState != ShelterFlowState.WaitingForFirstCraft))
        {
            return;
        }

        EnterState(ShelterFlowState.GuideToOperation);
    }

    private void HandleCraftCompleted()
    {
        NotifyFirstCraftCompleted();
    }

    /// <summary>플레이어가 출격 시설 안내 Trigger에 도착했음을 통지합니다.</summary>
    public void NotifyOperationFacilityReached()
    {
        if (!m_hasStarted || m_currentState != ShelterFlowState.GuideToOperation)
            return;

        EnterState(ShelterFlowState.WaitingForFirstDefenseResult);
    }

    /// <summary>
    /// 출격으로 씬을 떠나기 직전에 통지합니다.
    /// 셸터 데이터 동기화 전에 호출해야 귀환 후 출격 시설을 다시 안내하지 않습니다.
    /// </summary>
    public void NotifySortieStarted()
    {
        if (!m_hasStarted || m_currentState != ShelterFlowState.GuideToOperation)
            return;

        EnterState(ShelterFlowState.WaitingForFirstDefenseResult);
    }

    /// <summary>씬에 있는 흐름 컨트롤러가 있으면 출격 시작을 통지합니다.</summary>
    public static void NotifySortieStartedInScene()
    {
        ShelterFlowController controller = FindFirstObjectByType<ShelterFlowController>();
        if (controller != null)
            controller.NotifySortieStarted();
    }

    /// <summary>첫 방어전 성공 결과가 반영된 뒤 셸터에 귀환했음을 통지합니다.</summary>
    public void NotifyFirstDefenseSucceededAndReturned()
    {
        m_hasStarted = true;
        EnterState(ShelterFlowState.GuideToMedical, true);
    }

    /// <summary>
    /// 의료 시설에서 환자가 한 명이라도 완치되었음을 통지합니다.
    /// 의료 시설 안내가 마지막 단계이므로 다른 시설을 다시 안내하지 않고 안내를 끝냅니다.
    /// </summary>
    public void NotifyFirstPatientHealed()
    {
        if (!m_hasStarted || m_currentState != ShelterFlowState.GuideToMedical)
            return;

        EnterState(ShelterFlowState.Completed);
    }

    private void HandlePatientHealed(ShelterMemberRuntimeData _)
    {
        NotifyFirstPatientHealed();
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
            // 첫 제작 완료 전까지는 제조 시설 도착 여부와 관계없이 제조 시설을 계속 안내함
            case ShelterFlowState.GuideToManufacturing:
            case ShelterFlowState.WaitingForFirstCraft:
                m_manufacturingObjective.SetIndicatorVisible(true);
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

    private ManufacturingManager CacheManufacturingManager()
    {
        if (m_manufacturingManager == null)
            m_manufacturingManager = FindFirstObjectByType<ManufacturingManager>();

        return m_manufacturingManager;
    }

    private MedicalManager CacheMedicalManager()
    {
        if (m_medicalManager == null)
            m_medicalManager = FindFirstObjectByType<MedicalManager>(FindObjectsInactive.Include);

        return m_medicalManager;
    }

    private static bool HasSuccessfulFieldResult()
    {
        FieldResultData result = GameDataManager.Instance?.CreateLastFieldResultSnapshot();
        return result != null && result.Outcome == FieldOutcome.Success;
    }
}
