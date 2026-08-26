using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MapLevel.Editor
{
    public static class CubeOnlySarajevoMapGenerator
    {
        private const string ScenePath = "Assets/Scenes/CubeOnly_Sarajevo_Map.unity";
        private const string MaterialFolder = "Assets/Generated/CubeOnlyMap";

        private struct BlockSpec
        {
            public string name;
            public float x;
            public float z;
            public float width;
            public float depth;
            public float height;
            public float yaw;
            public bool beige;

            public BlockSpec(string name, float x, float z, float width, float depth, float height, float yaw, bool beige)
            {
                this.name = name;
                this.x = x;
                this.z = z;
                this.width = width;
                this.depth = depth;
                this.height = height;
                this.yaw = yaw;
                this.beige = beige;
            }
        }

        [MenuItem("Tools/Map Level/Generate Cube-Only Sarajevo Map")]
        public static void Generate()
        {
            EnsureFolder("Assets", "Scenes");
            EnsureFolder("Assets", "Generated");
            EnsureFolder("Assets/Generated", "CubeOnlyMap");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "CubeOnly_Sarajevo_Map";

            Material groundMaterial = CreateOrUpdateMaterial("Ground", new Color(0.91f, 0.90f, 0.86f, 1f), 0f);
            Material parkMaterial = CreateOrUpdateMaterial("Park", new Color(0.42f, 0.69f, 0.47f, 1f), 0f);
            Material sidewalkMaterial = CreateOrUpdateMaterial("Sidewalk", new Color(0.63f, 0.68f, 0.72f, 1f), 0.05f);
            Material roadMaterial = CreateOrUpdateMaterial("Road", new Color(0.16f, 0.18f, 0.20f, 1f), 0.08f);
            Material routeMaterial = CreateOrUpdateMaterial("Route_Cyan", new Color(0.00f, 0.62f, 0.76f, 1f), 0.18f);
            Material beigeMaterial = CreateOrUpdateMaterial("Building_Beige", new Color(0.82f, 0.73f, 0.56f, 1f), 0.08f);
            Material grayMaterial = CreateOrUpdateMaterial("Building_LightGray", new Color(0.63f, 0.68f, 0.74f, 1f), 0.08f);

            GameObject worldRoot = new GameObject("CUBE-ONLY MAP - No External Assets");
            GameObject groundRoot = new GameObject("GROUND - Cubes");
            GameObject roadRoot = new GameObject("ROADS - Cubes");
            GameObject buildingRoot = new GameObject("BUILDINGS - Cubes");
            GameObject cameraRoot = new GameObject("LIGHTING AND CAMERA");
            groundRoot.transform.SetParent(worldRoot.transform, false);
            roadRoot.transform.SetParent(worldRoot.transform, false);
            buildingRoot.transform.SetParent(worldRoot.transform, false);
            cameraRoot.transform.SetParent(worldRoot.transform, false);

            CreateCube("Base_Ground", new Vector3(0f, -0.35f, 0f), new Vector3(246f, 0.6f, 246f), Quaternion.identity, groundMaterial, groundRoot.transform);
            CreateCube("Southwest_Park", new Vector3(-99f, -0.01f, -74f), new Vector3(48f, 0.18f, 96f), Quaternion.identity, parkMaterial, groundRoot.transform);

            CreateRoadNetwork(roadRoot.transform, sidewalkMaterial, roadMaterial, routeMaterial);
            CreateBuildingLayout(buildingRoot.transform, beigeMaterial, grayMaterial);
            CreateLightingAndCamera(cameraRoot.transform);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.68f, 0.70f, 0.72f, 1f);
            RenderSettings.fog = false;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = worldRoot;
            Debug.Log("[CubeOnlySarajevoMap] Generated only with Unity Cube primitives: " + ScenePath);
        }

        private static void CreateRoadNetwork(Transform parent, Material sidewalk, Material asphalt, Material route)
        {
            AddRoad(parent, "R01_North_Bolnicka", new[]
            {
                new Vector3(-123,0,101), new Vector3(-88,0,104), new Vector3(-55,0,114),
                new Vector3(-20,0,109), new Vector3(15,0,103), new Vector3(48,0,102),
                new Vector3(72,0,106), new Vector3(80,0,91), new Vector3(122,0,106)
            }, 7.8f, sidewalk, asphalt, route);

            AddRoad(parent, "R02_West_Patriotske_Lige", new[]
            {
                new Vector3(-96,0,122), new Vector3(-94,0,91), new Vector3(-87,0,58),
                new Vector3(-79,0,27), new Vector3(-73,0,-9), new Vector3(-70,0,-50),
                new Vector3(-69,0,-122)
            }, 7.5f, sidewalk, asphalt, route);

            AddRoad(parent, "R03_Central_Bolnicka", new[]
            {
                new Vector3(-54,0,112), new Vector3(-47,0,87), new Vector3(-38,0,57),
                new Vector3(-22,0,36), new Vector3(-10,0,22), new Vector3(-17,0,-9),
                new Vector3(-27,0,-39), new Vector3(-39,0,-77), new Vector3(-49,0,-122)
            }, 8.2f, sidewalk, asphalt, route);

            AddRoad(parent, "R04_West_Cross_Connector", new[]
            {
                new Vector3(-84,0,43), new Vector3(-60,0,49), new Vector3(-39,0,55),
                new Vector3(-20,0,48), new Vector3(-10,0,33)
            }, 6.2f, sidewalk, asphalt, route);

            AddRoad(parent, "R05_Northwest_Inner", new[]
            {
                new Vector3(-58,0,112), new Vector3(-52,0,91), new Vector3(-46,0,72)
            }, 5.4f, sidewalk, asphalt, route);

            AddRoad(parent, "R06_West_Inner_Service", new[]
            {
                new Vector3(-45,0,48), new Vector3(-39,0,23), new Vector3(-37,0,-1),
                new Vector3(-42,0,-29), new Vector3(-37,0,-50), new Vector3(-44,0,-70)
            }, 5.5f, sidewalk, asphalt, route);

            AddRoad(parent, "R07_Kosevska_East", new[]
            {
                new Vector3(-10,0,22), new Vector3(18,0,15), new Vector3(47,0,5),
                new Vector3(72,0,-3), new Vector3(86,0,-13), new Vector3(91,0,-37),
                new Vector3(106,0,-57), new Vector3(123,0,-70)
            }, 7.5f, sidewalk, asphalt, route);

            AddRoad(parent, "R08_Cekalusa_Diagonal", new[]
            {
                new Vector3(-17,0,-9), new Vector3(8,0,-35), new Vector3(36,0,-62),
                new Vector3(65,0,-90), new Vector3(97,0,-122)
            }, 7.3f, sidewalk, asphalt, route);

            AddRoad(parent, "R09_Northeast_Spur", new[]
            {
                new Vector3(72,0,106), new Vector3(84,0,84), new Vector3(122,0,98)
            }, 7.1f, sidewalk, asphalt, route);

            CreateCube("Central_Intersection", new Vector3(-10f, 0.24f, 22f), new Vector3(10.5f, 0.24f, 10.5f), Quaternion.Euler(0f, -14f, 0f), asphalt, parent);
            CreateCube("Central_Intersection_Route", new Vector3(-10f, 0.39f, 22f), new Vector3(1.0f, 0.05f, 10f), Quaternion.Euler(0f, -14f, 0f), route, parent);
        }

        private static void AddRoad(Transform parent, string roadName, Vector3[] points, float width, Material sidewalk, Material asphalt, Material route)
        {
            GameObject roadRoot = new GameObject(roadName);
            roadRoot.transform.SetParent(parent, false);
            for (int i = 0; i < points.Length - 1; i++)
            {
                CreateSegmentCube("Sidewalk_" + (i + 1).ToString("00"), points[i], points[i + 1], width + 3.2f, 0.16f, 0.09f, sidewalk, roadRoot.transform);
                CreateSegmentCube("Asphalt_" + (i + 1).ToString("00"), points[i], points[i + 1], width, 0.24f, 0.22f, asphalt, roadRoot.transform);
                CreateSegmentCube("CyanRoute_" + (i + 1).ToString("00"), points[i], points[i + 1], 0.72f, 0.05f, 0.38f, route, roadRoot.transform);
            }
        }

        private static void CreateSegmentCube(string name, Vector3 start, Vector3 end, float width, float height, float y, Material material, Transform parent)
        {
            Vector3 direction = end - start;
            direction.y = 0f;
            float length = direction.magnitude;
            if (length < 0.01f)
                return;

            Vector3 center = (start + end) * 0.5f;
            center.y = y;
            Quaternion rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            CreateCube(name, center, new Vector3(width, height, length + 0.8f), rotation, material, parent);
        }

        private static void CreateBuildingLayout(Transform parent, Material beige, Material gray)
        {
            BlockSpec[] blocks =
            {
                new BlockSpec("B01_NW_Beige_Main", -76, 78, 17, 27, 10, -6, true),
                new BlockSpec("B01_NW_Beige_Wing", -65, 66, 12, 10, 8, 2, true),
                new BlockSpec("B02_NW_Gray_Tower", -59, 88, 9, 20, 13, -10, false),
                new BlockSpec("B03_Restaurant_Main", -62, 53, 25, 14, 9, -3, false),
                new BlockSpec("B03_Restaurant_Wing", -73, 58, 9, 18, 7, -3, false),
                new BlockSpec("B04_West_Beige_Shop", -59, 29, 17, 14, 7, -9, true),
                new BlockSpec("B05_Barbershop_Main", -55, 2, 17, 17, 9, 8, false),
                new BlockSpec("B05_Barbershop_Wing", -48, 9, 8, 13, 7, 8, false),
                new BlockSpec("B06_West_Lower_Beige", -55, -31, 18, 14, 7, 9, true),
                new BlockSpec("B07_West_Lower_Gray", -51, -60, 18, 13, 8, -10, false),
                new BlockSpec("B08_SW_Gray_01", -54, -86, 13, 10, 8, -12, false),
                new BlockSpec("B08_SW_Gray_02", -60, -100, 14, 14, 10, -12, false),
                new BlockSpec("B09_SW_Beige_Edge", -94, -36, 20, 10, 6, 6, true),

                new BlockSpec("B10_North_Central_L", -30, 83, 13, 26, 12, -8, false),
                new BlockSpec("B10_North_Central_Wing", -22, 71, 12, 10, 9, -8, false),
                new BlockSpec("B11_North_Thin", -6, 84, 8, 23, 11, -13, false),
                new BlockSpec("B12_Northeast_Gray", 20, 80, 18, 25, 13, -16, false),
                new BlockSpec("B13_NorthEast_Beige_Long", 77, 91, 54, 14, 9, -15, true),
                new BlockSpec("B14_Ophthalmology_Main", 78, 58, 25, 14, 11, -17, false),
                new BlockSpec("B14_Ophthalmology_Wing", 91, 64, 8, 16, 8, -17, false),
                new BlockSpec("B15_Central_Beige", -31, 35, 15, 21, 8, 10, true),
                new BlockSpec("B16_Central_Gray_Narrow", -24, 13, 8, 22, 10, 12, false),
                new BlockSpec("B17_Central_Gray_South", -30, -14, 9, 18, 8, -8, false),
                new BlockSpec("B18_Central_Beige_Shop", -8, 46, 15, 20, 9, 9, true),

                new BlockSpec("B19_East_Beige_Angled", 18, 39, 15, 29, 8, -31, true),
                new BlockSpec("B20_East_U_Left", 47, 24, 8, 31, 11, 8, false),
                new BlockSpec("B20_East_U_Right", 66, 24, 8, 31, 11, 8, false),
                new BlockSpec("B20_East_U_Top", 56, 38, 24, 8, 11, 8, false),
                new BlockSpec("B21_FarEast_Gray_01", 99, 28, 12, 25, 9, -20, false),
                new BlockSpec("B21_FarEast_Gray_02", 112, 20, 22, 11, 9, -20, false),
                new BlockSpec("B22_FarEast_Gray_03", 108, -11, 13, 24, 8, -15, false),

                new BlockSpec("B23_Medicine_Top", 41, -18, 49, 17, 12, -13, true),
                new BlockSpec("B23_Medicine_Left", 21, -31, 15, 38, 12, -13, true),
                new BlockSpec("B23_Medicine_Right", 61, -48, 16, 68, 12, -13, true),
                new BlockSpec("B23_Medicine_Inner", 42, -32, 27, 14, 7, -13, true),
                new BlockSpec("B24_East_Mid_Gray", 92, -35, 12, 18, 8, -41, false),
                new BlockSpec("B25_FarEast_Mid", 113, -48, 12, 17, 9, -43, false),

                new BlockSpec("B26_Dentistry_Main", -2, -83, 43, 16, 12, -6, false),
                new BlockSpec("B26_Dentistry_LeftWing", -20, -92, 14, 29, 11, -6, false),
                new BlockSpec("B26_Dentistry_RightWing", 16, -94, 16, 23, 9, -6, false),
                new BlockSpec("B27_South_Center_Gray", 40, -105, 27, 15, 9, -10, false),
                new BlockSpec("B28_SE_Beige", 77, -85, 18, 24, 8, -43, true),
                new BlockSpec("B29_SE_Gray_Long", 103, -77, 16, 30, 10, -43, false),
                new BlockSpec("B30_SE_Gray_Small", 119, -96, 14, 18, 8, -43, false)
            };

            for (int i = 0; i < blocks.Length; i++)
            {
                BlockSpec block = blocks[i];
                Material material = block.beige ? beige : gray;
                Vector3 position = new Vector3(block.x, 0.42f + block.height * 0.5f, block.z);
                Vector3 scale = new Vector3(block.width, block.height, block.depth);
                CreateCube(block.name, position, scale, Quaternion.Euler(0f, block.yaw, 0f), material, parent);
            }
        }

        private static GameObject CreateCube(string name, Vector3 position, Vector3 scale, Quaternion rotation, Material material, Transform parent)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, true);
            cube.transform.position = position;
            cube.transform.rotation = rotation;
            cube.transform.localScale = scale;
            cube.isStatic = true;
            Renderer renderer = cube.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return cube;
        }

        private static Material CreateOrUpdateMaterial(string name, Color color, float smoothness)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                throw new InvalidOperationException("No compatible Lit shader was found.");

            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.color = color;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void CreateLightingAndCamera(Transform parent)
        {
            GameObject lightObject = new GameObject("Directional Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(52f, -28f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = new Color(1f, 0.96f, 0.88f, 1f);
            light.shadows = LightShadows.Soft;

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.position = new Vector3(0f, 180f, 0f);
            cameraObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 123f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 400f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.10f, 0.11f, 0.12f, 1f);
            camera.allowHDR = true;
            cameraObject.AddComponent<AudioListener>();
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }
    }
}
