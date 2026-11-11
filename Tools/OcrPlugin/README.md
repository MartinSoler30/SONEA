# Plugin OCR de SONEA

Lector de letreros de puerta con **ML Kit Text Recognition v2**. Todo el
procesamiento ocurre en el dispositivo; no se envia ninguna imagen a internet.

## Compilar el AAR

Requisitos:

- Android Studio (o un JDK 17/21 y el Android SDK).
- Gradle 8.9 o superior.

```powershell
$env:JAVA_HOME = "C:\Program Files\Android\Android Studio\jbr"
$env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"
$gradle = "ruta\a\gradle-8.9\bin\gradle.bat"

cd Tools\OcrPlugin
& $gradle :ocr:assembleRelease

Copy-Item ocr\build\outputs\aar\ocr-release.aar `
  ..\..\Assets\Plugins\Android\sonea-ocr.aar -Force
```

El AAR solo contiene la clase Java `DoorSignOcr`. La dependencia de ML Kit se
declara en `Assets/Plugins/Android/mainTemplate.gradle`; Unity la resuelve al
compilar el APK.

## Uso desde Unity

`DoorSignReader` captura un frame de `ARCameraManager`, lo convierte a ARGB y
llama a `DoorSignOcr.recognizeAsync`. El resultado llega como JSON:

```json
{
  "text": "EN105",
  "lines": [
    { "text": "EN105", "left": 120, "top": 340, "right": 420, "bottom": 390 }
  ]
}
```

Si ML Kit no puede procesar el frame devuelve `{"error":"..."}`.
