# Códigos QR

Cada espacio navegable y cada entrada tiene una etiqueta QR imprimible. El QR sirve para fijar el punto de origen o el destino sin escribir el código del salón.

## Contenido

- **Payload:** `SONEA:<id>`, por ejemplo `SONEA:EN101` o `SONEA:modulares_P1_entrada_este`.
- **Formato:** PNG de 400 × 400 px.
- **Cantidad:** 90 etiquetas (62 espacios, 4 entradas, 10 escaleras/ascensores y 14 puntos exteriores).
- **Ubicación:** [`docs/qr/`](qr/).

Archivos:

```
docs/qr/
  png/<id>.png             una etiqueta por espacio
  manifiesto.csv           id, nombre, edificio, piso, tipo, payload, archivo
  index.html               hoja imprimible con todas las etiquetas
  Edificio_Nuevo_Piso_1.html
  Edificio_Nuevo_Piso_2.html
  Edificio_Nuevo_Piso_3.html
  Edificio_Nuevo_Piso_4.html
  Edificio_Modulares_Piso_1.html
  Edificio_Modulares_Piso_2.html
  Campus_y_exteriores.html
```

## Cómo se usa en la app

1. Abrir el buscador del panel 2D.
2. Escanear el QR con la cámara del teléfono o escribir el payload.
3. La app interpreta `SONEA:<id>` y filtra las listas por ese id.
4. El origen elegido se conserva; solo cambia el destino (o el origen, si el usuario lo indica).

El buscador también acepta el id suelto (`EN101`) y el nombre visible (`Biblioteca`).

## Regenerar las etiquetas

En Unity:

1. Menú **ETITC Tools → Generar Códigos QR del Campus**.
2. El generador lee `Assets/Resources/CampusGraph.json` y escribe en `Builds/QR`.
3. Copiar `Builds/QR` a `docs/qr`:

```powershell
Copy-Item -Path Builds\QR\* -Destination docs\qr -Recurse -Force
```

Por consola:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Unity.exe" `
  -batchmode -quit -projectPath "C:\Users\USUARIO\SONEA" `
  -executeMethod QRCodeGenerator.GenerateQrCodes `
  -logFile unity-qr.log
```

El generador usa el servicio `api.qrserver.com`; necesita conexión a internet. Si el servicio falla, el manifiesto se escribe igual y las etiquetas que no se descarguen se reportan en la consola.

## Impresión

1. Abrir `docs/qr/index.html` en un navegador.
2. Imprimir en A4, una hoja por piso si se usan las hojas individuales.
3. Plastificar o pegar en la puerta, a la altura de los ojos.
4. Comprobar con el teléfono que el QR se lee con poca luz.

## Manifiesto

`manifiesto.csv` tiene una fila por etiqueta:

```csv
id,nombre,edificio,piso,tipo,payload,archivo
EN101,EN101,Edificio Nuevo,1,salon,SONEA:EN101,png/EN101.png
nuevo_P1_entrada_principal,Entrada principal Nuevo,Edificio Nuevo,1,entrada,SONEA:nuevo_P1_entrada_principal,png/nuevo_P1_entrada_principal.png
```

Se puede abrir en Excel o Google Sheets para llevar el control de qué etiquetas ya están instaladas.

## Notas

- El id del QR debe existir en el grafo; si se renombra un nodo, hay que regenerar las etiquetas.
- No se deben imprimir QR de nodos de paso (`ext_camino_central`), porque no son destinos. El generador también crea sus etiquetas, pero se pueden descartar.
- Las etiquetas de escaleras y ascensores sí se generan: sirven para probar el cambio de piso.
