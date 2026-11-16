# Validación de fotos: pruebas en dispositivos reales (Feature #14307)

Pendientes de las HUs #14308 (AC7) y #14309 (AC8). Exigen celulares reales y personas del equipo que **autoricen** el uso de su rostro y su documento. No se automatizan ni se simulan.

## Reglas
- Solo personas que autorizan. No se guardan fotos, capturas de pantalla ni videos.
- Se anota **solo** el conteo por caso (sin imágenes ni datos del documento).
- Entorno: un ambiente de pruebas con un enlace de captura manual por intento (cada enlace es de un solo uso; pedir uno nuevo por persona).

## Preparación
1. Abrir el enlace de captura desde el celular, con HTTPS y cámara permitida.
2. Dejar que cargue el paso «Rostro» unos segundos (los modelos se precargan ahí, ~4 MB comprimidos la primera vez).

## #14308 AC7: selfie (falsos rechazos)
Dispositivos: al menos 2 Android y 2 iPhone, de gama media y alta.
Por cada dispositivo, 10 selfies de personas distintas o condiciones distintas (luz de interior, luz de ventana, contraluz leve, con y sin gafas), **bien encuadradas** en el óvalo.
Registrar: `dispositivo / navegador / intentos / pasó a la primera (sí/no) / mensaje mostrado`.
Tasa de falso rechazo = selfies bien hechas que mostraron «No vemos tu rostro» o «Debe aparecer solo una persona» ÷ total.
Caso extra: foto con dos personas (debe rechazar) y foto con la mano tapando la cara (debe rechazar).

## #14309 AC8: documentos colombianos
Por cada tipo, en al menos 2 Android y 2 iPhone: **cédula amarilla** (PDF417 atrás), **cédula digital** (QR), **cédula de extranjería**.
Para cada uno, probar anverso y reverso, con buena luz y con reflejo leve:
| Prueba | Esperado |
|---|---|
| Anverso | pasa; si se muestra el reverso, «Muestra el lado de tu documento con tu foto» |
| Reverso | pasa; si se muestra el anverso, «Muestra el lado con el código de barras» |
| Sin documento (mesa, pared) | pide encuadrar el documento |
Registrar por tipo de documento: `intentos hasta pasar / lado que falló / mensaje`.
Si el código de barras del reverso casi nunca se lee, anotar distancia, inclinación y luz: se ajustan umbrales con esos datos.
Pasaporte (opcional): debe pedir las tres fotos y validar solo que sea un documento.

## Qué mirar además
- Tiempo entre «Capturar» y el resultado (si pasa de ~12 s el flujo deja continuar como «No verificada»).
- Con datos móviles lentos: la primera foto puede mostrar «Revisando tu foto…» más tiempo (descarga única de los modelos).
- Que ninguna petición de red lleve imágenes en el análisis (en el navegador de escritorio, pestaña Red: solo el envío final al backend).

## Prueba de humo en escritorio (sin personas)
`pnpm --filter @flit/frontend motores:fotos && node frontend/scripts/smoke-motores-fotos.cjs` carga los motores reales en Chromium con códigos sintéticos y confirma que no hay peticiones a otros orígenes. No reemplaza las pruebas de arriba.
