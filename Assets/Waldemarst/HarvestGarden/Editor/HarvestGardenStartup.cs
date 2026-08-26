using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Broccoli.Utils
{
    [InitializeOnLoad]
    public static class HarvestGardenStartup
    {
        public enum RenderPipelineType { BuiltIn, URP, HDRP }

        public const string PACKAGE_NAME = "Harvest Garden Package";
        public const string PACKAGE_FOLDER = "HarvestGarden";

        // The physical file used to track initialization. Kept as HarvestGarden per your request.
        public const string FINGERPRINT_FILENAME = "HarvestGarden_Initialized.txt";

        private static RenderPipelineType activePipelineCache;
        private static bool isBootComplete = false;

        public static readonly Dictionary<RenderPipelineType, string> StandardShaders = new Dictionary<RenderPipelineType, string>()
        {
            { RenderPipelineType.BuiltIn, "Standard" },
            { RenderPipelineType.URP, "Universal Render Pipeline/Lit" },
            { RenderPipelineType.HDRP, "HDRP/Lit" }
        };

        public static readonly Dictionary<RenderPipelineType, string> SpeedTreeShaders = new Dictionary<RenderPipelineType, string>()
        {
            { RenderPipelineType.BuiltIn, "Nature/SpeedTree8" },
            { RenderPipelineType.URP, "Universal Render Pipeline/Nature/SpeedTree8" },
            { RenderPipelineType.HDRP, "HDRP/Nature/SpeedTree8" }
        };

        static HarvestGardenStartup()
        {
            EditorApplication.update += EditorUpdateTick;
        }

        private static void EditorUpdateTick()
        {
            if (!isBootComplete)
            {
                isBootComplete = true;
                activePipelineCache = DetectRenderPipeline();
                RunBootSequence();
            }
            else
            {
                // WATCHER: Detects if the user changes the Render Pipeline Asset in Project Settings dynamically
                RenderPipelineType currentPipeline = DetectRenderPipeline();
                if (currentPipeline != activePipelineCache)
                {
                    activePipelineCache = currentPipeline;
                    Debug.Log($"[{PACKAGE_NAME}] Render Pipeline change detected: Switched to {currentPipeline}");
                    CheckAndPromptUpgrade(currentPipeline);
                }
            }
        }

        private static void RunBootSequence()
        {
            ReadFingerprint(out bool hasSeenWelcome, out string lastPipeline);
            
            bool pipelineMismatch;

            if (!hasSeenWelcome)
            {
                // Fresh install. Assuming package materials are Built-In by default, 
                // if the project is already URP/HDRP, flag a mismatch immediately.
                pipelineMismatch = (activePipelineCache != RenderPipelineType.BuiltIn);

                // Open the custom welcome window
                BroccoliPackageWelcomeWindow.ShowWindow(pipelineMismatch, activePipelineCache, () => 
                {
                    if (pipelineMismatch)
                    {
                        CheckAndPromptUpgrade(activePipelineCache);
                    }
                    else
                    {
                        // Write the fingerprint so the welcome screen never shows again
                        WriteFingerprint(activePipelineCache);
                    }
                });
            }
            else
            {
                // Previously installed. Check if the pipeline saved in the text file matches the current project.
                pipelineMismatch = (lastPipeline != activePipelineCache.ToString());
                if (pipelineMismatch)
                {
                    CheckAndPromptUpgrade(activePipelineCache);
                }
            }
        }

        public static void CheckAndPromptUpgrade(RenderPipelineType currentPipeline)
        {
            string packageRoot = GetPackageRootPath();
            List<string> upgradeableMaterials = DetectUpgradeableMaterials(packageRoot, currentPipeline);

            if (upgradeableMaterials.Count > 0)
            {
                bool userWantsUpgrade = EditorUtility.DisplayDialog(
                    $"{PACKAGE_NAME}: Pipeline Mismatch Detected",
                    $"Your project is using {currentPipeline}, but {upgradeableMaterials.Count} materials in {PACKAGE_NAME} need to be upgraded to match this render pipeline.\n\n" +
                    "Do you want to automatically upgrade these materials now? (No other materials in your project will be modified).",
                    "Yes, upgrade materials", 
                    "No, leave them alone"
                );

                if (userWantsUpgrade)
                {
                    UpgradeMaterials(upgradeableMaterials, currentPipeline);
                }
            }

            // Save the state to the physical asset so we don't nag again
            WriteFingerprint(currentPipeline);
        }

        // --- FINGERPRINT I/O LOGIC ---

        private static string GetFingerprintPath()
        {
            // Dynamically locates where this script lives using nameof() to survive class renaming
            string[] guids = AssetDatabase.FindAssets($"{nameof(HarvestGardenStartup)} t:Script");
            if (guids.Length > 0)
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                string editorFolderPath = Path.GetDirectoryName(scriptPath);
                return Path.Combine(editorFolderPath, FINGERPRINT_FILENAME).Replace('\\', '/');
            }
            
            // Generic Fallback utilizing customizable folders
            return $"Assets/Waldemarst/{PACKAGE_FOLDER}/Editor/{FINGERPRINT_FILENAME}";
        }

        private static void ReadFingerprint(out bool hasSeenWelcome, out string lastPipeline)
        {
            string path = GetFingerprintPath();
            if (File.Exists(path))
            {
                hasSeenWelcome = true;
                lastPipeline = File.ReadAllText(path).Trim();
            }
            else
            {
                hasSeenWelcome = false;
                lastPipeline = string.Empty;
            }
        }

        public static void WriteFingerprint(RenderPipelineType pipeline)
        {
            try
            {
                string path = GetFingerprintPath();
                File.WriteAllText(path, pipeline.ToString());
                AssetDatabase.ImportAsset(path); // Force Unity to acknowledge the new text file
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{PACKAGE_NAME}] Failed to write initialization fingerprint: {e.Message}");
            }
        }

        // --- UTILITY METHODS ---

        public static RenderPipelineType DetectRenderPipeline()
        {
            var currentPipelineAsset = GraphicsSettings.defaultRenderPipeline;
            if (currentPipelineAsset != null)
            {
                string assetName = currentPipelineAsset.GetType().Name;
                if (assetName.Contains("UniversalRenderPipelineAsset") || assetName.Contains("LightweightPipelineAsset")) return RenderPipelineType.URP;
                if (assetName.Contains("HDRenderPipelineAsset")) return RenderPipelineType.HDRP;
            }
            return RenderPipelineType.BuiltIn;
        }

        public static string GetPackageRootPath()
        {
            // Dynamically locates where this script lives using nameof()
            string[] guids = AssetDatabase.FindAssets($"{nameof(HarvestGardenStartup)} t:Script");
            if (guids.Length > 0)
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                return Path.GetDirectoryName(Path.GetDirectoryName(scriptPath)).Replace('\\', '/');
            }
            return $"Assets/Waldemarst/{PACKAGE_FOLDER}";
        }

        private static List<string> DetectUpgradeableMaterials(string searchPath, RenderPipelineType targetPipeline)
        {
            List<string> filteredPaths = new List<string>();
            if (!AssetDatabase.IsValidFolder(searchPath)) return filteredPaths;

            string targetStandardShader = StandardShaders[targetPipeline];
            string targetSpeedTreeShader = SpeedTreeShaders[targetPipeline];
            string[] allMaterialGUIDs = AssetDatabase.FindAssets("t:Material", new[] { searchPath });

            foreach (var guid in allMaterialGUIDs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (mat != null && mat.shader != null)
                {
                    if (mat.shader.name != targetStandardShader && mat.shader.name != targetSpeedTreeShader)
                    {
                        bool isStandard = mat.shader.name.Contains("Standard") || mat.shader.name.Contains("Lit");
                        bool isSpeedTree = mat.shader.name.Contains("SpeedTree8");

                        if (isStandard || isSpeedTree) filteredPaths.Add(path);
                    }
                }
            }
            return filteredPaths;
        }

        private static void UpgradeMaterials(List<string> materialPaths, RenderPipelineType targetPipeline)
        {
            Shader standardShader = Shader.Find(StandardShaders[targetPipeline]);
            Shader speedTreeShader = Shader.Find(SpeedTreeShaders[targetPipeline]);

            if (standardShader == null || speedTreeShader == null)
            {
                Debug.LogError($"[{PACKAGE_NAME}] Could not find target shaders for pipeline: {targetPipeline}");
                return;
            }

            int upgradedCount = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (string path in materialPaths)
                {
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    bool isSpeedTree = material.shader.name.Contains("SpeedTree8");

                    if (isSpeedTree)
                    {
                        material.shader = speedTreeShader;
                        
                        // Wrap the HDRP logic in preprocessor directives
                        if (targetPipeline == RenderPipelineType.HDRP)
                        {
                            ApplyDiffusionProfile(material);
                        }
                        
                        upgradedCount++;
                    }
                    else
                    {
                        Material oldMaterial = new Material(material);
                        material.shader = standardShader;
                        TransferStandardProperties(oldMaterial, material, targetPipeline);
                        Object.DestroyImmediate(oldMaterial);
                        upgradedCount++;
                    }
                    EditorUtility.SetDirty(material);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            Debug.Log($"[{PACKAGE_NAME}] Successfully upgraded {upgradedCount} materials to {targetPipeline}."); 
        }

        private static string GetProfilePath(string assetName)
        {
            string[] guids = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(assetName));
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(assetName)) return path;
            }
            return string.Empty;
        }

        private static void ApplyDiffusionProfile(Material mat)
        {
            string profilePath = "";
            if (mat.name.Contains("Bark", StringComparison.OrdinalIgnoreCase))
            {
                profilePath = GetProfilePath("HarvestGardenBarkDP.asset");
            }
            else if (mat.name.Contains("Foliage", StringComparison.OrdinalIgnoreCase) || 
                    mat.name.Contains("Leaf", StringComparison.OrdinalIgnoreCase) ||
                    mat.name.Contains("Sprout", StringComparison.OrdinalIgnoreCase))
            {
                profilePath = GetProfilePath("HarvestGardenGreenFoliageDP.asset");
            }

            if (string.IsNullOrEmpty(profilePath)) return;

            Object profileAsset = AssetDatabase.LoadAssetAtPath<Object>(profilePath);
            if (profileAsset == null) return;

            SetDiffusionProfile(mat, (ScriptableObject)profileAsset);
        }

        public static void SetDiffusionProfile (Material material, ScriptableObject diffusionProfile) {
				float hash = GetHashFromDiffusionProfile (diffusionProfile);
				Vector4 guidVector = GetVector4FromScriptableObject (diffusionProfile);
				material.SetFloat ("Diffusion_Profile", hash);
				material.SetVector ("Diffusion_Profile_Asset", guidVector);
                /*
				material.SetFloat ("_Surface", 0f);
				material.SetFloat ("_SurfaceType", 0f);
				material.SetFloat ("_AlphaCutoffEnable", 1f);
				material.SetFloat ("_AlphaClipThreshold", cutoff);
				material.SetFloat ("_OpaqueCullMode", 0f);
				material.SetFloat ("_OpaqueCullMode", 0f);
				material.SetFloat ("_CullMode", 0f);
				material.SetFloat ("_CullModeForward", 0f);
                */
				material.EnableKeyword ("DEBUG_DISPLAY");
				material.EnableKeyword ("_DOUBLESIDED_ON");
				material.EnableKeyword ("_DISABLE_SSR_TRANSPARENT");
				material.EnableKeyword ("_ALPHATEST_ON");
		}

        public static float GetHashFromDiffusionProfile (ScriptableObject diffusionProfileSettings) {
			uint hash = 0;
			System.Reflection.BindingFlags bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
			System.Reflection.FieldInfo profileFieldInfo = diffusionProfileSettings.GetType ().GetField ("profile", bf);
			if (profileFieldInfo != null) {
				object profileObj = profileFieldInfo.GetValue ((object)diffusionProfileSettings);
				System.Reflection.FieldInfo hashFieldInfo = profileObj.GetType ().GetField ("hash", bf);
				if (hashFieldInfo != null) { 
					hash = (uint)hashFieldInfo.GetValue (profileObj);
				}
				return ConvertHashToFloat (hash);
			}
			return 0f;
		}
		public static Vector4 GetVector4FromScriptableObject (ScriptableObject so) {
			Vector4 vector = Vector4.zero;
			#if UNITY_EDITOR
			string pathToAsset = UnityEditor.AssetDatabase.GetAssetPath (so);
			string guid = UnityEditor.AssetDatabase.AssetPathToGUID(pathToAsset);
			vector = ConvertGUIDToVector4 (guid);
			#endif
			return vector;
		}
		private static float ConvertHashToFloat (uint hash) {
			float hashFloat = 0f;
			byte[] bytes = System.BitConverter.GetBytes (hash);
			hashFloat = System.BitConverter.ToSingle (bytes,0*sizeof(float));
			return hashFloat;
		}
		private static Vector4 ConvertGUIDToVector4 (string guid)
        {
            byte[] bytes = new byte[16];
            for (int i = 0; i < 16; i++)
                bytes[i] = byte.Parse(guid.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber);
			Vector4 vect = Vector4.zero;
			vect.x = System.BitConverter.ToSingle(bytes,0*sizeof(float));
			vect.y = System.BitConverter.ToSingle(bytes,1*sizeof(float));
			vect.z = System.BitConverter.ToSingle(bytes,2*sizeof(float));
			vect.w = System.BitConverter.ToSingle(bytes,3*sizeof(float));
			return vect;
        }

        private static void TransferStandardProperties(Material oldMat, Material newMat, RenderPipelineType targetPipeline)
        {
            if (oldMat.HasProperty("_Color")) newMat.SetColor(GetColorProp(targetPipeline), oldMat.GetColor("_Color"));
            else if (oldMat.HasProperty("_BaseColor")) newMat.SetColor(GetColorProp(targetPipeline), oldMat.GetColor("_BaseColor"));

            if (oldMat.HasProperty("_MainTex")) newMat.SetTexture(GetTexProp(targetPipeline), oldMat.GetTexture("_MainTex"));
            else if (oldMat.HasProperty("_BaseMap")) newMat.SetTexture(GetTexProp(targetPipeline), oldMat.GetTexture("_BaseMap"));
            
            if (oldMat.HasProperty("_BumpMap")) newMat.SetTexture(GetNormalProp(targetPipeline), oldMat.GetTexture("_BumpMap"));
            else if (oldMat.HasProperty("_NormalMap")) newMat.SetTexture(GetNormalProp(targetPipeline), oldMat.GetTexture("_NormalMap"));

            if (oldMat.HasProperty("_Glossiness") && newMat.HasProperty("_Smoothness")) newMat.SetFloat("_Smoothness", oldMat.GetFloat("_Glossiness"));
            if (oldMat.HasProperty("_Metallic") && newMat.HasProperty("_Metallic")) newMat.SetFloat("_Metallic", oldMat.GetFloat("_Metallic"));

            string cutoffProp = targetPipeline == RenderPipelineType.HDRP ? "_AlphaCutoff" : "_Cutoff";
            if (oldMat.HasProperty("_Cutoff") && newMat.HasProperty(cutoffProp)) newMat.SetFloat(cutoffProp, oldMat.GetFloat("_Cutoff"));

            int matMode = oldMat.HasProperty("_Mode") ? Mathf.RoundToInt(oldMat.GetFloat("_Mode")) : 0;
            if (matMode == 1 || matMode == 3)
            {
                if (newMat.HasProperty("_AlphaClip")) newMat.SetFloat("_AlphaClip", 1f); 
                if (newMat.HasProperty("_AlphaCutoffEnable")) newMat.SetFloat("_AlphaCutoffEnable", 1f); 
            }
        }

        private static string GetColorProp(RenderPipelineType pipeline) => pipeline == RenderPipelineType.BuiltIn ? "_Color" : "_BaseColor";
        private static string GetTexProp(RenderPipelineType pipeline) => pipeline == RenderPipelineType.HDRP ? "_BaseColorMap" : (pipeline == RenderPipelineType.URP ? "_BaseMap" : "_MainTex");
        private static string GetNormalProp(RenderPipelineType pipeline) => pipeline == RenderPipelineType.HDRP ? "_NormalMap" : "_BumpMap";

        /// <summary>
        /// Custom Welcome Modal Dialog
        /// </summary>
        public class BroccoliPackageWelcomeWindow : EditorWindow
        {
            // --- CUSTOMIZABLE TEXT CONSTANTS ---
            public const string WINDOW_TITLE = "Welcome to " + PACKAGE_NAME;
            public const string HEADER_TEXT = PACKAGE_NAME + " Installed Successfully";
            public const string DESCRIPTION_TEXT = 
                "Enhance your environments with this collection of high-quality, realistic vegetation assets, perfect for adding detail and life to your Unity scenes.\n\n" +
                "This package features a diverse selection of common plants, bushes and flowers, meticulously crafted for realism. Each prefab includes 3 Levels of Detail (LODs) ensuring optimal performance across various distances without sacrificing visual fidelity up close. Support for WindZones is provided by Unity's SpeedTree8 shaders.\n\n" +
                "<i>All the meshes and composite textures were produced using Broccoli Tree Creator and Alfalfa Plant Generator.</i>";

            public const string SCENE_RELATIVE_PATH = "Scenes/HarvestShowcase.unity";
            public const string BTN_OPEN_SCENE = "Open Showcase Scene";

            public const string WARNING_PREFIX = "<b>Notice:</b> A Render Pipeline mismatch was detected (";
            public const string WARNING_SUFFIX = ").\nThe Material Upgrader wizard will open immediately after you close this window.";

            public const string TOGGLE_DONT_SHOW = " Don't show this welcome screen again.";
            public const string BTN_CLOSE = "Close";

            public const string MSG_SCENE_NOT_FOUND_TITLE = "Scene Not Found";
            public const string MSG_SCENE_NOT_FOUND_PREFIX = "Could not locate the showcase scene at:\n";

            // --- INSTANCE VARIABLES ---
            private bool dontShowAgain = true;
            private bool hasPipelineMismatch;
            private RenderPipelineType detectedPipeline;
            private Action onCloseCallback;

            public static void ShowWindow(bool mismatch, RenderPipelineType pipeline, Action onClose)
            {
                BroccoliPackageWelcomeWindow window = GetWindow<BroccoliPackageWelcomeWindow>(true, WINDOW_TITLE, true);
                
                // Calculate required height based on what we need to display
                float windowHeight = (mismatch && pipeline == RenderPipelineType.HDRP) ? 620f : 400f;
                
                window.minSize = new Vector2(450, windowHeight);
                window.maxSize = new Vector2(450, windowHeight);
                
                window.hasPipelineMismatch = mismatch;
                window.detectedPipeline = pipeline;
                window.onCloseCallback = onClose;
                window.ShowUtility();
            }

            private void OnGUI()
            {
                GUILayout.Space(15);
                
                GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
                GUILayout.Label(HEADER_TEXT, headerStyle);
                
                GUILayout.Space(15);

                GUIStyle bodyStyle = new GUIStyle(EditorStyles.label) { wordWrap = true, fontSize = 12, richText = true };
                GUILayout.Label(DESCRIPTION_TEXT, bodyStyle);

                GUILayout.Space(25);

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(BTN_OPEN_SCENE, GUILayout.Width(250), GUILayout.Height(30)))
                {
                    OpenShowcaseScene();
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // --- REMOVE OR MOVE THIS FLEXIBLESPACE ---
                // GUILayout.FlexibleSpace(); 

                if (hasPipelineMismatch)
                {
                    GUILayout.Space(10);
                    GUIStyle warningStyle = new GUIStyle(EditorStyles.helpBox) { fontSize = 11, richText = true };
                    GUILayout.Label($"{WARNING_PREFIX}{detectedPipeline}{WARNING_SUFFIX}", warningStyle);
                    GUILayout.Space(10); 
                    
                    if (detectedPipeline == RenderPipelineType.HDRP)
                    { 
                        DrawRegistrationHelp();
                    }
                }

                // Now put the FlexibleSpace here, so it pushes the "Close" button to the bottom
                GUILayout.FlexibleSpace(); 

                GUILayout.BeginHorizontal();
                dontShowAgain = GUILayout.Toggle(dontShowAgain, TOGGLE_DONT_SHOW);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(BTN_CLOSE, GUILayout.Width(80)))
                {
                    Close();
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
            }

            private void DrawRegistrationHelp()
            {
                EditorGUILayout.HelpBox("IMPORTANT: HDRP Registration Required", MessageType.Warning);
                GUILayout.Label($"To enable Subsurface Scattering for {PACKAGE_NAME} in Unity 6+, please:", EditorStyles.wordWrappedLabel);
                
                GUILayout.Space(5);
                GUILayout.Label("1. Open Edit > Project Settings > Graphics > HDRP Global Settings.", EditorStyles.wordWrappedLabel);
                GUILayout.Label("2. Find the 'Default Volume Profile Asset' and select it.", EditorStyles.wordWrappedLabel);
                GUILayout.Label("3. In its Inspector, search for the 'Diffusion Profile List'.", EditorStyles.wordWrappedLabel);
                GUILayout.Label("4. Add the following Diffusion Profiles to that list:", EditorStyles.wordWrappedLabel);
                
                // Explicitly name the required profiles
                GUILayout.Label("   • HarvestGardenBarkDP", EditorStyles.boldLabel);
                GUILayout.Label("   • HarvestGardenGreenFoliageDP", EditorStyles.boldLabel);
                
                GUILayout.Space(5);
                
                // Button to ping and select the actual profiles, forcing the folder to open
                if (GUILayout.Button("Show Profiles in Project", GUILayout.Height(25)))
                {
                    string[] barkGuids = AssetDatabase.FindAssets("HarvestGardenBarkDP");
                    string[] foliageGuids = AssetDatabase.FindAssets("HarvestGardenGreenFoliageDP");

                    List<Object> profilesToSelect = new List<Object>();

                    if (barkGuids.Length > 0)
                        profilesToSelect.Add(AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(barkGuids[0])));
                    
                    if (foliageGuids.Length > 0)
                        profilesToSelect.Add(AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(foliageGuids[0])));

                    if (profilesToSelect.Count > 0)
                    {
                        // Selects the actual files, which forces the Project window to open their parent folder
                        Selection.objects = profilesToSelect.ToArray();
                        EditorGUIUtility.PingObject(profilesToSelect[0]);
                    }
                    else
                    {
                        Debug.LogWarning($"[{PACKAGE_NAME}] Could not locate the diffusion profiles.");
                    }
                }
                
                GUILayout.Space(10);
            }

            private void OpenShowcaseScene()
            {
                // Child class can safely access GetPackageRootPath() directly
                string packageRoot = GetPackageRootPath();
                string scenePath = Path.Combine(packageRoot, SCENE_RELATIVE_PATH).Replace('\\', '/');

                if (File.Exists(scenePath))
                {
                    if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    {
                        EditorSceneManager.OpenScene(scenePath);
                        Close(); // Closing automatically triggers the upgrader if needed via OnDisable
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog(MSG_SCENE_NOT_FOUND_TITLE, $"{MSG_SCENE_NOT_FOUND_PREFIX}{scenePath}", "OK");
                }
            }
            
            private void OnDisable()
            {
                if (dontShowAgain)
                {
                    // Child class can safely access WriteFingerprint() directly
                    WriteFingerprint(detectedPipeline);
                }
                
                // Trigger the upgrader sequence safely outside the OnDisable/Close flow
                if (onCloseCallback != null)
                {
                    EditorApplication.delayCall += () => onCloseCallback.Invoke();
                }
            }
        }
    }
}

