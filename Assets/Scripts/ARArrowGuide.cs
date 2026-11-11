using System.Collections.Generic;
using UnityEngine;
using Unity.XR.CoreUtils;

/// <summary>
/// Guía AR estilo Apple Maps: una flecha plana (decal) apoyada sobre la polilínea de la
/// ruta, unos metros por delante del usuario. Se detiene en el vértice del próximo giro,
/// cambia de color con el desvío y se desvanece al llegar.
/// </summary>
public class ARArrowGuide : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Transform del quad de la flecha (hijo de este objeto).")]
    public Transform arrowTransform;

    [Tooltip("Renderer del quad; se usa para cambiar el material según el estado.")]
    public Renderer arrowRenderer;

    [Tooltip("Materiales en orden: 0 azul (en ruta), 1 ámbar (desviado), 2 verde (llegada).")]
    public Material[] stateMaterials = new Material[3];

    [Tooltip("Controlador de navegación para conocer desvío y distancia restante.")]
    public NavigationManager navigationManager;

    [Header("Comportamiento")]
    [Tooltip("Distancia en metros para avanzar al siguiente nodo.")]
    public float arriveThreshold = 1.5f;

    [Tooltip("Velocidad de interpolación de la rotación (Slerp).")]
    public float rotationSpeed = 6.0f;

    [Tooltip("Cámara de referencia (AR Camera). Si se deja vacía se busca automáticamente.")]
    public Transform targetCamera;

    [Tooltip("Altura de los ojos usada si no se encuentra el XR Origin.")]
    public float fallbackEyeHeight = 1.6f;

    [Header("Decal sobre el piso")]
    [Tooltip("Altura de la flecha sobre el plano del piso, en metros.")]
    public float surfaceOffset = 0.02f;

    [Tooltip("Ancho base de la flecha, en metros.")]
    public float decalWidth = 0.9f;

    [Tooltip("Distancia mínima por delante del usuario, en metros.")]
    public float minAhead = 2.5f;

    [Tooltip("Distancia máxima por delante del usuario, en metros.")]
    public float maxAhead = 4.0f;

    [Tooltip("Amplitud del pulso suave (0.04 = 4 %).")]
    public float pulseAmplitude = 0.04f;

    [Tooltip("Velocidad del pulso.")]
    public float pulseSpeed = 2.5f;

    [Header("Estados")]
    [Tooltip("Desvío (m) a partir del cual la flecha pasa a ámbar.")]
    public float warningDistance = 4.0f;

    [Tooltip("Distancia restante (m) a la que la flecha pasa a verde.")]
    public float arrivalDistance = 5.0f;

    [Tooltip("Distancia al destino donde empieza el fundido (m).")]
    public float fadeStart = 1.5f;

    [Tooltip("Distancia al destino donde la flecha desaparece (m).")]
    public float fadeEnd = 0.5f;

    [Tooltip("Diferencia vertical (m) para considerar que un nodo está en el piso del usuario.")]
    public float floorTolerance = 2.0f;

    private XROrigin xrOrigin;
    private List<Node> currentPath = new List<Node>();
    private int targetNodeIndex = 0;
    private bool isActive = false;
    private int currentState = -1;
    private bool isVisible = true;
    private MaterialPropertyBlock propertyBlock;

    /// <summary>Indica si la flecha está guiando actualmente al usuario.</summary>
    public bool IsGuiding
    {
        get { return isActive; }
    }

    /// <summary>Nodo de control hacia el que apunta la flecha en este momento.</summary>
    public Node CurrentTargetNode
    {
        get
        {
            if (!isActive || targetNodeIndex < 0 || targetNodeIndex >= currentPath.Count) return null;
            return currentPath[targetNodeIndex];
        }
    }

    public int CurrentTargetIndex
    {
        get { return targetNodeIndex; }
    }

    /// <summary>Se dispara cuando la flecha pasa al siguiente nodo de control.</summary>
    public event System.Action<Node> TargetChanged;

    /// <summary>Se dispara al llegar al destino final.</summary>
    public event System.Action<Node> Arrived;

    void Awake()
    {
        if (arrowTransform == null && transform.childCount > 0)
        {
            arrowTransform = transform.GetChild(0);
        }

        if (arrowRenderer == null && arrowTransform != null)
        {
            arrowRenderer = arrowTransform.GetComponent<Renderer>();
        }

        if (navigationManager == null) navigationManager = GetComponentInParent<NavigationManager>();
        if (navigationManager == null) navigationManager = FindAnyObjectByType<NavigationManager>();

        if (targetCamera == null)
        {
            Camera cam = Camera.main;
            if (cam != null) targetCamera = cam.transform;
        }

        xrOrigin = GetComponentInParent<XROrigin>();
        if (xrOrigin == null) xrOrigin = FindAnyObjectByType<XROrigin>();

        propertyBlock = new MaterialPropertyBlock();
    }

    /// <summary>Recibe la ruta calculada y prepara la flecha para guiar al usuario.</summary>
    public void InitializePath(List<Node> path)
    {
        if (path == null || path.Count < 2) return;

        currentPath = new List<Node>(path);
        targetNodeIndex = 1;
        isActive = true;
        currentState = -1;

        SetVisible(true);

        if (TargetChanged != null) TargetChanged.Invoke(CurrentTargetNode);
    }

    /// <summary>Detiene la guía y oculta la flecha.</summary>
    public void ResetGuide()
    {
        isActive = false;
        targetNodeIndex = 0;
        currentPath = new List<Node>();
        currentState = -1;
        SetVisible(false);
    }

    void Update()
    {
        if (!isActive || currentPath == null || currentPath.Count < 2 || arrowTransform == null) return;

        Vector3 user = GetUserPosition();
        float floorY = GetFloorHeight();

        AdvanceIfPast(user, floorY);
        if (!isActive) return;

        Node from = currentPath[Mathf.Max(0, targetNodeIndex - 1)];
        Node to = currentPath[Mathf.Min(currentPath.Count - 1, targetNodeIndex)];
        if (from == null || to == null) return;

        Vector3 start = from.transform.position;
        Vector3 end = to.transform.position;

        // En escaleras el tramo no pertenece al piso del usuario: se oculta el decal.
        if (Mathf.Abs(start.y - floorY) > floorTolerance || Mathf.Abs(end.y - floorY) > floorTolerance)
        {
            SetVisible(false);
            return;
        }

        // Dirección horizontal del tramo.
        Vector3 direction = new Vector3(end.x - start.x, 0f, end.z - start.z);
        float segmentLength = direction.magnitude;

        if (segmentLength < 0.001f)
        {
            direction = targetCamera != null ? targetCamera.forward : Vector3.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
            direction.Normalize();
        }
        else
        {
            direction /= segmentLength;
        }

        // Punto de la polilínea más cercano al usuario.
        Vector3 toUser = new Vector3(user.x - start.x, 0f, user.z - start.z);
        float along = Mathf.Clamp(Vector3.Dot(toUser, direction), 0f, segmentLength);
        float remaining = segmentLength - along;

        // En un giro cercano la flecha se coloca en el vértice; si el tramo es largo,
        // avanza entre 2.5 y 4 m por delante del usuario.
        bool atTurn = remaining <= minAhead + 0.15f;
        float ahead = atTurn ? remaining : Mathf.Clamp(remaining * 0.55f, minAhead, maxAhead);

        Vector3 position = start + direction * (along + ahead);
        position.y = Mathf.Max(start.y, end.y) + surfaceOffset;

        // Orientación: en el vértice apunta hacia el tramo siguiente.
        Vector3 facing = direction;
        if (atTurn && targetNodeIndex + 1 < currentPath.Count)
        {
            Node after = currentPath[targetNodeIndex + 1];
            if (after != null)
            {
                Vector3 next = after.transform.position - end;
                next.y = 0f;
                if (next.sqrMagnitude > 0.0004f) facing = next.normalized;
            }
        }

        SetVisible(true);
        arrowTransform.position = position;

        Quaternion targetRotation = Quaternion.LookRotation(facing, Vector3.up);
        arrowTransform.rotation = Quaternion.Slerp(arrowTransform.rotation, targetRotation,
            Time.deltaTime * rotationSpeed);

        // Escala según la distancia: más grande cuanto más lejos, con un pulso suave.
        float distanceToArrow = Vector2.Distance(
            new Vector2(user.x, user.z),
            new Vector2(position.x, position.z));
        float scale = Mathf.Clamp(distanceToArrow / 4f, 0.75f, 1.5f) * decalWidth;
        float pulse = 1f + pulseAmplitude * Mathf.Sin(Time.time * pulseSpeed);
        arrowTransform.localScale = new Vector3(scale * pulse, 1f, scale * pulse);

        UpdateState(user, position);
    }

    /// <summary>Avanza de nodo cuando el usuario ya pasó el actual o llegó a él.</summary>
    private void AdvanceIfPast(Vector3 user, float floorY)
    {
        Node current = currentPath[targetNodeIndex];
        if (current == null)
        {
            AdvanceToNextNode();
            return;
        }

        float distance = GetAdvanceDistance(user, current.transform.position, floorY);

        // Si el nodo siguiente está más cerca, el usuario ya dobló: se salta el actual.
        while (targetNodeIndex + 1 < currentPath.Count)
        {
            Node next = currentPath[targetNodeIndex + 1];
            if (next == null) break;

            float nextDistance = GetAdvanceDistance(user, next.transform.position, floorY);
            if (nextDistance + 0.25f >= distance) break;

            targetNodeIndex++;
            if (TargetChanged != null) TargetChanged.Invoke(CurrentTargetNode);

            current = currentPath[targetNodeIndex];
            if (current == null) return;
            distance = GetAdvanceDistance(user, current.transform.position, floorY);
        }

        if (distance <= arriveThreshold) AdvanceToNextNode();
    }

    /// <summary>Cambia el material según el estado y aplica el fundido cerca del destino.</summary>
    private void UpdateState(Vector3 user, Vector3 arrowPosition)
    {
        float remainingTotal = navigationManager != null ? navigationManager.GetRemainingDistance() : 0f;
        float deviation = navigationManager != null ? navigationManager.GetDeviationFromPath(user) : 0f;

        int state = 0;
        if (remainingTotal <= arrivalDistance) state = 2;
        else if (deviation > warningDistance) state = 1;

        if (state != currentState)
        {
            currentState = state;
            if (arrowRenderer != null && stateMaterials != null &&
                state < stateMaterials.Length && stateMaterials[state] != null)
            {
                arrowRenderer.sharedMaterial = stateMaterials[state];
            }
        }

        Node destination = currentPath[currentPath.Count - 1];
        float toDestination = destination != null
            ? Vector2.Distance(new Vector2(arrowPosition.x, arrowPosition.z),
                new Vector2(destination.transform.position.x, destination.transform.position.z))
            : fadeStart;

        float alpha = Mathf.Clamp01(Mathf.InverseLerp(fadeEnd, fadeStart, toDestination));

        if (arrowRenderer != null)
        {
            propertyBlock.Clear();
            propertyBlock.SetColor("_BaseColor", new Color(1f, 1f, 1f, alpha));
            arrowRenderer.SetPropertyBlock(propertyBlock);
        }
    }

    private Vector3 GetUserPosition()
    {
        if (targetCamera != null) return targetCamera.position;
        if (xrOrigin != null && xrOrigin.Camera != null) return xrOrigin.Camera.transform.position;
        return transform.position;
    }

    private float GetAdvanceDistance(Vector3 userPosition, Vector3 targetPosition, float floorY)
    {
        Vector3 flatUser = new Vector3(userPosition.x, 0f, userPosition.z);
        Vector3 flatTarget = new Vector3(targetPosition.x, 0f, targetPosition.z);
        float horizontal = Vector3.Distance(flatUser, flatTarget);
        float vertical = Mathf.Abs(targetPosition.y - floorY);

        return vertical <= 1.2f
            ? horizontal
            : Mathf.Sqrt(horizontal * horizontal + vertical * vertical);
    }

    /// <summary>Altura del suelo real bajo el usuario (cámara menos altura de ojos).</summary>
    private float GetFloorHeight()
    {
        if (targetCamera != null) return targetCamera.position.y - fallbackEyeHeight;
        if (xrOrigin != null) return xrOrigin.transform.position.y;
        return 0f;
    }

    private void SetVisible(bool visible)
    {
        if (arrowTransform == null || isVisible == visible) return;
        isVisible = visible;
        arrowTransform.gameObject.SetActive(visible);
    }

    /// <summary>Avanza al siguiente nodo de control de la ruta.</summary>
    private void AdvanceToNextNode()
    {
        targetNodeIndex++;

        if (targetNodeIndex >= currentPath.Count)
        {
            isActive = false;
            SetVisible(false);

            Node destination = currentPath.Count > 0 ? currentPath[currentPath.Count - 1] : null;
            Debug.Log("¡Has llegado a tu destino!" + (destination != null ? " (" + destination.DisplayName + ")" : string.Empty));

            if (Arrived != null) Arrived.Invoke(destination);
            return;
        }

        if (TargetChanged != null) TargetChanged.Invoke(CurrentTargetNode);
    }
}
