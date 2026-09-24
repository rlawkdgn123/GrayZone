using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// G 투척 모드에서 현재 선택된 투척물 아이콘을 표시합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class GrenadeSelectionUI : MonoBehaviour
{
    [System.Serializable]
    private sealed class IconBinding
    {
        [Tooltip("아이콘을 연결할 투척물 Prefab입니다.")]
        [SerializeField] private ExplosiveProjectile m_projectilePrefab;

        [Tooltip("해당 투척물이 선택됐을 때 표시할 Sprite입니다.")]
        [SerializeField] private Sprite m_icon;

        public ExplosiveProjectile ProjectilePrefab => m_projectilePrefab;
        public Sprite Icon => m_icon;
    }

    [Tooltip("G 투척 모드에서만 활성화할 Canvas입니다. 비어 있으면 자식에서 자동으로 찾습니다.")]
    [SerializeField] private Canvas m_canvas;

    [Tooltip("현재 선택된 투척물 아이콘을 표시할 Image입니다. 비어 있으면 자식에서 자동으로 찾습니다.")]
    [SerializeField] private Image m_selectedIcon;

    [Tooltip("투척물 Prefab과 UI 아이콘 Sprite의 대응 목록입니다.")]
    [SerializeField] private List<IconBinding> m_iconBindings = new List<IconBinding>();

    private ExplosiveProjectileShooter m_owner;
    private ExplosiveProjectile m_displayedProjectile;

    private void Awake()
    {
        ResolveReferences();
        SetVisible(false);
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    /// <summary>
    /// 요청한 Shooter가 투척 모드이면 UI를 표시하고 선택된 투척물 아이콘으로 갱신합니다.
    /// </summary>
    public void SetState(
        ExplosiveProjectileShooter owner,
        bool visible,
        ExplosiveProjectile selectedProjectile)
    {
        if (owner == null)
        {
            return;
        }

        if (!visible)
        {
            if (m_owner != null && m_owner != owner)
            {
                return;
            }

            m_owner = null;
            m_displayedProjectile = null;
            SetVisible(false);
            return;
        }

        m_owner = owner;
        SetVisible(true);

        if (m_displayedProjectile == selectedProjectile)
        {
            return;
        }

        m_displayedProjectile = selectedProjectile;
        RefreshIcon(selectedProjectile);
    }

    private void ResolveReferences()
    {
        if (m_canvas == null)
        {
            m_canvas = GetComponentInChildren<Canvas>(true);
        }

        if (m_selectedIcon == null)
        {
            m_selectedIcon = GetComponentInChildren<Image>(true);
        }
    }

    private void SetVisible(bool visible)
    {
        ResolveReferences();
        if (m_canvas != null && m_canvas.gameObject.activeSelf != visible)
        {
            m_canvas.gameObject.SetActive(visible);
        }
    }

    private void RefreshIcon(ExplosiveProjectile projectile)
    {
        if (m_selectedIcon == null)
        {
            return;
        }

        Sprite icon = FindIcon(projectile);
        m_selectedIcon.sprite = icon;
        m_selectedIcon.preserveAspect = true;
        m_selectedIcon.enabled = icon != null;
    }

    private Sprite FindIcon(ExplosiveProjectile projectile)
    {
        if (projectile == null || m_iconBindings == null)
        {
            return null;
        }

        for (int i = 0; i < m_iconBindings.Count; i++)
        {
            IconBinding binding = m_iconBindings[i];
            if (binding != null && binding.ProjectilePrefab == projectile)
            {
                return binding.Icon;
            }
        }

        return null;
    }
}
