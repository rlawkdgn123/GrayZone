using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class ShelterSkyBackgroundPreview
{
    private const string TargetScenePath =
        "Assets/0.Scenes/HaYW/Shelter_Grey_Boxing_BackUp_2.unity";

    private const string BackgroundSkyboxPath =
        "Assets/3.Resources/Sky/Sky_1.mat";

    static ShelterSkyBackgroundPreview()
    {
        SceneView.duringSceneGui += ApplyBackgroundSkybox;
        EditorSceneManager.sceneOpened += OnSceneOpened;
        AssemblyReloadEvents.beforeAssemblyReload += RemoveBackgroundSkyboxes;
        EditorApplication.quitting += RemoveBackgroundSkyboxes;
        EditorApplication.delayCall += ApplyInitialOverrides;
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (scene.path == TargetScenePath)
        {
            ApplyInitialOverrides();
        }
    }

    private static void ApplyInitialOverrides()
    {
        Material backgroundSkybox = LoadBackgroundSkybox();

        if (backgroundSkybox != null &&
            SceneManager.GetActiveScene().path == TargetScenePath)
        {
            ApplyGameCameraBackground(backgroundSkybox);
        }

        SceneView.RepaintAll();
    }

    private static void ApplyBackgroundSkybox(SceneView sceneView)
    {
        if (sceneView == null || sceneView.camera == null)
        {
            return;
        }

        if (SceneManager.GetActiveScene().path != TargetScenePath)
        {
            RemoveBackgroundSkybox(sceneView.camera);
            return;
        }

        Material backgroundSkybox = LoadBackgroundSkybox();

        if (backgroundSkybox == null)
        {
            return;
        }

        ApplyGameCameraBackground(backgroundSkybox);

        Camera sceneCamera = sceneView.camera;
        Skybox skybox = sceneCamera.GetComponent<Skybox>();

        if (skybox == null)
        {
            skybox = sceneCamera.gameObject.AddComponent<Skybox>();
            skybox.hideFlags = HideFlags.HideAndDontSave;
        }

        skybox.material = backgroundSkybox;
        skybox.enabled = true;
        sceneCamera.clearFlags = CameraClearFlags.Skybox;
    }

    private static Material LoadBackgroundSkybox()
    {
        return AssetDatabase.LoadAssetAtPath<Material>(BackgroundSkyboxPath);
    }

    private static void ApplyGameCameraBackground(Material backgroundSkybox)
    {
        Camera[] cameras = Object.FindObjectsByType<Camera>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (Camera camera in cameras)
        {
            if (camera.gameObject.scene.path != TargetScenePath)
            {
                continue;
            }

            Skybox skybox = camera.GetComponent<Skybox>();

            if (skybox == null)
            {
                skybox = camera.gameObject.AddComponent<Skybox>();
            }

            camera.clearFlags = CameraClearFlags.Skybox;
            skybox.material = backgroundSkybox;
            skybox.enabled = true;
        }
    }

    private static void RemoveBackgroundSkyboxes()
    {
        foreach (SceneView sceneView in SceneView.sceneViews)
        {
            if (sceneView != null && sceneView.camera != null)
            {
                RemoveBackgroundSkybox(sceneView.camera);
            }
        }
    }

    private static void RemoveBackgroundSkybox(Camera sceneCamera)
    {
        Skybox skybox = sceneCamera.GetComponent<Skybox>();

        if (skybox == null ||
            (skybox.hideFlags & HideFlags.HideAndDontSave) == 0)
        {
            return;
        }

        Material backgroundSkybox = LoadBackgroundSkybox();

        if (skybox.material == backgroundSkybox)
        {
            Object.DestroyImmediate(skybox);
        }
    }
}
