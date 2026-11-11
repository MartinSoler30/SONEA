using System;
using System.Collections.Generic;
using UnityEngine;

// =====================================================================
// Modelo de datos del campus (Assets/Resources/CampusGraph.json).
// Un solo grafo para los dos edificios y el exterior. El archivo es la
// fuente de verdad: el builder de editor lo lee y crea los nodos/aristas.
// =====================================================================

[Serializable]
public class CampusOrigin
{
    public float x;
    public float z;
}

[Serializable]
public class CampusRoom
{
    public string id;
    public string name;
    public float x;
    [Tooltip("north = lado +Z del corredor, south = lado -Z")]
    public string side = "north";
    public string type = "salon";
    public bool showInList = true;
}

[Serializable]
public class CampusEntrance
{
    public string id;
    public string name;
    [Tooltip("Coordenada local del edificio sobre el eje del corredor (metros).")]
    public float x;
    [Tooltip("Coordenada local del edificio perpendicular al corredor (metros).")]
    public float z;
    [Tooltip("Fachada donde queda la puerta: east, west, north o south.")]
    public string side = "east";
    public string type = "entrada";
    public bool showInList = true;
}

[Serializable]
public class CampusFloor
{
    public int index;
    public string name;
    public float stairX;
    public float stairZ;
    public bool hasElevator;
    public float elevatorX;
    public float elevatorZ;
    public List<CampusRoom> rooms = new List<CampusRoom>();
    public List<CampusEntrance> entrances = new List<CampusEntrance>();
}

[Serializable]
public class CampusBuilding
{
    public string id;
    public string name;
    public string shortName;
    public float floorHeight = 3.5f;
    public CampusOrigin origin = new CampusOrigin();
    public float rotationDegrees;
    public float length = 40f;
    public float width = 20f;
    public float corridorZ;
    public float corridorStartX;
    public float corridorEndX;
    public float doorOffset = 2f;
    public List<CampusFloor> floors = new List<CampusFloor>();
}

[Serializable]
public class CampusExteriorNode
{
    public string id;
    public string name;
    public float x;
    public float z;
    public string type = "exterior";
    public bool showInList = true;
}

[Serializable]
public class CampusEdgeLink
{
    public string from;
    public string to;
    public string type;
}

[Serializable]
public class CampusExterior
{
    public List<CampusExteriorNode> nodes = new List<CampusExteriorNode>();
    public List<CampusEdgeLink> edges = new List<CampusEdgeLink>();
}

[Serializable]
public class CampusGraphData
{
    public int schemaVersion = 1;
    public string campusName = "ETITC";
    public List<CampusBuilding> buildings = new List<CampusBuilding>();
    public CampusExterior exterior = new CampusExterior();
    public List<CampusEdgeLink> connections = new List<CampusEdgeLink>();

    public const string ResourceName = "CampusGraph";

    /// <summary>Carga el grafo empaquetado en Assets/Resources.</summary>
    public static CampusGraphData Load()
    {
        TextAsset asset = Resources.Load<TextAsset>(ResourceName);
        if (asset == null)
        {
            Debug.LogError("[CampusGraphData] No se encontro Assets/Resources/" + ResourceName + ".json");
            return null;
        }

        return FromJson(asset.text);
    }

    public static CampusGraphData FromJson(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            Debug.LogError("[CampusGraphData] El JSON del campus esta vacio.");
            return null;
        }

        try
        {
            CampusGraphData data = JsonUtility.FromJson<CampusGraphData>(json);
            if (data == null) throw new Exception("JsonUtility devolvio null.");
            if (data.buildings == null) data.buildings = new List<CampusBuilding>();
            if (data.exterior == null) data.exterior = new CampusExterior();
            if (data.exterior.nodes == null) data.exterior.nodes = new List<CampusExteriorNode>();
            if (data.exterior.edges == null) data.exterior.edges = new List<CampusEdgeLink>();
            if (data.connections == null) data.connections = new List<CampusEdgeLink>();

            for (int b = 0; b < data.buildings.Count; b++)
            {
                CampusBuilding building = data.buildings[b];
                if (building == null || building.floors == null) continue;

                for (int f = 0; f < building.floors.Count; f++)
                {
                    CampusFloor floor = building.floors[f];
                    if (floor == null) continue;
                    if (floor.rooms == null) floor.rooms = new List<CampusRoom>();
                    if (floor.entrances == null) floor.entrances = new List<CampusEntrance>();
                }
            }

            return data;
        }
        catch (Exception exception)
        {
            Debug.LogError("[CampusGraphData] JSON invalido: " + exception.Message);
            return null;
        }
    }

    public CampusBuilding GetBuilding(string buildingId)
    {
        if (buildings == null) return null;
        for (int i = 0; i < buildings.Count; i++)
        {
            if (buildings[i] != null && string.Equals(buildings[i].id, buildingId, StringComparison.OrdinalIgnoreCase))
            {
                return buildings[i];
            }
        }
        return null;
    }

    /// <summary>Comprueba lo mínimo para poder construir el grafo.</summary>
    public bool Validate(out string error)
    {
        error = null;
        if (buildings == null || buildings.Count == 0)
        {
            error = "El campus no tiene edificios.";
            return false;
        }

        HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int b = 0; b < buildings.Count; b++)
        {
            CampusBuilding building = buildings[b];
            if (building == null || string.IsNullOrEmpty(building.id))
            {
                error = "Hay un edificio sin id.";
                return false;
            }

            if (building.floors == null || building.floors.Count == 0)
            {
                error = "El edificio " + building.id + " no tiene pisos.";
                return false;
            }

            for (int f = 0; f < building.floors.Count; f++)
            {
                CampusFloor floor = building.floors[f];
                if (floor == null || floor.rooms == null)
                {
                    error = "El edificio " + building.id + " tiene un piso sin lista de espacios.";
                    return false;
                }

                for (int r = 0; r < floor.rooms.Count; r++)
                {
                    CampusRoom room = floor.rooms[r];
                    if (room == null || string.IsNullOrEmpty(room.id))
                    {
                        error = "Hay un espacio sin id en " + building.id + " piso " + floor.index + ".";
                        return false;
                    }

                    if (!ids.Add(room.id))
                    {
                        error = "Id duplicado: " + room.id;
                        return false;
                    }
                }

                for (int e = 0; e < floor.entrances.Count; e++)
                {
                    CampusEntrance entrance = floor.entrances[e];
                    if (entrance == null || string.IsNullOrEmpty(entrance.id))
                    {
                        error = "Hay una entrada sin id en " + building.id + " piso " + floor.index + ".";
                        return false;
                    }

                    if (!ids.Add(entrance.id))
                    {
                        error = "Id duplicado: " + entrance.id;
                        return false;
                    }
                }
            }
        }

        for (int i = 0; i < exterior.nodes.Count; i++)
        {
            CampusExteriorNode node = exterior.nodes[i];
            if (node == null || string.IsNullOrEmpty(node.id))
            {
                error = "Hay un punto exterior sin id.";
                return false;
            }

            if (!ids.Add(node.id))
            {
                error = "Id duplicado: " + node.id;
                return false;
            }
        }

        return true;
    }
}
