using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Ancla el mapa del campus al entorno real usando los planos detectados por ARCore.
/// El nodo de origen se coloca a los pies del usuario y la ruta se orienta hacia
/// donde apunta la cámara (o hacia el punto del suelo que el usuario toque).
/// </summary>
public class ARCampusPlacement : MonoBehaviour
{
    [Header("Referencias AR")]
    public ARRaycastManager raycastManager;
    public ARPlaneManager planeManager;
    public Camera arCamera;

    [Header("Mapa del campus")]
    [Tooltip("Raíz que contiene todos los nodos. Se mueve y rota para anclarse al mundo real.")]
    public Transform campusRoot;
    public CampusGraphManager graphManager;

    [Header("Interacción")]
    [Tooltip("Activo solo mientras la navegación AR está en curso.")]
    public bool placementEnabled = false;

    [Tooltip("Diámetro de la retícula de colocación, en metros.")]
    public float reticleDiameter = 0.28f;

    [Tooltip("Separación de la retícula respecto al plano detectado, en metros.")]
    public float surfaceOffset = 0.01f;

    [Tooltip("Oculta las placas de piso del campus: en AR el suelo real es la referencia.")]
    public bool hideCampusFloorPlates = true;

    [Tooltip("Altura de los ojos del usuario, en metros (cámara - altura = suelo).")]
    public float eyeHeight = 1.6f;

    [Tooltip("Giro adicional del mapa para calibrar el norte real en sitio (grados).")]
    public float headingOffsetDegrees = 0f;

    [Tooltip("Usa la brújula del teléfono para orientar el mapa al norte real.")]
    public bool useCompass = true;

    /// <summary>True cuando ya existe una colocación válida del mapa.</summary>
    public bool HasPlacement { get; private set; }

    /// <summary>Posición (suelo) y orientación del mapa en el mundo real.</summary>
    public Pose PlacementPose { get; private set; }

    /// <summary>Altura real del suelo del piso colocado.</summary>
    public float FloorWorldY
    {
        get { return PlacementPose.position.y; }
    }

    /// <summary>Se dispara cada vez que el usuario recoloca el mapa.</summary>
    public event System.Action PlacementChanged;

    private GameObject reticle;
    private bool validHit;
    private Pose hitPose;
    private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();

    private static readonly Color AccentColor = new Color(0.25f, 0.85f, 1f, 1f);

    void Awake()
    {
        if (arCamera == null) arCamera = Camera.main;

        if (graphManager == null) graphManager = FindAnyObjectByType<CampusGraphManager>();
        if (campusRoot == null && graphManager != null) campusRoot = graphManager.transform;

        if (raycastManager == null) raycastManager = GetComponent<ARRaycastManager>();
        if (raycastManager == null) raycastManager = FindAnyObjectByType<ARRaycastManager>();

        if (planeManager == null) planeManager = GetComponent<ARPlaneManager>();
        if (planeManager == null) planeManager = FindAnyObjectByType<ARPlaneManager>();

        if (useCompass) Input.compass.enabled = true;

        CreateReticle();

        if (hideCampusFloorPlates) HideFloorPlates();
    }

    void OnDestroy()
    {
        if (reticle != null) Destroy(reticle);
    }

    void Update()
    {
        if (!placementEnabled || raycastManager == null || arCamera == null)
        {
            validHit = false;
            if (reticle != null && reticle.activeSelf) reticle.SetActive(false);
            return;
        }

        UpdateReticle();

        if (Input.touchCount == 0) return;

        Touch touch = Input.GetTouch(0);
        if (touch.phase != TouchPhase.Began) return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.fingerId)) return;

        if (validHit) ConfirmPlacement(hitPose);
    }

    /// <summary>Activa o desactiva el modo de colocación (se usa mientras se navega).</summary>
    public void SetPlacementEnabled(bool enabled)
    {
        placementEnabled = enabled;
        if (!enabled)
        {
            validHit = false;
            if (reticle != null) reticle.SetActive(false);
        }
    }

    /// <summary>Coloca el mapa delante del usuario sin necesidad de tocar la pantalla.</summary>
    public void AutoPlaceAtCamera()
    {
        if (arCamera == null) return;

        Vector3 position = arCamera.transform.position;
        float yaw = GetHeadingYaw();

        PlacementPose = new Pose(
            new Vector3(position.x, position.y - eyeHeight, position.z),
            Quaternion.Euler(0f, yaw, 0f));
        HasPlacement = true;

        if (PlacementChanged != null) PlacementChanged.Invoke();
    }

    /// <summary>Orienta el mapa usando el rumbo actual de la cámara (útil si la brújula falla).</summary>
    public void AlignToCameraForward()
    {
        if (arCamera == null) return;

        Vector3 position = arCamera.transform.position;
        PlacementPose = new Pose(
            new Vector3(position.x, position.y - eyeHeight, position.z),
            Quaternion.Euler(0f, arCamera.transform.eulerAngles.y, 0f));
        HasPlacement = true;

        if (PlacementChanged != null) PlacementChanged.Invoke();
    }

    /// <summary>Gira el mapa alrededor del usuario para calibrar el rumbo en sitio.</summary>
    public void RotateMap(float degrees)
    {
        headingOffsetDegrees += degrees;
        if (PlacementChanged != null) PlacementChanged.Invoke();
    }

    /// <summary>Anula la colocación actual (al detener la navegación).</summary>
    public void ResetPlacement()
    {
        HasPlacement = false;
        validHit = false;
        if (reticle != null) reticle.SetActive(false);
    }

    /// <summary>
    /// Traslada y rota el mapa para que <paramref name="startNode"/> quede a los pies del
    /// usuario y el norte del grafo coincida con el rumbo real (brújula + calibración).
    /// Es una transformación rígida: el grafo conserva todas sus distancias.
    /// </summary>
    public void ApplyPlacement(Node startNode, Node nextNode)
    {
        if (!HasPlacement || campusRoot == null || startNode == null) return;

        Vector3 start = startNode.transform.position;

        float yaw = PlacementPose.rotation.eulerAngles.y + headingOffsetDegrees;
        Quaternion targetRotation = Quaternion.Euler(0f, yaw, 0f);

        // Rotación rígida alrededor del nodo de origen.
        Quaternion delta = targetRotation * Quaternion.Inverse(campusRoot.rotation);
        Vector3 offset = start - campusRoot.position;
        Vector3 rotatedOffset = delta * offset;

        Vector3 target = new Vector3(PlacementPose.position.x, PlacementPose.position.y, PlacementPose.position.z);
        campusRoot.rotation = targetRotation;
        campusRoot.position = target - rotatedOffset;
    }

    /// <summary>Rumbo del mapa: brújula si está disponible; si no, el yaw de la cámara.</summary>
    private float GetHeadingYaw()
    {
        if (useCompass)
        {
            float heading = Input.compass.trueHeading;
            if (heading < 0f || heading > 360f) heading = Input.compass.magneticHeading;
            if (heading >= 0f && heading <= 360f) return heading;
        }

        return arCamera != null ? arCamera.transform.eulerAngles.y : 0f;
    }

    // ------------------------------------------------------------------
    // Internos
    // ------------------------------------------------------------------
    private void UpdateReticle()
    {
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        validHit = raycastManager.Raycast(
            screenCenter,
            hits,
            TrackableType.PlaneWithinPolygon | TrackableType.PlaneEstimated);

        if (!validHit)
        {
            if (reticle != null) reticle.SetActive(false);
            return;
        }

        hitPose = hits[0].pose;

        if (reticle != null)
        {
            reticle.SetActive(true);

            Vector3 position = hitPose.position;
            position.y += surfaceOffset;
            reticle.transform.position = position;

            reticle.transform.rotation = Quaternion.Euler(0f, arCamera.transform.eulerAngles.y, 0f);
        }
    }

    private void ConfirmPlacement(Pose hit)
    {
        if (arCamera == null) return;

        Vector3 cameraPosition = arCamera.transform.position;

        // El origen del mapa queda a los pies del usuario, sobre el plano tocado.
        // El rumbo lo da la brújula (o la cámara si no hay brújula).
        PlacementPose = new Pose(
            new Vector3(cameraPosition.x, hit.position.y, cameraPosition.z),
            Quaternion.Euler(0f, GetHeadingYaw(), 0f));

        HasPlacement = true;

        if (PlacementChanged != null) PlacementChanged.Invoke();
    }

    private void CreateReticle()
    {
        GameObject existing = GameObject.Find("ReticulaAR");
        if (existing != null)
        {
            reticle = existing;
            reticle.SetActive(false);
            return;
        }

        // Se construye la malla a mano: el proyecto no incluye el modulo de fisica,
        // asi que GameObject.CreatePrimitive fallaria al agregar el CapsuleCollider.
        reticle = new GameObject("ReticulaAR");

        MeshFilter filter = reticle.AddComponent<MeshFilter>();
        filter.sharedMesh = BuildDiskMesh(0.5f, 40);

        MeshRenderer meshRenderer = reticle.AddComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        reticle.transform.localScale = new Vector3(reticleDiameter, 1f, reticleDiameter);

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");

        if (shader != null)
        {
            Material material = new Material(shader);
            material.name = "MaterialReticula";

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", AccentColor);
            if (material.HasProperty("_Color")) material.SetColor("_Color", AccentColor);

            meshRenderer.sharedMaterial = material;
        }

        reticle.SetActive(false);
    }

    /// <summary>Disco plano (radio 0.5) en el plano XZ, con la normal hacia arriba.</summary>
    private static Mesh BuildDiskMesh(float radius, int segments)
    {
        if (segments < 8) segments = 8;

        Vector3[] vertices = new Vector3[segments + 1];
        int[] triangles = new int[segments * 3];

        vertices[0] = Vector3.zero;
        for (int i = 0; i < segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

            int next = (i + 1) % segments;
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = next + 1;
            triangles[i * 3 + 2] = i + 1;
        }

        Mesh mesh = new Mesh();
        mesh.name = "ReticulaMesh";
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void HideFloorPlates()
    {
        if (campusRoot == null) return;

        Renderer[] renderers = campusRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].name.StartsWith("PlanoPiso"))
            {
                renderers[i].enabled = false;
            }
        }
    }
}
