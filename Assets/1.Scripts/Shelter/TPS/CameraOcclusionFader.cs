using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 실제 출력 카메라와 캐릭터 가시성 타깃 사이를 검사하고,
/// 시야를 가리는 지붕 구획만 페이드합니다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[RequireComponent(typeof(CinemachineBrain))]
public sealed class CameraOcclusionFader : MonoBehaviour
{
    private sealed class FadeState
    {
        public float Amount;
    }

    [Header("References")]
    [SerializeField] private Camera m_outputCamera;
    [SerializeField] private CinemachineBrain m_brain;
    [Tooltip("카메라에서 가시성을 확보할 캐릭터의 머리 또는 가슴 타깃입니다.")]
    [SerializeField] private Transform m_visibilityTarget;

    [Header("Detection")]
    [Tooltip("감지용 지붕 Collider가 사용하는 LayerMask입니다.")]
    [SerializeField] private LayerMask m_occluderLayers;
    [SerializeField, Min(0.01f)] private float m_sphereRadius = 0.3f;
    [SerializeField, Min(4)] private int m_hitCapacity = 32;
    [Tooltip("Trigger 프록시 Collider를 사용할 수 있도록 기본값은 Collide입니다.")]
    [SerializeField] private QueryTriggerInteraction m_triggerInteraction = QueryTriggerInteraction.Collide;

    [Header("Fade")]
    [Tooltip("가려졌을 때 전달할 페이드값입니다. 0은 표시, 1은 최대 가림입니다.")]
    [SerializeField, Range(0f, 1f)] private float m_occludedFade = 1f;
    [SerializeField, Min(0.01f)] private float m_fadeDuration = 0.15f;
    [SerializeField, Min(0.01f)] private float m_restoreDuration = 0.25f;

    private readonly HashSet<RoofOcclusionTarget> m_currentTargets = new HashSet<RoofOcclusionTarget>();
    private readonly Dictionary<RoofOcclusionTarget, FadeState> m_fadeStates =
        new Dictionary<RoofOcclusionTarget, FadeState>();
    private readonly List<RoofOcclusionTarget> m_removalBuffer = new List<RoofOcclusionTarget>();

    private RaycastHit[] m_hitBuffer;

    public Transform VisibilityTarget => m_visibilityTarget;

    private void Reset()
    {
        m_outputCamera = GetComponent<Camera>();
        m_brain = GetComponent<CinemachineBrain>();

        int shelterRoofLayer = LayerMask.NameToLayer("ShelterRoof");
        if (shelterRoofLayer >= 0)
            m_occluderLayers = 1 << shelterRoofLayer;
    }

    private void Awake()
    {
        CacheReferences();
        RebuildHitBuffer();
    }

    private void OnEnable()
    {
        CacheReferences();
        RebuildHitBuffer();
        CinemachineCore.CameraUpdatedEvent.AddListener(OnCameraUpdated);
    }

    private void OnDisable()
    {
        CinemachineCore.CameraUpdatedEvent.RemoveListener(OnCameraUpdated);
        RestoreAllImmediately();
    }

    private void OnValidate()
    {
        m_sphereRadius = Mathf.Max(0.01f, m_sphereRadius);
        m_hitCapacity = Mathf.Max(4, m_hitCapacity);
        m_fadeDuration = Mathf.Max(0.01f, m_fadeDuration);
        m_restoreDuration = Mathf.Max(0.01f, m_restoreDuration);
    }

    public void SetVisibilityTarget(Transform target)
    {
        if (m_visibilityTarget == target)
            return;

        RestoreAllImmediately();
        m_visibilityTarget = target;
    }

    private void OnCameraUpdated(CinemachineBrain updatedBrain)
    {
        if (updatedBrain != m_brain)
            return;

        UpdateOcclusion(Time.deltaTime);
    }

    private void UpdateOcclusion(float deltaTime)
    {
        m_currentTargets.Clear();

        if (CanDetectOcclusion())
            CollectCurrentTargets();

        RegisterCurrentTargets();
        AdvanceFadeStates(deltaTime);
    }

    private bool CanDetectOcclusion()
    {
        return m_outputCamera != null &&
               m_visibilityTarget != null &&
               m_occluderLayers.value != 0;
    }

    private void CollectCurrentTargets()
    {
        Vector3 origin = m_outputCamera.transform.position;
        Vector3 offset = m_visibilityTarget.position - origin;
        float distance = offset.magnitude;

        if (distance <= Mathf.Epsilon)
            return;

        if (m_hitBuffer == null || m_hitBuffer.Length != m_hitCapacity)
            RebuildHitBuffer();

        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            m_sphereRadius,
            offset / distance,
            m_hitBuffer,
            distance,
            m_occluderLayers,
            m_triggerInteraction
        );

        Vector3 targetPosition = m_visibilityTarget.position;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = m_hitBuffer[i].collider;
            if (hitCollider == null)
                continue;

            RoofOcclusionTarget target =
                hitCollider.GetComponentInParent<RoofOcclusionTarget>();

            // SphereCast는 넓게 후보를 모으고, 최종 판정은 대상별 Detection Mode가 정합니다.
            if (target != null &&
                target.isActiveAndEnabled &&
                target.OwnsCollider(hitCollider) &&
                target.ConfirmOcclusion(hitCollider, origin, targetPosition))
            {
                m_currentTargets.Add(target);
            }
        }
    }

    private void RegisterCurrentTargets()
    {
        foreach (RoofOcclusionTarget target in m_currentTargets)
        {
            if (target == null || m_fadeStates.ContainsKey(target))
                continue;

            m_fadeStates.Add(
                target,
                new FadeState
                {
                    Amount = target.CurrentFade
                }
            );
        }
    }

    private void AdvanceFadeStates(float deltaTime)
    {
        m_removalBuffer.Clear();

        foreach (KeyValuePair<RoofOcclusionTarget, FadeState> pair in m_fadeStates)
        {
            RoofOcclusionTarget target = pair.Key;
            FadeState state = pair.Value;

            if (target == null)
            {
                m_removalBuffer.Add(target);
                continue;
            }

            bool isOccluded = m_currentTargets.Contains(target);
            float desiredAmount = isOccluded ? m_occludedFade : 0f;
            float duration = desiredAmount > state.Amount
                ? m_fadeDuration
                : m_restoreDuration;

            state.Amount = Mathf.MoveTowards(
                state.Amount,
                desiredAmount,
                deltaTime / duration
            );

            target.ApplyFade(state.Amount);

            if (!isOccluded && state.Amount <= Mathf.Epsilon)
                m_removalBuffer.Add(target);
        }

        for (int i = 0; i < m_removalBuffer.Count; i++)
            m_fadeStates.Remove(m_removalBuffer[i]);
    }

    private void RestoreAllImmediately()
    {
        foreach (KeyValuePair<RoofOcclusionTarget, FadeState> pair in m_fadeStates)
        {
            if (pair.Key != null)
                pair.Key.RestoreImmediately();
        }

        m_currentTargets.Clear();
        m_fadeStates.Clear();
        m_removalBuffer.Clear();
    }

    private void CacheReferences()
    {
        if (m_outputCamera == null)
            m_outputCamera = GetComponent<Camera>();

        if (m_brain == null)
            m_brain = GetComponent<CinemachineBrain>();
    }

    private void RebuildHitBuffer()
    {
        int capacity = Mathf.Max(4, m_hitCapacity);
        if (m_hitBuffer == null || m_hitBuffer.Length != capacity)
            m_hitBuffer = new RaycastHit[capacity];
    }
}
