# SONEA — Navegación AR del campus ETITC

Aplicación Android de realidad aumentada que guía a estudiantes, docentes y visitantes dentro del campus de la **Escuela Tecnológica Instituto Técnico Central (ETITC)**, en Bogotá. La app calcula la ruta más corta sobre un grafo en metros reales y la proyecta sobre el piso con una flecha plana estilo Apple Maps, acompañada de un banner con la próxima maniobra.

![Unity](https://img.shields.io/badge/Unity-6000.6.4f1-000?logo=unity)
![Android](https://img.shields.io/badge/Android-minSdk%2026-3DDC84?logo=android)
![ARCore](https://img.shields.io/badge/AR%20Foundation-6%20%2B%20ARCore-4285F4)

## Estado del proyecto

- **Geometría real** medida sobre el KML del campus (Google Earth), no estimada.
- **175 nodos** navegables: 62 espacios interiores, 4 entradas de edificio y 14 puntos exteriores.
- **88 destinos** visibles en la lista (salones, oficinas, baños, biblioteca, escaleras, ascensores, entradas, cafetería, canchas y parqueaderos).
- **Edificio Nuevo** (4 pisos) y **Edificio Modulares** (2 pisos) conectados por el exterior.
- Un **código QR por espacio** en [`docs/qr/`](docs/qr/), con payload `SONEA:<id>`.
- Navegación desde cualquier punto: no exige empezar en una entrada.

## Características

| Función | Descripción |
|---|---|
| Búsqueda 2D | Buscador con filtro de texto y dos listas agrupadas por edificio y piso. |
| Inicio libre | El origen puede ser cualquier espacio o entrada; también se puede fijar escaneando un QR. |
| Ruta más corta | Dijkstra sobre un grafo único en metros reales (X este, Z norte, Y arriba). |
| Flecha decal | Quad plano apoyado en la polilínea, 2,5–4 m por delante, con borde y halo blancos. |
| Estados de la flecha | Azul en ruta, ámbar con desvío > 4 m, verde a menos de 5 m del destino. |
| Banner superior | Tarjeta oscura «Navegando a X» + instrucción grande e icono de maniobra. |
| Cambio de piso | Instrucciones de subir/bajar por escalera o ascensor; la flecha se oculta en el tramo vertical. |
| Re-ruteo | Si el usuario se desvía más de 4 m durante 3 s, se recalcula la ruta (con 6 s de enfriamiento). |
| QR | 90 etiquetas imprimibles con manifiesto CSV y hojas HTML por piso. |

## Requisitos

- **Unity 6000.6.4f1** con los módulos *Android Build Support*, *Android SDK & NDK Tools* y *OpenJDK*.
- **AR Foundation 6** + **ARCore XR Plugin** (incluidos en `Packages/manifest.json`).
- Dispositivo Android con **ARCore**, Android 8.0 (API 26) o superior y arquitectura ARM64.
- Para compilar desde consola: `adb` y el editor de Unity en la ruta habitual.

## Inicio rápido

1. Abrir el proyecto en Unity Hub (versión 6000.6.4f1).
2. Menú **ETITC Tools → Regenerar Escena y Verificar Grafo** para reconstruir la escena desde `Assets/Resources/CampusGraph.json`.
3. Menú **ETITC Tools → Generar y Compilar APK Android** (o el método batch `CampusGraphAutoBuilder.RegenerateAndBuildAndroid`).
4. Instalar en el teléfono:

```powershell
adb install -r Builds\SONEA.apk
```

Compilación por consola:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Unity.exe" `
  -batchmode -quit -projectPath "C:\Users\USUARIO\SONEA" `
  -executeMethod CampusGraphAutoBuilder.RegenerateAndBuildAndroid `
  -logFile build-android.log
```

## Cómo se usa

1. La app abre en el panel 2D con el origen preseleccionado en la **entrada principal del Edificio Nuevo**.
2. Elige el destino con el buscador o las listas; también puedes escanear un QR para fijar el origen.
3. Pulsa **Iniciar Ruta AR**: el panel 2D se oculta y se abre la cámara.
4. Apunta al piso y toca la retícula para anclar el mapa (o deja que el origen se coloque a tus pies).
5. Camina siguiendo la flecha azul y las instrucciones del banner. Al llegar, la flecha se pone verde y se desvanece.

## Sistema de coordenadas

Origen en la **entrada principal del Edificio Nuevo**. Unidades en metros.

- **X** → este
- **Z** → norte
- **Y** → arriba (altura de piso)

| Edificio | Origen (X, Z) | Giro | Largo × ancho | Corredor |
|---|---|---|---|---|
| Nuevo | (−44,5; −11,3) | +1,8° | 44,1 × 24,8 m (planta en L) | Z = 12,7; X 4,0–42,0 |
| Modulares | (−106,2; 77,0) | −30,8° | 94,3 × 16,0 m | Z = 8,0; X 2,0–92,3 |

Detalle completo, entradas y puntos exteriores en [`docs/geometria-y-coordenadas.md`](docs/geometria-y-coordenadas.md).

## Estructura del repositorio

```
Assets/
  Resources/CampusGraph.json      Fuente de verdad del grafo (geometría + conexiones)
  Scenes/Main.unity               Escena generada por el builder
  Scripts/
    CampusGraphData.cs            DTOs y validación del JSON
    CampusGraphManager.cs         Registro de nodos en escena
    DijkstraPathfinder.cs         Ruta más corta
    NavigationManager.cs          Orquestación, re-ruteo y maniobras
    ARArrowGuide.cs               Flecha decal sobre la polilínea
    NavigationBanner.cs           Banner superior con icono de maniobra
    ARPathVisualizer.cs           Línea de ruta
    ARCampusPlacement.cs          Anclaje AR y retícula
    CampusUIController.cs         Buscador, listas y paneles
    NavigationDebugHUD.cs         Diagnóstico de campo
    Editor/
      CampusGraphAutoBuilder.cs   Genera grafo, escena, UI y APK
      NavigationTextureGenerator.cs  Texturas SDF de flecha e iconos
      QRCodeGenerator.cs          QR, manifiesto y hojas imprimibles
docs/
  arquitectura.md                 Componentes y flujo de datos
  geometria-y-coordenadas.md      Medidas, orígenes y conversión KML
  grafo.md                        Esquema, nodos, aristas y rutas de prueba
  pruebas.md                      Plan de pruebas incrementales
  qr.md                           Etiquetas QR y cómo regenerarlas
  qr/                             90 PNG + manifiesto.csv + hojas HTML
```

## Documentación

- [Arquitectura](docs/arquitectura.md)
- [Geometría y coordenadas](docs/geometria-y-coordenadas.md)
- [Grafo de navegación](docs/grafo.md)
- [Plan de pruebas](docs/pruebas.md)
- [Códigos QR](docs/qr.md)

## Pruebas

La validación avanza en tres fases: **piso → edificio → campus completo**. Cada fase se prueba primero en el teléfono del autor y luego en el de un compañero. El detalle y la lista de verificación están en [`docs/pruebas.md`](docs/pruebas.md).

Validación automática del grafo (editor o batch):

```
CampusGraphAutoBuilder.RegenerateAndVerify
```

Resultado esperado: 175 nodos, 175 alcanzables y rutas de referencia con distancias estables (por ejemplo EN101 → BIBLIOTECA = 45,2 m; EN101 → M219 = 247,7 m).

## Limitaciones conocidas

- El orden exacto de algunos salones y puertas interiores se distribuyó sobre la longitud medida; no sustituye un levantamiento puerta por puerta.
- La app no usa VLM continuo ni Depth/Scene Semantics: el grafo es la fuente de verdad y la IA solo asiste en la redacción de instrucciones.
- El APK no se versiona en git; se publica como *Release*.
- La precisión del anclaje depende del tracking de ARCore y de la luz del entorno.

## Créditos

Proyecto académico de la **ETITC**. Nombre de producto: **SONEA**. Package: `com.ETITC.SONEA`.
