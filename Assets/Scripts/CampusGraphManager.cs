using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Contenedor raíz del grafo del campus ("CampusGraphManager").
/// Registra todos los nodos del Edificio Nuevo y ofrece utilidades de consulta
/// para la UI y el sistema de navegación.
/// </summary>
public class CampusGraphManager : MonoBehaviour
{
    [Header("Nodos del Grafo")]
    [Tooltip("Registro de todos los nodos navegables del campus.")]
    public List<Node> allNodes = new List<Node>();

    /// <summary>Todos los nodos registrados en el grafo.</summary>
    public List<Node> AllNodes
    {
        get { return allNodes; }
    }

    /// <summary>Cantidad de nodos registrados.</summary>
    public int NodeCount
    {
        get { return allNodes == null ? 0 : allNodes.Count; }
    }

    void Awake()
    {
        if (allNodes == null) allNodes = new List<Node>();
    }

    /// <summary>Recorre la escena y vuelve a registrar todos los nodos existentes.</summary>
    public void Refresh()
    {
        if (allNodes == null) allNodes = new List<Node>();
        allNodes.Clear();
        allNodes.AddRange(FindObjectsByType<Node>(FindObjectsInactive.Exclude));
    }

    /// <summary>Busca un nodo por su nombre (no distingue mayúsculas/	minúsculas).</summary>
    public Node GetNodeByName(string nodeName)
    {
        if (string.IsNullOrEmpty(nodeName) || allNodes == null) return null;

        for (int i = 0; i < allNodes.Count; i++)
        {
            Node node = allNodes[i];
            if (node == null) continue;

            if (string.Equals(node.DisplayName, nodeName, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }
        }
        return null;
    }

    /// <summary>Busca un nodo por su id estable (el que llevan los QR).</summary>
    public Node GetNodeById(string id)
    {
        if (string.IsNullOrEmpty(id) || allNodes == null) return null;

        string clean = id.Trim();
        if (clean.StartsWith("SONEA:", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring(6);
        }

        for (int i = 0; i < allNodes.Count; i++)
        {
            Node node = allNodes[i];
            if (node == null) continue;
            if (string.Equals(node.EffectiveId, clean, StringComparison.OrdinalIgnoreCase)) return node;
        }
        return null;
    }

    /// <summary>Nodo mas cercano en el plano horizontal, opcionalmente limitado a un piso.</summary>
    public Node FindNearestNode(Vector3 worldPosition, float maxFloorDelta = float.MaxValue)
    {
        if (allNodes == null) return null;

        Node best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < allNodes.Count; i++)
        {
            Node node = allNodes[i];
            if (node == null) continue;

            if (maxFloorDelta < float.MaxValue &&
                Mathf.Abs(node.transform.position.y - worldPosition.y) > maxFloorDelta)
            {
                continue;
            }

            Vector3 delta = node.transform.position - worldPosition;
            delta.y = 0f;
            float distance = delta.sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = node;
            }
        }
        return best;
    }

    /// <summary>Devuelve los nodos cuyo nombre empieza por el prefijo indicado (ej. "EN1").</summary>
    public List<Node> GetNodesByPrefix(string prefix)
    {
        List<Node> result = new List<Node>();
        if (string.IsNullOrEmpty(prefix) || allNodes == null) return result;

        for (int i = 0; i < allNodes.Count; i++)
        {
            Node node = allNodes[i];
            if (node != null && node.DisplayName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(node);
            }
        }
        return result;
    }

    /// <summary>
    /// Valida el grafo completo: ids duplicados, nodos aislados, aristas sin retorno,
    /// alcanzabilidad desde una entrada y coherencia de piso/edificio.
    /// </summary>
    public bool ValidateGraph(out string report)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        if (allNodes == null || allNodes.Count == 0)
        {
            report = "El grafo no tiene nodos registrados.";
            return false;
        }

        List<string> errores = new List<string>();
        List<string> avisos = new List<string>();

        // 1) Ids unicos y metadatos.
        HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < allNodes.Count; i++)
        {
            Node node = allNodes[i];
            if (node == null)
            {
                errores.Add("Hay una referencia nula en allNodes.");
                continue;
            }

            string id = node.EffectiveId;
            if (!ids.Add(id)) errores.Add("Id duplicado: " + id);

            if (node.floor > 0 && string.IsNullOrEmpty(node.buildingId))
            {
                avisos.Add(node.DisplayName + " tiene piso pero no edificio.");
            }

            if (node.edges == null || node.edges.Count == 0)
            {
                errores.Add("Nodo sin conexiones: " + node.DisplayName + " (" + id + ")");
            }
        }

        // 2) Aristas sin retorno (el grafo debe ser navegable en ambos sentidos).
        int sinRetorno = 0;
        for (int i = 0; i < allNodes.Count; i++)
        {
            Node node = allNodes[i];
            if (node == null || node.edges == null) continue;

            for (int e = 0; e < node.edges.Count; e++)
            {
                Edge edge = node.edges[e];
                if (edge == null || edge.targetNode == null) continue;
                if (!edge.targetNode.HasEdgeTo(node))
                {
                    sinRetorno++;
                    if (sinRetorno <= 10)
                    {
                        avisos.Add("Arista sin retorno: " + node.DisplayName + " -> " + edge.targetNode.DisplayName);
                    }
                }
            }
        }

        // 3) Alcanzabilidad desde una entrada (BFS).
        Node origen = null;
        for (int i = 0; i < allNodes.Count; i++)
        {
            Node node = allNodes[i];
            if (node != null && node.floor == 1 && node.nodeType == "entrada")
            {
                origen = node;
                break;
            }
        }
        if (origen == null)
        {
            for (int i = 0; i < allNodes.Count; i++)
            {
                if (allNodes[i] != null) { origen = allNodes[i]; break; }
            }
        }

        HashSet<Node> visitados = new HashSet<Node>();
        if (origen != null)
        {
            Queue<Node> cola = new Queue<Node>();
            cola.Enqueue(origen);
            visitados.Add(origen);

            while (cola.Count > 0)
            {
                Node actual = cola.Dequeue();
                if (actual.edges == null) continue;

                for (int e = 0; e < actual.edges.Count; e++)
                {
                    Edge edge = actual.edges[e];
                    if (edge == null || edge.targetNode == null) continue;
                    if (visitados.Add(edge.targetNode)) cola.Enqueue(edge.targetNode);
                }
            }
        }

        List<Node> inalcanzables = new List<Node>();
        for (int i = 0; i < allNodes.Count; i++)
        {
            Node node = allNodes[i];
            if (node == null) continue;
            if (!visitados.Contains(node)) inalcanzables.Add(node);
        }

        if (inalcanzables.Count > 0)
        {
            errores.Add("Nodos inalcanzables desde " + (origen != null ? origen.DisplayName : "?") +
                        ": " + inalcanzables.Count);
            for (int i = 0; i < inalcanzables.Count && i < 16; i++)
            {
                errores.Add("  - " + inalcanzables[i].DisplayName + " (" + inalcanzables[i].EffectiveId + ")");
            }
        }

        // 4) Destinos visibles alcanzables.
        int destinos = 0;
        for (int i = 0; i < allNodes.Count; i++)
        {
            Node node = allNodes[i];
            if (node == null || !node.showInList) continue;
            destinos++;
            if (!visitados.Contains(node))
            {
                errores.Add("Destino no alcanzable: " + node.DisplayName);
            }
        }

        sb.AppendLine("Nodos: " + allNodes.Count + " | Destinos en lista: " + destinos +
                      " | Alcanzables: " + visitados.Count);
        sb.AppendLine("Aristas sin retorno: " + sinRetorno);
        if (avisos.Count > 0)
        {
            sb.AppendLine("Avisos:");
            for (int i = 0; i < avisos.Count && i < 12; i++) sb.AppendLine("  - " + avisos[i]);
        }
        if (errores.Count > 0)
        {
            sb.AppendLine("Errores (" + errores.Count + "):");
            for (int i = 0; i < errores.Count && i < 20; i++) sb.AppendLine("  - " + errores[i]);
        }
        else
        {
            sb.AppendLine("Grafo valido: todos los destinos son alcanzables.");
        }

        report = sb.ToString();
        return errores.Count == 0;
    }
}
