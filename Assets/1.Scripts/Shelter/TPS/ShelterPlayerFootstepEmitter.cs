using FMOD.Studio;
using FMODUnity;
using UnityEngine;

/// <summary>
/// 셸터 플레이어 애니메이션의 발걸음·착지 이벤트를 외곽 방어전과 같은 FMOD 이벤트로 전달합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShelterPlayerFootstepEmitter : MonoBehaviour
{
    private const string FootwearFmodParameter = "Footwear";
    private const string SurfaceFmodParameter = "Surface";

    [Header("References")]
    [SerializeField] private CharacterController m_characterController;

    [Header("FMOD Events")]
    [Tooltip("향후 웅크린 걷기에서 사용할 보행 이벤트입니다.")]
    [SerializeField] private EventReference m_footstepEvent;
    [Tooltip("현재 셸터의 기본 이동과 전력질주에서 사용할 달리기 이벤트입니다.")]
    [SerializeField] private EventReference m_runFootstepEvent;
    [SerializeField] private EventReference m_jumpEvent;
    [SerializeField] private EventReference m_landEvent;

    [Header("Surface / Footwear")]
    [SerializeField] private FootwearType m_footwearType = FootwearType.Sneakers;
    [Tooltip("발밑에 SurfaceMaterialTag가 없을 때 사용할 재질입니다.")]
    [SerializeField] private SurfaceMaterialType m_fallbackSurface = SurfaceMaterialType.Concrete;
    [SerializeField] private LayerMask m_groundLayers = ~0;

    [Header("Audio")]
    [Range(0f, 1f)]
    [SerializeField] private float m_footstepAudioVolume = 0.5f;
    [Tooltip("Blend Tree의 Walk/Run 클립이 거의 동시에 같은 발 디딤 이벤트를 보낼 때 중복 재생을 막는 간격입니다.")]
    [Min(0f)]
    [SerializeField] private float m_minFootstepInterval = 0.18f;
    [Tooltip("Blend Tree에서 이 비중 미만인 클립의 발 디딤 이벤트는 무시합니다. 0.5면 Walk/Run 중 더 많이 섞인 클립의 박자만 소리를 냅니다.")]
    [Range(0f, 1f)]
    [SerializeField] private float m_minFootstepClipWeight = 0.5f;
    [Tooltip("코드의 접지 전환과 착지 애니메이션 이벤트가 겹쳐도 착지음을 한 번만 재생하는 간격입니다.")]
    [Min(0f)]
    [SerializeField] private float m_minLandInterval = 0.2f;

    private bool m_loggedMissingMovementFmodEvent;
    private float m_nextFootstepTime;
    private float m_nextLandTime;

    private void Reset()
    {
        m_characterController = GetComponentInParent<CharacterController>();
    }

    private void Awake()
    {
        if (m_characterController == null)
            m_characterController = GetComponentInParent<CharacterController>();
    }

    /// <summary>걷기/달리기 클립의 AnimationEvent에서 호출됩니다.</summary>
    public void OnFootstep(AnimationEvent animationEvent)
    {
        // Blend Tree는 비중이 0에 가까운 클립의 이벤트도 보내므로, 주도 클립의 박자만 재생합니다.
        if (animationEvent.animatorClipInfo.weight < m_minFootstepClipWeight)
            return;

        if (Time.time < m_nextFootstepTime)
            return;

        // 셸터의 기본 이동 속도도 조깅에 가까우므로 외곽 방어전과 동일하게 Run 세트를 사용합니다.
        if (TryPlaySurfaceFmod(m_runFootstepEvent, ResolveFootstepSurface()))
            m_nextFootstepTime = Time.time + m_minFootstepInterval;
    }

    /// <summary>착지 클립의 AnimationEvent에서 호출됩니다.</summary>
    public void OnLand(AnimationEvent animationEvent)
    {
        if (animationEvent.animatorClipInfo.weight <= 0.5f)
            return;

        PlayLand();
    }

    /// <summary>실제 점프 입력이 적용되는 프레임에 도약음을 재생합니다.</summary>
    public void PlayJump()
    {
        TryPlaySurfaceFmod(m_jumpEvent, ResolveFootstepSurface());
    }

    /// <summary>캐릭터가 실제로 공중 상태에서 접지 상태로 바뀐 순간 착지음을 재생합니다.</summary>
    public void PlayLand()
    {
        if (Time.time < m_nextLandTime)
            return;

        if (TryPlaySurfaceFmod(m_landEvent, ResolveFootstepSurface()))
            m_nextLandTime = Time.time + m_minLandInterval;
    }

    private bool TryPlaySurfaceFmod(EventReference eventReference, SurfaceMaterialType surfaceType)
    {
        if (eventReference.IsNull || !SoundManager.IsReady)
            return false;

        try
        {
            EventInstance instance = SoundManager.Instance.CreateInstance(eventReference, gameObject);
            if (!instance.isValid())
                return false;

            instance.setParameterByNameWithLabel(FootwearFmodParameter, m_footwearType.ToString());
            instance.setParameterByNameWithLabel(SurfaceFmodParameter, surfaceType.ToString());
            instance.setVolume(m_footstepAudioVolume);
            return SoundManager.Instance.StartOneShot(instance);
        }
        catch (EventNotFoundException exception)
        {
            if (!m_loggedMissingMovementFmodEvent)
            {
                Debug.LogWarning($"[ShelterPlayerFootstepEmitter] FMOD 이동 효과음 이벤트를 찾지 못했습니다: {exception.Message}", this);
                m_loggedMissingMovementFmodEvent = true;
            }

            return false;
        }
    }

    private SurfaceMaterialType ResolveFootstepSurface()
    {
        if (m_characterController == null)
            return m_fallbackSurface;

        Transform controllerTransform = m_characterController.transform;
        Vector3 center = controllerTransform.TransformPoint(m_characterController.center);
        float footY = center.y - (m_characterController.height * 0.5f);
        Vector3 origin = new Vector3(center.x, footY + 0.35f, center.z);

        if (!Physics.Raycast(
                origin,
                Vector3.down,
                out RaycastHit hit,
                0.8f,
                m_groundLayers,
                QueryTriggerInteraction.Ignore))
        {
            return m_fallbackSurface;
        }

        SurfaceMaterialType resolvedSurface = SurfaceMaterialTag.Resolve(hit.collider);
        return resolvedSurface != SurfaceMaterialType.Unknown ? resolvedSurface : m_fallbackSurface;
    }
}
