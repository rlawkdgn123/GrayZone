using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 한 지점에서 터져 범위 안의 대상에게 한 번씩 고정 피해를 주는 폭발 처리입니다.
/// </summary>
/// <remarks>
/// <see cref="ExplosiveProjectile"/>이 들고 있던 폭발 처리를 그대로 꺼내 둔 것입니다. 투척물이 아닌
/// 지뢰나 클레이모어 같은 설치물도 같은 폭발을 쓰게 하려고 분리했습니다. 상태를 갖지 않으므로
/// 언제 터질지와 터진 뒤 자기 자신을 어떻게 할지는 부르는 쪽이 정합니다.
///
/// 범위 모양이 둘입니다. <see cref="DetonateCylinder"/>는 사방으로 퍼지는 폭발이고,
/// <see cref="DetonateTrapezoid"/>는 앞쪽으로만 퍼져 나가는 지향성 폭발입니다.
///
/// 사방 폭발이 구가 아니라 원통인 이유는 높이 판정이 수평 판정과 따로 놀아야 하기 때문입니다.
/// 발밑에서 터진 폭발이 위층까지 닿으면 안 되고, 같은 층이라면 조금 높은 곳에 선 대상도 닿아야 합니다.
/// </remarks>
public static class ExplosionDamage
{
    private const float RangeVisualDuration = 1.0f;
    private const float RangeVisualAlpha = 0.2f;
    private const string TrapezoidMeshName = "ExplosionTrapezoidRange";
    private static readonly Color RangeVisualColor = new Color(1.0f, 0.35f, 0.05f, RangeVisualAlpha);

    /// <summary>차폐 검사 시작점을 폭발 중심에서 위로 띄우는 거리(m)입니다.</summary>
    /// <remarks>바닥이나 벽에 닿아 터지면 중심이 표면에 붙어 있어, 그대로 쓰면 선이 그 표면에 바로 걸립니다.</remarks>
    private const float OcclusionOriginLift = 0.2f;

    /// <summary>차폐 검사 지점을 콜라이더 위아래로 벌리는 비율입니다. 1이면 끝점까지 씁니다.</summary>
    private const float OcclusionSampleSpread = 0.8f;

    /// <summary>
    /// 지정한 지점을 중심으로 원통 범위 안의 대상에게 폭발 피해를 적용합니다.
    /// </summary>
    /// <param name="center">폭발 중심의 월드 좌표입니다.</param>
    /// <param name="radius">피해 원통의 수평 반지름(m)입니다.</param>
    /// <param name="height">피해 원통의 전체 높이(m)입니다. 중심을 기준으로 위아래 절반씩 적용됩니다.</param>
    /// <param name="damage">범위 안의 각 대상에게 한 번씩 적용할 고정 피해량입니다.</param>
    /// <param name="targetLayers">피해 후보로 검색할 Collider Layer입니다.</param>
    /// <param name="source">
    /// 이 폭발의 피해 정보 틀입니다. 공격 방식(투척물·함정 등), 세부 종류, 공격자를 담고, 피해량은 대상마다 채워 넣습니다.
    /// 피해를 받은 쪽이 반격할 상대와 사인을 아는 데 쓰입니다.
    /// </param>
    /// <param name="showRangeVisual">폭발 범위를 잠깐 그려 보여 줄지 여부입니다.</param>
    /// <param name="playerDamage">
    /// Player 진영 대상에게 적용할 피해입니다. 음수면 <paramref name="damage"/>와 같은 피해를 적용합니다.
    /// </param>
    /// <param name="onTargetDamaged">피해가 실제로 적용된 대상을 즉시 전달할 선택 콜백입니다.</param>
    /// <param name="occluderLayers">
    /// 폭발을 가로막는 벽으로 볼 Layer입니다. 비어 있으면 차폐를 보지 않고 범위 안 대상에게 모두 피해를 줍니다.
    /// </param>
    /// <returns>실제로 피해를 준 대상의 수입니다.</returns>
    public static int DetonateCylinder(
        Vector3 center,
        float radius,
        float height,
        int damage,
        LayerMask targetLayers,
        DamageInfo source = default,
        bool showRangeVisual = true,
        int playerDamage = -1,
        Action<IDamageable> onTargetDamaged = null,
        LayerMask occluderLayers = default)
    {
        float explosionRadius = Mathf.Max(0.0f, radius);
        float halfHeight = Mathf.Max(0.0f, height) * 0.5f;

        if (showRangeVisual)
        {
            ShowCylinderRangeVisual(center, explosionRadius, halfHeight * 2.0f);
        }

        if (explosionRadius <= 0.0f || halfHeight <= 0.0f)
        {
            return 0;
        }

        // Unity에는 원통 질의가 없어서 OverlapBox로 넉넉히 후보를 모은 뒤 모양으로 거릅니다.
        Collider[] colliders = Physics.OverlapBox(
            center,
            new Vector3(explosionRadius, halfHeight, explosionRadius),
            Quaternion.identity,
            targetLayers,
            QueryTriggerInteraction.Collide);

        Func<Collider, bool> isInsideRange =
            targetCollider => IntersectsCylinder(targetCollider, center, explosionRadius, halfHeight);
        int damagedCount = ApplyDamage(
            colliders,
            isInsideRange,
            damage,
            playerDamage >= 0 ? playerDamage : damage,
            source,
            onTargetDamaged,
            center,
            occluderLayers);

        TriggerChainDetonation(
            center,
            new Vector3(explosionRadius, halfHeight, explosionRadius),
            Quaternion.identity,
            isInsideRange,
            source.Attacker);

        return damagedCount;
    }

    /// <summary>
    /// 앞쪽으로 퍼져 나가는 사다리꼴 범위 안의 대상에게 폭발 피해를 적용합니다.
    /// </summary>
    /// <param name="origin">폭발이 시작되는 월드 좌표입니다. 사다리꼴의 짧은 변이 여기에 놓입니다.</param>
    /// <param name="rotation">폭발이 향하는 방향입니다. 이 회전의 +Z가 정면입니다.</param>
    /// <param name="range">정면으로 뻗는 거리(m)입니다.</param>
    /// <param name="nearWidth">폭발 지점 바로 앞의 폭(m)입니다.</param>
    /// <param name="farWidth">가장 먼 지점의 폭(m)입니다. 보통 <paramref name="nearWidth"/>보다 넓습니다.</param>
    /// <param name="height">피해 범위의 전체 높이(m)입니다. 폭발 지점을 기준으로 위아래 절반씩 적용됩니다.</param>
    /// <param name="damage">범위 안의 각 대상에게 한 번씩 적용할 고정 피해량입니다.</param>
    /// <param name="targetLayers">피해 후보로 검색할 Collider Layer입니다.</param>
    /// <param name="source">이 폭발의 피해 정보 틀입니다. 공격 방식, 세부 종류, 공격자를 담고, 피해량은 대상마다 채워 넣습니다.</param>
    /// <param name="showRangeVisual">폭발 범위를 잠깐 그려 보여 줄지 여부입니다.</param>
    /// <returns>실제로 피해를 준 대상의 수입니다.</returns>
    /// <remarks>
    /// 폭이 앞으로 갈수록 넓어지므로 부채꼴과 비슷하지만, 각도가 아니라 두 폭으로 정의합니다.
    /// 각도로 두면 사거리를 늘릴 때마다 끝 폭이 같이 변해 기획 수치를 잡기 어렵습니다.
    /// 뒤쪽(-Z)에는 아무 피해도 주지 않습니다. 설치한 사람이 등 뒤에서 안전해야 하기 때문입니다.
    /// </remarks>
    public static int DetonateTrapezoid(
        Vector3 origin,
        Quaternion rotation,
        float range,
        float nearWidth,
        float farWidth,
        float height,
        int damage,
        LayerMask targetLayers,
        DamageInfo source = default,
        bool showRangeVisual = true)
    {
        float explosionRange = Mathf.Max(0.0f, range);
        float halfNearWidth = Mathf.Max(0.0f, nearWidth) * 0.5f;
        float halfFarWidth = Mathf.Max(0.0f, farWidth) * 0.5f;
        float halfHeight = Mathf.Max(0.0f, height) * 0.5f;

        if (showRangeVisual)
        {
            ShowTrapezoidRangeVisual(
                origin,
                rotation,
                explosionRange,
                halfNearWidth,
                halfFarWidth,
                halfHeight);
        }

        float halfMaxWidth = Mathf.Max(halfNearWidth, halfFarWidth);
        if (explosionRange <= 0.0f || halfMaxWidth <= 0.0f || halfHeight <= 0.0f)
        {
            return 0;
        }

        // 사다리꼴을 감싸는 최소 상자로 후보를 모읍니다. 상자는 정면 절반만큼 앞으로 밀려 있습니다.
        Vector3 boxCenter = origin + rotation * new Vector3(0.0f, 0.0f, explosionRange * 0.5f);
        Collider[] colliders = Physics.OverlapBox(
            boxCenter,
            new Vector3(halfMaxWidth, halfHeight, explosionRange * 0.5f),
            rotation,
            targetLayers,
            QueryTriggerInteraction.Collide);

        Quaternion inverseRotation = Quaternion.Inverse(rotation);

        Func<Collider, bool> isInsideRange = targetCollider => IntersectsTrapezoid(
            targetCollider,
            origin,
            rotation,
            inverseRotation,
            explosionRange,
            halfNearWidth,
            halfFarWidth,
            halfHeight);
        int damagedCount = ApplyDamage(colliders, isInsideRange, damage, damage, source, null, origin, default);

        TriggerChainDetonation(
            boxCenter,
            new Vector3(halfMaxWidth, halfHeight, explosionRange * 0.5f),
            rotation,
            isInsideRange,
            source.Attacker);

        return damagedCount;
    }

    /// <summary>
    /// 범위 안에 있는 <see cref="IChainDetonatable"/>을 찾아 함께 터지도록 알립니다.
    /// </summary>
    /// <param name="boxCenter">후보를 모을 상자의 중심입니다.</param>
    /// <param name="boxHalfExtents">후보를 모을 상자의 절반 크기입니다.</param>
    /// <param name="boxRotation">후보를 모을 상자의 회전입니다.</param>
    /// <param name="isInsideRange">상자로 모은 후보 중 실제 범위 안에 든 것만 고르는 판정입니다.</param>
    /// <param name="source">연쇄를 일으킨 대상입니다. 모르면 <c>null</c>입니다.</param>
    /// <returns>이번 호출로 터지기 시작한 대상의 수입니다.</returns>
    /// <remarks>
    /// 피해 대상 레이어와 따로 모든 레이어를 봅니다. 함정은 Trap 레이어에 있어 피해 대상(Enemy)에 들지
    /// 않고, 나중에 붙을 드럼통 같은 소품은 또 다른 레이어에 있을 수 있기 때문입니다.
    /// 폭발뿐 아니라 화염(<see cref="DragonBreathEffect"/>)도 이 메서드로 연쇄를 일으킵니다.
    ///
    /// 후보를 다 모은 뒤에 한꺼번에 알립니다. 받는 쪽이 그 자리에서 터지면 이 메서드가 다시 불리므로,
    /// 모으는 도중에 부르면 순서가 꼬이기 쉽습니다.
    /// </remarks>
    public static int TriggerChainDetonation(
        Vector3 boxCenter,
        Vector3 boxHalfExtents,
        Quaternion boxRotation,
        Func<Collider, bool> isInsideRange,
        GameObject source)
    {
        Collider[] colliders = Physics.OverlapBox(
            boxCenter,
            boxHalfExtents,
            boxRotation,
            Physics.AllLayers,
            QueryTriggerInteraction.Collide);

        List<IChainDetonatable> targets = new List<IChainDetonatable>();
        HashSet<IChainDetonatable> seenTargets = new HashSet<IChainDetonatable>();

        foreach (Collider candidate in colliders)
        {
            if (candidate == null || !isInsideRange(candidate))
            {
                continue;
            }

            IChainDetonatable target = candidate.GetComponentInParent<IChainDetonatable>();
            if (target != null && seenTargets.Add(target))
            {
                targets.Add(target);
            }
        }

        int detonatedCount = 0;
        foreach (IChainDetonatable target in targets)
        {
            if (target.TryChainDetonate(source))
            {
                detonatedCount++;
            }
        }

        return detonatedCount;
    }

    /// <summary>범위 판정을 통과한 대상에게 한 번씩 피해를 넣습니다.</summary>
    /// <remarks>
    /// 같은 대상에 붙은 여러 콜라이더(몸통과 히트박스)가 함께 잡혀도 피해는 한 번만 들어갑니다.
    /// 벽에 가려진 콜라이더는 처리한 것으로 치지 않습니다. 같은 대상의 다른 콜라이더가 보이면 그쪽으로 맞습니다.
    /// </remarks>
    private static int ApplyDamage(
        Collider[] colliders,
        Func<Collider, bool> isInsideRange,
        int damage,
        int playerDamage,
        DamageInfo source,
        Action<IDamageable> onTargetDamaged,
        Vector3 explosionOrigin,
        LayerMask occluderLayers)
    {
        HashSet<IDamageable> damagedTargets = new HashSet<IDamageable>();
        int damagedCount = 0;

        foreach (Collider targetCollider in colliders)
        {
            if (!isInsideRange(targetCollider))
            {
                continue;
            }

            IDamageable target = targetCollider.GetComponentInParent<IDamageable>();
            if (target == null || damagedTargets.Contains(target))
            {
                continue;
            }

            if (occluderLayers.value != 0
                && IsOccluded(explosionOrigin, targetCollider, target, occluderLayers))
            {
                continue;
            }

            damagedTargets.Add(target);

            int targetDamage = target.Faction == Faction.Player
                ? playerDamage
                : damage;

            if (targetDamage <= 0)
            {
                continue;
            }

            if (!target.TakeDamage(source.WithAmount(targetDamage)))
            {
                continue;
            }

            onTargetDamaged?.Invoke(target);
            damagedCount++;
        }

        return damagedCount;
    }

    /// <summary>폭발 지점에서 대상 콜라이더까지 벽이 가로막고 있는지 판정합니다.</summary>
    /// <remarks>
    /// 콜라이더의 위·가운데·아래 세 점으로 선을 긋고, 하나라도 뚫려 있으면 보이는 것으로 칩니다.
    /// 가운데 한 점만 보면 낮은 엄폐물 뒤에 머리를 내민 대상이 통째로 빠집니다.
    /// 선이 처음 닿은 것이 대상 자신의 일부(벽 레이어에 놓인 장비 등)라면 대상까지 닿은 것으로 봅니다.
    /// </remarks>
    private static bool IsOccluded(
        Vector3 explosionOrigin,
        Collider targetCollider,
        IDamageable target,
        LayerMask occluderLayers)
    {
        Vector3 origin = explosionOrigin + Vector3.up * OcclusionOriginLift;
        Bounds bounds = targetCollider.bounds;
        Vector3 verticalOffset = Vector3.up * (bounds.extents.y * OcclusionSampleSpread);

        return IsLineBlocked(origin, bounds.center, target, occluderLayers)
            && IsLineBlocked(origin, bounds.center + verticalOffset, target, occluderLayers)
            && IsLineBlocked(origin, bounds.center - verticalOffset, target, occluderLayers);
    }

    /// <summary>두 점 사이를 벽 레이어가 막는지 판정합니다. 막은 것이 대상 자신이면 막히지 않은 것으로 봅니다.</summary>
    private static bool IsLineBlocked(
        Vector3 from,
        Vector3 to,
        IDamageable target,
        LayerMask occluderLayers)
    {
        if (!Physics.Linecast(from, to, out RaycastHit hit, occluderLayers, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        return hit.collider.GetComponentInParent<IDamageable>() != target;
    }

    /// <summary>콜라이더가 폭발 원통과 겹치는지 판정합니다.</summary>
    /// <remarks>
    /// 원통 축에서 가장 가까운 점을 먼저 구하고, 그 점을 콜라이더 표면으로 당겨 높이와 수평 거리를
    /// 따로 봅니다. 중심 좌표만 비교하면 큰 콜라이더가 범위에 걸쳐 있을 때 빠집니다.
    /// </remarks>
    private static bool IntersectsCylinder(
        Collider targetCollider,
        Vector3 center,
        float radius,
        float halfHeight)
    {
        float minY = center.y - halfHeight;
        float maxY = center.y + halfHeight;
        float axisY = Mathf.Clamp(targetCollider.bounds.center.y, minY, maxY);
        Vector3 axisPoint = new Vector3(center.x, axisY, center.z);
        Vector3 closestPoint = targetCollider.ClosestPoint(axisPoint);

        if (closestPoint.y < minY || closestPoint.y > maxY)
        {
            return false;
        }

        Vector2 horizontalOffset = new Vector2(
            closestPoint.x - center.x,
            closestPoint.z - center.z);
        return horizontalOffset.sqrMagnitude <= radius * radius;
    }

    /// <summary>콜라이더가 전방 사다리꼴 범위와 겹치는지 판정합니다.</summary>
    /// <remarks>
    /// 원통과 같은 방식입니다. 사다리꼴의 중심선 위에서 가장 가까운 점을 잡고, 그 점을 향해 콜라이더
    /// 표면을 당긴 뒤 폭발 기준 좌표계에서 앞뒤·좌우·위아래를 따로 봅니다. 좌우 허용치는 앞으로
    /// 갈수록 넓어지므로 거리에 따라 보간해서 구합니다.
    /// </remarks>
    private static bool IntersectsTrapezoid(
        Collider targetCollider,
        Vector3 origin,
        Quaternion rotation,
        Quaternion inverseRotation,
        float range,
        float halfNearWidth,
        float halfFarWidth,
        float halfHeight)
    {
        Vector3 localBoundsCenter = inverseRotation * (targetCollider.bounds.center - origin);
        Vector3 localAxisPoint = new Vector3(
            0.0f,
            Mathf.Clamp(localBoundsCenter.y, -halfHeight, halfHeight),
            Mathf.Clamp(localBoundsCenter.z, 0.0f, range));

        Vector3 axisPoint = origin + rotation * localAxisPoint;
        Vector3 localClosest = inverseRotation * (targetCollider.ClosestPoint(axisPoint) - origin);

        if (localClosest.z < 0.0f || localClosest.z > range)
        {
            return false;
        }

        if (localClosest.y < -halfHeight || localClosest.y > halfHeight)
        {
            return false;
        }

        float halfWidthAtDistance = Mathf.Lerp(halfNearWidth, halfFarWidth, localClosest.z / range);
        return Mathf.Abs(localClosest.x) <= halfWidthAtDistance;
    }

    /// <summary>
    /// 폭발 원통 범위를 보여 줄 반투명 오브젝트를 만듭니다. 스스로 사라지지 않습니다.
    /// </summary>
    /// <remarks>
    /// 터지는 순간에만 잠깐 보이는 것으로는 수치를 맞추기 어려워서, 계속 띄워 둘 수 있게 분리했습니다.
    /// 다 쓴 뒤에는 반드시 <see cref="DestroyRangeVisual"/>로 지워야 머테리얼이 남지 않습니다.
    /// </remarks>
    /// <returns>만들어진 표시용 오브젝트입니다. 수치가 0이거나 셰이더를 찾지 못하면 <c>null</c>입니다.</returns>
    public static GameObject CreateCylinderRangeVisual(Vector3 center, float radius, float height)
    {
        float visualRadius = Mathf.Max(0.0f, radius);
        float visualHeight = Mathf.Max(0.0f, height);

        if (visualRadius <= 0.0f || visualHeight <= 0.0f)
        {
            return null;
        }

        Material rangeMaterial = CreateRangeMaterial();
        if (rangeMaterial == null)
        {
            return null;
        }

        GameObject rangeVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rangeVisual.name = "ExplosionDamageRangeVisual";
        rangeVisual.hideFlags = HideFlags.HideAndDontSave;
        rangeVisual.transform.position = center;
        rangeVisual.transform.rotation = Quaternion.identity;

        // 기본 실린더 메시는 높이가 2라서 Y 스케일에 절반을 넣어야 원하는 높이가 나옵니다.
        rangeVisual.transform.localScale = new Vector3(
            visualRadius * 2.0f,
            visualHeight * 0.5f,
            visualRadius * 2.0f);

        Collider rangeCollider = rangeVisual.GetComponent<Collider>();
        rangeCollider.enabled = false;
        DestroyObject(rangeCollider);

        ApplyRangeVisual(rangeVisual, rangeMaterial);
        return rangeVisual;
    }

    /// <summary>
    /// 전방 사다리꼴 범위를 보여 줄 반투명 오브젝트를 만듭니다. 스스로 사라지지 않습니다.
    /// </summary>
    /// <remarks>
    /// 기본 도형에는 사다리꼴 기둥이 없어서 그때그때 메시를 만듭니다. 반투명이라 안쪽도 보이는 편이
    /// 나아서 앞뒤 양면을 모두 그립니다. 다 쓴 뒤에는 <see cref="DestroyRangeVisual"/>로 지웁니다.
    /// </remarks>
    /// <returns>만들어진 표시용 오브젝트입니다. 수치가 0이거나 셰이더를 찾지 못하면 <c>null</c>입니다.</returns>
    public static GameObject CreateTrapezoidRangeVisual(
        Vector3 origin,
        Quaternion rotation,
        float range,
        float nearWidth,
        float farWidth,
        float height)
    {
        float visualRange = Mathf.Max(0.0f, range);
        float halfNearWidth = Mathf.Max(0.0f, nearWidth) * 0.5f;
        float halfFarWidth = Mathf.Max(0.0f, farWidth) * 0.5f;
        float halfHeight = Mathf.Max(0.0f, height) * 0.5f;

        if (visualRange <= 0.0f || halfHeight <= 0.0f || Mathf.Max(halfNearWidth, halfFarWidth) <= 0.0f)
        {
            return null;
        }

        Material rangeMaterial = CreateRangeMaterial();
        if (rangeMaterial == null)
        {
            return null;
        }

        GameObject rangeVisual = new GameObject("ExplosionDamageRangeVisual")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        rangeVisual.transform.SetPositionAndRotation(origin, rotation);

        Mesh mesh = BuildTrapezoidMesh(visualRange, halfNearWidth, halfFarWidth, halfHeight);
        mesh.hideFlags = HideFlags.HideAndDontSave;
        rangeVisual.AddComponent<MeshFilter>().sharedMesh = mesh;
        rangeVisual.AddComponent<MeshRenderer>();

        ApplyRangeVisual(rangeVisual, rangeMaterial);
        return rangeVisual;
    }

    /// <summary>
    /// 표시용 오브젝트를 지웁니다. 함께 만들어 둔 머테리얼과 메시도 같이 버립니다.
    /// </summary>
    /// <param name="rangeVisual">지울 표시용 오브젝트입니다. <c>null</c>이면 아무 일도 하지 않습니다.</param>
    /// <param name="delay">지우기까지 기다릴 시간(초)입니다.</param>
    /// <remarks>
    /// 머테리얼은 표시용으로 새로 만든 것이라 오브젝트만 지우면 남습니다. 메시는 사다리꼴처럼 직접
    /// 만든 것만 지웁니다. 실린더는 Unity 기본 메시를 공유해 쓰므로 지우면 다른 곳까지 깨집니다.
    /// </remarks>
    public static void DestroyRangeVisual(GameObject rangeVisual, float delay = 0.0f)
    {
        if (rangeVisual == null)
        {
            return;
        }

        Renderer rangeRenderer = rangeVisual.GetComponent<Renderer>();
        if (rangeRenderer != null && rangeRenderer.sharedMaterial != null)
        {
            DestroyObject(rangeRenderer.sharedMaterial, delay);
        }

        MeshFilter meshFilter = rangeVisual.GetComponent<MeshFilter>();
        if (meshFilter != null
            && meshFilter.sharedMesh != null
            && meshFilter.sharedMesh.name == TrapezoidMeshName)
        {
            DestroyObject(meshFilter.sharedMesh, delay);
        }

        DestroyObject(rangeVisual, delay);
    }

    /// <summary>표시용으로 만든 것을 지웁니다.</summary>
    /// <remarks>
    /// 실행 중이 아니면 <c>Destroy</c>가 통하지 않습니다. 이 표시는 에디터 도구에서도 만들어 보게 되므로
    /// 두 경우를 모두 받습니다. 에디터에서는 기다리지 않고 바로 지웁니다.
    /// </remarks>
    private static void DestroyObject(UnityEngine.Object target, float delay = 0.0f)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(target, delay);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    /// <summary>폭발 원통을 반투명하게 잠깐 띄웁니다. 표시 전용이라 피해 판정과는 무관합니다.</summary>
    private static void ShowCylinderRangeVisual(Vector3 center, float radius, float height)
    {
        DestroyRangeVisual(
            CreateCylinderRangeVisual(center, radius, height),
            RangeVisualDuration);
    }

    /// <summary>전방 사다리꼴 범위를 반투명하게 잠깐 띄웁니다.</summary>
    private static void ShowTrapezoidRangeVisual(
        Vector3 origin,
        Quaternion rotation,
        float range,
        float halfNearWidth,
        float halfFarWidth,
        float halfHeight)
    {
        DestroyRangeVisual(
            CreateTrapezoidRangeVisual(
                origin,
                rotation,
                range,
                halfNearWidth * 2.0f,
                halfFarWidth * 2.0f,
                halfHeight * 2.0f),
            RangeVisualDuration);
    }

    /// <summary>표시용 사다리꼴 기둥 메시를 만듭니다. 원점이 짧은 변의 한가운데이고 +Z가 정면입니다.</summary>
    private static Mesh BuildTrapezoidMesh(
        float range,
        float halfNearWidth,
        float halfFarWidth,
        float halfHeight)
    {
        Vector3[] vertices =
        {
            new Vector3(-halfNearWidth, -halfHeight, 0.0f),
            new Vector3(halfNearWidth, -halfHeight, 0.0f),
            new Vector3(halfNearWidth, halfHeight, 0.0f),
            new Vector3(-halfNearWidth, halfHeight, 0.0f),
            new Vector3(-halfFarWidth, -halfHeight, range),
            new Vector3(halfFarWidth, -halfHeight, range),
            new Vector3(halfFarWidth, halfHeight, range),
            new Vector3(-halfFarWidth, halfHeight, range),
        };

        int[] outerTriangles =
        {
            0, 1, 2, 0, 2, 3, // 뒷면
            4, 6, 5, 4, 7, 6, // 앞면
            0, 3, 7, 0, 7, 4, // 왼쪽
            1, 5, 6, 1, 6, 2, // 오른쪽
            0, 4, 5, 0, 5, 1, // 바닥
            3, 2, 6, 3, 6, 7, // 천장
        };

        // 같은 면을 뒤집어 한 벌 더 넣어 안에서도 보이게 합니다.
        int[] triangles = new int[outerTriangles.Length * 2];
        Array.Copy(outerTriangles, triangles, outerTriangles.Length);
        for (int i = 0; i < outerTriangles.Length; i += 3)
        {
            int target = outerTriangles.Length + i;
            triangles[target] = outerTriangles[i];
            triangles[target + 1] = outerTriangles[i + 2];
            triangles[target + 2] = outerTriangles[i + 1];
        }

        Mesh mesh = new Mesh { name = TrapezoidMeshName };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>표시용 반투명 머테리얼을 만듭니다. 셰이더를 찾지 못하면 <c>null</c>입니다.</summary>
    private static Material CreateRangeMaterial()
    {
        Shader rangeShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (rangeShader == null)
        {
            return null;
        }

        Material rangeMaterial = new Material(rangeShader)
        {
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = (int)RenderQueue.Transparent
        };
        rangeMaterial.SetColor("_BaseColor", RangeVisualColor);
        rangeMaterial.SetFloat("_Surface", 1.0f);
        rangeMaterial.SetFloat("_Blend", 0.0f);
        rangeMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        rangeMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        rangeMaterial.SetFloat("_ZWrite", 0.0f);
        rangeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        return rangeMaterial;
    }

    /// <summary>표시용 오브젝트에 머테리얼을 물립니다.</summary>
    /// <remarks>
    /// 여기서는 지우지 않습니다. 언제 사라질지는 부르는 쪽이 정합니다. 터질 때 쓰는 표시는 정해진
    /// 시간 뒤 사라지지만, 계속 띄워 두는 표시는 끌 때까지 남아야 합니다.
    /// </remarks>
    private static void ApplyRangeVisual(GameObject rangeVisual, Material rangeMaterial)
    {
        Renderer rangeRenderer = rangeVisual.GetComponent<Renderer>();
        rangeRenderer.sharedMaterial = rangeMaterial;
        rangeRenderer.shadowCastingMode = ShadowCastingMode.Off;
        rangeRenderer.receiveShadows = false;
    }
}
