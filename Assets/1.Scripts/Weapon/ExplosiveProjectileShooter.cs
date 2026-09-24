using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

/// <summary>
/// 플레이어의 투척 모드와 좌클릭 입력을 받아 폭발탄 경로를 표시하고 투척합니다.
/// </summary>
public class ExplosiveProjectileShooter : MonoBehaviour
{
    private const int MaxTrajectoryPointCount = 65;
    private const int ExplosionPreviewSegmentCount = 48;
    private const float ExplosionPreviewHeightOffset = 0.03f;

    [System.Serializable]
    private sealed class CrosshairPreset
    {
        [Tooltip("무기 탄퍼짐에 따라 조준선 간격을 변경할지 여부입니다.")]
        [SerializeField] private bool m_useSpreadAccuracy;

        [Tooltip("중앙 표시 형태입니다.")]
        [SerializeField] private CrosshairController.MainShape m_mainShape = CrosshairController.MainShape.Ring;

        [Tooltip("중앙점 크기입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_mainSizePixels = 3.0f;

        [Tooltip("중앙 링 지름입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_mainRingSizePixels = 14.0f;

        [Tooltip("중앙 링 두께입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_mainRingThicknessPixels = 2.0f;

        [Tooltip("중앙 표시 색상입니다.")]
        [SerializeField] private Color m_mainColor = new Color(1.0f, 1.0f, 1.0f, 0.27450982f);

        [Tooltip("중앙 표시 외곽선 두께입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_mainStrokeThicknessPixels = 1.0f;

        [Tooltip("중앙 표시 외곽선 색상입니다.")]
        [SerializeField] private Color m_mainStrokeColor = new Color(0.0f, 0.0f, 0.0f, 0.27450982f);

        [Tooltip("보조 표시 형태입니다.")]
        [SerializeField] private CrosshairController.SubShape m_subShape = CrosshairController.SubShape.RoundedCross;

        [Tooltip("중앙과 보조 표시 사이의 간격입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_centerSpacePixels = 12.0f;

        [Tooltip("보조 점 크기입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_subSizePixels = 2.0f;

        [Tooltip("보조 십자선 길이입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_subWidthPixels = 7.0f;

        [Tooltip("보조 십자선 두께입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_subThicknessPixels = 2.0f;

        [Tooltip("보조 링 지름입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_subRingSizePixels = 20.0f;

        [Tooltip("보조 링 두께입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_subRingThicknessPixels = 2.0f;

        [Tooltip("보조 표시 색상입니다.")]
        [SerializeField] private Color m_subColor = new Color(1.0f, 1.0f, 1.0f, 0.27450982f);

        [Tooltip("보조 표시 외곽선 두께입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_subStrokeThicknessPixels = 1.0f;

        [Tooltip("보조 표시 외곽선 색상입니다.")]
        [SerializeField] private Color m_subStrokeColor = new Color(0.0f, 0.0f, 0.0f, 0.27450982f);

        [Tooltip("Rounded Cross 모서리 반지름입니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_cornerRadiusPixels = 2.0f;

        public static CrosshairPreset Capture(CrosshairController crosshair)
        {
            return new CrosshairPreset
            {
                m_useSpreadAccuracy = crosshair.SpreadAccuracyEnabled,
                m_mainShape = crosshair.CurrentMainShape,
                m_mainSizePixels = crosshair.MainSizePixels,
                m_mainRingSizePixels = crosshair.MainRingSizePixels,
                m_mainRingThicknessPixels = crosshair.MainRingThicknessPixels,
                m_mainColor = crosshair.MainColor,
                m_mainStrokeThicknessPixels = crosshair.MainStrokeThicknessPixels,
                m_mainStrokeColor = crosshair.MainStrokeColor,
                m_subShape = crosshair.CurrentSubShape,
                m_centerSpacePixels = crosshair.CenterSpacePixels,
                m_subSizePixels = crosshair.SubSizePixels,
                m_subWidthPixels = crosshair.SubWidthPixels,
                m_subThicknessPixels = crosshair.SubThicknessPixels,
                m_subRingSizePixels = crosshair.SubRingSizePixels,
                m_subRingThicknessPixels = crosshair.SubRingThicknessPixels,
                m_subColor = crosshair.SubColor,
                m_subStrokeThicknessPixels = crosshair.SubStrokeThicknessPixels,
                m_subStrokeColor = crosshair.SubStrokeColor,
                m_cornerRadiusPixels = crosshair.CornerRadiusPixels,
            };
        }

        public void Apply(CrosshairController crosshair)
        {
            crosshair.SetSpreadAccuracyEnabled(m_useSpreadAccuracy);
            crosshair.CurrentMainShape = m_mainShape;
            crosshair.MainSizePixels = m_mainSizePixels;
            crosshair.MainRingSizePixels = m_mainRingSizePixels;
            crosshair.MainRingThicknessPixels = m_mainRingThicknessPixels;
            crosshair.MainColor = m_mainColor;
            crosshair.MainStrokeThicknessPixels = m_mainStrokeThicknessPixels;
            crosshair.MainStrokeColor = m_mainStrokeColor;
            crosshair.CurrentSubShape = m_subShape;
            crosshair.CenterSpacePixels = m_centerSpacePixels;
            crosshair.SubSizePixels = m_subSizePixels;
            crosshair.SubWidthPixels = m_subWidthPixels;
            crosshair.SubThicknessPixels = m_subThicknessPixels;
            crosshair.SubRingSizePixels = m_subRingSizePixels;
            crosshair.SubRingThicknessPixels = m_subRingThicknessPixels;
            crosshair.SubColor = m_subColor;
            crosshair.SubStrokeThicknessPixels = m_subStrokeThicknessPixels;
            crosshair.SubStrokeColor = m_subStrokeColor;
            crosshair.CornerRadiusPixels = m_cornerRadiusPixels;
        }
    }

    [Tooltip("Q/E 또는 마우스 휠로 순환 선택할 ExplosiveProjectile Prefab 목록입니다.")]
    [SerializeField] private List<ExplosiveProjectile> m_projectilePrefabs = new List<ExplosiveProjectile>();

    [UnityEngine.Serialization.FormerlySerializedAs("m_projectilePrefab")]
    [SerializeField, HideInInspector] private ExplosiveProjectile m_legacyProjectilePrefab;

    [Tooltip("현재 선택된 투척물 목록 인덱스입니다.")]
    [Min(0)]
    [SerializeField] private int m_selectedProjectileIndex;

    [Tooltip("G 투척 모드에서 현재 선택된 투척물 아이콘을 표시할 UI입니다. 비어 있으면 Scene에서 자동으로 찾습니다.")]
    [SerializeField] private GrenadeSelectionUI m_grenadeSelectionUI;

    [Tooltip("G 투척 모드에서 수치 프리셋을 적용할 크로스헤어입니다. 비어 있으면 AimController 또는 Scene에서 자동으로 찾습니다.")]
    [SerializeField] private CrosshairController m_defaultCrosshair;

    [Tooltip("G 투척 모드에서 기존 크로스헤어에 임시로 적용할 수치 프리셋입니다.")]
    [SerializeField] private CrosshairPreset m_throwCrosshairPreset = new CrosshairPreset();

    [Tooltip("플레이어 Collider 중심을 기준으로 한 로컬 투척 시작 위치입니다.")]
    [SerializeField] private Vector3 m_throwOriginOffset = new Vector3(0.0f, 0.2f, 1.0f);

    [Tooltip("수평 조준 시 폭탄이 같은 높이로 돌아올 때의 기준 투척 거리입니다. 실제 비행 종료점은 아닙니다.")]
    [UnityEngine.Serialization.FormerlySerializedAs("m_maxThrowDistance")]
    [Min(0.1f)]
    [SerializeField] private float m_referenceThrowDistance = 20.0f;

    [Tooltip("수평 조준 시 투척 시작점보다 올라갈 기준 최고 높이입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_arcHeight = 1.5f;

    [Tooltip("포물선을 아래로 휘게 하는 스크립트 가속도입니다. Rigidbody 중력은 사용하지 않습니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_downwardAcceleration = 20.0f;

    [Tooltip("포물선의 거리와 높이는 유지하면서 실제 비행 속도만 조절합니다. 1은 기본 속도, 2는 두 배 속도입니다.")]
    [InspectorName("Throw Speed")]
    [Min(0.01f)]
    [SerializeField] private float m_throwSpeedMultiplier = 1.25f;

    [Tooltip("LineRenderer로 미리 보여 줄 포물선의 최대 누적 길이입니다. 실제 폭탄 이동은 제한하지 않습니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_trajectoryPreviewDistance = 20.0f;

    [Tooltip("한 번 투척한 뒤 다음 투척 경로를 표시하고 다시 던질 수 있을 때까지의 시간입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_throwCooldown = 1.0f;

    [Tooltip("경로 표시를 구성할 선분 수입니다.")]
    [Range(4, 64)]
    [SerializeField] private int m_trajectorySegments = 24;

    [Tooltip("이동 중 충돌을 검사할 구체 반지름입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_collisionRadius = 0.5f;

    [Tooltip("투척 경로 및 실제 이동 중 충돌을 검사할 Layer입니다.")]
    [SerializeField] private LayerMask m_collisionLayers = ~0;

    [Tooltip("경로 표시 선의 두께입니다.")]
    [Min(0.001f)]
    [SerializeField] private float m_trajectoryWidth = 0.04f;

    [Tooltip("경로 표시 선의 색상입니다.")]
    [SerializeField] private Color m_trajectoryColor = new Color(1.0f, 0.75f, 0.1f, 0.9f);

    private readonly RaycastHit[] m_previewHits = new RaycastHit[16];
    private readonly Vector3[] m_trajectoryPoints = new Vector3[MaxTrajectoryPointCount];

    private PlayerInputController m_input;
    private AimController m_aimController;
    private Collider m_sourceCollider;
    private LineRenderer m_trajectoryLine;
    private LineRenderer m_explosionPreviewLine;
    private Material m_runtimeLineMaterial;
    private bool m_wasThrowModeActive;
    private bool m_throwWasHeld;
    private Vector3 m_throwStart;
    private Vector3 m_initialVelocity;
    private bool m_hasPlannedCollision;
    private float m_plannedCollisionTime;
    private Vector3 m_plannedCollisionPosition;
    private bool m_hasExplosionPreview;
    private Vector3 m_explosionPreviewCenter;
    private int m_trajectoryPointCount;
    private float m_nextThrowReadyTime;
    private bool m_crosshairModeInitialized;
    private bool m_throwCrosshairActive;
    private CrosshairPreset m_savedCrosshairPreset;

    private void Awake()
    {
        MigrateLegacyProjectile();
        m_input = GetComponent<PlayerInputController>();
        m_aimController = GetComponent<AimController>();
        m_sourceCollider = GetComponent<Collider>();
        ResolveGrenadeSelectionUI();
        CreateTrajectoryLine();
        ApplyCrosshairMode(m_input != null && m_input.ThrowMode);
    }

    private void LateUpdate()
    {
        bool throwModeActive = m_input != null && m_input.ThrowMode;
        ApplyCrosshairMode(throwModeActive);

        if (m_input == null || m_aimController == null || !throwModeActive)
        {
            UpdateGrenadeSelectionUI(false);
            HideTrajectory();
            m_wasThrowModeActive = false;
            m_throwWasHeld = false;
            return;
        }

        CycleProjectile(m_input.ConsumeThrowSelectionDelta());
        UpdateGrenadeSelectionUI(true);
        bool throwHeld = m_input.Throw;

        if (m_input.Sprint)
        {
            HideTrajectory();
            m_wasThrowModeActive = true;
            m_throwWasHeld = throwHeld;
            return;
        }

        bool enteredThrowModeThisFrame = !m_wasThrowModeActive;
        bool canThrow = Time.time >= m_nextThrowReadyTime;

        if (canThrow)
        {
            ResolveTrajectory();
            BuildTrajectoryPlan();
            DrawTrajectory();

            if (!enteredThrowModeThisFrame && throwHeld && !m_throwWasHeld && ThrowProjectile())
            {
                m_nextThrowReadyTime = Time.time + m_throwCooldown;
                HideTrajectory();
            }
        }
        else
        {
            HideTrajectory();
        }

        m_wasThrowModeActive = true;
        m_throwWasHeld = throwHeld;
    }

    private void OnDisable()
    {
        UpdateGrenadeSelectionUI(false);
        ApplyCrosshairMode(false);
        m_crosshairModeInitialized = false;
        HideTrajectory();
        m_wasThrowModeActive = false;
        m_throwWasHeld = false;
    }

    private void OnDestroy()
    {
        if (m_runtimeLineMaterial != null)
        {
            Destroy(m_runtimeLineMaterial);
        }
    }

    private void OnValidate()
    {
        MigrateLegacyProjectile();
    }

    private void MigrateLegacyProjectile()
    {
        if (m_projectilePrefabs == null)
        {
            m_projectilePrefabs = new List<ExplosiveProjectile>();
        }

        if (m_projectilePrefabs.Count == 0 && m_legacyProjectilePrefab != null)
        {
            m_projectilePrefabs.Add(m_legacyProjectilePrefab);
            m_legacyProjectilePrefab = null;
        }

        m_selectedProjectileIndex = WrapIndex(m_selectedProjectileIndex, m_projectilePrefabs.Count);
    }

    private void CycleProjectile(int selectionDelta)
    {
        if (selectionDelta == 0 || m_projectilePrefabs == null || m_projectilePrefabs.Count == 0)
        {
            return;
        }

        m_selectedProjectileIndex = WrapIndex(
            m_selectedProjectileIndex + selectionDelta,
            m_projectilePrefabs.Count);
    }

    private ExplosiveProjectile GetSelectedProjectile()
    {
        if (m_projectilePrefabs == null || m_projectilePrefabs.Count == 0)
        {
            return m_legacyProjectilePrefab;
        }

        m_selectedProjectileIndex = WrapIndex(m_selectedProjectileIndex, m_projectilePrefabs.Count);
        return m_projectilePrefabs[m_selectedProjectileIndex];
    }

    private int GetProjectileCollisionLayers(ExplosiveProjectile projectile)
    {
        int excludedLayers = projectile != null
            ? projectile.ContactExplosionExcludeLayers.value
            : 0;
        return m_collisionLayers.value & ~excludedLayers;
    }

    private static int WrapIndex(int index, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        return (index % count + count) % count;
    }

    private void ResolveGrenadeSelectionUI()
    {
        if (m_grenadeSelectionUI == null)
        {
            m_grenadeSelectionUI = FindFirstObjectByType<GrenadeSelectionUI>(FindObjectsInactive.Include);
        }
    }

    private void UpdateGrenadeSelectionUI(bool visible)
    {
        ResolveGrenadeSelectionUI();
        if (m_grenadeSelectionUI != null)
        {
            m_grenadeSelectionUI.SetState(this, visible, GetSelectedProjectile());
        }
    }

    private void ApplyCrosshairMode(bool throwModeActive)
    {
        if (m_crosshairModeInitialized && m_throwCrosshairActive == throwModeActive)
        {
            return;
        }

        ResolveDefaultCrosshair();
        if (throwModeActive)
        {
            if (m_defaultCrosshair != null && m_throwCrosshairPreset != null)
            {
                m_savedCrosshairPreset = CrosshairPreset.Capture(m_defaultCrosshair);
                m_throwCrosshairPreset.Apply(m_defaultCrosshair);
            }
        }
        else if (m_defaultCrosshair != null && m_savedCrosshairPreset != null)
        {
            m_savedCrosshairPreset.Apply(m_defaultCrosshair);
            m_savedCrosshairPreset = null;
        }

        m_throwCrosshairActive = throwModeActive;
        m_crosshairModeInitialized = true;
    }

    private void ResolveDefaultCrosshair()
    {
        if (m_defaultCrosshair != null)
        {
            return;
        }

        if (m_aimController != null)
        {
            m_defaultCrosshair = m_aimController.CrosshairController;
        }

        if (m_defaultCrosshair == null)
        {
            m_defaultCrosshair = FindFirstObjectByType<CrosshairController>(FindObjectsInactive.Include);
        }
    }

    private void ResolveTrajectory()
    {
        Vector3 origin = m_sourceCollider != null ? m_sourceCollider.bounds.center : transform.position;
        m_throwStart = origin + transform.TransformDirection(m_throwOriginOffset);

        Vector3 aimDirection = m_aimController.CurrentAimPoint - m_throwStart;
        if (aimDirection.sqrMagnitude < 0.0001f)
        {
            aimDirection = transform.forward;
        }

        aimDirection.Normalize();

        Vector3 horizontalDirection = Vector3.ProjectOnPlane(aimDirection, Vector3.up);
        if (horizontalDirection.sqrMagnitude < 0.0001f)
        {
            horizontalDirection = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        }

        horizontalDirection.Normalize();

        float downwardAcceleration = Mathf.Max(0.01f, m_downwardAcceleration);
        float upwardSpeed = m_arcHeight > 0.0f
            ? Mathf.Sqrt(2.0f * downwardAcceleration * m_arcHeight)
            : 0.0f;
        float referenceFlightTime = upwardSpeed > 0.0f
            ? 2.0f * upwardSpeed / downwardAcceleration
            : 1.0f;
        float horizontalSpeed = m_referenceThrowDistance / referenceFlightTime;
        float launchSpeed = Mathf.Sqrt(horizontalSpeed * horizontalSpeed + upwardSpeed * upwardSpeed);
        float baseLaunchAngle = Mathf.Atan2(upwardSpeed, horizontalSpeed);
        float aimPitch = Mathf.Asin(Mathf.Clamp(aimDirection.y, -1.0f, 1.0f));
        float launchAngle = Mathf.Clamp(
            baseLaunchAngle + aimPitch,
            -80.0f * Mathf.Deg2Rad,
            80.0f * Mathf.Deg2Rad);

        m_initialVelocity =
            horizontalDirection * (Mathf.Cos(launchAngle) * launchSpeed) +
            Vector3.up * (Mathf.Sin(launchAngle) * launchSpeed);
    }

    private void DrawTrajectory()
    {
        m_trajectoryLine.enabled = true;
        m_trajectoryLine.positionCount = m_trajectoryPointCount;

        for (int i = 0; i < m_trajectoryPointCount; i++)
        {
            m_trajectoryLine.SetPosition(i, m_trajectoryPoints[i]);
        }

        DrawExplosionPreview();
    }

    private void BuildTrajectoryPlan()
    {
        int segmentCount = Mathf.Clamp(m_trajectorySegments, 4, MaxTrajectoryPointCount - 1);
        m_trajectoryPointCount = 1;
        m_trajectoryPoints[0] = m_throwStart;
        m_hasPlannedCollision = false;
        m_plannedCollisionTime = 0.0f;
        m_plannedCollisionPosition = m_throwStart;
        m_hasExplosionPreview = false;
        m_explosionPreviewCenter = m_throwStart;

        Vector3 previous = m_throwStart;
        float previousTime = 0.0f;
        float previewDistance = Mathf.Max(0.1f, m_trajectoryPreviewDistance);
        float targetSegmentLength = previewDistance / segmentCount;
        float accumulatedDistance = 0.0f;
        float throwSpeedMultiplier = Mathf.Max(0.01f, m_throwSpeedMultiplier);
        ExplosiveProjectile selectedProjectile = GetSelectedProjectile();
        float trajectoryTimeLimit = selectedProjectile != null
            ? Mathf.Max(0.0f, selectedProjectile.FuseTime) * throwSpeedMultiplier
            : float.PositiveInfinity;

        if (trajectoryTimeLimit <= 0.0f)
        {
            m_hasExplosionPreview = true;
            return;
        }

        for (int i = 1; i <= segmentCount; i++)
        {
            Vector3 currentVelocity = ParabolicProjectileMover.EvaluateVelocity(
                m_initialVelocity,
                m_downwardAcceleration,
                previousTime);
            float sampleInterval = Mathf.Clamp(
                targetSegmentLength / Mathf.Max(currentVelocity.magnitude, 1.0f),
                0.01f,
                0.25f);
            float currentTime = Mathf.Min(previousTime + sampleInterval, trajectoryTimeLimit);

            if (currentTime <= previousTime)
            {
                return;
            }

            Vector3 next = ParabolicProjectileMover.EvaluatePosition(
                m_throwStart,
                m_initialVelocity,
                m_downwardAcceleration,
                currentTime);
            Vector3 segment = next - previous;
            float segmentDistance = segment.magnitude;
            float remainingPreviewDistance = previewDistance - accumulatedDistance;
            bool reachedPreviewLimit = segmentDistance >= remainingPreviewDistance;

            if (reachedPreviewLimit && segmentDistance > 0.0001f)
            {
                float previewFraction = Mathf.Clamp01(remainingPreviewDistance / segmentDistance);
                currentTime = Mathf.Lerp(previousTime, currentTime, previewFraction);
                next = ParabolicProjectileMover.EvaluatePosition(
                    m_throwStart,
                    m_initialVelocity,
                    m_downwardAcceleration,
                    currentTime);
                segment = next - previous;
                segmentDistance = segment.magnitude;
            }

            if (TryGetBlockingHit(previous, next, out RaycastHit hit))
            {
                float hitFraction = segmentDistance > 0.0001f
                    ? Mathf.Clamp01(hit.distance / segmentDistance)
                    : 0.0f;

                m_hasPlannedCollision = true;
                m_plannedCollisionTime = Mathf.Lerp(previousTime, currentTime, hitFraction);
                m_plannedCollisionPosition = ParabolicProjectileMover.EvaluatePosition(
                    m_throwStart,
                    m_initialVelocity,
                    m_downwardAcceleration,
                    m_plannedCollisionTime);
                m_hasExplosionPreview = true;
                m_explosionPreviewCenter = m_plannedCollisionPosition;

                if ((m_plannedCollisionPosition - previous).sqrMagnitude > 0.000001f)
                {
                    m_trajectoryPoints[m_trajectoryPointCount] = m_plannedCollisionPosition;
                    m_trajectoryPointCount++;
                }

                return;
            }

            m_trajectoryPoints[m_trajectoryPointCount] = next;
            m_trajectoryPointCount++;
            accumulatedDistance += segmentDistance;

            if (currentTime >= trajectoryTimeLimit)
            {
                m_hasExplosionPreview = true;
                m_explosionPreviewCenter = next;
                return;
            }

            if (reachedPreviewLimit)
            {
                return;
            }

            previous = next;
            previousTime = currentTime;
        }
    }

    private bool ThrowProjectile()
    {
        ExplosiveProjectile selectedProjectile = GetSelectedProjectile();
        if (selectedProjectile == null)
        {
            Debug.LogWarning($"[{name}] 투척할 폭발 투사체 Prefab이 없습니다.", this);
            return false;
        }

        Quaternion rotation = m_initialVelocity.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(m_initialVelocity.normalized, Vector3.up)
            : transform.rotation;

        ExplosiveProjectile projectile = Instantiate(selectedProjectile, m_throwStart, rotation);
        Collider[] projectileColliders = projectile.GetComponentsInChildren<Collider>(true);
        foreach (Collider projectileCollider in projectileColliders)
        {
            projectileCollider.isTrigger = true;
        }

        Rigidbody projectileRigidbody = projectile.GetComponent<Rigidbody>();
        projectileRigidbody.useGravity = false;
        projectileRigidbody.isKinematic = true;
        projectileRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        projectileRigidbody.linearVelocity = Vector3.zero;
        projectileRigidbody.angularVelocity = Vector3.zero;

        ParabolicProjectileMover mover = projectile.gameObject.AddComponent<ParabolicProjectileMover>();
        mover.Initialize(
            projectile,
            m_throwStart,
            m_initialVelocity,
            m_downwardAcceleration,
            m_throwSpeedMultiplier,
            m_hasPlannedCollision,
            m_plannedCollisionTime,
            m_plannedCollisionPosition,
            m_collisionRadius,
            GetProjectileCollisionLayers(selectedProjectile),
            transform);

        return true;
    }

    private bool TryGetBlockingHit(Vector3 start, Vector3 end, out RaycastHit nearestHit)
    {
        nearestHit = default;
        Vector3 movement = end - start;
        float distance = movement.magnitude;

        if (distance <= 0.0001f)
        {
            return false;
        }

        int hitCount = Physics.SphereCastNonAlloc(
            start,
            m_collisionRadius,
            movement / distance,
            m_previewHits,
            distance,
            GetProjectileCollisionLayers(GetSelectedProjectile()),
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit candidate = m_previewHits[i];
            if (candidate.collider == null || IsOwnedByPlayer(candidate.collider.transform))
            {
                continue;
            }

            if (candidate.distance < nearestDistance)
            {
                nearestDistance = candidate.distance;
                nearestHit = candidate;
                found = true;
            }
        }

        return found;
    }

    private bool IsOwnedByPlayer(Transform hitTransform)
    {
        return hitTransform == transform || hitTransform.IsChildOf(transform);
    }

    private void CreateTrajectoryLine()
    {
        GameObject lineObject = new GameObject("ThrowTrajectoryPreview");
        lineObject.transform.SetParent(transform, false);

        m_trajectoryLine = lineObject.AddComponent<LineRenderer>();
        ConfigurePreviewLine(m_trajectoryLine, false);

        GameObject explosionPreviewObject = new GameObject("ExplosionRadiusPreview");
        explosionPreviewObject.transform.SetParent(transform, false);

        m_explosionPreviewLine = explosionPreviewObject.AddComponent<LineRenderer>();
        ConfigurePreviewLine(m_explosionPreviewLine, true);

        Shader lineShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (lineShader != null)
        {
            m_runtimeLineMaterial = new Material(lineShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            m_runtimeLineMaterial.SetColor("_BaseColor", m_trajectoryColor);
            m_trajectoryLine.sharedMaterial = m_runtimeLineMaterial;
            m_explosionPreviewLine.sharedMaterial = m_runtimeLineMaterial;
        }
    }

    private void ConfigurePreviewLine(LineRenderer lineRenderer, bool loop)
    {
        lineRenderer.useWorldSpace = true;
        lineRenderer.loop = loop;
        lineRenderer.widthMultiplier = m_trajectoryWidth;
        lineRenderer.startColor = m_trajectoryColor;
        lineRenderer.endColor = m_trajectoryColor;
        lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.enabled = false;
    }

    private void DrawExplosionPreview()
    {
        ExplosiveProjectile selectedProjectile = GetSelectedProjectile();
        if (!m_hasExplosionPreview || selectedProjectile == null)
        {
            m_explosionPreviewLine.enabled = false;
            m_explosionPreviewLine.positionCount = 0;
            return;
        }

        float radius = Mathf.Max(0.0f, selectedProjectile.ExplosionRadius);
        if (radius <= 0.0f)
        {
            m_explosionPreviewLine.enabled = false;
            m_explosionPreviewLine.positionCount = 0;
            return;
        }

        Vector3 center = m_explosionPreviewCenter + Vector3.up * ExplosionPreviewHeightOffset;
        m_explosionPreviewLine.enabled = true;
        m_explosionPreviewLine.positionCount = ExplosionPreviewSegmentCount;

        for (int i = 0; i < ExplosionPreviewSegmentCount; i++)
        {
            float angle = 2.0f * Mathf.PI * i / ExplosionPreviewSegmentCount;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)) * radius;
            m_explosionPreviewLine.SetPosition(i, center + offset);
        }
    }

    private void HideTrajectory()
    {
        if (m_trajectoryLine == null)
        {
            return;
        }

        m_trajectoryLine.enabled = false;
        m_trajectoryLine.positionCount = 0;

        if (m_explosionPreviewLine != null)
        {
            m_explosionPreviewLine.enabled = false;
            m_explosionPreviewLine.positionCount = 0;
        }
    }
}
