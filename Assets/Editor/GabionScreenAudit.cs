using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class GabionScreenAudit
{
    private static int frames;

    static GabionScreenAudit()
    {
        EditorApplication.update += Run;
    }

    private static void Run()
    {
        if (EditorApplication.isCompiling || ++frames < 12) return;
        var view = SceneView.lastActiveSceneView;
        var camera = view != null ? view.camera : null;
        if (camera == null || camera.pixelWidth < 100 || camera.transform.position.sqrMagnitude < 1f) return;
        EditorApplication.update -= Run;

        Debug.Log($"[GabionScreenAudit] cameraPos={camera.transform.position:F3}, cameraRot={camera.transform.eulerAngles:F3}, pixels={camera.pixelWidth}x{camera.pixelHeight}, ortho={camera.orthographic}, size={camera.orthographicSize:F3}");

        var roots = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(t => t.gameObject.scene.IsValid() && t.gameObject.activeInHierarchy)
            .Where(IsGabionRoot)
            .Where(t => t.position.x >= 68f && t.position.x <= 83f && t.position.z >= 54f && t.position.z <= 92f)
            .OrderByDescending(t => t.position.z)
            .ThenBy(t => t.position.y)
            .ToArray();

        foreach (var t in roots)
        {
            var renderers = t.GetComponentsInChildren<Renderer>(true);
            var center = renderers.Length > 0 ? renderers[0].bounds.center : t.position;
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                center = bounds.center;
            }

            var p = camera.WorldToScreenPoint(center);
            var gui = new Vector2(p.x, camera.pixelHeight - p.y);
            Debug.Log($"[GabionScreenAudit] name={t.name}, pos={t.position:F3}, gui={gui:F1}, depth={p.z:F2}");
        }
    }

    private static bool IsGabionRoot(Transform t)
    {
        if (!t.name.Contains("SM_Gabion_A", StringComparison.OrdinalIgnoreCase)) return false;
        return PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject) == t.gameObject;
    }
}
