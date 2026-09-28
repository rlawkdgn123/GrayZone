using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>피격 Hitbox의 초기 크기를 계산할 기준입니다.</summary>
public enum EnemyHitboxSizingMode
{
    MeshInfluence,
    BoneRatio,
}

/// <summary>실제 메시 외곽에 추가할 게임플레이 판정 여유입니다.</summary>
public enum EnemyHitboxForgiveness
{
    Standard,
    Accurate,
    Wide,
}

/// <summary>적 전투 콜라이더 구성 시 적용할 범위입니다.</summary>
[Serializable]
public sealed class EnemyCombatColliderBuildOptions
{
    public bool BuildHitboxes = true;
    public bool BuildMelee = true;
    public bool BuildRagdoll = true;
    public EnemyHitboxSizingMode HitboxSizingMode = EnemyHitboxSizingMode.MeshInfluence;
    public EnemyHitboxForgiveness HitboxForgiveness = EnemyHitboxForgiveness.Standard;
    public bool PreserveExistingColliderSizes = true;
    public bool RefitHitDetectVolume;
}

/// <summary>
/// 명시적으로 선택한 Humanoid 리그에 피격, 근접 공격, 래그돌 구성을 생성합니다.
/// </summary>
/// <remarks>
/// 프리팹 에셋만 대상으로 하며, 잔존 구 리그는 자동으로 제거하지 않습니다.
/// 같은 대상에 다시 실행하면 고정 이름의 생성물을 재사용하므로 중복 생성되지 않습니다.
/// </remarks>
public static class EnemyCombatColliderBuilder
{
    private const string MeleeBalancePath = "Assets/5.Data/ScriptableObject/Enemy/ZombieAttackBalance.asset";

    private enum HitboxShape
    {
        Box,
        Sphere,
        Capsule,
    }

    private readonly struct HitboxSpec
    {
        public readonly HumanBodyBones Bone;
        public readonly HumanBodyBones NextBone;
        public readonly HitboxShape Shape;
        public readonly float RadiusRatio;

        public HitboxSpec(HumanBodyBones bone, HumanBodyBones nextBone, HitboxShape shape, float radiusRatio = 0.2f)
        {
            Bone = bone;
            NextBone = nextBone;
            Shape = shape;
            RadiusRatio = radiusRatio;
        }
    }

    /// <summary>부위별로 분류한 Skinned Mesh 정점의 월드 좌표입니다.</summary>
    private sealed class MeshInfluenceSamples
    {
        private readonly Dictionary<HumanBodyBones, List<Vector3>> m_points =
            new Dictionary<HumanBodyBones, List<Vector3>>();

        public int TotalPointCount { get; private set; }

        public void Add(HumanBodyBones part, Vector3 worldPoint)
        {
            if (!m_points.TryGetValue(part, out List<Vector3> points))
            {
                points = new List<Vector3>();
                m_points.Add(part, points);
            }

            points.Add(worldPoint);
            TotalPointCount++;
        }

        public bool TryGet(HumanBodyBones part, out List<Vector3> points)
        {
            return m_points.TryGetValue(part, out points) && points.Count >= 4;
        }
    }

    private static readonly HitboxSpec[] HitboxSpecs =
    {
        new HitboxSpec(HumanBodyBones.Hips, HumanBodyBones.LastBone, HitboxShape.Box),
        new HitboxSpec(HumanBodyBones.Chest, HumanBodyBones.LastBone, HitboxShape.Box),
        new HitboxSpec(HumanBodyBones.Head, HumanBodyBones.LastBone, HitboxShape.Sphere),
        new HitboxSpec(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HitboxShape.Capsule, 0.20f),
        new HitboxSpec(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HitboxShape.Capsule, 0.18f),
        new HitboxSpec(HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleProximal, HitboxShape.Box),
        new HitboxSpec(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HitboxShape.Capsule, 0.20f),
        new HitboxSpec(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HitboxShape.Capsule, 0.18f),
        new HitboxSpec(HumanBodyBones.RightHand, HumanBodyBones.RightMiddleProximal, HitboxShape.Box),
        new HitboxSpec(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HitboxShape.Capsule, 0.24f),
        new HitboxSpec(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HitboxShape.Capsule, 0.20f),
        new HitboxSpec(HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, HitboxShape.Box),
        new HitboxSpec(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HitboxShape.Capsule, 0.24f),
        new HitboxSpec(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HitboxShape.Capsule, 0.20f),
        new HitboxSpec(HumanBodyBones.RightFoot, HumanBodyBones.RightToes, HitboxShape.Box),
    };

    /// <summary>프리팹 에셋을 열어 선택한 리그에 전투 구성을 적용하고 저장합니다.</summary>
    public static bool BuildPrefab(
        string prefabPath,
        string animatorPath,
        EnemyCombatColliderBuildOptions options,
        List<string> log,
        out EnemyCombatSetupResult validation)
    {
        validation = new EnemyCombatSetupResult();
        if (string.IsNullOrEmpty(prefabPath))
        {
            validation.Add(EnemyCombatIssueLevel.Error, "PREFAB_PATH_EMPTY", "프리팹 경로가 비어 있습니다.");
            return false;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
        {
            validation.Add(EnemyCombatIssueLevel.Error, "PREFAB_OPEN_FAILED", $"프리팹을 열지 못했습니다: {prefabPath}");
            return false;
        }

        try
        {
            Transform animatorTransform = EnemyCombatColliderValidator.ResolveIndexedPath(root.transform, animatorPath);
            Animator animator = animatorTransform != null ? animatorTransform.GetComponent<Animator>() : null;
            EnemyCombatSetupResult preflight = EnemyCombatColliderValidator.ValidatePrerequisites(root, animator);
            validation.Merge(preflight);
            if (preflight.HasErrors)
            {
                Add(log, "필수 조건 검사에 실패해 프리팹을 변경하지 않았습니다.");
                return false;
            }

            options ??= new EnemyCombatColliderBuildOptions();
            Add(log, $"적용 대상 Animator: {EnemyCombatColliderValidator.GetDisplayPath(root.transform, animator.transform)}");
            Add(log, $"적용 대상 리그: {EnemyCombatColliderValidator.GetDisplayPath(root.transform, EnemyCombatColliderValidator.ResolveRigRoot(root, animator))}");

            if (options.BuildRagdoll && !HumanoidRagdollBuilder.Build(root, animator, log))
            {
                validation.Add(EnemyCombatIssueLevel.Error, "RAGDOLL_BUILD_FAILED", "래그돌 구성에 실패했습니다.");
                return false;
            }

            if (options.BuildHitboxes && !EnsureHitboxes(root, animator, options, log))
            {
                validation.Add(EnemyCombatIssueLevel.Error, "HITBOX_BUILD_FAILED", "피격 Hitbox 구성에 실패했습니다.");
                return false;
            }

            if (options.BuildMelee && !EnsureMelee(root, animator, options.PreserveExistingColliderSizes, log))
            {
                validation.Add(EnemyCombatIssueLevel.Error, "MELEE_BUILD_FAILED", "근접 공격 판정 구성에 실패했습니다.");
                return false;
            }

            if (options.RefitHitDetectVolume)
            {
                RefitHitDetectVolume(root, animator, log);
            }

            validation = EnemyCombatColliderValidator.Validate(root, animator);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            AssetDatabase.SaveAssets();
            Add(log, $"프리팹 저장 완료: {prefabPath}");
            return true;
        }
        catch (Exception exception)
        {
            validation.Add(EnemyCombatIssueLevel.Error, "BUILD_EXCEPTION", exception.Message);
            Add(log, exception.ToString());
            return false;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>검사만 수행하고 프리팹을 변경하지 않습니다.</summary>
    public static EnemyCombatSetupResult ValidatePrefab(string prefabPath, string animatorPath)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
        {
            EnemyCombatSetupResult failed = new EnemyCombatSetupResult();
            failed.Add(EnemyCombatIssueLevel.Error, "PREFAB_OPEN_FAILED", $"프리팹을 열지 못했습니다: {prefabPath}");
            return failed;
        }

        try
        {
            Transform animatorTransform = EnemyCombatColliderValidator.ResolveIndexedPath(root.transform, animatorPath);
            Animator animator = animatorTransform != null ? animatorTransform.GetComponent<Animator>() : null;
            return EnemyCombatColliderValidator.Validate(root, animator);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>사용자가 고른 잔존 구 리그 하나를 프리팹에서 제거합니다.</summary>
    public static bool RemoveLegacyRig(
        string prefabPath,
        string animatorPath,
        string legacyRigPath,
        List<string> log,
        out EnemyCombatSetupResult validation)
    {
        validation = new EnemyCombatSetupResult();
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
        {
            validation.Add(EnemyCombatIssueLevel.Error, "PREFAB_OPEN_FAILED", $"프리팹을 열지 못했습니다: {prefabPath}");
            return false;
        }

        try
        {
            Transform animatorTransform = EnemyCombatColliderValidator.ResolveIndexedPath(root.transform, animatorPath);
            Animator animator = animatorTransform != null ? animatorTransform.GetComponent<Animator>() : null;
            if (!EnemyCombatColliderValidator.IsValidHumanoid(root, animator))
            {
                validation.Add(EnemyCombatIssueLevel.Error, "ANIMATOR_INVALID", "적용 대상 Animator를 확인할 수 없습니다.");
                return false;
            }

            EnemyCombatSetupResult beforeCleanup = EnemyCombatColliderValidator.Validate(root, animator);
            if (beforeCleanup.Status != EnemyCombatSetupStatus.CleanupRequired)
            {
                validation = beforeCleanup;
                validation.Add(
                    EnemyCombatIssueLevel.Error,
                    "CLEANUP_NOT_READY",
                    "새 리그의 전투 구성이 오류 없이 검증된 CLEANUP_REQUIRED 상태에서만 구 리그를 제거할 수 있습니다.");
                return false;
            }

            Transform legacy = EnemyCombatColliderValidator.ResolveIndexedPath(root.transform, legacyRigPath);
            List<Transform> allowed = EnemyCombatColliderValidator.FindLegacyRigCandidates(root, animator);
            if (legacy == null || !allowed.Contains(legacy))
            {
                validation.Add(EnemyCombatIssueLevel.Error, "LEGACY_TARGET_INVALID", "선택한 오브젝트가 현재 잔존 구 리그 후보가 아닙니다.");
                return false;
            }

            Transform targetRig = EnemyCombatColliderValidator.ResolveRigRoot(root, animator);
            if (legacy == targetRig || targetRig.IsChildOf(legacy) || legacy.IsChildOf(targetRig))
            {
                validation.Add(EnemyCombatIssueLevel.Error, "TARGET_RIG_PROTECTED", "적용 대상 리그 또는 그 상하위 계층은 제거할 수 없습니다.");
                return false;
            }

            string removedPath = EnemyCombatColliderValidator.GetDisplayPath(root.transform, legacy);
            UnityEngine.Object.DestroyImmediate(legacy.gameObject);
            validation = EnemyCombatColliderValidator.Validate(root, animator);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            AssetDatabase.SaveAssets();
            Add(log, $"잔존 구 리그 제거 및 저장 완료: {removedPath}");
            return true;
        }
        catch (Exception exception)
        {
            validation.Add(EnemyCombatIssueLevel.Error, "CLEANUP_EXCEPTION", exception.Message);
            Add(log, exception.ToString());
            return false;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool EnsureHitboxes(
        GameObject root,
        Animator animator,
        EnemyCombatColliderBuildOptions options,
        List<string> log)
    {
        int layer = LayerMask.NameToLayer("EnemyHitbox");
        HitboxGroup group = root.GetComponentInChildren<HitboxGroup>(true);
        if (layer < 0 || group == null)
        {
            Add(log, "EnemyHitbox 레이어 또는 HitboxGroup가 없어 Hitbox를 구성할 수 없습니다.");
            return false;
        }

        MeshInfluenceSamples meshSamples = options.HitboxSizingMode == EnemyHitboxSizingMode.MeshInfluence
            ? CollectMeshInfluenceSamples(root, animator, log)
            : null;

        List<Collider> colliders = new List<Collider>();
        for (int i = 0; i < HitboxSpecs.Length; i++)
        {
            HitboxSpec spec = HitboxSpecs[i];
            Transform bone = ResolveHitboxBone(animator, spec.Bone);
            if (bone == null)
            {
                Add(log, $"{spec.Bone} 뼈가 없어 Hitbox를 만들지 못했습니다.");
                return false;
            }

            string objectName = EnemyCombatColliderValidator.HitboxPrefix + spec.Bone;
            Transform point = FindDirectChild(bone, objectName);
            bool created = point == null;
            if (created)
            {
                GameObject pointObject = new GameObject(objectName);
                point = pointObject.transform;
                point.SetParent(bone, false);
                point.localPosition = Vector3.zero;
                point.localRotation = Quaternion.identity;
                point.localScale = Vector3.one;
                Add(log, $"{EnemyCombatColliderValidator.GetDisplayPath(root.transform, point)} 생성.");
            }

            point.gameObject.layer = layer;
            Collider collider = EnsureCollider(point.gameObject, spec.Shape);
            collider.isTrigger = true;
            collider.enabled = false;

            Hitbox hitbox = point.GetComponent<Hitbox>();
            if (hitbox == null)
            {
                hitbox = point.gameObject.AddComponent<Hitbox>();
            }

            SerializedObject hitboxSerialized = new SerializedObject(hitbox);
            hitboxSerialized.FindProperty("m_isHeadshot").boolValue = spec.Bone == HumanBodyBones.Head;
            hitboxSerialized.FindProperty("m_alwaysActive").boolValue = false;
            hitboxSerialized.FindProperty("m_logHit").boolValue = false;
            hitboxSerialized.ApplyModifiedPropertiesWithoutUndo();

            if (created || !options.PreserveExistingColliderSizes)
            {
                FitHitbox(collider, animator, spec, meshSamples, options.HitboxForgiveness, log);
            }

            colliders.Add(collider);
        }

        SerializedObject groupSerialized = new SerializedObject(group);
        SerializedProperty hitboxArray = groupSerialized.FindProperty("m_hitboxes");
        hitboxArray.arraySize = colliders.Count;
        for (int i = 0; i < colliders.Count; i++)
        {
            hitboxArray.GetArrayElementAtIndex(i).objectReferenceValue = colliders[i];
        }

        groupSerialized.ApplyModifiedPropertiesWithoutUndo();
        Add(log, $"Hitbox {colliders.Count}개를 구성하고 HitboxGroup에 연결했습니다.");
        return true;
    }

    private static bool EnsureMelee(GameObject root, Animator animator, bool preserveSizes, List<string> log)
    {
        EnemyAttack attack = root.GetComponentInChildren<EnemyAttack>(true);
        if (attack == null)
        {
            Add(log, "EnemyAttack을 찾지 못했습니다.");
            return false;
        }

        int enemyLayer = LayerMask.NameToLayer("Enemy");
        MeleeBalanceSO balance = AssetDatabase.LoadAssetAtPath<MeleeBalanceSO>(MeleeBalancePath);
        if (balance == null)
        {
            Add(log, $"경고: 근접 밸런스 SO를 찾지 못했습니다: {MeleeBalancePath}");
        }

        List<Melee> melees = new List<Melee>
        {
            EnsureMeleePoint(root, animator, HumanBodyBones.LeftHand, EnemyCombatColliderValidator.LeftAttackPointName, 0.4f, enemyLayer, attack, balance, preserveSizes, log),
            EnsureMeleePoint(root, animator, HumanBodyBones.RightHand, EnemyCombatColliderValidator.RightAttackPointName, 0.5f, enemyLayer, attack, balance, preserveSizes, log),
        };

        if (melees[0] == null || melees[1] == null)
        {
            return false;
        }

        SerializedObject attackSerialized = new SerializedObject(attack);
        SerializedProperty meleeArray = attackSerialized.FindProperty("m_melees");
        meleeArray.arraySize = melees.Count;
        for (int i = 0; i < melees.Count; i++)
        {
            meleeArray.GetArrayElementAtIndex(i).objectReferenceValue = melees[i];
        }

        attackSerialized.FindProperty("m_animator").objectReferenceValue = animator;
        attackSerialized.ApplyModifiedPropertiesWithoutUndo();
        Add(log, "EnemyAttack에 새 리그의 좌우 Melee와 Animator를 연결했습니다.");
        return true;
    }

    private static Melee EnsureMeleePoint(
        GameObject root,
        Animator animator,
        HumanBodyBones handBone,
        string pointName,
        float radius,
        int layer,
        EnemyAttack attack,
        MeleeBalanceSO balance,
        bool preserveSizes,
        List<string> log)
    {
        Transform hand = animator.GetBoneTransform(handBone);
        if (hand == null)
        {
            Add(log, $"{handBone} 뼈가 없어 {pointName}을 만들지 못했습니다.");
            return null;
        }

        Transform point = EnemyCombatColliderValidator.FindDescendant(hand, pointName);
        bool created = point == null;
        if (created)
        {
            GameObject pointObject = new GameObject(pointName);
            point = pointObject.transform;
            point.SetParent(hand, false);
            point.localPosition = Vector3.zero;
            point.localRotation = Quaternion.identity;
            point.localScale = Vector3.one;
            Add(log, $"{EnemyCombatColliderValidator.GetDisplayPath(root.transform, point)} 생성.");
        }

        point.gameObject.layer = layer;
        SphereCollider collider = point.GetComponent<SphereCollider>();
        if (collider == null)
        {
            collider = point.gameObject.AddComponent<SphereCollider>();
        }

        if (created || !preserveSizes)
        {
            collider.center = Vector3.zero;
            collider.radius = radius;
        }

        collider.isTrigger = true;
        collider.enabled = false;

        Melee melee = point.GetComponent<Melee>();
        if (melee == null)
        {
            melee = point.gameObject.AddComponent<Melee>();
        }

        SerializedObject meleeSerialized = new SerializedObject(melee);
        meleeSerialized.FindProperty("m_balanceSO").objectReferenceValue = balance;
        meleeSerialized.FindProperty("m_attack").objectReferenceValue = attack;
        meleeSerialized.ApplyModifiedPropertiesWithoutUndo();
        return melee;
    }

    private static Collider EnsureCollider(GameObject target, HitboxShape shape)
    {
        Collider existing = target.GetComponent<Collider>();
        if (existing != null)
        {
            return existing;
        }

        return shape switch
        {
            HitboxShape.Box => target.AddComponent<BoxCollider>(),
            HitboxShape.Sphere => target.AddComponent<SphereCollider>(),
            _ => target.AddComponent<CapsuleCollider>(),
        };
    }

    private static void FitHitbox(
        Collider collider,
        Animator animator,
        HitboxSpec spec,
        MeshInfluenceSamples meshSamples,
        EnemyHitboxForgiveness forgiveness,
        List<string> log)
    {
        Transform bone = ResolveHitboxBone(animator, spec.Bone);
        if (bone == null)
        {
            return;
        }

        if (meshSamples != null && meshSamples.TryGet(spec.Bone, out List<Vector3> meshPoints))
        {
            if (FitHitboxToMesh(collider, animator, spec, meshPoints, forgiveness))
            {
                Add(log, $"{collider.name}: 메시 정점 {meshPoints.Count}개 기준으로 크기를 맞췄습니다.");
                return;
            }
        }

        switch (spec.Shape)
        {
            case HitboxShape.Box:
                if (collider is BoxCollider box)
                {
                    FitBox(box, animator, spec);
                }
                break;
            case HitboxShape.Sphere:
                if (collider is SphereCollider sphere)
                {
                    FitHeadSphere(sphere, animator);
                }
                break;
            case HitboxShape.Capsule:
                if (collider is CapsuleCollider capsule)
                {
                    Transform next = animator.GetBoneTransform(spec.NextBone);
                    FitCapsule(capsule, bone, next, spec.RadiusRatio);
                }
                break;
        }

        ApplyBoneFallbackForgiveness(collider, GetForgivenessMultiplier(spec.Bone, forgiveness));
        Add(log, $"{collider.name}: 메시 표본이 없어 뼈 비율 기준으로 크기를 맞췄습니다.");
    }

    /// <summary>Skinned Mesh의 Bone Weight를 15개 피격 부위로 묶습니다.</summary>
    private static MeshInfluenceSamples CollectMeshInfluenceSamples(
        GameObject root,
        Animator animator,
        List<string> log)
    {
        MeshInfluenceSamples samples = new MeshInfluenceSamples();
        Transform rigRoot = EnemyCombatColliderValidator.ResolveRigRoot(root, animator);
        if (rigRoot == null)
        {
            return samples;
        }

        Dictionary<Transform, HumanBodyBones> directGroups = BuildDirectBoneGroups(animator);
        SkinnedMeshRenderer[] renderers = rigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        int readableRenderers = 0;

        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            SkinnedMeshRenderer renderer = renderers[rendererIndex];
            Mesh mesh = renderer.sharedMesh;
            if (mesh == null)
            {
                continue;
            }

            Vector3[] vertices;
            BoneWeight[] weights;
            try
            {
                vertices = mesh.vertices;
                weights = mesh.boneWeights;
            }
            catch (UnityException)
            {
                Add(log, $"경고: '{mesh.name}' 메시가 Read/Write 불가라 뼈 비율 계산으로 대체합니다.");
                continue;
            }

            Transform[] rendererBones = renderer.bones;
            if (vertices.Length == 0 || weights.Length != vertices.Length || rendererBones.Length == 0)
            {
                Add(log, $"경고: '{mesh.name}'에서 정점과 Bone Weight를 함께 읽지 못했습니다.");
                continue;
            }

            readableRenderers++;
            Matrix4x4 localToWorld = renderer.localToWorldMatrix;
            for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
            {
                int boneIndex = GetDominantBoneIndex(weights[vertexIndex]);
                if (boneIndex < 0 || boneIndex >= rendererBones.Length)
                {
                    continue;
                }

                Transform weightedBone = rendererBones[boneIndex];
                if (!TryResolveHitboxGroup(weightedBone, directGroups, out HumanBodyBones part))
                {
                    continue;
                }

                samples.Add(part, localToWorld.MultiplyPoint3x4(vertices[vertexIndex]));
            }
        }

        Add(log, $"메시 기반 Hitbox 표본: Renderer {readableRenderers}/{renderers.Length}, 정점 {samples.TotalPointCount}개.");
        return samples;
    }

    private static Dictionary<Transform, HumanBodyBones> BuildDirectBoneGroups(Animator animator)
    {
        Dictionary<Transform, HumanBodyBones> groups = new Dictionary<Transform, HumanBodyBones>();

        for (int i = 0; i < HitboxSpecs.Length; i++)
        {
            HitboxSpec spec = HitboxSpecs[i];
            Transform bone = ResolveHitboxBone(animator, spec.Bone);
            if (bone != null)
            {
                groups[bone] = spec.Bone;
            }
        }

        AddBoneGroup(groups, animator, HumanBodyBones.Spine, HumanBodyBones.Chest);
        AddBoneGroup(groups, animator, HumanBodyBones.UpperChest, HumanBodyBones.Chest);
        AddBoneGroup(groups, animator, HumanBodyBones.LeftShoulder, HumanBodyBones.Chest);
        AddBoneGroup(groups, animator, HumanBodyBones.RightShoulder, HumanBodyBones.Chest);
        AddBoneGroup(groups, animator, HumanBodyBones.Neck, HumanBodyBones.Head);

        return groups;
    }

    private static void AddBoneGroup(
        Dictionary<Transform, HumanBodyBones> groups,
        Animator animator,
        HumanBodyBones source,
        HumanBodyBones target)
    {
        Transform bone = animator.GetBoneTransform(source);
        if (bone != null)
        {
            groups[bone] = target;
        }
    }

    private static bool TryResolveHitboxGroup(
        Transform weightedBone,
        Dictionary<Transform, HumanBodyBones> directGroups,
        out HumanBodyBones part)
    {
        Transform current = weightedBone;
        while (current != null)
        {
            if (directGroups.TryGetValue(current, out part))
            {
                return true;
            }

            current = current.parent;
        }

        part = HumanBodyBones.LastBone;
        return false;
    }

    private static int GetDominantBoneIndex(BoneWeight weight)
    {
        int index = weight.boneIndex0;
        float highest = weight.weight0;

        if (weight.weight1 > highest)
        {
            highest = weight.weight1;
            index = weight.boneIndex1;
        }

        if (weight.weight2 > highest)
        {
            highest = weight.weight2;
            index = weight.boneIndex2;
        }

        if (weight.weight3 > highest)
        {
            highest = weight.weight3;
            index = weight.boneIndex3;
        }

        return highest > 0.0001f ? index : -1;
    }

    private static bool FitHitboxToMesh(
        Collider collider,
        Animator animator,
        HitboxSpec spec,
        List<Vector3> worldPoints,
        EnemyHitboxForgiveness forgiveness)
    {
        float multiplier = GetForgivenessMultiplier(spec.Bone, forgiveness);
        switch (collider)
        {
            case BoxCollider box:
                FitMeshBox(box, worldPoints, multiplier);
                return true;
            case SphereCollider sphere:
                FitMeshSphere(sphere, worldPoints, multiplier);
                return true;
            case CapsuleCollider capsule:
                Transform start = ResolveHitboxBone(animator, spec.Bone);
                Transform end = animator.GetBoneTransform(spec.NextBone);
                if (start == null || end == null)
                {
                    return false;
                }

                FitMeshCapsule(capsule, start, end, worldPoints, spec.RadiusRatio, multiplier);
                return true;
            default:
                return false;
        }
    }

    private static void FitMeshBox(BoxCollider box, List<Vector3> worldPoints, float multiplier)
    {
        Bounds bounds = BuildLocalBounds(box.transform, worldPoints);
        box.center = bounds.center;
        box.size = EnsureMinimumSize(bounds.size * multiplier, 0.04f);
    }

    private static void FitMeshSphere(SphereCollider sphere, List<Vector3> worldPoints, float multiplier)
    {
        Bounds bounds = BuildLocalBounds(sphere.transform, worldPoints);
        Vector3 center = bounds.center;
        float radius = 0.0f;
        for (int i = 0; i < worldPoints.Count; i++)
        {
            Vector3 local = sphere.transform.InverseTransformPoint(worldPoints[i]);
            radius = Mathf.Max(radius, Vector3.Distance(center, local));
        }

        sphere.center = center;
        sphere.radius = Mathf.Max(0.04f, radius * multiplier);
    }

    private static void FitMeshCapsule(
        CapsuleCollider capsule,
        Transform start,
        Transform end,
        List<Vector3> worldPoints,
        float fallbackRadiusRatio,
        float multiplier)
    {
        Vector3 localEnd = start.InverseTransformPoint(end.position);
        int axis = DominantAxis(localEnd);
        Bounds bounds = BuildLocalBounds(capsule.transform, worldPoints);
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;

        float axisExtent = GetAxis(extents, axis);
        float perpendicularA = GetAxis(extents, (axis + 1) % 3);
        float perpendicularB = GetAxis(extents, (axis + 2) % 3);
        float meshRadius = Mathf.Sqrt(perpendicularA * perpendicularA + perpendicularB * perpendicularB);
        float boneLength = localEnd.magnitude;
        float minimumRadius = Mathf.Max(0.025f, boneLength * fallbackRadiusRatio);
        float maximumRadius = Mathf.Max(minimumRadius, boneLength * 0.65f);
        float radius = Mathf.Clamp(meshRadius * multiplier, minimumRadius, maximumRadius);

        capsule.direction = axis;
        capsule.center = center;
        capsule.radius = radius;
        capsule.height = Mathf.Max(axisExtent * 2.0f * multiplier, boneLength * 1.05f, radius * 2.0f);
    }

    private static Bounds BuildLocalBounds(Transform localSpace, List<Vector3> worldPoints)
    {
        Vector3 first = localSpace.InverseTransformPoint(worldPoints[0]);
        Bounds bounds = new Bounds(first, Vector3.zero);
        for (int i = 1; i < worldPoints.Count; i++)
        {
            bounds.Encapsulate(localSpace.InverseTransformPoint(worldPoints[i]));
        }

        return bounds;
    }

    private static Vector3 EnsureMinimumSize(Vector3 size, float minimum)
    {
        return new Vector3(
            Mathf.Max(size.x, minimum),
            Mathf.Max(size.y, minimum),
            Mathf.Max(size.z, minimum));
    }

    private static float GetAxis(Vector3 value, int axis)
    {
        return axis switch
        {
            0 => value.x,
            1 => value.y,
            _ => value.z,
        };
    }

    private static float GetForgivenessMultiplier(HumanBodyBones bone, EnemyHitboxForgiveness forgiveness)
    {
        bool isHead = bone == HumanBodyBones.Head;
        bool isTorso = bone == HumanBodyBones.Hips || bone == HumanBodyBones.Chest;
        bool isEnd = bone == HumanBodyBones.LeftHand
            || bone == HumanBodyBones.RightHand
            || bone == HumanBodyBones.LeftFoot
            || bone == HumanBodyBones.RightFoot;

        return forgiveness switch
        {
            EnemyHitboxForgiveness.Accurate => 1.0f,
            EnemyHitboxForgiveness.Wide => isHead ? 1.08f : isTorso ? 1.16f : isEnd ? 1.23f : 1.20f,
            _ => isHead ? 1.05f : isTorso ? 1.10f : isEnd ? 1.15f : 1.12f,
        };
    }

    private static void ApplyBoneFallbackForgiveness(Collider collider, float multiplier)
    {
        switch (collider)
        {
            case BoxCollider box:
                box.size *= multiplier;
                break;
            case SphereCollider sphere:
                sphere.radius *= multiplier;
                break;
            case CapsuleCollider capsule:
                capsule.radius *= multiplier;
                capsule.height *= multiplier;
                break;
        }
    }

    private static void FitBox(BoxCollider box, Animator animator, HitboxSpec spec)
    {
        Transform bone = ResolveHitboxBone(animator, spec.Bone);
        List<Vector3> worldPoints = new List<Vector3> { bone.position };

        if (spec.Bone == HumanBodyBones.Hips)
        {
            AddBonePoint(worldPoints, animator, HumanBodyBones.LeftUpperLeg);
            AddBonePoint(worldPoints, animator, HumanBodyBones.RightUpperLeg);
            AddBonePoint(worldPoints, animator, HumanBodyBones.Spine);
        }
        else if (spec.Bone == HumanBodyBones.Chest)
        {
            AddBonePoint(worldPoints, animator, HumanBodyBones.LeftUpperArm);
            AddBonePoint(worldPoints, animator, HumanBodyBones.RightUpperArm);
            AddBonePoint(worldPoints, animator, HumanBodyBones.Neck);
            if (worldPoints.Count < 4)
            {
                AddBonePoint(worldPoints, animator, HumanBodyBones.Head);
            }
        }
        else
        {
            Transform tip = animator.GetBoneTransform(spec.NextBone);
            if (tip != null)
            {
                worldPoints.Add(tip.position);
            }
            else if (bone.parent != null)
            {
                Vector3 outward = bone.position - bone.parent.position;
                worldPoints.Add(bone.position + outward * 0.45f);
            }
        }

        FitBoxToPoints(box, worldPoints, spec.Bone == HumanBodyBones.Hips || spec.Bone == HumanBodyBones.Chest ? 0.12f : 0.05f);
    }

    private static void FitBoxToPoints(BoxCollider box, List<Vector3> worldPoints, float minimumThickness)
    {
        Transform local = box.transform;
        Vector3 first = local.InverseTransformPoint(worldPoints[0]);
        Bounds bounds = new Bounds(first, Vector3.zero);
        for (int i = 1; i < worldPoints.Count; i++)
        {
            bounds.Encapsulate(local.InverseTransformPoint(worldPoints[i]));
        }

        Vector3 size = bounds.size;
        size.x = Mathf.Max(size.x * 1.15f, minimumThickness);
        size.y = Mathf.Max(size.y * 1.05f, minimumThickness);
        size.z = Mathf.Max(size.z * 1.15f, minimumThickness);
        box.center = bounds.center;
        box.size = size;
    }

    private static void FitHeadSphere(SphereCollider sphere, Animator animator)
    {
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
        Transform neck = animator.GetBoneTransform(HumanBodyBones.Neck)
            ?? animator.GetBoneTransform(HumanBodyBones.Chest)
            ?? animator.GetBoneTransform(HumanBodyBones.Spine);

        float reference = neck != null ? Vector3.Distance(head.position, neck.position) : 0.2f;
        sphere.center = Vector3.zero;
        sphere.radius = Mathf.Max(0.04f, reference * 0.55f);
    }

    private static void FitCapsule(CapsuleCollider capsule, Transform start, Transform end, float radiusRatio)
    {
        if (start == null || end == null)
        {
            return;
        }

        Vector3 localEnd = start.InverseTransformPoint(end.position);
        float length = localEnd.magnitude;
        float radius = Mathf.Max(0.025f, length * radiusRatio);
        capsule.center = localEnd * 0.5f;
        capsule.direction = DominantAxis(localEnd);
        capsule.radius = radius;
        capsule.height = Mathf.Max(length * 1.05f, radius * 2.0f);
    }

    private static int DominantAxis(Vector3 vector)
    {
        Vector3 absolute = new Vector3(Mathf.Abs(vector.x), Mathf.Abs(vector.y), Mathf.Abs(vector.z));
        if (absolute.x >= absolute.y && absolute.x >= absolute.z) return 0;
        if (absolute.y >= absolute.x && absolute.y >= absolute.z) return 1;
        return 2;
    }

    private static Transform ResolveHitboxBone(Animator animator, HumanBodyBones bone)
    {
        if (bone == HumanBodyBones.Chest)
        {
            return animator.GetBoneTransform(HumanBodyBones.Chest)
                ?? animator.GetBoneTransform(HumanBodyBones.Spine);
        }

        return animator.GetBoneTransform(bone);
    }

    private static void AddBonePoint(List<Vector3> points, Animator animator, HumanBodyBones bone)
    {
        Transform transform = animator.GetBoneTransform(bone);
        if (transform != null)
        {
            points.Add(transform.position);
        }
    }

    private static Transform FindDirectChild(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == name)
            {
                return child;
            }
        }

        return null;
    }

    private static void RefitHitDetectVolume(GameObject root, Animator animator, List<string> log)
    {
        Transform rigRoot = EnemyCombatColliderValidator.ResolveRigRoot(root, animator);
        Transform volumeTransform = EnemyCombatColliderValidator.FindDescendant(root.transform, "HitDetectVolume");
        BoxCollider volume = volumeTransform != null ? volumeTransform.GetComponent<BoxCollider>() : null;
        Renderer[] renderers = rigRoot != null ? rigRoot.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
        if (volume == null || renderers.Length == 0)
        {
            Add(log, "경고: HitDetectVolume BoxCollider 또는 대상 리그 Renderer가 없어 크기를 재계산하지 못했습니다.");
            return;
        }

        bool hasBounds = false;
        Bounds worldBounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (!hasBounds)
            {
                worldBounds = renderers[i].bounds;
                hasBounds = true;
            }
            else
            {
                worldBounds.Encapsulate(renderers[i].bounds);
            }
        }

        Vector3 min = worldBounds.min;
        Vector3 max = worldBounds.max;
        List<Vector3> corners = new List<Vector3>(8);
        for (int x = 0; x <= 1; x++)
        {
            for (int y = 0; y <= 1; y++)
            {
                for (int z = 0; z <= 1; z++)
                {
                    corners.Add(new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z));
                }
            }
        }

        FitBoxToPoints(volume, corners, 0.2f);
        volume.isTrigger = true;
        int layer = LayerMask.NameToLayer("EnemyHitDetect");
        if (layer >= 0)
        {
            volume.gameObject.layer = layer;
        }

        Add(log, $"HitDetectVolume을 대상 리그 Renderer Bounds에 맞췄습니다: size={volume.size}");
    }

    private static void Add(List<string> log, string message)
    {
        log?.Add(message);
    }
}
