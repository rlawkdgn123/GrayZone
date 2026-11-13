using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>한 전투 대상에게 적용 중인 버프·디버프·상태이상의 런타임 인스턴스를 관리합니다.</summary>
[DisallowMultipleComponent]
public sealed class StatusEffectContainer : MonoBehaviour
{
    private sealed class Entry
    {
        public StatusEffectDefinitionSO Definition;
        public Faction SourceFaction;
        public GameObject Source;
        public int Stacks;
        public float ExpireTime;
        public float NextTickTime;
        public GameObject VisualInstance;
        public long VisualSequence;
    }

    private sealed class OneShotVisual
    {
        public UnityEngine.Object Key;
        public StatusEffectVisualSO Definition;
        public GameObject Instance;
        public float ExpireTime;
        public long VisualSequence;
    }

    private sealed class ModelTintTarget
    {
        public Renderer Renderer;
        public int MaterialIndex;
        public bool HasBaseColor;
        public bool HasColor;
        public Color OriginalBaseColor;
        public Color OriginalColor;
    }

    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");

    private readonly List<Entry> m_entries = new List<Entry>();

    /// <summary>
    /// 효과 정의별로 마지막에 그 효과를 건 공격의 정보입니다. 주기 피해의 공격자·원래 공격으로 씁니다.
    /// </summary>
    /// <remarks>
    /// 상태이상 피해의 공격자는 마지막 적용자로 정합니다. 출처별로 따로 쌓이는 효과도 정의 하나에 하나만 두어,
    /// 어느 쌓임에서 나온 피해든 마지막에 건 쪽으로 기록됩니다.
    /// 공격자 오브젝트(화염 지대 등)가 먼저 사라져도 이름과 공격 방식이 남도록 걸 때 저장합니다.
    /// </remarks>
    private readonly Dictionary<StatusEffectDefinitionSO, DamageInfo> m_lastCauses = new Dictionary<StatusEffectDefinitionSO, DamageInfo>();
    private readonly List<OneShotVisual> m_oneShotVisuals = new List<OneShotVisual>();
    private readonly Dictionary<GameObject, Stack<GameObject>> m_visualPool =
        new Dictionary<GameObject, Stack<GameObject>>();
    private readonly List<ModelTintTarget> m_modelTintTargets = new List<ModelTintTarget>();
    private MaterialPropertyBlock m_modelTintPropertyBlock;

    [Header("Recipient Visuals")]
    [Tooltip("Optional anchor for body status visuals. If empty, a renderer-bounds center anchor is created at runtime.")]
    [SerializeField] private Transform m_effectAnchor;
    [Tooltip("Optional root whose mesh renderers receive status colors. If empty, the whole recipient is used.")]
    [SerializeField] private Transform m_modelTintRoot;

    private IDamageable m_damageable;
    private SquadMemberController m_squadMember;
    private Transform m_runtimeEffectAnchor;
    private Transform m_visualPoolRoot;
    private long m_nextVisualSequence;

    public int Count => m_entries.Count;
    public int ActiveVisualCount
    {
        get
        {
            int count = m_oneShotVisuals.Count;
            for (int i = 0; i < m_entries.Count; i++)
            {
                if (m_entries[i].VisualInstance != null && m_entries[i].VisualInstance.activeSelf)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public int PooledVisualCount
    {
        get
        {
            int count = 0;
            foreach (KeyValuePair<GameObject, Stack<GameObject>> pair in m_visualPool)
            {
                count += pair.Value.Count;
            }

            return count;
        }
    }

    public float MovementSpeedMultiplier { get; private set; } = 1.0f;
    public float ActionSpeedMultiplier { get; private set; } = 1.0f;
    public bool HasActiveModelTint { get; private set; }
    public Color CurrentModelTintColor { get; private set; } = Color.white;
    public float CurrentModelTintStrength { get; private set; }

    /// <summary>HUD가 읽는 적용 중인 효과 한 건의 사본입니다.</summary>
    public readonly struct ActiveEffect
    {
        public ActiveEffect(StatusEffectDefinitionSO definition, float remaining, int stacks)
        {
            Definition = definition;
            Remaining = remaining;
            Stacks = stacks;
        }

        /// <summary>효과 정의입니다.</summary>
        public StatusEffectDefinitionSO Definition { get; }

        /// <summary>남은 시간(초)입니다.</summary>
        public float Remaining { get; }

        /// <summary>중첩 수입니다.</summary>
        public int Stacks { get; }
    }

    /// <summary>
    /// 적용 중인 효과를 처음 받은 순서대로 <paramref name="results"/>에 채웁니다. 목록은 먼저 비웁니다.
    /// </summary>
    /// <remarks>
    /// 같은 효과를 다시 받아 지속 시간만 갱신되면 순서는 바뀌지 않습니다(목록 위치를 유지하기 때문입니다).
    /// 출처별로 따로 쌓이는 효과(<see cref="StatusEffectStackPolicy.IndependentBySource"/>)는 건마다 들어가므로,
    /// 한 칸으로 보여 줄 쪽에서 정의 기준으로 합칩니다.
    /// </remarks>
    public void GetActiveEffects(List<ActiveEffect> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        float now = Time.time;
        for (int i = 0; i < m_entries.Count; i++)
        {
            Entry entry = m_entries[i];
            if (entry.Definition == null)
            {
                continue;
            }

            results.Add(new ActiveEffect(entry.Definition, Mathf.Max(0.0f, entry.ExpireTime - now), entry.Stacks));
        }
    }

    /// <summary>효과 추가·갱신·중첩·제거 시 UI가 목록을 다시 읽도록 알립니다.</summary>
    public event Action<StatusEffectContainer> OnChanged;

    private void Awake()
    {
        m_modelTintPropertyBlock = new MaterialPropertyBlock();
        ResolveOwner();
        CacheModelTintTargets();
    }

    private void OnEnable()
    {
        RefreshModelTint();
    }

    private void OnDisable()
    {
        RestoreModelTint();
    }

    private void Update()
    {
        if (m_entries.Count == 0 && m_oneShotVisuals.Count == 0)
        {
            return;
        }

        ResolveOwner();
        if (m_damageable != null && m_damageable.IsDead)
        {
            Clear();
            return;
        }

        float now = Time.time;
        bool changed = false;
        bool visualChanged = false;
        for (int i = m_entries.Count - 1; i >= 0; i--)
        {
            Entry entry = m_entries[i];
            ApplyExpiredTicks(entry, now);

            if (now >= entry.ExpireTime || (m_damageable != null && m_damageable.IsDead))
            {
                RecycleVisual(entry.Definition != null ? entry.Definition.RecipientVisual : null, entry.VisualInstance);
                m_entries.RemoveAt(i);
                changed = true;
            }
        }

        for (int i = m_oneShotVisuals.Count - 1; i >= 0; i--)
        {
            OneShotVisual visual = m_oneShotVisuals[i];
            if (now < visual.ExpireTime && (m_damageable == null || !m_damageable.IsDead))
            {
                continue;
            }

            RecycleVisual(visual.Definition, visual.Instance);
            m_oneShotVisuals.RemoveAt(i);
            visualChanged = true;
        }

        if (changed || visualChanged)
        {
            if (changed)
            {
                RecalculateModifiers();
            }

            RefreshModelTint();
            OnChanged?.Invoke(this);
        }
    }

    /// <summary>효과를 추가하거나 정의된 중첩 규칙에 따라 기존 효과를 갱신합니다.</summary>
    /// <param name="cause">
    /// 이 효과를 건 공격의 정보입니다. 주기 피해가 있는 효과(화상 등)는 이 값을 공격자·원래 공격으로 남깁니다.
    /// 비우면 <paramref name="source"/>를 공격자로 하고 공격 방식은 모름으로 남깁니다.
    /// 같은 효과를 다른 공격이 다시 걸면 마지막에 건 공격으로 바뀝니다. 출처별로 따로 쌓는 효과도 마찬가지입니다.
    /// </param>
    public bool Apply(StatusEffectDefinitionSO definition, Faction sourceFaction, GameObject source = null, DamageInfo cause = default)
    {
        if (!cause.IsValid)
        {
            cause = DamageInfo.Unknown(0.0f, source);
        }

        if (definition == null)
        {
            return false;
        }

        ResolveOwner();
        if (m_damageable != null && m_damageable.IsDead)
        {
            return false;
        }

        m_lastCauses[definition] = cause;
        float now = Time.time;
        Entry entry = FindEntry(definition, source);
        if (entry == null)
        {
            entry = new Entry
            {
                Definition = definition,
                SourceFaction = sourceFaction,
                Source = source,
                Stacks = 1,
                ExpireTime = now + definition.Duration,
                NextTickTime = now + definition.TickInterval,
                VisualSequence = ++m_nextVisualSequence,
            };
            entry.VisualInstance = SpawnVisual(definition.RecipientVisual);
            m_entries.Add(entry);
        }
        else
        {
            entry.SourceFaction = sourceFaction;
            entry.Source = source;
            entry.ExpireTime = now + definition.Duration;
            entry.VisualSequence = ++m_nextVisualSequence;
            if (definition.StackPolicy == StatusEffectStackPolicy.StackIntensity)
            {
                entry.Stacks = Mathf.Min(definition.MaxStacks, entry.Stacks + 1);
            }

            if (entry.VisualInstance == null)
            {
                entry.VisualInstance = SpawnVisual(definition.RecipientVisual);
            }
            else if (definition.RecipientVisual != null && definition.RecipientVisual.RestartOnStatusRefresh)
            {
                FeedbackPlaybackUtility.RestartPlayback(entry.VisualInstance);
            }
        }

        RecalculateModifiers();
        RefreshModelTint();
        OnChanged?.Invoke(this);
        return true;
    }

    public bool Has(StatusEffectDefinitionSO definition)
    {
        return definition != null && m_entries.Exists(entry => entry.Definition == definition);
    }

    public int GetStacks(StatusEffectDefinitionSO definition)
    {
        int stacks = 0;
        for (int i = 0; i < m_entries.Count; i++)
        {
            if (m_entries[i].Definition == definition)
            {
                stacks += m_entries[i].Stacks;
            }
        }

        return stacks;
    }

    public float GetRemaining(StatusEffectDefinitionSO definition)
    {
        float remaining = 0.0f;
        for (int i = 0; i < m_entries.Count; i++)
        {
            if (m_entries[i].Definition == definition)
            {
                remaining = Mathf.Max(remaining, m_entries[i].ExpireTime - Time.time);
            }
        }

        return Mathf.Max(0.0f, remaining);
    }

    public bool Remove(StatusEffectDefinitionSO definition)
    {
        int removed = 0;
        for (int i = m_entries.Count - 1; i >= 0; i--)
        {
            Entry entry = m_entries[i];
            if (entry.Definition != definition)
            {
                continue;
            }

            RecycleVisual(definition != null ? definition.RecipientVisual : null, entry.VisualInstance);
            m_entries.RemoveAt(i);
            removed++;
        }

        if (removed <= 0)
        {
            return false;
        }

        RecalculateModifiers();
        RefreshModelTint();
        OnChanged?.Invoke(this);
        return true;
    }

    public void Clear()
    {
        if (m_entries.Count == 0 && m_oneShotVisuals.Count == 0)
        {
            RestoreModelTint();
            return;
        }

        for (int i = 0; i < m_entries.Count; i++)
        {
            Entry entry = m_entries[i];
            RecycleVisual(entry.Definition != null ? entry.Definition.RecipientVisual : null, entry.VisualInstance);
        }

        for (int i = 0; i < m_oneShotVisuals.Count; i++)
        {
            RecycleVisual(m_oneShotVisuals[i].Definition, m_oneShotVisuals[i].Instance);
        }

        m_entries.Clear();
        m_oneShotVisuals.Clear();
        RecalculateModifiers();
        RefreshModelTint();
        OnChanged?.Invoke(this);
    }

    /// <summary>Plays a pooled body visual without adding a persistent gameplay status.</summary>
    public GameObject PlayVisual(StatusEffectVisualSO visual, float lifetime = -1.0f)
    {
        GameObject instance = SpawnVisual(visual);
        if (instance == null)
        {
            return null;
        }

        m_oneShotVisuals.Add(new OneShotVisual
        {
            Key = null,
            Definition = visual,
            Instance = instance,
            ExpireTime = Time.time + (lifetime > 0.0f ? lifetime : visual.OneShotLifetime),
            VisualSequence = ++m_nextVisualSequence,
        });
        RefreshModelTint();
        return instance;
    }

    /// <summary>
    /// Plays or refreshes a keyed body visual. Reapplying the same buff extends one instance
    /// instead of stacking duplicate visuals.
    /// </summary>
    public GameObject PlayOrRefreshVisual(
        UnityEngine.Object key,
        StatusEffectVisualSO visual,
        float lifetime = -1.0f)
    {
        if (key == null)
        {
            return PlayVisual(visual, lifetime);
        }

        float resolvedLifetime = lifetime > 0.0f
            ? lifetime
            : visual != null ? visual.OneShotLifetime : 0.0f;

        for (int i = 0; i < m_oneShotVisuals.Count; i++)
        {
            OneShotVisual active = m_oneShotVisuals[i];
            if (active.Key != key)
            {
                continue;
            }

            active.ExpireTime = Time.time + resolvedLifetime;
            active.VisualSequence = ++m_nextVisualSequence;
            if (active.Definition == visual && active.Instance != null)
            {
                if (visual != null && visual.RestartOnStatusRefresh)
                {
                    FeedbackPlaybackUtility.RestartPlayback(active.Instance);
                }

                RefreshModelTint();
                return active.Instance;
            }

            RecycleVisual(active.Definition, active.Instance);
            active.Definition = visual;
            active.Instance = SpawnVisual(visual);
            RefreshModelTint();
            return active.Instance;
        }

        GameObject instance = SpawnVisual(visual);
        if (instance == null)
        {
            return null;
        }

        m_oneShotVisuals.Add(new OneShotVisual
        {
            Key = key,
            Definition = visual,
            Instance = instance,
            ExpireTime = Time.time + resolvedLifetime,
            VisualSequence = ++m_nextVisualSequence,
        });
        RefreshModelTint();
        return instance;
    }

    /// <summary>Stops and pools every transient body visual registered with the supplied key.</summary>
    public bool StopVisual(UnityEngine.Object key)
    {
        if (key == null)
        {
            return false;
        }

        bool stopped = false;
        for (int i = m_oneShotVisuals.Count - 1; i >= 0; i--)
        {
            OneShotVisual active = m_oneShotVisuals[i];
            if (active.Key != key)
            {
                continue;
            }

            RecycleVisual(active.Definition, active.Instance);
            m_oneShotVisuals.RemoveAt(i);
            stopped = true;
        }

        if (stopped)
        {
            RefreshModelTint();
        }

        return stopped;
    }

    /// <summary>효과를 받을 GameObject에서 컨테이너를 찾고, 없으면 런타임에 하나 추가합니다.</summary>
    public static StatusEffectContainer GetOrAdd(GameObject owner)
    {
        if (owner == null)
        {
            return null;
        }

        StatusEffectContainer container = owner.GetComponent<StatusEffectContainer>();
        return container != null ? container : owner.AddComponent<StatusEffectContainer>();
    }

    /// <summary>피해 대상 컴포넌트가 있는 GameObject에 컨테이너를 찾거나 추가합니다.</summary>
    public static StatusEffectContainer GetOrAdd(IDamageable target)
    {
        Component component = target as Component;
        return component != null ? GetOrAdd(component.gameObject) : null;
    }

    /// <summary>이 효과를 마지막에 건 공격의 정보입니다. 기록이 없으면 그 쌓임의 출처를 공격자로 한 모름 정보입니다.</summary>
    private DamageInfo GetLastCause(StatusEffectDefinitionSO definition, Entry entry)
    {
        return m_lastCauses.TryGetValue(definition, out DamageInfo cause)
            ? cause
            : DamageInfo.Unknown(0.0f, entry.Source);
    }

    private Entry FindEntry(StatusEffectDefinitionSO definition, GameObject source)
    {
        for (int i = 0; i < m_entries.Count; i++)
        {
            Entry entry = m_entries[i];
            if (entry.Definition != definition)
            {
                continue;
            }

            if (definition.StackPolicy != StatusEffectStackPolicy.IndependentBySource || entry.Source == source)
            {
                return entry;
            }
        }

        return null;
    }

    private void ApplyExpiredTicks(Entry entry, float now)
    {
        StatusEffectDefinitionSO definition = entry.Definition;
        if (definition == null || !definition.HasPeriodicDamage || m_damageable == null)
        {
            return;
        }

        while (entry.NextTickTime <= now && entry.NextTickTime <= entry.ExpireTime)
        {
            int damage = definition.ResolvePeriodicDamage(
                definition.PeriodicDamage * Mathf.Max(1, entry.Stacks),
                m_damageable.Faction);

            // 감소율 100%로 0이 되면 이번 틱은 건너뛰고 다음 틱 시각만 넘깁니다.
            if (damage > 0
                && !CombatDamage.TryApplyDamage(m_damageable, entry.SourceFaction, GetLastCause(definition, entry).ToStatusEffect(damage, definition.name)))
            {
                break;
            }

            entry.NextTickTime += definition.TickInterval;
        }
    }

    private void RecalculateModifiers()
    {
        float movement = 1.0f;
        float action = 1.0f;
        for (int i = 0; i < m_entries.Count; i++)
        {
            Entry entry = m_entries[i];
            if (entry.Definition == null)
            {
                continue;
            }

            movement *= Mathf.Pow(entry.Definition.MovementSpeedMultiplier, entry.Stacks);
            action *= Mathf.Pow(entry.Definition.ActionSpeedMultiplier, entry.Stacks);
        }

        MovementSpeedMultiplier = Mathf.Max(0.01f, movement);
        ActionSpeedMultiplier = Mathf.Max(0.01f, action);

        if (m_squadMember != null)
        {
            m_squadMember.SetSpeedPercent(MovementSpeedMultiplier * 100.0f, ActionSpeedMultiplier * 100.0f);
        }
    }

    private void CacheModelTintTargets()
    {
        if (m_modelTintPropertyBlock == null)
        {
            m_modelTintPropertyBlock = new MaterialPropertyBlock();
        }

        m_modelTintTargets.Clear();
        Transform root = m_modelTintRoot != null ? m_modelTintRoot : transform;
        Renderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (renderers.Length == 0)
        {
            renderers = root.GetComponentsInChildren<MeshRenderer>(true);
        }

        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            if (renderer == null)
            {
                continue;
            }

            Material[] materials = renderer.sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null)
                {
                    continue;
                }

                bool hasBaseColor = material.HasProperty(BaseColorProperty);
                bool hasColor = material.HasProperty(ColorProperty);
                if (!hasBaseColor && !hasColor)
                {
                    continue;
                }

                renderer.GetPropertyBlock(m_modelTintPropertyBlock, materialIndex);
                m_modelTintTargets.Add(new ModelTintTarget
                {
                    Renderer = renderer,
                    MaterialIndex = materialIndex,
                    HasBaseColor = hasBaseColor,
                    HasColor = hasColor,
                    OriginalBaseColor = hasBaseColor && m_modelTintPropertyBlock.HasColor(BaseColorProperty)
                        ? m_modelTintPropertyBlock.GetColor(BaseColorProperty)
                        : hasBaseColor ? material.GetColor(BaseColorProperty) : Color.white,
                    OriginalColor = hasColor && m_modelTintPropertyBlock.HasColor(ColorProperty)
                        ? m_modelTintPropertyBlock.GetColor(ColorProperty)
                        : hasColor ? material.GetColor(ColorProperty) : Color.white,
                });
            }
        }
    }

    private void RefreshModelTint()
    {
        StatusEffectVisualSO latestVisual = null;
        long latestSequence = long.MinValue;

        for (int i = 0; i < m_entries.Count; i++)
        {
            StatusEffectVisualSO visual = m_entries[i].Definition != null
                ? m_entries[i].Definition.RecipientVisual
                : null;
            if (visual != null && visual.ApplyModelTint &&
                m_entries[i].VisualSequence > latestSequence)
            {
                latestVisual = visual;
                latestSequence = m_entries[i].VisualSequence;
            }
        }

        for (int i = 0; i < m_oneShotVisuals.Count; i++)
        {
            OneShotVisual active = m_oneShotVisuals[i];
            if (active.Definition != null && active.Definition.ApplyModelTint &&
                active.VisualSequence > latestSequence)
            {
                latestVisual = active.Definition;
                latestSequence = active.VisualSequence;
            }
        }

        if (latestVisual == null)
        {
            RestoreModelTint();
            return;
        }

        if (m_modelTintTargets.Count == 0)
        {
            CacheModelTintTargets();
        }

        Color tint = latestVisual.ModelTintColor;
        float strength = latestVisual.ModelTintStrength;

        HasActiveModelTint = true;
        CurrentModelTintColor = tint;
        CurrentModelTintStrength = strength;

        for (int i = 0; i < m_modelTintTargets.Count; i++)
        {
            ModelTintTarget target = m_modelTintTargets[i];
            if (target.Renderer == null)
            {
                continue;
            }

            target.Renderer.GetPropertyBlock(m_modelTintPropertyBlock, target.MaterialIndex);
            if (target.HasBaseColor)
            {
                Color targetColor = tint;
                targetColor.a = target.OriginalBaseColor.a;
                m_modelTintPropertyBlock.SetColor(
                    BaseColorProperty,
                    Color.Lerp(target.OriginalBaseColor, targetColor, strength));
            }

            if (target.HasColor)
            {
                Color targetColor = tint;
                targetColor.a = target.OriginalColor.a;
                m_modelTintPropertyBlock.SetColor(
                    ColorProperty,
                    Color.Lerp(target.OriginalColor, targetColor, strength));
            }

            target.Renderer.SetPropertyBlock(m_modelTintPropertyBlock, target.MaterialIndex);
        }
    }

    private void RestoreModelTint()
    {
        for (int i = 0; i < m_modelTintTargets.Count; i++)
        {
            ModelTintTarget target = m_modelTintTargets[i];
            if (target.Renderer == null)
            {
                continue;
            }

            target.Renderer.GetPropertyBlock(m_modelTintPropertyBlock, target.MaterialIndex);
            if (target.HasBaseColor)
            {
                m_modelTintPropertyBlock.SetColor(BaseColorProperty, target.OriginalBaseColor);
            }

            if (target.HasColor)
            {
                m_modelTintPropertyBlock.SetColor(ColorProperty, target.OriginalColor);
            }

            target.Renderer.SetPropertyBlock(m_modelTintPropertyBlock, target.MaterialIndex);
        }

        HasActiveModelTint = false;
        CurrentModelTintColor = Color.white;
        CurrentModelTintStrength = 0.0f;
    }

    private void ResolveOwner()
    {
        if (m_damageable == null)
        {
            m_damageable = GetComponent<IDamageable>()
                ?? GetComponentInChildren<IDamageable>(true)
                ?? GetComponentInParent<IDamageable>();
        }

        if (m_squadMember == null)
        {
            m_squadMember = GetComponent<SquadMemberController>()
                ?? GetComponentInParent<SquadMemberController>();
        }
    }

    private GameObject SpawnVisual(StatusEffectVisualSO visual)
    {
        GameObject prefab = visual != null ? visual.Prefab : null;
        if (prefab == null)
        {
            return null;
        }

        GameObject instance = TakeVisual(prefab);
        if (instance == null)
        {
            instance = Instantiate(prefab);
        }

        Transform instanceTransform = instance.transform;
        instanceTransform.SetParent(ResolveEffectAnchor(), false);
        instanceTransform.localPosition = visual.LocalPosition;
        instanceTransform.localRotation = visual.LocalRotation;
        instanceTransform.localScale = Vector3.Scale(prefab.transform.localScale, visual.LocalScaleMultiplier);
        instance.SetActive(true);
        FeedbackPlaybackUtility.RestartPlayback(instance);
        return instance;
    }

    private GameObject TakeVisual(GameObject prefab)
    {
        if (!m_visualPool.TryGetValue(prefab, out Stack<GameObject> idle))
        {
            return null;
        }

        while (idle.Count > 0)
        {
            GameObject instance = idle.Pop();
            if (instance != null)
            {
                return instance;
            }
        }

        return null;
    }

    private void RecycleVisual(StatusEffectVisualSO visual, GameObject instance)
    {
        GameObject prefab = visual != null ? visual.Prefab : null;
        if (prefab == null || instance == null)
        {
            return;
        }

        instance.SetActive(false);
        instance.transform.SetParent(ResolveVisualPoolRoot(), false);
        if (!m_visualPool.TryGetValue(prefab, out Stack<GameObject> idle))
        {
            idle = new Stack<GameObject>();
            m_visualPool.Add(prefab, idle);
        }

        idle.Push(instance);
    }

    private Transform ResolveEffectAnchor()
    {
        if (m_effectAnchor != null)
        {
            return m_effectAnchor;
        }

        if (m_runtimeEffectAnchor != null)
        {
            return m_runtimeEffectAnchor;
        }

        GameObject anchorObject = new GameObject("StatusEffectAnchor");
        m_runtimeEffectAnchor = anchorObject.transform;
        m_runtimeEffectAnchor.SetParent(transform, false);

        Renderer[] renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (renderers.Length == 0)
        {
            renderers = GetComponentsInChildren<MeshRenderer>(true);
        }

        if (renderers.Length == 0)
        {
            return m_runtimeEffectAnchor;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        m_runtimeEffectAnchor.position = bounds.center;
        return m_runtimeEffectAnchor;
    }

    private Transform ResolveVisualPoolRoot()
    {
        if (m_visualPoolRoot != null)
        {
            return m_visualPoolRoot;
        }

        GameObject root = new GameObject("StatusEffectVisualPool(Idle)");
        m_visualPoolRoot = root.transform;
        m_visualPoolRoot.SetParent(transform, false);
        root.SetActive(false);
        return m_visualPoolRoot;
    }
}
