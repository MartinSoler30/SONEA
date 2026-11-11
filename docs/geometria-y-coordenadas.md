# Geometría y coordenadas del campus

La geometría se midió sobre el KML del campus (`UPK map project`) en Google Earth. Todas las distancias están en **metros reales** y el grafo usa un único sistema de coordenadas.

## Marco de referencia

- **Origen (0, 0):** entrada principal del Edificio Nuevo.
- **X:** este.
- **Z:** norte.
- **Y:** arriba; la altura de cada piso es `floorHeight` (3,5 m en el Nuevo, 3,0 m en Modulares).
- **Unidad:** 1 unidad de Unity = 1 metro.

Cada edificio define un origen, un giro y un rectángulo local:

- `x` local recorre el largo del edificio (0 → `length`).
- `z` local recorre el ancho (0 → `width`); el corredor está en `corridorZ`.
- El giro se aplica alrededor del origen del edificio.

Conversión de coordenadas locales a mundo:

```
world.x = origin.x + x·cos(θ) + z·sin(θ)
world.z = origin.z − x·sin(θ) + z·cos(θ)
```

con `θ = rotationDegrees` en radianes. La inversa se usa para ordenar los nodos del corredor:

```
local.x = (world.x − origin.x)·cos(θ) − (world.z − origin.z)·sin(θ)
```

## Edificios

| Edificio | id | Origen (X, Z) | Giro | Largo | Ancho | Corredor Z | Corredor X | Hueco de puerta |
|---|---|---|---|---|---|---|---|---|
| Edificio Nuevo | `nuevo` | (−44,5; −11,3) | +1,8° | 44,1 m | 24,8 m | 12,7 m | 4,0 – 42,0 m | 2,5 m |
| Edificio Modulares | `modulares` | (−106,2; 77,0) | −30,8° | 94,3 m | 16,0 m | 8,0 m | 2,0 – 92,3 m | 2,2 m |

### Edificio Nuevo

- Planta en L: el rectángulo de 44,1 × 24,8 m tiene un recorte de aproximadamente 3,1 × 13,9 m en la esquina noroeste.
- Corredor recto de extremo a extremo, a `Z = 12,7` local.
- **Escalera:** (4,2; 9,8) local, al oeste.
- **Ascensor:** (6,6; 15,9) local.
- Pisos: 4 (altura 3,5 m).
- **Entrada principal:** (44,0; 12,7) local → **(0,0)** en mundo, fachada este.
- **Entrada secundaria:** (11,9; 22,4) local → **(−31,9; 10,7)** en mundo, fachada norte.

### Edificio Modulares

- Rectángulo de 94,3 × 16,0 m, girado −30,8°.
- Corredor a `Z = 8,0` local, de X = 2,0 a X = 92,3.
- Salones a ambos lados del corredor.
- **Escalera:** (3,2; 4,2) local, al suroeste.
- Sin ascensor.
- Pisos: 2 (altura 3,0 m).
- **Entrada cafetería:** (45,5; 1,2) local → **(−67,7; 101,3)** en mundo, fachada sur.
- **Entrada noreste:** (93,3; 3,9) local → **(−28,1; 128,1)** en mundo, fachada este.

## Puntos exteriores

Coordenadas directamente en el marco del campus:

| id | Nombre | X | Z | Tipo |
|---|---|---|---|---|
| `ext_nuevo_principal` | Entrada principal Edificio Nuevo | 0,0 | 0,0 | entrada |
| `ext_nuevo_secundaria` | Entrada secundaria Edificio Nuevo | −31,9 | 10,7 | entrada |
| `ext_motos` | Parqueadero de motos | −8,7 | 29,4 | parqueadero |
| `ext_carros` | Parqueadero de carros | −0,5 | 49,3 | parqueadero |
| `ext_bicicletas` | Parqueadero de bicicletas | −6,6 | 81,5 | parqueadero |
| `ext_entrada_parqueadero` | Entrada vehicular del parqueadero | 21,2 | 41,0 | entrada |
| `ext_cancha` | Cancha | −68,9 | 74,6 | cancha |
| `ext_cancha_2` | Cancha 2 | −88,2 | 68,9 | cancha |
| `ext_voleibol` | Cancha de voleibol | −22,9 | 55,2 | cancha |
| `ext_entrada_modulares` | Entrada Modulares y cafetería | −57,5 | 84,2 | entrada |
| `ext_pasillo_modulares` | Pasillo de entrada Modulares | −57,3 | 94,1 | exterior |
| `ext_cafeteria` | Cafetería | −37,0 | 100,5 | cafetería |
| `ext_modulares_secundaria` | Entrada secundaria de Modulares | −28,0 | 128,1 | entrada |
| `ext_camino_central` | Camino central | −24,0 | 70,0 | exterior (nodo de paso) |

`ext_camino_central` no aparece en la lista de destinos: solo sirve para conectar el camino entre el parqueadero, las canchas y Modulares.

## Distancias medidas

| Tramo | Distancia |
|---|---|
| Entrada principal ↔ entrada secundaria del Nuevo | 33,6 m |
| Entrada principal ↔ parqueadero de carros | 49,3 m |
| Cancha ↔ Cancha 2 | 20,1 m |
| Cancha 2 ↔ entrada a Modulares | 34,3 m |
| Entrada cafetería ↔ entrada noreste de Modulares | 52,9 m (por el corredor) |
| Centro del Nuevo ↔ centro de Modulares | 118 m |
| Perímetro del campus | 505 m |
| Área aproximada del campus | 20.353 m² |
| Altitud del terreno | 2.541,7 – 2.542,4 m |

## Conversión desde el KML

El KML entrega latitud y longitud. Para pasarlas al marco local se usaron estas constantes:

```
lat0  =  4.653871731882435
lon0  = −74.16179674443912
mLat  = 111132          m por grado de latitud
mLon  = 111320·cos(lat0) m por grado de longitud
X = (lon − lon0)·mLon
Z = (lat − lat0)·mLat
```

Después se trasladó el origen a la entrada principal del Nuevo y se aplicó el giro de cada edificio.

## Notas de precisión

- Las dimensiones y los giros provienen de los seis polígonos del KML; son la mejor medida disponible del campus.
- El orden exacto de algunos salones y puertas interiores se distribuyó sobre la longitud medida. No sustituye un levantamiento puerta por puerta.
- La escalera y el ascensor del Nuevo están al oeste; la escalera de Modulares, al suroeste.
- El corredor del Nuevo es recto de extremo a extremo; no se modelaron quiebres internos.
