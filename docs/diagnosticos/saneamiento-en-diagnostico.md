# Saneamiento en el Diagnóstico Vehicular

> Generado: 2026-11-14. Reemplaza la versión del 2026-11-13, que describía D-08 (panel informativo). Fuentes: decisiones del usuario S1–S29 del 2026-11-14, `docs/diagnosticos/plan-saneamiento-oleada-unica.md` (contrato técnico) y [ADR-0087] (`docs/decisions/ADR-0087-diagnostico-saneamiento-circuito-de-gestion.md`, Propuesto).

## 1. Qué es (y qué no)

El Diagnóstico Vehicular tiene **dos modos**, Simple y Con Análisis IA. «Con saneamiento» no es un modo: el **saneamiento** es lo que se hace **después** de diagnosticar, para quitar lo que impide el trámite (comparendos, falta de paz y salvo, impuesto, SOAT, RTM).

Desde el 2026-11-14 (S1, ADR-0087) el saneamiento es un **circuito de gestión sin dinero dentro de Diagnóstico**:

- La compañía lo **solicita** con una bandera, en un clic.
- **FLIT** lo gestiona por fuera del sistema y deja constancia del resultado por fuente, con un PDF opcional de evidencia de pago.
- Diagnóstico **no cobra, no liquida y no registra pagos estructurados**.
- El saneamiento **no cambia el dictamen**.

## 2. Qué cambió frente a lo anterior

| Antes | Ahora |
|---|---|
| D-08 = S2 (plan v2): «Sanear» abría un panel informativo, sin solicitud ni gestión | Circuito con solicitud, gestión FLIT, cierre y auditoría; el panel de deuda se mantiene dentro del circuito |
| R-10: «Sanear» solo con Bloqueado (Con Análisis IA) o con deuda > 0 (Simple) | Bandera en **cualquier vehículo terminado**, en los dos modos y con cualquier veredicto (S3) |
| Épica #12679: redirigir a un módulo de saneamiento externo | No hay módulo externo. El circuito vive en Diagnóstico |
| Reserva `derivaciones.destino = 'SANEAMIENTO'` | No se usa: el saneamiento tiene tablas propias, no es una derivación |
| Rótulo «Bloqueado / Requiere Saneamiento» | Se queda «Bloqueado» (S22) |

Sigue igual: con `BLOQUEADO` no se ofrece «Crear trámite».

## 3. El circuito

### 3.1 Quién marca y cuándo

- Cualquier usuario de la compañía con rol de Diagnóstico (permiso `diagnostico.saneamiento.solicitar`). El backoffice FLIT no marca.
- El vehículo debe estar **terminado** (todas sus consultas `Completado` o `Na`) y no tener otro saneamiento no anulado. No espera al resto del lote.
- Individual (bandera por fila) o masivo («Sanear (N)» sobre la selección, hasta 500). Sin «todo el lote».

### 3.2 Estados

```text
 (compañía marca) ──► SOLICITADO ──(FLIT toma)──► EN_GESTION ──(FLIT cierra)──► SANEADO
                        │                           │      └──(FLIT cierra)──► NO_PROCEDE
                        │ compañía desmarca         │ FLIT anula                 │
                        │ o FLIT anula              ▼                            │ FLIT reabre
                        └────────────────────────► ANULADO          EN_GESTION ◄─┘ (con motivo)
```

| Transición | Quién | Motivo |
|---|---|---|
| → `SOLICITADO` | Compañía | No |
| `SOLICITADO` → `ANULADO` | Compañía (cualquier usuario con el permiso) | No |
| `SOLICITADO` o `EN_GESTION` → `ANULADO` | FLIT | Obligatorio |
| `SOLICITADO` → `EN_GESTION` | FLIT | No |
| `EN_GESTION` → `SANEADO` | FLIT | No (reglas de cierre) |
| `EN_GESTION` → `NO_PROCEDE` | FLIT | Obligatorio |
| `SANEADO` o `NO_PROCEDE` → `EN_GESTION` | FLIT | Obligatorio |

`ANULADO` es final: para volver a sanear, la compañía marca de nuevo. Tras un cierre, solo FLIT reabre.

### 3.3 Renglones por fuente

- Al **tomar** (solo la primera vez) el sistema crea un renglón por fuente con hallazgo: SIMIT del vehículo, del propietario y del locatario (deuda o sin paz y salvo; el del locatario también «sin verificar»), impuesto (deuda), SOAT y RTM (no vigentes). Usa los mismos predicados que las reglas del dictamen y funciona también en modo Simple.
- FLIT marca cada renglón como pendiente, saneado o no procede, con nota y un **PDF opcional** (máximo 10 MB). Puede agregar renglones «Otro» y quitar renglones.
- Cerrar como **saneado** exige al menos un renglón, ninguno pendiente y al menos uno saneado. **No procede** solo exige motivo.

### 3.4 Relación con el dictamen y con «Crear trámite»

- Ninguna transición del saneamiento toca el dictamen, y reprocesar o reemitir el dictamen no toca el saneamiento.
- Un vehículo puede estar **«Saneado» y «Bloqueado» a la vez**. Para desbloquearlo, FLIT ajusta el dictamen o el admin de la compañía reprocesa. La pantalla lo explica con un texto fijo.
- Las reglas de «Crear trámite» no cambian. Si hay un saneamiento en curso, el diálogo muestra un aviso, sin bloquear.

## 4. Dónde se ve

| Superficie | Quién | Qué |
|---|---|---|
| Tablero del lote | Compañía | Bandera por fila, etiqueta de estado, filtro con/sin saneamiento, «Sanear (N)» |
| Panel «Deuda y saneamiento» | Compañía y FLIT | Motivos del dictamen (si hay), deuda por fuente (nunca vacío), estado y renglones del saneamiento, evidencias. PDF y Excel |
| Pestaña «Vehículos» del inicio | Compañía | Todas las placas de la compañía, con filtros (incluido el estado del saneamiento) |
| Campana | Compañía (quien solicitó) | Tomado, cerrado, reabierto o anulado por FLIT, agrupado por acción y diagnóstico. Sin correo |
| Bandeja «Saneamientos» del backoffice | FLIT | Todas las compañías, insignia con las solicitudes sin tomar, tomar en bloque, Excel |
| Gestión de un saneamiento | FLIT | Renglones, evidencias, observación, cerrar, reabrir, anular, historial completo |

La compañía ve «FLIT» como actor, nunca el usuario del operador. El panel no suma ni muestra el total del SIMIT: los comparendos van con su valor tal como llega (S17).

## 5. Puntos técnicos que conviene saber

- **Tablas propias** en `core-diagnostico`: `saneamientos`, `saneamiento_renglones` y `saneamiento_historial` (auditoría de solo inserción), más `vehiculos.saneamiento_estado` para las grillas. Un único escritor, `EscritorSaneamiento`.
- **Permisos**: `diagnostico.saneamiento.solicitar` (roles de Diagnóstico) y `diagnostico.saneamiento.gestionar` (solo operación FLIT).
- **Bandeja entre compañías**: es la única ruta del backoffice sin empresa explícita. Cada lectura registra una auditoría por cada compañía que aparece en la página.
- **Evidencias**: el almacén no tiene borrado, así que al reemplazar o quitar un PDF el anterior queda **huérfano** (sin ningún camino de lectura). Es una deuda aceptada en ADR-0087.
- **Contrato**: `contracts/openapi/diagnostico.v1.yaml` pasa de 1.3.1 a 1.4.0, solo con cambios aditivos.

## 6. Fuera de alcance

Cobro, liquidación, pasarela y pagos estructurados; correo; total del SIMIT; cambios al motor de reglas o al ajuste del dictamen; eventos hacia otros productos; migración de datos del sistema anterior; borrado físico de evidencias.

## 7. Pendientes

| # | Tema | Estado |
|---|---|---|
| P1 | ADR-0087 | Propuesto. Lo acepta el Líder Técnico |
| P2 | Enmiendas a las épicas #12679 y #12678 | Comentario «Ajustes 2026-11» al crear el Feature (plan §10) |
| P3 | Total del SIMIT en pesos (C1) | Abierto; no bloquea el circuito |
| P4 | Borrado de evidencias huérfanas | HU propia, fuera de esta oleada |
