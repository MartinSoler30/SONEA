# Grafo de navegación

El grafo vive en **`Assets/Resources/CampusGraph.json`** y es la única fuente de verdad. El script de editor `CampusGraphAutoBuilder` lo lee y construye la escena: nunca se edita la escena a mano para cambiar la topología.

## Composición

| Grupo | Nodos |
|---|---:|
| Nodos de corredor | 85 |
| Espacios interiores (salones, oficinas, baños, biblioteca, hall, salidas) | 62 |
| Entradas de edificio | 4 |
| Escaleras y ascensores | 10 |
| Puntos exteriores | 14 |
| **Total** | **175** |

Destinos que ve el usuario en las listas: **88** (78 nodos del JSON marcados con `showInList` + 10 escaleras/ascensores que el builder agrega siempre).

## Esquema

```jsonc
{
  "schemaVersion": 2,
  "campusName": "ETITC",
  "buildings": [
    {
      "id": "nuevo",
      "name": "Edificio Nuevo",
      "shortName": "Nuevo",
      "floorHeight": 3.5,
      "origin": { "x": -44.5, "z": -11.3 },
      "rotationDegrees": 1.8,
      "length": 44.1,
      "width": 24.8,
      "corridorZ": 12.7,
      "corridorStartX": 4.0,
      "corridorEndX": 42.0,
      "doorOffset": 2.5,
      "floors": [
        {
          "index": 1,
          "name": "Piso 1",
          "stairX": 4.2,
          "stairZ": 9.8,
          "hasElevator": true,
          "elevatorX": 6.6,
          "elevatorZ": 15.9,
          "rooms": [
            { "id": "EN101", "name": "EN101", "x": 7.0, "side": "north", "type": "salon", "showInList": true }
          ],
          "entrances": [
            { "id": "nuevo_P1_entrada_principal", "name": "Entrada principal Nuevo", "x": 44.0, "z": 12.7, "side": "east" }
          ]
        }
      ]
    }
  ],
  "exterior": {
    "nodes": [
      { "id": "ext_nuevo_principal", "name": "Entrada principal Edificio Nuevo", "x": 0, "z": 0, "type": "entrada", "showInList": true }
    ],
    "edges": [
      { "from": "ext_nuevo_principal", "to": "ext_carros", "type": "camino" }
    ]
  },
  "connections": [
    { "from": "nuevo_P1_entrada_principal", "to": "ext_nuevo_principal", "type": "puerta" }
  ]
}
```

### Campos de un espacio

- `id`: identificador estable (también es el payload del QR).
- `name`: texto que ve el usuario.
- `x`: posición sobre el eje del corredor (metros locales).
- `side`: `north` o `south`; define a qué lado del corredor queda la puerta.
- `type`: `salon`, `oficina`, `bano`, `biblioteca`, `hall`, `salida`, `entrada`, etc.
- `showInList`: si aparece como destino seleccionable.

### Campos de una entrada

- `x`, `z`: posición real de la puerta en coordenadas locales del edificio.
- `side`: fachada (`east`, `west`, `north`, `south`), informativa.
- Las entradas se conectan al nodo de corredor más cercano; no se inventan puertas.

## Cómo se arma la topología

1. Por cada piso se crea un **nodo de corredor** en cada `x` relevante: extremos del corredor, escalera, ascensor, cada salón y cada entrada. Los valores se ordenan y se deduplican con tolerancia de 0,25 m.
2. Los nodos de corredor se encadenan en orden.
3. Cada salón se conecta al nodo de corredor más cercano a su `x`. La puerta se coloca a `corridorZ ± doorOffset` (norte o sur).
4. Escaleras y ascensores se conectan al corredor de su piso.
5. Las escaleras se encadenan entre pisos con peso `floorHeight + 1.5`; los ascensores, con peso 2,0.
6. Los nodos exteriores se conectan con las aristas de `exterior.edges`.
7. Las `connections` unen cada entrada de edificio con el punto exterior correspondiente.

El peso de una arista es la **distancia euclidiana en 3D**; solo los tramos verticales usan un peso fijo.

## Rutas de referencia

Estas rutas se validan en cada regeneración (`CampusGraphAutoBuilder.RegenerateAndVerify`) y deben conservar la distancia:

| Origen | Destino | Nodos | Distancia |
|---|---|---:|---:|
| EN101 | BIBLIOTECA | 14 | 45,2 m |
| EN101 | EN105 | 10 | 25,0 m |
| M103 | M108 | 16 | 76,4 m |
| M103 | M219 | 15 | 58,1 m |
| EN101 | M219 | 32 | 247,7 m |
| ext_nuevo_principal | ext_carros | 2 | 49,3 m |
| ext_nuevo_principal | ext_cancha | 4 | 110,0 m |
| ext_nuevo_principal | ext_modulares_secundaria | 7 | 166,4 m |
| ext_entrada_modulares | ext_cafeteria | 3 | 31,2 m |
| nuevo_P1_entrada_principal | nuevo_P1_entrada_secundaria | 12 | 41,8 m |
| modulares_P1_entrada_cafeteria | modulares_P1_entrada_este | 11 | 58,7 m |

Ruta completa del caso más largo (EN101 → M219):

```
EN101 → corredor P1 Nuevo → entrada secundaria → ext_nuevo_secundaria
→ ext_motos → ext_voleibol → ext_camino_central → ext_entrada_modulares
→ entrada cafetería Modulares → corredor P1 Modulares → escalera P1
→ escalera P2 → corredor P2 Modulares → M219
```

## Validación

`VerifyCampusGraph` comprueba:

- Que exista la escena y el `CampusGraphManager`.
- Que todos los nodos tengan id único.
- Que el número de nodos coincida con el JSON.
- Que las 4 entradas de edificio estén conectadas al exterior.
- Que cada piso tenga escalera y que las escaleras estén encadenadas.
- Que las rutas de referencia existan y que las que deben cruzar pisos realmente lo hagan.
- Que todos los nodos del grafo sean alcanzables.

Salida esperada:

```
[CampusGraphAutoBuilder] Grafo construido: 175 nodos (2 edificios, 14 puntos exteriores).
[CampusGraphAutoBuilder] Ruta EN101 -> BIBLIOTECA: 14 nodos, 45,2 m
...
[CampusGraphAutoBuilder] VERIFICACION EXITOSA: grafo, escaleras, UI y rutas de campus correctos.
```

## QR y buscador

Cada espacio y entrada tiene un QR con payload:

```
SONEA:<id>
```

El buscador acepta el texto `SONEA:EN101` o el id suelto. Al escanearlo, la app filtra el origen y el destino por ese id y conserva el origen ya elegido.

## Regenerar el grafo

1. Editar `Assets/Resources/CampusGraph.json`.
2. Menú **ETITC Tools → Regenerar Escena y Verificar Grafo**.
3. Revisar la consola: no debe haber errores y la verificación debe terminar en `VERIFICACION EXITOSA`.
4. Regenerar los QR (**ETITC Tools → Generar Códigos QR del Campus**) y copiar `Builds/QR` a `docs/qr`.
