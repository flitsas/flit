# Plan técnico — Mejoras `modificaciones.txt`

**Fecha:** 2026-07-29 · **Rama base:** `develop` (limpia; PR #198 y #201 ya mergeados)
**Origen:** `modificaciones.txt` (19 ajustes + rediseño de la tabla de trámites)
**Aclaraciones del PO recogidas antes de planificar:**

1. El **FUR no cambia de contenido**: el único defecto es que **el nombre de empresa largo se desborda** del campo de nombre (no se trata de imprimir datos del representante legal).
2. Las columnas de la tabla de trámites están definidas al final de `modificaciones.txt` (líneas 57-70).
3. Entrega: plan + **un único Feature** con todas las HUs en Azure DevOps (New, sprint siguiente, sin activar).

---

## 1. Estado verificado en código (qué existe hoy)

| # | Ajuste | Estado actual (verificado) | Falta |
|---|--------|---------------------------|-------|
| 1 | FUR, nombre de empresa desbordado | `FurFieldMapper.NameParts` (`FurFieldMapper.cs:435`) pone la razón social completa en `vehicle_owner_name` / `vehicle_buyer_name`; el overlay pinta a cuerpo fijo del manifest (`FurOverlayRenderer`) | Auto-ajuste (shrink/wrap/clip) del texto al ancho del campo |
| 2 | No regenerar documentación con trámite aprobado | `ConsolidadoEndpoints.cs` solo bloquea `migrado_solo_lectura`; existe el helper de dominio `TramiteEstado.PermiteEdicionDatos` (`TramiteEstado.cs:66`) y `TramiteEstado.Finales = [aprobado, anulado]` | Guard en generación (FUR, consolidado, impronta, compraventa, mandato, virtual) + ocultar botones en FE |
| 3 | Editar RL: precargar empresas asociadas | Existe multiempresa por RL (migración `HU10932_LegalRepresentativeCompanies`) y `LegalRepresentativesFormPanel.tsx` | Precarga completa de las compañías asociadas al abrir el formulario en modo edición |
| 4 | Renovar firma de baúl / identidad del RL vencida | `AdminLegalRepresentativeIdentityEndpoints` ya expone `POST /send` y `POST /resend` ("respeta vigencia: no reenvía si ya hay aprobada y vigente") | Camino explícito de **renovación cuando está vencida** + UI en la pestaña Representantes / Baúl |
| 5 | Renovar identidad del mandatario del OT vencida | `AdminMandateSignerIdentityEndpoints` con `/send` y `/resend` equivalentes | Igual que #4, en el módulo de mandatarios del OT |
| 6 | Elegir firma cuando el RL tiene varias activas | El lookup por NIT ya devuelve por representante `FirmaVigente` e `IdentidadVigente` (`FindRepresentativeByNitResponse.cs:26`) y el FE ya permite **elegir representante** (HU #10937, `ActorsForm.tsx:638-656`) | Selector del **mecanismo de firma** (baúl vs. validación de identidad) y persistirlo en el trámite para que los generadores lo respeten |
| 7 | Ver documentos online desde el listado de trámites | Patrón ya resuelto en OT: `ClientProceduresSection` + `OtDocumentosTab` + `DocumentPreviewModal` (`components/shared/DocumentPreviewModal.tsx`). En trámites ya existe el endpoint `GET /instances/{id}/attachments/{attachmentId}/preview-url` (`AttachmentEndpoints.cs:140`) | Panel de documentos abierto desde la fila del listado de trámites (solo FE) |
| 8 | Botón de consolidado en la tabla de trámites | Existe en OT (`ClientProceduresTable.tsx:215` "Ver consolidado"); en trámites la fila solo abre el wizard | Botón por fila visible solo si el consolidado ya está generado |
| 9 | Firmas de mandato y trámite virtual sobre la línea | `SolicitudVirtualPdfGenerator.RenderFirmaSlot` (`:197`) pinta imagen **o** línea, y el sello va **debajo** del bloque de datos (`:214`). `MandatoPdfGenerator.RenderFirmaSlot` (`:304`) igual | La línea debe existir siempre y la estampa (identidad **o** baúl) ir **sobre** ella, encima de los datos |
| 10 | Mandato: bloque de datos completo | `MandanteIdentificacion` (`MandatoPdfGenerator.cs:404`) imprime NOMBRE, doc, EMPRESA, NIT — **sin CELULAR ni CORREO** y en orden distinto al pedido | Orden EMPRESA/NIT/NOMBRE/CC/CELULAR/CORREO + teléfono y correo (persona natural: sin EMPRESA/NIT) |
| 11 | Título de compañía en el admin de configuración | `CompanyConfigTabs.tsx` no imprime la compañía; el contenedor es `app/admin/companies/[tenantId]/page.tsx` | Encabezado persistente con razón social + NIT de la compañía en edición |
| 12 | Carga/actualización de escrituras más intuitiva | Alta/edición vive en el detalle del representante dentro de la pestaña Representantes (`LegalRepresentativesTab`, `ActiveDeedsCollapse`); endpoints `AdminDeedsEndpoints` (`:51`, `:60`) ya soportan PDF + vigencia + compañías | Rediseño de la interacción (drag & drop, estado de vigencia visible, reemplazo en un paso) |
| 13 | Área clickeable de iconos en tablas | `RowActions.tsx:50` usa `p-1.5` con icono `h-4 w-4` → target ~28 px, por debajo de 44 px; `OtUsersSection` y varias tablas usan botones sueltos con el mismo padding | Ampliar el área efectiva (min 40-44 px) sin cambiar el diseño visual |
| 14 | Correo repetible entre comprador y vendedor | `ActorsForm.validateActors` (`:155`) valida "vendedor≠comprador por doc/**email**" | Quitar solo la comparación por correo (el documento sigue debiendo diferir) |
| 15 | Banner de estado quemado | `TramiteWizard.tsx:732-748` imprime siempre "Enviado a tránsito — solo visualización…" para cualquier estado no editable (`entregado`, `aprobado`, `rechazado`, `anulado`) | Mensaje derivado del estado real (y coherente con lo que sí se puede hacer, ver #2) |
| 16 | Orden vendedor → comprador en el paso FUR | La sección "Firma de la compraventa" ya lo hace (`FirmaFurStep.tsx:1051`, HU #11019) y `ExpedienteVisor` también; **pero** el resumen de identidad usa `['comprador','vendedor']` (`BiometricStep.tsx:49`, e `IdentityStatusPanel`) | Invertir el orden en el resumen de validación de identidad |
| 17 | Fecha AAAA/MM/DD sin hora en certificados | `RnmcCertificatePdfGenerator.cs:47` y `:83` usan `yyyy-MM-dd HH:mm`; `IdentityCertificatePdfGenerator.cs:70` igual; SOAT/RTM/RUES pintan las fechas tal como llegan del proveedor | Normalizar a `yyyy/MM/dd` en todos los certificados generados que entran al consolidado |
| 18 | Un solo botón: todo se genera con el consolidado | La cascada ya existe (HU #10860 y #11017: el consolidado regenera FUR y genera la impronta que falte — `ConsolidadoCommand.cs:29-54`). El botón "Solicitar firma" de compraventa ya se quitó (HU #11019, `FirmaFurStep.tsx:1175`). **Siguen visibles** "Generar FUR / certificado" (`:1878`) y "Generar Improntas" (`:1638`) | Ocultar los botones de generación paso a paso y dejar "Generar consolidado" como único disparador; extender la cascada a compraventa/mandato/trámite virtual |
| 19 | Rediseño de la tabla de trámites | `TramitesTable.tsx:603-614`: Compañía, Placa+radicado, Vendedor, Comprador, VIN, Vehículo, Modalidad, Paso, Estado, Organismo, Creado, Acciones | Ver §2: faltan 4 datos en el contrato del listado |

### Ya resuelto en `develop` (no genera HU nueva)

- Botón "Solicitar firma" de la compraventa: retirado en HU #11019.
- Orden vendedor→comprador en la sección de firmas del paso FUR y en el expediente: HU #11019/#11020.
- Cascada FUR + impronta al generar el consolidado: HU #10860 / #11017.

---

## 2. Tabla de trámites — brecha de datos

Columnas pedidas (`modificaciones.txt:57-70`) contra `InstanceSummary` (`frontend/lib/api/types/procedure-runtime.ts:106`):

| Columna pedida | Hoy | Origen |
|----------------|-----|--------|
| Radicado | ✔ `referenceNumber` | — |
| VIN | ✔ `vin` | — |
| Placa | ✔ `placa` | — |
| Trámite / Modalidad | ✔ `modalidad` | — |
| Propietario / vendedor (traspasos) | ✔ `vendedorNombre` | — |
| **Firmado** (vendedor) | ✖ solo `signaturePending` agregado | **Backend**: estado de firma por parte |
| Comprador | ✔ `compradorNombre` | — |
| **Firmado** (comprador) | ✖ | **Backend** |
| Fecha de creación | ✔ `createdAt` | — |
| **Fecha de actualización** | ✖ | **Backend**: `ProcedureInstance.UpdatedAt` ya existe (`:143`), falta proyectarlo |
| Secretaría | ✔ `organismoTransito` | — |
| **Gestor** (empresa – persona que radica) | ◐ `companiaNombre` solo en listado SuperAdmin; falta la persona | **Backend**: `CreatedByUserId` (`:138`) → nombre |
| **Fuente** (Dashboard / integración / QX) | ✖ | **Backend**: `ProcedureInstance.Origin` (`:100`) ya distingue `ict`; falta mapear las tres fuentes |
| Acciones: continuar · ver documentos · consolidado | ◐ solo continuar | FE (#7 y #8) |

⇒ El rediseño de la tabla **requiere una HU de backend previa** que amplíe `InstanceSummary`.

---

## 3. Descomposición (1 Feature / 21 HUs · 88 SP)

Por decisión del PO, **un solo Feature** agrupa los 19 ajustes. Los bloques de abajo son secciones del
mismo Feature, no Features distintos. Story points en Fibonacci (1, 2, 3, 5, 8).

> Los 19 ajustes del archivo producen 21 HUs porque tres de ellos se descomponen en más de una historia
> (posición de firma y datos del mandante; tabla de trámites en backend y frontend; bloqueo de
> regeneración y botón único), y tres ajustes ya estaban resueltos en `develop` (§1).

### Bloque 1 — Documentos generados: firmas, datos y formato · 21 SP

| HU | Tipo | SP | Alcance |
|----|------|----|---------|
| HU01 | BACKEND | 5 | Mandato y trámite virtual: línea de firma siempre presente y estampa (identidad **o** baúl) **sobre** la línea, encima de los datos del firmante; garantizar que se pinte la firma registrada en el trámite |
| HU02 | BACKEND | 3 | Mandato: bloque de identificación completo y en el orden pedido (EMPRESA/NIT/NOMBRE/CC/CELULAR/CORREO; persona natural sin EMPRESA/NIT) |
| HU03 | BACKEND | 5 | FUR: auto-ajuste del texto al ancho del campo (razón social larga) en el renderer del overlay, sin recalibrar el manifest |
| HU04 | BACKEND | 3 | Fechas `AAAA/MM/DD` sin hora en todos los certificados generados que entran al consolidado (RNMC, identidad, SOAT, RTM, RUES) |
| HU05 | BACKEND | 5 | Extender la cascada del consolidado a compraventa, mandato y solicitud virtual (todo documento generable se produce al generar el consolidado) |

### Bloque 2 — Generación documental: bloqueo por estado y botón único · 11 SP

| HU | Tipo | SP | Alcance |
|----|------|----|---------|
| HU06 | BACKEND | 5 | Guard de estado: con el trámite `aprobado` (o `anulado`) ninguna ruta de generación/regeneración del gestor procede (409 con código propio) |
| HU07 | FRONTEND | 3 | Ocultar los botones de generación paso a paso ("Generar FUR / certificado", "Generar Improntas") dejando "Generar consolidado" como único disparador |
| HU08 | FRONTEND | 3 | Aviso del wizard derivado del estado real del trámite (entregado / aprobado / rechazado / anulado / borrador finalizado), coherente con lo que el estado permite |

### Bloque 3 — Trámites: visibilidad de documentos y tabla · 21 SP

| HU | Tipo | SP | Alcance |
|----|------|----|---------|
| HU09 | FRONTEND | 5 | Ver documentos del expediente desde el listado, sin abrir el detalle (reutiliza `DocumentPreviewModal` + `preview-url`) |
| HU10 | FRONTEND | 3 | Botón de consolidado en la fila, visible solo si ya está generado, abriendo el modal de PDF |
| HU11 | BACKEND | 5 | Ampliar `InstanceSummary`: fecha de actualización, gestor (empresa + persona que radica), fuente (dashboard/integración/QX) y estado de firma por parte |
| HU12 | FRONTEND | 8 | Rediseño de la tabla de trámites con las columnas definidas y nombres consistentes, respetando el diseño UI (depende de HU11) |

### Bloque 4 — Compañías: representantes, escrituras y firmas · 27 SP

| HU | Tipo | SP | Alcance |
|----|------|----|---------|
| HU13 | FULLSTACK | 5 | Editar RL: precargar todas las compañías asociadas en el formulario |
| HU14 | FULLSTACK | 5 | Renovar la validación de identidad **o** la firma del baúl del RL cuando está vencida (estado visible + acción de renovación) |
| HU15 | FULLSTACK | 5 | Renovar la validación de identidad del mandatario del OT cuando está vencida |
| HU16 | FULLSTACK | 5 | Al registrar el trámite con NIT: elegir qué firma se registra cuando el RL tiene varias activas (baúl / validación) y persistirla para los generadores |
| HU17 | FRONTEND | 2 | Encabezado con la compañía (razón social + NIT) en el administrador de configuración |
| HU18 | FRONTEND | 5 | Carga/actualización de escrituras por compañía más intuitiva (subida directa, vigencia visible, reemplazo en un paso) |

### Bloque 5 — Usabilidad del wizard y tablas · 8 SP

| HU | Tipo | SP | Alcance |
|----|------|----|---------|
| HU19 | FRONTEND | 3 | Área clickeable de los botones de icono en tablas (≥40 px) sin alterar el diseño |
| ~~HU20~~ | — | 0 | **Ya resuelta en `develop`.** La HU #11019 quitó el bloqueo por correo compartido en frontend (`ActorsForm.tsx:196-203`) y en backend (`TraspasoPartes.MensajeDuplicadas`, que solo bloquea por documento). Solo quedaba un comentario desactualizado, corregido junto con HU19 |
| HU21 | FRONTEND | 3 | Resumen de validación de identidad: vendedor antes que comprador |

**Total: 86 SP en 20 HUs, bajo un único Feature.**

### Causa raíz de HU19 (hallazgo durante la implementación)

El síntoma reportado — "el puntero no da clic cuando me ubico en el centro del botón y sobre el
icono" — **no** es un problema de propagación de eventos. `frontend/app/globals.css:233-247` define un
cursor personalizado: un SVG de 22×22 px con el hotspot declarado en `2 2` (la esquina). El punto que
recibe el clic queda por tanto hasta ~20 px arriba y a la izquierda del cuerpo visible de la flecha.
Con el objetivo anterior (icono de 16 px + `p-1.5` = 28 px), el usuario ve el puntero sobre el icono
mientras el clic real cae fuera del botón. Llevar el área a 40 px absorbe ese desfase y además cumple
WCAG 2.5.8. El icono no cambia de tamaño: crece solo la superficie sensible.

---

## 4. Orden de ejecución

1. **Bloque 5** (HU19, HU20, HU21) — cambios aislados de FE, sin dependencias; liberan valor de inmediato.
2. **HU06 → HU07 → HU08** — el guard de backend antes de esconder botones, para no dejar rutas abiertas.
3. **HU01, HU02, HU04** en paralelo (generadores distintos) → **HU03** (renderer del overlay) → **HU05** (cascada, después de HU06 para no generar sobre trámites aprobados).
4. **HU11 → HU12**; **HU09 y HU10** pueden ir en paralelo desde el inicio (solo FE).
5. **HU13, HU17, HU18** (módulo compañías) → **HU14, HU15** (renovación de vigencias) → **HU16** (consume el mecanismo elegido; conviene después de HU01, que decide qué firma se pinta).

## 5. Riesgos y trampas conocidas

- **Regeneración al aprobar (#2):** el enunciado dice que la documentación *ya se regenera cuando el OT aprueba*. El guard de B1 debe permitir esa regeneración **interna del flujo de aprobación** (`ApproveOtClientProcedureCommand`) y bloquear solo la invocación del gestor. Si se bloquea por estado sin distinguir el llamador, se rompe la aprobación.
- **Prioridad del baúl (A1/D4):** HU #11031 fijó que con firma de baúl vigente **no** se añade además el sello de identidad. El ajuste #9 pide ambas cosas "sobre la línea" según el mecanismo — hay que conservar la exclusividad y solo mover la posición, o el documento vuelve a parecer firmado dos veces.
- **A3 (texto largo):** el manifest del FUR está calibrado a milímetros (HU #10921). El auto-ajuste debe hacerse en el renderer con el ancho declarado del campo; **no** recalibrar el manifest ni cambiar posiciones.
- **A5 (cascada):** mandato y trámite virtual dependen de configuración por OT (`MandatoTemplateResolver`, matriz HU #10917). Un trámite sin esa configuración no debe hacer fallar el consolidado; la cascada tiene que ser tolerante como ya lo es con la impronta.
- **DbSignatureVaultReader:** antecedente conocido — abría transacción anidada y el best-effort se tragaba el fallo, dejando la regeneración muerta en persona jurídica. Cualquier cambio de A1/D4 que toque la resolución de firmas debe verificarse con un trámite de NIT real.
- **C3 (fuente):** hoy `Origin` solo distingue `ict`; el enunciado pide "solo la que esté activa actualmente". Confirmar con el PO qué valor corresponde a Dashboard y a QX antes de proyectar la columna.
- **C4:** el listado es client-side paginado y de ancho fijo (`min-w-[1340px]`); añadir 4 columnas exige revisar el responsive y no volver a desalinear la cabecera (antecedente en `docs/reporte-desalineamiento-tablas.md`).

## 6. Fuera de alcance / a confirmar

- **Columna Fuente (HU11/HU12) — mapeo propuesto.** `40-ICT-procedure-external-ref.sql:4` documenta el
  dominio real de `procedure_instances.origin`: `'ict'` para lo materializado por la integración y
  `null` = plataforma. `IctOrchestrationService.cs:98` es el único productor de `'ict'`. No existe un
  origen `QX`: Quipux es canal de **salida** (radicación), no de creación, y el historial de estados
  usa además `migration_v1` para lo migrado de FLIT 1.0. Propuesta a confirmar en el gate del Feature:
  `null` → **Dashboard**; `'ict'` → **Integración**; trámite con envío Quipux asociado → **QX**;
  `migration_v1` → **Migrado**. Con esto la columna ya no bloquea la HU.
- El ajuste #9 y el #10 del archivo describen el mismo cambio de posición de firma en el mandato (líneas 19 y 22); se consolidaron en HU01 + HU02.
