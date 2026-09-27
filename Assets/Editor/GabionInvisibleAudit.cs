using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
internal static class GabionInvisibleAudit
{
    private const string SessionKey = "Codex.GabionInvisibleAudit.20260924.3";

    static GabionInvisibleAudit()
    {
        EditorApplication.delayCall += Run;
    }

    private static void Run()
    {
        if (SessionState.GetBool(SessionKey, false)) return;
        SessionState.SetBool(SessionKey, true);

        var scene = EditorSceneManager.GetActiveScene();
        var selected = Selection.gameObjects;
        var sceneRenderers = scene.GetRootGameObjects()
            .SelectMany(go => go.GetComponentsInChildren<Renderer>(true))
            .Where(r => r.gameObject.activeInHierarchy && !IsGabionInAncestry(r.transform))
            .ToArray();
        Debug.Log($"[GabionInvisibleAudit] scene={scene.path}, selected={selected.Length}, active={GetPath(Selection.activeGameObject?.transform)}");

        var camera = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
        if (camera != null)
            Debug.Log($"[GabionInvisibleAudit] camera pos={camera.transform.position:F3}, rot={camera.transform.eulerAngles:F3}, orthographic={camera.orthographic}, size={camera.orthographicSize:F3}");

        foreach (var root in selected)
        {
            var gabions = root.GetComponentsInChildren<Transform>(true)
                .Where(t => IsGabionRoot(t))
                .OrderBy(t => t.position.x)
                .ThenBy(t => t.position.z)
                .ThenBy(t => t.position.y)
                .ToArray();

            Debug.Log($"[GabionInvisibleAudit] SELECTED_ROOT path={GetPath(root.transform)}, gabionRoots={gabions.Length}");
            for (var i = 0; i < gabions.Length; i++)
            {
                var t = gabions[i];
                var renderers = t.GetComponentsInChildren<Renderer>(true);
                var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(t.position, Vector3.zero);
                for (var r = 1; r < renderers.Length; r++) bounds.Encapsulate(renderers[r].bounds);

                var overlapNames = Physics.OverlapBox(bounds.center, bounds.extents * 0.92f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)
                    .Select(c => c.transform)
                    .Where(o => !o.IsChildOf(t) && !t.IsChildOf(o) && !IsGabionInAncestry(o))
                    .Select(GetPath)
                    .Distinct()
                    .Take(6)
                    .ToArray();

                var rendererOverlaps = sceneRenderers
                    .Select(r => new { Renderer = r, Ratio = IntersectionVolume(bounds, r.bounds) / Mathf.Max(0.0001f, BoundsVolume(bounds)) })
                    .Where(x => x.Ratio > 0.015f)
                    .OrderByDescending(x => x.Ratio)
                    .Take(8)
                    .Select(x => $"{x.Ratio:F2}:{GetPath(x.Renderer.transform)}")
                    .ToArray();

                if (t.position.x >= 68f && t.position.x <= 83f && t.position.z >= 54f && t.position.z <= 92f)
                    Debug.Log($"[GabionInvisibleAudit] FOCUS {i:D2} name={t.name}, pos={t.position:F3}, rotY={t.eulerAngles.y:F3}, visible={renderers.Any(x => x.isVisible)}, colliderOverlaps=[{string.Join(" | ", overlapNames)}], rendererOverlaps=[{string.Join(" | ", rendererOverlaps)}]");
            }
        }

        Selection.objects = Array.Empty<UnityEngine.Object>();
        SceneView.RepaintAll();
        Debug.Log("[GabionInvisibleAudit] selection-cleared-for-visual-check");
    }

    private static float BoundsVolume(Bounds b)
    {
        return b.size.x * b.size.y * b.size.z;
    }

    private static float IntersectionVolume(Bounds a, Bounds b)
    {
        var x = Mathf.Max(0f, Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x));
        var y = Mathf.Max(0f, Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y));
        var z = Mathf.Max(0f, Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z));
        return x * y * z;
    }

    private static bool IsGabionRoot(Transform t)
    {
        if (!t.name.Contains("SM_Gabion_A", StringComparison.OrdinalIgnoreCase)) return false;
        var nearest = PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject);
        return nearest == t.gameObject;
    }

    private static bool IsGabionInAncestry(Transform t)
    {
        for (var p = t; p != null; p = p.parent)
            if (p.name.Contains("SM_Gabion_A", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string GetPath(Transform t)
    {
        if (t == null) return "<null>";
        var path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}
