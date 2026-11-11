using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Banner superior estilo Apple Maps: "Navegando a X" + instrucción grande con el
/// icono de la próxima maniobra. Se alimenta de <see cref="NavigationManager"/>.
/// </summary>
public class NavigationBanner : MonoBehaviour
{
    [Header("Referencias")]
    public NavigationManager navigationManager;

    [Header("Textos")]
    public TMP_Text titleText;
    public TMP_Text instructionText;

    [Header("Icono de maniobra")]
    public RawImage iconImage;
    public Texture2D iconStraight;
    public Texture2D iconLeft;
    public Texture2D iconRight;
    public Texture2D iconUp;
    public Texture2D iconDown;
    public Texture2D iconArrive;

    [Header("Frecuencia")]
    public float refreshInterval = 0.2f;

    private float timer;
    private NavigationManager.ManeuverType lastManeuver = NavigationManager.ManeuverType.None;
    private bool hasManeuver = false;

    void Start()
    {
        Resolve();
        Refresh();
    }

    void Update()
    {
        if (navigationManager == null) Resolve();

        timer += Time.unscaledDeltaTime;
        if (timer < refreshInterval) return;

        timer = 0f;
        Refresh();
    }

    private void Resolve()
    {
        if (navigationManager == null) navigationManager = FindAnyObjectByType<NavigationManager>();
    }

    /// <summary>Actualiza textos e icono con el estado actual de la navegación.</summary>
    public void Refresh()
    {
        if (navigationManager == null) return;

        if (titleText != null) titleText.text = navigationManager.GetBannerTitle();
        if (instructionText != null) instructionText.text = navigationManager.GetBannerInstruction();

        NavigationManager.ManeuverType maneuver = navigationManager.GetManeuver();
        if (!hasManeuver || maneuver != lastManeuver)
        {
            hasManeuver = true;
            lastManeuver = maneuver;
            if (iconImage != null) iconImage.texture = IconFor(maneuver);
        }
    }

    private Texture2D IconFor(NavigationManager.ManeuverType maneuver)
    {
        switch (maneuver)
        {
            case NavigationManager.ManeuverType.Left: return iconLeft;
            case NavigationManager.ManeuverType.Right: return iconRight;
            case NavigationManager.ManeuverType.Up: return iconUp;
            case NavigationManager.ManeuverType.Down: return iconDown;
            case NavigationManager.ManeuverType.Arrive: return iconArrive;
            default: return iconStraight;
        }
    }
}
