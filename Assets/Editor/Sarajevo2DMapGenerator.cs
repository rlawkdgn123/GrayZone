using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MapLevel.Editor
{
    public static class Sarajevo2DMapGenerator
    {
        private const float PixelsPerUnit = 10f;
        private const float ImageWidth = 1632f;
        private const float ImageHeight = 850f;
        private const string ScenePath = "Assets/Scenes/Sarajevo_2D_Map_Layout.unity";
        private const string MaterialFolder = "Assets/Generated/Sarajevo2DMap";

        private struct BuildingSpec
        {
            public string name;
            public float px;
            public float py;
            public float widthPx;
            public float heightPx;
            public float rotation;
            public bool beige;

            public BuildingSpec(string name, float px, float py, float widthPx, float heightPx, float rotation, bool beige)
            {
                this.name = name;
                this.px = px;
                this.py = py;
                this.widthPx = widthPx;
                this.heightPx = heightPx;
                this.rotation = rotation;
                this.beige = beige;
            }
        }

        [MenuItem("Tools/Map Level/Generate Sarajevo 2D Map")]
        public static void Generate()
        {
            EnsureFolder("Assets", "Scenes");
            EnsureFolder("Assets", "Generated");
            EnsureFolder("Assets/Generated", "Sarajevo2DMap");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "Sarajevo_2D_Map_Layout";

            Material background = CreateOrUpdateMaterial("Background", new Color(0.972f, 0.968f, 0.968f, 1f));
            Material roadEdge = CreateOrUpdateMaterial("Road_Edge", new Color(0.88f, 0.90f, 0.92f, 1f));
            Material road = CreateOrUpdateMaterial("Road", new Color(0.735f, 0.79f, 0.84f, 1f));
            Material grayBuilding = CreateOrUpdateMaterial("Building_Gray", new Color(0.91f, 0.92f, 0.945f, 1f));
            Material beigeBuilding = CreateOrUpdateMaterial("Building_Beige", new Color(0.992f, 0.975f, 0.91f, 1f));
            Material grayBorder = CreateOrUpdateMaterial("Building_Gray_Border", new Color(0.84f, 0.86f, 0.90f, 1f));
            Material beigeBorder = CreateOrUpdateMaterial("Building_Beige_Border", new Color(0.90f, 0.84f, 0.70f, 1f));

            GameObject root = new GameObject("SARAJEVO 2D MAP - 1632x850px at 10px per unit");
            GameObject groundRoot = CreateRoot("00_BACKGROUND", root.transform);
            GameObject roadRoot = CreateRoot("10_ROADS", root.transform);
            GameObject buildingRoot = CreateRoot("20_BUILDINGS", root.transform);
            GameObject cameraRoot = CreateRoot("90_CAMERA", root.transform);

            CreateQuad("Map_Background", Vector2.zero, new Vector2(ImageWidth / PixelsPerUnit, ImageHeight / PixelsPerUnit), 0f, 2f, background, groundRoot.transform, 0);
            CreateRoads(roadRoot.transform, roadEdge, road);
            CreateBuildings(buildingRoot.transform, grayBuilding, beigeBuilding, grayBorder, beigeBorder);
            CreateCamera(cameraRoot.transform);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white;
            RenderSettings.fog = false;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = root;
            Debug.Log("[Sarajevo2DMap] Generated at 10 px per Unity unit. Saved: " + ScenePath);
        }

        private static void CreateRoads(Transform parent, Material edgeMaterial, Material roadMaterial)
        {
            AddRoad(parent, "R01_Faculty_Cekalusa_Main", 30f, edgeMaterial, roadMaterial,
                -20,150, 120,285, 265,420, 390,575, 460,675, 520,850);
            AddRoad(parent, "R02_West_Lower_Connector", 25f, edgeMaterial, roadMaterial,
                -20,510, 110,538, 240,560, 390,590);
            AddRoad(parent, "R03_Cekalusa_Cikma", 23f, edgeMaterial, roadMaterial,
                390,590, 475,548, 555,500, 665,418);
            AddRoad(parent, "R04_Himzarina_Long", 25f, edgeMaterial, roadMaterial,
                320,-20, 420,85, 565,160, 700,245, 805,365, 905,465, 1015,570, 1110,635);
            AddRoad(parent, "R05_Visnjik_North", 28f, edgeMaterial, roadMaterial,
                650,-20, 735,32, 815,52, 915,45, 1010,5);
            AddRoad(parent, "R06_Derebent", 25f, edgeMaterial, roadMaterial,
                850,425, 965,325, 1080,220, 1200,110, 1330,-10);
            AddRoad(parent, "R07_Provare_Main", 27f, edgeMaterial, roadMaterial,
                1110,610, 1205,505, 1305,390, 1405,280, 1508,155, 1625,25);
            AddRoad(parent, "R08_Provare_East_Fork", 26f, edgeMaterial, roadMaterial,
                1360,505, 1430,420, 1510,330, 1575,315, 1635,380, 1680,440);
            AddRoad(parent, "R09_Avde_Sumbula", 25f, edgeMaterial, roadMaterial,
                535,860, 660,825, 805,795, 945,765, 1085,735);
            AddRoad(parent, "R10_Tahtali_Sokak", 25f, edgeMaterial, roadMaterial,
                1080,650, 1195,665, 1320,700, 1445,765, 1545,850);
            AddRoad(parent, "R11_Radiation_Center_South", 28f, edgeMaterial, roadMaterial,
                1110,625, 1090,690, 1090,770, 1125,860);
            AddRoad(parent, "R12_Central_Curved_Local", 18f, edgeMaterial, roadMaterial,
                495,760, 575,715, 660,675, 730,668, 780,700, 815,785);
            AddRoad(parent, "R13_Mosque_Connector", 21f, edgeMaterial, roadMaterial,
                905,470, 965,520, 1030,580, 1095,630);
            AddRoad(parent, "R14_Far_Right_South", 27f, edgeMaterial, roadMaterial,
                1638,585, 1590,650, 1555,730, 1505,820, 1480,860);
        }

        private static void AddRoad(Transform parent, string name, float widthPx, Material edgeMaterial, Material roadMaterial, params float[] xy)
        {
            if (xy == null || xy.Length < 4 || xy.Length % 2 != 0)
                return;

            Vector3[] points = new Vector3[xy.Length / 2];
            for (int i = 0; i < points.Length; i++)
                points[i] = PixelToWorld(xy[i * 2], xy[i * 2 + 1], 1.0f);

            GameObject root = CreateRoot(name, parent);
            CreateLine("Edge", points, (widthPx + 10f) / PixelsPerUnit, 1.1f, edgeMaterial, root.transform, 5);
            CreateLine("Road", points, widthPx / PixelsPerUnit, 0.9f, roadMaterial, root.transform, 10);
        }

        private static void CreateLine(string name, Vector3[] points, float width, float z, Material material, Transform parent, int sortingOrder)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = points.Length;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 p = points[i];
                p.z = z;
                line.SetPosition(i, p);
            }
            line.startWidth = width;
            line.endWidth = width;
            line.sharedMaterial = material;
            line.numCornerVertices = 8;
            line.numCapVertices = 8;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = sortingOrder;
        }

        private static void CreateBuildings(Transform parent, Material gray, Material beige, Material grayBorder, Material beigeBorder)
        {
            BuildingSpec[] specs =
            {
                new BuildingSpec("B001_West_Beige_Edge", 83,82,82,310,15,true),
                new BuildingSpec("B002_NW_Small", 218,78,58,60,-5,false),
                new BuildingSpec("B003_Northwest_01", 350,42,72,88,-42,false),
                new BuildingSpec("B004_Northwest_02", 414,112,65,84,-40,false),
                new BuildingSpec("B005_North_Small_01", 500,80,48,52,45,false),
                new BuildingSpec("B006_North_Small_02", 582,18,65,58,45,false),
                new BuildingSpec("B007_North_Residence", 650,78,72,63,45,false),
                new BuildingSpec("B008_North_Residence_02", 744,140,54,53,42,false),
                new BuildingSpec("B009_Left_Mid_Gray", 290,230,96,112,40,false),
                new BuildingSpec("B010_Left_Mid_Beige", 214,289,86,96,45,true),
                new BuildingSpec("B011_Hotel_West_Block", 535,220,78,118,-35,false),
                new BuildingSpec("B012_Hotel_East_Block", 600,232,48,118,-35,false),
                new BuildingSpec("B013_Hotel_America", 738,275,45,61,-20,true),
                new BuildingSpec("B014_Tiny_Central", 530,316,28,27,-15,false),
                new BuildingSpec("B015_School_West", 345,414,86,95,-43,false),
                new BuildingSpec("B016_School_Main", 510,432,224,86,-25,true),
                new BuildingSpec("B017_School_South", 487,492,185,70,-25,true),
                new BuildingSpec("B018_West_Lower_01", 45,455,98,68,12,false),
                new BuildingSpec("B019_West_Lower_02", 125,525,172,43,12,false),
                new BuildingSpec("B020_West_Lower_03", 160,625,92,42,10,false),
                new BuildingSpec("B021_Restaurant_Beige", 310,628,70,40,-15,true),
                new BuildingSpec("B022_Cekalusa_South_01", 370,676,52,83,-18,false),
                new BuildingSpec("B023_Cekalusa_South_02", 430,690,58,45,-18,false),
                new BuildingSpec("B024_Cekalusa_Beige", 485,650,60,52,-35,true),
                new BuildingSpec("B025_Cekalusa_Gray_Long", 585,626,140,52,-28,false),
                new BuildingSpec("B026_Cekalusa_Gray_South", 650,704,120,68,-25,false),
                new BuildingSpec("B027_Central_South_Large", 790,635,108,185,-18,false),

                new BuildingSpec("B028_Visnjik_North_01", 985,44,52,70,-42,false),
                new BuildingSpec("B029_Visnjik_North_02", 1045,70,54,70,-42,false),
                new BuildingSpec("B030_Visnjik_North_03", 1100,108,50,68,-42,false),
                new BuildingSpec("B031_Visnjik_North_04", 1160,116,55,63,-42,false),
                new BuildingSpec("B032_Derebent_West_01", 980,205,86,90,-42,false),
                new BuildingSpec("B033_Derebent_West_02", 1044,285,64,64,-42,false),
                new BuildingSpec("B034_Derebent_Central_01", 1092,350,48,48,-42,false),
                new BuildingSpec("B035_Derebent_Central_02", 1142,388,52,52,-42,false),
                new BuildingSpec("B036_Derebent_East_01", 1218,222,73,72,-42,false),
                new BuildingSpec("B037_Derebent_East_02", 1264,130,61,63,-42,false),
                new BuildingSpec("B038_Derebent_East_03", 1332,80,55,58,-42,false),
                new BuildingSpec("B039_NE_Beige", 1448,102,67,118,-40,true),
                new BuildingSpec("B040_NE_Gray_01", 1510,55,48,55,-42,false),
                new BuildingSpec("B041_NE_Gray_02", 1570,105,48,70,-42,false),
                new BuildingSpec("B042_NE_Gray_03", 1535,170,48,65,-42,false),

                new BuildingSpec("B043_Mosque_West_01", 820,320,64,76,-42,false),
                new BuildingSpec("B044_Mosque_West_02", 865,378,70,84,-42,false),
                new BuildingSpec("B045_Mosque_Beige", 915,495,76,68,-42,true),
                new BuildingSpec("B046_Mosque_North", 920,270,94,100,-42,false),
                new BuildingSpec("B047_Mosque_East_01", 1005,470,67,80,-42,false),
                new BuildingSpec("B048_Mosque_East_02", 1065,530,68,74,-42,false),
                new BuildingSpec("B049_Radiation_West", 930,650,58,93,-20,false),
                new BuildingSpec("B050_Radiation_Main", 1020,700,68,102,-15,false),
                new BuildingSpec("B051_Radiation_Beige", 1140,610,45,58,-30,true),
                new BuildingSpec("B052_Radiation_South", 1140,760,72,73,8,false),
                new BuildingSpec("B053_Tahtali_01", 1225,620,45,60,-20,false),
                new BuildingSpec("B054_Tahtali_02", 1270,650,43,62,-20,false),
                new BuildingSpec("B055_Tahtali_03", 1320,680,48,65,-20,false),
                new BuildingSpec("B056_Tahtali_Beige", 1360,615,47,68,-25,true),
                new BuildingSpec("B057_Tahtali_04", 1395,725,50,66,-30,false),
                new BuildingSpec("B058_Tahtali_05", 1450,745,48,68,-35,false),

                new BuildingSpec("B059_Provare_West_01", 1190,390,52,63,-42,false),
                new BuildingSpec("B060_Provare_West_02", 1240,430,53,68,-42,false),
                new BuildingSpec("B061_Provare_East_01", 1310,335,60,86,-42,false),
                new BuildingSpec("B062_Provare_East_02", 1365,260,76,100,-42,false),
                new BuildingSpec("B063_Provare_East_03", 1430,210,52,72,-42,false),
                new BuildingSpec("B064_Provare_East_04", 1475,270,43,58,-42,false),
                new BuildingSpec("B065_FarEast_01", 1550,260,52,65,-42,false),
                new BuildingSpec("B066_FarEast_02", 1605,300,54,68,-42,false),
                new BuildingSpec("B067_FarEast_03", 1515,430,58,78,-42,false),
                new BuildingSpec("B068_FarEast_04", 1580,500,62,88,-42,false),
                new BuildingSpec("B069_FarEast_05", 1460,500,57,80,-42,false),
                new BuildingSpec("B070_FarEast_06", 1410,550,48,66,-42,false),
                new BuildingSpec("B071_FarEast_South_01", 1510,630,70,94,-42,false),
                new BuildingSpec("B072_FarEast_South_02", 1580,705,70,95,-42,false),
                new BuildingSpec("B073_FarEast_South_03", 1450,690,48,70,-42,false),
                new BuildingSpec("B074_FarEast_South_04", 1605,815,65,82,-42,false),

                new BuildingSpec("B075_Bottom_Left_01", 270,760,40,60,-10,false),
                new BuildingSpec("B076_Bottom_Left_02", 335,760,38,58,-10,false),
                new BuildingSpec("B077_Bottom_Left_03", 395,780,48,74,-10,false),
                new BuildingSpec("B078_Bottom_Beige_01", 300,820,55,57,3,true),
                new BuildingSpec("B079_Bottom_Beige_02", 365,820,52,55,3,true),
                new BuildingSpec("B080_Bottom_Center_01", 720,780,52,65,-8,false),
                new BuildingSpec("B081_Bottom_Center_02", 850,820,66,60,-5,false),
                new BuildingSpec("B082_Bottom_Center_Beige", 1035,830,58,48,-8,true)
            };

            for (int i = 0; i < specs.Length; i++)
            {
                BuildingSpec spec = specs[i];
                Material fill = spec.beige ? beige : gray;
                Material border = spec.beige ? beigeBorder : grayBorder;
                Vector2 center = PixelToWorld2D(spec.px, spec.py);
                Vector2 size = new Vector2(spec.widthPx / PixelsPerUnit, spec.heightPx / PixelsPerUnit);
                CreateQuad(spec.name + "_Border", center, size + Vector2.one * 0.32f, spec.rotation, 0.62f, border, parent, 18);
                CreateQuad(spec.name, center, size, spec.rotation, 0.42f, fill, parent, 20);
            }
        }

        private static GameObject CreateQuad(string name, Vector2 position, Vector2 size, float rotation, float z, Material material, Transform parent, int sortingOrder)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.position = new Vector3(position.x, position.y, z);
            quad.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            Collider collider = quad.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);
            MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = sortingOrder;
            return quad;
        }

        private static Vector3 PixelToWorld(float px, float py, float z)
        {
            Vector2 p = PixelToWorld2D(px, py);
            return new Vector3(p.x, p.y, z);
        }

        private static Vector2 PixelToWorld2D(float px, float py)
        {
            return new Vector2((px - ImageWidth * 0.5f) / PixelsPerUnit, (ImageHeight * 0.5f - py) / PixelsPerUnit);
        }

        private static GameObject CreateRoot(string name, Transform parent)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            return root;
        }

        private static Material CreateOrUpdateMaterial(string name, Color color)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                throw new InvalidOperationException("No compatible unlit shader was found.");

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
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void CreateCamera(Transform parent)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.position = new Vector3(0f, 0f, -20f);
            cameraObject.transform.rotation = Quaternion.identity;
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = ImageHeight / PixelsPerUnit * 0.5f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.13f, 0.14f, 1f);
            camera.allowHDR = false;
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
