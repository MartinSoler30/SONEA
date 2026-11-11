using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Componente asignado a cada punto navegable del campus (salones, pasillos, escaleras y exteriores).
/// Guarda el identificador del punto y la lista de conexiones salientes.
/// </summary>
public class Node : MonoBehaviour
{
    [Header("Identificador del Punto")]
    public string nodeName; // Ejemplo: "EN101", "Biblioteca", "Entrada Principal"

    [Header("Metadatos del campus")]
    [Tooltip("Id estable usado por el JSON y los codigos QR (ej. EN101, MOD_P2_BANO_M).")]
    public string nodeId;

    [Tooltip("Id del edificio: nuevo, modulares o vacio para exteriores.")]
    public string buildingId;

    [Tooltip("Orden del edificio en CampusGraph.json (0 = Nuevo, 1 = Modulares).")]
    public int buildingOrder;

    [Tooltip("Nombre legible del edificio.")]
    public string buildingName;

    [Tooltip("Nombre corto del edificio para la UI (Nuevo, Modulares, Campus).")]
    public string buildingShortName;

    [Tooltip("Piso (1..N). 0 para puntos exteriores.")]
    public int floor;

    [Tooltip("Altura del piso en coordenadas del grafo (0, 3.5, 7.0...).")]
    public float floorY;

    [Tooltip("salon, oficina, bano, biblioteca, pasillo, escalera, ascensor, entrada, salida, cancha, parqueadero, exterior, hall")]
    public string nodeType = "salon";

    [Tooltip("Si aparece en las listas de origen/destino de la app.")]
    public bool showInList = true;

    [Tooltip("Payload del QR asociado. Por defecto SONEA:<nodeId>.")]
    public string qrId;

    [Header("Conexiones")]
    public List<Edge> edges = new List<Edge>();

    [Header("Visualización")]
    [Tooltip("Color con el que este nodo se dibuja en la vista de escena (Gizmo).")]
    public Color gizmoColor = new Color(0.20f, 0.80f, 1.00f);

    [Tooltip("Radio del gizmo en metros (1 unidad Unity = 1 metro real).")]
    public float gizmoRadius = 0.35f;

    /// <summary>Nombre legible del nodo. Si no se asignó, usa el nombre del GameObject.</summary>
    public string DisplayName
    {
        get { return string.IsNullOrEmpty(nodeName) ? gameObject.name : nodeName; }
    }

    /// <summary>Etiqueta agrupada para los dropdowns: "Nuevo P1 · EN101".</summary>
    public string DropdownLabel
    {
        get
        {
            if (floor <= 0 || string.IsNullOrEmpty(buildingShortName))
            {
                return "Campus · " + DisplayName;
            }
            return buildingShortName + " P" + floor + " · " + DisplayName;
        }
    }

    /// <summary>Etiqueta del grupo al que pertenece (edificio + piso).</summary>
    public string GroupLabel
    {
        get
        {
            if (floor <= 0 || string.IsNullOrEmpty(buildingName))
            {
                return "Campus y exteriores";
            }
            return buildingName + " · Piso " + floor;
        }
    }

    public bool IsIndoor
    {
        get { return floor > 0 && !string.IsNullOrEmpty(buildingId); }
    }

    public bool IsStair
    {
        get { return nodeType == "escalera" || nodeType == "ascensor"; }
    }

    public string QrPayload
    {
        get { return string.IsNullOrEmpty(qrId) ? "SONEA:" + EffectiveId : qrId; }
    }

    public string EffectiveId
    {
        get { return string.IsNullOrEmpty(nodeId) ? DisplayName : nodeId; }
    }

    /// <summary>Agrega una arista saliente hacia <paramref name="target"/>.</summary>
    public void AddEdge(Node target, float weight = 0f)
    {
        if (target == null) return;

        Edge edge = new Edge();
        edge.targetNode = target;
        edge.weight = weight;
        edges.Add(edge);
    }

    /// <summary>Elimina todas las aristas salientes.</summary>
    public void ClearEdges()
    {
        edges.Clear();
    }

    /// <summary>Indica si ya existe una arista hacia <paramref name="target"/>.</summary>
    public bool HasEdgeTo(Node target)
    {
        if (target == null) return false;

        for (int i = 0; i < edges.Count; i++)
        {
            Edge edge = edges[i];
            if (edge != null && edge.targetNode == target) return true;
        }
        return false;
    }

    /// <summary>
    /// Peso efectivo de la arista hacia <paramref name="target"/>:
    /// el peso manual si es mayor que cero, o la distancia física en metros.
    /// </summary>
    public float GetEdgeWeight(Node target)
    {
        if (target == null) return float.PositiveInfinity;

        for (int i = 0; i < edges.Count; i++)
        {
            Edge edge = edges[i];
            if (edge == null || edge.targetNode != target) continue;

            return edge.weight > 0f
                ? edge.weight
                : Vector3.Distance(transform.position, target.transform.position);
        }
        return float.PositiveInfinity;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.01f, gizmoRadius));
        Gizmos.DrawSphere(transform.position, Mathf.Max(0.01f, gizmoRadius) * 0.35f);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (string.IsNullOrEmpty(nodeName))
        {
            nodeName = gameObject.name;
        }
    }
#endif
}

/// <summary>
/// Conexión dirigida de un nodo hacia otro nodo del grafo.
/// </summary>
[Serializable]
public class Edge
{
    public Node targetNode;
    public float weight; // Si se deja en 0, calcula la distancia física en metros automáticamente
}
