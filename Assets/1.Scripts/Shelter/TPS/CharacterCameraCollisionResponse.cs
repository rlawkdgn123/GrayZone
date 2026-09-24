using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Cinemachine Third Person Follow가 벽 때문에 압축될 때만 카메라 높이를 보정하고,
/// 현재 캐릭터의 Renderer를 거리 기반으로 디더 처리합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class CharacterCameraCollisionResponse : MonoBehaviour
{
    private const string NearFadeKeyword = "N_F_NFD_ON";
    private const string ToonClippingKeyword = "_IS_CLIPPING_MODE";
    private const string ToonClippingOffKeyword = "_IS_CLIPPING_OFF";
    private const string ToonTransparentClippingKeyword = "_IS_CLIPPING_TRANSMODE";

    private static readonly int NearFadeEnabledId = Shader.PropertyToID("_N_F_NFD");
    private static readonly int MinFadeDistanceId = Shader.PropertyToID("_MinFadDistance");
    private static readonly int MaxFadeDistanceId = Shader.PropertyToID("_MaxFadDistance");
    private static readonly int ToonClippingMaskId = Shader.PropertyToID("_ClippingMask");
    private static readonly int ToonClippingLevelId = Shader.PropertyToID("_Clipping_Level");
    private static readonly int ToonClippingModeId = Shader.PropertyToID("_ClippingMode");
    private static readonly int ToonInverseClippingId = Shader.PropertyToID("_Inverse_Clipping");
    private static readonly int ToonBaseAlphaClippingId =
        Shader.PropertyToID("_IsBaseMapAlphaAsClippingMask");

    private sealed class CameraRigState
    {
        public CinemachineThirdPersonFollow Rig;
        public Vector3 BaseShoulderOffset;
    }

    private sealed class RendererState
    {
        public SkinnedMeshRenderer Renderer;
        public Material[] OriginalMaterials;
        public Material[] RuntimeMaterials;
        public MaterialPropertyBlock OriginalPropertyBlock;
    }

    [Header("References")]
    [SerializeField] private Camera m_outputCamera;
    [SerializeField] private CinemachineBrain m_brain;
    [SerializeField] private CinemachineThirdPersonFollow[] m_cameraRigs =
        new CinemachineThirdPersonFollow[0];
    [Tooltip("디더를 적용할 캐릭터 시각 계층입니다.")]
    [SerializeField] private Transform m_renderRoot;

    [Header("Collision Detection")]
    [Tooltip("기본 Camera Distance보다 이 값 이상 짧아졌을 때 충돌 압축 상태로 판단합니다.")]
    [SerializeField, Min(0.001f)] private float m_compressionThreshold = 0.05f;
    [Tooltip("Collider 접촉이 잠깐 끊겨도 높이와 디더가 튀지 않도록 충돌 상태를 유지하는 시간입니다.")]
    [SerializeField, Min(0f)] private float m_collisionReleaseDelay = 0.2f;

    [Header("Collision Camera Blend")]
    [Tooltip("벽 충돌 중 Shoulder Offset Y에 더할 값입니다. 음수면 카메라가 몸통 쪽으로 내려갑니다.")]
    [SerializeField] private float m_collisionHeightOffset = -0.45f;
    [Tooltip("벽 충돌 중 사용할 Shoulder Offset X입니다. 0이면 캐릭터 중앙입니다.")]
    [SerializeField] private float m_collisionShoulderOffsetX = 0f;
    [Tooltip("충돌 카메라 위치로 부드럽게 진입하는 시간입니다.")]
    [SerializeField, Min(0.01f)] private float m_heightBlendInDuration = 0.25f;
    [Tooltip("기본 카메라 위치로 부드럽게 복귀하는 시간입니다.")]
    [SerializeField, Min(0.01f)] private float m_heightBlendOutDuration = 0.4f;

    [Header("Collision Camera Safety")]
    [Tooltip("충돌 중 카메라와 캐릭터 중심 사이에 확보할 최소 거리입니다.")]
    [SerializeField, Min(0f)] private float m_minimumCameraClearance = 0.6f;
    [Tooltip("충돌 중 근접한 캐릭터가 Near Clip Plane에 잘리지 않도록 사용할 값입니다.")]
    [SerializeField, Min(0.01f)] private float m_collisionNearClipPlane = 0.1f;

    [Header("Character Dither")]
    [Tooltip("카메라가 가장 가까울 때 화면에 남길 디더 픽셀 비율입니다.")]
    [SerializeField, Range(0f, 1f)] private float m_minimumVisibility = 0.2f;
    [Tooltip("이 거리 이하에서는 Minimum Visibility를 적용합니다.")]
    [SerializeField, Min(0f)] private float m_fullDitherDistance = 0.6f;
    [Tooltip("이 거리 이상에서는 충돌 중이어도 캐릭터를 완전히 표시합니다.")]
    [SerializeField, Min(0.01f)] private float m_ditherStartDistance = 1.8f;
    [SerializeField, Min(0.01f)] private float m_ditherBlendInDuration = 0.1f;
    [SerializeField, Min(0.01f)] private float m_ditherBlendOutDuration = 0.15f;

    private readonly List<CameraRigState> m_rigStates = new List<CameraRigState>();
    private readonly List<RendererState> m_rendererStates = new List<RendererState>();
    private readonly List<Material> m_runtimeMaterials = new List<Material>();
    private readonly RaycastHit[] m_cameraClearanceHits = new RaycastHit[16];

    private MaterialPropertyBlock m_propertyBlock;
    private Texture2D m_ditherMask;
    private CharacterController m_characterController;
    private float m_currentHeightOffset;
    private float m_collisionBlend;
    private float m_collisionBlendVelocity;
    private float m_currentVisibility = 1f;
    private float m_lastCollisionTime = float.NegativeInfinity;
    private float m_baseNearClipPlane;
    private bool m_hasBaseNearClipPlane;
    private bool m_runtimeInitialized;

    public bool IsCollisionResponseActive { get; private set; }
    public float CurrentHeightOffset => m_currentHeightOffset;
    public float CurrentVisibility => m_currentVisibility;
    public int ManagedRendererCount => m_rendererStates.Count;

    private void Reset()
    {
        m_outputCamera = Camera.main;
        if (m_outputCamera != null)
            m_brain = m_outputCamera.GetComponent<CinemachineBrain>();

        m_cameraRigs = FindObjectsByType<CinemachineThirdPersonFollow>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        m_renderRoot = transform;
    }

    private void Awake()
    {
        if (!Application.isPlaying)
            return;

        InitializeRuntime();
    }

    private void OnEnable()
    {
        if (!Application.isPlaying)
            return;

        InitializeRuntime();
        AssignRuntimeMaterials();
        ApplyVisibility(m_currentVisibility);
        CinemachineCore.CameraUpdatedEvent.AddListener(OnCameraUpdated);
    }

    private void OnDisable()
    {
        CinemachineCore.CameraUpdatedEvent.RemoveListener(OnCameraUpdated);

        if (!m_runtimeInitialized)
            return;

        RestoreCameraRigs();
        RestoreCameraNearClipPlane();
        RestoreOriginalRendererState();
        m_currentHeightOffset = 0f;
        m_collisionBlend = 0f;
        m_collisionBlendVelocity = 0f;
        m_currentVisibility = 1f;
        m_lastCollisionTime = float.NegativeInfinity;
        IsCollisionResponseActive = false;
    }

    private void OnDestroy()
    {
        DestroyRuntimeMaterials();
    }

    private void OnValidate()
    {
        m_compressionThreshold = Mathf.Max(0.001f, m_compressionThreshold);
        m_collisionReleaseDelay = Mathf.Max(0f, m_collisionReleaseDelay);
        m_heightBlendInDuration = Mathf.Max(0.01f, m_heightBlendInDuration);
        m_heightBlendOutDuration = Mathf.Max(0.01f, m_heightBlendOutDuration);
        m_minimumCameraClearance = Mathf.Max(0f, m_minimumCameraClearance);
        m_collisionNearClipPlane = Mathf.Max(0.01f, m_collisionNearClipPlane);
        m_minimumVisibility = Mathf.Clamp01(m_minimumVisibility);
        m_fullDitherDistance = Mathf.Max(0f, m_fullDitherDistance);
        m_ditherStartDistance = Mathf.Max(m_fullDitherDistance + 0.01f, m_ditherStartDistance);
        m_ditherBlendInDuration = Mathf.Max(0.01f, m_ditherBlendInDuration);
        m_ditherBlendOutDuration = Mathf.Max(0.01f, m_ditherBlendOutDuration);
    }

    private void OnCameraUpdated(CinemachineBrain updatedBrain)
    {
        if (updatedBrain != m_brain || !m_runtimeInitialized)
            return;

        CinemachineThirdPersonFollow activeRig = FindActiveRig();
        bool followsThisCharacter = activeRig != null && FollowsThisCharacter(activeRig);
        bool collisionDetected = followsThisCharacter && IsCollisionCompressed(activeRig);

        if (!followsThisCharacter)
            m_lastCollisionTime = float.NegativeInfinity;

        if (collisionDetected)
            m_lastCollisionTime = Time.time;

        bool collisionActive = collisionDetected ||
                               Time.time - m_lastCollisionTime <= m_collisionReleaseDelay;
        IsCollisionResponseActive = collisionActive;

        float deltaTime = Time.deltaTime;
        UpdateCameraOffset(collisionActive, deltaTime);
        ApplyCameraOffset();
        ApplyCameraSafety(activeRig, collisionActive);

        float desiredVisibility = collisionActive
            ? CalculateCollisionVisibility()
            : 1f;

        UpdateVisibility(desiredVisibility, deltaTime);
        ApplyVisibility(m_currentVisibility);
    }

    private void InitializeRuntime()
    {
        if (m_runtimeInitialized)
            return;

        CacheReferences();
        CacheCameraNearClipPlane();
        CacheCameraRigs();
        CreateDitherMask();
        CacheRendererMaterials();
        m_propertyBlock = new MaterialPropertyBlock();
        m_runtimeInitialized = true;
    }

    private void CacheReferences()
    {
        if (m_outputCamera == null)
            m_outputCamera = Camera.main;

        if (m_brain == null && m_outputCamera != null)
            m_brain = m_outputCamera.GetComponent<CinemachineBrain>();

        if (m_renderRoot == null)
            m_renderRoot = transform;

        m_characterController = GetComponent<CharacterController>();

        if (m_cameraRigs == null || m_cameraRigs.Length == 0)
        {
            m_cameraRigs = FindObjectsByType<CinemachineThirdPersonFollow>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );
        }
    }

    private void CacheCameraRigs()
    {
        m_rigStates.Clear();

        if (m_cameraRigs == null)
            return;

        for (int i = 0; i < m_cameraRigs.Length; i++)
        {
            CinemachineThirdPersonFollow rig = m_cameraRigs[i];
            if (rig == null)
                continue;

            m_rigStates.Add(new CameraRigState
            {
                Rig = rig,
                BaseShoulderOffset = rig.ShoulderOffset
            });
        }
    }

    private void CacheCameraNearClipPlane()
    {
        if (m_outputCamera == null || m_hasBaseNearClipPlane)
            return;

        m_baseNearClipPlane = m_outputCamera.nearClipPlane;
        m_hasBaseNearClipPlane = true;
    }

    private void CacheRendererMaterials()
    {
        m_rendererStates.Clear();

        if (m_renderRoot == null)
            return;

        SkinnedMeshRenderer[] renderers =
            m_renderRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Dictionary<Material, Material> materialCopies = new Dictionary<Material, Material>();

        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer targetRenderer = renderers[i];
            Material[] originalMaterials = targetRenderer.sharedMaterials;
            Material[] runtimeMaterials = new Material[originalMaterials.Length];
            MaterialPropertyBlock originalPropertyBlock = new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(originalPropertyBlock);

            for (int materialIndex = 0; materialIndex < originalMaterials.Length; materialIndex++)
            {
                Material source = originalMaterials[materialIndex];
                runtimeMaterials[materialIndex] = GetOrCreateRuntimeMaterial(source, materialCopies);
            }

            m_rendererStates.Add(new RendererState
            {
                Renderer = targetRenderer,
                OriginalMaterials = originalMaterials,
                RuntimeMaterials = runtimeMaterials,
                OriginalPropertyBlock = originalPropertyBlock
            });
        }
    }

    private Material GetOrCreateRuntimeMaterial(
        Material source,
        Dictionary<Material, Material> materialCopies)
    {
        if (source == null)
            return source;

        bool supportsNearFade = source.HasProperty(NearFadeEnabledId);
        bool supportsToonClipping = source.HasProperty(ToonClippingMaskId) &&
                                    source.HasProperty(ToonClippingLevelId);
        if (!supportsNearFade && !supportsToonClipping)
            return source;

        if (materialCopies.TryGetValue(source, out Material existingCopy))
            return existingCopy;

        Material runtimeMaterial = new Material(source)
        {
            name = source.name + " (Runtime Character Dither)"
        };

        if (supportsNearFade)
        {
            runtimeMaterial.SetFloat(NearFadeEnabledId, 1f);
            runtimeMaterial.EnableKeyword(NearFadeKeyword);
        }

        if (supportsToonClipping)
        {
            runtimeMaterial.DisableKeyword(ToonClippingOffKeyword);
            runtimeMaterial.DisableKeyword(ToonTransparentClippingKeyword);
            runtimeMaterial.EnableKeyword(ToonClippingKeyword);
            runtimeMaterial.SetFloat(ToonClippingModeId, 1f);
            runtimeMaterial.SetFloat(ToonInverseClippingId, 0f);
            runtimeMaterial.SetFloat(ToonBaseAlphaClippingId, 0f);
            runtimeMaterial.SetTexture(ToonClippingMaskId, m_ditherMask);
            runtimeMaterial.SetFloat(ToonClippingLevelId, 0.5f);
        }

        materialCopies.Add(source, runtimeMaterial);
        m_runtimeMaterials.Add(runtimeMaterial);
        return runtimeMaterial;
    }

    private void CreateDitherMask()
    {
        if (m_ditherMask != null)
            return;

        int[] bayer8x8 =
        {
             0, 48, 12, 60,  3, 51, 15, 63,
            32, 16, 44, 28, 35, 19, 47, 31,
             8, 56,  4, 52, 11, 59,  7, 55,
            40, 24, 36, 20, 43, 27, 39, 23,
             2, 50, 14, 62,  1, 49, 13, 61,
            34, 18, 46, 30, 33, 17, 45, 29,
            10, 58,  6, 54,  9, 57,  5, 53,
            42, 26, 38, 22, 41, 25, 37, 21
        };

        const int textureSize = 256;
        Color32[] pixels = new Color32[textureSize * textureSize];
        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                int rank = bayer8x8[(y % 8) * 8 + x % 8];
                byte value = (byte)Mathf.RoundToInt((rank + 0.5f) / 64f * 255f);
                pixels[y * textureSize + x] = new Color32(value, value, value, 255);
            }
        }

        m_ditherMask = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
        {
            name = "Runtime Character Bayer Dither",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
            hideFlags = HideFlags.DontSave
        };
        m_ditherMask.SetPixels32(pixels);
        m_ditherMask.Apply(false, true);
    }

    private CinemachineThirdPersonFollow FindActiveRig()
    {
        if (m_brain == null)
            return null;

        CinemachineThirdPersonFollow bestRig = null;
        int bestPriority = int.MinValue;

        for (int i = 0; i < m_rigStates.Count; i++)
        {
            CinemachineThirdPersonFollow rig = m_rigStates[i].Rig;
            if (rig == null || !rig.isActiveAndEnabled ||
                !m_brain.IsLiveChild(rig.VirtualCamera))
            {
                continue;
            }

            int priority = rig.VirtualCamera.Priority.Value;
            if (bestRig == null || priority > bestPriority)
            {
                bestRig = rig;
                bestPriority = priority;
            }
        }

        return bestRig;
    }

    private bool FollowsThisCharacter(CinemachineThirdPersonFollow rig)
    {
        Transform followTarget = rig.FollowTarget;
        return followTarget != null &&
               (followTarget == transform || followTarget.IsChildOf(transform));
    }

    private bool IsCollisionCompressed(CinemachineThirdPersonFollow rig)
    {
        if (rig.CurrentObstacle != null)
            return true;

        rig.GetRigPositions(out _, out _, out Vector3 hand);
        float actualDistance = Vector3.Distance(hand, rig.VirtualCamera.State.RawPosition);
        return actualDistance < rig.CameraDistance - m_compressionThreshold;
    }

    private void UpdateCameraOffset(bool collisionActive, float deltaTime)
    {
        float targetBlend = collisionActive ? 1f : 0f;
        float duration = collisionActive
            ? m_heightBlendInDuration
            : m_heightBlendOutDuration;

        m_collisionBlend = Mathf.SmoothDamp(
            m_collisionBlend,
            targetBlend,
            ref m_collisionBlendVelocity,
            duration,
            Mathf.Infinity,
            deltaTime
        );

        if (Mathf.Abs(m_collisionBlend - targetBlend) < 0.001f)
        {
            m_collisionBlend = targetBlend;
            m_collisionBlendVelocity = 0f;
        }

        m_currentHeightOffset = m_collisionHeightOffset * m_collisionBlend;
    }

    private void ApplyCameraOffset()
    {
        for (int i = 0; i < m_rigStates.Count; i++)
        {
            CameraRigState state = m_rigStates[i];
            if (state.Rig == null)
                continue;

            Vector3 shoulderOffset = state.BaseShoulderOffset;
            shoulderOffset.x = Mathf.Lerp(
                state.BaseShoulderOffset.x,
                m_collisionShoulderOffsetX,
                m_collisionBlend
            );
            shoulderOffset.y += m_currentHeightOffset;
            state.Rig.ShoulderOffset = shoulderOffset;
        }
    }

    private void ApplyCameraSafety(
        CinemachineThirdPersonFollow activeRig,
        bool collisionActive)
    {
        UpdateCameraNearClipPlane(collisionActive);

        if (!collisionActive || activeRig == null || m_outputCamera == null ||
            m_minimumCameraClearance <= 0f)
        {
            return;
        }

        Vector3 characterCenter = m_characterController != null
            ? m_characterController.bounds.center
            : transform.position;
        Vector3 cameraOffset = m_outputCamera.transform.position - characterCenter;
        float currentDistance = cameraOffset.magnitude;
        if (currentDistance >= m_minimumCameraClearance)
            return;

        Vector3 direction = currentDistance > 0.001f
            ? cameraOffset / currentDistance
            : -m_outputCamera.transform.forward;
        float availableDistance = FindAvailableCameraDistance(
            activeRig,
            characterCenter,
            direction
        );

        if (availableDistance <= currentDistance)
            return;

        m_outputCamera.transform.position =
            characterCenter + direction * availableDistance;
    }

    private float FindAvailableCameraDistance(
        CinemachineThirdPersonFollow activeRig,
        Vector3 origin,
        Vector3 direction)
    {
        CinemachineThirdPersonFollow.ObstacleSettings obstacleSettings =
            activeRig.AvoidObstacles;
        int hitCount = Physics.RaycastNonAlloc(
            origin,
            direction,
            m_cameraClearanceHits,
            m_minimumCameraClearance,
            obstacleSettings.CollisionFilter,
            QueryTriggerInteraction.Ignore
        );
        float availableDistance = m_minimumCameraClearance;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = m_cameraClearanceHits[i];
            if (hit.collider == null ||
                hit.collider.transform == transform ||
                hit.collider.transform.IsChildOf(transform))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(obstacleSettings.IgnoreTag) &&
                hit.collider.CompareTag(obstacleSettings.IgnoreTag))
            {
                continue;
            }

            float safeDistance = Mathf.Max(
                0f,
                hit.distance - obstacleSettings.CameraRadius
            );
            availableDistance = Mathf.Min(availableDistance, safeDistance);
        }

        return availableDistance;
    }

    private void UpdateCameraNearClipPlane(bool collisionActive)
    {
        if (m_outputCamera == null || !m_hasBaseNearClipPlane)
            return;

        m_outputCamera.nearClipPlane = collisionActive
            ? Mathf.Min(m_baseNearClipPlane, m_collisionNearClipPlane)
            : m_baseNearClipPlane;
    }

    private void RestoreCameraNearClipPlane()
    {
        if (m_outputCamera != null && m_hasBaseNearClipPlane)
            m_outputCamera.nearClipPlane = m_baseNearClipPlane;
    }

    private float CalculateCollisionVisibility()
    {
        if (m_outputCamera == null)
            return 1f;

        Vector3 characterCenter = m_characterController != null
            ? m_characterController.bounds.center
            : (m_renderRoot != null ? m_renderRoot.position : transform.position);
        float distance = Vector3.Distance(m_outputCamera.transform.position, characterCenter);
        float distanceRatio = Mathf.InverseLerp(
            m_fullDitherDistance,
            m_ditherStartDistance,
            distance
        );

        return Mathf.Lerp(m_minimumVisibility, 1f, distanceRatio);
    }

    private void UpdateVisibility(float desiredVisibility, float deltaTime)
    {
        float duration = desiredVisibility < m_currentVisibility
            ? m_ditherBlendInDuration
            : m_ditherBlendOutDuration;

        m_currentVisibility = Mathf.MoveTowards(
            m_currentVisibility,
            desiredVisibility,
            deltaTime / duration
        );
    }

    private void ApplyVisibility(float visibility)
    {
        if (m_outputCamera == null || m_propertyBlock == null)
            return;

        float clampedVisibility = Mathf.Clamp01(visibility);
        const float fadeRange = 1f;
        const float fullyVisibleThreshold = 0.999f;

        for (int i = 0; i < m_rendererStates.Count; i++)
        {
            SkinnedMeshRenderer targetRenderer = m_rendererStates[i].Renderer;
            if (targetRenderer == null)
                continue;

            float minFadeDistance = -fadeRange;
            if (clampedVisibility < fullyVisibleThreshold)
            {
                Vector3 fadeOrigin = targetRenderer.rootBone != null
                    ? targetRenderer.rootBone.position
                    : targetRenderer.transform.position;
                float rendererDistance = Vector3.Distance(
                    m_outputCamera.transform.position,
                    fadeOrigin
                );
                minFadeDistance = rendererDistance - clampedVisibility * fadeRange;
            }

            m_propertyBlock.Clear();
            targetRenderer.GetPropertyBlock(m_propertyBlock);
            m_propertyBlock.SetFloat(NearFadeEnabledId, 1f);
            m_propertyBlock.SetFloat(MaxFadeDistanceId, fadeRange);
            m_propertyBlock.SetFloat(MinFadeDistanceId, minFadeDistance);
            m_propertyBlock.SetFloat(
                ToonClippingLevelId,
                clampedVisibility - 0.5f
            );
            targetRenderer.SetPropertyBlock(m_propertyBlock);
        }
    }

    private void AssignRuntimeMaterials()
    {
        for (int i = 0; i < m_rendererStates.Count; i++)
        {
            RendererState state = m_rendererStates[i];
            if (state.Renderer != null)
                state.Renderer.sharedMaterials = state.RuntimeMaterials;
        }
    }

    private void RestoreCameraRigs()
    {
        for (int i = 0; i < m_rigStates.Count; i++)
        {
            CameraRigState state = m_rigStates[i];
            if (state.Rig != null)
                state.Rig.ShoulderOffset = state.BaseShoulderOffset;
        }
    }

    private void RestoreOriginalRendererState()
    {
        for (int i = 0; i < m_rendererStates.Count; i++)
        {
            RendererState state = m_rendererStates[i];
            if (state.Renderer == null)
                continue;

            state.Renderer.sharedMaterials = state.OriginalMaterials;
            state.Renderer.SetPropertyBlock(state.OriginalPropertyBlock);
        }
    }

    private void DestroyRuntimeMaterials()
    {
        for (int i = 0; i < m_runtimeMaterials.Count; i++)
        {
            Material runtimeMaterial = m_runtimeMaterials[i];
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }

        m_runtimeMaterials.Clear();

        if (m_ditherMask != null)
        {
            Destroy(m_ditherMask);
            m_ditherMask = null;
        }
    }
}
