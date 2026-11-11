using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Administra la UI del Canvas: llena los Dropdowns agrupados por edificio/piso,
/// aplica el filtro dinámico de búsqueda y conmuta entre el panel 2D y el HUD de AR.
/// Los dropdowns se resuelven por índice contra una lista de nodos (nunca por texto).
/// </summary>
public class CampusUIController : MonoBehaviour
{
    [Header("Paneles UI")]
    public GameObject panel2DMap;
    public GameObject panelARHUD;

    [Header("Elementos de Selección y Búsqueda")]
    public TMP_Dropdown originDropdown;
    public TMP_Dropdown destinationDropdown;
    public TMP_InputField searchInputField;

    [Tooltip("Botón que dispara el cálculo de la ruta.")]
    public Button btnStartNavigation;

    [Tooltip("Botón opcional para detener la navegación AR.")]
    public Button btnStopNavigation;

    [Tooltip("Botón que valida la alcanzabilidad del grafo.")]
    public Button btnValidateGraph;

    [Header("Referencias del Sistema")]
    public NavigationManager navigationManager;

    [Tooltip("Registro de nodos del campus. Si se deja vacío se buscan en la escena.")]
    public CampusGraphManager graphManager;

    [Header("Retroalimentación")]
    [Tooltip("Texto del HUD AR donde se muestran las instrucciones.")]
    public TMP_Text statusLabel;

    [Tooltip("Texto del panel 2D para errores y validación del grafo.")]
    public TMP_Text status2DLabel;

    [Header("Anclaje AR")]
    [Tooltip("Componente que ancla el mapa a los planos detectados.")]
    public ARCampusPlacement campusPlacement;

    private List<Node> allCampusNodes = new List<Node>();
    private List<Node> listNodes = new List<Node>();
    private List<Node> filteredNodes = new List<Node>();

    // Opciones reales de cada dropdown (los encabezados de grupo van con null).
    private List<Node> originOptions = new List<Node>();
    private List<Node> destinationOptions = new List<Node>();

    private int lastOriginValue = -1;
    private int lastDestinationValue = -1;
    private bool suppressCallbacks = false;

    /// <summary>Nodos actualmente disponibles como destino (tras aplicar el filtro de búsqueda).</summary>
    public List<Node> FilteredNodes
    {
        get { return filteredNodes; }
    }

    void Start()
    {
        CollectNodes();
        PopulateDropdowns(listNodes);

        // Se enlaza en runtime solo si el builder no dejó un listener persistente en la escena.
        if (searchInputField != null && searchInputField.onValueChanged.GetPersistentEventCount() == 0)
        {
            searchInputField.onValueChanged.AddListener(OnSearchValueChanged);
        }

        if (btnStartNavigation != null && btnStartNavigation.onClick.GetPersistentEventCount() == 0)
        {
            btnStartNavigation.onClick.AddListener(OnClickStartNavigation);
        }

        if (btnStopNavigation != null && btnStopNavigation.onClick.GetPersistentEventCount() == 0)
        {
            btnStopNavigation.onClick.AddListener(OnClickStopNavigation);
        }

        if (btnValidateGraph != null && btnValidateGraph.onClick.GetPersistentEventCount() == 0)
        {
            btnValidateGraph.onClick.AddListener(OnClickValidateGraph);
        }

        if (originDropdown != null && originDropdown.onValueChanged.GetPersistentEventCount() == 0)
        {
            originDropdown.onValueChanged.AddListener(OnOriginDropdownChanged);
        }

        if (destinationDropdown != null && destinationDropdown.onValueChanged.GetPersistentEventCount() == 0)
        {
            destinationDropdown.onValueChanged.AddListener(OnDestinationDropdownChanged);
        }

        SelectDefaults();
        SetStatus("Selecciona origen y destino, luego pulsa Iniciar Ruta AR.");
        SetStatus2D("Listo. " + listNodes.Count + " destinos en el campus.");
    }

    void Update()
    {
        // El texto del HUD lo actualiza NavigationBanner con instrucciones cortas
        // (estilo Apple Maps). Aquí no se sobreescribe para no competir con el banner.
    }

    /// <summary>Recoge todos los nodos del campus disponibles.</summary>
    public void CollectNodes()
    {
        List<Node> found = new List<Node>();

        if (graphManager != null && graphManager.AllNodes != null && graphManager.AllNodes.Count > 0)
        {
            found.AddRange(graphManager.AllNodes.Where(n => n != null));
        }
        else if (navigationManager != null && navigationManager.AllNodes != null && navigationManager.AllNodes.Count > 0)
        {
            found.AddRange(navigationManager.AllNodes.Where(n => n != null));
        }
        else
        {
            found.AddRange(FindObjectsByType<Node>(FindObjectsInactive.Exclude).Where(n => n != null));
        }

        allCampusNodes = found
            .GroupBy(n => n.EffectiveId)
            .Select(g => g.First())
            .OrderBy(n => n.floor <= 0 ? 1 : 0)
            .ThenBy(n => n.buildingOrder)
            .ThenBy(n => n.floor)
            .ThenBy(n => n.DisplayName)
            .ToList();

        listNodes = allCampusNodes.Where(n => n.showInList).ToList();
        filteredNodes = new List<Node>(listNodes);
    }

    /// <summary>Llena los dos Dropdowns con encabezados por edificio y piso.</summary>
    public void PopulateDropdowns(List<Node> nodes)
    {
        filteredNodes = new List<Node>(nodes ?? new List<Node>());

        RebuildOriginOptions(filteredNodes);
        RebuildDestinationOptions(filteredNodes);

        SelectDefaults();
    }

    private void BuildGroupedOptions(List<Node> nodes, out List<string> labels, out List<Node> mapped)
    {
        labels = new List<string>();
        mapped = new List<Node>();

        List<Node> ordered = (nodes ?? new List<Node>())
            .Where(n => n != null && n.showInList)
            .OrderBy(n => n.floor <= 0 ? 1 : 0)
            .ThenBy(n => n.buildingOrder)
            .ThenBy(n => n.floor)
            .ThenBy(n => n.DisplayName)
            .ToList();

        string currentGroup = null;
        for (int i = 0; i < ordered.Count; i++)
        {
            Node node = ordered[i];
            string group = node.GroupLabel;

            if (!string.Equals(group, currentGroup, StringComparison.Ordinal))
            {
                currentGroup = group;
                labels.Add("── " + group + " ──");
                mapped.Add(null);
            }

            labels.Add(node.DropdownLabel);
            mapped.Add(node);
        }
    }

    /// <summary>
    /// Reconstruye el Dropdown de origen. Mantiene siempre el origen ya elegido,
    /// aunque la búsqueda activa no coincida con él, para no perderlo al filtrar destinos.
    /// </summary>
    private void RebuildOriginOptions(List<Node> nodes)
    {
        Node previous = GetSelectedNode(originOptions, originDropdown);

        List<Node> source = new List<Node>(nodes ?? new List<Node>());
        if (previous != null && !source.Contains(previous)) source.Add(previous);

        List<string> labels;
        List<Node> map;
        BuildGroupedOptions(source, out labels, out map);

        originOptions = map;

        suppressCallbacks = true;
        if (originDropdown != null)
        {
            originDropdown.ClearOptions();
            originDropdown.AddOptions(labels);
        }
        suppressCallbacks = false;

        int index = previous != null ? originOptions.IndexOf(previous) : -1;
        if (index < 0) index = FirstNodeIndex(originOptions);

        if (originDropdown != null && index >= 0)
        {
            suppressCallbacks = true;
            originDropdown.value = index;
            originDropdown.RefreshShownValue();
            suppressCallbacks = false;
            lastOriginValue = index;
        }
    }

    private void RebuildDestinationOptions(List<Node> nodes)
    {
        List<string> labels;
        List<Node> map;
        BuildGroupedOptions(nodes, out labels, out map);

        Node previous = GetSelectedNode(destinationOptions, destinationDropdown);

        destinationOptions = map;

        suppressCallbacks = true;
        if (destinationDropdown != null)
        {
            destinationDropdown.ClearOptions();
            destinationDropdown.AddOptions(labels);
        }
        suppressCallbacks = false;

        int index = previous != null ? destinationOptions.IndexOf(previous) : -1;
        if (index < 0) index = FirstNodeIndex(destinationOptions);

        if (destinationDropdown != null && index >= 0)
        {
            suppressCallbacks = true;
            destinationDropdown.value = index;
            destinationDropdown.RefreshShownValue();
            suppressCallbacks = false;
            lastDestinationValue = index;
        }
    }

    private void SelectDefaults()
    {
        if (originDropdown != null)
        {
            int index = -1;

            // Preferir la entrada principal del Edificio Nuevo como punto de partida.
            for (int i = 0; i < originOptions.Count; i++)
            {
                Node node = originOptions[i];
                if (node == null || node.nodeType != "entrada") continue;
                if (node.buildingId == "nuevo" &&
                    node.EffectiveId.EndsWith("entrada_principal", StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                for (int i = 0; i < originOptions.Count; i++)
                {
                    Node node = originOptions[i];
                    if (node != null && node.nodeType == "entrada" && node.buildingId == "nuevo")
                    {
                        index = i;
                        break;
                    }
                }
            }

            if (index < 0) index = FirstNodeIndex(originOptions);

            if (index >= 0)
            {
                suppressCallbacks = true;
                originDropdown.value = index;
                originDropdown.RefreshShownValue();
                suppressCallbacks = false;
                lastOriginValue = index;
            }
        }

        if (destinationDropdown != null)
        {
            // Primer salón del Edificio Nuevo en el primer piso: destino de prueba útil.
            int index = -1;
            for (int i = 0; i < destinationOptions.Count; i++)
            {
                Node node = destinationOptions[i];
                if (node != null && node.buildingId == "nuevo" && node.floor == 1 && node.nodeType == "salon")
                {
                    index = i;
                    break;
                }
            }

            if (index < 0) index = FirstNodeIndex(destinationOptions);

            if (index >= 0)
            {
                suppressCallbacks = true;
                destinationDropdown.value = index;
                destinationDropdown.RefreshShownValue();
                suppressCallbacks = false;
                lastDestinationValue = index;
            }
        }
    }

    private int FirstNodeIndex(List<Node> options)
    {
        if (options == null) return -1;
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] != null) return i;
        }
        return -1;
    }

    private Node GetSelectedNode(List<Node> options, TMP_Dropdown dropdown)
    {
        if (options == null || dropdown == null) return null;
        int index = dropdown.value;
        if (index < 0 || index >= options.Count) return null;
        return options[index];
    }

    private void OnOriginDropdownChanged(int index)
    {
        if (suppressCallbacks) return;

        if (originOptions == null || index < 0 || index >= originOptions.Count || originOptions[index] == null)
        {
            // Se tocó un encabezado: volver a la última selección válida.
            if (lastOriginValue >= 0 && originDropdown != null)
            {
                suppressCallbacks = true;
                originDropdown.value = lastOriginValue;
                originDropdown.RefreshShownValue();
                suppressCallbacks = false;
            }
            return;
        }

        lastOriginValue = index;
    }

    private void OnDestinationDropdownChanged(int index)
    {
        if (suppressCallbacks) return;

        if (destinationOptions == null || index < 0 || index >= destinationOptions.Count || destinationOptions[index] == null)
        {
            if (lastDestinationValue >= 0 && destinationDropdown != null)
            {
                suppressCallbacks = true;
                destinationDropdown.value = lastDestinationValue;
                destinationDropdown.RefreshShownValue();
                suppressCallbacks = false;
            }
            return;
        }

        lastDestinationValue = index;
    }

    /// <summary>Filtro dinámico de búsqueda por nombre, id, edificio, piso o tipo.</summary>
    public void OnSearchValueChanged(string query)
    {
        string search = (query ?? string.Empty).Trim();
        if (search.StartsWith("SONEA:", StringComparison.OrdinalIgnoreCase))
        {
            search = search.Substring(6).Trim();
        }

        filteredNodes = listNodes
            .Where(n => string.IsNullOrEmpty(search) || Matches(n, search))
            .ToList();

        RebuildOriginOptions(filteredNodes);
        RebuildDestinationOptions(filteredNodes);

        SetStatus2D(string.IsNullOrEmpty(search)
            ? "Listo. " + listNodes.Count + " destinos en el campus."
            : filteredNodes.Count + " resultados para \"" + search + "\". Elige origen y destino en las listas.");
    }

    private bool Matches(Node node, string search)
    {
        if (node == null) return false;

        return Contains(node.DisplayName, search) ||
               Contains(node.EffectiveId, search) ||
               Contains(node.DropdownLabel, search) ||
               Contains(node.buildingName, search) ||
               Contains(node.nodeType, search) ||
               (node.floor > 0 && Contains("piso " + node.floor, search));
    }

    private bool Contains(string value, string search)
    {
        return !string.IsNullOrEmpty(value) &&
               value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Botón "Iniciar Ruta AR": calcula la ruta, oculta el mapa 2D y muestra el HUD.</summary>
    public void OnClickStartNavigation()
    {
        if (originDropdown == null || destinationDropdown == null)
        {
            SetStatus2D("Faltan los Dropdowns de origen y destino.");
            return;
        }

        Node startNode = GetSelectedNode(originOptions, originDropdown);
        Node targetNode = GetSelectedNode(destinationOptions, destinationDropdown);

        if (startNode == null || targetNode == null)
        {
            SetStatus2D("Selecciona un origen y un destino válidos (no los encabezados).");
            return;
        }

        if (startNode == targetNode)
        {
            SetStatus2D("El origen y el destino son el mismo punto.");
            return;
        }

        if (navigationManager == null)
        {
            SetStatus2D("No hay NavigationManager asignado en el Canvas.");
            return;
        }

        bool ok = navigationManager.TryStartNavigation(startNode, targetNode);

        if (!ok)
        {
            SetStatus2D("No se encontró ruta entre " + startNode.DisplayName + " y " + targetNode.DisplayName + ".");
            return;
        }

        if (panel2DMap != null) panel2DMap.SetActive(false);
        if (panelARHUD != null) panelARHUD.SetActive(true);

        SetStatus("Ruta: " + startNode.DisplayName + " → " + targetNode.DisplayName +
                  "\nAlinea el teléfono con el pasillo y camina. Toca el suelo para recolocar.");
    }

    /// <summary>Botón opcional para detener la guía AR.</summary>
    public void OnClickStopNavigation()
    {
        if (navigationManager != null) navigationManager.StopNavigation();
        if (panelARHUD != null) panelARHUD.SetActive(false);
        if (panel2DMap != null) panel2DMap.SetActive(true);
        SetStatus2D("Navegación detenida.");
    }

    /// <summary>Valida que todos los destinos sean alcanzables y muestra el resumen.</summary>
    public void OnClickValidateGraph()
    {
        if (graphManager == null)
        {
            SetStatus2D("No hay CampusGraphManager asignado.");
            return;
        }

        string report;
        bool ok = graphManager.ValidateGraph(out report);
        Debug.Log("[CampusUIController] Validación del grafo:\n" + report);

        string[] lines = report.Split('\n');
        string summary = string.Empty;
        for (int i = 0; i < lines.Length && i < 5; i++)
        {
            if (string.IsNullOrEmpty(lines[i].Trim())) continue;
            summary += (summary.Length > 0 ? "\n" : string.Empty) + lines[i].Trim();
        }

        SetStatus2D((ok ? "GRAFO OK\n" : "GRAFO CON ERRORES\n") + summary);
    }

    public void OnClickRotateLeft()
    {
        if (campusPlacement != null) campusPlacement.RotateMap(-15f);
    }

    public void OnClickRotateRight()
    {
        if (campusPlacement != null) campusPlacement.RotateMap(15f);
    }

    public void OnClickAlignMap()
    {
        if (campusPlacement != null) campusPlacement.AlignToCameraForward();
    }

    /// <summary>Escribe un mensaje de estado en el HUD AR.</summary>
    public void SetStatus(string message)
    {
        if (statusLabel != null) statusLabel.text = message;
        else Debug.Log("[CampusUIController] " + message);
    }

    /// <summary>Escribe un mensaje de estado en el panel 2D.</summary>
    public void SetStatus2D(string message)
    {
        if (status2DLabel != null) status2DLabel.text = message;
        else Debug.Log("[CampusUIController] " + message);
    }
}
