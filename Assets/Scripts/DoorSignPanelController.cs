using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Flujo de "Leer letrero": oculta el panel 2D, muestra la camara AR, captura
/// un frame, ejecuta el OCR y ofrece los candidatos del grafo para fijar el origen.
/// </summary>
public class DoorSignPanelController : MonoBehaviour
{
    [Header("Paneles")]
    public GameObject panel2DMap;
    public GameObject panelARHUD;
    public GameObject panelSignScan;

    [Header("Componentes")]
    public DoorSignReader reader;
    public CampusUIController uiController;
    public CampusGraphManager graphManager;

    [Header("Textos")]
    public TMP_Text scanStatus;
    public TMP_Text scanDetail;

    [Header("Botones")]
    public Button captureButton;
    public Button cancelButton;
    public Button[] candidateButtons = new Button[3];

    [Tooltip("Puntuacion minima para mostrar un candidato.")]
    public float minCandidateScore = 0.60f;

    private static readonly int[] Rotations = { 90, 270, 0, 180 };

    private readonly List<RoomSignMatcher.SignMatch> matches =
        new List<RoomSignMatcher.SignMatch>();

    private int rotationIndex;
    private string lastText = string.Empty;
    private bool busy;

    void Awake()
    {
        if (reader == null) reader = FindAnyObjectByType<DoorSignReader>();
        if (uiController == null) uiController = FindAnyObjectByType<CampusUIController>();
        if (graphManager == null) graphManager = FindAnyObjectByType<CampusGraphManager>();
        HideCandidates();
    }

    /// <summary>Abre el modo de lectura del letrero.</summary>
    public void Open()
    {
        if (panelSignScan == null)
        {
            if (uiController != null) uiController.SetStatus2D("No esta configurado el panel de lectura.");
            return;
        }

        busy = false;
        rotationIndex = 0;
        lastText = string.Empty;
        matches.Clear();
        HideCandidates();

        if (panel2DMap != null) panel2DMap.SetActive(false);
        if (panelARHUD != null) panelARHUD.SetActive(false);
        panelSignScan.SetActive(true);

        if (scanStatus != null) scanStatus.text = "Apunta al letrero de la puerta";
        if (scanDetail != null) scanDetail.text = "Acercate a 1-3 m y procura que el texto quede centrado.";
        SetCaptureLabel("Capturar");
    }

    /// <summary>Toma una foto del frame actual y empieza la lectura.</summary>
    public void Capture()
    {
        if (busy) return;

        if (reader == null) reader = FindAnyObjectByType<DoorSignReader>();
        if (reader == null)
        {
            ShowError("No se encontro el lector de letreros.");
            return;
        }

        if (!reader.CaptureFrame())
        {
            ShowError("La camara aun no esta lista. Espera un segundo y vuelve a intentar.");
            return;
        }

        busy = true;
        matches.Clear();
        HideCandidates();
        if (scanStatus != null) scanStatus.text = "Leyendo letrero...";
        if (scanDetail != null) scanDetail.text = "Manten el telefono apuntando al texto.";
        SetCaptureLabel("Leyendo...");
        rotationIndex = 0;
        TryNextRotation();
    }

    /// <summary>Cierra el modo de lectura sin cambiar el origen.</summary>
    public void Cancel()
    {
        busy = false;
        rotationIndex = Rotations.Length;
        if (reader != null) reader.ClearFrame();
        ClosePanel();

        if (uiController != null) uiController.SetStatus2D("Lectura cancelada. Puedes elegir el origen en la lista.");
    }

    private void TryNextRotation()
    {
        if (rotationIndex >= Rotations.Length)
        {
            NoMatch();
            return;
        }

        int rotation = Rotations[rotationIndex++];
        reader.Recognize(rotation, OnOcrJson);
    }

    private void OnOcrJson(string json)
    {
        if (!busy) return;

        OcrPayload payload = null;
        try
        {
            payload = JsonUtility.FromJson<OcrPayload>(json);
        }
        catch (Exception)
        {
            payload = null;
        }

        if (payload == null || !string.IsNullOrEmpty(payload.error))
        {
            TryNextRotation();
            return;
        }

        string text = payload.text != null ? payload.text.Trim() : string.Empty;
        if (text.Length > 0) lastText = text;

        Node prior = uiController != null ? uiController.SelectedOrigin : null;
        List<RoomSignMatcher.SignMatch> found = RoomSignMatcher.Match(
            text, graphManager != null ? graphManager.AllNodes : null, prior,
            candidateButtons != null ? candidateButtons.Length : 3, minCandidateScore);

        if (found.Count == 0)
        {
            TryNextRotation();
            return;
        }

        matches.Clear();
        matches.AddRange(found);
        ShowCandidates(text);
    }

    private void ShowCandidates(string text)
    {
        busy = false;
        if (reader != null) reader.ClearFrame();

        if (scanStatus != null)
        {
            scanStatus.text = string.IsNullOrEmpty(text)
                ? "Letrero detectado"
                : "Letrero: \"" + Shorten(text, 70) + "\"";
        }

        if (scanDetail != null)
        {
            scanDetail.text = matches.Count == 1
                ? "Elige el origen correcto:"
                : "Hay varias coincidencias. Elige el origen correcto:";
        }

        int count = candidateButtons != null ? candidateButtons.Length : 0;
        for (int i = 0; i < count; i++)
        {
            Button button = candidateButtons[i];
            if (button == null) continue;

            if (i >= matches.Count)
            {
                button.gameObject.SetActive(false);
                continue;
            }

            RoomSignMatcher.SignMatch match = matches[i];
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.text = match.node.DisplayName + "  ·  " +
                             Mathf.RoundToInt(match.score * 100f) + " %";
            }

            int capturedIndex = i;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => UseCandidate(capturedIndex));
            button.gameObject.SetActive(true);
        }

        SetCaptureLabel("Reintentar");
    }

    private void NoMatch()
    {
        busy = false;
        if (reader != null) reader.ClearFrame();
        HideCandidates();

        if (scanStatus != null) scanStatus.text = "No reconoci el salon";
        if (scanDetail != null)
        {
            scanDetail.text = string.IsNullOrEmpty(lastText)
                ? "Acercate mas al letrero, evita reflejos y vuelve a intentar."
                : "Texto leido: \"" + Shorten(lastText, 80) + "\". Intenta mas cerca o usa el QR.";
        }

        SetCaptureLabel("Reintentar");
    }

    private void UseCandidate(int index)
    {
        if (index < 0 || index >= matches.Count) return;

        RoomSignMatcher.SignMatch match = matches[index];
        bool ok = uiController != null && uiController.SelectOrigin(match.node);

        if (!ok)
        {
            ShowError("No se pudo fijar ese origen. Elegelo manualmente en la lista.");
            return;
        }

        ClosePanel();
        if (uiController != null)
        {
            uiController.SetStatus2D("Letrero detectado: origen " + match.node.DisplayName +
                                     ". Revisa la lista y pulsa Iniciar Ruta AR.");
        }
    }

    private void ClosePanel()
    {
        if (panelSignScan != null) panelSignScan.SetActive(false);
        if (panel2DMap != null) panel2DMap.SetActive(true);
        if (panelARHUD != null) panelARHUD.SetActive(false);
    }

    private void ShowError(string message)
    {
        busy = false;
        if (scanStatus != null) scanStatus.text = "No se pudo leer el letrero";
        if (scanDetail != null) scanDetail.text = message;
        SetCaptureLabel("Reintentar");
    }

    private void HideCandidates()
    {
        if (candidateButtons == null) return;
        for (int i = 0; i < candidateButtons.Length; i++)
        {
            if (candidateButtons[i] != null) candidateButtons[i].gameObject.SetActive(false);
        }
    }

    private void SetCaptureLabel(string value)
    {
        if (captureButton == null) return;
        TMP_Text label = captureButton.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = value;
    }

    private static string Shorten(string value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        string singleLine = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return singleLine.Length <= max ? singleLine : singleLine.Substring(0, max) + "...";
    }

    [Serializable]
    private class OcrPayload
    {
        public string text;
        public string error;
    }
}
