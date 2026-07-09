using UnityEngine;

// UI를 여는 시설용 상호작용. PlayerInteractor는 IInteractable로만 호출하고,
// UIManager 의존은 이 컴포넌트가 대신 가진다(= UI 소통 창구는 여기서 연결).
public class FacilityUIInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private UIManager m_uiManager;

    private void Awake()
    {
        // 인스펙터에 지정하지 않았으면 씬에서 찾아 캐싱.
        if (m_uiManager == null)
            m_uiManager = FindFirstObjectByType<UIManager>();
    }

    public void Interact(GameObject interactor)
    {
        if (m_uiManager == null)
        {
            Debug.LogWarning("[FacilityUIInteractable] UIManager is not found.", this);
            return;
        }

        // 넘길 대상은 상호작용을 건 플레이어(interactor)가 아니라, 시설 본체(this).
        m_uiManager.TryOpenTargetUI(gameObject);
    }
}
