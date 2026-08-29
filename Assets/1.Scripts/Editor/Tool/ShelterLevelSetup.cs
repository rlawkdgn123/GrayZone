using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GrayZone.EditorTools
{
    /// <summary>
    /// Creates the Shelter_Level work scene and extracts its authored room roots into dedicated prefabs.
    /// Shared models, materials, textures, and other existing project assets keep their original references.
    /// </summary>
    public static class ShelterLevelSetup
    {
        private const string SourceScenePath = "Assets/0.Scenes/HaYW/Shelter_Grey_Boxing_BackUp_2.unity";
        private const string DestinationScenePath = "Assets/0.Scenes/YeongHae/Shelter_Level.unity";
        private const string ResourceRootPath = "Assets/3.Resources/YeongHaeEdit/ShelterBuildingResources";
        private const string AutoRunSessionKey = "GrayZone.ShelterLevelSetup.AutoRunComplete";

        private static readonly string[] TargetNames =
        {
            "Operations_Room",
            "Medical_Room",
            "Power_Room",
            "Production_Room",
            "Grard_Post",
        };

        [InitializeOnLoadMethod]
        private static void RunOnceAfterCompilation()
        {
            EditorApplication.delayCall += () =>
            {
                if (SessionState.GetBool(AutoRunSessionKey, false) ||
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(DestinationScenePath) != null)
                {
                    return;
                }

                SessionState.SetBool(AutoRunSessionKey, true);
                CreateShelterLevel();
            };
        }

        [MenuItem("Tools/GrayZone/Shelter Level/Create Shelter Level")]
        public static void CreateShelterLevel()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DestinationScenePath) != null)
            {
                Debug.LogWarning($"[ShelterLevel] {DestinationScenePath} already exists. No files were changed.");
                return;
            }

            EnsureAssetFolder(Path.GetDirectoryName(DestinationScenePath)?.Replace('\\', '/'));
            EnsureAssetFolder(ResourceRootPath);

            if (!AssetDatabase.CopyAsset(SourceScenePath, DestinationScenePath))
            {
                Debug.LogError($"[ShelterLevel] Could not copy {SourceScenePath} to {DestinationScenePath}.");
                return;
            }

            var workScene = EditorSceneManager.OpenScene(DestinationScenePath, OpenSceneMode.Additive);
            try
            {
                foreach (var targetName in TargetNames)
                {
                    var root = FindLargestNamedRoot(workScene, targetName);
                    if (root == null)
                    {
                        Debug.LogError($"[ShelterLevel] Could not find a root named {targetName} in {DestinationScenePath}.");
                        continue;
                    }

                    var targetFolder = $"{ResourceRootPath}/{targetName}/Prefabs";
                    EnsureAssetFolder(targetFolder);
                    var prefabPath = $"{targetFolder}/{targetName}.prefab";
                    PrefabUtility.SaveAsPrefabAssetAndConnect(root, prefabPath, InteractionMode.AutomatedAction);

                    if (root.GetComponentsInChildren<Transform>(true).Length == 1)
                    {
                        Debug.LogWarning($"[ShelterLevel] {targetName} contains only an empty Transform; its prefab is a marker without visible room content.");
                    }
                }

                EditorSceneManager.SaveScene(workScene);
                AssetDatabase.SaveAssets();
                Debug.Log($"[ShelterLevel] Created {DestinationScenePath} and room prefabs under {ResourceRootPath}.");
            }
            finally
            {
                EditorSceneManager.CloseScene(workScene, true);
            }
        }

        [MenuItem("Tools/GrayZone/Shelter Level/Report Target Resources")]
        public static void ReportTargets()
        {
            var scene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
            var matches = new List<GameObject>();

            foreach (var root in scene.GetRootGameObjects())
            {
                CollectNamedObjects(root.transform, matches);
            }

            Debug.Log($"[ShelterLevel] Found {matches.Count} exact target objects in {SourceScenePath}.");
            foreach (var target in matches)
            {
                var prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(target);
                var prefabPath = prefabRoot == null
                    ? "<scene object>"
                    : AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(prefabRoot));

                Debug.Log($"[ShelterLevel] name={target.name} | hierarchy={GetHierarchyPath(target.transform)} | prefab={prefabPath}");
            }
        }

        private static void CollectNamedObjects(Transform current, ICollection<GameObject> matches)
        {
            if (Array.IndexOf(TargetNames, current.name) >= 0)
            {
                matches.Add(current.gameObject);
            }

            foreach (Transform child in current)
            {
                CollectNamedObjects(child, matches);
            }
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var names = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
            {
                names.Push(current.name);
            }

            return string.Join("/", names);
        }

        private static GameObject FindLargestNamedRoot(Scene scene, string name)
        {
            GameObject result = null;
            var largestHierarchySize = -1;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!string.Equals(root.name, name, StringComparison.Ordinal))
                {
                    continue;
                }

                var hierarchySize = root.GetComponentsInChildren<Transform>(true).Length;
                if (hierarchySize > largestHierarchySize)
                {
                    result = root;
                    largestHierarchySize = hierarchySize;
                }
            }

            return result;
        }

        private static void EnsureAssetFolder(string assetFolderPath)
        {
            if (string.IsNullOrWhiteSpace(assetFolderPath) || AssetDatabase.IsValidFolder(assetFolderPath))
            {
                return;
            }

            var segments = assetFolderPath.Split('/');
            var currentPath = segments[0];
            for (var index = 1; index < segments.Length; index++)
            {
                var nextPath = $"{currentPath}/{segments[index]}";
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, segments[index]);
                }

                currentPath = nextPath;
            }
        }
    }
}
