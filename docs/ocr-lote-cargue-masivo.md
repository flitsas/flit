# Cargue masivo de documentos (OCR de lote)

El operador puede cargar **una sola vez** un expediente completo, una carpeta o un `.zip`, y el sistema
reparte cada documento en su casilla del checklist. Convive con el cargue campo a campo, que **no
cambia**: quien ya sabe qué archivo va en cada casilla sigue usándolo igual.

Endpoint: `POST /api/v1/tramites/ocr/lote`.

## Por qué hay un clasificador aparte

El OCR que ya existía es **dirigido**: se le dice "esto es una factura" y él verifica y localiza. El
cargue masivo necesita lo inverso — dado un expediente, decidir **qué** hay en cada página. Es una
capacidad distinta, no una variante, y por eso vive en su propio contrato
(`IDocumentBatchClassifier`) en vez de retorcer `IDocumentOcrAnalyzer`.

El flujo son dos fases por archivo:

1. **Clasificar** (una llamada, modelo fuerte) → mapa `tipo → páginas + confianza`.
2. **Recortar y verificar** (N llamadas pequeñas, modelo barato) → cada recorte pasa por el prompt de
   su tipo, el mismo del cargue campo a campo.

La alternativa obvia — mandar el PDF completo una vez por cada tipo — es correcta pero cuesta ~5× en
tokens y latencia: un expediente de 30 páginas son ~60–90k tokens de entrada **por llamada**. Con la
fase de clasificación eso se paga una sola vez.

Nada se sube automáticamente. La respuesta alimenta una **pantalla de revisión** y es el operador quien
confirma; así un error de clasificación no deja rastro en el expediente.

## Contrato

`multipart/form-data`:

| Campo | Qué es |
|---|---|
| `files` | Uno o varios archivos. Un `.zip` se expande en el backend. |
| `tipos` | Tipos que el trámite espera, separados por comas (`factura,aduana,impronta,soat,rtm`). |

Header `X-Tenant-Id` obligatorio, igual que el resto de endpoints de trámites.

Respuesta `200`:

```jsonc
{
  "piezas": [                       // lo que se propone adjuntar
    {
      "tipo": "factura",
      "sourceFilename": "expediente.pdf",
      "filename": "factura_expediente.pdf",   // nombre del recorte
      "mimetype": "application/pdf",
      "sizeBytes": 184320,
      "paginas": [5, 6, 7],
      "totalPaginasOrigen": 16,
      "confianza": 0.97,
      "motivo": "Factura electronica con CUFE y datos del vehiculo",
      "data": { "es_factura_valida": true, "...": "..." },  // igual que el cargue campo a campo
      "analisisError": null,
      "contentBase64": "JVBERi0..."           // el recorte, listo para subir
    }
  ],
  "noReconocidos": [                // páginas que no correspondían a ningún tipo
    { "sourceFilename": "expediente.pdf", "paginas": [1, 8, 9], "totalPaginas": 16 }
  ],
  "errores": [                      // archivos que no se pudieron procesar
    { "filename": "roto.zip", "motivo": "No se pudo abrir el comprimido..." }
  ]
}
```

**Un lote donde todo falla sigue siendo un 200** con `piezas` vacío: el fallo de un archivo suelto es
información para el operador, no un fallo de la petición. Sólo se devuelve `400` cuando el problema es
del lote entero (sin tipos válidos, sin archivos, más de 20 archivos, más de 100 MB).

`data` tiene **exactamente la misma forma** que la del cargue campo a campo. Eso es deliberado: el
frontend reutiliza `evaluateOcr` (validez de tipo + cruce de VIN) y `OcrStatusPanel` sin duplicar
ninguna regla, y el operador lee la misma tarjeta que ya conoce.

## Límites

| Límite | Valor | Por qué |
|---|---|---|
| Archivos por lote | 20 | Tope de coste y latencia |
| Peso total | 100 MB | Idem |
| Peso por archivo | 32 MB | El tope real de la API de visión |
| Páginas por PDF | 100 | Pre-filtro antes de gastar la llamada |
| Formatos | PDF, JPG, PNG, ZIP | WEBP se admite como adjunto pero el modelo de visión no lo lee |

El tope por archivo es **cinco veces** el del cargue campo a campo (10 MB) a propósito: el caso típico
del lote es justamente el expediente escaneado que allí no cabía.

Las carpetas se leen **planas** (un nivel). Los `.zip` se aplanan y se ignoran `__MACOSX` y los archivos
ocultos, con cortafuegos de zip bomb por número de entradas y por peso descomprimido.

## Configuración

El proveedor es el mismo flag de siempre (`OCR_PROVIDER`), así que activar el OCR real activa las dos
cosas. Variables nuevas, todas opcionales:

```bash
export OCR_PROVIDER=anthropic
export ANTHROPIC_API_KEY=sk-ant-...

# Clasificación del lote (defaults):
# export ANTHROPIC_CLASSIFIER_MODEL=claude-sonnet-5
# export ANTHROPIC_CLASSIFIER_MAX_TOKENS=8000
# export ANTHROPIC_CLASSIFIER_TIMEOUT_SECONDS=180
```

Tres decisiones que conviene no deshacer sin medir:

- **Modelo fuerte para clasificar.** Decidir qué documento hay en cada página de un expediente escaneado
  es bastante más difícil que verificar un tipo ya conocido. Los recortes siguen con Haiku.
- **`max_tokens` holgado.** En Sonnet 5 el *thinking* adaptativo está activo por defecto y `max_tokens`
  limita razonamiento + respuesta **juntos**: con los 2000 del analizador, el JSON se trunca.
- **Timeout propio.** El `HttpClient` se registra con el mayor de los dos deadlines y cada llamada
  impone el suyo con un CTS enlazado, así el analizador conserva sus 60 s intactos.

## Qué se verifica automáticamente (mock, sin key)

```bash
# backend
dotnet test services/core-api/tests/Flit.Tramites.Application.Tests --filter Ocr
dotnet test services/core-api/tests/Flit.Infrastructure.Tests --filter Ocr
dotnet test services/core-api/tests/Flit.Admin.Tests --filter Ocr
# frontend
cd frontend && npx vitest run __tests__/batch-files.test.ts \
  __tests__/procedure-batch-upload.test.ts __tests__/procedure-batch-confirm.test.ts \
  __tests__/batch-review-panel.test.tsx
```

Cubierto: parseo y saneamiento de la clasificación (tipos fuera de la modalidad, páginas fuera de rango,
la misma página reclamada por dos documentos), el handler completo (recorte, verificación, errores por
archivo, zip, topes), el endpoint por el pipeline HTTP real, la lectura de carpetas y arrastre, las
reglas de qué llega marcado, y el flujo de confirmar/reemplazar/persistir.

**Lo que el mock NO puede cubrir es la precisión del clasificador.** Eso sólo se mide con documentos
reales y la key puesta.

## Verificación manual con Anthropic real

**Paso manual, no automatizable.** Requiere `ANTHROPIC_API_KEY`, el API levantado, un JWT válido y el
`X-Tenant-Id`.

### Material de prueba

Expedientes reales aprobados (`~/Downloads/material-prueba-ocr/`): 7 PDFs de 16 a 29 páginas y 2.4 a
11.6 MB. Son la **salida** que genera Flit, no literalmente lo que carga el usuario, pero contienen los
documentos fuente y sirven como material de clasificación.

### Llamada

```bash
curl -sS -X POST "http://localhost:5000/api/v1/tramites/ocr/lote" \
  -H "Authorization: Bearer $JWT" \
  -H "X-Tenant-Id: $TENANT_ID" \
  -F "tipos=factura,aduana,impronta,soat,rtm" \
  -F "files=@Fur_Matricula_QYS740_Aprobado.pdf" \
  | jq '{piezas: [.piezas[] | {tipo, paginas, confianza, motivo}],
         noReconocidos: [.noReconocidos[] | {sourceFilename, n: (.paginas|length)}],
         errores}'
```

Los 7 de una pasada, para ver el patrón de conjunto:

```bash
cd ~/Downloads/material-prueba-ocr
for f in *.pdf; do
  case "$f" in
    *Matricula*) TIPOS="factura,aduana,impronta,soat,rtm" ;;
    *)           TIPOS="impronta,soat,rtm" ;;
  esac
  echo "── $f"
  curl -sS -X POST "http://localhost:5000/api/v1/tramites/ocr/lote" \
    -H "Authorization: Bearer $JWT" -H "X-Tenant-Id: $TENANT_ID" \
    -F "tipos=$TIPOS" -F "files=@$f" \
    | jq -r '.piezas[] | "  \(.tipo)  págs \(.paginas|join(","))  \(.confianza)"'
done
```

### Resultados de la pasada real (2026-08-01, `claude-sonnet-5`)

Los 7 expedientes, con el prompt tal como está en el código.

| Archivo | Págs | Peso | Qué encontró | Latencia |
|---|---|---|---|---|
| `Matricula_QXU037` | 29 | 5.9 MB | factura 4 · impronta 5 · aduana 6 · impronta 7 | 22 s |
| `Matricula_QXU140` | 16 | 2.6 MB | factura 4–5 · impronta 6 · aduana 7–9 · impronta 10 | 13 s |
| `Matricula_QYS740` | 16 | 2.5 MB | aduana 2–4 · factura 5–6 · impronta 7 · impronta 14 | 10 s |
| `Matricula_QYS756` | 17 | 2.8 MB | aduana 2–4 · factura 5–6 · impronta 7 · impronta 14 | 13 s |
| `Traspaso_DMK181` | 18 | 4.4 MB | impronta 12 | 18 s |
| `Traspaso_NGT915` | 18 | 6.1 MB | impronta 10–11 | 15 s |
| `Traspaso_POV336` | 26 | 12.1 MB | impronta 20–21 | 22 s |

**Sin falsos positivos.** Ninguna página de mandato, cédula, solicitud, FUR, licencia, prenda o paz y
salvo se coló en un tipo. Confianzas entre 0.85 y 0.98, coherentes con el acierto.

Casos de aceptación cubiertos por esta pasada:

- **Tamaño**: `POV336` (12.1 MB) se procesó sin problema. Con el tope anterior de 10 MB no habría entrado.
- **Escaneos ilegibles**: las págs. 5–11 de `POV336` son escaneos sucios y quedaron en `noReconocidos`,
  sin forzarse dentro de un tipo.
- **Consulta RUNT ≠ certificado**: el certificado de consulta al RUNT que genera Flit (pág. 16 de
  `POV336`) **no** se clasificó como `soat` ni `rtm` en ninguna corrida.
- **Modalidad**: en los traspasos no apareció ninguna pieza de `factura` ni `aduana`.
- **Estabilidad**: `QYS740` se corrió tres veces y devolvió siempre la misma agrupación de páginas.

**Coste y latencia**: ~360k tokens de entrada y ~6k de salida para los 7 → **~$0.11 por expediente**
(precio introductorio de Sonnet 5; ~$0.16 al precio estándar). La llamada más lenta fueron 22 s, muy
por debajo del timeout de 180 s.

### Dos hallazgos que conviene conocer

**Cada matrícula tiene DOS improntas.** La hoja de "improntas del cliente" (foto de la placa VIN) y el
"Certificado de Improntas" del CDA son ambos documentos de improntas legítimos. En la pantalla de
revisión aparecerán como dos piezas compitiendo por la misma casilla: se marca la de mayor confianza y
la otra queda desmarcada con el aviso correspondiente, que es el comportamiento diseñado. Si el negocio
quisiera conservar las dos, haría falta una segunda casilla en el checklist.

**Cuidado al construir la verdad de referencia con `pdftotext`.** Durante esta verificación se dio por
falso positivo una impronta que era correcta: la capa de texto de esa página sólo contenía el bloque de
firma electrónica, porque la impronta en sí es una imagen. Para juzgar una clasificación hay que
**mirar la página renderizada** (`pdftoppm -f N -l N -r 60 -jpeg archivo.pdf salida`), no su texto.

## Pendiente / deuda conocida

- **`soat` y `rtm` sin verificar con material real.** Ninguno de los 7 expedientes trae la póliza SOAT
  ni el certificado RTM originales (usan el certificado de consulta al RUNT, que correctamente se
  descarta). Hace falta un expediente que los lleve para cerrar esos dos tipos.
- **Un solo nivel de carpeta.** Decisión de producto; si aparece el caso de subcarpetas, es un cambio
  acotado en `soloPrimerNivel` y en la lectura del arrastre.
- **Los `noReconocidos` no ofrecen asignación directa.** La salida hoy es cargar el archivo original en
  su casilla, donde el OCR dirigido reintenta. Asignar un rango de páginas suelto a un campo desde la
  pantalla de revisión sería el siguiente escalón.
- **Sin tope de reintentos.** Cada reintento del operador vuelve a pagar la clasificación (~$0.11).
  No se puso límite a la espera de ver el uso real.
- **Falta la pasada por la UI.** Lo verificado es el clasificador contra la API. El flujo completo
  (arrastrar → revisar → adjuntar → checklist actualizado) está cubierto por tests pero no se ha
  ejercido a mano contra el stack levantado.
