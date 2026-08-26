using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MapLevel.Editor
{
    public static class ChernobylMapLayoutGenerator
    {
        private const string ScenePath = "Assets/Scenes/Chernobyl_Map_Reconstruction.unity";
        private const string ChernobylRoot = "Assets/Chernobyl";

        private struct BuildingSpec
        {
            public string path;
            public string name;
            public Vector3 position;
            public float yaw;
            public float scale;

            public BuildingSpec(string path, string name, float x, float z, float yaw, float scale)
            {
                this.path = path;
                this.name = name;
                position = new Vector3(x, 0f, z);
                this.yaw = yaw;
                this.scale = scale;
            }
        }

        [MenuItem("Tools/Map Level/Generate Chernobyl Map Layout")]
        public static void Generate()
        {
            EnsureFolder("Assets", "Scenes");
            EnsureFolder("Assets", "Generated");
            EnsureFolder("Assets/Generated", "ChernobylMap");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "Chernobyl_Map_Reconstruction";

            GameObject environmentRoot = new GameObject("ENVIRONMENT");
            GameObject roadsRoot = new GameObject("ROADS - Map Traced");
            GameObject buildingsRoot = new GameObject("BUILDINGS - Chernobyl Assets");
            GameObject propsRoot = new GameObject("PROPS - Chernobyl Assets");
            GameObject referenceRoot = new GameObject("MAP REFERENCE - Sarajevo Koševo");

            Material groundMaterial = GetOrCreateGroundMaterial();
            Material roadMaterial = LoadMaterialFromPrefab(ChernobylRoot + "/Prefabs/Street/Road_4x4m.prefab");
            Material sidewalkMaterial = LoadMaterialFromPrefab(ChernobylRoot + "/Prefabs/Street/Sidewalk_4m_01.prefab");

            if (groundMaterial == null || roadMaterial == null || sidewalkMaterial == null)
                throw new InvalidOperationException("Required Chernobyl ground/road materials could not be loaded.");

            CreateGround(environmentRoot.transform, groundMaterial);
            CreateRoadNetwork(roadsRoot.transform, sidewalkMaterial, roadMaterial);
            CreateBuildings(buildingsRoot.transform);
            CreateProps(propsRoot.transform);
            CreateReferenceMarkers(referenceRoot.transform);
            CreateLightingAndCamera(environmentRoot.transform);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.48f, 0.5f, 0.46f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.55f, 0.58f, 0.54f);
            RenderSettings.fogStartDistance = 170f;
            RenderSettings.fogEndDistance = 340f;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = buildingsRoot;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log("[ChernobylMapLayout] Generated and saved: " + ScenePath);
        }

        private static void CreateGround(Transform parent, Material material)
        {
            GameObject ground = CreateQuad("Ground_260x240", new Vector3(-130f, 0f, -120f), new Vector3(-130f, 0f, 120f), new Vector3(130f, 0f, -120f), new Vector3(130f, 0f, 120f), material, 0.25f);
            ground.transform.SetParent(parent, true);
            ground.isStatic = true;
            var collider = ground.AddComponent<MeshCollider>();
            collider.sharedMesh = ground.GetComponent<MeshFilter>().sharedMesh;
        }

        private static void CreateRoadNetwork(Transform parent, Material sidewalkMaterial, Material roadMaterial)
        {
            AddRoad(parent, "Bolnicka_North", new[]
            {
                new Vector3(-130,0,99), new Vector3(-82,0,103), new Vector3(-44,0,116),
                new Vector3(5,0,107), new Vector3(47,0,105), new Vector3(79,0,111), new Vector3(86,0,96),
                new Vector3(130,0,112)
            }, 8.5f, sidewalkMaterial, roadMaterial);

            AddRoad(parent, "Patriotske_Lige_West", new[]
            {
                new Vector3(-87,0,-120), new Vector3(-88,0,-72), new Vector3(-90,0,-20),
                new Vector3(-95,0,28), new Vector3(-103,0,71), new Vector3(-108,0,120)
            }, 8f, sidewalkMaterial, roadMaterial);

            AddRoad(parent, "Bolnicka_Central", new[]
            {
                new Vector3(-32,0,-120), new Vector3(-20,0,-75), new Vector3(-10,0,-35),
                new Vector3(0,0,6), new Vector3(10,0,45), new Vector3(29,0,72),
                new Vector3(18,0,105)
            }, 9f, sidewalkMaterial, roadMaterial);

            AddRoad(parent, "Cekalusa_Diagonal", new[]
            {
                new Vector3(-2,0,-4), new Vector3(25,0,-35), new Vector3(55,0,-65),
                new Vector3(89,0,-96), new Vector3(117,0,-120)
            }, 8f, sidewalkMaterial, roadMaterial);

            AddRoad(parent, "Kosevska_East", new[]
            {
                new Vector3(-1,0,7), new Vector3(34,0,0), new Vector3(72,0,-14),
                new Vector3(91,0,-28), new Vector3(96,0,-58), new Vector3(113,0,-78),
                new Vector3(130,0,-91)
            }, 8f, sidewalkMaterial, roadMaterial);

            AddRoad(parent, "West_Cross_Connector", new[]
            {
                new Vector3(-98,0,35), new Vector3(-68,0,45), new Vector3(-36,0,53),
                new Vector3(7,0,49)
            }, 6.5f, sidewalkMaterial, roadMaterial);

            AddRoad(parent, "Northwest_Inner", new[]
            {
                new Vector3(-65,0,112), new Vector3(-55,0,80), new Vector3(-48,0,56)
            }, 6f, sidewalkMaterial, roadMaterial);

            AddRoad(parent, "Southwest_Inner", new[]
            {
                new Vector3(-61,0,45), new Vector3(-53,0,10), new Vector3(-55,0,-30),
                new Vector3(-49,0,-65), new Vector3(-58,0,-96)
            }, 6f, sidewalkMaterial, roadMaterial);

            AddRoad(parent, "Northeast_Spur", new[]
            {
                new Vector3(76,0,108), new Vector3(91,0,82), new Vector3(130,0,96)
            }, 7f, sidewalkMaterial, roadMaterial);
        }

        private static void AddRoad(Transform parent, string name, Vector3[] points, float width, Material sidewalkMaterial, Material roadMaterial)
        {
            GameObject roadRoot = new GameObject(name);
            roadRoot.transform.SetParent(parent, false);

            for (int i = 0; i < points.Length - 1; i++)
            {
                CreateStrip(name + "_Sidewalk_" + (i + 1).ToString("00"), points[i], points[i + 1], width + 3.6f, 0.035f, sidewalkMaterial, roadRoot.transform, 0.25f);
                CreateStrip(name + "_Road_" + (i + 1).ToString("00"), points[i], points[i + 1], width, 0.07f, roadMaterial, roadRoot.transform, 0.25f);
            }
        }

        private static GameObject CreateStrip(string name, Vector3 start, Vector3 end, float width, float y, Material material, Transform parent, float uvScale)
        {
            Vector3 direction = end - start;
            direction.y = 0f;
            float length = direction.magnitude;
            direction.Normalize();
            Vector3 perpendicular = new Vector3(-direction.z, 0f, direction.x) * (width * 0.5f);

            Vector3 a = start - perpendicular + Vector3.up * y;
            Vector3 b = start + perpendicular + Vector3.up * y;
            Vector3 c = end - perpendicular + Vector3.up * y;
            Vector3 d = end + perpendicular + Vector3.up * y;

            GameObject go = CreateQuad(name, a, b, c, d, material, uvScale, length, width);
            go.transform.SetParent(parent, true);
            go.isStatic = true;
            MeshCollider collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            return go;
        }

        private static GameObject CreateQuad(string name, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Material material, float uvScale, float explicitLength = 0f, float explicitWidth = 0f)
        {
            GameObject go = new GameObject(name);
            Mesh mesh = new Mesh { name = name + "_Mesh" };
            mesh.vertices = new[] { a, b, c, d };
            mesh.triangles = new[]
            {
                0, 2, 1, 2, 3, 1,
                0, 1, 2, 2, 1, 3
            };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };

            float length = explicitLength > 0f ? explicitLength : Vector3.Distance(a, c);
            float width = explicitWidth > 0f ? explicitWidth : Vector3.Distance(a, b);
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(0f, width * uvScale),
                new Vector2(length * uvScale, 0f), new Vector2(length * uvScale, width * uvScale)
            };
            mesh.RecalculateBounds();

            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return go;
        }

        private static void CreateBuildings(Transform parent)
        {
            string house = ChernobylRoot + "/Prefabs/House/";
            BuildingSpec[] specs =
            {
                new BuildingSpec(house + "Apartment_Building_02.prefab", "B01_Northwest_Residential", -78, 82, 12, 0.72f),
                new BuildingSpec(house + "Apartment_Building_04.prefab", "B02_North_Inner", -28, 84, -8, 0.78f),
                new BuildingSpec(house + "Apartment_Building_05.prefab", "B03_North_Central", 2, 83, 18, 0.72f),
                new BuildingSpec(house + "Apartment_Building_03.prefab", "B04_Northeast_Block", 53, 78, -16, 0.72f),
                new BuildingSpec(house + "Department_Store.prefab", "B05_Northeast_Store", 103, 86, 12, 1.05f),

                new BuildingSpec(house + "Department_Store.prefab", "B06_West_Restaurant_Block", -72, 57, -8, 1.0f),
                new BuildingSpec(house + "Pharmacy.prefab", "B07_Pharmacy_Block", -33, 42, 12, 0.9f),
                new BuildingSpec(house + "Apartment_Building_06.prefab", "B08_Central_West_Block", -29, 15, -8, 0.78f),
                new BuildingSpec(house + "Pharmacy.prefab", "B09_Central_East_Shop", 34, 34, -24, 0.95f),
                new BuildingSpec(house + "Apartment_Building_04.prefab", "B10_East_Clinic", 78, 28, 8, 0.88f),
                new BuildingSpec(house + "Apartment_Building_05.prefab", "B11_Far_East_Block", 116, 16, -18, 0.82f),

                new BuildingSpec(house + "Department_Store.prefab", "B12_West_Mid_Block", -70, 8, 10, 0.95f),
                new BuildingSpec(house + "Apartment_Building_06.prefab", "B13_Inner_West_Block", -36, -17, 4, 0.72f),
                new BuildingSpec(house + "Gym_Energy.prefab", "B14_Faculty_Of_Medicine", 48, -25, -14, 1.08f),
                new BuildingSpec(house + "Apartment_Building_02.prefab", "B15_East_Residential", 106, -31, -20, 0.76f),

                new BuildingSpec(house + "Pharmacy.prefab", "B16_Southwest_Shop", -72, -43, 8, 0.92f),
                new BuildingSpec(house + "Department_Store.prefab", "B17_Southwest_Block", -43, -62, 4, 1.0f),
                new BuildingSpec(house + "Apartment_Building_03.prefab", "B18_Faculty_Of_Dentistry", -2, -83, 84, 0.78f),
                new BuildingSpec(house + "Apartment_Building_04.prefab", "B19_South_Central_Block", 34, -100, 86, 0.84f),
                new BuildingSpec(house + "Apartment_Building_01.prefab", "B20_Southeast_Block", 80, -82, -42, 0.78f),
                new BuildingSpec(house + "Apartment_Building_06.prefab", "B21_Far_Southeast_Block", 116, -94, -43, 0.72f),
                new BuildingSpec(house + "Department_Store.prefab", "B22_Far_Southwest_Block", -70, -96, 12, 0.95f)
            };

            for (int i = 0; i < specs.Length; i++)
                PlacePrefabByBounds(specs[i].path, specs[i].name, specs[i].position, specs[i].yaw, specs[i].scale, parent);
        }

        private static void CreateProps(Transform parent)
        {
            string tree = ChernobylRoot + "/Prefabs/Vegetation/Small_Tree_0";
            Vector3[] treePositions =
            {
                new Vector3(-121,0,-92), new Vector3(-111,0,-67), new Vector3(-120,0,-42), new Vector3(-114,0,-14),
                new Vector3(-122,0,12), new Vector3(-116,0,40), new Vector3(-122,0,68),
                new Vector3(14,0,-20), new Vector3(24,0,-10), new Vector3(18,0,-42), new Vector3(31,0,-48),
                new Vector3(58,0,12), new Vector3(66,0,40), new Vector3(96,0,53), new Vector3(110,0,61),
                new Vector3(54,0,-99), new Vector3(99,0,-111), new Vector3(-45,0,-101)
            };

            for (int i = 0; i < treePositions.Length; i++)
            {
                string path = tree + ((i % 4) + 1) + ".prefab";
                PlacePrefabByBounds(path, "Tree_" + (i + 1).ToString("00"), treePositions[i], (i * 47) % 360, 0.85f + (i % 3) * 0.08f, parent);
            }

            string lampPath = ChernobylRoot + "/Prefabs/Street/Street_Lamp_01.prefab";
            Vector3[] lamps =
            {
                new Vector3(-10,0,-63), new Vector3(-2,0,-25), new Vector3(8,0,18), new Vector3(17,0,55),
                new Vector3(-83,0,-52), new Vector3(-91,0,4), new Vector3(-98,0,58),
                new Vector3(38,0,-41), new Vector3(70,0,-69), new Vector3(74,0,-8)
            };
            for (int i = 0; i < lamps.Length; i++)
                PlacePrefabByBounds(lampPath, "StreetLamp_" + (i + 1).ToString("00"), lamps[i], 0f, 1f, parent);

            PlacePrefabByBounds(ChernobylRoot + "/Prefabs/Environment/Bus_Stop.prefab", "Bus_Stop_Central", new Vector3(-8,0,-6), 18f, 1f, parent);
            PlacePrefabByBounds(ChernobylRoot + "/Prefabs/Environment/Bench_01.prefab", "Bench_Faculty_01", new Vector3(22,0,-24), 75f, 1f, parent);
            PlacePrefabByBounds(ChernobylRoot + "/Prefabs/Environment/Bench_02.prefab", "Bench_Faculty_02", new Vector3(27,0,-31), 75f, 1f, parent);
        }

        private static void CreateReferenceMarkers(Transform parent)
        {
            string[] notes =
            {
                "Layout traced from attached 2D Sarajevo Koševo map capture",
                "Beige/light-grey rectangles interpreted as building footprints",
                "Dark-grey/blue lines interpreted as roads",
                "North is +Z; east is +X; scale is approximate"
            };
            for (int i = 0; i < notes.Length; i++)
            {
                GameObject marker = new GameObject(notes[i]);
                marker.transform.SetParent(parent, false);
            }
        }

        private static GameObject PlacePrefabByBounds(string prefabPath, string objectName, Vector3 desiredCenter, float yaw, float uniformScale, Transform parent)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("Missing Chernobyl prefab: " + prefabPath);
                return null;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = objectName;
            instance.transform.SetParent(parent, true);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            instance.transform.localScale = Vector3.one * uniformScale;

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);
                Vector3 offset = new Vector3(desiredCenter.x - bounds.center.x, -bounds.min.y + 0.08f, desiredCenter.z - bounds.center.z);
                instance.transform.position += offset;
            }
            else
            {
                instance.transform.position = desiredCenter;
            }

            return instance;
        }

        private static Material LoadMaterialFromPrefab(string prefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                return null;
            Renderer renderer = prefab.GetComponentInChildren<Renderer>(true);
            return renderer != null ? renderer.sharedMaterial : null;
        }

        private static Material GetOrCreateGroundMaterial()
        {
            const string path = "Assets/Generated/ChernobylMap/Ground_URP.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                return null;

            material = new Material(shader) { name = "Ground_URP" };
            Color groundColor = new Color(0.22f, 0.27f, 0.18f, 1f);
            material.color = groundColor;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", groundColor);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void CreateLightingAndCamera(Transform parent)
        {
            GameObject lightObject = new GameObject("Directional Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.color = new Color(1f, 0.91f, 0.78f);
            light.shadows = LightShadows.Soft;

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.position = new Vector3(8f, 175f, -178f);
            cameraObject.transform.LookAt(new Vector3(0f, 0f, -5f));
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 47f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 600f;
            camera.clearFlags = CameraClearFlags.Skybox;
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
