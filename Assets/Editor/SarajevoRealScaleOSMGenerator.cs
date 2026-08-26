using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Xml;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MapLevel.Editor
{
    public static class SarajevoRealScaleOSMGenerator
    {
        private const string ScenePath = "Assets/Scenes/Sarajevo_RealScale_OSM_Level.unity";
        private const string MaterialFolder = "Assets/Generated/SarajevoRealScale";
        private const double West = 18.41315;
        private const double South = 43.86355;
        private const double East = 18.42055;
        private const double North = 43.86635;
        private const double CenterLon = (West + East) * 0.5;
        private const double CenterLat = (South + North) * 0.5;
        private const double MetersPerLatDegree = 111320.0;

        private static readonly double MetersPerLonDegree = MetersPerLatDegree * Math.Cos(CenterLat * Math.PI / 180.0);
        private static readonly float MapWidth = (float)((East - West) * MetersPerLonDegree);
        private static readonly float MapDepth = (float)((North - South) * MetersPerLatDegree);

        [MenuItem("Tools/Map Level/Generate Sarajevo Real-Scale OSM Level")]
        public static void Generate()
        {
            EnsureFolder("Assets", "Scenes");
            EnsureFolder("Assets", "Generated");
            EnsureFolder("Assets/Generated", "SarajevoRealScale");

            string url = string.Format(CultureInfo.InvariantCulture,
                "https://api.openstreetmap.org/api/0.6/map?bbox={0},{1},{2},{3}", West, South, East, North);

            string xmlText;
            using (WebClient client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "Codex-Unity-MapLevel/1.0";
                xmlText = client.DownloadString(url);
            }

            XmlDocument document = new XmlDocument();
            document.LoadXml(xmlText);

            Dictionary<long, Vector2> nodes = ParseNodes(document);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "Sarajevo_RealScale_OSM_Level";

            Material groundMaterial = CreateOrUpdateMaterial("Ground", new Color(0.82f, 0.84f, 0.80f, 1f), 0f);
            Material asphaltMaterial = CreateOrUpdateMaterial("Asphalt", new Color(0.13f, 0.15f, 0.17f, 1f), 0.06f);
            Material serviceRoadMaterial = CreateOrUpdateMaterial("ServiceRoad", new Color(0.23f, 0.25f, 0.27f, 1f), 0.04f);
            Material walkwayMaterial = CreateOrUpdateMaterial("Walkway", new Color(0.48f, 0.50f, 0.51f, 1f), 0.02f);
            Material buildingGray = CreateOrUpdateMaterial("BuildingGray", new Color(0.63f, 0.67f, 0.70f, 1f), 0.08f);
            Material buildingBeige = CreateOrUpdateMaterial("BuildingBeige", new Color(0.72f, 0.66f, 0.53f, 1f), 0.08f);

            GameObject root = new GameObject("SARAJEVO REAL SCALE - 1 UNIT = 1 METER");
            GameObject groundRoot = CreateRoot("00_GROUND", root.transform);
            GameObject roadRoot = CreateRoot("10_ROADS_REAL_WIDTH", root.transform);
            GameObject buildingRoot = CreateRoot("20_BUILDINGS_REAL_FOOTPRINT", root.transform);
            GameObject metadataRoot = CreateRoot("80_METADATA", root.transform);
            GameObject cameraRoot = CreateRoot("90_CAMERA_AND_LIGHT", root.transform);

            CreateCube("Ground_" + MapWidth.ToString("F1") + "m_x_" + MapDepth.ToString("F1") + "m",
                new Vector3(0f, -0.3f, 0f), new Vector3(MapWidth + 20f, 0.5f, MapDepth + 20f),
                Quaternion.identity, groundMaterial, groundRoot.transform, true);

            int roadWays;
            int roadSegments;
            float minRoadWidth;
            float maxRoadWidth;
            CreateRoads(document, nodes, roadRoot.transform, asphaltMaterial, serviceRoadMaterial, walkwayMaterial,
                out roadWays, out roadSegments, out minRoadWidth, out maxRoadWidth);

            int buildingCount;
            float minFootprint;
            float maxFootprint;
            CreateBuildings(document, nodes, buildingRoot.transform, buildingGray, buildingBeige,
                out buildingCount, out minFootprint, out maxFootprint);

            CreateMetadata(metadataRoot.transform, url, roadWays, roadSegments, buildingCount,
                minRoadWidth, maxRoadWidth, minFootprint, maxFootprint);
            CreateCameraAndLighting(cameraRoot.transform);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.58f, 0.60f, 0.62f, 1f);
            RenderSettings.fog = false;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[SarajevoRealScale] Saved {0}. Area {1:F1}m x {2:F1}m, buildings {3}, road ways {4}, segments {5}, widths {6:F1}-{7:F1}m.",
                ScenePath, MapWidth, MapDepth, buildingCount, roadWays, roadSegments, minRoadWidth, maxRoadWidth));
        }

        private static Dictionary<long, Vector2> ParseNodes(XmlDocument document)
        {
            Dictionary<long, Vector2> nodes = new Dictionary<long, Vector2>();
            XmlNodeList nodeList = document.SelectNodes("/osm/node");
            if (nodeList == null)
                return nodes;

            foreach (XmlNode node in nodeList)
            {
                long id;
                double lat;
                double lon;
                if (!long.TryParse(GetAttribute(node, "id"), out id) ||
                    !double.TryParse(GetAttribute(node, "lat"), NumberStyles.Float, CultureInfo.InvariantCulture, out lat) ||
                    !double.TryParse(GetAttribute(node, "lon"), NumberStyles.Float, CultureInfo.InvariantCulture, out lon))
                    continue;
                nodes[id] = GeoToLocal(lon, lat);
            }
            return nodes;
        }

        private static void CreateRoads(XmlDocument document, Dictionary<long, Vector2> nodes, Transform parent,
            Material asphalt, Material service, Material walkway,
            out int wayCount, out int segmentCount, out float minWidth, out float maxWidth)
        {
            wayCount = 0;
            segmentCount = 0;
            minWidth = float.MaxValue;
            maxWidth = 0f;
            XmlNodeList ways = document.SelectNodes("/osm/way");
            if (ways == null)
                return;

            foreach (XmlNode way in ways)
            {
                Dictionary<string, string> tags = ReadTags(way);
                string highway;
                if (!tags.TryGetValue("highway", out highway))
                    continue;

                List<Vector2> points = ReadWayPoints(way, nodes);
                if (points.Count < 2)
                    continue;

                float width = EstimateRoadWidth(tags, highway);
                Material material = IsWalkway(highway) ? walkway : (highway == "service" ? service : asphalt);
                string roadName;
                if (!tags.TryGetValue("name", out roadName) || string.IsNullOrEmpty(roadName))
                    roadName = highway;
                string wayId = GetAttribute(way, "id");
                GameObject roadObject = CreateRoot("OSM_" + wayId + "_" + SanitizeName(roadName) + "_[" + width.ToString("F1") + "m]", parent);

                int createdForWay = 0;
                for (int i = 0; i < points.Count - 1; i++)
                {
                    Vector2 a = points[i];
                    Vector2 b = points[i + 1];
                    Vector2 mid = (a + b) * 0.5f;
                    if (Mathf.Abs(mid.x) > MapWidth * 0.5f + 12f || Mathf.Abs(mid.y) > MapDepth * 0.5f + 12f)
                        continue;

                    Vector2 direction2D = b - a;
                    float length = direction2D.magnitude;
                    if (length < 0.2f)
                        continue;
                    Vector3 center = new Vector3(mid.x, 0.08f, mid.y);
                    Quaternion rotation = Quaternion.LookRotation(new Vector3(direction2D.x, 0f, direction2D.y).normalized, Vector3.up);
                    CreateCube("Segment_" + (i + 1).ToString("000") + "_[" + length.ToString("F1") + "m]",
                        center, new Vector3(width, 0.16f, length + 0.15f), rotation, material, roadObject.transform, true);
                    createdForWay++;
                    segmentCount++;
                }

                if (createdForWay == 0)
                {
                    UnityEngine.Object.DestroyImmediate(roadObject);
                    continue;
                }
                wayCount++;
                minWidth = Mathf.Min(minWidth, width);
                maxWidth = Mathf.Max(maxWidth, width);
            }

            if (wayCount == 0)
                minWidth = 0f;
        }

        private static void CreateBuildings(XmlDocument document, Dictionary<long, Vector2> nodes, Transform parent,
            Material gray, Material beige, out int buildingCount, out float minFootprint, out float maxFootprint)
        {
            buildingCount = 0;
            minFootprint = float.MaxValue;
            maxFootprint = 0f;
            XmlNodeList ways = document.SelectNodes("/osm/way");
            if (ways == null)
                return;

            foreach (XmlNode way in ways)
            {
                Dictionary<string, string> tags = ReadTags(way);
                string buildingType;
                if (!tags.TryGetValue("building", out buildingType))
                    continue;

                List<Vector2> polygon = ReadWayPoints(way, nodes);
                RemoveClosingDuplicate(polygon);
                if (polygon.Count < 3)
                    continue;

                Vector2 center = Average(polygon);
                if (Mathf.Abs(center.x) > MapWidth * 0.5f + 10f || Mathf.Abs(center.y) > MapDepth * 0.5f + 10f)
                    continue;

                float area = Mathf.Abs(SignedArea(polygon));
                if (area < 4f)
                    continue;

                float height = EstimateBuildingHeight(tags, buildingType);
                string wayId = GetAttribute(way, "id");
                string buildingName;
                if (!tags.TryGetValue("name", out buildingName) || string.IsNullOrEmpty(buildingName))
                    buildingName = buildingType;

                Material material = IsBeigeBuilding(tags, buildingType) ? beige : gray;
                GameObject building = CreateExtrudedPolygon(
                    "OSM_" + wayId + "_" + SanitizeName(buildingName) + "_[" + area.ToString("F0") + "m2_H" + height.ToString("F1") + "m]",
                    polygon, height, material, parent);
                if (building == null)
                    continue;

                buildingCount++;
                minFootprint = Mathf.Min(minFootprint, area);
                maxFootprint = Mathf.Max(maxFootprint, area);
            }

            if (buildingCount == 0)
                minFootprint = 0f;
        }

        private static GameObject CreateExtrudedPolygon(string name, List<Vector2> input, float height, Material material, Transform parent)
        {
            List<Vector2> polygon = new List<Vector2>(input);
            if (SignedArea(polygon) < 0f)
                polygon.Reverse();

            List<int> capTriangles = Triangulate(polygon);
            if (capTriangles.Count < 3)
                return null;

            int count = polygon.Count;
            Vector3[] vertices = new Vector3[count * 2];
            for (int i = 0; i < count; i++)
            {
                vertices[i] = new Vector3(polygon[i].x, 0.17f, polygon[i].y);
                vertices[i + count] = new Vector3(polygon[i].x, height + 0.17f, polygon[i].y);
            }

            List<int> triangles = new List<int>();
            for (int i = 0; i < capTriangles.Count; i += 3)
            {
                int a = capTriangles[i];
                int b = capTriangles[i + 1];
                int c = capTriangles[i + 2];
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
                triangles.Add(a + count);
                triangles.Add(c + count);
                triangles.Add(b + count);
            }

            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                triangles.Add(i);
                triangles.Add(next + count);
                triangles.Add(next);
                triangles.Add(i);
                triangles.Add(i + count);
                triangles.Add(next + count);
            }

            Mesh mesh = new Mesh { name = name + "_Mesh" };
            int[] sourceTriangles = triangles.ToArray();
            Vector3[] flatVertices = new Vector3[sourceTriangles.Length];
            Vector2[] flatUv = new Vector2[sourceTriangles.Length];
            int[] flatTriangles = new int[sourceTriangles.Length];
            for (int i = 0; i < sourceTriangles.Length; i++)
            {
                Vector3 vertex = vertices[sourceTriangles[i]];
                flatVertices[i] = vertex;
                flatUv[i] = new Vector2(vertex.x * 0.05f, (vertex.y + vertex.z) * 0.05f);
                flatTriangles[i] = i;
            }

            if (flatVertices.Length > 65000)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = flatVertices;
            mesh.uv = flatUv;
            mesh.triangles = flatTriangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Unwrapping.GenerateSecondaryUVSet(mesh);

            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.OccluderStatic);
            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            MeshCollider collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            return go;
        }

        private static List<int> Triangulate(List<Vector2> polygon)
        {
            List<int> indices = new List<int>();
            for (int i = 0; i < polygon.Count; i++)
                indices.Add(i);
            List<int> triangles = new List<int>();
            int guard = polygon.Count * polygon.Count;

            while (indices.Count > 3 && guard-- > 0)
            {
                bool earFound = false;
                for (int i = 0; i < indices.Count; i++)
                {
                    int previous = indices[(i - 1 + indices.Count) % indices.Count];
                    int current = indices[i];
                    int next = indices[(i + 1) % indices.Count];
                    if (Cross(polygon[current] - polygon[previous], polygon[next] - polygon[current]) <= 0.0001f)
                        continue;

                    bool containsPoint = false;
                    for (int j = 0; j < indices.Count; j++)
                    {
                        int candidate = indices[j];
                        if (candidate == previous || candidate == current || candidate == next)
                            continue;
                        if (PointInTriangle(polygon[candidate], polygon[previous], polygon[current], polygon[next]))
                        {
                            containsPoint = true;
                            break;
                        }
                    }
                    if (containsPoint)
                        continue;

                    triangles.Add(previous);
                    triangles.Add(current);
                    triangles.Add(next);
                    indices.RemoveAt(i);
                    earFound = true;
                    break;
                }
                if (!earFound)
                    break;
            }

            if (indices.Count == 3)
            {
                triangles.Add(indices[0]);
                triangles.Add(indices[1]);
                triangles.Add(indices[2]);
            }
            return triangles;
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float c1 = Cross(b - a, p - a);
            float c2 = Cross(c - b, p - b);
            float c3 = Cross(a - c, p - c);
            return c1 >= -0.0001f && c2 >= -0.0001f && c3 >= -0.0001f;
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static float SignedArea(List<Vector2> polygon)
        {
            float area = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }

        private static void RemoveClosingDuplicate(List<Vector2> points)
        {
            if (points.Count > 2 && Vector2.SqrMagnitude(points[0] - points[points.Count - 1]) < 0.0001f)
                points.RemoveAt(points.Count - 1);
            for (int i = points.Count - 1; i > 0; i--)
            {
                if (Vector2.SqrMagnitude(points[i] - points[i - 1]) < 0.0001f)
                    points.RemoveAt(i);
            }
        }

        private static Vector2 Average(List<Vector2> points)
        {
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < points.Count; i++)
                sum += points[i];
            return sum / points.Count;
        }

        private static List<Vector2> ReadWayPoints(XmlNode way, Dictionary<long, Vector2> nodes)
        {
            List<Vector2> points = new List<Vector2>();
            XmlNodeList refs = way.SelectNodes("nd");
            if (refs == null)
                return points;
            foreach (XmlNode reference in refs)
            {
                long id;
                Vector2 point;
                if (long.TryParse(GetAttribute(reference, "ref"), out id) && nodes.TryGetValue(id, out point))
                    points.Add(point);
            }
            return points;
        }

        private static Dictionary<string, string> ReadTags(XmlNode way)
        {
            Dictionary<string, string> tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            XmlNodeList tagNodes = way.SelectNodes("tag");
            if (tagNodes == null)
                return tags;
            foreach (XmlNode tag in tagNodes)
            {
                string key = GetAttribute(tag, "k");
                string value = GetAttribute(tag, "v");
                if (!string.IsNullOrEmpty(key))
                    tags[key] = value;
            }
            return tags;
        }

        private static float EstimateRoadWidth(Dictionary<string, string> tags, string highway)
        {
            string widthValue;
            float width;
            if (tags.TryGetValue("width", out widthValue) && TryParseMeters(widthValue, out width))
                return Mathf.Clamp(width, 1f, 14f);

            string lanesValue;
            int lanes;
            if (tags.TryGetValue("lanes", out lanesValue) && int.TryParse(lanesValue, out lanes) && lanes > 0)
                return Mathf.Clamp(lanes * 3.0f, 3f, 14f);

            switch (highway)
            {
                case "motorway": return 14f;
                case "trunk": return 11f;
                case "primary": return 9f;
                case "secondary": return 8f;
                case "tertiary": return 7f;
                case "residential": return 5.5f;
                case "unclassified": return 5f;
                case "service": return 3.5f;
                case "living_street": return 4.5f;
                case "pedestrian": return 3f;
                case "steps": return 2f;
                case "footway": return 1.8f;
                case "path": return 1.5f;
                case "track": return 3f;
                default: return 3f;
            }
        }

        private static float EstimateBuildingHeight(Dictionary<string, string> tags, string buildingType)
        {
            string heightValue;
            float height;
            if (tags.TryGetValue("height", out heightValue) && TryParseMeters(heightValue, out height))
                return Mathf.Clamp(height, 2.5f, 80f);

            string levelsValue;
            float levels;
            if (tags.TryGetValue("building:levels", out levelsValue) &&
                float.TryParse(levelsValue, NumberStyles.Float, CultureInfo.InvariantCulture, out levels))
                return Mathf.Clamp(levels * 3f, 3f, 80f);

            switch (buildingType)
            {
                case "apartments": return 15f;
                case "residential": return 12f;
                case "commercial": return 10f;
                case "retail": return 8f;
                case "school": return 12f;
                case "university": return 15f;
                case "hospital": return 15f;
                case "church": return 12f;
                case "mosque": return 12f;
                case "house": return 8f;
                case "detached": return 8f;
                case "garage": return 3f;
                case "garages": return 3f;
                case "shed": return 3f;
                default: return 9f;
            }
        }

        private static bool TryParseMeters(string value, out float meters)
        {
            meters = 0f;
            if (string.IsNullOrEmpty(value))
                return false;
            string clean = value.ToLowerInvariant().Replace("meters", "").Replace("meter", "").Replace("m", "").Trim();
            int semicolon = clean.IndexOf(';');
            if (semicolon >= 0)
                clean = clean.Substring(0, semicolon);
            return float.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out meters);
        }

        private static bool IsWalkway(string highway)
        {
            return highway == "footway" || highway == "path" || highway == "steps" || highway == "pedestrian";
        }

        private static bool IsBeigeBuilding(Dictionary<string, string> tags, string buildingType)
        {
            if (buildingType == "school" || buildingType == "university" || buildingType == "hospital" ||
                buildingType == "commercial" || buildingType == "retail" || buildingType == "mosque" || buildingType == "church")
                return true;
            return tags.ContainsKey("amenity") || tags.ContainsKey("shop") || tags.ContainsKey("tourism");
        }

        private static Vector2 GeoToLocal(double lon, double lat)
        {
            float x = (float)((lon - CenterLon) * MetersPerLonDegree);
            float z = (float)((lat - CenterLat) * MetersPerLatDegree);
            return new Vector2(x, z);
        }

        private static string GetAttribute(XmlNode node, string name)
        {
            if (node == null || node.Attributes == null || node.Attributes[name] == null)
                return string.Empty;
            return node.Attributes[name].Value;
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "Unnamed";
            char[] invalid = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };
            for (int i = 0; i < invalid.Length; i++)
                value = value.Replace(invalid[i], '_');
            return value.Length > 60 ? value.Substring(0, 60) : value;
        }

        private static GameObject CreateCube(string name, Vector3 position, Vector3 scale, Quaternion rotation,
            Material material, Transform parent, bool keepCollider)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, true);
            cube.transform.position = position;
            cube.transform.rotation = rotation;
            cube.transform.localScale = scale;
            GameObjectUtility.SetStaticEditorFlags(cube,
                StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.OccluderStatic);
            MeshRenderer renderer = cube.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = scale.y > 0.5f ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            if (!keepCollider)
            {
                Collider collider = cube.GetComponent<Collider>();
                if (collider != null)
                    UnityEngine.Object.DestroyImmediate(collider);
            }
            return cube;
        }

        private static GameObject CreateRoot(string name, Transform parent)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            return root;
        }

        private static Material CreateOrUpdateMaterial(string name, Color color, float smoothness)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                throw new InvalidOperationException("No compatible lit shader was found.");

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

        private static void CreateMetadata(Transform parent, string url, int roadWays, int roadSegments, int buildings,
            float minRoadWidth, float maxRoadWidth, float minFootprint, float maxFootprint)
        {
            string[] entries =
            {
                "Scale: 1 Unity unit = 1 meter",
                "Bounds: " + MapWidth.ToString("F1") + "m x " + MapDepth.ToString("F1") + "m",
                "Source: OpenStreetMap API",
                "Source URL: " + url,
                "Road ways: " + roadWays + ", segments: " + roadSegments,
                "Road widths: " + minRoadWidth.ToString("F1") + "m to " + maxRoadWidth.ToString("F1") + "m",
                "Buildings: " + buildings,
                "Building footprint areas: " + minFootprint.ToString("F1") + "m2 to " + maxFootprint.ToString("F1") + "m2",
                "OSM attribution: Data © OpenStreetMap contributors, ODbL 1.0"
            };
            for (int i = 0; i < entries.Length; i++)
                CreateRoot(entries[i], parent);
        }

        private static void CreateCameraAndLighting(Transform parent)
        {
            GameObject lightObject = new GameObject("Directional Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(52f, -32f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.95f, 0.86f, 1f);
            light.shadows = LightShadows.Soft;

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.position = new Vector3(0f, 500f, 0f);
            cameraObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = MapDepth * 0.5f + 10f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 900f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.10f, 0.11f, 1f);
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
