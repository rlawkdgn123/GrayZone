using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 트리거 하나의 완료 조건을 셉니다. 트리거가 실행될 때 만들어 세기 시작하고, 그 트리거를 닫는 엔드포인트가 닫히면 <see cref="Dispose"/>합니다.
/// </summary>
/// <remarks>
/// 트리거와 엔드포인트 사이에 대사가 있어도 그동안의 처치·상호작용을 셀 수 있도록, 조건은 트리거 시점부터 셉니다.
/// 열린 트리거가 여러 개면 감시자도 여러 개가 함께 세고, 엔드포인트는 자기가 닫는 트리거의 감시자가 모두 채워질 때까지 기다립니다.
/// </remarks>
public sealed class TutorialConditionWatcher : IDisposable
{
    private readonly TutorialBlock m_trigger;
    private readonly float m_startTime;
    private readonly Dictionary<EnemyHealth, Action> m_deathHandlers = new();

    private int m_killed;
    private bool m_interacted;

    /// <summary>대상 ID로 대상을 찾지 못했는지 여부입니다. 튜토리얼이 멈추지 않도록 경고 후 조건을 채운 것으로 봅니다.</summary>
    private bool m_targetMissing;

    private bool m_disposed;

    /// <summary>트리거의 완료 조건을 세기 시작합니다.</summary>
    /// <param name="trigger">완료 조건을 가진 트리거 블록입니다.</param>
    /// <param name="context">경고 로그를 눌렀을 때 선택할 오브젝트입니다.</param>
    public TutorialConditionWatcher(TutorialBlock trigger, UnityEngine.Object context)
    {
        m_trigger = trigger;
        m_startTime = Time.unscaledTime;

        switch (trigger.Condition)
        {
            case TutorialConditionType.KillEnemy:
                EnemyHealth[] enemies = UnityEngine.Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
                for (int i = 0; i < enemies.Length; i++)
                {
                    WatchEnemy(enemies[i]);
                }

                // 세는 동안 새로 나오는(스폰되거나 풀에서 꺼내지는) 적도 셉니다.
                EnemyHealth.OnEnemyEnabled += WatchEnemy;
                break;

            case TutorialConditionType.Interact:
                if (!string.IsNullOrEmpty(trigger.TargetId) && TutorialTarget.Find(trigger.TargetId) == null)
                {
                    Debug.LogWarning($"튜토리얼 대상 '{trigger.TargetId}'를 찾지 못해 상호작용 조건을 건너뜁니다. 씬 오브젝트에 같은 ID의 {nameof(TutorialTarget)}이 있는지 확인하세요.", context);
                    m_targetMissing = true;
                }

                InteractionController.OnAnyInteracted += HandleInteracted;
                break;
        }
    }

    /// <summary>조건을 채웠는지 여부입니다.</summary>
    public bool IsMet
    {
        get
        {
            switch (m_trigger.Condition)
            {
                case TutorialConditionType.None:
                    return true;

                case TutorialConditionType.Time:
                    return Time.unscaledTime - m_startTime >= m_trigger.Seconds;

                case TutorialConditionType.KillEnemy:
                    return m_killed >= m_trigger.KillCount;

                case TutorialConditionType.ReachArea:
                    return TutorialEvents.IsPlayerInArea(m_trigger.AreaCenter, m_trigger.AreaRadius);

                case TutorialConditionType.Interact:
                    return m_interacted || m_targetMissing;

                default:
                    return true;
            }
        }
    }

    /// <summary>걸어 둔 알림을 모두 뗍니다.</summary>
    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        m_disposed = true;
        EnemyHealth.OnEnemyEnabled -= WatchEnemy;
        InteractionController.OnAnyInteracted -= HandleInteracted;
        foreach (KeyValuePair<EnemyHealth, Action> pair in m_deathHandlers)
        {
            if (pair.Key != null)
            {
                pair.Key.OnDeath -= pair.Value;
            }
        }

        m_deathHandlers.Clear();
    }

    private void WatchEnemy(EnemyHealth enemy)
    {
        if (enemy == null || m_deathHandlers.ContainsKey(enemy) || !TutorialEvents.IsFromPrefab(enemy, m_trigger.EnemyPrefab))
        {
            return;
        }

        Action handler = () => m_killed++;
        enemy.OnDeath += handler;
        m_deathHandlers[enemy] = handler;
    }

    private void HandleInteracted(IInteractable interactable, GameObject interactor)
    {
        string id = m_trigger.TargetId;
        if (string.IsNullOrEmpty(id))
        {
            m_interacted = true;
            return;
        }

        TutorialTarget target = interactable is Component component ? component.GetComponentInParent<TutorialTarget>() : null;
        if (target != null && target.Id == id)
        {
            m_interacted = true;
        }
    }
}
