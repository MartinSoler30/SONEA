using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Genera una etiqueta QR por cada espacio navegable del campus (salones, oficinas,
/// baños, biblioteca, escaleras, entradas y puntos exteriores).
///
/// Salida en Builds/QR:
///   png/&lt;id&gt;.png          codigo QR (payload SONEA:&lt;id&gt;)
///   manifiesto.csv         id, nombre, edificio, piso, tipo, payload, archivo
///   index.html             hoja imprimible con todas las etiquetas
///   &lt;grupo&gt;.html          hoja por edificio/piso
/// </summary>
public static class QRCodeGenerator
{
    private const string ApiUrl = "https://api.qrserver.com/v1/create-qr-code/?size=400x400&data=";
    private const string OutputFolder = "Builds/QR";

    private class QrItem
    {
        public string id;
        public string name;
        public string building;
        public int floor;
        public string type;
        public string group;
        public string file;
        public bool downloaded;
    }

    [MenuItem("ETITC Tools/Generar Codigos QR del Campus", false, 40)]
    public static void GenerateQrCodes()
    {
        CampusGraphData data = CampusGraphData.Load();
        if (data == null)
        {
            EditorUtility.DisplayDialog("SONEA", "No se pudo cargar Assets/Resources/CampusGraph.json.", "Cerrar");
            return;
        }

        List<QrItem> items = BuildItems(data);
        if (items.Count == 0)
        {
            EditorUtility.DisplayDialog("SONEA", "El grafo no tiene espacios para generar QR.", "Cerrar");
            return;
        }

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string outputRoot = Path.Combine(projectRoot, OutputFolder);
        string pngFolder = Path.Combine(outputRoot, "png");
        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(pngFolder);

        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

        int ok = 0;
        int failed = 0;

        using (WebClient client = new WebClient())
        {
            client.Headers.Add("User-Agent", "SONEA-QR-Generator/1.0");

            for (int i = 0; i < items.Count; i++)
            {
                QrItem item = items[i];
                string payload = "SONEA:" + item.id;
                string fileName = SafeFileName(item.id) + ".png";
                string fullPath = Path.Combine(pngFolder, fileName);
                item.file = "png/" + fileName;

                try
                {
                    if (!File.Exists(fullPath) || new FileInfo(fullPath).Length < 100)
                    {
                        client.DownloadFile(ApiUrl + Uri.EscapeDataString(payload), fullPath);
                    }

                    item.downloaded = File.Exists(fullPath) && new FileInfo(fullPath).Length > 100;
                    if (item.downloaded) ok++;
                    else failed++;
                }
                catch (Exception exception)
                {
                    failed++;
                    Debug.LogWarning("[QRCodeGenerator] No se pudo generar " + item.id + ": " + exception.Message);
                }

                if (EditorUtility.DisplayCancelableProgressBar("SONEA · Generando QR",
                        item.building + " · " + item.name + " (" + (i + 1) + "/" + items.Count + ")",
                        (i + 1) / (float)items.Count))
                {
                    break;
                }
            }
        }

        EditorUtility.ClearProgressBar();

        WriteManifest(outputRoot, items);
        WriteHtml(Path.Combine(outputRoot, "index.html"), "SONEA · Todas las etiquetas QR", items);
        WriteGroupSheets(outputRoot, items);

        Debug.Log("[QRCodeGenerator] QR generados: " + ok + " | fallidos: " + failed +
                  " | carpeta: " + outputRoot);

        if (Application.isBatchMode)
        {
            Debug.Log("[QRCodeGenerator] Manifiesto y hojas HTML listos.");
        }
        else
        {
            EditorUtility.DisplayDialog("SONEA · QR",
                "Etiquetas generadas: " + ok + "\nFallidas: " + failed + "\n\n" + outputRoot, "Abrir carpeta");
            EditorUtility.RevealInFinder(outputRoot);
        }
    }

    private static List<QrItem> BuildItems(CampusGraphData data)
    {
        List<QrItem> items = new List<QrItem>();

        for (int b = 0; b < data.buildings.Count; b++)
        {
            CampusBuilding building = data.buildings[b];
            if (building == null || building.floors == null) continue;

            string buildingName = string.IsNullOrEmpty(building.name) ? building.id : building.name;

            for (int f = 0; f < building.floors.Count; f++)
            {
                CampusFloor floor = building.floors[f];
                if (floor == null) continue;

                string group = buildingName + " · Piso " + floor.index;

                // Escalera y ascensor (los crea el builder, no están en rooms).
                items.Add(new QrItem
                {
                    id = building.id + "_P" + floor.index + "_escalera",
                    name = "Escalera " + buildingName + " Piso " + floor.index,
                    building = buildingName,
                    floor = floor.index,
                    type = "escalera",
                    group = group
                });

                if (floor.hasElevator)
                {
                    items.Add(new QrItem
                    {
                        id = building.id + "_P" + floor.index + "_ascensor",
                        name = "Ascensor " + buildingName + " Piso " + floor.index,
                        building = buildingName,
                        floor = floor.index,
                        type = "ascensor",
                        group = group
                    });
                }

                // Entradas reales definidas en CampusGraph.json.
                if (floor.entrances != null)
                {
                    for (int e = 0; e < floor.entrances.Count; e++)
                    {
                        CampusEntrance entrance = floor.entrances[e];
                        if (entrance == null || string.IsNullOrEmpty(entrance.id)) continue;

                        items.Add(new QrItem
                        {
                            id = entrance.id,
                            name = entrance.name,
                            building = buildingName,
                            floor = floor.index,
                            type = string.IsNullOrEmpty(entrance.type) ? "entrada" : entrance.type,
                            group = group
                        });
                    }
                }

                if (floor.rooms != null)
                {
                    for (int r = 0; r < floor.rooms.Count; r++)
                    {
                        CampusRoom room = floor.rooms[r];
                        if (room == null) continue;

                        items.Add(new QrItem
                        {
                            id = room.id,
                            name = room.name,
                            building = buildingName,
                            floor = floor.index,
                            type = room.type,
                            group = group
                        });
                    }
                }
            }
        }

        if (data.exterior != null && data.exterior.nodes != null)
        {
            for (int i = 0; i < data.exterior.nodes.Count; i++)
            {
                CampusExteriorNode node = data.exterior.nodes[i];
                if (node == null) continue;

                items.Add(new QrItem
                {
                    id = node.id,
                    name = node.name,
                    building = "Campus",
                    floor = 0,
                    type = node.type,
                    group = "Campus y exteriores"
                });
            }
        }

        return items;
    }

    private static void WriteManifest(string outputRoot, List<QrItem> items)
    {
        StringBuilder csv = new StringBuilder();
        csv.AppendLine("id,nombre,edificio,piso,tipo,payload,archivo,generado");

        for (int i = 0; i < items.Count; i++)
        {
            QrItem item = items[i];
            csv.AppendLine(string.Join(",", new[]
            {
                Quote(item.id),
                Quote(item.name),
                Quote(item.building),
                item.floor.ToString(CultureInfo.InvariantCulture),
                Quote(item.type),
                Quote("SONEA:" + item.id),
                Quote(item.file),
                item.downloaded ? "si" : "no"
            }));
        }

        File.WriteAllText(Path.Combine(outputRoot, "manifiesto.csv"), csv.ToString(), new UTF8Encoding(true));
    }

    private static void WriteGroupSheets(string outputRoot, List<QrItem> items)
    {
        Dictionary<string, List<QrItem>> groups = new Dictionary<string, List<QrItem>>();
        for (int i = 0; i < items.Count; i++)
        {
            string key = items[i].group ?? "Campus";
            if (!groups.ContainsKey(key)) groups[key] = new List<QrItem>();
            groups[key].Add(items[i]);
        }

        foreach (KeyValuePair<string, List<QrItem>> pair in groups)
        {
            string fileName = SafeFileName(pair.Key) + ".html";
            WriteHtml(Path.Combine(outputRoot, fileName), "SONEA · " + pair.Key, pair.Value);
        }
    }

    private static void WriteHtml(string path, string title, List<QrItem> items)
    {
        StringBuilder html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"es\"><head><meta charset=\"utf-8\">");
        html.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.AppendLine("<title>" + Escape(title) + "</title>");
        html.AppendLine("<style>");
        html.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;background:#0b1220;color:#e8f2ff;margin:24px}");
        html.AppendLine("h1{color:#3ed8ff;font-size:22px;margin:0 0 6px}");
        html.AppendLine("p.sub{color:#8fa6bd;margin:0 0 20px;font-size:13px}");
        html.AppendLine(".grid{display:grid;grid-template-columns:repeat(3,1fr);gap:14px}");
        html.AppendLine(".card{border:1px solid #2a3b52;border-radius:10px;padding:12px;text-align:center;background:#111c2d;break-inside:avoid}");
        html.AppendLine(".card img{width:150px;height:150px;background:#fff;border-radius:6px;padding:4px}");
        html.AppendLine(".name{font-size:16px;font-weight:600;margin-top:8px;color:#fff}");
        html.AppendLine(".meta{font-size:11px;color:#9db2c8;margin-top:2px}");
        html.AppendLine(".id{font-size:10px;color:#5d7a96;margin-top:4px;word-break:break-all}");
        html.AppendLine("@media print{body{background:#fff;color:#000;margin:8mm}.card{background:#fff;border:1px solid #bbb}.name{color:#000}.meta{color:#333}.id{color:#666}h1{color:#000}p.sub{color:#333}.grid{grid-template-columns:repeat(3,1fr)}}");
        html.AppendLine("@page{size:A4;margin:8mm}");
        html.AppendLine("</style></head><body>");
        html.AppendLine("<h1>" + Escape(title) + "</h1>");
        html.AppendLine("<p class=\"sub\">ETITC · SONEA · " + items.Count +
                        " etiquetas · payload SONEA:&lt;id&gt; · imprimir al 100 %</p>");
        html.AppendLine("<div class=\"grid\">");

        for (int i = 0; i < items.Count; i++)
        {
            QrItem item = items[i];
            html.AppendLine("<div class=\"card\">");
            if (item.downloaded)
            {
                html.AppendLine("<img src=\"" + Escape(item.file) + "\" alt=\"" + Escape(item.id) + "\">");
            }
            else
            {
                html.AppendLine("<div style=\"height:150px;display:flex;align-items:center;justify-content:center;color:#ff9f43\">QR pendiente</div>");
            }
            html.AppendLine("<div class=\"name\">" + Escape(item.name) + "</div>");
            html.AppendLine("<div class=\"meta\">" + Escape(item.group) + "</div>");
            html.AppendLine("<div class=\"id\">SONEA:" + Escape(item.id) + "</div>");
            html.AppendLine("</div>");
        }

        html.AppendLine("</div></body></html>");
        File.WriteAllText(path, html.ToString(), new UTF8Encoding(false));
    }

    private static string Quote(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    }

    private static string Escape(string value)
    {
        return (value ?? string.Empty)
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    private static string SafeFileName(string value)
    {
        if (string.IsNullOrEmpty(value)) return "sin_nombre";

        value = value.Replace(" · ", "_").Replace("·", "_").Replace(" ", "_");

        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (Array.IndexOf(invalid, c) >= 0) sb.Append('_');
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
