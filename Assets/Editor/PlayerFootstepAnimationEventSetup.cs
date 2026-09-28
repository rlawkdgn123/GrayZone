using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 현재 플레이어 Animator가 사용하는 이동 FBX를 샘플링해 양발 접지 시점에 발소리 이벤트를 추가합니다.
/// </summary>
public static class PlayerFootstepAnimationEventSetup
{
    private const string MenuPath = "GrayZone/Audio/Apply Player Footstep Animation Events";
    private const string PlayerPrefabPath = "Assets/2.Prefabs/Player/Narin.prefab";
    private const string AnimatorOverrideControllerPath =
        "Assets/3.Resources/Animation/Playerble/Narin_PlayerInputsThirdPerson.overrideController";
    private const string FootstepFunctionName = "OnFootstep";
    private const int SampleCount = 240;

    private static readonly string[] LocomotionFolders =
    {
        "Assets/3.Resources/Animation/Playerble/Crouch_Walk/",
        "Assets/3.Resources/Animation/Playerble/Run/",
        "Assets/3.Resources/Animation/Playerble/Crouch_Aim/",
        "Assets/3.Resources/Animation/Playerble/Walk_Aim/",
        "Assets/3.Resources/Animation/Playerble/StrafeJog/",
    };

    private sealed class ContactPlan
    {
        public string AssetPath;
        public string ClipName;
        public float LeftFootTime;
        public float RightFootTime;
    }

    [MenuItem(MenuPath)]
    public static void Apply()
    {
        AnimatorOverrideController controller =
            AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(AnimatorOverrideControllerPath);
        if (controller == null)
        {
            throw new InvalidOperationException(
                $"Animator Override Controller를 찾지 못했습니다: {AnimatorOverrideControllerPath}");
        }

        List<ContactPlan> plans = BuildContactPlans(controller);
        if (plans.Count == 0)
        {
            throw new InvalidOperationException("발소리 이벤트를 적용할 이동 FBX를 찾지 못했습니다.");
        }

        int updatedCount = 0;
        foreach (ContactPlan plan in plans)
        {
            if (ApplyEvents(plan))
            {
                updatedCount++;
            }
        }

        AssetDatabase.Refresh();
        int verifiedCount = VerifyEvents(plans);
        Debug.Log(
            $"[PlayerFootstepAnimationEventSetup] 적용 완료: 대상={plans.Count}, " +
            $"메타 갱신={updatedCount}, 검증 통과={verifiedCount}");
    }

    private static List<ContactPlan> BuildContactPlans(AnimatorOverrideController controller)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            Animator animator = root.GetComponent<Animator>();
            if (animator == null)
            {
                animator = root.GetComponentInChildren<Animator>(true);
            }

            if (animator == null || !animator.isHuman)
            {
                throw new InvalidOperationException($"Humanoid Animator를 찾지 못했습니다: {PlayerPrefabPath}");
            }

            Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (leftFoot == null || rightFoot == null)
            {
                throw new InvalidOperationException("플레이어 리그에서 좌우 발 본을 찾지 못했습니다.");
            }

            var plans = new List<ContactPlan>();
            var visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (AnimationClip clip in controller.animationClips)
            {
                if (clip == null)
                {
                    continue;
                }

                string assetPath = AssetDatabase.GetAssetPath(clip);
                if (!IsLocomotionFbx(assetPath) || !visitedPaths.Add(assetPath))
                {
                    continue;
                }

                plans.Add(SampleFootContacts(root, leftFoot, rightFoot, clip, assetPath));
            }

            plans.Sort((left, right) => string.CompareOrdinal(left.AssetPath, right.AssetPath));
            return plans;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool IsLocomotionFbx(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath) ||
            !assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (string folder in LocomotionFolders)
        {
            if (assetPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static ContactPlan SampleFootContacts(
        GameObject root,
        Transform leftFoot,
        Transform rightFoot,
        AnimationClip clip,
        string assetPath)
    {
        float leftMinimum = float.MaxValue;
        float rightMinimum = float.MaxValue;
        float leftNormalizedTime = 0.0f;
        float rightNormalizedTime = 0.0f;

        for (int index = 0; index < SampleCount; index++)
        {
            float normalizedTime = index / (float)SampleCount;
            clip.SampleAnimation(root, normalizedTime * clip.length);

            float rootY = root.transform.position.y;
            float leftHeight = leftFoot.position.y - rootY;
            float rightHeight = rightFoot.position.y - rootY;

            if (leftHeight < leftMinimum)
            {
                leftMinimum = leftHeight;
                leftNormalizedTime = normalizedTime;
            }

            if (rightHeight < rightMinimum)
            {
                rightMinimum = rightHeight;
                rightNormalizedTime = normalizedTime;
            }
        }

        return new ContactPlan
        {
            AssetPath = assetPath,
            ClipName = clip.name,
            LeftFootTime = leftNormalizedTime * clip.length,
            RightFootTime = rightNormalizedTime * clip.length,
        };
    }

    private static bool ApplyEvents(ContactPlan plan)
    {
        ModelImporter importer = AssetImporter.GetAtPath(plan.AssetPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogWarning($"[PlayerFootstepAnimationEventSetup] ModelImporter가 아닙니다: {plan.AssetPath}");
            return false;
        }

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
        {
            clips = importer.defaultClipAnimations;
        }

        bool found = false;
        for (int index = 0; index < clips.Length; index++)
        {
            ModelImporterClipAnimation clip = clips[index];
            if (!string.Equals(clip.name, plan.ClipName, StringComparison.Ordinal))
            {
                continue;
            }

            found = true;
            var events = new List<AnimationEvent>();
            if (clip.events != null)
            {
                foreach (AnimationEvent existing in clip.events)
                {
                    if (!string.Equals(existing.functionName, FootstepFunctionName, StringComparison.Ordinal))
                    {
                        events.Add(existing);
                    }
                }
            }

            events.Add(CreateFootstepEvent(plan.LeftFootTime));
            events.Add(CreateFootstepEvent(plan.RightFootTime));
            events.Sort((left, right) => left.time.CompareTo(right.time));
            clip.events = events.ToArray();
            clips[index] = clip;
        }

        if (!found)
        {
            Debug.LogWarning(
                $"[PlayerFootstepAnimationEventSetup] 클립 설정을 찾지 못했습니다: " +
                $"{plan.ClipName} ({plan.AssetPath})");
            return false;
        }

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        return true;
    }

    private static AnimationEvent CreateFootstepEvent(float time)
    {
        return new AnimationEvent
        {
            functionName = FootstepFunctionName,
            time = time,
        };
    }

    private static int VerifyEvents(IEnumerable<ContactPlan> plans)
    {
        int verifiedCount = 0;
        foreach (ContactPlan plan in plans)
        {
            AnimationClip clip = FindClip(plan.AssetPath, plan.ClipName);
            if (clip == null)
            {
                continue;
            }

            int footstepEventCount = 0;
            foreach (AnimationEvent animationEvent in AnimationUtility.GetAnimationEvents(clip))
            {
                if (string.Equals(animationEvent.functionName, FootstepFunctionName, StringComparison.Ordinal))
                {
                    footstepEventCount++;
                }
            }

            if (footstepEventCount == 2)
            {
                verifiedCount++;
            }
            else
            {
                Debug.LogWarning(
                    $"[PlayerFootstepAnimationEventSetup] 검증 실패: {plan.ClipName}, " +
                    $"OnFootstep={footstepEventCount}");
            }
        }

        return verifiedCount;
    }

    private static AnimationClip FindClip(string assetPath, string clipName)
    {
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
        {
            if (asset is AnimationClip clip &&
                string.Equals(clip.name, clipName, StringComparison.Ordinal))
            {
                return clip;
            }
        }

        return null;
    }
}
