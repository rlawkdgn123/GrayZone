using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Restores the missing X/Y Bot model materials as URP/Lit materials.
/// The models originated in a legacy project, so their imported materials can
/// otherwise resolve to an unsupported shader and render magenta in this URP project.
/// </summary>
[InitializeOnLoad]
internal static class XYBotUrpMaterialRepair
{
    private const string RepairSessionKey = "GrayZone.XYBotUrpMaterialRepair.Completed";

    private static readonly string[] ModelPaths =
    {
        "Assets/3.Resources/ThirdParty/XY_Bot/X Bot.fbx",
        "Assets/3.Resources/ThirdParty/XY_Bot/Y Bot.fbx",
    };

    static XYBotUrpMaterialRepair()
    {
        EditorApplication.delayCall += RepairAfterImport;
    }

    [MenuItem("Tools/GrayZone/Repair X-Y Bot URP Materials")]
    private static void RepairFromMenu()
    {
        RepairModels();
    }

    private static void RepairAfterImport()
    {
        if (SessionState.GetBool(RepairSessionKey, false))
        {
            return;
        }

        SessionState.SetBool(RepairSessionKey, true);
        RepairModels();
    }

    private static void RepairModels()
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            Debug.LogError("[XY Bot] Universal Render Pipeline/Lit shader was not found.");
            return;
        }

        int repaired = 0;
        foreach (string modelPath in ModelPaths)
        {
            ModelImporter importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[XY Bot] Model not found: {modelPath}");
                continue;
            }

            List<Material> embeddedMaterials = new List<Material>();
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            {
                if (asset is Material material)
                {
                    embeddedMaterials.Add(material);
                }
            }

            if (embeddedMaterials.Count == 0)
            {
                Debug.LogWarning($"[XY Bot] No embedded materials found in: {modelPath}");
                continue;
            }

            string materialFolder = Path.GetDirectoryName(modelPath)?.Replace('\\', '/') + "/Materials";
            EnsureFolder(materialFolder);

            bool changed = false;
            foreach (Material sourceMaterial in embeddedMaterials)
            {
                string materialPath = $"{materialFolder}/{Path.GetFileNameWithoutExtension(modelPath)}_{SafeName(sourceMaterial.name)}_URP.mat";
                Material urpMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (urpMaterial == null)
                {
                    urpMaterial = new Material(urpLit)
                    {
                        name = Path.GetFileNameWithoutExtension(materialPath),
                    };

                    if (sourceMaterial.HasProperty("_Color"))
                    {
                        urpMaterial.SetColor("_BaseColor", sourceMaterial.color);
                    }

                    AssetDatabase.CreateAsset(urpMaterial, materialPath);
                }
                else if (urpMaterial.shader != urpLit)
                {
                    urpMaterial.shader = urpLit;
                    EditorUtility.SetDirty(urpMaterial);
                }

                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), sourceMaterial.name), urpMaterial);
                changed = true;
                repaired++;
            }

            if (changed)
            {
                importer.SaveAndReimport();
            }
        }

        AssetDatabase.SaveAssets();
        if (repaired > 0)
        {
            Debug.Log($"[XY Bot] Repaired {repaired} model material mapping(s) for URP.");
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }

        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static string SafeName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(value) ? "Material" : value;
    }
}
