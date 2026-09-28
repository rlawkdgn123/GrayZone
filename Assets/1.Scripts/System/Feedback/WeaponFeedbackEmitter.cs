using System.Collections.Generic;
using UnityEngine;
using VInspector;

/// <summary>무기 행동 타이밍에 맞춰 사운드·머즐·트레이서·탄피 피드백을 출력합니다.</summary>
/// <remarks>
/// 사격 가능 여부와 타이밍은 <see cref="Gun"/>이 결정하며, 이 컴포넌트는 전달받은 결과를 표현하기만 합니다.
/// <para>
/// 표현에 쓸 리소스는 이 컴포넌트가 직접 소유합니다. 재생을 담당하는 쪽이 재생할 것을 갖는 편이 자연스럽고,
/// <see cref="Gun"/>이 매 호출마다 SO를 통째로 넘기던 구조에서는 같은 타입의 리소스 여러 개를 구분할 수 없었습니다.
/// 필드 이름으로 짝을 찾으면 <c>m_muzzleEffectPrefab</c>과 <c>m_tracerEffectPrefab</c>이
/// 둘 다 <c>GameObject</c>여도 정확히 갈립니다.
/// </para>
/// <para>
/// 값이 정해지는 순서는 밸런스와 같습니다. 프리팹에 저작된 값 &lt; 통합 피드백 SO &lt; 개별 피드백 SO.
/// </para>
/// </remarks>
[DisallowMultipleComponent]
public sealed class WeaponFeedbackEmitter : MonoBehaviour, ISharedFeedbackReceiver
{
    [Header("Feedback SO (통합보다 우선)")]
    [Tooltip("이 무기 전용 피드백 SO입니다. 지정하면 엔티티 통합 피드백 SO보다 우선합니다. 비우면 통합 SO나 아래 값을 그대로 씁니다.")]
    [SerializeField] private WeaponFeedbackSO m_feedbackSO;

    [Header("Audio")]
    [Tooltip("무기 피드백 효과음을 재생할 전용 3D AudioSource입니다. 비어 있으면 런타임에 자식 오브젝트로 생성합니다.")]
    [SerializeField] private AudioSource m_audioSource;

    [Tooltip("무기 피드백 효과음의 기본 음량입니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_volume = 1.0f;

    [Tooltip("매 재생마다 적용할 무작위 피치 편차입니다. 0이면 원본 피치로 재생합니다.")]
    [Range(0.0f, 0.25f)]
    [SerializeField] private float m_pitchVariation = 0.03f;

    [Tooltip("무기 효과음이 최대 크기로 들리는 거리(m)입니다.")]
    [SerializeField] private float m_minDistance = 1.0f;

    [Tooltip("무기 효과음이 감쇠되어 들리는 최대 거리(m)입니다.")]
    [SerializeField] private float m_maxDistance = 55.0f;

    // ───────────── 아래는 피드백 SO에서 주입받는 리소스입니다 ─────────────
    // 필드 이름이 WeaponFeedbackSO와 같아야 BindManager가 짝을 찾습니다.

    [Header("Injected Resources (SO가 있으면 덮임)")]
    [Tooltip("사격할 때 후보 중 하나를 선택해 재생할 사운드 목록입니다.")]
    [FeedbackField]
    [SerializeField] private AudioClip[] m_shotSounds = System.Array.Empty<AudioClip>();

    [Tooltip("탄약이 없거나 사격이 막혔을 때 후보 중 하나를 선택해 재생할 사운드 목록입니다.")]
    [FeedbackField]
    [SerializeField] private AudioClip[] m_dryFireSounds = System.Array.Empty<AudioClip>();

    [Tooltip("재장전할 때 후보 중 하나를 선택해 재생할 사운드 목록입니다.")]
    [FeedbackField]
    [SerializeField] private AudioClip[] m_reloadSounds = System.Array.Empty<AudioClip>();

    [Tooltip("사격할 때 재생할 FMOD 이벤트입니다. 지정하면 AudioClip 목록보다 우선합니다.")]
    [FeedbackField]
    [SerializeField] private FMODUnity.EventReference m_shotEvent;

    [Tooltip("탄약이 없거나 사격이 막혔을 때 재생할 FMOD 이벤트입니다. 지정하면 AudioClip 목록보다 우선합니다.")]
    [FeedbackField]
    [SerializeField] private FMODUnity.EventReference m_dryFireEvent;

    [Tooltip("재장전할 때 재생할 FMOD 이벤트입니다. 지정하면 AudioClip 목록보다 우선합니다.")]
    [FeedbackField]
    [SerializeField] private FMODUnity.EventReference m_reloadEvent;

    [Tooltip("총구 소켓에서 재생하거나 생성할 머즐 이펙트 프리팹입니다.")]
    [FeedbackField]
    [SerializeField] private GameObject m_muzzleEffectPrefab;

    [Tooltip("생성한 머즐 이펙트를 자동 제거하기까지의 시간(초)입니다. 0 이하면 자동 제거하지 않습니다.")]
    [FeedbackField]
    [SerializeField] private float m_muzzleEffectLifetime = 2.0f;

    [Tooltip("총구에서 히트 지점까지 표시할 레이 또는 트레이서 이펙트 프리팹입니다.")]
    [FeedbackField]
    [SerializeField] private GameObject m_tracerEffectPrefab;

    [Tooltip("생성한 레이 또는 트레이서 이펙트를 자동 제거하기까지의 시간(초)입니다.")]
    [FeedbackField]
    [SerializeField] private float m_tracerEffectLifetime = 0.15f;

    [Tooltip("탄이 지형에 멈춘 위치에서 재생할 이 무기 전용 탄착 이펙트 프리팹입니다.")]
    [FeedbackField]
    [SerializeField] private GameObject m_impactEffectPrefab;

    [Tooltip("총기 전용 탄착 이펙트를 개인 풀로 회수하기까지의 시간(초)입니다.")]
    [FeedbackField]
    [SerializeField] private float m_impactEffectLifetime = 1.0f;

    [Tooltip("탄피 배출 소켓에서 생성할 탄피 프리팹입니다.")]
    [FeedbackField]
    [SerializeField] private GameObject m_shellPrefab;

    [Tooltip("생성한 탄피 오브젝트를 자동 제거하기까지의 시간(초)입니다.")]
    [FeedbackField]
    [SerializeField] private float m_shellLifetime = 8.0f;

    // Debug 구역은 직렬화 필드의 맨 끝에 둡니다. Foldout은 다음 Foldout이나 EndFoldout이 나올 때까지 이어지므로,
    // 중간에 두면 뒤따르는 필드가 전부 Debug 구역으로 딸려 들어가 인스펙터와 수집 목록이 함께 어긋납니다.
    [Foldout("Debug")]
    [Tooltip("이 무기를 선택했을 때 효과음의 감쇠 시작 거리와 소멸 거리를 Scene 뷰에 원으로 표시합니다. 소음 시스템의 도달 거리와는 별개 값입니다.")]
    [SerializeField] private bool m_debugDrawAudioDistance = false;

    private int m_lastShotIndex = -1;
    private int m_lastDryFireIndex = -1;
    private int m_lastReloadIndex = -1;
    private bool m_loggedMissingFmodEvent;
    private PersonalEffectPool m_personalEffectPool;

    /// <summary>이 무기를 든 스쿼드 멤버입니다. 조작 주체 판정의 정본 소유자입니다.</summary>
    private SquadMemberController m_ownerMember;

    private const float ImpactSurfaceOffset = 0.002f;
    private static readonly Quaternion MuzzleEffectAxisCorrection = Quaternion.Euler(90.0f, 0.0f, 0.0f);

    /// <summary>이 무기에 지정된 개별 피드백 SO입니다. 지정하지 않았으면 <c>null</c>입니다.</summary>
    public WeaponFeedbackSO FeedbackSO => m_feedbackSO;

    /// <summary>개별 피드백 SO를 직접 물고 있는지 여부입니다.</summary>
    /// <remarks><c>true</c>면 <see cref="SOBinder"/>가 통합 SO 주입을 건너뜁니다.</remarks>
    public bool HasOwnFeedback => m_feedbackSO != null;

    /// <summary>
    /// 표현할 리소스를 하나라도 가지고 있는지 여부입니다.
    /// </summary>
    /// <remarks>
    /// <see cref="Gun"/>이 새 피드백 경로를 쓸지 예전 개별 배선 경로를 쓸지 판단하는 기준입니다.
    /// 수명 값만 있고 클립·프리팹이 전부 비어 있으면 재생할 것이 없으므로 없는 것으로 봅니다.
    /// </remarks>
    public bool HasFeedback =>
        HasEvent(m_shotEvent)
        || HasEvent(m_dryFireEvent)
        || HasEvent(m_reloadEvent)
        || HasAny(m_shotSounds)
        || HasAny(m_dryFireSounds)
        || HasAny(m_reloadSounds)
        || m_muzzleEffectPrefab != null
        || m_tracerEffectPrefab != null
        || m_impactEffectPrefab != null
        || m_shellPrefab != null;

    /// <summary>개별 피드백 SO가 있으면 먼저 적용하고 오디오 소스를 준비합니다.</summary>
    private void Awake()
    {
        BindConfiguredFeedback();
        EnsureAudioSource();
        EnsurePersonalEffectPool();
    }

    /// <summary>이 무기가 소유한 활성 개인 이펙트의 수명을 갱신하고 회수합니다.</summary>
    private void Update()
    {
        m_personalEffectPool?.Tick(Time.time);
    }

    /// <summary>무기가 비활성화되면 이 무기가 소유한 활성 개인 이펙트를 모두 회수합니다.</summary>
    private void OnDisable()
    {
        m_personalEffectPool?.RecycleAll();
    }

    /// <summary>무기가 파괴되기 전에 공용 예산에 보고한 개인 이펙트·사운드 등록을 정리합니다.</summary>
    private void OnDestroy()
    {
        m_personalEffectPool?.RecycleAll();

        AudioManager fieldAudio = FieldManager.Instance != null ? FieldManager.Instance.AudioManager : null;
        if (fieldAudio != null)
        {
            fieldAudio.UnregisterPersonalSource(m_audioSource);
        }
    }

    /// <summary>
    /// 엔티티 통합 피드백 SO의 리소스를 적용합니다.
    /// </summary>
    /// <param name="feedback">통합 피드백 SO입니다.</param>
    /// <returns>이번 바인딩의 집계 결과입니다.</returns>
    /// <remarks>개별 SO 슬롯은 비운 채로 둡니다. 비어 있다는 것 자체가 "개별 지정 없음"을 뜻합니다.</remarks>
    public BalanceBindResult BindSharedFeedback(ScriptableObject feedback)
    {
        return BindFrom(feedback);
    }

    /// <summary>지정된 개별 SO가 있을 때 그 리소스를 적용합니다.</summary>
    private BalanceBindResult BindConfiguredFeedback()
    {
        return BindFrom(m_feedbackSO);
    }

    /// <summary>주어진 원본 SO에서 리소스 참조를 대입합니다.</summary>
    /// <param name="feedback">값을 읽어올 피드백 SO입니다.</param>
    /// <returns>이번 바인딩의 집계 결과입니다. 원본이 없으면 기본값입니다.</returns>
    private BalanceBindResult BindFrom(ScriptableObject feedback)
    {
        if (feedback == null)
        {
            return default;
        }

        return BindManager.Instance.BindFeedback(feedback, this, this);
    }

    /// <summary>성공한 한 발의 사운드와 시각 피드백을 출력합니다.</summary>
    /// <param name="muzzleSocket">머즐 이펙트를 생성할 소켓입니다.</param>
    /// <param name="shellSocket">탄피를 생성할 소켓입니다.</param>
    /// <param name="tracerStart">트레이서 시작 지점입니다.</param>
    /// <param name="tracerEnd">트레이서 끝 지점입니다.</param>
    /// <param name="playTracer">일반탄 트레이서를 출력할지 여부입니다.</param>
    public void PlayShot(
        Transform muzzleSocket,
        Transform shellSocket,
        Vector3 tracerStart,
        Vector3 tracerEnd,
        bool playTracer = true)
    {
        PlayLocal(m_shotEvent, m_shotSounds, ref m_lastShotIndex);

        Vector3 tracerDelta = tracerEnd - tracerStart;
        bool hasShotDirection = tracerDelta.sqrMagnitude > 0.0001f;

        if (muzzleSocket != null)
        {
            // The socket follows the animated weapon, but the visible flash must follow the
            // actual hitscan direction. Preserve the prefab root's authored rotation, then
            // turn its longest flash axis +90 degrees around local X so it extends along the
            // muzzle's +Z shot direction.
            Quaternion muzzleRotation = hasShotDirection
                ? Quaternion.LookRotation(tracerDelta.normalized)
                : muzzleSocket.rotation;
            if (m_muzzleEffectPrefab != null)
            {
                muzzleRotation *= m_muzzleEffectPrefab.transform.localRotation
                    * MuzzleEffectAxisCorrection;
            }

            SpawnPersonalEffect(
                m_muzzleEffectPrefab,
                muzzleSocket.position,
                muzzleRotation,
                m_muzzleEffectLifetime,
                muzzleSocket,
                forceLocalParticleSimulation: true);
        }

        if (playTracer)
        {
            PlayTracer(tracerStart, tracerEnd);
        }

        if (shellSocket != null)
        {
            SpawnPersonalEffect(
                m_shellPrefab,
                shellSocket.position,
                shellSocket.rotation,
                m_shellLifetime);
        }
    }

    /// <summary>한 펠릿의 총구-탄착 경로에 트레이서만 출력합니다.</summary>
    /// <param name="tracerStart">트레이서 시작 지점입니다.</param>
    /// <param name="tracerEnd">트레이서 끝 지점입니다.</param>
    /// <remarks>
    /// 산탄총의 두 번째 이후 펠릿처럼 사운드·머즐·탄피를 다시 재생하면 안 되는 경로에서 사용합니다.
    /// 첫 펠릿은 <see cref="PlayShot"/>이 이 메서드를 함께 호출합니다.
    /// </remarks>
    public void PlayTracer(Vector3 tracerStart, Vector3 tracerEnd)
    {
        Vector3 tracerDelta = tracerEnd - tracerStart;
        Quaternion tracerRotation = tracerDelta.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(tracerDelta.normalized)
            : Quaternion.identity;

        GameObject tracer = SpawnPersonalEffect(
            m_tracerEffectPrefab,
            tracerStart,
            tracerRotation,
            m_tracerEffectLifetime);
        FeedbackPlaybackUtility.ConfigureTracer(tracer, tracerStart, tracerEnd);
    }

    /// <summary>탄이 지형에 멈춘 위치에서 이 무기 전용 탄착 이펙트를 재생합니다.</summary>
    /// <param name="hit">지형 충돌 위치와 표면 법선을 가진 히트 결과입니다.</param>
    public void PlayImpact(RaycastHit hit)
    {
        if (hit.collider == null || m_impactEffectPrefab == null)
        {
            return;
        }

        Vector3 safeNormal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : Vector3.up;

        // 탄착 VFX는 맞은 Collider의 자식으로 두지 않습니다.
        // 표면 Transform의 비균일 스케일이 ParticleSystem 일부에만 적용되면
        // 같은 프리팹도 벽과 바닥에서 크기와 형태가 달라집니다.
        SpawnPersonalEffect(
            m_impactEffectPrefab,
            hit.point + safeNormal * ImpactSurfaceOffset,
            Quaternion.LookRotation(safeNormal),
            m_impactEffectLifetime);
    }

    /// <summary>사격이 탄약 부족 등으로 막힌 순간의 드라이 사운드를 출력합니다.</summary>
    public void PlayDryFire()
    {
        PlayLocal(m_dryFireEvent, m_dryFireSounds, ref m_lastDryFireIndex);
    }

    /// <summary>재장전 시작 사운드를 출력합니다.</summary>
    public void PlayReload()
    {
        PlayLocal(m_reloadEvent, m_reloadSounds, ref m_lastReloadIndex);
    }

    /// <summary>FMOD 이벤트 참조가 실제 이벤트를 가리키는지 확인합니다.</summary>
    private static bool HasEvent(FMODUnity.EventReference eventReference)
    {
        return !eventReference.IsNull;
    }

    /// <summary>후보 목록이 실제로 재생할 클립을 가지고 있는지 확인합니다.</summary>
    /// <param name="clips">검사할 클립 목록입니다.</param>
    /// <returns>비어 있지 않고 유효한 클립이 하나라도 있으면 <c>true</c>입니다.</returns>
    private static bool HasAny(IReadOnlyList<AudioClip> clips)
    {
        if (clips == null)
        {
            return false;
        }

        for (int i = 0; i < clips.Count; i++)
        {
            if (clips[i] != null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>FMOD 이벤트를 우선 재생하고, 사용할 수 없으면 기존 AudioClip 경로로 복귀합니다.</summary>
    private void PlayLocal(
        FMODUnity.EventReference eventReference,
        IReadOnlyList<AudioClip> clips,
        ref int lastIndex)
    {
        if (TryPlayFmod(eventReference))
        {
            return;
        }

        AudioSource source = EnsureAudioSource();
        if (source == null || !FeedbackPlaybackUtility.TryPickClip(clips, ref lastIndex, out AudioClip clip))
        {
            return;
        }

        source.priority = AudioManager.ResolveUnityPriority(ResolveCombatPriorityClass());
        source.pitch = 1.0f + Random.Range(-m_pitchVariation, m_pitchVariation);
        source.PlayOneShot(clip, m_volume);
    }

    /// <summary>현재 무기 위치에 FMOD one-shot을 붙여 재생합니다.</summary>
    private bool TryPlayFmod(FMODUnity.EventReference eventReference)
    {
        if (eventReference.IsNull || !FMODUnity.RuntimeManager.IsInitialized)
        {
            return false;
        }

        try
        {
            FMOD.Studio.EventInstance instance = FMODUnity.RuntimeManager.CreateInstance(eventReference);
            if (!instance.isValid())
            {
                return false;
            }

            FMODUnity.RuntimeManager.AttachInstanceToGameObject(instance, gameObject);
            instance.setVolume(m_volume);
            instance.setPitch(1.0f + Random.Range(-m_pitchVariation, m_pitchVariation));
            instance.start();
            instance.release();
            return true;
        }
        catch (FMODUnity.EventNotFoundException exception)
        {
            if (!m_loggedMissingFmodEvent)
            {
                Debug.LogWarning($"[WeaponFeedbackEmitter] FMOD 이벤트를 찾지 못해 AudioClip으로 대체합니다: {exception.Message}", this);
                m_loggedMissingFmodEvent = true;
            }

            return false;
        }
    }

    /// <summary>
    /// 이 무기 소리의 보호 등급을 정합니다. 플레이어가 직접 조작하는 멤버의 무기가 가장 높습니다.
    /// </summary>
    /// <remarks>
    /// 조작 주체는 런타임에 바뀌므로(<see cref="SquadMemberController.OnPlayerSquadMemberChanged"/>)
    /// 등급을 캐시하지 않고 재생마다 다시 묻습니다. 컴포넌트 참조만 캐시합니다.
    /// 스쿼드 멤버에 속하지 않은 무기는 플레이어 것으로 봅니다. 자기 총성이 씹히는 편이
    /// 남의 총성이 씹히는 것보다 나쁘므로, 판정이 불확실할 때는 더 보호하는 쪽으로 기울입니다.
    /// </remarks>
    private AudioPriorityClass ResolveCombatPriorityClass()
    {
        if (m_ownerMember == null)
        {
            m_ownerMember = GetComponentInParent<SquadMemberController>(true);
        }

        return m_ownerMember == null || m_ownerMember.IsPlayerSquadMember
            ? AudioPriorityClass.PlayerCritical
            : AudioPriorityClass.AllyCombat;
    }

    private AudioSource EnsureAudioSource()
    {
        if (m_audioSource == null)
        {
            GameObject sourceObject = new GameObject("Weapon Feedback Audio");
            sourceObject.transform.SetParent(transform, false);
            m_audioSource = sourceObject.AddComponent<AudioSource>();
        }

        m_audioSource.loop = false;

        AudioManager fieldAudio = FieldManager.Instance != null ? FieldManager.Instance.AudioManager : null;
        if (fieldAudio != null)
        {
            // 무기 소리는 손을 따라 움직여야 해서 풀을 쓰지 않습니다. 대신 개인 사운드로 등록해
            // 공용 풀이 그만큼 양보하게 만듭니다. EffectPool의 개인 이펙트 회계와 같은 방식입니다.
            fieldAudio.ApplyWorldSourcePolicy(
                m_audioSource,
                ResolveCombatPriorityClass(),
                m_minDistance,
                m_maxDistance);
            fieldAudio.RegisterPersonalSource(m_audioSource);
        }
        else
        {
            m_audioSource.playOnAwake = false;
            m_audioSource.spatialBlend = 1.0f;
            m_audioSource.dopplerLevel = 0.0f;
            m_audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
            m_audioSource.minDistance = Mathf.Max(0.01f, m_minDistance);
            m_audioSource.maxDistance = Mathf.Max(m_audioSource.minDistance, m_maxDistance);
        }

        return m_audioSource;
    }

    /// <summary>이 무기 전용 시각 이펙트 풀을 준비합니다.</summary>
    private PersonalEffectPool EnsurePersonalEffectPool()
    {
        return m_personalEffectPool ??= new PersonalEffectPool(transform);
    }

    /// <summary>이 무기의 개인 풀에서 프리팹 인스턴스를 꺼내 재생합니다.</summary>
    private GameObject SpawnPersonalEffect(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        float lifetime,
        Transform parent = null,
        bool forceLocalParticleSimulation = false)
    {
        EffectPool sharedBudget = FieldManager.Instance != null && FieldManager.Instance.EffectManager != null
            ? FieldManager.Instance.EffectManager.Pool
            : null;

        return EnsurePersonalEffectPool().Spawn(
            prefab,
            position,
            rotation,
            lifetime,
            parent,
            forceLocalParticleSimulation,
            sharedBudget);
    }

    /// <summary>한 무기가 생성한 시각 이펙트만 보관하고 재사용하는 런타임 풀입니다.</summary>
    /// <remarks>
    /// 공용 <see cref="EffectPool"/>은 이 인스턴스들을 소유하거나 회수하지 않습니다.
    /// 다만 활성·회수 시점의 개수만 보고받아 <c>개인 이펙트 포함하기</c> 옵션의 공용 가용량 계산에 사용합니다.
    /// 별도 최대치는 두지 않고, 각 프리팹의 실제 동시 재생량까지 한 번 생성한 뒤 그 범위 안에서 재사용합니다.
    /// </remarks>
    private sealed class PersonalEffectPool
    {
        private sealed class LiveEffect
        {
            public GameObject Instance;
            public GameObject Prefab;
            public float ExpiresAt;
            public bool HasLifetime;
            public EffectPool SharedBudget;
        }

        private readonly Transform m_owner;
        private readonly List<LiveEffect> m_live = new List<LiveEffect>(16);
        private readonly Dictionary<GameObject, Stack<GameObject>> m_idle =
            new Dictionary<GameObject, Stack<GameObject>>();

        private Transform m_idleRoot;

        public PersonalEffectPool(Transform owner)
        {
            m_owner = owner;
        }

        public GameObject Spawn(
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            float lifetime,
            Transform parent,
            bool forceLocalParticleSimulation,
            EffectPool sharedBudget)
        {
            if (prefab == null)
            {
                return null;
            }

            GameObject instance = TakeIdle(prefab);
            if (instance == null)
            {
                instance = Object.Instantiate(prefab);
            }

            Transform instanceTransform = instance.transform;
            instanceTransform.SetParent(parent, false);
            instanceTransform.SetPositionAndRotation(position, rotation);

            // Muzzle flashes are attached to an animated socket. World-space particles stay
            // at the previous frame's emission position while crouch/recoil moves the gun,
            // making the flash visibly detach even though its root remains on BulletPoint.
            if (forceLocalParticleSimulation)
            {
                ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < particles.Length; i++)
                {
                    ParticleSystem.MainModule main = particles[i].main;
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                }
            }

            instance.SetActive(true);
            FeedbackPlaybackUtility.RestartPlayback(instance);

            sharedBudget?.RegisterPersonalEffect();
            m_live.Add(new LiveEffect
            {
                Instance = instance,
                Prefab = prefab,
                ExpiresAt = Time.time + lifetime,
                HasLifetime = lifetime > 0.0f,
                SharedBudget = sharedBudget,
            });

            return instance;
        }

        public void Tick(float now)
        {
            for (int i = m_live.Count - 1; i >= 0; i--)
            {
                LiveEffect entry = m_live[i];
                if (entry.Instance == null)
                {
                    if (entry.SharedBudget != null)
                    {
                        entry.SharedBudget.UnregisterPersonalEffect();
                    }

                    m_live.RemoveAt(i);
                    continue;
                }

                if (entry.HasLifetime && now >= entry.ExpiresAt)
                {
                    RecycleAt(i);
                }
            }
        }

        public void RecycleAll()
        {
            for (int i = m_live.Count - 1; i >= 0; i--)
            {
                RecycleAt(i);
            }
        }

        private void RecycleAt(int index)
        {
            LiveEffect entry = m_live[index];
            m_live.RemoveAt(index);
            if (entry.SharedBudget != null)
            {
                entry.SharedBudget.UnregisterPersonalEffect();
            }

            if (entry.Instance == null)
            {
                return;
            }

            entry.Instance.SetActive(false);
            entry.Instance.transform.SetParent(ResolveIdleRoot(), false);

            if (!m_idle.TryGetValue(entry.Prefab, out Stack<GameObject> stack))
            {
                stack = new Stack<GameObject>();
                m_idle[entry.Prefab] = stack;
            }

            stack.Push(entry.Instance);
        }

        private GameObject TakeIdle(GameObject prefab)
        {
            if (!m_idle.TryGetValue(prefab, out Stack<GameObject> stack))
            {
                return null;
            }

            while (stack.Count > 0)
            {
                GameObject instance = stack.Pop();
                if (instance != null)
                {
                    return instance;
                }
            }

            return null;
        }

        private Transform ResolveIdleRoot()
        {
            if (m_idleRoot != null)
            {
                return m_idleRoot;
            }

            GameObject root = new GameObject("WeaponEffectPool(Idle)");
            root.transform.SetParent(m_owner, false);
            root.SetActive(false);
            m_idleRoot = root.transform;
            return m_idleRoot;
        }
    }

    /// <summary>
    /// 선택했을 때 이 소리의 감쇠 시작·소멸 거리를 그립니다.
    /// </summary>
    /// <remarks>
    /// 안쪽 원(minDistance)까지는 원래 크기로 들리고, 바깥 원(maxDistance)에서 완전히 사라집니다.
    /// 총성이 어디까지 들리는지는 잠입 설계와 직결되는데, 이 값은 <b>소음 시스템의 도달 거리와 별개</b>입니다.
    /// 하나는 플레이어가 듣는 거리이고 다른 하나는 변이체가 반응하는 거리라, 둘이 크게 어긋나면
    /// "들리는데 아무도 안 오는" 또는 그 반대의 어긋남이 생깁니다. 두 기즈모를 같이 켜서 비교하는 용도입니다.
    /// </remarks>
    private void OnDrawGizmosSelected()
    {
        if (!m_debugDrawAudioDistance)
        {
            return;
        }

        Gizmos.color = new Color(0.5f, 1.0f, 0.9f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.01f, m_minDistance));

        Gizmos.color = new Color(0.2f, 0.6f, 0.7f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(m_minDistance, m_maxDistance));
    }
}
