using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARCore;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;
using Unity.XR.CoreUtils;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using TMPro;

/// <summary>
/// Script de extensión del editor de Unity que automatiza la instanciación de los
/// GameObjects de los 4 pisos del Edificio Nuevo de la ETITC, las conexiones entre
/// nodos y el montaje completo de la escena (Canvas UI, NavigationManager y rig AR).
///
/// Uso:
///   - Menú:  ETITC Tools / Generar Grafo y Escena del Campus
///   - Batch: Unity.exe -batchmode -quit -executeMethod CampusGraphAutoBuilder.GenerateCampusNodes
/// </summary>
public static class CampusGraphAutoBuilder
{
    // ---------------------------------------------------------------------
    // Constantes de configuración
    // ---------------------------------------------------------------------
    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const string RootName = "CampusGraphManager";
    private const string CampusMapPath = "Assets/Textures/SatelliteMApView.jpg";
    private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
    private const string UrpRendererPath = "Assets/Settings/UniversalRendererData.asset";
    private const string UrpPipelinePath = "Assets/Settings/URP-ETITC.asset";

    // Resolución de referencia del Canvas (README).
    private static readonly Vector2 ReferenceResolution = new Vector2(1080f, 1920f);

    // =====================================================================
    // Punto de entrada
    // =====================================================================
    [MenuItem("ETITC Tools/Generar Grafo y Escena del Campus", false, 0)]
    public static void GenerateCampusNodes()
    {
        try
        {
            EnsureFolder("Assets", "Scenes");
            EnsureFolder("Assets", "Settings");

            SetupUniversalRenderPipeline();
            EnsureARBackgroundRendererFeature();
            EnsureTMPEssentialResources();
            ConfigureXRPlugins();
            NavigationTextureGenerator.GenerateAll();

            // URP rinde mejor en espacio de color lineal.
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                PlayerSettings.colorSpace = ColorSpace.Linear;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CampusGraphManager graph = BuildGraph();
            NavigationManager navigation = BuildNavigationManager(graph);
            BuildCanvas(navigation, graph);
            BuildARRig(navigation);
            BuildLighting();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            AddSceneToBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[CampusGraphAutoBuilder] Escena generada correctamente en " + ScenePath);
        }
        catch (System.Exception exception)
        {
            Debug.LogError("[CampusGraphAutoBuilder] Error al generar la escena: " + exception);
            throw;
        }
    }

    // =====================================================================
    // Grafo del campus
    // =====================================================================
    private static int currentBuildingOrder;

    private static CampusGraphManager BuildGraph()
    {
        CampusGraphData data = CampusGraphData.Load();
        if (data == null)
        {
            throw new Exception("No se pudo cargar Assets/Resources/CampusGraph.json. " +
                                "Sin ese archivo no se puede generar el grafo del campus.");
        }

        string dataError;
        if (!data.Validate(out dataError))
        {
            throw new Exception("CampusGraph.json invalido: " + dataError);
        }

        GameObject root = new GameObject(RootName);
        CampusGraphManager graph = root.AddComponent<CampusGraphManager>();

        Dictionary<string, Node> byId = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        List<Node> allNodes = new List<Node>();

        for (int b = 0; b < data.buildings.Count; b++)
        {
            CampusBuilding building = data.buildings[b];
            currentBuildingOrder = b;

            GameObject buildingRoot = new GameObject("Edificio_" + building.id);
            buildingRoot.transform.SetParent(root.transform, false);
            buildingRoot.transform.position = new Vector3(building.origin.x, 0f, building.origin.z);
            buildingRoot.transform.rotation = Quaternion.Euler(0f, building.rotationDegrees, 0f);

            List<Node> stairNodes = new List<Node>();
            List<Node> elevatorNodes = new List<Node>();

            for (int f = 0; f < building.floors.Count; f++)
            {
                CampusFloor floor = building.floors[f];
                float floorY = (floor.index - 1) * building.floorHeight;

                GameObject floorGO = new GameObject("Piso_" + floor.index);
                floorGO.transform.SetParent(buildingRoot.transform, false);
                floorGO.transform.localPosition = new Vector3(0f, floorY, 0f);

                // --- Corredor: un nodo por cada X relevante (puertas, escalera, extremos) ---
                List<float> xs = new List<float>
                {
                    building.corridorStartX,
                    building.corridorEndX,
                    floor.stairX,
                    floor.elevatorX
                };

                if (floor.rooms != null)
                {
                    for (int r = 0; r < floor.rooms.Count; r++)
                    {
                        if (floor.rooms[r] != null) xs.Add(floor.rooms[r].x);
                    }
                }

                if (floor.entrances != null)
                {
                    for (int e = 0; e < floor.entrances.Count; e++)
                    {
                        if (floor.entrances[e] != null) xs.Add(floor.entrances[e].x);
                    }
                }

                xs.Sort();
                List<float> corridorXs = new List<float>();
                for (int i = 0; i < xs.Count; i++)
                {
                    if (corridorXs.Count == 0 || Mathf.Abs(corridorXs[corridorXs.Count - 1] - xs[i]) > 0.25f)
                    {
                        corridorXs.Add(xs[i]);
                    }
                }

                List<Node> corridor = new List<Node>();
                for (int i = 0; i < corridorXs.Count; i++)
                {
                    string corridorId = building.id + "_P" + floor.index + "_C" + i;
                    Node corridorNode = CreateNode(
                        floorGO.transform,
                        "Pasillo " + building.shortName + " P" + floor.index + " (" + corridorXs[i].ToString("F1") + ")",
                        LocalToWorld(building, corridorXs[i], floorY, building.corridorZ),
                        new Color(0.55f, 0.62f, 0.70f),
                        0.22f);

                    ApplyMetadata(corridorNode, corridorId, building, floor.index, floorY, "pasillo", false);
                    RegisterNode(byId, allNodes, corridorNode);
                    corridor.Add(corridorNode);
                }

                for (int i = 1; i < corridor.Count; i++)
                {
                    ConnectBidirectional(corridor[i - 1], corridor[i]);
                }

                // --- Escalera y ascensor ---
                Node stair = CreateNode(
                    floorGO.transform,
                    "Escalera " + building.shortName + " P" + floor.index,
                    LocalToWorld(building, floor.stairX, floorY, floor.stairZ),
                    new Color(1.00f, 0.30f, 0.30f),
                    0.55f);

                ApplyMetadata(stair, building.id + "_P" + floor.index + "_escalera",
                    building, floor.index, floorY, "escalera", true);
                RegisterNode(byId, allNodes, stair);
                ConnectBidirectional(stair, NearestByX(corridor, floor.stairX, buildingRoot.transform));
                stairNodes.Add(stair);

                if (floor.hasElevator)
                {
                    Node elevator = CreateNode(
                        floorGO.transform,
                        "Ascensor " + building.shortName + " P" + floor.index,
                        LocalToWorld(building, floor.elevatorX, floorY, floor.elevatorZ),
                        new Color(1.00f, 0.60f, 0.15f),
                        0.45f);

                    ApplyMetadata(elevator, building.id + "_P" + floor.index + "_ascensor",
                        building, floor.index, floorY, "ascensor", true);
                    RegisterNode(byId, allNodes, elevator);
                    ConnectBidirectional(elevator, NearestByX(corridor, floor.elevatorX, buildingRoot.transform));
                    elevatorNodes.Add(elevator);
                }

                // --- Salones, oficinas, baños y biblioteca ---
                if (floor.rooms != null)
                {
                    for (int r = 0; r < floor.rooms.Count; r++)
                    {
                        CampusRoom room = floor.rooms[r];
                        if (room == null) continue;

                        float z = building.corridorZ + (room.side == "south" ? -building.doorOffset : building.doorOffset);
                        Node roomNode = CreateNode(
                            floorGO.transform,
                            room.name,
                            LocalToWorld(building, room.x, floorY, z),
                            GetNodeColor(room.type),
                            0.42f);

                        ApplyMetadata(roomNode, room.id, building, floor.index, floorY,
                            string.IsNullOrEmpty(room.type) ? "salon" : room.type, room.showInList);
                        RegisterNode(byId, allNodes, roomNode);
                        ConnectBidirectional(roomNode, NearestByX(corridor, room.x, buildingRoot.transform));
                    }
                }

                // --- Entradas reales del edificio (definidas en el JSON) ---
                if (floor.entrances != null)
                {
                    for (int e = 0; e < floor.entrances.Count; e++)
                    {
                        CampusEntrance entrance = floor.entrances[e];
                        if (entrance == null || string.IsNullOrEmpty(entrance.id)) continue;

                        Node entranceNode = CreateNode(
                            floorGO.transform,
                            entrance.name,
                            LocalToWorld(building, entrance.x, floorY, entrance.z),
                            Color.white,
                            0.55f);

                        ApplyMetadata(entranceNode, entrance.id, building, floor.index, floorY,
                            string.IsNullOrEmpty(entrance.type) ? "entrada" : entrance.type,
                            entrance.showInList);
                        RegisterNode(byId, allNodes, entranceNode);

                        Node corridorNearest = NearestByX(corridor, entrance.x, buildingRoot.transform);
                        ConnectBidirectional(entranceNode, corridorNearest);
                    }
                }

                CreateFloorPlate(floorGO.transform, building, floor);
            }

            // Conexiones verticales: la escalera pesa un poco mas que la distancia real.
            for (int i = 1; i < stairNodes.Count; i++)
            {
                ConnectBidirectional(stairNodes[i - 1], stairNodes[i], building.floorHeight + 1.5f);
            }

            for (int i = 1; i < elevatorNodes.Count; i++)
            {
                ConnectBidirectional(elevatorNodes[i - 1], elevatorNodes[i], 2.0f);
            }
        }

        // --- Exteriores: canchas, parqueadero, aulas y caminos ---
        GameObject exteriorRoot = new GameObject("Exteriores");
        exteriorRoot.transform.SetParent(root.transform, false);

        for (int i = 0; i < data.exterior.nodes.Count; i++)
        {
            CampusExteriorNode exterior = data.exterior.nodes[i];
            if (exterior == null) continue;

            Node node = CreateNode(
                exteriorRoot.transform,
                exterior.name,
                new Vector3(exterior.x, 0f, exterior.z),
                GetNodeColor(exterior.type),
                0.65f);

            node.nodeId = exterior.id;
            node.buildingId = string.Empty;
            node.buildingName = "Campus";
            node.buildingShortName = "Campus";
            node.buildingOrder = 99;
            node.floor = 0;
            node.floorY = 0f;
            node.nodeType = string.IsNullOrEmpty(exterior.type) ? "exterior" : exterior.type;
            node.showInList = exterior.showInList;
            node.qrId = "SONEA:" + exterior.id;

            RegisterNode(byId, allNodes, node);
        }

        for (int i = 0; i < data.exterior.edges.Count; i++)
        {
            CampusEdgeLink edge = data.exterior.edges[i];
            if (edge == null) continue;
            ConnectById(byId, edge.from, edge.to, 0f);
        }

        // --- Puertas edificio <-> exterior ---
        for (int i = 0; i < data.connections.Count; i++)
        {
            CampusEdgeLink connection = data.connections[i];
            if (connection == null) continue;
            ConnectById(byId, connection.from, connection.to, 0f);
        }

        graph.allNodes = allNodes
            .Where(n => n != null)
            .OrderBy(n => n.floor <= 0 ? 1 : 0)
            .ThenBy(n => n.buildingName)
            .ThenBy(n => n.floor)
            .ThenBy(n => n.DisplayName)
            .ToList();

        Debug.Log("[CampusGraphAutoBuilder] Grafo construido: " + graph.allNodes.Count + " nodos (" +
                  data.buildings.Count + " edificios, " + data.exterior.nodes.Count + " puntos exteriores).");

        EditorUtility.SetDirty(graph);
        return graph;
    }

    private static void ApplyMetadata(Node node, string nodeId, CampusBuilding building,
        int floor, float floorY, string nodeType, bool showInList)
    {
        node.nodeId = nodeId;
        node.buildingId = building.id;
        node.buildingName = building.name;
        node.buildingShortName = building.shortName;
        node.buildingOrder = currentBuildingOrder;
        node.floor = floor;
        node.floorY = floorY;
        node.nodeType = nodeType;
        node.showInList = showInList;
        node.qrId = "SONEA:" + nodeId;
    }

    private static void RegisterNode(Dictionary<string, Node> byId, List<Node> allNodes, Node node)
    {
        if (node == null || string.IsNullOrEmpty(node.nodeId)) return;

        if (byId.ContainsKey(node.nodeId))
        {
            Debug.LogError("[CampusGraphAutoBuilder] Id duplicado en el grafo: " + node.nodeId);
            return;
        }

        byId[node.nodeId] = node;
        allNodes.Add(node);
    }

    private static void ConnectById(Dictionary<string, Node> byId, string from, string to, float weight)
    {
        Node a;
        Node b;

        if (!byId.TryGetValue(from ?? string.Empty, out a) || !byId.TryGetValue(to ?? string.Empty, out b))
        {
            Debug.LogWarning("[CampusGraphAutoBuilder] Conexion ignorada (no existe el id): " + from + " -> " + to);
            return;
        }

        ConnectBidirectional(a, b, weight);
    }

    /// <summary>Pasa una coordenada local del edificio a coordenadas del campus.</summary>
    private static Vector3 LocalToWorld(CampusBuilding building, float localX, float y, float localZ)
    {
        Quaternion rotation = Quaternion.Euler(0f, building.rotationDegrees, 0f);
        Vector3 local = new Vector3(localX, y, localZ);
        Vector3 rotated = rotation * local;

        return new Vector3(
            building.origin.x + rotated.x,
            rotated.y,
            building.origin.z + rotated.z);
    }

    private static Node NearestByX(List<Node> nodes, float localX, Transform buildingRoot)
    {
        if (nodes == null || nodes.Count == 0) return null;

        Node best = nodes[0];
        Vector3 bestLocal = buildingRoot != null
            ? buildingRoot.InverseTransformPoint(best.transform.position)
            : best.transform.position;
        float bestDistance = Mathf.Abs(bestLocal.x - localX);

        for (int i = 1; i < nodes.Count; i++)
        {
            Vector3 local = buildingRoot != null
                ? buildingRoot.InverseTransformPoint(nodes[i].transform.position)
                : nodes[i].transform.position;

            float distance = Mathf.Abs(local.x - localX);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = nodes[i];
            }
        }
        return best;
    }

    private static Color GetNodeColor(string nodeType)
    {
        switch (nodeType)
        {
            case "escalera": return new Color(1.00f, 0.30f, 0.30f);
            case "ascensor": return new Color(1.00f, 0.60f, 0.15f);
            case "bano": return new Color(1.00f, 0.85f, 0.30f);
            case "oficina": return new Color(0.35f, 0.65f, 1.00f);
            case "biblioteca": return new Color(0.75f, 0.45f, 1.00f);
            case "entrada": return Color.white;
            case "salida": return new Color(0.75f, 0.75f, 0.75f);
            case "cancha": return new Color(0.30f, 0.90f, 0.45f);
            case "parqueadero": return new Color(0.60f, 0.65f, 0.70f);
            case "hall": return new Color(0.90f, 0.80f, 0.55f);
            case "pasillo": return new Color(0.55f, 0.62f, 0.70f);
            case "exterior": return new Color(0.70f, 0.75f, 0.80f);
            default: return new Color(0.25f, 0.85f, 0.35f);
        }
    }

    /// <summary>Crea un GameObject con el componente Node y lo registra en la jerarquía.</summary>
    private static Node CreateNode(Transform parent, string nodeName, Vector3 worldPosition, Color gizmoColor, float gizmoRadius)
    {
        GameObject nodeGO = new GameObject("Nodo_" + nodeName.Replace(" ", "_"));
        nodeGO.transform.SetParent(parent, false);

        Node node = nodeGO.AddComponent<Node>();
        node.nodeName = nodeName;
        node.gizmoColor = gizmoColor;
        node.gizmoRadius = gizmoRadius;

        nodeGO.transform.position = worldPosition;
        return node;
    }

    /// <summary>Crea la arista en ambos sentidos (bidireccional).</summary>
    private static void ConnectBidirectional(Node a, Node b, float weight = 0f)
    {
        if (a == null || b == null || a == b) return;

        if (!a.HasEdgeTo(b)) a.AddEdge(b, weight);
        if (!b.HasEdgeTo(a)) b.AddEdge(a, weight);
    }

    /// <summary>Placa horizontal que representa la superficie del piso (solo visible en el editor).</summary>
    private static void CreateFloorPlate(Transform parent, CampusBuilding building, CampusFloor floor)
    {
        GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = "PlanoPiso_" + building.id + "_P" + floor.index;
        plate.transform.SetParent(parent, false);
        plate.transform.localPosition = new Vector3(building.length * 0.5f, -0.06f, building.width * 0.5f);
        plate.transform.localScale = new Vector3(building.length, 0.1f, building.width);

        Collider collider = plate.GetComponent<Collider>();
        if (collider != null) UnityEngine.Object.DestroyImmediate(collider);

        plate.GetComponent<Renderer>().sharedMaterial = GetFloorMaterial(GetFloorColor(floor.index));
    }

    private static Color GetFloorColor(int floorIndex)
    {
        switch (floorIndex)
        {
            case 1: return new Color(0.25f, 0.85f, 0.35f);
            case 2: return new Color(0.25f, 0.70f, 1.00f);
            case 3: return new Color(1.00f, 0.80f, 0.25f);
            default: return new Color(0.85f, 0.40f, 1.00f);
        }
    }

    private static Material _floorMaterial;

    private static Material GetFloorMaterial(Color tint)
    {
        if (_floorMaterial != null) return _floorMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) return null;

        _floorMaterial = new Material(shader);
        _floorMaterial.name = "MaterialPlanoPiso";
        _floorMaterial.SetColor("_BaseColor", new Color(0.22f, 0.24f, 0.28f));
        _floorMaterial.SetColor("_Color", new Color(0.22f, 0.24f, 0.28f));
        return _floorMaterial;
    }

    // =====================================================================
    // NavigationManager + visualizador + flecha AR
    // =====================================================================
    private static NavigationManager BuildNavigationManager(CampusGraphManager graph)
    {
        GameObject navGO = new GameObject("NavigationManager");
        NavigationManager navigation = navGO.AddComponent<NavigationManager>();
        navigation.graphManager = graph;

        navigation.pathfinder = navGO.AddComponent<DijkstraPathfinder>();

        ARPathVisualizer visualizer = navGO.AddComponent<ARPathVisualizer>();
        navigation.pathVisualizer = visualizer;

        LineRenderer lineRenderer = navGO.AddComponent<LineRenderer>();
        lineRenderer.useWorldSpace = true;
        lineRenderer.widthMultiplier = visualizer.lineWidth;
        lineRenderer.numCapVertices = 4;
        lineRenderer.numCornerVertices = 2;
        lineRenderer.sharedMaterial = ARPathVisualizer.CreateRouteMaterial();
        visualizer.lineRenderer = lineRenderer;

        // Flecha decal apoyada en el piso (estilo Apple Maps).
        GameObject arrowRoot = new GameObject("FlechaGuiadora");
        arrowRoot.transform.SetParent(navGO.transform, false);
        arrowRoot.transform.position = new Vector3(0f, 0.02f, 0f);

        ARArrowGuide arrowGuide = arrowRoot.AddComponent<ARArrowGuide>();
        navigation.arrowGuide = arrowGuide;

        GameObject arrowVisual = new GameObject("FlechaDecal");
        arrowVisual.transform.SetParent(arrowRoot.transform, false);
        arrowVisual.transform.localPosition = Vector3.zero;

        MeshFilter meshFilter = arrowVisual.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = EnsureDecalQuadMesh();

        MeshRenderer meshRenderer = arrowVisual.AddComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        Material blueMaterial = EnsureDecalMaterial("FlechaAR_Azul", "arrow_decal_blue.png");
        Material amberMaterial = EnsureDecalMaterial("FlechaAR_Ambar", "arrow_decal_amber.png");
        Material greenMaterial = EnsureDecalMaterial("FlechaAR_Verde", "arrow_decal_green.png");
        meshRenderer.sharedMaterial = blueMaterial;

        arrowGuide.arrowTransform = arrowVisual.transform;
        arrowGuide.arrowRenderer = meshRenderer;
        arrowGuide.stateMaterials = new[] { blueMaterial, amberMaterial, greenMaterial };
        arrowGuide.navigationManager = navigation;
        arrowGuide.arriveThreshold = 1.5f;
        arrowGuide.rotationSpeed = 7.0f;

        EditorUtility.SetDirty(navigation);
        return navigation;
    }

    /// <summary>Quad de 1 x 1 m en el plano XZ, con la punta de la textura hacia +Z.</summary>
    private static Mesh EnsureDecalQuadMesh()
    {
        const string meshPath = "Assets/Meshes/FlechaDecal.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (existing != null) return existing;

        EnsureFolder("Assets", "Meshes");

        Vector3[] vertices =
        {
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, 0.5f),
            new Vector3(-0.5f, 0f, 0.5f)
        };

        Vector2[] uvs =
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };

        int[] triangles = { 0, 2, 1, 0, 3, 2 };

        Mesh mesh = new Mesh();
        mesh.name = "FlechaDecal";
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.RecalculateBounds();

        AssetDatabase.CreateAsset(mesh, meshPath);
        AssetDatabase.SaveAssets();
        return mesh;
    }

    /// <summary>Material URP/Unlit transparente para el decal, con ZWrite desactivado.</summary>
    private static Material EnsureDecalMaterial(string assetName, string textureFile)
    {
        string materialPath = "Assets/Materials/" + assetName + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

        if (material == null)
        {
            EnsureFolder("Assets", "Materials");

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null)
            {
                Debug.LogError("[CampusGraphAutoBuilder] No se encontro el shader para el decal.");
                return null;
            }

            material = new Material(shader);
            material.name = assetName;
            AssetDatabase.CreateAsset(material, materialPath);
        }

        Texture2D texture = NavigationTextureGenerator.Load(textureFile);

        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
        if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = 3000;

        EditorUtility.SetDirty(material);
        return material;
    }

    // =====================================================================
    // Canvas / UI
    // =====================================================================
    private static void BuildCanvas(NavigationManager navigation, CampusGraphManager graph)
    {
        GameObject canvasGO = new GameObject("MainCanvas");

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        GameObject eventSystemGO = new GameObject("EventSystem");
        eventSystemGO.transform.SetParent(canvasGO.transform, false);
        eventSystemGO.AddComponent<EventSystem>();
        eventSystemGO.AddComponent<StandaloneInputModule>();

        TMP_DefaultControls.Resources resources = GetStandardResources();

        // ---------------- Panel 2D ----------------
        GameObject panel2D = new GameObject("Panel2DMap", typeof(RectTransform));
        panel2D.transform.SetParent(canvasGO.transform, false);
        Image panel2DImage = panel2D.AddComponent<Image>();
        panel2DImage.color = new Color(0.05f, 0.07f, 0.10f, 1f);
        Stretch(panel2D.GetComponent<RectTransform>());

        // ---------------- Medidas del layout ----------------
        const float TitleHeight = 60f;
        const float SearchHeight = 100f;
        const float LabelHeight = 40f;
        const float DropdownHeight = 140f;
        const float ButtonHeight = 128f;
        const float ValidateHeight = 96f;
        const float MapMaxHeight = 720f;

        Texture2D mapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(CampusMapPath);

        float mapHeight = 0f;
        float mapWidth = 0f;
        if (mapTexture != null)
        {
            float mapAspect = mapTexture.height > 0 ? (float)mapTexture.width / mapTexture.height : 0.70f;
            float mapMaxWidth = ReferenceResolution.x - 80f;
            mapHeight = Mathf.Min(MapMaxHeight, mapMaxWidth / mapAspect);
            mapWidth = mapHeight * mapAspect;
        }

        // Altura total para centrar el bloque verticalmente en cualquier pantalla.
        float totalHeight = TitleHeight + 12f;
        if (mapTexture != null) totalHeight += mapHeight + 24f;
        totalHeight += SearchHeight + 22f;
        totalHeight += (LabelHeight + 6f) + DropdownHeight + 22f;
        totalHeight += (LabelHeight + 6f) + DropdownHeight + 26f;
        totalHeight += ButtonHeight;
        totalHeight += 16f + ValidateHeight;

        // Contenedor centrado; se desplaza 20 px hacia abajo para equilibrar el boton.
        GameObject contentGO = new GameObject("Panel2DContent", typeof(RectTransform));
        contentGO.transform.SetParent(panel2D.transform, false);
        RectTransform contentRT = contentGO.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0f, 0.5f);
        contentRT.anchorMax = new Vector2(1f, 0.5f);
        contentRT.pivot = new Vector2(0.5f, 0.5f);
        contentRT.sizeDelta = new Vector2(0f, totalHeight);
        contentRT.anchoredPosition = new Vector2(0f, -20f);

        float cursor = 0f;

        TMP_Text title = CreateLabel("Titulo", contentGO.transform,
            "ETITC - Campus AR Navigator", 46f, TextAlignmentOptions.Center);
        PlaceTop(title.rectTransform, cursor, TitleHeight);
        title.color = new Color(0.25f, 0.85f, 1f);
        cursor += TitleHeight + 12f;

        // Mapa 2D del campus (vista satelital), respetando la relacion de aspecto.
        if (mapTexture != null)
        {
            GameObject mapGO = new GameObject("CampusMapImage", typeof(RectTransform));
            mapGO.transform.SetParent(contentGO.transform, false);
            RawImage rawImage = mapGO.AddComponent<RawImage>();
            rawImage.texture = mapTexture;
            rawImage.color = Color.white;

            RectTransform mapRT = mapGO.GetComponent<RectTransform>();
            mapRT.anchorMin = new Vector2(0.5f, 1f);
            mapRT.anchorMax = new Vector2(0.5f, 1f);
            mapRT.pivot = new Vector2(0.5f, 1f);
            mapRT.sizeDelta = new Vector2(mapWidth, mapHeight);
            mapRT.anchoredPosition = new Vector2(0f, -cursor);

            cursor += mapHeight + 24f;
        }

        // Buscador.
        GameObject inputGO = TMP_DefaultControls.CreateInputField(resources);
        inputGO.name = "SearchInputField";
        inputGO.transform.SetParent(contentGO.transform, false);

        TMP_InputField searchInput = inputGO.GetComponent<TMP_InputField>();
        TMP_Text placeholderText = searchInput.placeholder as TMP_Text;
        if (placeholderText != null) placeholderText.text = "Buscar salón, biblioteca, baños...";
        if (searchInput.textComponent != null) searchInput.textComponent.fontSize = 34f;
        StyleControl(inputGO, new Color(0.12f, 0.15f, 0.20f, 1f));
        PlaceTop(inputGO.GetComponent<RectTransform>(), cursor, SearchHeight);
        cursor += SearchHeight + 22f;

        // Origen.
        TMP_Text originLabel = CreateLabel("OriginLabel", contentGO.transform, "Punto de partida", 32f, TextAlignmentOptions.Left);
        PlaceTop(originLabel.rectTransform, cursor, LabelHeight);
        cursor += LabelHeight + 6f;

        GameObject originGO = TMP_DefaultControls.CreateDropdown(resources);
        originGO.name = "OriginDropdown";
        originGO.transform.SetParent(contentGO.transform, false);
        TMP_Dropdown originDropdown = originGO.GetComponent<TMP_Dropdown>();
        StyleControl(originGO, new Color(0.12f, 0.15f, 0.20f, 1f));
        StyleDropdownTemplate(originDropdown);
        EnlargeDropdown(originDropdown, DropdownHeight, 38f);
        PlaceTop(originGO.GetComponent<RectTransform>(), cursor, DropdownHeight);
        cursor += DropdownHeight + 22f;

        // Destino.
        TMP_Text destLabel = CreateLabel("DestinationLabel", contentGO.transform, "Destino", 32f, TextAlignmentOptions.Left);
        PlaceTop(destLabel.rectTransform, cursor, LabelHeight);
        cursor += LabelHeight + 6f;

        GameObject destGO = TMP_DefaultControls.CreateDropdown(resources);
        destGO.name = "DestinationDropdown";
        destGO.transform.SetParent(contentGO.transform, false);
        TMP_Dropdown destinationDropdown = destGO.GetComponent<TMP_Dropdown>();
        StyleControl(destGO, new Color(0.12f, 0.15f, 0.20f, 1f));
        StyleDropdownTemplate(destinationDropdown);
        EnlargeDropdown(destinationDropdown, DropdownHeight, 38f);
        PlaceTop(destGO.GetComponent<RectTransform>(), cursor, DropdownHeight);
        cursor += DropdownHeight + 26f;

        // Botón de inicio.
        GameObject startGO = TMP_DefaultControls.CreateButton(resources);
        startGO.name = "BtnStartNavigation";
        startGO.transform.SetParent(contentGO.transform, false);
        Button startButton = startGO.GetComponent<Button>();
        TMP_Text startLabel = startGO.GetComponentInChildren<TMP_Text>();
        if (startLabel != null)
        {
            startLabel.text = "Iniciar Ruta AR";
            startLabel.fontSize = 40f;
        }
        StyleControl(startGO, new Color(0.15f, 0.55f, 0.85f, 1f));
        PlaceTop(startGO.GetComponent<RectTransform>(), cursor, ButtonHeight);
        cursor += ButtonHeight + 16f;

        // Botón de validación del grafo (no requiere caminar).
        GameObject validateGO = TMP_DefaultControls.CreateButton(resources);
        validateGO.name = "BtnValidateGraph";
        validateGO.transform.SetParent(contentGO.transform, false);
        Button validateButton = validateGO.GetComponent<Button>();
        TMP_Text validateLabel = validateGO.GetComponentInChildren<TMP_Text>();
        if (validateLabel != null)
        {
            validateLabel.text = "Validar grafo del campus";
            validateLabel.fontSize = 34f;
        }
        StyleControl(validateGO, new Color(0.18f, 0.34f, 0.44f, 1f));
        PlaceTop(validateGO.GetComponent<RectTransform>(), cursor, ValidateHeight);
        cursor += ValidateHeight;

        // Estado del panel 2D, anclado abajo para no empujar el contenido.
        TMP_Text status2D = CreateLabel("Status2D", panel2D.transform, string.Empty, 26f, TextAlignmentOptions.Center);
        RectTransform status2DRT = status2D.rectTransform;
        status2DRT.anchorMin = new Vector2(0f, 0f);
        status2DRT.anchorMax = new Vector2(1f, 0f);
        status2DRT.pivot = new Vector2(0.5f, 0f);
        status2DRT.sizeDelta = new Vector2(-80f, 120f);
        status2DRT.anchoredPosition = new Vector2(0f, 34f);
        status2D.color = new Color(0.72f, 0.82f, 0.92f);

        // ---------------- Panel AR (HUD) ----------------
        GameObject hud = new GameObject("PanelARHUD", typeof(RectTransform));
        hud.transform.SetParent(canvasGO.transform, false);
        Image hudImage = hud.AddComponent<Image>();
        hudImage.color = new Color(0f, 0f, 0f, 0.05f);
        hudImage.raycastTarget = false;
        Stretch(hud.GetComponent<RectTransform>());

        // ---------------- Banner superior estilo Apple Maps ----------------
        GameObject bannerGO = new GameObject("NavigationBanner", typeof(RectTransform));
        bannerGO.transform.SetParent(hud.transform, false);

        Image bannerBackground = bannerGO.AddComponent<Image>();
        bannerBackground.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        bannerBackground.type = Image.Type.Sliced;
        bannerBackground.pixelsPerUnitMultiplier = 2.5f;
        bannerBackground.color = new Color(0.04f, 0.06f, 0.08f, 0.88f);
        bannerBackground.raycastTarget = false;

        RectTransform bannerRT = bannerGO.GetComponent<RectTransform>();
        bannerRT.anchorMin = new Vector2(0f, 1f);
        bannerRT.anchorMax = new Vector2(1f, 1f);
        bannerRT.pivot = new Vector2(0.5f, 1f);
        bannerRT.sizeDelta = new Vector2(-64f, 296f);
        bannerRT.anchoredPosition = new Vector2(0f, -32f);

        GameObject iconGO = new GameObject("IconoManiobra", typeof(RectTransform));
        iconGO.transform.SetParent(bannerGO.transform, false);
        RawImage maneuverIcon = iconGO.AddComponent<RawImage>();
        maneuverIcon.texture = NavigationTextureGenerator.Load("turn_up.png");
        maneuverIcon.color = Color.white;
        maneuverIcon.raycastTarget = false;

        RectTransform iconRT = iconGO.GetComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0f, 0.5f);
        iconRT.anchorMax = new Vector2(0f, 0.5f);
        iconRT.pivot = new Vector2(0f, 0.5f);
        iconRT.sizeDelta = new Vector2(150f, 150f);
        iconRT.anchoredPosition = new Vector2(40f, 0f);

        TMP_Text bannerTitle = CreateLabel("BannerTitulo", bannerGO.transform, "Navegando", 30f,
            TextAlignmentOptions.TopLeft);
        bannerTitle.color = new Color(0.72f, 0.82f, 0.92f);

        RectTransform titleRT = bannerTitle.rectTransform;
        titleRT.anchorMin = new Vector2(0f, 1f);
        titleRT.anchorMax = new Vector2(1f, 1f);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.offsetMin = new Vector2(212f, -86f);
        titleRT.offsetMax = new Vector2(-32f, -18f);

        TMP_Text bannerInstruction = CreateLabel("BannerInstruccion", bannerGO.transform, "En ruta", 46f,
            TextAlignmentOptions.TopLeft);
        bannerInstruction.fontStyle = FontStyles.Bold;
        bannerInstruction.color = Color.white;
        bannerInstruction.textWrappingMode = TextWrappingModes.Normal;

        RectTransform instructionRT = bannerInstruction.rectTransform;
        instructionRT.anchorMin = new Vector2(0f, 1f);
        instructionRT.anchorMax = new Vector2(1f, 1f);
        instructionRT.pivot = new Vector2(0.5f, 1f);
        instructionRT.offsetMin = new Vector2(212f, -272f);
        instructionRT.offsetMax = new Vector2(-32f, -96f);

        NavigationBanner banner = bannerGO.AddComponent<NavigationBanner>();
        banner.navigationManager = navigation;
        banner.titleText = bannerTitle;
        banner.instructionText = bannerInstruction;
        banner.iconImage = maneuverIcon;
        banner.iconStraight = NavigationTextureGenerator.Load("turn_up.png");
        banner.iconLeft = NavigationTextureGenerator.Load("turn_left.png");
        banner.iconRight = NavigationTextureGenerator.Load("turn_right.png");
        banner.iconUp = NavigationTextureGenerator.Load("turn_up.png");
        banner.iconDown = NavigationTextureGenerator.Load("turn_down.png");
        banner.iconArrive = NavigationTextureGenerator.Load("turn_arrive.png");

        // El HUD de estado escribe sobre la instrucción del banner.
        TMP_Text hudStatus = bannerInstruction;

        GameObject stopGO = TMP_DefaultControls.CreateButton(resources);
        stopGO.name = "BtnStopNavigation";
        stopGO.transform.SetParent(hud.transform, false);
        Button stopButton = stopGO.GetComponent<Button>();
        TMP_Text stopLabel = stopGO.GetComponentInChildren<TMP_Text>();
        if (stopLabel != null)
        {
            stopLabel.text = "Detener Ruta";
            stopLabel.fontSize = 34f;
        }
        StyleControl(stopGO, new Color(0.70f, 0.20f, 0.20f, 1f));

        RectTransform stopRT = stopGO.GetComponent<RectTransform>();
        stopRT.anchorMin = new Vector2(0.5f, 0f);
        stopRT.anchorMax = new Vector2(0.5f, 0f);
        stopRT.pivot = new Vector2(0.5f, 0f);
        stopRT.sizeDelta = new Vector2(600f, 110f);
        stopRT.anchoredPosition = new Vector2(0f, 180f);

        // HUD de depuración para las pruebas de campo.
        TMP_Text debugText = CreateLabel("HudDebug", hud.transform, string.Empty, 24f, TextAlignmentOptions.TopLeft);
        PlaceTop(debugText.rectTransform, 352f, 220f);
        debugText.color = new Color(0.70f, 0.82f, 0.94f);

        NavigationDebugHUD debugHud = hud.AddComponent<NavigationDebugHUD>();
        debugHud.navigationManager = navigation;
        debugHud.graphManager = graph;
        debugHud.debugText = debugText;

        // Botones de calibración del rumbo del mapa.
        Button rotateLeft = CreateHudButton(resources, hud.transform, "Girar -15°", -270f,
            new Color(0.18f, 0.28f, 0.38f, 1f));
        Button alignMap = CreateHudButton(resources, hud.transform, "Alinear", 0f,
            new Color(0.18f, 0.40f, 0.48f, 1f));
        Button rotateRight = CreateHudButton(resources, hud.transform, "Girar +15°", 270f,
            new Color(0.18f, 0.28f, 0.38f, 1f));

        hud.SetActive(false);

        // ---------------- Controlador de UI ----------------
        CampusUIController uiController = canvasGO.AddComponent<CampusUIController>();
        uiController.panel2DMap = panel2D;
        uiController.panelARHUD = hud;
        uiController.originDropdown = originDropdown;
        uiController.destinationDropdown = destinationDropdown;
        uiController.searchInputField = searchInput;
        uiController.btnStartNavigation = startButton;
        uiController.btnStopNavigation = stopButton;
        uiController.btnValidateGraph = validateButton;
        uiController.navigationManager = navigation;
        uiController.graphManager = graph;
        uiController.statusLabel = hudStatus;
        uiController.status2DLabel = status2D;

        // Listeners persistentes (quedan guardados en la escena).
        UnityEventTools.AddPersistentListener(startButton.onClick, new UnityAction(uiController.OnClickStartNavigation));
        UnityEventTools.AddPersistentListener(stopButton.onClick, new UnityAction(uiController.OnClickStopNavigation));
        UnityEventTools.AddPersistentListener(searchInput.onValueChanged, new UnityAction<string>(uiController.OnSearchValueChanged));
        UnityEventTools.AddPersistentListener(validateButton.onClick, new UnityAction(uiController.OnClickValidateGraph));
        UnityEventTools.AddPersistentListener(rotateLeft.onClick, new UnityAction(uiController.OnClickRotateLeft));
        UnityEventTools.AddPersistentListener(alignMap.onClick, new UnityAction(uiController.OnClickAlignMap));
        UnityEventTools.AddPersistentListener(rotateRight.onClick, new UnityAction(uiController.OnClickRotateRight));

        EditorUtility.SetDirty(uiController);
        EditorUtility.SetDirty(canvasGO);
    }

    private static TMP_DefaultControls.Resources GetStandardResources()
    {
        TMP_DefaultControls.Resources resources = new TMP_DefaultControls.Resources();
        resources.standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        resources.background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        resources.inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd");
        resources.knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        resources.checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd");
        resources.dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd");
        resources.mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd");
        return resources;
    }

    private static void StyleControl(GameObject control, Color background)
    {
        Image image = control.GetComponent<Image>();
        if (image != null)
        {
            image.color = background;
            image.type = Image.Type.Sliced;
        }

        TMP_Text[] texts = control.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            texts[i].color = Color.white;
            if (texts[i].fontSize < 24f) texts[i].fontSize = 30f;
        }
    }

    /// <summary>
    /// El template de TMP trae fondo de item casi blanco (245,245,245). Si el texto
    /// tambien queda blanco, la lista se ve "en blanco". Aqui se oscurece el panel
    /// y se dejan los textos y el checkmark con contraste suficiente.
    /// </summary>
    private static void StyleDropdownTemplate(TMP_Dropdown dropdown)
    {
        if (dropdown == null || dropdown.template == null) return;

        Color panelColor = new Color(0.08f, 0.10f, 0.14f, 1f);
        Color itemColor = new Color(0.13f, 0.17f, 0.23f, 1f);
        Color accent = new Color(0.25f, 0.85f, 1f, 1f);

        RectTransform template = dropdown.template;

        Image templateImage = template.GetComponent<Image>();
        if (templateImage != null) templateImage.color = panelColor;

        Transform viewport = template.Find("Viewport");
        Transform content = viewport != null ? viewport.Find("Content") : null;
        Transform item = content != null ? content.Find("Item") : null;

        if (item != null)
        {
            Transform itemBackground = item.Find("Item Background");
            Image itemBackgroundImage = itemBackground != null ? itemBackground.GetComponent<Image>() : null;
            if (itemBackgroundImage != null) itemBackgroundImage.color = itemColor;

            Transform itemCheckmark = item.Find("Item Checkmark");
            Image itemCheckmarkImage = itemCheckmark != null ? itemCheckmark.GetComponent<Image>() : null;
            if (itemCheckmarkImage != null) itemCheckmarkImage.color = accent;

            Transform itemLabel = item.Find("Item Label");
            TMP_Text itemLabelText = itemLabel != null ? itemLabel.GetComponent<TMP_Text>() : null;
            if (itemLabelText != null)
            {
                itemLabelText.color = Color.white;
                if (itemLabelText.fontSize < 28f) itemLabelText.fontSize = 30f;
            }

            Toggle toggle = item.GetComponent<Toggle>();
            if (toggle != null)
            {
                ColorBlock colors = toggle.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(0.75f, 0.90f, 1f, 1f);
                colors.pressedColor = new Color(0.55f, 0.75f, 0.95f, 1f);
                colors.selectedColor = new Color(0.75f, 0.90f, 1f, 1f);
                colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                colors.colorMultiplier = 1f;
                toggle.colors = colors;
            }
        }

        Transform scrollbar = template.Find("Scrollbar");
        if (scrollbar != null)
        {
            Image scrollbarImage = scrollbar.GetComponent<Image>();
            if (scrollbarImage != null) scrollbarImage.color = new Color(0.10f, 0.12f, 0.16f, 1f);

            Transform slidingArea = scrollbar.Find("Sliding Area");
            Transform handle = slidingArea != null ? slidingArea.Find("Handle") : null;
            Image handleImage = handle != null ? handle.GetComponent<Image>() : null;
            if (handleImage != null) handleImage.color = new Color(0.45f, 0.75f, 0.95f, 1f);
        }

        if (dropdown.captionText != null) dropdown.captionText.color = Color.white;
        if (dropdown.itemText != null) dropdown.itemText.color = Color.white;
    }

    /// <summary>
    /// Agranda el desplegable y su lista para que sea comodo de tocar con el dedo.
    /// </summary>
    private static void EnlargeDropdown(TMP_Dropdown dropdown, float height, float fontSize)
    {
        if (dropdown == null) return;

        if (dropdown.captionText != null) dropdown.captionText.fontSize = fontSize;
        if (dropdown.itemText != null) dropdown.itemText.fontSize = fontSize - 4f;

        RectTransform rootRT = dropdown.GetComponent<RectTransform>();
        if (rootRT != null) rootRT.sizeDelta = new Vector2(rootRT.sizeDelta.x, height);

        RectTransform template = dropdown.template;
        if (template == null) return;

        // Lista mas alta: unos 5 elementos visibles, el resto se desplaza.
        template.sizeDelta = new Vector2(template.sizeDelta.x, 380f);

        Transform viewport = template.Find("Viewport");
        Transform content = viewport != null ? viewport.Find("Content") : null;
        Transform item = content != null ? content.Find("Item") : null;

        if (item != null)
        {
            RectTransform itemRT = item.GetComponent<RectTransform>();
            if (itemRT != null) itemRT.sizeDelta = new Vector2(itemRT.sizeDelta.x, 76f);
        }

        Transform arrow = dropdown.transform.Find("Arrow");
        RectTransform arrowRT = arrow as RectTransform;
        if (arrowRT != null) arrowRT.sizeDelta = new Vector2(30f, 30f);
    }

    // =====================================================================
    // AR: fondo de camara (URP)
    // =====================================================================
    /// <summary>
    /// AR Foundation 6 con URP exige ARBackgroundRendererFeature en el Universal Renderer
    /// para dibujar el feed de la camara. Sin el, la pantalla AR queda sin imagen.
    /// </summary>
    private static void EnsureARBackgroundRendererFeature()
    {
        UniversalRendererData rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(UrpRendererPath);

        if (rendererData == null)
        {
            Debug.LogWarning("[CampusGraphAutoBuilder] No se encontro el renderer data: " + UrpRendererPath);
            return;
        }

        if (rendererData.rendererFeatures == null)
        {
            Debug.LogWarning("[CampusGraphAutoBuilder] El renderer data no expone rendererFeatures.");
            return;
        }

        for (int i = 0; i < rendererData.rendererFeatures.Count; i++)
        {
            if (rendererData.rendererFeatures[i] is ARBackgroundRendererFeature) return;
        }

        ARBackgroundRendererFeature feature = null;
        UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(UrpRendererPath);

        for (int i = 0; i < subAssets.Length; i++)
        {
            feature = subAssets[i] as ARBackgroundRendererFeature;
            if (feature != null) break;
        }

        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<ARBackgroundRendererFeature>();
            feature.name = "ARBackgroundRendererFeature";
            AssetDatabase.AddObjectToAsset(feature, rendererData);
        }

        rendererData.rendererFeatures.Add(feature);

        // Se replica lo que hace el inspector: lista + mapa de fileIDs.
        SerializedObject serialized = new SerializedObject(rendererData);
        SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
        SerializedProperty map = serialized.FindProperty("m_RendererFeatureMap");

        if (features != null)
        {
            features.arraySize = rendererData.rendererFeatures.Count;
            for (int i = 0; i < rendererData.rendererFeatures.Count; i++)
            {
                features.GetArrayElementAtIndex(i).objectReferenceValue = rendererData.rendererFeatures[i];
            }
        }

        if (map != null)
        {
            map.arraySize = rendererData.rendererFeatures.Count;
            for (int i = 0; i < rendererData.rendererFeatures.Count; i++)
            {
                string guid;
                long localId;
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(rendererData.rendererFeatures[i], out guid, out localId))
                {
                    map.GetArrayElementAtIndex(i).longValue = localId;
                }
            }
        }

        if (features != null || map != null) serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(rendererData);
        AssetDatabase.SaveAssets();

        Debug.Log("[CampusGraphAutoBuilder] ARBackgroundRendererFeature agregado al Universal Renderer.");
    }

    private static TMP_Text CreateLabel(string name, Transform parent, string text, float fontSize, TextAlignmentOptions alignment)
    {
        GameObject labelGO = new GameObject(name, typeof(RectTransform));
        labelGO.transform.SetParent(parent, false);

        TMP_Text label = labelGO.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = Color.white;
        label.raycastTarget = false;

        if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;
        return label;
    }

    /// <summary>Botón pequeño del HUD, anclado a la fila inferior.</summary>
    private static Button CreateHudButton(TMP_DefaultControls.Resources resources, Transform parent,
        string text, float x, Color color)
    {
        GameObject buttonGO = TMP_DefaultControls.CreateButton(resources);
        buttonGO.name = "Btn" + text.Replace(" ", string.Empty).Replace("°", string.Empty).Replace("+", "Plus").Replace("-", "Minus");
        buttonGO.transform.SetParent(parent, false);

        Button button = buttonGO.GetComponent<Button>();
        TMP_Text label = buttonGO.GetComponentInChildren<TMP_Text>();
        if (label != null)
        {
            label.text = text;
            label.fontSize = 26f;
        }

        StyleControl(buttonGO, color);

        RectTransform rect = buttonGO.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(250f, 80f);
        rect.anchoredPosition = new Vector2(x, 60f);

        return button;
    }

    private static void Stretch(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.localScale = Vector3.one;
    }

    private static void PlaceTop(RectTransform rectTransform, float topOffset, float height, float margin = 40f)
    {
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(1f, 1f);
        rectTransform.pivot = new Vector2(0.5f, 1f);
        rectTransform.sizeDelta = new Vector2(-margin * 2f, height);
        rectTransform.anchoredPosition = new Vector2(0f, -topOffset);
    }

    // =====================================================================
    // Rig de Realidad Aumentada
    // =====================================================================
    private static void BuildARRig(NavigationManager navigation)
    {
        GameObject sessionGO = new GameObject("AR Session");
        sessionGO.AddComponent<ARSession>();

        GameObject originGO = CreateXROriginFromARFoundation();
        Camera camera;

        if (originGO != null)
        {
            camera = originGO.GetComponentInChildren<Camera>(true);
        }
        else
        {
            // Respaldo: se arma el rig a mano con la jerarquía oficial XR Origin → Camera Offset → Camera.
            originGO = new GameObject("XR Origin");
            XROrigin rig = originGO.AddComponent<XROrigin>();

            GameObject offsetGO = new GameObject("Camera Offset");
            offsetGO.transform.SetParent(originGO.transform, false);

            GameObject cameraGO = new GameObject("AR Camera");
            cameraGO.transform.SetParent(offsetGO.transform, false);
            camera = cameraGO.AddComponent<Camera>();

            rig.CameraFloorOffsetObject = offsetGO;
            rig.Camera = camera;
        }

        if (camera == null)
        {
            Debug.LogError("[CampusGraphAutoBuilder] No se pudo crear la camara AR.");
            return;
        }

        camera.gameObject.name = "AR Camera";
        camera.gameObject.tag = "MainCamera";
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 400f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.05f, 0.07f, 0.10f);

        if (camera.GetComponent<AudioListener>() == null) camera.gameObject.AddComponent<AudioListener>();
        if (camera.GetComponent<ARCameraManager>() == null) camera.gameObject.AddComponent<ARCameraManager>();
        if (camera.GetComponent<ARCameraBackground>() == null) camera.gameObject.AddComponent<ARCameraBackground>();
        if (camera.GetComponent<ARInputManager>() == null) camera.gameObject.AddComponent<ARInputManager>();

        if (camera.transform.localPosition == Vector3.zero)
        {
            camera.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        }

        XROrigin xrOrigin = originGO.GetComponent<XROrigin>();
        if (xrOrigin != null)
        {
            xrOrigin.Camera = camera;
            xrOrigin.Origin = originGO;
        }

        // La flecha AR sigue a la cámara.
        if (navigation != null && navigation.arrowGuide != null)
        {
            navigation.arrowGuide.targetCamera = camera.transform;
        }

        ConfigureARCampusPlacement(originGO, camera, navigation);
    }

    /// <summary>
    /// Agrega deteccion de planos, raycast y el componente que ancla el mapa al suelo real.
    /// </summary>
    private static void ConfigureARCampusPlacement(GameObject originGO, Camera camera, NavigationManager navigation)
    {
        if (originGO == null || camera == null) return;

        ARPlaneManager planeManager = originGO.GetComponent<ARPlaneManager>();
        if (planeManager == null) planeManager = originGO.AddComponent<ARPlaneManager>();

        ARRaycastManager raycastManager = originGO.GetComponent<ARRaycastManager>();
        if (raycastManager == null) raycastManager = originGO.AddComponent<ARRaycastManager>();

        ARCampusPlacement placement = originGO.GetComponent<ARCampusPlacement>();
        if (placement == null) placement = originGO.AddComponent<ARCampusPlacement>();

        placement.raycastManager = raycastManager;
        placement.planeManager = planeManager;
        placement.arCamera = camera;
        placement.graphManager = navigation != null ? navigation.graphManager : null;
        placement.campusRoot = navigation != null && navigation.graphManager != null
            ? navigation.graphManager.transform
            : null;

        if (navigation != null) navigation.campusPlacement = placement;

        CampusUIController ui = UnityEngine.Object.FindAnyObjectByType<CampusUIController>();
        if (ui != null) ui.campusPlacement = placement;

        EditorUtility.SetDirty(placement);
        if (navigation != null) EditorUtility.SetDirty(navigation);
        if (ui != null) EditorUtility.SetDirty(ui);
    }

    /// <summary>
    /// Usa la utilidad oficial de AR Foundation (menú GameObject/XR/XR Origin (Mobile AR))
    /// para crear un rig ya cableado, incluido el TrackedPoseDriver de la cámara.
    /// </summary>
    private static GameObject CreateXROriginFromARFoundation()
    {
        try
        {
            Type utilType = Type.GetType("UnityEditor.XR.ARFoundation.XROriginCreateUtil, Unity.XR.ARFoundation.Editor");
            if (utilType == null) return null;

            MethodInfo create = utilType.GetMethod("CreateXROriginWithParent",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (create == null) return null;

            XROrigin xrOrigin = create.Invoke(null, new object[] { null }) as XROrigin;
            return xrOrigin != null ? xrOrigin.gameObject : null;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[CampusGraphAutoBuilder] No se pudo usar XROriginCreateUtil: " + exception.Message);
            return null;
        }
    }

    /// <summary>Registra ARCore como proveedor de XR para Android (XR Plug-in Management).</summary>
    private static void ConfigureXRPlugins()
    {
        try
        {
            MethodInfo getOrCreate = typeof(XRGeneralSettingsPerBuildTarget).GetMethod(
                "GetOrCreate", BindingFlags.NonPublic | BindingFlags.Static);

            XRGeneralSettingsPerBuildTarget container = getOrCreate != null
                ? getOrCreate.Invoke(null, null) as XRGeneralSettingsPerBuildTarget
                : null;

            if (container == null)
            {
                Debug.LogWarning("[CampusGraphAutoBuilder] No se pudo obtener la configuracion de XR Plug-in Management.");
            }
            else
            {
                BuildTargetGroup target = BuildTargetGroup.Android;

                if (!container.HasSettingsForBuildTarget(target))
                {
                    container.CreateDefaultSettingsForBuildTarget(target);
                }

                if (!container.HasManagerSettingsForBuildTarget(target))
                {
                    container.CreateDefaultManagerSettingsForBuildTarget(target);
                }

                XRManagerSettings manager = container.ManagerSettingsForBuildTarget(target);
                if (manager == null)
                {
                    Debug.LogWarning("[CampusGraphAutoBuilder] No se pudo crear XRManagerSettings para Android.");
                }
                else if (XRPackageMetadataStore.AssignLoader(manager, typeof(ARCoreLoader).FullName, target))
                {
                    Debug.Log("[CampusGraphAutoBuilder] ARCore registrado en XR Plug-in Management (Android).");
                }
                else
                {
                    Debug.LogWarning("[CampusGraphAutoBuilder] No se pudo registrar el ARCoreLoader.");
                }

                EditorUtility.SetDirty(container);
                AssetDatabase.SaveAssets();
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[CampusGraphAutoBuilder] Error configurando XR Plug-in Management: " + exception.Message);
        }

        // ARCore exige ARM64 en Android.
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
    }

    private static void BuildLighting()
    {
        GameObject lightGO = new GameObject("Directional Light");
        Light light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.shadows = LightShadows.Soft;
        lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    // =====================================================================
    // Utilidades
    // =====================================================================
    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name))
        {
            AssetDatabase.CreateFolder(parent, name);
        }
    }

    private static void AddSceneToBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();

        for (int i = 0; i < scenes.Count; i++)
        {
            if (scenes[i].path == ScenePath)
            {
                scenes[i] = new EditorBuildSettingsScene(ScenePath, true);
                EditorBuildSettings.scenes = scenes.ToArray();
                return;
            }
        }

        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    /// <summary>Configura el Universal Render Pipeline (asset + renderer data).</summary>
    private static void SetupUniversalRenderPipeline()
    {
        UniversalRendererData rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(UrpRendererPath);
        if (rendererData == null)
        {
            rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rendererData, UrpRendererPath);
            AssetDatabase.SaveAssets();

            // Reimporta para que URP complete los recursos (shaders/materiales por defecto) del renderer.
            AssetDatabase.ImportAsset(UrpRendererPath, ImportAssetOptions.ForceUpdate);
            rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(UrpRendererPath);
        }

        UniversalRenderPipelineAsset pipelineAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpPipelinePath);
        if (pipelineAsset == null)
        {
            pipelineAsset = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(pipelineAsset, UrpPipelinePath);
            AssetDatabase.SaveAssets();
        }

        GraphicsSettings.defaultRenderPipeline = pipelineAsset;

        int currentLevel = QualitySettings.GetQualityLevel();
        string[] levels = QualitySettings.names;
        for (int i = 0; i < levels.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = pipelineAsset;
        }
        QualitySettings.SetQualityLevel(currentLevel, false);

        AssetDatabase.SaveAssets();
    }

    /// <summary>Importa los recursos esenciales de TextMeshPro si aún no están en el proyecto.</summary>
    private static void EnsureTMPEssentialResources()
    {
        TMP_Settings settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);

        if (settings == null)
        {
            TMP_PackageResourceImporter.ImportResources(true, false, false);
            AssetDatabase.Refresh();
            settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
        }

        if (settings != null && TMP_Settings.defaultFontAsset != null)
        {
            return; // Recursos esenciales de TMP ya disponibles.
        }

        if (settings == null)
        {
            Debug.LogWarning("[CampusGraphAutoBuilder] No se encontro TMP Settings en " + TmpSettingsPath);
            return;
        }

        // Plan B: generar una fuente de respaldo con la fuente integrada de Unity.
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        if (font == null)
        {
            Debug.LogWarning("[CampusGraphAutoBuilder] No se encontro una fuente para el respaldo de TMP.");
            return;
        }

        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(font);
        if (fontAsset == null)
        {
            Debug.LogWarning("[CampusGraphAutoBuilder] TMP_FontAsset.CreateFontAsset devolvio null.");
            return;
        }

        fontAsset.name = "FuenteAR_Fallback";
        AssetDatabase.CreateAsset(fontAsset, "Assets/Settings/FuenteAR_Fallback.asset");

        TMP_Settings.defaultFontAsset = fontAsset;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
    }

    // =====================================================================
    // Compilación para Android (ARCore)
    // =====================================================================
    private const string ApkPath = "Builds/SONEA.apk";
    private const string AndroidPackageName = "com.ETITC.SONEA";

    /// <summary>Regenera la escena completa y compila el APK en una sola pasada.</summary>
    [MenuItem("ETITC Tools/Regenerar Escena y Compilar APK", false, 31)]
    public static void RegenerateAndBuildAndroid()
    {
        GenerateCampusNodes();
        BuildAndroid();
    }

    /// <summary>Regenera la escena y valida el grafo en una sola pasada (batch / menú).</summary>
    [MenuItem("ETITC Tools/Regenerar Escena y Verificar Grafo", false, 32)]
    public static void RegenerateAndVerify()
    {
        GenerateCampusNodes();
        VerifyCampusGraph();
    }

    /// <summary>Configura el player de Android y genera el APK instalable con ARCore.</summary>
    [MenuItem("ETITC Tools/Compilar APK para Android", false, 30)]
    public static void BuildAndroid()
    {
        ConfigureAndroidPlayerSettings();
        EnsureARBackgroundRendererFeature();

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            Debug.Log("[CampusGraphAutoBuilder] Cambiando a la plataforma Android...");
            bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Android, BuildTarget.Android);

            if (!switched)
            {
                throw new Exception("No se pudo cambiar a la plataforma Android. " +
                                    "Instala el modulo Android Build Support desde Unity Hub.");
            }
        }

        if (!File.Exists(ScenePath))
        {
            Debug.Log("[CampusGraphAutoBuilder] La escena no existe; generandola antes de compilar...");
            GenerateCampusNodes();
        }

        Directory.CreateDirectory("Builds");

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = ApkPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None
        };

        Debug.Log("[CampusGraphAutoBuilder] Compilando APK Android (IL2CPP/ARM64)...");
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result != BuildResult.Succeeded)
        {
            Debug.LogError("[CampusGraphAutoBuilder] BUILD FALLIDO: " + summary.result +
                           " (" + summary.totalErrors + " errores, " + summary.totalWarnings + " avisos)");
            throw new Exception("La compilacion de Android fallo: " + summary.result);
        }

        Debug.Log("[CampusGraphAutoBuilder] APK generado: " + Path.GetFullPath(ApkPath) +
                  " (" + (summary.totalSize / (1024f * 1024f)).ToString("F1") + " MB)");
    }

    private static void ConfigureAndroidPlayerSettings()
    {
        PlayerSettings.companyName = "ETITC";
        PlayerSettings.productName = "SONEA";
        PlayerSettings.bundleVersion = "1.0";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AndroidPackageName);
        PlayerSettings.Android.bundleVersionCode = 1;

        // ARCore exige ARM64 + IL2CPP.
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

        // El stripping agresivo puede romper la reflexion de ARFoundation/URP.
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);

        // ARCore + Vulkan exige minSdk 29; con OpenGLES3 funciona desde API 26
        // y es mas estable en GPUs Mali (TECNO).
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });

        EditorUserBuildSettings.buildAppBundle = false;
        PlayerSettings.Android.useCustomKeystore = false;

        AssetDatabase.SaveAssets();
        Debug.Log("[CampusGraphAutoBuilder] PlayerSettings Android configurados: " +
                  AndroidPackageName + " / ARM64 / IL2CPP / minSdk 26");
    }

    // =====================================================================
    // Verificación
    // =====================================================================
    /// <summary>Abre la escena generada y valida la integridad del grafo de todo el campus.</summary>
    [MenuItem("ETITC Tools/Verificar Grafo del Campus", false, 20)]
    public static void VerifyCampusGraph()
    {
        if (File.Exists(ScenePath))
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        List<string> errores = new List<string>();

        CampusGraphManager graph = UnityEngine.Object.FindAnyObjectByType<CampusGraphManager>();
        if (graph == null)
        {
            Debug.LogError("[CampusGraphAutoBuilder] No se encontro CampusGraphManager en la escena.");
            return;
        }

        Node[] nodes = UnityEngine.Object.FindObjectsByType<Node>(FindObjectsInactive.Exclude);
        if (nodes.Length == 0) errores.Add("No hay nodos en la escena.");

        string report;
        if (!graph.ValidateGraph(out report))
        {
            errores.Add("ValidateGraph reporto problemas.");
        }
        Debug.Log("[CampusGraphAutoBuilder] Validacion del grafo:\n" + report);

        // Conexiones verticales: cada edificio debe tener su escalera en todos los pisos.
        string[] edificios = { "nuevo", "modulares" };
        for (int b = 0; b < edificios.Length; b++)
        {
            string id = edificios[b];
            List<Node> escaleras = new List<Node>();
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i] != null && nodes[i].buildingId == id && nodes[i].nodeType == "escalera")
                {
                    escaleras.Add(nodes[i]);
                }
            }

            escaleras.Sort((a, c) => a.floor.CompareTo(c.floor));
            if (escaleras.Count < 2)
            {
                errores.Add("El edificio " + id + " no tiene al menos dos escaleras conectadas.");
            }
            else
            {
                for (int i = 1; i < escaleras.Count; i++)
                {
                    if (!escaleras[i - 1].HasEdgeTo(escaleras[i]) || !escaleras[i].HasEdgeTo(escaleras[i - 1]))
                    {
                        errores.Add("Falta la conexion vertical " + escaleras[i - 1].DisplayName + " <-> " + escaleras[i].DisplayName);
                    }
                }
            }
        }

        // UI cableada.
        CampusUIController ui = UnityEngine.Object.FindAnyObjectByType<CampusUIController>();
        if (ui == null)
        {
            errores.Add("No se encontro el CampusUIController en la escena.");
        }
        else
        {
            if (ui.originDropdown == null) errores.Add("El Dropdown de origen no esta asignado.");
            if (ui.destinationDropdown == null) errores.Add("El Dropdown de destino no esta asignado.");
            if (ui.searchInputField == null) errores.Add("El campo de busqueda no esta asignado.");
            if (ui.btnStartNavigation == null) errores.Add("El boton de inicio no esta asignado.");
            if (ui.btnValidateGraph == null) errores.Add("El boton de validacion no esta asignado.");
            if (ui.status2DLabel == null) errores.Add("El estado del panel 2D no esta asignado.");
            if (ui.navigationManager == null) errores.Add("El NavigationManager no esta asignado.");
        }

        NavigationManager navigation = UnityEngine.Object.FindAnyObjectByType<NavigationManager>();
        if (navigation == null) errores.Add("No se encontro el NavigationManager en la escena.");

        if (navigation != null && navigation.pathfinder != null)
        {
            DijkstraPathfinder pathfinder = navigation.pathfinder;
            List<Node> todos = new List<Node>(nodes);

            CheckRoute(graph, pathfinder, todos, "EN101", "BIBLIOTECA", true, errores);
            CheckRoute(graph, pathfinder, todos, "EN101", "EN105", false, errores);
            CheckRoute(graph, pathfinder, todos, "M103", "M108", false, errores);
            CheckRoute(graph, pathfinder, todos, "M103", "M219", true, errores);
            CheckRoute(graph, pathfinder, todos, "EN101", "M219", true, errores);
            CheckRoute(graph, pathfinder, todos, "ext_nuevo_principal", "ext_carros", false, errores);
            CheckRoute(graph, pathfinder, todos, "ext_nuevo_principal", "ext_cancha", false, errores);
            CheckRoute(graph, pathfinder, todos, "ext_nuevo_principal", "ext_modulares_secundaria", false, errores);
            CheckRoute(graph, pathfinder, todos, "ext_entrada_modulares", "ext_cafeteria", false, errores);
            CheckRoute(graph, pathfinder, todos, "nuevo_P1_entrada_principal", "nuevo_P1_entrada_secundaria", false, errores);
            CheckRoute(graph, pathfinder, todos, "modulares_P1_entrada_cafeteria", "modulares_P1_entrada_este", false, errores);
        }

        Debug.Log("[CampusGraphAutoBuilder] Nodos en la escena: " + nodes.Length);

        if (errores.Count == 0)
        {
            Debug.Log("[CampusGraphAutoBuilder] VERIFICACION EXITOSA: grafo, escaleras, UI y rutas de campus correctos.");
        }
        else
        {
            Debug.LogError("[CampusGraphAutoBuilder] VERIFICACION CON " + errores.Count + " PROBLEMAS:\n" +
                           string.Join("\n", errores.ToArray()));
        }
    }

    private static void CheckRoute(CampusGraphManager graph, DijkstraPathfinder pathfinder, List<Node> allNodes,
        string fromId, string toId, bool expectFloorChange, List<string> errores)
    {
        Node from = graph.GetNodeById(fromId);
        Node to = graph.GetNodeById(toId);

        if (from == null || to == null)
        {
            errores.Add("No existe el nodo " + (from == null ? fromId : toId) + " en el grafo.");
            return;
        }

        List<Node> route = pathfinder.FindShortestPath(from, to, allNodes);
        if (route == null || route.Count == 0)
        {
            errores.Add("Dijkstra no encontro ruta de " + fromId + " a " + toId + ".");
            return;
        }

        bool usesVertical = route.Exists(n => n != null && (n.nodeType == "escalera" || n.nodeType == "ascensor"));
        if (expectFloorChange && !usesVertical)
        {
            errores.Add("La ruta " + fromId + " -> " + toId + " deberia cambiar de piso.");
        }
        if (!expectFloorChange && usesVertical && from.buildingId == to.buildingId)
        {
            errores.Add("La ruta local " + fromId + " -> " + toId + " usa escaleras/ascensor (no deberia).");
        }

        Debug.Log("[CampusGraphAutoBuilder] Ruta " + fromId + " -> " + toId + ": " +
                  route.Count + " nodos, " + pathfinder.GetPathLength(route).ToString("F1") + " m\n  " +
                  string.Join(" -> ", route.ConvertAll(n => n.DisplayName).ToArray()));

        float total = pathfinder.GetPathLength(route);
        if (total > 100f)
        {
            for (int i = 1; i < route.Count; i++)
            {
                Node a = route[i - 1];
                Node b = route[i];
                Debug.Log("    tramo " + i + ": " + a.DisplayName + " -> " + b.DisplayName +
                          " = " + a.GetEdgeWeight(b).ToString("F2") + " m | " +
                          a.transform.position.ToString("F1") + " -> " + b.transform.position.ToString("F1"));
            }
        }
    }
}
