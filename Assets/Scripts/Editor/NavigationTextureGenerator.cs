using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Genera por código las texturas de la capa AR (sin depender de assets externos):
///   - Flecha de piso estilo Apple Maps (azul, ámbar y verde), con borde y halo blancos.
///   - Iconos de maniobra para el banner superior (recto, izquierda, derecha, llegada).
///
/// Las texturas se dibujan con un campo de distancia con signo (SDF) para que los
/// bordes queden suaves a cualquier resolución. Salida: Assets/Textures/.
/// </summary>
public static class NavigationTextureGenerator
{
    private const string TexturesFolder = "Assets/Textures";

    private const int DecalSize = 512;
    private const int IconSize = 128;

    // Contorno de la flecha en coordenadas normalizadas (x derecha, y arriba, punta en +y).
    private static readonly Vector2[] ArrowShape =
    {
        new Vector2(0.00f, 0.94f),   // punta
        new Vector2(0.47f, -0.08f),  // ala derecha
        new Vector2(0.00f, 0.26f),   // muesca trasera
        new Vector2(-0.47f, -0.08f)  // ala izquierda
    };

    private static readonly Color FillBlue = new Color32(10, 132, 255, 255);
    private static readonly Color FillAmber = new Color32(255, 159, 10, 255);
    private static readonly Color FillGreen = new Color32(48, 209, 88, 255);

    [MenuItem("ETITC Tools/Generar Texturas de Navegación", false, 41)]
    public static void GenerateAll()
    {
        Directory.CreateDirectory(TexturesFolder);

        // Flecha de piso: 512 px, borde blanco de 9 px y halo de 26 px.
        WriteDecal("arrow_decal_blue.png", FillBlue, 9f, 26f);
        WriteDecal("arrow_decal_amber.png", FillAmber, 9f, 26f);
        WriteDecal("arrow_decal_green.png", FillGreen, 9f, 26f);

        // Iconos del banner (blancos para poder tintarlos desde la UI).
        WriteIcon("turn_up.png", ArrowShape, 0f);
        WriteIcon("turn_left.png", Rotate(ArrowShape, 90f), 0f);
        WriteIcon("turn_right.png", Rotate(ArrowShape, -90f), 0f);
        WriteIcon("turn_down.png", Rotate(ArrowShape, 180f), 0f);
        WriteArrivalIcon("turn_arrive.png");

        AssetDatabase.Refresh();
        Debug.Log("[NavigationTextureGenerator] Texturas de navegación generadas en " + TexturesFolder);
    }

    // ------------------------------------------------------------------
    // Flecha de piso
    // ------------------------------------------------------------------
    private static void WriteDecal(string fileName, Color fill, float borderPx, float haloPx)
    {
        int size = DecalSize;
        float scale = size * 0.74f;                        // 0.98 unidades de alto -> ~379 px
        Vector2 center = new Vector2(size * 0.5f, size * 0.55f);

        Vector2[] poly = ToPixels(ArrowShape, center, scale);
        Color[] pixels = new Color[size * size];

        float minY = float.MaxValue;
        float maxY = float.MinValue;
        for (int i = 0; i < poly.Length; i++)
        {
            minY = Mathf.Min(minY, poly[i].y);
            maxY = Mathf.Max(maxY, poly[i].y);
        }

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float distance = SignedDistance(p, poly);
                float inside = -distance;

                Color color = new Color(1f, 1f, 1f, 0f);

                if (inside >= borderPx)
                {
                    float t = Mathf.Clamp01((y - minY) / Mathf.Max(1f, maxY - minY));
                    Color top = new Color(
                        Mathf.Min(1f, fill.r * 1.18f + 0.05f),
                        Mathf.Min(1f, fill.g * 1.18f + 0.05f),
                        Mathf.Min(1f, fill.b * 1.18f + 0.05f));
                    color = Color.Lerp(top, fill, t);
                    color.a = 1f;
                }
                else if (inside >= 0f)
                {
                    color = Color.white;
                }
                else
                {
                    float halo = Mathf.Clamp01(1f - (-inside) / haloPx);
                    color = new Color(1f, 1f, 1f, 0.88f * halo * halo);
                }

                pixels[y * size + x] = color;
            }
        }

        WriteTexture(fileName, size, pixels, true, true);
    }

    // ------------------------------------------------------------------
    // Iconos de maniobra
    // ------------------------------------------------------------------
    private static void WriteIcon(string fileName, Vector2[] shape, float borderPx)
    {
        int size = IconSize;
        float scale = size * 0.78f;
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        Vector2[] poly = ToPixels(shape, center, scale);

        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float distance = SignedDistance(p, poly);
                float alpha = Mathf.Clamp01((-distance) / 1.6f + 0.5f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        WriteTexture(fileName, size, pixels, true, false);
    }

    /// <summary>Anillo con punto central: icono de "Has llegado".</summary>
    private static void WriteArrivalIcon(string fileName)
    {
        int size = IconSize;
        float center = size * 0.5f;
        float outer = size * 0.36f;
        float inner = size * 0.24f;
        float dot = size * 0.11f;
        float aa = 1.4f;

        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(center, center));

                float ring = Mathf.Clamp01((outer - d) / aa) * Mathf.Clamp01((d - inner) / aa);
                float point = Mathf.Clamp01((dot - d) / aa);
                float alpha = Mathf.Max(ring, point);

                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        WriteTexture(fileName, size, pixels, true, false);
    }

    // ------------------------------------------------------------------
    // Utilidades
    // ------------------------------------------------------------------
    private static Vector2[] ToPixels(Vector2[] shape, Vector2 center, float scale)
    {
        Vector2[] result = new Vector2[shape.Length];
        for (int i = 0; i < shape.Length; i++)
        {
            result[i] = new Vector2(center.x + shape[i].x * scale, center.y - shape[i].y * scale);
        }
        return result;
    }

    private static Vector2[] Rotate(Vector2[] shape, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);

        Vector2[] result = new Vector2[shape.Length];
        for (int i = 0; i < shape.Length; i++)
        {
            result[i] = new Vector2(
                shape[i].x * cos - shape[i].y * sin,
                shape[i].x * sin + shape[i].y * cos);
        }
        return result;
    }

    /// <summary>Distancia con signo a un polígono (negativa dentro).</summary>
    private static float SignedDistance(Vector2 point, Vector2[] polygon)
    {
        int count = polygon.Length;
        float best = (point - polygon[0]).sqrMagnitude;
        float sign = 1f;

        for (int i = 0, j = count - 1; i < count; j = i, i++)
        {
            Vector2 edge = polygon[j] - polygon[i];
            Vector2 toPoint = point - polygon[i];

            float denominator = Vector2.Dot(edge, edge);
            float t = denominator > 0.000001f
                ? Mathf.Clamp01(Vector2.Dot(toPoint, edge) / denominator)
                : 0f;

            Vector2 closest = toPoint - edge * t;
            best = Mathf.Min(best, closest.sqrMagnitude);

            bool c1 = point.y >= polygon[i].y;
            bool c2 = point.y < polygon[j].y;
            bool c3 = edge.x * toPoint.y > edge.y * toPoint.x;

            if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) sign = -sign;
        }

        return sign * Mathf.Sqrt(best);
    }

    private static void WriteTexture(string fileName, int size, Color[] pixels, bool alphaIsTransparency, bool mipmaps)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels(pixels);
        texture.Apply();

        string path = TexturesFolder + "/" + fileName;
        File.WriteAllBytes(path, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = alphaIsTransparency;
            importer.mipmapEnabled = mipmaps;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = Mathf.Max(128, size);
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
        }
    }

    /// <summary>Carga una textura ya generada (o null si no existe).</summary>
    public static Texture2D Load(string fileName)
    {
        return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturesFolder + "/" + fileName);
    }
}
