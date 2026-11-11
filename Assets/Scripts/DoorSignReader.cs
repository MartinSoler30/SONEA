using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Captura un frame de la camara AR y lo envia al plugin Android de ML Kit
/// para leer el letrero de una puerta. La imagen nunca sale del telefono.
/// </summary>
public class DoorSignReader : MonoBehaviour
{
    [Header("Camara AR")]
    [Tooltip("Camara de AR Foundation. Si se deja vacia se busca automaticamente.")]
    public ARCameraManager cameraManager;

    [Tooltip("Ancho maximo del frame que se envia al OCR. Menos ancho = mas rapido.")]
    public int maxWidth = 960;

    [Tooltip("Milisegundos maximos que se espera una respuesta antes de avisar.")]
    public int timeoutMilliseconds = 6000;

    private AndroidJavaClass ocrClass;
    private int[] pixels;
    private int pixelWidth;
    private int pixelHeight;
    private bool busy;
    private OcrCallbackProxy activeProxy;
    private float requestStartedAt = -1f;
    private Action<string> pendingCallback;

    public bool IsBusy
    {
        get { return busy; }
    }

    public bool HasFrame
    {
        get { return pixels != null && pixels.Length > 0; }
    }

    void Awake()
    {
        if (cameraManager == null) cameraManager = FindAnyObjectByType<ARCameraManager>();
    }

    void Update()
    {
        if (!busy || pendingCallback == null) return;

        if (Time.unscaledTime - requestStartedAt > timeoutMilliseconds / 1000f)
        {
            FinishWithError("El OCR tardo demasiado. Acercate al letrero e intentalo otra vez.");
        }
    }

    /// <summary>
    /// Toma el ultimo frame de ARCore y lo deja listo para reconocer.
    /// Devuelve false si la camara aun no tiene imagen.
    /// </summary>
    public bool CaptureFrame()
    {
        if (cameraManager == null) cameraManager = FindAnyObjectByType<ARCameraManager>();
        if (cameraManager == null)
        {
            Debug.LogWarning("[DoorSignReader] No hay ARCameraManager en la escena.");
            return false;
        }

        if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
        {
            Debug.LogWarning("[DoorSignReader] ARCore aun no entrega frames.");
            return false;
        }

        try
        {
            float scale = Mathf.Min(1f, maxWidth / (float)image.width);
            int width = Mathf.Max(1, Mathf.RoundToInt(image.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(image.height * scale));

            XRCpuImage.ConversionParams parameters = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, image.width, image.height),
                outputDimensions = new Vector2Int(width, height),
                outputFormat = TextureFormat.RGBA32,
                transformation = XRCpuImage.Transformation.None
            };

            int byteCount = width * height * 4;
            using (NativeArray<byte> data = new NativeArray<byte>(byteCount, Allocator.Temp))
            {
                image.Convert(parameters, data);

                if (pixels == null || pixels.Length != width * height)
                {
                    pixels = new int[width * height];
                }

                for (int i = 0; i < pixels.Length; i++)
                {
                    int offset = i * 4;
                    int r = data[offset];
                    int g = data[offset + 1];
                    int b = data[offset + 2];
                    int a = data[offset + 3];
                    pixels[i] = (a << 24) | (r << 16) | (g << 8) | b;
                }

                pixelWidth = width;
                pixelHeight = height;
            }

            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[DoorSignReader] No se pudo convertir el frame: " + exception.Message);
            pixels = null;
            return false;
        }
        finally
        {
            image.Dispose();
        }
    }

    /// <summary>
    /// Reconoce el texto del frame capturado con el giro indicado.
    /// El callback se invoca en el hilo principal con el JSON del plugin.
    /// </summary>
    public void Recognize(int rotationDegrees, Action<string> onResult)
    {
        if (onResult == null) return;

        if (busy)
        {
            onResult("{\"error\":\"Ya hay una lectura en curso\"}");
            return;
        }

        if (!HasFrame)
        {
            onResult("{\"error\":\"No hay frame capturado\"}");
            return;
        }

        if (Application.platform != RuntimePlatform.Android)
        {
            onResult("{\"error\":\"El OCR solo funciona en Android\"}");
            return;
        }

        try
        {
            if (ocrClass == null)
            {
                ocrClass = new AndroidJavaClass("com.etitc.sonea.ocr.DoorSignOcr");
            }

            busy = true;
            requestStartedAt = Time.unscaledTime;
            pendingCallback = onResult;
            activeProxy = new OcrCallbackProxy(OnPluginResult);

            ocrClass.CallStatic("recognizeAsync", pixels, pixelWidth, pixelHeight,
                rotationDegrees, activeProxy);
        }
        catch (Exception exception)
        {
            busy = false;
            pendingCallback = null;
            activeProxy = null;
            Debug.LogError("[DoorSignReader] Error llamando al plugin OCR: " + exception);
            onResult("{\"error\":\"No se pudo cargar el lector de letreros\"}");
        }
    }

    /// <summary>Libera el frame capturado.</summary>
    public void ClearFrame()
    {
        pixels = null;
        pixelWidth = 0;
        pixelHeight = 0;
    }

    private void OnPluginResult(string json)
    {
        Action<string> callback = pendingCallback;
        pendingCallback = null;
        activeProxy = null;
        busy = false;

        if (callback != null) callback(json);
    }

    private void FinishWithError(string message)
    {
        Action<string> callback = pendingCallback;
        pendingCallback = null;
        activeProxy = null;
        busy = false;

        if (callback != null)
        {
            callback("{\"error\":\"" + message.Replace("\"", "\\\"") + "\"}");
        }
    }

    private class OcrCallbackProxy : AndroidJavaProxy
    {
        private readonly Action<string> callback;

        public OcrCallbackProxy(Action<string> callback)
            : base("com.etitc.sonea.ocr.DoorSignOcr$OcrCallback")
        {
            this.callback = callback;
        }

        // El nombre debe coincidir con el metodo de la interfaz Java.
        public void onResult(string json)
        {
            if (callback != null) callback(json);
        }
    }
}
