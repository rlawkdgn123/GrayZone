using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class CodexApplyMountainGround3Texture
{
    private const string TexturePath = "Assets/3.Resources/GrayBoxing/Textures/Terrain/MountainGround3_GrayZoneConcept.png";
    private const string LayerPath = "Assets/3.Resources/ThirdParty/Mountain Forest/Assets/Terrain Assets/Terrain Layers/TerrainLayer-MountainGround3.terrainlayer";

    static CodexApplyMountainGround3Texture()
    {
        EditorApplication.delayCall += Apply;
    }

    private static void Apply()
    {
        AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerPath);
        if (texture == null || layer == null)
        {
            Debug.LogError($"[CodexMountainGround3Texture] APPLY_FAILED texture={texture != null} layer={layer != null}");
            return;
        }

        Undo.RecordObject(layer, "Apply MountainGround3 GrayZone Texture");
        layer.diffuseTexture = texture;

        // Keep the original TerrainLayer material controls unchanged. The new
        // texture contains the complete concept colour treatment.
        layer.specular = new Color(0f, 0f, 0f, 0f);
        layer.metallic = 0f;
        layer.smoothness = 0f;
        layer.normalScale = 1f;
        layer.diffuseRemapMin = Vector4.zero;
        layer.diffuseRemapMax = Vector4.one;

        EditorUtility.SetDirty(layer);
        AssetDatabase.SaveAssets();
        SceneView.RepaintAll();

        Debug.Log($"[CodexMountainGround3Texture] APPLY_DONE texture='{TexturePath}' size={texture.width}x{texture.height} layer='{LayerPath}'");
    }
}
