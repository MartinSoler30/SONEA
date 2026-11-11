using TMPro;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// HUD de depuración para las pruebas de campo: FPS, estado de tracking,
/// piso activo, nodo más cercano, distancia restante y desvío de la ruta.
/// </summary>
public class NavigationDebugHUD : MonoBehaviour
{
    [Header("Referencias")]
    public NavigationManager navigationManager;
    public CampusGraphManager graphManager;
    public ARCampusPlacement campusPlacement;
    public TMP_Text debugText;

    [Header("Frecuencia")]
    public float refreshInterval = 0.4f;
    public float floorTolerance = 2.0f;

    private Camera arCamera;
    private float elapsed;
    private int frames;
    private float fps;

    void Start()
    {
        Resolve();
        Refresh();
    }

    void Update()
    {
        frames++;
        elapsed += Time.unscaledDeltaTime;

        if (elapsed < refreshInterval) return;

        fps = frames / Mathf.Max(0.0001f, elapsed);
        frames = 0;
        elapsed = 0f;
        Refresh();
    }

    private void Resolve()
    {
        if (navigationManager == null) navigationManager = FindAnyObjectByType<NavigationManager>();
        if (graphManager == null) graphManager = FindAnyObjectByType<CampusGraphManager>();
        if (campusPlacement == null) campusPlacement = FindAnyObjectByType<ARCampusPlacement>();
        if (arCamera == null) arCamera = Camera.main;
        if (navigationManager != null && graphManager == null) graphManager = navigationManager.graphManager;
    }

    private void Refresh()
    {
        if (debugText == null) return;
        Resolve();

        Vector3 user = navigationManager != null
            ? navigationManager.GetUserPosition()
            : (arCamera != null ? arCamera.transform.position : Vector3.zero);

        float floorY = user.y - 1.6f;
        Node nearest = graphManager != null ? graphManager.FindNearestNode(user, floorTolerance) : null;

        int floor = 0;
        string building = "Campus";

        if (navigationManager != null && navigationManager.IsNavigating)
        {
            floor = navigationManager.GetActiveFloor();
        }

        if (nearest != null)
        {
            if (floor <= 0) floor = nearest.floor;
            if (!string.IsNullOrEmpty(nearest.buildingShortName)) building = nearest.buildingShortName;
        }

        string tracking = ARSession.state.ToString();
        string floorLabel = floor > 0 ? building + " · Piso " + floor : "Exterior";
        string nearLabel = nearest != null ? nearest.DisplayName : "sin nodo";

        float remaining = navigationManager != null ? navigationManager.GetRemainingDistance() : 0f;
        float deviation = navigationManager != null ? navigationManager.GetDeviationFromPath(user) : 0f;
        float heading = campusPlacement != null ? campusPlacement.headingOffsetDegrees : 0f;

        string state = navigationManager != null && navigationManager.IsNavigating
            ? (navigationManager.HasArrived ? "LLEGADA" : "EN RUTA")
            : "LIBRE";

        debugText.text =
            "FPS " + fps.ToString("F0") + " · AR " + tracking + "\n" +
            floorLabel + " · Cerca: " + nearLabel + "\n" +
            state + " · Faltan " + remaining.ToString("F0") + " m · Desvío " + deviation.ToString("F1") + " m\n" +
            "Rumbo " + heading.ToString("+0;-0;0") + "° · Suelo " + floorY.ToString("F2") + " m";
    }
}
