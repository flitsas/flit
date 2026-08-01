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

### Casos de aceptación

Verificados por inspección del material; el OCR debe reproducirlos.

| # | Archivo | Qué debe salir |
|---|---|---|
| 1 | `Fur_Matricula_QYS740` (16 págs) | `aduana` en 2–4, `factura` en 5–7, `impronta` en 14. Las demás en `noReconocidos` (mandato, solicitud, carta selfie, FUR, licencia, prenda). |
| 2 | `Fur_Traspaso_POV336` (26 págs, **11.6 MB**) | Se procesa sin rechazarlo por tamaño — con el tope viejo de 10 MB no habría entrado. Las págs. 5–11 son escaneos sucios: deben caer en `noReconocidos`, **no** forzarse dentro de un tipo. |
| 3 | Cualquier matrícula, `tipos=impronta,soat,rtm` | No aparece ninguna pieza de `factura` ni `aduana` aunque el documento las tenga. |
| 4 | Certificado de consulta al RUNT (pág. 16 de POV336) | **No** debe clasificarse como `soat` ni `rtm`: es un reporte de consulta, no el certificado de la aseguradora ni del CDA. |
| 5 | Los 7 en un `.zip` | Se expande y se procesan; el `.zip` no aparece como pieza ni como error. |
| 6 | Sin key / Anthropic caído | Cada archivo sale en `errores` con el mensaje de carga manual. El endpoint responde **200**, no 5xx. |
| 7 | En la UI, con una casilla ya ocupada | La pieza llega **desmarcada** con el aviso de reemplazo. Sin marcarla, el adjunto anterior no se toca. |

### Qué mirar además del acierto

- **Confianza calibrada**: una pieza correcta con 0.4 es tan mal síntoma como una equivocada con 0.95.
- **Falsos positivos por encima de falsos negativos**: que meta un mandato en `factura` es peor que
  dejarlo en `noReconocidos`, porque el operador puede no revisarlo.
- **Latencia real** del expediente de 26 páginas, para confirmar que 180 s sobran.

## Pendiente / deuda conocida

- **Precisión del clasificador sin medir.** El prompt se calibró leyendo el material, no ejecutándolo.
  Es el primer ajuste probable tras la primera pasada real.
- **Un solo nivel de carpeta.** Decisión de producto; si aparece el caso de subcarpetas, es un cambio
  acotado en `soloPrimerNivel` y en la lectura del arrastre.
- **Los `noReconocidos` no ofrecen asignación directa.** La salida hoy es cargar el archivo original en
  su casilla, donde el OCR dirigido reintenta. Asignar un rango de páginas suelto a un campo desde la
  pantalla de revisión sería el siguiente escalón.
- **Sin tope de reintentos.** Cada reintento del operador vuelve a pagar la clasificación. No se puso
  límite a la espera de ver el coste real.
