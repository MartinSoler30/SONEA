using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dibuja la trayectoria calculada sobre el suelo real mediante un <see cref="LineRenderer"/>.
/// </summary>
public class ARPathVisualizer : MonoBehaviour
{
    [Header("Referencia")]
    public LineRenderer lineRenderer;

    [Header("Estilo de la Ruta")]
    [Tooltip("Grosor de la línea en metros.")]
    public float lineWidth = 0.25f;

    public Color lineColor = new Color(0.10f, 0.90f, 1.00f, 1.00f);

    [Tooltip("Desplazamiento vertical en metros para evitar el z-fighting con el suelo.")]
    public float groundOffset = 0.02f;

    [Tooltip("Crea un LineRenderer automáticamente si no se ha asignado ninguno.")]
    public bool createLineRendererIfMissing = true;

    /// <summary>
    /// Recibe la lista de nodos calculada y dibuja una línea 3D continua.
    /// </summary>
    public void RenderPath(List<Node> pathNodes)
    {
        ClearPath();

        if (pathNodes == null || pathNodes.Count == 0) return;
        if (!EnsureLineRenderer()) return;

        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = pathNodes.Count;
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.startColor = lineColor;
        lineRenderer.endColor = lineColor;
        lineRenderer.numCapVertices = 4;
        lineRenderer.numCornerVertices = 2;

        for (int i = 0; i < pathNodes.Count; i++)
        {
            Node node = pathNodes[i];
            if (node == null) continue;

            Vector3 position = node.transform.position;
            position.y += groundOffset;
            lineRenderer.SetPosition(i, position);
        }
    }

    /// <summary>Borra la ruta dibujada.</summary>
    public void ClearPath()
    {
        if (lineRenderer == null) return;
        lineRenderer.positionCount = 0;
    }

    /// <summary>Devuelve el LineRenderer, creándolo si hace falta.</summary>
    public bool EnsureLineRenderer()
    {
        if (lineRenderer == null)
        {
            if (!createLineRendererIfMissing) return false;

            lineRenderer = GetComponent<LineRenderer>();
            if (lineRenderer == null)
            {
                lineRenderer = gameObject.AddComponent<LineRenderer>();
            }
        }

        if (lineRenderer.sharedMaterial == null)
        {
            lineRenderer.sharedMaterial = CreateRouteMaterial();
        }

        return lineRenderer != null;
    }

    /// <summary>Material simple para la línea de ruta (compatible con URP y con el pipeline clásico).</summary>
    public static Material CreateRouteMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) return null;

        Material material = new Material(shader);
        material.name = "MaterialRutaAR";

        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);

        return material;
    }
}
