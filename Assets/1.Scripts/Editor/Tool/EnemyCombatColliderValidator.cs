using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>적 전투 콜라이더 구성의 종합 상태입니다.</summary>
public enum EnemyCombatSetupStatus
{
    Pass = 0,
    Warning = 1,
    CleanupRequired = 2,
    Fail = 3,
}

/// <summary>검사 항목 하나의 심각도입니다.</summary>
public enum EnemyCombatIssueLevel
{
    Info,
    Warning,
    CleanupRequired,
    Error,
}

/// <summary>검사 항목 하나를 사용자에게 전달하기 위한 값입니다.</summary>
public sealed class EnemyCombatSetupIssue
{
    public readonly EnemyCombatIssueLevel Level;
    public readonly string Code;
    public readonly string Message;

    public EnemyCombatSetupIssue(EnemyCombatIssueLevel level, string code, string message)
    {
        Level = level;
        Code = code;
        Message = message;
    }
}

/// <summary>적 전투 콜라이더 검사 결과입니다.</summary>
public sealed class EnemyCombatSetupResult
{
    private readonly List<EnemyCombatSetupIssue> m_issues = new List<EnemyCombatSetupIssue>();

    public EnemyCombatSetupStatus Status { get; private set; } = EnemyCombatSetupStatus.Pass;
    public IReadOnlyList<EnemyCombatSetupIssue> Issues => m_issues;
    public bool HasErrors => Status == EnemyCombatSetupStatus.Fail;

    public void Add(EnemyCombatIssueLevel level, string code, string message)
    {
        m_issues.Add(new EnemyCombatSetupIssue(level, code, message));

        EnemyCombatSetupStatus next = level switch
        {
            EnemyCombatIssueLevel.Error => EnemyCombatSetupStatus.Fail,
            EnemyCombatIssueLevel.CleanupRequired => EnemyCombatSetupStatus.CleanupRequired,
            EnemyCombatIssueLevel.Warning => EnemyCombatSetupStatus.Warning,
            _ => EnemyCombatSetupStatus.Pass,
        };

        if (next > Status)
        {
            Status = next;
        }
    }

    public void Merge(EnemyCombatSetupResult other)
    {
        if (other == null)
        {
            return;
        }

        for (int i = 0; i < other.m_issues.Count; i++)
        {
            EnemyCombatSetupIssue issue = other.m_issues[i];
            Add(issue.Level, issue.Code, issue.Message);
        }
    }
}

/// <summary>프리팹 안에서 선택 가능한 Humanoid Animator 정보입니다.</summary>
public sealed class EnemyCombatAnimatorCandidate
{
    public string AnimatorPath;
    public string DisplayPath;
    public string RigRootPath;
    public string RigRootDisplayPath;
    public int RecommendationScore;
}

/// <summary>잔존 구 리그 후보 정보입니다.</summary>
public sealed class EnemyCombatLegacyRigCandidate
{
    public string RigRootPath;
    public string DisplayPath;
    public string Reason;
}

/// <summary>프리팹을 변경하지 않고 수집한 구성 대상 정보입니다.</summary>
public sealed class EnemyCombatColliderInspection
{
    public readonly List<EnemyCombatAnimatorCandidate> Animators = new List<EnemyCombatAnimatorCandidate>();
    public readonly List<EnemyCombatLegacyRigCandidate> LegacyRigs = new List<EnemyCombatLegacyRigCandidate>();
    public string SelectedAnimatorPath;
    public EnemyCombatSetupResult Result = new EnemyCombatSetupResult();
}

/// <summary>
/// 적 프리팹의 적용 대상 리그, 전투용 콜라이더와 잔존 구 리그를 검사합니다.
/// </summary>
public static class EnemyCombatColliderValidator
{
    public const string HitboxPrefix = "Hitbox_";
    public const string LeftAttackPointName = "AttackPoint_L";
    public const string RightAttackPointName = "AttackPoint_R";

    public static readonly HumanBodyBones[] HitboxBones =
    {
        HumanBodyBones.Hips,
        HumanBodyBones.Chest,
        HumanBodyBones.Head,
        HumanBodyBones.LeftUpperArm,
        HumanBodyBones.LeftLowerArm,
        HumanBodyBones.LeftHand,
        HumanBodyBones.RightUpperArm,
        HumanBodyBones.RightLowerArm,
        HumanBodyBones.RightHand,
        HumanBodyBones.LeftUpperLeg,
        HumanBodyBones.LeftLowerLeg,
        HumanBodyBones.LeftFoot,
        HumanBodyBones.RightUpperLeg,
        HumanBodyBones.RightLowerLeg,
        HumanBodyBones.RightFoot,
    };

    private static readonly HumanBodyBones[] RequiredRagdollBones =
    {
        HumanBodyBones.Hips,
        HumanBodyBones.Spine,
        HumanBodyBones.Head,
        HumanBodyBones.LeftUpperLeg,
        HumanBodyBones.LeftLowerLeg,
        HumanBodyBones.LeftFoot,
        HumanBodyBones.RightUpperLeg,
        HumanBodyBones.RightLowerLeg,
        HumanBodyBones.RightFoot,
        HumanBodyBones.LeftUpperArm,
        HumanBodyBones.LeftLowerArm,
        HumanBodyBones.LeftHand,
        HumanBodyBones.RightUpperArm,
        HumanBodyBones.RightLowerArm,
        HumanBodyBones.RightHand,
    };

    /// <summary>프리팹 에셋을 변경하지 않고 Animator와 잔존 리그 후보를 찾습니다.</summary>
    public static EnemyCombatColliderInspection InspectPrefab(string prefabPath, string preferredAnimatorPath = null)
    {
        EnemyCombatColliderInspection inspection = new EnemyCombatColliderInspection();
        if (string.IsNullOrEmpty(prefabPath))
        {
            inspection.Result.Add(EnemyCombatIssueLevel.Error, "PREFAB_PATH_EMPTY", "프리팹 경로가 비어 있습니다.");
            return inspection;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
        {
            inspection.Result.Add(EnemyCombatIssueLevel.Error, "PREFAB_OPEN_FAILED", $"프리팹을 열지 못했습니다: {prefabPath}");
            return inspection;
        }

        try
        {
            List<Animator> animators = FindHumanoidAnimators(root);
            for (int i = 0; i < animators.Count; i++)
            {
                Animator animator = animators[i];
                Transform rigRoot = ResolveRigRoot(root, animator);
                inspection.Animators.Add(new EnemyCombatAnimatorCandidate
                {
                    AnimatorPath = GetIndexedPath(root.transform, animator.transform),
                    DisplayPath = GetDisplayPath(root.transform, animator.transform),
                    RigRootPath = GetIndexedPath(root.transform, rigRoot),
                    RigRootDisplayPath = GetDisplayPath(root.transform, rigRoot),
                    RecommendationScore = ScoreAnimator(root, animator),
                });
            }

            if (inspection.Animators.Count == 0)
            {
                inspection.Result.Add(
                    EnemyCombatIssueLevel.Error,
                    "HUMANOID_NOT_FOUND",
                    "유효한 Humanoid Animator를 찾지 못했습니다.");
                return inspection;
            }

            EnemyCombatAnimatorCandidate selected = inspection.Animators
                .FirstOrDefault(candidate => candidate.AnimatorPath == preferredAnimatorPath)
                ?? inspection.Animators.OrderByDescending(candidate => candidate.RecommendationScore).First();

            inspection.SelectedAnimatorPath = selected.AnimatorPath;
            Animator selectedAnimator = ResolveIndexedPath(root.transform, selected.AnimatorPath)?.GetComponent<Animator>();
            if (selectedAnimator == null)
            {
                inspection.Result.Add(EnemyCombatIssueLevel.Error, "ANIMATOR_RESOLVE_FAILED", "선택한 Animator를 다시 찾지 못했습니다.");
                return inspection;
            }

            List<Transform> legacy = FindLegacyRigCandidates(root, selectedAnimator);
            for (int i = 0; i < legacy.Count; i++)
            {
                Transform candidate = legacy[i];
                inspection.LegacyRigs.Add(new EnemyCombatLegacyRigCandidate
                {
                    RigRootPath = GetIndexedPath(root.transform, candidate),
                    DisplayPath = GetDisplayPath(root.transform, candidate),
                    Reason = DescribeLegacyEvidence(candidate),
                });
            }

            inspection.Result.Add(
                EnemyCombatIssueLevel.Info,
                "INSPECTION_READY",
                $"Humanoid Animator {inspection.Animators.Count}개, 잔존 구 리그 후보 {inspection.LegacyRigs.Count}개를 찾았습니다.");
            return inspection;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>구성 전 반드시 만족해야 하는 조건만 검사합니다.</summary>
    public static EnemyCombatSetupResult ValidatePrerequisites(GameObject root, Animator targetAnimator)
    {
        EnemyCombatSetupResult result = new EnemyCombatSetupResult();
        if (root == null)
        {
            result.Add(EnemyCombatIssueLevel.Error, "ROOT_MISSING", "적 프리팹 루트가 없습니다.");
            return result;
        }

        if (!IsValidHumanoid(root, targetAnimator))
        {
            result.Add(EnemyCombatIssueLevel.Error, "ANIMATOR_INVALID", "선택한 Animator가 이 프리팹에 속한 유효한 Humanoid가 아닙니다.");
            return result;
        }

        List<string> missingBones = new List<string>();
        for (int i = 0; i < RequiredRagdollBones.Length; i++)
        {
            if (targetAnimator.GetBoneTransform(RequiredRagdollBones[i]) == null)
            {
                missingBones.Add(RequiredRagdollBones[i].ToString());
            }
        }

        if (missingBones.Count > 0)
        {
            result.Add(
                EnemyCombatIssueLevel.Error,
                "BONES_MISSING",
                "필수 Humanoid 뼈가 없습니다: " + string.Join(", ", missingBones));
        }

        RequireComponent<EnemyHealth>(root, result, "EnemyHealth");
        RequireComponent<EnemyAttack>(root, result, "EnemyAttack");
        RequireComponent<HitboxGroup>(root, result, "HitboxGroup");

        if (LayerMask.NameToLayer("EnemyHitbox") < 0)
        {
            result.Add(EnemyCombatIssueLevel.Error, "LAYER_ENEMY_HITBOX_MISSING", "EnemyHitbox 레이어가 없습니다.");
        }

        if (LayerMask.NameToLayer("Enemy") < 0)
        {
            result.Add(EnemyCombatIssueLevel.Error, "LAYER_ENEMY_MISSING", "Enemy 레이어가 없습니다.");
        }

        if (!result.HasErrors)
        {
            result.Add(EnemyCombatIssueLevel.Info, "PREFLIGHT_PASS", "구성 적용 전 필수 조건을 충족했습니다.");
        }

        return result;
    }

    /// <summary>구성 완료 상태를 정적으로 검사합니다.</summary>
    public static EnemyCombatSetupResult Validate(GameObject root, Animator targetAnimator)
    {
        EnemyCombatSetupResult result = ValidatePrerequisites(root, targetAnimator);
        if (result.HasErrors)
        {
            return result;
        }

        Transform rigRoot = ResolveRigRoot(root, targetAnimator);
        int hitboxLayer = LayerMask.NameToLayer("EnemyHitbox");
        Hitbox[] hitboxes = rigRoot.GetComponentsInChildren<Hitbox>(true);
        if (hitboxes.Length != HitboxBones.Length)
        {
            result.Add(
                EnemyCombatIssueLevel.Error,
                "HITBOX_COUNT",
                $"적용 대상 리그의 Hitbox가 {hitboxes.Length}개입니다. 필요한 수량은 {HitboxBones.Length}개입니다.");
        }

        int headshots = 0;
        List<Collider> hitboxColliders = new List<Collider>();
        for (int i = 0; i < hitboxes.Length; i++)
        {
            Hitbox hitbox = hitboxes[i];
            if (hitbox.IsHeadshot)
            {
                headshots++;
            }

            Collider collider = hitbox.GetComponent<Collider>();
            if (collider == null)
            {
                result.Add(EnemyCombatIssueLevel.Error, "HITBOX_COLLIDER_MISSING", $"{hitbox.name}에 Collider가 없습니다.");
                continue;
            }

            hitboxColliders.Add(collider);
            if (!collider.isTrigger)
            {
                result.Add(EnemyCombatIssueLevel.Error, "HITBOX_NOT_TRIGGER", $"{hitbox.name} Collider가 Trigger가 아닙니다.");
            }

            if (hitbox.gameObject.layer != hitboxLayer)
            {
                result.Add(EnemyCombatIssueLevel.Error, "HITBOX_LAYER", $"{hitbox.name}의 레이어가 EnemyHitbox가 아닙니다.");
            }
        }

        if (headshots != 1)
        {
            result.Add(EnemyCombatIssueLevel.Error, "HEADSHOT_COUNT", $"약점 Hitbox가 {headshots}개입니다. 정확히 1개여야 합니다.");
        }

        ValidateHitboxGroup(root, hitboxColliders, result);
        ValidateMelee(root, rigRoot, targetAnimator, result);
        ValidateRagdoll(root, rigRoot, result);
        ValidateHitDetectVolume(root, result);
        ValidateAttackEvents(targetAnimator, result);

        List<Transform> legacy = FindLegacyRigCandidates(root, targetAnimator);
        if (legacy.Count > 0)
        {
            result.Add(
                EnemyCombatIssueLevel.CleanupRequired,
                "LEGACY_RIG_REMAINS",
                "잔존 구 리그가 남아 있습니다: " + string.Join(", ", legacy.Select(item => GetDisplayPath(root.transform, item))));
        }

        if (result.Status == EnemyCombatSetupStatus.Pass)
        {
            result.Add(EnemyCombatIssueLevel.Info, "STATIC_PASS", "전투 콜라이더 정적 구성이 완료되었습니다.");
        }

        return result;
    }

    public static List<Animator> FindHumanoidAnimators(GameObject root)
    {
        List<Animator> found = new List<Animator>();
        if (root == null)
        {
            return found;
        }

        Animator[] animators = root.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            if (IsValidHumanoid(root, animators[i]))
            {
                found.Add(animators[i]);
            }
        }

        return found;
    }

    public static bool IsValidHumanoid(GameObject root, Animator animator)
    {
        return root != null
            && animator != null
            && (animator.transform == root.transform || animator.transform.IsChildOf(root.transform))
            && animator.avatar != null
            && animator.avatar.isHuman
            && animator.avatar.isValid
            && animator.GetBoneTransform(HumanBodyBones.Hips) != null
            && animator.GetBoneTransform(HumanBodyBones.Head) != null;
    }

    /// <summary>Animator의 Hips가 속한 프리팹 루트 직계 모델 가지를 돌려줍니다.</summary>
    public static Transform ResolveRigRoot(GameObject root, Animator animator)
    {
        if (root == null || animator == null)
        {
            return null;
        }

        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        if (hips == null)
        {
            return animator.transform;
        }

        Transform current = hips;
        while (current.parent != null && current.parent != root.transform)
        {
            current = current.parent;
        }

        return current.parent == root.transform ? current : animator.transform;
    }

    /// <summary>적용 대상 모델 가지 밖에 남은 전투 리그 후보를 찾습니다.</summary>
    public static List<Transform> FindLegacyRigCandidates(GameObject root, Animator targetAnimator)
    {
        List<Transform> result = new List<Transform>();
        if (root == null || targetAnimator == null)
        {
            return result;
        }

        Transform targetRigRoot = ResolveRigRoot(root, targetAnimator);
        HashSet<Transform> candidates = new HashSet<Transform>();

        Animator[] animators = root.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            if (animators[i] == targetAnimator || !IsValidHumanoid(root, animators[i]))
            {
                continue;
            }

            Transform branch = ResolveRigRoot(root, animators[i]);
            AddLegacyCandidate(root.transform, targetRigRoot, branch, candidates);
        }

        AddBranchesFromComponents(root, targetRigRoot, root.GetComponentsInChildren<Hitbox>(true), candidates);
        AddBranchesFromComponents(root, targetRigRoot, root.GetComponentsInChildren<Melee>(true), candidates);
        AddBranchesFromComponents(root, targetRigRoot, root.GetComponentsInChildren<CharacterJoint>(true), candidates);

        Rigidbody[] bodies = root.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i].transform == root.transform)
            {
                continue;
            }

            Transform branch = GetRootBranch(root.transform, bodies[i].transform);
            AddLegacyCandidate(root.transform, targetRigRoot, branch, candidates);
        }

        result.AddRange(candidates);
        result.Sort((a, b) => string.CompareOrdinal(GetDisplayPath(root.transform, a), GetDisplayPath(root.transform, b)));
        return result;
    }

    public static string GetIndexedPath(Transform root, Transform target)
    {
        if (root == null || target == null || (target != root && !target.IsChildOf(root)))
        {
            return null;
        }

        if (target == root)
        {
            return string.Empty;
        }

        List<int> indices = new List<int>();
        Transform current = target;
        while (current != null && current != root)
        {
            indices.Add(current.GetSiblingIndex());
            current = current.parent;
        }

        indices.Reverse();
        return string.Join("/", indices);
    }

    public static Transform ResolveIndexedPath(Transform root, string path)
    {
        if (root == null || path == null)
        {
            return null;
        }

        if (path.Length == 0)
        {
            return root;
        }

        Transform current = root;
        string[] parts = path.Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out int index) || index < 0 || index >= current.childCount)
            {
                return null;
            }

            current = current.GetChild(index);
        }

        return current;
    }

    public static string GetDisplayPath(Transform root, Transform target)
    {
        if (root == null || target == null)
        {
            return "<없음>";
        }

        if (target == root)
        {
            return root.name;
        }

        List<string> names = new List<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            names.Add(current.name);
            current = current.parent;
        }

        names.Add(root.name);
        names.Reverse();
        return string.Join("/", names);
    }

    public static Transform FindDescendant(Transform parent, string name)
    {
        if (parent == null)
        {
            return null;
        }

        if (parent.name == name)
        {
            return parent;
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindDescendant(parent.GetChild(i), name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static int ScoreAnimator(GameObject root, Animator animator)
    {
        int score = 0;
        if (animator.transform != root.transform)
        {
            score += 25;
        }

        if (animator.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
        {
            score += 50;
        }

        if (animator.gameObject.activeInHierarchy)
        {
            score += 10;
        }

        if (animator.enabled)
        {
            score += 5;
        }

        return score;
    }

    private static void RequireComponent<T>(GameObject root, EnemyCombatSetupResult result, string label) where T : Component
    {
        if (root.GetComponentInChildren<T>(true) == null)
        {
            result.Add(EnemyCombatIssueLevel.Error, "COMPONENT_MISSING", $"루트 계층에서 {label}을 찾지 못했습니다.");
        }
    }

    private static void ValidateHitboxGroup(GameObject root, List<Collider> expected, EnemyCombatSetupResult result)
    {
        HitboxGroup group = root.GetComponentInChildren<HitboxGroup>(true);
        if (group == null)
        {
            return;
        }

        SerializedObject serialized = new SerializedObject(group);
        SerializedProperty array = serialized.FindProperty("m_hitboxes");
        if (array == null || array.arraySize != expected.Count)
        {
            result.Add(EnemyCombatIssueLevel.Error, "HITBOX_GROUP_COUNT", "HitboxGroup가 새 리그 Hitbox 전체를 명시적으로 참조하지 않습니다.");
            return;
        }

        HashSet<UnityEngine.Object> actual = new HashSet<UnityEngine.Object>();
        for (int i = 0; i < array.arraySize; i++)
        {
            actual.Add(array.GetArrayElementAtIndex(i).objectReferenceValue);
        }

        if (expected.Any(collider => !actual.Contains(collider)))
        {
            result.Add(EnemyCombatIssueLevel.Error, "HITBOX_GROUP_REFERENCE", "HitboxGroup에 다른 리그의 Collider가 포함되었거나 새 Hitbox가 누락됐습니다.");
        }
    }

    private static void ValidateMelee(GameObject root, Transform rigRoot, Animator animator, EnemyCombatSetupResult result)
    {
        Melee[] melees = rigRoot.GetComponentsInChildren<Melee>(true);
        if (melees.Length != 2)
        {
            result.Add(EnemyCombatIssueLevel.Error, "MELEE_COUNT", $"적용 대상 리그의 Melee가 {melees.Length}개입니다. 좌우 손 2개가 필요합니다.");
        }

        ValidateAttackPoint(rigRoot, LeftAttackPointName, result);
        ValidateAttackPoint(rigRoot, RightAttackPointName, result);

        EnemyAttack attack = root.GetComponentInChildren<EnemyAttack>(true);
        if (attack == null)
        {
            return;
        }

        SerializedObject serialized = new SerializedObject(attack);
        SerializedProperty animatorProperty = serialized.FindProperty("m_animator");
        if (animatorProperty == null || animatorProperty.objectReferenceValue != animator)
        {
            result.Add(EnemyCombatIssueLevel.Error, "ATTACK_ANIMATOR", "EnemyAttack이 선택한 Animator를 참조하지 않습니다.");
        }

        SerializedProperty meleeArray = serialized.FindProperty("m_melees");
        if (meleeArray == null || meleeArray.arraySize != melees.Length)
        {
            result.Add(EnemyCombatIssueLevel.Error, "ATTACK_MELEE_ARRAY", "EnemyAttack의 Melee 배열이 적용 대상 리그와 일치하지 않습니다.");
            return;
        }

        HashSet<UnityEngine.Object> actual = new HashSet<UnityEngine.Object>();
        for (int i = 0; i < meleeArray.arraySize; i++)
        {
            actual.Add(meleeArray.GetArrayElementAtIndex(i).objectReferenceValue);
        }

        if (melees.Any(melee => !actual.Contains(melee)))
        {
            result.Add(EnemyCombatIssueLevel.Error, "ATTACK_MELEE_REFERENCE", "EnemyAttack이 다른 리그의 Melee를 참조합니다.");
        }
    }

    private static void ValidateAttackPoint(Transform rigRoot, string name, EnemyCombatSetupResult result)
    {
        Transform point = FindDescendant(rigRoot, name);
        if (point == null)
        {
            result.Add(EnemyCombatIssueLevel.Error, "ATTACK_POINT_MISSING", $"{name}이 없습니다.");
            return;
        }

        Collider collider = point.GetComponent<Collider>();
        if (collider == null || !collider.isTrigger)
        {
            result.Add(EnemyCombatIssueLevel.Error, "ATTACK_POINT_COLLIDER", $"{name}에 Trigger Collider가 없습니다.");
        }
    }

    private static void ValidateRagdoll(GameObject root, Transform rigRoot, EnemyCombatSetupResult result)
    {
        if (root.GetComponent<RagdollController>() == null)
        {
            result.Add(EnemyCombatIssueLevel.Error, "RAGDOLL_CONTROLLER", "루트에 RagdollController가 없습니다.");
        }

        int bodies = rigRoot.GetComponentsInChildren<Rigidbody>(true).Length;
        int joints = rigRoot.GetComponentsInChildren<CharacterJoint>(true).Length;
        if (bodies != 11)
        {
            result.Add(EnemyCombatIssueLevel.Error, "RAGDOLL_BODY_COUNT", $"적용 대상 리그의 래그돌 Rigidbody가 {bodies}개입니다. 필요한 수량은 11개입니다.");
        }

        if (joints != 10)
        {
            result.Add(EnemyCombatIssueLevel.Error, "RAGDOLL_JOINT_COUNT", $"적용 대상 리그의 CharacterJoint가 {joints}개입니다. 필요한 수량은 10개입니다.");
        }
    }

    private static void ValidateHitDetectVolume(GameObject root, EnemyCombatSetupResult result)
    {
        Transform volume = FindDescendant(root.transform, "HitDetectVolume");
        if (volume == null || volume.GetComponent<Collider>() == null)
        {
            result.Add(EnemyCombatIssueLevel.Error, "HIT_DETECT_MISSING", "HitDetectVolume과 Collider가 필요합니다.");
            return;
        }

        int layer = LayerMask.NameToLayer("EnemyHitDetect");
        if (layer >= 0 && volume.gameObject.layer != layer)
        {
            result.Add(EnemyCombatIssueLevel.Warning, "HIT_DETECT_LAYER", "HitDetectVolume의 레이어가 EnemyHitDetect가 아닙니다.");
        }
    }

    private static void ValidateAttackEvents(Animator animator, EnemyCombatSetupResult result)
    {
        RuntimeAnimatorController controller = animator.runtimeAnimatorController;
        if (controller == null)
        {
            result.Add(EnemyCombatIssueLevel.Warning, "ANIMATOR_CONTROLLER_MISSING", "선택한 Animator에 RuntimeAnimatorController가 없습니다.");
            return;
        }

        bool hasOn = false;
        bool hasOff = false;
        AnimationClip[] clips = controller.animationClips;
        for (int i = 0; i < clips.Length && (!hasOn || !hasOff); i++)
        {
            AnimationEvent[] events = AnimationUtility.GetAnimationEvents(clips[i]);
            for (int j = 0; j < events.Length; j++)
            {
                hasOn |= events[j].functionName == "OnAttackHitboxOn";
                hasOff |= events[j].functionName == "OnAttackHitboxOff";
            }
        }

        if (!hasOn || !hasOff)
        {
            result.Add(
                EnemyCombatIssueLevel.Warning,
                "ATTACK_EVENTS_UNCONFIRMED",
                "Animator Controller의 클립에서 OnAttackHitboxOn/Off 이벤트 쌍을 확인하지 못했습니다. Play Mode에서 공격 판정 구간을 확인하세요.");
        }
    }

    private static void AddBranchesFromComponents<T>(
        GameObject root,
        Transform targetRigRoot,
        T[] components,
        HashSet<Transform> candidates) where T : Component
    {
        for (int i = 0; i < components.Length; i++)
        {
            Transform branch = GetRootBranch(root.transform, components[i].transform);
            AddLegacyCandidate(root.transform, targetRigRoot, branch, candidates);
        }
    }

    private static void AddLegacyCandidate(
        Transform root,
        Transform targetRigRoot,
        Transform candidate,
        HashSet<Transform> candidates)
    {
        if (candidate == null || candidate == root || candidate == targetRigRoot)
        {
            return;
        }

        if (candidate.IsChildOf(targetRigRoot) || targetRigRoot.IsChildOf(candidate))
        {
            return;
        }

        candidates.Add(candidate);
    }

    private static Transform GetRootBranch(Transform root, Transform child)
    {
        if (root == null || child == null || (child != root && !child.IsChildOf(root)))
        {
            return null;
        }

        Transform current = child;
        while (current.parent != null && current.parent != root)
        {
            current = current.parent;
        }

        return current.parent == root ? current : null;
    }

    private static string DescribeLegacyEvidence(Transform candidate)
    {
        List<string> evidence = new List<string>();
        int hitboxes = candidate.GetComponentsInChildren<Hitbox>(true).Length;
        int melees = candidate.GetComponentsInChildren<Melee>(true).Length;
        int bodies = candidate.GetComponentsInChildren<Rigidbody>(true).Length;
        int joints = candidate.GetComponentsInChildren<CharacterJoint>(true).Length;

        if (hitboxes > 0) evidence.Add($"Hitbox {hitboxes}");
        if (melees > 0) evidence.Add($"Melee {melees}");
        if (bodies > 0) evidence.Add($"Rigidbody {bodies}");
        if (joints > 0) evidence.Add($"Joint {joints}");
        if (candidate.GetComponentInChildren<Animator>(true) != null) evidence.Add("Animator");

        return evidence.Count > 0 ? string.Join(", ", evidence) : "이전 Humanoid 골격";
    }
}
