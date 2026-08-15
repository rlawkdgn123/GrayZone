using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class CharacterLodBinder : MonoBehaviour
{
    [SerializeField] private GameObject lodPrefab;
    [SerializeField] private string lodObjectName = "AI_Monster_rig_LODGroup_test_LODGroup";
    [SerializeField] private string[] staleLodObjectNames =
    {
        "AI_Monster_rig_LOD_LODGroup",
        "AI_Monster_rig_LODGroup"
    };
    [SerializeField] private float lod0ScreenRelativeHeight = 0.55f;
    [SerializeField] private float lod1ScreenRelativeHeight = 0.3f;
    [SerializeField] private float lod2ScreenRelativeHeight = 0.15f;
    [SerializeField] private float lod3ScreenRelativeHeight = 0.05f;
    [SerializeField] private bool keepBoneBindingsSynced = true;

#if UNITY_EDITOR
    private bool applyQueued;
#endif
    private Transform cachedLodRoot;

    private void OnEnable()
    {
        QueueApply();
    }

    private void Start()
    {
        QueueApply();
    }

    private void OnValidate()
    {
        lod0ScreenRelativeHeight = Mathf.Clamp01(lod0ScreenRelativeHeight);
        lod1ScreenRelativeHeight = Mathf.Clamp01(lod1ScreenRelativeHeight);
        lod2ScreenRelativeHeight = Mathf.Clamp01(lod2ScreenRelativeHeight);
        lod3ScreenRelativeHeight = Mathf.Clamp01(lod3ScreenRelativeHeight);
        cachedLodRoot = null;
        QueueApply();
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying || !keepBoneBindingsSynced)
        {
            return;
        }

        Transform lodRoot = ResolveExistingLodRoot();
        if (lodRoot != null)
        {
            RemapSkinnedMeshBones(lodRoot);
        }
    }

    private void QueueApply()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            if (applyQueued)
            {
                return;
            }

            applyQueued = true;
            EditorApplication.delayCall += DelayedApply;
            return;
        }
#endif

        Apply();
    }

#if UNITY_EDITOR
    private void DelayedApply()
    {
        applyQueued = false;
        if (this == null)
        {
            return;
        }

        Apply();
    }
#endif

    private void Apply()
    {
        if (!gameObject.scene.IsValid())
        {
            return;
        }

        CleanupStaleLodChildren();
        Transform lodRoot = ResolveOrCreateLodRoot();
        if (lodRoot == null)
        {
            return;
        }

        NormalizeLodTransform(lodRoot);
        DisableNestedControllers(lodRoot);
        RemapSkinnedMeshBones(lodRoot);
        ConfigureLodGroup(lodRoot);
        MarkSceneDirty();
    }

    private Transform ResolveExistingLodRoot()
    {
        if (cachedLodRoot != null)
        {
            return cachedLodRoot;
        }

        cachedLodRoot = transform.Find(lodObjectName);
        return cachedLodRoot;
    }

    private Transform ResolveOrCreateLodRoot()
    {
        Transform child = ResolveExistingLodRoot();
        if (child != null)
        {
            return child;
        }

        Transform existing = FindSceneObject(lodObjectName);
        if (existing != null)
        {
            existing.SetParent(transform, true);
            return existing;
        }

        if (lodPrefab == null)
        {
            return null;
        }

        GameObject instance;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            instance = PrefabUtility.InstantiatePrefab(lodPrefab, transform) as GameObject;
        }
        else
#endif
        {
            instance = Instantiate(lodPrefab, transform);
        }

        if (instance == null)
        {
            return null;
        }

        instance.name = lodObjectName;
        cachedLodRoot = instance.transform;
        return instance.transform;
    }

    private void CleanupStaleLodChildren()
    {
        if (staleLodObjectNames == null)
        {
            return;
        }

        foreach (string staleName in staleLodObjectNames)
        {
            if (string.IsNullOrWhiteSpace(staleName) || staleName == lodObjectName)
            {
                continue;
            }

            Transform stale = transform.Find(staleName);
            if (stale != null)
            {
                DestroyLodObject(stale.gameObject);
            }
        }
    }

    private static void DestroyLodObject(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
            return;
        }

#if UNITY_EDITOR
        DestroyImmediate(target);
#endif
    }

    private Transform FindSceneObject(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return null;
        }

        GameObject[] roots = gameObject.scene.GetRootGameObjects();
        foreach (GameObject root in roots)
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in children)
            {
                if (child == transform || child.IsChildOf(transform))
                {
                    continue;
                }

                if (child.name == objectName)
                {
                    return child;
                }
            }
        }

        return null;
    }

    private static void NormalizeLodTransform(Transform lodRoot)
    {
        lodRoot.localPosition = Vector3.zero;
        lodRoot.localRotation = Quaternion.identity;
        lodRoot.localScale = Vector3.one;
    }

    private void DisableNestedControllers(Transform lodRoot)
    {
        Animator[] animators = lodRoot.GetComponentsInChildren<Animator>(true);
        foreach (Animator animator in animators)
        {
            animator.enabled = false;
        }

        Animation[] animations = lodRoot.GetComponentsInChildren<Animation>(true);
        foreach (Animation animation in animations)
        {
            animation.enabled = false;
        }

        MeshRenderer[] staticRenderers = lodRoot.GetComponentsInChildren<MeshRenderer>(true);
        foreach (MeshRenderer staticRenderer in staticRenderers)
        {
            staticRenderer.enabled = false;
        }

        LODGroup hostLodGroup = GetComponent<LODGroup>();
        LODGroup[] nestedGroups = lodRoot.GetComponentsInChildren<LODGroup>(true);
        foreach (LODGroup nestedGroup in nestedGroups)
        {
            if (nestedGroup != hostLodGroup)
            {
                nestedGroup.enabled = false;
            }
        }
    }

    private void RemapSkinnedMeshBones(Transform lodRoot)
    {
        Dictionary<string, Transform> hostBones = new Dictionary<string, Transform>(StringComparer.Ordinal);
        Transform[] hostTransforms = GetComponentsInChildren<Transform>(true);
        foreach (Transform hostTransform in hostTransforms)
        {
            if (ShouldSkipHostTransform(hostTransform, lodRoot))
            {
                continue;
            }

            hostBones[hostTransform.name] = hostTransform;
            string normalizedName = NormalizeBoneName(hostTransform.name);
            if (!hostBones.ContainsKey(normalizedName))
            {
                hostBones.Add(normalizedName, hostTransform);
            }
        }

        SkinnedMeshRenderer[] skinnedMeshes = lodRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (SkinnedMeshRenderer skinnedMesh in skinnedMeshes)
        {
            Transform[] bones = skinnedMesh.bones;
            bool changed = false;

            for (int i = 0; i < bones.Length; i++)
            {
                Transform bone = bones[i];
                if (bone != null && TryGetHostBone(hostBones, bone.name, out Transform hostBone))
                {
                    bones[i] = hostBone;
                    changed = true;
                }
            }

            if (changed)
            {
                skinnedMesh.bones = bones;
            }

            Transform rootBone = skinnedMesh.rootBone;
            if (rootBone != null && TryGetHostBone(hostBones, rootBone.name, out Transform hostRootBone))
            {
                skinnedMesh.rootBone = hostRootBone;
            }
            else if (hostBones.TryGetValue("CC_Base_Hip", out Transform hip))
            {
                skinnedMesh.rootBone = hip;
            }
            else if (hostBones.TryGetValue("CC_Base_Pelvis", out Transform pelvis))
            {
                skinnedMesh.rootBone = pelvis;
            }
        }
    }

    private bool ShouldSkipHostTransform(Transform candidate, Transform activeLodRoot)
    {
        if (candidate == null)
        {
            return true;
        }

        if (candidate == transform)
        {
            return false;
        }

        Transform current = candidate;
        while (current != null && current != transform)
        {
            if (current == activeLodRoot || IsStaleLodRootName(current.name))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private bool IsStaleLodRootName(string candidateName)
    {
        if (staleLodObjectNames == null)
        {
            return false;
        }

        foreach (string staleName in staleLodObjectNames)
        {
            if (!string.IsNullOrWhiteSpace(staleName) && candidateName == staleName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetHostBone(Dictionary<string, Transform> hostBones, string boneName, out Transform hostBone)
    {
        if (hostBones.TryGetValue(boneName, out hostBone))
        {
            return true;
        }

        return hostBones.TryGetValue(NormalizeBoneName(boneName), out hostBone);
    }

    private static string NormalizeBoneName(string boneName)
    {
        if (string.IsNullOrEmpty(boneName))
        {
            return string.Empty;
        }

        string normalized = boneName.Replace("(Clone)", string.Empty).Trim();
        int dotIndex = normalized.LastIndexOf('.');
        if (dotIndex >= 0 && dotIndex < normalized.Length - 1)
        {
            bool numericSuffix = true;
            for (int i = dotIndex + 1; i < normalized.Length; i++)
            {
                if (!char.IsDigit(normalized[i]))
                {
                    numericSuffix = false;
                    break;
                }
            }

            if (numericSuffix)
            {
                normalized = normalized.Substring(0, dotIndex);
            }
        }

        return normalized;
    }

    private void ConfigureLodGroup(Transform lodRoot)
    {
        LODGroup lodGroup = GetComponent<LODGroup>();
        if (lodGroup == null)
        {
            lodGroup = gameObject.AddComponent<LODGroup>();
        }

        List<Renderer> lod0Renderers = new List<Renderer>();
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (!IsLodRenderer(renderer))
            {
                continue;
            }

            if (ShouldSkipHostTransform(renderer.transform, lodRoot))
            {
                continue;
            }

            lod0Renderers.Add(renderer);
        }

        SortedDictionary<int, List<Renderer>> lodRenderers = CollectLodRenderers(lodRoot, lod0Renderers.Count > 0);

        List<LOD> lods = new List<LOD>();
        if (lod0Renderers.Count > 0)
        {
            lods.Add(new LOD(lod0ScreenRelativeHeight, lod0Renderers.ToArray()));
        }

        AddLod(lods, lodRenderers, 1, lod1ScreenRelativeHeight);
        AddLod(lods, lodRenderers, 2, lod2ScreenRelativeHeight);
        AddLod(lods, lodRenderers, 3, lod3ScreenRelativeHeight);

        if (lods.Count < 2 && lodRenderers.TryGetValue(1, out List<Renderer> fallbackRenderers))
        {
            lods.Add(new LOD(lod1ScreenRelativeHeight, fallbackRenderers.ToArray()));
        }

        if (lods.Count == 0)
        {
            return;
        }

        lodGroup.SetLODs(lods.ToArray());
        lodGroup.RecalculateBounds();
        lodGroup.enabled = true;
    }

    private static SortedDictionary<int, List<Renderer>> CollectLodRenderers(Transform lodRoot, bool skipChildLod0)
    {
        SortedDictionary<int, List<Renderer>> lodRenderers = new SortedDictionary<int, List<Renderer>>();
        Renderer[] renderers = lodRoot.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            if (!IsLodRenderer(renderer))
            {
                continue;
            }

            int lodIndex = GetLodIndex(renderer.transform);
            if (skipChildLod0 && lodIndex == 0)
            {
                continue;
            }

            if (lodIndex == 0)
            {
                lodIndex = 1;
            }

            if (!lodRenderers.TryGetValue(lodIndex, out List<Renderer> group))
            {
                group = new List<Renderer>();
                lodRenderers.Add(lodIndex, group);
            }

            group.Add(renderer);
        }

        return lodRenderers;
    }

    private static int GetLodIndex(Transform rendererTransform)
    {
        Transform current = rendererTransform;
        while (current != null)
        {
            string name = current.name.ToLowerInvariant();
            if (name.Contains("lod3"))
            {
                return 3;
            }

            if (name.Contains("lod2"))
            {
                return 2;
            }

            if (name.Contains("lod1"))
            {
                return 1;
            }

            if (name.Contains("lod0"))
            {
                return 0;
            }

            current = current.parent;
        }

        return 0;
    }

    private static bool IsLodRenderer(Renderer renderer)
    {
        return renderer is SkinnedMeshRenderer;
    }

    private static void AddLod(List<LOD> lods, SortedDictionary<int, List<Renderer>> lodRenderers, int index, float screenRelativeHeight)
    {
        if (!lodRenderers.TryGetValue(index, out List<Renderer> renderers) || renderers.Count == 0)
        {
            return;
        }

        lods.Add(new LOD(screenRelativeHeight, renderers.ToArray()));
    }

    private void MarkSceneDirty()
    {
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            return;
        }

        EditorUtility.SetDirty(this);
        LODGroup lodGroup = GetComponent<LODGroup>();
        if (lodGroup != null)
        {
            EditorUtility.SetDirty(lodGroup);
        }

        EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }
}
