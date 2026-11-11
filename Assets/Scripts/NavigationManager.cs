using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controlador orquestador central que vincula <see cref="DijkstraPathfinder"/>,
/// <see cref="ARPathVisualizer"/> y <see cref="ARArrowGuide"/>.
/// Añade re-ruteo, instrucciones turn-by-turn y detección del piso activo.
/// </summary>
public class NavigationManager : MonoBehaviour
{
    [Header("Componentes del Sistema")]
    public DijkstraPathfinder pathfinder;
    public ARPathVisualizer pathVisualizer;
    public ARArrowGuide arrowGuide;

    [Header("Contexto del Grafo")]
    [Tooltip("Registro de nodos del campus. Si se deja vacío se buscan en la escena.")]
    public CampusGraphManager graphManager;

    [Header("Anclaje al entorno real")]
    [Tooltip("Coloca el mapa sobre los planos detectados por ARCore.")]
    public ARCampusPlacement campusPlacement;

    [Header("Re-ruteo")]
    public bool enableRerouting = true;

    [Tooltip("Desvío en metros que dispara el re-ruteo.")]
    public float rerouteDistance = 4.0f;

    [Tooltip("Tiempo (s) que hay que estar desviado antes de recalcular.")]
    public float rerouteDelay = 3.0f;

    [Tooltip("Tiempo mínimo (s) entre dos re-ruteos.")]
    public float rerouteCooldown = 6.0f;

    [Tooltip("Diferencia vertical (m) para considerar que un nodo está en el piso del usuario.")]
    public float floorTolerance = 1.6f;

    [Header("Instrucciones")]
    [Tooltip("Ángulo (grados) a partir del cual se anuncia un giro.")]
    public float turnAngleThreshold = 35f;

    [Tooltip("Altura de los ojos usada para calcular el suelo.")]
    public float eyeHeight = 1.6f;

    private List<Node> allNodes = new List<Node>();
    private List<Node> currentPath;
    private Node startNode;
    private Node targetNode;
    private Node arrivedNode;
    private bool isNavigating = false;
    private bool lastNavigationSucceeded = false;
    private bool placementSubscribed = false;
    private bool arrowSubscribed = false;
    private bool arrived = false;
    private float offRouteTimer = 0f;
    private float lastRerouteTime = -100f;
    private Camera userCamera;

    /// <summary>Todos los nodos disponibles para el cálculo de rutas.</summary>
    public List<Node> AllNodes
    {
        get { return allNodes; }
    }

    /// <summary>Ruta activa (null si no hay navegación).</summary>
    public List<Node> CurrentPath
    {
        get { return currentPath; }
    }

    public Node StartNode
    {
        get { return startNode; }
    }

    public Node TargetNode
    {
        get { return targetNode; }
    }

    public Node ArrivedNode
    {
        get { return arrivedNode; }
    }

    public bool IsNavigating
    {
        get { return isNavigating; }
    }

    public bool HasArrived
    {
        get { return arrived; }
    }

    /// <summary>Resultado de la última llamada a <see cref="StartNavigation"/>.</summary>
    public bool LastNavigationSucceeded
    {
        get { return lastNavigationSucceeded; }
    }

    void Awake()
    {
        ResolveReferences();
    }

    void Start()
    {
        RefreshNodes();
        SubscribePlacement();
        SubscribeArrow();
    }

    void OnDisable()
    {
        UnsubscribePlacement();
        UnsubscribeArrow();
    }

    /// <summary>Busca automáticamente los componentes del sistema si no fueron asignados.</summary>
    public void ResolveReferences()
    {
        if (pathfinder == null) pathfinder = GetComponent<DijkstraPathfinder>();
        if (pathfinder == null) pathfinder = FindAnyObjectByType<DijkstraPathfinder>();

        if (pathVisualizer == null) pathVisualizer = GetComponentInChildren<ARPathVisualizer>();
        if (pathVisualizer == null) pathVisualizer = FindAnyObjectByType<ARPathVisualizer>();

        if (arrowGuide == null) arrowGuide = GetComponentInChildren<ARArrowGuide>();
        if (arrowGuide == null) arrowGuide = FindAnyObjectByType<ARArrowGuide>();

        if (graphManager == null) graphManager = FindAnyObjectByType<CampusGraphManager>();

        if (campusPlacement == null) campusPlacement = FindAnyObjectByType<ARCampusPlacement>();

        if (userCamera == null) userCamera = Camera.main;
    }

    private void SubscribePlacement()
    {
        if (placementSubscribed || campusPlacement == null) return;
        campusPlacement.PlacementChanged += OnPlacementChanged;
        placementSubscribed = true;
    }

    private void UnsubscribePlacement()
    {
        if (!placementSubscribed || campusPlacement == null) return;
        campusPlacement.PlacementChanged -= OnPlacementChanged;
        placementSubscribed = false;
    }

    private void SubscribeArrow()
    {
        if (arrowSubscribed || arrowGuide == null) return;
        arrowGuide.Arrived += OnArrived;
        arrowSubscribed = true;
    }

    private void UnsubscribeArrow()
    {
        if (!arrowSubscribed || arrowGuide == null) return;
        arrowGuide.Arrived -= OnArrived;
        arrowSubscribed = false;
    }

    private void OnPlacementChanged()
    {
        RefreshNavigationPlacement();
    }

    private void OnArrived(Node destination)
    {
        arrived = true;
        isNavigating = false;
        arrivedNode = destination != null ? destination : targetNode;
        Debug.Log("[NavigationManager] Llegada a " + (arrivedNode != null ? arrivedNode.DisplayName : "destino") + ".");
    }

    /// <summary>Vuelve a cargar la lista de nodos navegables del campus.</summary>
    public void RefreshNodes()
    {
        allNodes = new List<Node>();

        if (graphManager != null && graphManager.AllNodes != null && graphManager.AllNodes.Count > 0)
        {
            for (int i = 0; i < graphManager.AllNodes.Count; i++)
            {
                if (graphManager.AllNodes[i] != null) allNodes.Add(graphManager.AllNodes[i]);
            }
        }
        else
        {
            allNodes.AddRange(FindObjectsByType<Node>(FindObjectsInactive.Exclude));
        }
    }

    /// <summary>Calcula la ruta entre dos nodos y la dibuja en la escena.</summary>
    public void StartNavigation(Node start, Node target)
    {
        TryStartNavigation(start, target);
    }

    /// <summary>
    /// Igual que <see cref="StartNavigation"/> pero devuelve si se encontró una ruta válida.
    /// </summary>
    public bool TryStartNavigation(Node start, Node target)
    {
        lastNavigationSucceeded = false;

        if (start == null || target == null)
        {
            Debug.LogWarning("[NavigationManager] Origen o destino inválido: no se puede iniciar la navegación.");
            return false;
        }

        if (pathfinder == null) ResolveReferences();
        if (pathfinder == null)
        {
            Debug.LogError("[NavigationManager] No se encontró el DijkstraPathfinder.");
            return false;
        }

        if (allNodes.Count == 0) RefreshNodes();

        List<Node> path = pathfinder.FindShortestPath(start, target, allNodes);

        if (path == null || path.Count == 0)
        {
            Debug.LogWarning("[NavigationManager] No existe ruta entre " + start.DisplayName + " y " + target.DisplayName + ".");
            if (pathVisualizer != null) pathVisualizer.ClearPath();
            if (arrowGuide != null) arrowGuide.ResetGuide();
            isNavigating = false;
            currentPath = null;
            return false;
        }

        startNode = start;
        targetNode = target;
        arrivedNode = null;
        arrived = false;
        offRouteTimer = 0f;
        currentPath = new List<Node>(path);
        isNavigating = true;

        if (campusPlacement == null) campusPlacement = FindAnyObjectByType<ARCampusPlacement>();
        SubscribePlacement();

        if (campusPlacement != null)
        {
            campusPlacement.SetPlacementEnabled(true);

            // Si aún no hay anclaje, el mapa se coloca en la pose real del usuario.
            if (!campusPlacement.HasPlacement) campusPlacement.AutoPlaceAtCamera();
        }

        RefreshNavigationPlacement();

        if (arrowGuide != null) arrowGuide.InitializePath(currentPath);
        SubscribeArrow();

        lastNavigationSucceeded = true;
        return true;
    }

    /// <summary>
    /// Vuelve a aplicar el anclaje actual del mapa y redibuja la ruta y la flecha.
    /// Se llama al iniciar la navegación y cada vez que el usuario recoloca o gira el mapa.
    /// </summary>
    public void RefreshNavigationPlacement()
    {
        if (!isNavigating || currentPath == null || currentPath.Count == 0) return;

        if (campusPlacement != null)
        {
            Node start = currentPath[0];
            Node next = currentPath.Count > 1 ? currentPath[1] : null;
            campusPlacement.ApplyPlacement(start, next);
        }

        if (pathVisualizer != null) pathVisualizer.RenderPath(currentPath);
    }

    /// <summary>Detiene la navegación actual y borra la ruta dibujada.</summary>
    public void StopNavigation()
    {
        lastNavigationSucceeded = false;
        isNavigating = false;
        arrived = false;
        currentPath = null;
        startNode = null;
        targetNode = null;
        arrivedNode = null;

        if (pathVisualizer != null) pathVisualizer.ClearPath();
        if (arrowGuide != null) arrowGuide.ResetGuide();

        if (campusPlacement != null)
        {
            campusPlacement.ResetPlacement();
            campusPlacement.SetPlacementEnabled(false);
        }
    }

    void Update()
    {
        if (!isNavigating || currentPath == null || currentPath.Count == 0) return;

        if (!enableRerouting) return;
        if (Time.time - lastRerouteTime < rerouteCooldown) return;

        Vector3 user = GetUserPosition();
        if (user == Vector3.zero) return;

        float deviation = GetDeviationFromPath(user);
        if (deviation > rerouteDistance)
        {
            offRouteTimer += Time.deltaTime;
            if (offRouteTimer >= rerouteDelay)
            {
                TryReroute(user);
                offRouteTimer = 0f;
            }
        }
        else
        {
            offRouteTimer = 0f;
        }
    }

    /// <summary>Posición del usuario (cámara AR).</summary>
    public Vector3 GetUserPosition()
    {
        if (userCamera == null)
        {
            userCamera = Camera.main;
            if (userCamera == null && arrowGuide != null && arrowGuide.targetCamera != null)
            {
                return arrowGuide.targetCamera.position;
            }
        }
        return userCamera != null ? userCamera.transform.position : Vector3.zero;
    }

    /// <summary>Altura del suelo real bajo el usuario.</summary>
    public float GetUserFloorY()
    {
        Vector3 user = GetUserPosition();
        return user.y - eyeHeight;
    }

    /// <summary>Piso activo según el nodo más cercano en el mismo nivel.</summary>
    public int GetActiveFloor()
    {
        if (startNode != null && currentPath != null && currentPath.Count > 0)
        {
            Vector3 user = GetUserPosition();
            Node nearest = FindNearestNodeOnLevel(user, currentPath);
            if (nearest != null) return nearest.floor;
            return startNode.floor;
        }

        return 0;
    }

    private Node FindNearestNodeOnLevel(Vector3 user, List<Node> preferredNodes)
    {
        Node best = null;
        float bestDistance = float.MaxValue;
        float floorY = user.y - eyeHeight;

        if (preferredNodes != null)
        {
            for (int i = 0; i < preferredNodes.Count; i++)
            {
                Node node = preferredNodes[i];
                if (node == null) continue;
                if (Mathf.Abs(node.transform.position.y - floorY) > floorTolerance) continue;

                Vector3 delta = node.transform.position - user;
                delta.y = 0f;
                if (delta.sqrMagnitude < bestDistance)
                {
                    bestDistance = delta.sqrMagnitude;
                    best = node;
                }
            }
        }

        return best;
    }

    /// <summary>Distancia mínima del usuario a la polilínea de la ruta (solo tramos de su piso).</summary>
    public float GetDeviationFromPath(Vector3 user)
    {
        if (currentPath == null || currentPath.Count < 2) return 0f;

        float floorY = user.y - eyeHeight;
        float best = float.MaxValue;
        int considered = 0;

        for (int i = 1; i < currentPath.Count; i++)
        {
            Node a = currentPath[i - 1];
            Node b = currentPath[i];
            if (a == null || b == null) continue;

            // Ignora tramos de otro piso (escaleras incluidas).
            if (Mathf.Abs(a.transform.position.y - floorY) > floorTolerance) continue;
            if (Mathf.Abs(b.transform.position.y - floorY) > floorTolerance) continue;

            considered++;
            Vector3 p = new Vector3(user.x, 0f, user.z);
            Vector3 pa = new Vector3(a.transform.position.x, 0f, a.transform.position.z);
            Vector3 pb = new Vector3(b.transform.position.x, 0f, b.transform.position.z);

            Vector3 ab = pb - pa;
            float t = ab.sqrMagnitude > 0.0001f
                ? Mathf.Clamp01(Vector3.Dot(p - pa, ab) / ab.sqrMagnitude)
                : 0f;

            float distance = Vector3.Distance(p, pa + ab * t);
            if (distance < best) best = distance;
        }

        return considered > 0 ? best : 0f;
    }

    /// <summary>Distancia restante (m) desde el próximo nodo de control hasta el destino.</summary>
    public float GetRemainingDistance()
    {
        if (currentPath == null || currentPath.Count == 0) return 0f;

        int index = arrowGuide != null ? Mathf.Clamp(arrowGuide.CurrentTargetIndex, 0, currentPath.Count - 1) : 0;
        float total = 0f;

        for (int i = index + 1; i < currentPath.Count; i++)
        {
            Node a = currentPath[i - 1];
            Node b = currentPath[i];
            if (a == null || b == null) continue;

            Vector3 pa = a.transform.position;
            Vector3 pb = b.transform.position;
            float horizontal = Vector2.Distance(new Vector2(pa.x, pa.z), new Vector2(pb.x, pb.z));
            float vertical = Mathf.Abs(pb.y - pa.y);
            total += vertical > 1.0f ? Mathf.Sqrt(horizontal * horizontal + vertical * vertical) : horizontal;
        }

        return total;
    }

    /// <summary>Texto breve que se muestra en el HUD durante la ruta.</summary>
    public string GetInstruction()
    {
        if (arrived)
        {
            string name = arrivedNode != null ? arrivedNode.DisplayName : (targetNode != null ? targetNode.DisplayName : "tu destino");
            return "Llegaste a " + name + ".\nToca Detener Ruta para volver al mapa.";
        }

        if (!isNavigating || currentPath == null || currentPath.Count == 0)
        {
            return string.Empty;
        }

        int index = arrowGuide != null ? arrowGuide.CurrentTargetIndex : 1;
        index = Mathf.Clamp(index, 1, currentPath.Count - 1);

        Node next = currentPath[index];
        Node previous = currentPath[index - 1];
        if (next == null) return string.Empty;

        Vector3 user = GetUserPosition();
        Vector3 targetPosition = next.transform.position;
        Vector3 flatUser = new Vector3(user.x, 0f, user.z);
        Vector3 flatTarget = new Vector3(targetPosition.x, 0f, targetPosition.z);
        float distance = Vector3.Distance(flatUser, flatTarget);

        string line;
        bool floorChange = previous != null && next.floor != previous.floor && next.floor > 0;

        if (floorChange)
        {
            string verb = next.floor > previous.floor ? "Sube" : "Baja";
            line = verb + " al piso " + next.floor + " por la escalera";
            if (next.nodeType == "ascensor") line = verb + " al piso " + next.floor + " en el ascensor";
        }
        else if (next.nodeType == "entrada" || next.nodeType == "exterior" ||
                 next.nodeType == "cancha" || next.nodeType == "parqueadero")
        {
            line = distance > 3f ? "Sal al exterior y sigue " + distance.ToString("F0") + " m" : "Llegas al exterior";
        }
        else if (distance > 2.5f)
        {
            float angle = 0f;
            if (userCamera == null) userCamera = Camera.main;
            if (userCamera != null)
            {
                Vector3 forward = userCamera.transform.forward;
                forward.y = 0f;
                Vector3 direction = flatTarget - flatUser;
                if (forward.sqrMagnitude > 0.0001f && direction.sqrMagnitude > 0.0001f)
                {
                    angle = Vector3.SignedAngle(forward.normalized, direction.normalized, Vector3.up);
                }
            }

            if (Mathf.Abs(angle) >= turnAngleThreshold)
            {
                line = (angle > 0f ? "Gira a la derecha" : "Gira a la izquierda") +
                       " y sigue " + distance.ToString("F0") + " m";
            }
            else
            {
                line = "Sigue " + distance.ToString("F0") + " m por el pasillo";
            }
        }
        else
        {
            line = "Sigue " + distance.ToString("F1") + " m";
        }

        string destination = targetNode != null ? targetNode.DisplayName : "destino";
        return line + "\nDestino: " + destination + " · " + GetRemainingDistance().ToString("F0") + " m";
    }

    /// <summary>Tipo de maniobra que viene en la ruta (para el icono del banner).</summary>
    public enum ManeuverType
    {
        None,
        Straight,
        Left,
        Right,
        Up,
        Down,
        Exit,
        Arrive
    }

    private int GetManeuverIndex()
    {
        if (currentPath == null || currentPath.Count < 2) return -1;
        int index = arrowGuide != null ? arrowGuide.CurrentTargetIndex : 1;
        return Mathf.Clamp(index, 1, currentPath.Count - 1);
    }

    /// <summary>Maniobra del próximo nodo de control, calculada con la geometría de la ruta.</summary>
    public ManeuverType GetManeuver()
    {
        if (arrived) return ManeuverType.Arrive;
        if (!isNavigating || currentPath == null || currentPath.Count < 2) return ManeuverType.None;

        int index = GetManeuverIndex();
        if (index < 0) return ManeuverType.None;

        Node next = currentPath[index];
        Node previous = currentPath[index - 1];
        if (next == null) return ManeuverType.None;

        if (previous != null && next.floor != previous.floor)
        {
            return next.floor > previous.floor ? ManeuverType.Up : ManeuverType.Down;
        }

        if (next.nodeType == "entrada") return ManeuverType.Exit;

        if (previous != null && index + 1 < currentPath.Count)
        {
            Node after = currentPath[index + 1];
            if (after != null && after.floor == next.floor)
            {
                Vector3 incoming = next.transform.position - previous.transform.position;
                Vector3 outgoing = after.transform.position - next.transform.position;
                incoming.y = 0f;
                outgoing.y = 0f;

                if (incoming.sqrMagnitude > 0.01f && outgoing.sqrMagnitude > 0.01f)
                {
                    float angle = Vector3.SignedAngle(incoming, outgoing, Vector3.up);
                    if (angle >= turnAngleThreshold) return ManeuverType.Right;
                    if (angle <= -turnAngleThreshold) return ManeuverType.Left;
                }
            }
        }

        return ManeuverType.Straight;
    }

    /// <summary>Distancia horizontal (m) al próximo punto de maniobra.</summary>
    public float GetDistanceToNextManeuver()
    {
        if (!isNavigating || currentPath == null || currentPath.Count < 2) return 0f;

        int index = GetManeuverIndex();
        if (index < 0) return 0f;

        Node next = currentPath[index];
        if (next == null) return 0f;

        Vector3 user = GetUserPosition();
        Vector3 target = next.transform.position;
        return Vector2.Distance(new Vector2(user.x, user.z), new Vector2(target.x, target.z));
    }

    /// <summary>Título del banner: "Navegando a &lt;destino&gt;".</summary>
    public string GetBannerTitle()
    {
        if (targetNode != null) return "Navegando a " + targetNode.DisplayName;
        if (arrivedNode != null) return arrivedNode.DisplayName;
        return "SONEA";
    }

    /// <summary>Instrucción corta para el banner superior (estilo Apple Maps).</summary>
    public string GetBannerInstruction()
    {
        ManeuverType maneuver = GetManeuver();
        float distance = GetDistanceToNextManeuver();
        string meters = distance >= 10f ? distance.ToString("F0") : distance.ToString("F1");

        switch (maneuver)
        {
            case ManeuverType.Arrive:
                return "Has llegado a " + (arrivedNode != null ? arrivedNode.DisplayName : "tu destino");

            case ManeuverType.Up:
            case ManeuverType.Down:
            {
                Node next = CurrentTarget;
                int floor = next != null ? next.floor : 0;
                string verb = maneuver == ManeuverType.Up ? "Sube" : "Baja";
                string place = next != null && next.nodeType == "ascensor" ? "en el ascensor" : "por la escalera";
                return verb + " al piso " + floor + " " + place;
            }

            case ManeuverType.Exit:
                return distance > 3f ? "Sal al exterior · " + meters + " m" : "Sal al exterior";

            case ManeuverType.Left:
                return distance > 3f ? "En " + meters + " m gira a la izquierda" : "Gira a la izquierda";

            case ManeuverType.Right:
                return distance > 3f ? "En " + meters + " m gira a la derecha" : "Gira a la derecha";

            case ManeuverType.Straight:
                return distance > 2f ? "Continúa " + meters + " m" : "Sigue recto";

            default:
                return "En ruta";
        }
    }

    /// <summary>Destino activo (para el icono del banner).</summary>
    public Node CurrentTarget
    {
        get { return CurrentTargetNodeOrDestination(); }
    }

    private Node CurrentTargetNodeOrDestination()
    {
        int index = GetManeuverIndex();
        if (index >= 0 && currentPath != null) return currentPath[index];
        return targetNode;
    }


    private void TryReroute(Vector3 user)
    {
        if (graphManager == null || targetNode == null || currentPath == null) return;

        Node nearest = graphManager.FindNearestNode(user, floorTolerance + 0.5f);
        if (nearest == null) return;

        // Si el nodo más cercano ya está en la ruta, no hay nada que corregir.
        if (currentPath.Contains(nearest)) return;

        // Evita re-rutear a puntos absurdamente lejos (pérdida de tracking).
        Vector3 delta = nearest.transform.position - user;
        delta.y = 0f;
        if (delta.sqrMagnitude > 100f) return;

        List<Node> newPath = pathfinder.FindShortestPath(nearest, targetNode, allNodes);
        if (newPath == null || newPath.Count < 2) return;

        currentPath = newPath;
        lastRerouteTime = Time.time;
        startNode = nearest;

        Debug.Log("[NavigationManager] Re-ruteo desde " + nearest.DisplayName +
                  " (" + newPath.Count + " nodos).");

        if (pathVisualizer != null) pathVisualizer.RenderPath(currentPath);
        if (arrowGuide != null) arrowGuide.InitializePath(currentPath);
    }
}
