using System;
using UnityEngine;

/// <summary>
/// 시설 오브젝트에서 UI 열기 요청을 <see cref="UIManager"/>로 전달하는 상호작용 컴포넌트
/// </summary>
public class FacilityUIInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private UIManager m_uiManager;

    private FacilityInteractionPoint m_facilityInteractionPoint;

    /// <summary>시설 UI 열기 요청이 정상적으로 전달된 뒤 발생합니다.</summary>
    public event Action<FacilityUIInteractable> Interacted;

    /// <summary>탭 상호작용이므로 홀드 시간이 없음</summary>
    public float HoldDuration => 0f;

    /// <summary>
    /// 이 컴포넌트가 활성 상태일 때만 상호작용을 허용
    /// </summary>
    /// <param name="interactor">상호작용을 시도한 오브젝트</param>
    /// <returns>상호작용 가능하면 <c>true</c></returns>
    public bool CanInteract(GameObject interactor)
    {
        return isActiveAndEnabled
            && (m_facilityInteractionPoint == null
                || m_facilityInteractionPoint.InteractionEnabled);
    }

    /// <summary>상호작용 프롬프트 텍스트</summary>
    public string GetPrompt() => "Facility";

    private void Awake()
    {
        m_facilityInteractionPoint = GetComponent<FacilityInteractionPoint>();

        // 인스펙터에 지정하지 않았으면 씬에서 찾아 캐싱.
        if (m_uiManager == null)
            m_uiManager = FindFirstObjectByType<UIManager>();
    }

    /// <summary>
    /// 현재 시설 오브젝트에 연결된 UI를 열도록 요청
    /// </summary>
    /// <param name="interactor">상호작용을 실행한 오브젝트</param>
    public void Interact(GameObject interactor)
    {
        if (!CanInteract(interactor))
            return;

        if (m_uiManager == null)
        {
            Debug.LogWarning("[FacilityUIInteractable] UIManager is not found.", this);
            return;
        }

        // 넘길 대상은 상호작용을 건 플레이어(interactor)가 아니라, 시설 본체(this).
        if (m_uiManager.TryOpenTargetUI(gameObject))
            Interacted?.Invoke(this);
    }
}
