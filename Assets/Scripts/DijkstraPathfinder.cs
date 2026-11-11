using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Implementación pura del algoritmo de Dijkstra para la red de nodos del campus.
/// </summary>
public class DijkstraPathfinder : MonoBehaviour
{
    /// <summary>
    /// Calcula el camino más corto entre dos nodos del campus.
    /// Devuelve una lista vacía si no existe ruta alcanzable.
    /// </summary>
    public List<Node> FindShortestPath(Node startNode, Node targetNode, List<Node> allNodes)
    {
        List<Node> path = new List<Node>();

        if (startNode == null || targetNode == null || allNodes == null || allNodes.Count == 0)
        {
            return path;
        }

        Dictionary<Node, float> distances = new Dictionary<Node, float>();
        Dictionary<Node, Node> previousNodes = new Dictionary<Node, Node>();
        List<Node> unvisited = new List<Node>();

        for (int i = 0; i < allNodes.Count; i++)
        {
            Node node = allNodes[i];
            if (node == null) continue;

            distances[node] = float.MaxValue;
            previousNodes[node] = null;
            unvisited.Add(node);
        }

        if (!distances.ContainsKey(startNode))
        {
            distances[startNode] = 0f;
            previousNodes[startNode] = null;
            unvisited.Add(startNode);
        }
        distances[startNode] = 0f;

        while (unvisited.Count > 0)
        {
            // Extraer el nodo no visitado con la distancia mínima.
            int bestIndex = 0;
            float bestDistance = distances[unvisited[0]];

            for (int i = 1; i < unvisited.Count; i++)
            {
                float distance = distances[unvisited[i]];
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            Node current = unvisited[bestIndex];
            unvisited.RemoveAt(bestIndex);

            if (current == targetNode) break;
            if (bestDistance == float.MaxValue) break; // El resto del grafo es inalcanzable.

            for (int i = 0; i < current.edges.Count; i++)
            {
                Edge edge = current.edges[i];
                if (edge == null || edge.targetNode == null) continue;
                if (!distances.ContainsKey(edge.targetNode)) continue;

                // Peso 0 => distancia física real en metros entre los dos nodos.
                float edgeWeight = edge.weight > 0f
                    ? edge.weight
                    : Vector3.Distance(current.transform.position, edge.targetNode.transform.position);

                float altDistance = distances[current] + edgeWeight;

                if (altDistance < distances[edge.targetNode])
                {
                    distances[edge.targetNode] = altDistance;
                    previousNodes[edge.targetNode] = current;
                }
            }
        }

        // Reconstrucción del camino (del destino hacia el origen).
        Node currentNode = targetNode;
        while (currentNode != null)
        {
            path.Add(currentNode);

            Node previous;
            if (!previousNodes.TryGetValue(currentNode, out previous) || previous == null) break;
            currentNode = previous;
        }
        path.Reverse();

        // Si no se pudo volver al origen, no existe ruta.
        if (path.Count == 0 || path[0] != startNode)
        {
            path.Clear();
        }
        return path;
    }

    /// <summary>Variante que indica si encontró una ruta válida.</summary>
    public bool TryFindShortestPath(Node startNode, Node targetNode, List<Node> allNodes, out List<Node> path)
    {
        path = FindShortestPath(startNode, targetNode, allNodes);
        return path.Count > 0;
    }

    /// <summary>Longitud total de un camino (en metros o en unidades de peso).</summary>
    public float GetPathLength(List<Node> path)
    {
        if (path == null || path.Count < 2) return 0f;

        float total = 0f;
        for (int i = 1; i < path.Count; i++)
        {
            Node from = path[i - 1];
            Node to = path[i];
            if (from == null || to == null) continue;

            total += from.GetEdgeWeight(to);
        }
        return total;
    }
}
