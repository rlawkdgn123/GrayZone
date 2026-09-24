using UnityEngine;

/// <summary>
/// 플레이어가 상호작용한 오브젝트에서 실제 시설 루트와 시설 타입을 찾는 연결 지점
/// </summary>
public class FacilityInteractionPoint : MonoBehaviour
{
    [Header("Facility")]
    [SerializeField] private FacilityInteractionType interactionType = FacilityInteractionType.None;
    [SerializeField] private GameObject facilityRoot;

    private bool interactionEnabled = true;

    /// <summary>이 지점이 연결된 시설 UI 타입</summary>
    public FacilityInteractionType InteractionType => interactionType;

    /// <summary>시설 컴포넌트를 탐색할 기준 루트 비어 있으면 현재 오브젝트를 사용</summary>
    public GameObject FacilityRoot => facilityRoot != null ? facilityRoot : gameObject;

    /// <summary>현재 이 시설 지점의 상호작용 허용 여부</summary>
    public bool InteractionEnabled => interactionEnabled;

    private void Reset()
    {
        facilityRoot = gameObject;
    }

    /// <summary>
    /// 진행 상태에 따라 이 시설 지점의 상호작용 허용 여부를 변경합니다.
    /// </summary>
    public void SetInteractionEnabled(bool enabled)
    {
        interactionEnabled = enabled;
    }

    /// <summary>
    /// 시설 루트 또는 부모 계층의 시설 컴포넌트 탐색
    /// </summary>
    /// <typeparam name="T">찾을 시설 컴포넌트 타입</typeparam>
    /// <param name="facility">찾은 시설 컴포넌트</param>
    /// <returns>컴포넌트를 찾았으면 <c>true</c></returns>
    public bool TryGetFacility<T>(out T facility) where T : Component
    {
        facility = null;

        GameObject root = FacilityRoot;
        if (root != null)
            facility = root.GetComponentInParent<T>();

        if (facility == null)
            facility = GetComponentInParent<T>();

        return facility != null;
    }
}
