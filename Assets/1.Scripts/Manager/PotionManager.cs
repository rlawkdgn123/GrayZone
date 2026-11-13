using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 회복약 사용을 담당합니다. 키를 누르면 스쿼드 인벤토리에서 회복약을 1개 소모하고 조작 중인 대원 한 명만 즉시 회복합니다.
/// </summary>
/// <remarks>
/// 회복약 수량은 <see cref="SquadInventoryManager"/>가 소유합니다. 이 매니저는 소모와 회복, 쿨타임만 맡습니다.
/// 회복할 필요가 없거나(체력 가득, 다운, 사망) 쿨타임 중이면 회복약을 소모하지 않습니다.
/// 쿨타임은 0이면 없음입니다. 값만 바꾸면 바로 적용되도록 시스템은 갖춰 둡니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class PotionManager : MonoBehaviour
{
    [Header("Item")]
    [Tooltip("회복약 아이템 정의 ID입니다. 스쿼드 인벤토리에서 이 ID의 수량을 소모합니다.")]
    [SerializeField] private string m_itemDefinitionId = "Potion_01";

    [Header("Effect")]
    [Tooltip("회복약 1개로 회복하는 체력입니다.")]
    [Min(1)]
    [SerializeField] private int m_healAmount = 30;

    [Tooltip("사용 후 다시 쓸 수 있을 때까지의 시간(초)입니다. 0이면 쿨타임이 없습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_cooldownSeconds;

    [Tooltip("켜면 체력이 가득 찬 대원은 회복약을 쓰지 않습니다(수량이 줄지 않습니다).")]
    [SerializeField] private bool m_skipWhenFullHealth = true;

    [Tooltip("회복할 때 대원 주변에 재생할 연출입니다. 비어 있으면 Resources의 TeamHeal 연출을 씁니다.")]
    [SerializeField] private StatusEffectVisualSO m_healVisual;

    [Header("Input")]
    [Tooltip("회복약을 사용하는 키입니다.")]
    [SerializeField] private Key m_useKey = Key.X;

    [Header("References")]
    [Tooltip("조작 중인 대원을 제공하는 SquadManager입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private SquadManager m_squadManager;

    [Tooltip("회복약 수량을 가진 스쿼드 인벤토리입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private SquadInventoryManager m_inventory;

    private float m_nextReadyTime;
    private bool m_keyHeld;

    /// <summary>회복약을 사용했을 때 발생합니다. 인자는 회복한 대원입니다.</summary>
    public event Action<SquadMemberController> OnUsed;

    /// <summary>
    /// 켜면 회복약이 없어도 쓸 수 있고 수량도 줄지 않습니다. 디버그 트레이너(F9) 전용입니다. 체력·쿨타임 조건은 그대로 적용합니다.
    /// </summary>
    public bool DebugInfinitePotions { get; set; }

    /// <summary>회복약 사용 키입니다. HUD 표시에 씁니다.</summary>
    public Key UseKey => m_useKey;

    /// <summary>스쿼드 인벤토리의 회복약 수량입니다.</summary>
    public int Count
    {
        get
        {
            ResolveReferences();
            return m_inventory != null ? m_inventory.CountOf(m_itemDefinitionId) : 0;
        }
    }

    /// <summary>전체 쿨타임(초)입니다. 0이면 쿨타임이 없습니다.</summary>
    public float CooldownDuration => Mathf.Max(0.0f, m_cooldownSeconds);

    /// <summary>남은 쿨타임(초)입니다.</summary>
    public float CooldownRemaining => Mathf.Max(0.0f, m_nextReadyTime - Time.time);

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || m_useKey == Key.None)
        {
            return;
        }

        // 눌림 상태가 바뀌는 순간을 직접 잡습니다. 에디터에서 Game 뷰 포커스에 따라 입력이 다른 갱신 단계에서
        // 처리되면 wasPressedThisFrame이 빠질 수 있어서입니다. 길게 눌러도 한 번만 씁니다.
        bool isDown = keyboard[m_useKey].isPressed;
        bool pressed = isDown && !m_keyHeld;
        m_keyHeld = isDown;
        if (pressed && Time.timeScale > 0.0f && IsInputAllowed())
        {
            TryUse();
        }
    }

    /// <summary>조작 중인 대원에게 회복약을 사용합니다. 실제로 회복했을 때만 true입니다.</summary>
    public bool TryUse()
    {
        ResolveReferences();
        SquadMemberController member = m_squadManager != null ? m_squadManager.PlayerSquadMember : null;
        PlayerHealth health = member != null ? member.GetComponent<PlayerHealth>() : null;
        if (health == null || health.IsDead || health.IsDowned || CooldownRemaining > 0.0f)
        {
            return false;
        }

        if (m_inventory == null && !DebugInfinitePotions)
        {
            return false;
        }

        if (m_skipWhenFullHealth && health.CurrentHP >= health.MaxHP)
        {
            return false;
        }

        // 회복이 가능한 상태를 먼저 확인한 뒤 소모합니다. 반대로 하면 실패할 때 회복약만 사라집니다.
        if (!DebugInfinitePotions && !m_inventory.TryConsumeOne(m_itemDefinitionId))
        {
            return false;
        }

        health.Heal(m_healAmount);
        m_nextReadyTime = Time.time + CooldownDuration;

        StatusEffectVisualSO visual = m_healVisual != null
            ? m_healVisual
            : Resources.Load<StatusEffectVisualSO>("StatusEffectVisuals/TeamHeal");
        if (visual != null && member.StatusEffects != null)
        {
            member.StatusEffects.PlayVisual(visual);
        }

        OnUsed?.Invoke(member);
        return true;
    }

    /// <summary>조작 대원의 입력이 막혀 있으면(컷신, 메뉴, 튜토리얼 등) 쓰지 않습니다.</summary>
    private bool IsInputAllowed()
    {
        SquadMemberController member = m_squadManager != null ? m_squadManager.PlayerSquadMember : null;
        PlayerInputController input = member != null ? member.GetComponent<PlayerInputController>() : null;
        return input != null && input.IsInputEnabled && !PlayerInputController.IsInputLocked(PlayerInputLock.Potion);
    }

    private void ResolveReferences()
    {
        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>(FindObjectsInactive.Include);
        }

        if (m_inventory == null)
        {
            m_inventory = FindFirstObjectByType<SquadInventoryManager>(FindObjectsInactive.Include);
        }
    }
}
