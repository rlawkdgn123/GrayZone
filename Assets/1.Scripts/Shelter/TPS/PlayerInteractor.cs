using System;
using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PlayerInteractor : MonoBehaviour
{
    [Header("Target Filter")]
    [SerializeField] private string m_targetLayer = "ShelterFacility";

    [Header("UI")]
    [SerializeField] private UIManager m_uiManager;

    // Other systems can subscribe later without changing the trigger logic.
    public event Action<GameObject> TargetChanged;

    private readonly List<GameObject> m_interactionTargets = new List<GameObject>();
    private GameObject m_currentTarget;
    private int m_targetLayerIndex = -1;

    public IReadOnlyList<GameObject> InteractionTargets => m_interactionTargets;
    public GameObject CurrentTarget => m_currentTarget;

    private void Reset()
    {
        // Make the required trigger setup automatic when the script is added.
        ApplyRequiredComponentSettings();
        AutoFindUIManager();
    }

    private void OnValidate()
    {
        // Keep Inspector string changes reflected in the cached layer index.
        CacheTargetLayer();
        ApplyRequiredComponentSettings();
    }

    private void Awake()
    {
        // Cache once at runtime so target checks are cheap.
        CacheTargetLayer();
        ApplyRequiredComponentSettings();
        AutoFindUIManager();

        if (!string.IsNullOrEmpty(m_targetLayer) && m_targetLayerIndex < 0)
            Debug.LogWarning($"[Interactor] Layer not found : {m_targetLayer}", this);
    }

    private void Update()
    {
        // Destroyed or disabled objects may not always send OnTriggerExit.
        RemoveInvalidTargets();

        // The closest valid target can change while the player moves.
        RefreshCurrentTarget();

        // Interaction is allowed only when a current target exists.
        HandleInteractInput();
    }

    private void OnTriggerEnter(Collider other)
    {
        GameObject target = ResolveInteractionTarget(other);

        if (target == null)
            return;

        // Prevent duplicate registration from repeated trigger messages.
        if (m_interactionTargets.Contains(target))
            return;

        m_interactionTargets.Add(target);

        RefreshCurrentTarget();
    }

    private void OnTriggerExit(Collider other)
    {
        GameObject target = FindRegisteredTarget(other);

        if (target == null)
            return;

        m_interactionTargets.Remove(target);

        RefreshCurrentTarget();
    }

    private void HandleInteractInput()
    {
        if (m_currentTarget == null)
            return;

        if (WasInteractPressed())
            InteractWithCurrentTarget();
    }

    private bool WasInteractPressed()
    {
#if ENABLE_INPUT_SYSTEM
        // Unity 6 project setting currently uses the new Input System.
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            return true;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        // Legacy fallback for projects that enable both input backends.
        if (Input.GetKeyDown(KeyCode.E))
            return true;
#endif

        return false;
    }

    private void InteractWithCurrentTarget()
    {
        // 실행은 대상에게 위임한다. UI 여는 시설(FacilityUIInteractable), 수면(SleepInteractable),
        // 문/작업대 등 모두 IInteractable을 구현하므로 여기서는 타입을 몰라도 된다.
        IInteractable interactable = ResolveInteractable(m_currentTarget);

        if (interactable != null)
        {
            interactable.Interact(gameObject);
            return;
        }

        Debug.Log($"Interact : {m_currentTarget.name}", m_currentTarget);
    }

    private IInteractable ResolveInteractable(GameObject target)
    {
        if (target == null)
            return null;

        // 규칙: IInteractable은 감지되는 콜라이더 오브젝트에 붙인다.
        // "콜라이더는 자식, 로직은 루트" 구조를 위해 부모까지만 탐색한다(자신 포함).
        // 자식 탐색은 중첩 상호작용에서 엉뚱한 대상을 잡을 수 있어 제외한다.
        return target.GetComponentInParent<IInteractable>();
    }

    private void RefreshCurrentTarget()
    {
        GameObject closestTarget = FindClosestTarget();

        if (closestTarget == m_currentTarget)
            return;

        m_currentTarget = closestTarget;

        NotifyTargetChanged(m_currentTarget);
    }

    private GameObject FindClosestTarget()
    {
        GameObject closestTarget = null;
        float closestDistanceSqr = float.PositiveInfinity;
        Vector3 origin = transform.position;

        // Squared distance avoids an unnecessary square root every frame.
        for (int i = 0; i < m_interactionTargets.Count; i++)
        {
            GameObject target = m_interactionTargets[i];

            if (!IsValidRegisteredTarget(target))
                continue;

            float distanceSqr = (target.transform.position - origin).sqrMagnitude;

            if (distanceSqr >= closestDistanceSqr)
                continue;

            closestDistanceSqr = distanceSqr;
            closestTarget = target;
        }

        return closestTarget;
    }

    private void NotifyTargetChanged(GameObject target)
    {
        // Notify code listeners first, then pass the same target to the temporary UI.
        TargetChanged?.Invoke(target);

        if (m_uiManager != null)
        {
            m_uiManager.SetInteractionTarget(target);
            return;
        }

        // Fallback log keeps testing visible even when a UIManager is not in the scene yet.
        if (target != null)
            Debug.Log($"[UI] Show Interaction : {target.name}", target);
        else
            Debug.Log("[UI] Hide Interaction", this);
    }

    private GameObject ResolveInteractionTarget(Collider other)
    {
        if (other == null)
            return null;

        // First check the collider object itself.
        if (IsInteractionTarget(other.gameObject))
            return other.gameObject;

        // If the collider belongs to a Rigidbody root, allow the root to be layered.
        if (other.attachedRigidbody != null && IsInteractionTarget(other.attachedRigidbody.gameObject))
            return other.attachedRigidbody.gameObject;

        return null;
    }

    private GameObject FindRegisteredTarget(Collider other)
    {
        GameObject resolvedTarget = ResolveInteractionTarget(other);

        if (resolvedTarget != null && m_interactionTargets.Contains(resolvedTarget))
            return resolvedTarget;

        // If a target changed layer while inside the trigger, still remove it.
        if (other != null && m_interactionTargets.Contains(other.gameObject))
            return other.gameObject;

        if (other != null && other.attachedRigidbody != null)
        {
            GameObject rigidbodyObject = other.attachedRigidbody.gameObject;

            if (m_interactionTargets.Contains(rigidbodyObject))
                return rigidbodyObject;
        }

        return null;
    }

    private bool IsInteractionTarget(GameObject candidate)
    {
        if (candidate == null)
            return false;

        // The configured layer qualifies the object as interactable.
        return IsTargetLayer(candidate);
    }

    private bool IsTargetLayer(GameObject candidate)
    {
        return m_targetLayerIndex >= 0 && candidate.layer == m_targetLayerIndex;
    }

    private bool IsValidRegisteredTarget(GameObject target)
    {
        return target != null && target.activeInHierarchy && IsInteractionTarget(target);
    }

    private void RemoveInvalidTargets()
    {
        for (int i = m_interactionTargets.Count - 1; i >= 0; i--)
        {
            if (IsValidRegisteredTarget(m_interactionTargets[i]))
                continue;

            m_interactionTargets.RemoveAt(i);
        }
    }

    private void CacheTargetLayer()
    {
        m_targetLayerIndex = string.IsNullOrEmpty(m_targetLayer) ? -1 : LayerMask.NameToLayer(m_targetLayer);
    }

    private void AutoFindUIManager()
    {
        if (m_uiManager != null)
            return;

        m_uiManager = FindFirstObjectByType<UIManager>();
    }

    private void ApplyRequiredComponentSettings()
    {
        SphereCollider sphereCollider = GetComponent<SphereCollider>();

        if (sphereCollider != null)
            sphereCollider.isTrigger = true;

        Rigidbody rigidbody = GetComponent<Rigidbody>();

        if (rigidbody == null)
            return;

        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;
    }
}
