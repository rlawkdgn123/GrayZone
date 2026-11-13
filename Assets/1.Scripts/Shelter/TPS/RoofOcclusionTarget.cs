using UnityEngine;

/// <summary>
/// 하나의 지붕 구획을 구성하는 Renderer들을 묶고,
/// 카메라 가림 정도를 MaterialPropertyBlock으로 전달합니다.
/// </summary>
/// <remarks>
/// 전봇대처럼 가는 오브젝트는 Detection Mode를 <see cref="DetectionMode.BetweenCameraAndTarget"/>로 두어
/// 카메라와 캐릭터 사이 시선이 실제로 이 오브젝트를 지날 때만 페이드합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class RoofOcclusionTarget : MonoBehaviour
{
    /// <summary>가림 판정 방식입니다.</summary>
    public enum DetectionMode
    {
        /// <summary>감지 SphereCast에 닿기만 해도 가린 것으로 봅니다. 지붕용 기본값입니다.</summary>
        AnyHit,
        /// <summary>캐릭터가 이 오브젝트보다 뒤에 있고, 카메라-캐릭터 시선이 이 오브젝트를 지날 때만 가린 것으로 봅니다.</summary>
        BetweenCameraAndTarget,
    }

    private const string DefaultFadeProperty = "_OcclusionFade";

    [Header("Roof Visuals")]
    [Tooltip("이 지붕 구획과 함께 페이드할 Renderer들입니다. 비어 있으면 Awake에서 자식 Renderer를 자동 수집합니다.")]
    [SerializeField] private Renderer[] m_renderers = new Renderer[0];

    [Tooltip("Renderer 배열이 비어 있을 때 비활성 자식까지 포함해 자동 수집합니다.")]
    [SerializeField] private bool m_collectChildRenderersWhenEmpty = true;

    [Tooltip("가림 감지에는 쓰지 않고 페이드만 함께 적용할 Renderer들입니다. 철골 등 지붕 부속물에 사용합니다.")]
    [SerializeField] private Renderer[] m_followerRenderers = new Renderer[0];

    [Header("Shader")]
    [Tooltip("지붕 셰이더가 가림 정도(0=표시, 1=가림)를 받을 Float 프로퍼티 이름입니다.")]
    [SerializeField] private string m_fadeProperty = DefaultFadeProperty;

    [Header("Detection")]
    [Tooltip("AnyHit: 감지에 닿기만 해도 페이드합니다(지붕). " +
             "BetweenCameraAndTarget: 캐릭터가 이 오브젝트보다 뒤에 있고 카메라와 캐릭터 사이 시선이 이 오브젝트를 지날 때만 페이드합니다(전봇대 등).")]
    [SerializeField] private DetectionMode m_detectionMode = DetectionMode.AnyHit;

    [Tooltip("BetweenCameraAndTarget에서 캐릭터 폭의 절반(미터)입니다. 캐릭터 중심과 좌우 끝으로 시선을 검사합니다. " +
             "카메라 Fader의 Sphere Radius보다 크면 끝쪽 시선은 감지되지 않습니다.")]
    [SerializeField, Min(0f)] private float m_targetHalfWidth = 0.15f;

    [Tooltip("BetweenCameraAndTarget에서 캐릭터가 이 오브젝트보다 최소 이만큼(미터) 뒤에 있어야 가린 것으로 봅니다.")]
    [SerializeField, Min(0f)] private float m_minTargetGap = 0.2f;

    [Tooltip("BetweenCameraAndTarget에서 카메라로부터 이 거리(미터) 안에서 닿은 경우는 무시합니다. 카메라가 오브젝트 옆을 스칠 때의 오작동을 막습니다.")]
    [SerializeField, Min(0f)] private float m_ignoreNearCameraDistance = 0.3f;

    private MaterialPropertyBlock m_propertyBlock;
    private int m_fadePropertyId;
    private float m_currentFade;
    private bool m_initialized;

    public float CurrentFade => m_currentFade;

    private void Reset()
    {
        CollectChildRenderers();
    }

    private void Awake()
    {
        Initialize();
        ApplyFade(0f);
    }

    private void OnDisable()
    {
        ApplyFade(0f);
    }

    /// <summary>
    /// 0이면 완전히 표시하고 1이면 셰이더가 정의한 최대 가림 상태로 만듭니다.
    /// </summary>
    public void ApplyFade(float fade)
    {
        Initialize();

        m_currentFade = Mathf.Clamp01(fade);

        ApplyFadeToRenderers(m_renderers);
        ApplyFadeToRenderers(m_followerRenderers);
    }

    private void ApplyFadeToRenderers(Renderer[] renderers)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer targetRenderer = renderers[i];
            if (targetRenderer == null)
                continue;

            m_propertyBlock.Clear();
            targetRenderer.GetPropertyBlock(m_propertyBlock);
            m_propertyBlock.SetFloat(m_fadePropertyId, m_currentFade);
            targetRenderer.SetPropertyBlock(m_propertyBlock);
        }
    }

    public void RestoreImmediately()
    {
        ApplyFade(0f);
    }

    /// <summary>
    /// 감지된 Collider가 이 구획 자체이거나 등록된 지붕 Renderer 계층에 속하는지 확인합니다.
    /// 같은 그룹 아래의 조명, 기둥 등 다른 Collider로는 페이드하지 않기 위해 사용합니다.
    /// </summary>
    public bool OwnsCollider(Collider hitCollider)
    {
        if (hitCollider == null)
            return false;

        Initialize();

        Transform hitTransform = hitCollider.transform;
        if (hitTransform == transform)
            return true;

        for (int i = 0; i < m_renderers.Length; i++)
        {
            Renderer targetRenderer = m_renderers[i];
            if (targetRenderer != null && hitTransform.IsChildOf(targetRenderer.transform))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 감지된 Collider가 이 오브젝트의 판정 방식으로도 실제로 시야를 가리는지 확인합니다.
    /// </summary>
    public bool ConfirmOcclusion(Collider hitCollider, Vector3 cameraPosition, Vector3 targetPosition)
    {
        if (m_detectionMode == DetectionMode.AnyHit)
            return true;

        if (hitCollider == null)
            return false;

        Vector3 offset = targetPosition - cameraPosition;
        float distance = offset.magnitude;
        if (distance <= Mathf.Epsilon)
            return false;

        if (IsSightLineBlocked(hitCollider, cameraPosition, targetPosition))
            return true;

        if (m_targetHalfWidth <= 0f)
            return false;

        // 시선에 수직인 수평 방향으로 캐릭터 좌우 끝을 잡습니다.
        Vector3 side = Vector3.Cross(Vector3.up, offset / distance);
        if (side.sqrMagnitude < 1e-6f)
            return false;

        side = side.normalized * m_targetHalfWidth;
        return IsSightLineBlocked(hitCollider, cameraPosition, targetPosition + side) ||
               IsSightLineBlocked(hitCollider, cameraPosition, targetPosition - side);
    }

    /// <summary>
    /// 카메라에서 지점까지의 시선이 캐릭터 앞쪽(카메라 쪽)에서 이 Collider를 지나는지 검사합니다.
    /// </summary>
    private bool IsSightLineBlocked(Collider hitCollider, Vector3 from, Vector3 to)
    {
        Vector3 offset = to - from;
        float distance = offset.magnitude;
        float maxDistance = distance - m_minTargetGap;
        if (maxDistance <= m_ignoreNearCameraDistance)
            return false;

        if (!hitCollider.Raycast(new Ray(from, offset / distance), out RaycastHit hit, maxDistance))
            return false;

        return hit.distance >= m_ignoreNearCameraDistance;
    }

    [ContextMenu("Collect Child Renderers")]
    private void CollectChildRenderers()
    {
        m_renderers = GetComponentsInChildren<Renderer>(true);
    }

    private void Initialize()
    {
        if (m_initialized)
            return;

        if (m_collectChildRenderersWhenEmpty &&
            (m_renderers == null || m_renderers.Length == 0))
        {
            CollectChildRenderers();
        }

        if (m_renderers == null)
            m_renderers = new Renderer[0];

        if (m_followerRenderers == null)
            m_followerRenderers = new Renderer[0];

        if (string.IsNullOrWhiteSpace(m_fadeProperty))
            m_fadeProperty = DefaultFadeProperty;

        m_fadePropertyId = Shader.PropertyToID(m_fadeProperty);
        m_propertyBlock = new MaterialPropertyBlock();
        m_initialized = true;
    }
}
