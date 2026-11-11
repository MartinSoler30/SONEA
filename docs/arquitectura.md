# Arquitectura

SONEA es una app de Unity 6 para Android con AR Foundation + ARCore. La escena se genera por código desde el grafo, de modo que la geometría y la topología siempre salen del JSON y no de ajustes manuales en la escena.

## Flujo general

```
CampusGraph.json
      │
      ▼
CampusGraphData (DTO + validación)
      │
      ▼
CampusGraphAutoBuilder (editor)
      │  crea nodos, aristas, pisos, UI, HUD, AR rig
      ▼
Main.unity
      │
      ├── CampusGraphManager  ── registro de nodos
      ├── NavigationManager   ── Dijkstra + re-ruteo + maniobras
      ├── ARPathVisualizer    ── línea de ruta
      ├── ARArrowGuide        ── flecha decal
      ├── NavigationBanner    ── banner superior
      └── CampusUIController  ── buscador, listas y paneles
```

## Componentes

### Datos y grafo

| Script | Responsabilidad |
|---|---|
| `CampusGraphData.cs` | DTOs (`CampusBuilding`, `CampusFloor`, `CampusRoom`, `CampusEntrance`, `CampusExterior`), carga desde `Resources` y validación de ids duplicados. |
| `CampusGraphManager.cs` | Lista de todos los `Node` de la escena, búsqueda por id, por nombre y por edificio/piso. |
| `Node.cs` | Metadatos de cada punto (id, edificio, piso, tipo, `showInList`) y aristas salientes. |
| `DijkstraPathfinder.cs` | Ruta más corta con cola de prioridad; devuelve la lista de nodos. |

### Navegación

| Script | Responsabilidad |
|---|---|
| `NavigationManager.cs` | Orquesta el inicio/fin de navegación, calcula la ruta, detecta el piso activo, mide el desvío, re-rutea y expone las maniobras del banner. |
| `ARPathVisualizer.cs` | Dibuja la polilínea con `LineRenderer` (0,15 m de ancho) a 2 cm del piso, en cian. |
| `ARArrowGuide.cs` | Coloca y orienta la flecha decal sobre el tramo actual. |
| `NavigationBanner.cs` | Actualiza cada 0,2 s el título, la instrucción y el icono de la maniobra. |
| `ARCampusPlacement.cs` | Ancla el mapa al piso detectado, lo orienta con la brújula (offset 0°) y coloca el origen a los pies del usuario. |
| `NavigationDebugHUD.cs` | FPS, tracking, piso activo, nodo más cercano, distancia restante y desvío. |
| `CampusUIController.cs` | Llena las listas por edificio/piso, filtra por texto, acepta `SONEA:<id>` y alterna entre panel 2D y HUD AR. |

### Editor

| Script | Responsabilidad |
|---|---|
| `CampusGraphAutoBuilder.cs` | Genera el grafo, los pisos, la UI, el rig AR, configura el player de Android, compila el APK y verifica la escena. |
| `NavigationTextureGenerator.cs` | Genera por SDF las texturas de la flecha (azul, ámbar, verde) y los iconos de maniobra. |
| `QRCodeGenerator.cs` | Genera 90 QR, el manifiesto CSV y las hojas HTML imprimibles. |

## Flecha decal

La flecha es un **quad de 1 × 1 m** en el plano XZ con una textura de 512 × 512 que incluye:

- Relleno azul (o ámbar/verde) con degradado sutil.
- Borde blanco de 9 px.
- Halo blanco de 26 px con caída suave.

El material es **URP/Unlit transparente**, `ZWrite Off`, `Cull Off`, render queue 3000 y blending `SrcAlpha / OneMinusSrcAlpha`. Se apoya a 2 cm sobre el piso para evitar z-fighting.

Cada frame, `ARArrowGuide`:

1. Proyecta la posición del usuario sobre el segmento actual de la polilínea.
2. Coloca la flecha entre **2,5 y 4 m** por delante; si el vértice del giro está más cerca, la coloca en el vértice.
3. Orienta la flecha con la dirección del tramo; en un giro, apunta hacia el tramo siguiente.
4. Escala entre 0,75× y 1,5× según la distancia, con un pulso suave del 4 %.
5. Cambia a ámbar si el desvío supera 4 m, a verde si faltan menos de 5 m y se desvanece entre 1,5 y 0,5 m del destino.
6. Se oculta cuando el tramo es vertical (escalera o ascensor), porque el decal no pertenece al piso del usuario.

## Banner superior

Es una tarjeta anclada arriba, con fondo negro al 88 %, esquinas redondeadas (`UISprite` sliced, `pixelsPerUnitMultiplier 2.5`) y dos textos:

- **Título:** `Navegando a <destino>`.
- **Instrucción:** grande y en negrita, con el icono de la maniobra a la izquierda.

Las maniobras se calculan con el ángulo entre el tramo entrante y el saliente (`turnAngleThreshold = 35°`):

| Situación | Texto |
|---|---|
| Recto | «Continúa 18 m» / «Sigue recto» |
| Giro | «En 8 m gira a la izquierda» |
| Cambio de piso | «Sube al piso 2 por la escalera» |
| Salida | «Sal al exterior · 5 m» |
| Llegada | «Has llegado a EN105» |

## Anclaje AR

`ARCampusPlacement` usa `ARRaycastManager` para proyectar la retícula sobre el piso y `ARPlaneManager` para detectar planos. El usuario puede:

- Tocar la pantalla para fijar el origen en el punto tocado.
- Dejar que el origen se coloque a sus pies (cámara − 1,6 m).

El mapa se orienta con la brújula del dispositivo (`headingOffsetDegrees = 0`), de modo que el norte del grafo coincide con el norte real. La colocación solo está activa durante la navegación.

## Re-ruteo

`NavigationManager` mide la distancia del usuario a la polilínea:

- Desvío > **4 m** durante **3 s** → recalcula desde el nodo más cercano.
- Enfriamiento de **6 s** para no re-rutear en bucle.
- El destino se conserva y la flecha se reposiciona sobre la nueva ruta.

## Qué NO hace la app

- No usa un modelo de lenguaje para decidir la ruta: la ruta siempre sale del grafo y de Dijkstra.
- No usa Depth API ni Scene Semantics.
- No hace reconocimiento visual continuo (VLM); la cámara solo muestra el entorno y el decal.
- No inventa puertas: las entradas salen del JSON medido.

## Configuración de Android

| Ajuste | Valor |
|---|---|
| Package | `com.ETITC.SONEA` |
| Versión mínima | API 26 (Android 8.0) |
| Arquitectura | ARM64 |
| Scripting backend | IL2CPP |
| Graphics API | OpenGLES3 |
| Orientación | Retrato |
| Resolución de referencia | 1080 × 1920 |
| XR | ARCore (AR Foundation 6) |
| Escena | `Assets/Scenes/Main.unity` |
