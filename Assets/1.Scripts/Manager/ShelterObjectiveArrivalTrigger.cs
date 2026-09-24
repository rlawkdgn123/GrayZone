using UnityEngine;

/// <summary>
/// 플레이어가 목표 시설의 도착 영역에 진입했음을 셸터 흐름 컨트롤러에 전달합니다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class ShelterObjectiveArrivalTrigger : MonoBehaviour
{
    public enum ObjectiveStep
    {
        ManufacturingFacility = 0,
        OperationFacility = 1,
        MedicalFacility = 2,
    }

    [SerializeField] private ShelterFlowController m_flowController;
    [SerializeField] private ObjectiveStep m_objectiveStep;

    private bool m_consumed;

    private void Reset()
    {
        m_flowController = FindFirstObjectByType<ShelterFlowController>();

        Collider triggerCollider = GetComponent<Collider>();
        if (triggerCollider != null)
            triggerCollider.isTrigger = true;
    }

    private void Awake()
    {
        if (m_flowController == null)
            m_flowController = FindFirstObjectByType<ShelterFlowController>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (m_consumed
            || m_flowController == null
            || other.GetComponentInParent<PlayerMove>() == null)
        {
            return;
        }

        switch (m_objectiveStep)
        {
            case ObjectiveStep.ManufacturingFacility:
                if (m_flowController.CurrentState
                    != ShelterFlowState.GuideToManufacturing)
                {
                    return;
                }

                m_flowController.NotifyManufacturingFacilityReached();
                break;

            case ObjectiveStep.OperationFacility:
                if (m_flowController.CurrentState
                    != ShelterFlowState.GuideToOperation)
                {
                    return;
                }

                m_flowController.NotifyOperationFacilityReached();
                break;

            case ObjectiveStep.MedicalFacility:
                if (m_flowController.CurrentState
                    != ShelterFlowState.GuideToMedical)
                {
                    return;
                }

                m_flowController.NotifyMedicalFacilityReached();
                break;
        }

        m_consumed = true;
    }
}
