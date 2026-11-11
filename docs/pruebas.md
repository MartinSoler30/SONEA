# Plan de pruebas

La validación es incremental: **piso → edificio → campus completo**. Cada fase se prueba primero en el teléfono del autor y después en el de un compañero, para detectar diferencias de cámara, brújula y tracking.

## Preparación

1. Instalar el APK:

```powershell
adb install -r Builds\SONEA.apk
```

2. Verificar que el dispositivo tenga ARCore instalado y actualizado.
3. Conceder permiso de cámara.
4. Activar ubicación y brújula; calibrar moviendo el teléfono en forma de 8.
5. Llevar el teléfono a la entrada principal del Edificio Nuevo (origen 0,0).

## Fase 1 — Un piso

**Objetivo:** validar que la flecha, el banner y el anclaje funcionan en un espacio conocido.

| # | Prueba | Criterio de aceptación |
|---|---|---|
| 1.1 | Origen EN101 → destino EN105 | Ruta de 25,0 m, sin saltos ni tramos fuera del corredor. |
| 1.2 | La flecha aparece a 2,5–4 m | Se ve plana sobre el piso, azul, con borde y halo blancos. |
| 1.3 | Caminar hacia el destino | La flecha avanza y se detiene en el vértice del giro. |
| 1.4 | Acercarse a menos de 5 m | La flecha pasa a verde y se desvanece al llegar. |
| 1.5 | Banner | Muestra «Navegando a EN105» y la instrucción con el icono correcto. |
| 1.6 | Desviarse a un aula vecina | Después de 3 s y 4 m, la ruta se recalcula. |

**Evidencia:** captura del panel 2D, captura del HUD con la flecha azul, captura de la llegada en verde.

### Lectura del letrero de la puerta

| # | Prueba | Criterio de aceptación |
|---|---|---|
| 1.7 | Letrero `EN105` a 2 m, de frente | Aparece EN105 como primer candidato con confianza alta. |
| 1.8 | Letrero `EN105` con ligera inclinación | La app prueba rotaciones y sigue detectando EN105. |
| 1.9 | Letrero `SALÓN 105` | El alias lo asocia a EN105 aunque no incluya el prefijo. |
| 1.10 | Letrero de Modulares (`M103`) con origen en Modulares | No lo confunde con un salón del Nuevo. |
| 1.11 | Letrero borroso o con reflejo | No inventa un origen: muestra el texto leído y pide reintentar. |
| 1.12 | Confirmar el candidato | El dropdown de origen queda en el salón y se puede iniciar la ruta. |

**Evidencia:** captura del panel de lectura con el letrero, captura de la lista de candidatos y captura del origen ya seleccionado.

## Fase 2 — Un edificio completo

**Objetivo:** validar cambios de piso, escaleras, ascensor y rutas largas.

| # | Prueba | Criterio de aceptación |
|---|---|---|
| 2.1 | EN101 → BIBLIOTECA | 45,2 m; la instrucción indica subir al piso 3 por la escalera. |
| 2.2 | Escalera del Nuevo (oeste) | La flecha se oculta en el tramo vertical y reaparece en el piso destino. |
| 2.3 | EN101 → EN206 | Ruta por escalera al piso 2, sin atravesar paredes. |
| 2.4 | Ascensor | La instrucción dice «en el ascensor» y el tramo vertical no dibuja decal. |
| 2.5 | Entrada secundaria → oficina del piso 4 | La ruta usa el corredor completo y la escalera correcta. |
| 2.6 | Cambio de piso con re-ruteo | Si el usuario se desvía en otro piso, la ruta se recalcula sin perder el destino. |

**Evidencia:** captura de la instrucción «Sube al piso 3», captura de la flecha en el piso 3, captura del tramo de escalera (sin flecha).

## Fase 3 — Campus completo

**Objetivo:** validar la conexión Nuevo ↔ exterior ↔ Modulares, cafetería, canchas y parqueaderos.

| # | Prueba | Criterio de aceptación |
|---|---|---|
| 3.1 | EN101 → M219 | 247,7 m; pasa por la entrada secundaria, el camino central y la escalera de Modulares. |
| 3.2 | Entrada principal → parqueadero de carros | 49,3 m por el camino exterior. |
| 3.3 | Entrada principal → cancha | 110,0 m; no cruza edificios. |
| 3.4 | Entrada Modulares → cafetería | 31,2 m; la flecha sigue el pasillo exterior. |
| 3.5 | Entrada cafetería → entrada noreste de Modulares | 58,7 m por el corredor de Modulares. |
| 3.6 | QR de un salón | Al escanearlo, el buscador filtra el id y conserva el origen elegido. |
| 3.7 | Inicio desde cualquier punto | Elegir un salón del piso 2 de Modulares como origen y navegar al Nuevo. |
| 3.8 | Exterior con sol directo y con sombra | El tracking se recupera en menos de 5 s tras una pérdida breve. |

**Evidencia:** captura de la ruta larga en el mapa 2D, captura de la flecha en el exterior, captura de la llegada a M219, video corto de 20–30 s.

## Pruebas en dos teléfonos

| Teléfono | Rol | Fecha | Resultado |
|---|---|---|---|
| TECNO POVA 5 (`103953738P002823`) | Autor | — | Pendiente de la corrida final |
| Teléfono del compañero | Segundo usuario | — | Pendiente |

Registrar en cada corrida:

- Modelo y versión de Android.
- Versión de ARCore.
- Si la brújula estaba calibrada.
- Tiempo de anclaje inicial.
- ¿La flecha quedó sobre el piso o flotando?
- ¿El banner cambió de instrucción en los giros?
- ¿Hubo re-ruteo innecesario?

## Comandos útiles

```powershell
# Dispositivos conectados
adb devices -l

# Instalar
adb install -r Builds\SONEA.apk

# Captura (usar cmd para no corromper el PNG)
cmd /c "adb -s 103953738P002823 exec-out screencap -p > Capturas\pantalla.png"

# Logs de Unity
adb logcat -s Unity

# Desinstalar
adb uninstall com.ETITC.SONEA
```

## Criterios de aceptación globales

- La app abre sin errores y muestra el panel 2D.
- El destino se puede elegir por lista, por texto o por QR.
- La ruta se dibuja sobre el piso y no atraviesa paredes.
- La flecha es plana, azul, con borde y halo; no tiene volumen ni anillo.
- El banner muestra título e instrucción en todos los giros y cambios de piso.
- Las 11 rutas de referencia existen y conservan su distancia.
- Los 175 nodos son alcanzables.

## Problemas conocidos y cómo reportarlos

- **La flecha se recorta al mirar muy abajo:** es un decal; se mitiga manteniéndola a 2,5 m o más. Reportar con captura y distancia.
- **La brújula queda desfasada:** recalibrar y volver a anclar; anotar el modelo del teléfono.
- **El tracking se pierde en el exterior:** apuntar a una superficie con textura y esperar la recuperación.
- **Ruta extraña en un salón:** verificar el `x` y el `side` de ese salón en `CampusGraph.json`; el orden de puertas es aproximado.

Adjuntar siempre: modelo del teléfono, captura, id de origen y destino, y el texto del banner.
