# Plan — Ajustes documentales: visor de identidad, compraventa, portada, pie, marca de agua, membrete y regeneración

> Fuente del requerimiento: `Ajustes-documentación.txt` (7 puntos).
> Recursos de diseño: `recursos dllo membrete/` (portada, hojas flit, muestras PDF).
> Fecha de análisis: 2026-07-22. Estado del código validado por escaneo directo (file:line abajo).

---

## 0. Diagnóstico transversal (el hilo conductor)

Cinco de los siete puntos (2B, 3, 4, 5, 6) tocan **el mismo dolor**: hoy no existe una **capa de marca/tema documental** compartida, y el ensamblador del consolidado es un **concatenador de bytes "tonto"**.

- `PdfExpedienteConsolidadoMerger.Merge(IReadOnlyList<byte[]>)` (`Flit.Infrastructure/Documents/PdfExpedienteConsolidadoMerger.cs:34-52`) solo copia páginas con **PdfSharpCore** (`output.AddPage(input.Pages[i])`, :44-46). No conoce el estado del trámite, ni a qué documento pertenece cada página, ni tiene portada. Los puntos 3/4/5 exigen convertirlo en un **compositor con contexto**.
- Los generadores QuestPDF de certificados (`Rues`, `Rnmc`, `Identity`) **duplican** página/margen/fuente (A4 + 2 cm + `Fonts.Arial`), **sin membrete, sin logo, sin Poppins** (grep "Poppins" → 0 coincidencias en `services/core-api`). El único con `Header()`/`Footer()` y paleta de marca es `ExecutiveSummaryPdfGenerator.cs:22-25,51,205`, pero está aislado.

**Estrategia (alta cohesión / bajo acoplamiento):** introducir un módulo único de marca —
`Flit.Infrastructure/Documents/Branding/` — con:
- `FlitDocumentTheme` (colores `#557EFF`, `#162744`; márgenes 2,54 cm; tamaño **Carta**; tipografías Poppins).
- `FlitLetterhead` (componente QuestPDF `IComponent` reutilizable: membrete arriba/abajo + nombre del documento abajo-derecha).
- `Branding/Fonts/Poppins-*.ttf` (`EmbeddedResource`) + registrador QuestPDF (`FlitFonts.EnsureRegistered()`, análogo a `FurFontResolver.EnsureRegistered()` de `Fur/`).
- `FlitPdfStamper` (overlay PdfSharpCore `XGraphics`: **pie de página, marca de agua y assets de portada** sobre páginas ya fusionadas, sin importar su origen).

Todos los generadores y el merger dependen de **este módulo**, no entre sí. Cada punto se implementa como una HU que consume la base sin reescribir a los demás.

**Dos rutas de consolidado a mantener consistentes** (ambos handlers, o consolidar en el merger):
- `consolidado` (wizard) → `ConsolidadoCommand.cs` (`merger.Merge` :78).
- `consolidado_maestro` (botón único, Feature #10701) → `ConsolidadoMaestroCommand.cs` (`merger.Merge` :97).

---

## 1. Punto 1 — Visor del PDF de "Certificado de identidad" en comprador/vendedor (paso Generar FUR)

**Estado actual**
- El componente con las pestañas Vehículo / Vendedor / Comprador / Documentos es `frontend/components/operacion/ExpedienteVisor.tsx` (tipo `MainTab` :31, `mainTab` :64, tabs :89-126). Paneles Vendedor (:186-206) y Comprador (:208-242) montan `IdentidadBlock` (:255-305).
- **Las fotos son placeholders vacíos "Sin foto"** (grid literal Selfie/Frente/Reverso, `ExpedienteVisor.tsx:289-302`). No hay descarga de imágenes reales que quitar; solo hay que reemplazar ese grid.
- Los paneles se **montan/desmontan** por render condicional según `mainTab` (:186, :208) → el lazy-load "al abrir la pestaña" sale casi gratis.
- **El PDF del certificado ya se puede obtener**: `tramitesClient.downloadBiometricCertificado(instanceId, validationId)` → `GET /instances/{id}/biometric/{validationId}/certificado` (`tramites-client.ts:913-945`; backend `BiometricaEndpoints.cs:214-235`). `validationId = bio.id` (ya disponible como `compradorBio.id`/`vendedorBio.id`).
- **Visor de PDF existente reutilizable**: `frontend/components/shared/DocumentPreviewModal.tsx` usa `<iframe>` + estados `idle|loading|loaded|error` con **skeleton `role="status"`** (:70-87, :144-151). Patrón blob→`URL.createObjectURL` en `ClientProceduresSection.tsx:317-331`. No hay react-pdf/pdfjs (por diseño).

**Relación con otras funcionalidades**
- Dos orígenes posibles del PDF: (a) certificado del **proveedor por validación** (`downloadBiometricCertificado`, disponible en cuanto la biométrica está aprobada, cuelga de la sección "Validación de identidad"); (b) adjunto `certificado_identidad(_vendedor)` generado por "Generar FUR" (`downloadAttachment`, solo existe tras generar el FUR). **✅ Decidido (D1): se usa (a), el certificado del proveedor (`downloadBiometricCertificado`), disponible en cuanto la biométrica está aprobada.**

**Plan**
1. En `ExpedienteVisor.tsx`: subir al padre un caché por parte (`certUrlComprador`, `certUrlVendedor`, con `loading`/`error`) para no recargar al re-abrir pestaña; `URL.revokeObjectURL` en cleanup.
2. Lazy-load: `useEffect` dependiente de `mainTab` + `bio.id` que, al activar comprador/vendedor y si no hay caché, llame `downloadBiometricCertificado(instanceId, bio.id)` (D1), genere objectURL y baje `loading`. Manejar el caso "biométrica aún no aprobada" (sin `bio.id` vigente) mostrando el estado de validación en vez del visor.
3. Reemplazar el grid `:289-302` de `IdentidadBlock` por un visor `<iframe>` embebido (extraído del bloque de `DocumentPreviewModal`) con skeleton mientras carga y fallback de descarga si falla. **Mantener** "Datos del comprador/vendedor" y el cuadro "Validación de identidad".
4. Tests: `frontend/__tests__/firma-fur-step.test.tsx` (+ nuevo de `ExpedienteVisor`).

**Archivos**: `ExpedienteVisor.tsx` (principal), reutiliza `DocumentPreviewModal.tsx` y `tramites-client.ts:913-945` (sin endpoint nuevo).
**Esfuerzo**: 3 SP · **Solo frontend** · Riesgo bajo.

---

## 2. Punto 2 — Firmas de documentos

### 2A. FUR — mantener lógica actual
Sin cambios. La firma del FUR ya opera por precedencia: **baúl de firmas** (actor jurídico/NIT, imagen real; `ISignatureVaultPolicy` + `FurCommand.ResolveVaultSignaturesAsync` :329-378, ADR-0025) o **sello de validación de identidad** (persona natural, texto; `FurCommand.BuildIdentidadSello` :428-438, HU #10488). Si no hay identidad → sello "NO FIRMADO".

### 2B. Compraventa autogenerada firyada por ambas partes (solo traspasos)

**Estado actual (mejor de lo esperado, con un bug latente)**
- `FurCompraventaDocumentGenerator.cs:11-74` **ya genera un PDF real** (QuestPDF): referencia+modalidad, tabla vehículo, tabla de partes (Rol/Nombre/Documento). **NO pinta firmas ni sellos** (ignora `SellosIdentidad`/`FirmaImagenes` aunque ya llegan en `FurDocumentData`).
- Ya está enganchado al consolidado: `FurOverlayDocumentGenerator.GenerateCompraventa` (:46-55) → adjunto `tipo="compraventa"`, `Source="system"`; `TraspasoConsolidadoOrdering.cs:25` lo incluye.
- **Bug latente vs. el requerimiento**: hoy se genera **siempre** en traspaso (`FurCommand.cs:106-109`) y el loop de persistencia **borra el adjunto previo del mismo tipo sin mirar `Source`** (`FurCommand.cs:186-195`) → si el usuario sube una compraventa autenticada y luego genera el FUR, **el sistema la clobbea con la autogenerada**.
- La carga manual de compraventa es un ítem de checklist: matriz `('compraventa', true, 10)` (`25-HU10522-traspaso-matrix-seed.sql:21`), parametrizable por compañía a `Oculto|Obligatorio|Opcional` vía `CompanyDocumentParam` + `ChecklistEngine.cs:132-138` (esto explica el "hoy NO obligatorio"). Discriminador cargado-por-usuario: `Source="user"` vs `Source="system"`.
- Las firmas por sujeto ya están resueltas y viajan a `GenerateCompraventa(data)`: `SellosIdentidad[rol]`, `FirmaImagenes[rol]`, `IdentidadValidada`. "Vigente por sujeto" lo resuelve `IdentityApprovalResolver` (:24-64) + `BiometricRules.EsAprobadaVigente` (:224-242).

**Plan**
1. `FurCommand.cs:108-109`: generar la compraventa automática **solo si NO existe un adjunto `compraventa` con `Source != "system"`** (es decir, si el usuario no cargó una).
2. `FurCommand.cs:186-195`: proteger del borrado idempotente la compraventa subida por el usuario (reemplazar solo los `Source="system"`).
3. `FurCompraventaDocumentGenerator.cs`: enriquecer el PDF con el **cuerpo del pacto** (adaptación de `content.buyingselling.hbs`) + **dos bloques de firma** (comprador/vendedor) que consuman `data.SellosIdentidad[rol]` (y `FirmaImagenes[rol]` si aplica). Si `!IdentidadValidada` o falta el sello del rol → **documento sin firmas** (requisito explícito). Aplicar de una vez el membrete del punto 6.
4. Tests: `FurOverlayDocumentGeneratorTests.cs` (render con/sin firmas), `FurHandlerTests.cs` (no autogenerar ni clobbear si hay `Source="user"`).
5. ADR nuevo (supersede parcial de ADR-0028): compraventa autogenerada firmada con la misma info de identidad del FUR + no-obligatoriedad de la carga manual.

**Archivos**: `FurCompraventaDocumentGenerator.cs`, `FurCommand.cs`; opcional `MockFurDocumentGenerator.cs`. **No** requiere cambios en ordering ni catálogo SQL.
**Esfuerzo**: 5 SP · **Backend** · Riesgo medio (firma legal del documento).

---

## 3. Punto 3 — Portada del consolidado (primera página, todos los trámites)

**Estado actual**
- No hay portada. El tipo catálogo `portada` ("Primera hoja del expediente consolidado (autogenerada)") **ya existe** en el seed (`23-HU10520-document-types-seed.sql`). El merger no compone páginas nuevas (solo copia).
- Datos disponibles en ambos handlers: `instance` cargada (`ConsolidadoCommand.cs:39`, `ConsolidadoMaestroCommand.cs:44`) → placa, tipo de trámite, secretaría de tránsito, compañía radicadora, código/referencia.

**Diseño objetivo** (muestra `recursos dllo membrete/portada/muestra-membrete-documentos.pdf`; assets SVG/PNG `Recurso 1..9`):
- Membrete/franja angular gradiente cian→azul (izquierda/abajo). Logo FLIT + "Versión 2.0".
- `TRÁMITE:` Poppins **Bold 12pt mayúscula** color `#162744`.
- Código (`FLIT-025678`) Poppins **Bold 24pt** color `#557EFF`.
- Datos: `Placa / Tipo de trámite / Secretaría de Tránsito / Compañía radicadora` — etiqueta antes de `:` Poppins Bold 12pt, valor después Poppins Medium 12pt. Último texto a **3,5 cm del borde inferior**.

**Plan**
1. `Branding/FlitCoverPageGenerator` (QuestPDF): compone la portada Carta con `FlitDocumentTheme` + assets SVG/PNG (preferir SVG por calidad de impresión; PNG @72x como fallback). Los assets se copian a `Documents/Branding/Assets/` como `EmbeddedResource`.
2. `PdfExpedienteConsolidadoMerger`: nueva sobrecarga que **antepone la portada** como primer `byte[]` de `pdfParts` (o método `BuildCover(coverData)`), ampliando `IExpedienteConsolidadoMerger`.
3. Ambos handlers (`ConsolidadoCommand.cs:60/78`, `ConsolidadoMaestroCommand.cs:77/97`): construir `CoverData` desde `instance` e insertarla al inicio.
4. Tests: `PdfExpedienteConsolidadoMergerTests.cs`.

**Archivos**: nuevo `Branding/FlitCoverPageGenerator.cs`, `PdfExpedienteConsolidadoMerger.cs`, `IExpedienteConsolidadoMerger.cs`, ambos handlers.
**Esfuerzo**: 5 SP · **Backend** (depende de la base Branding).

---

## 4. Punto 4 — Pie de página con descripción por documento (excepto portada)

**Estado actual**
- No hay pie ni metadata por documento: `Merge` copia páginas crudas y **no sabe a qué documento pertenece cada página** (`:44-46`).
- La fuente del texto ya existe: `DocumentType.Name/Description` (`DocumentType.cs:16-20`), seed `23-HU10520-document-types-seed.sql` (ej. `impronta`→"Impronta de motor y chasis" :19; `compraventa`→"Formato de Compraventa" :30). **Pero** `DocumentTypeRule` (`IDocumentTypeCatalog.cs:8-11`) hoy **no expone** `Name/Description` (solo Code/Mime/MaxSize; proyección en `DocumentTypeCatalog.cs:27`).
- Faltará distinguir compraventa **autenticada** (`Source="user"`) de **generada por el sistema** (`Source="system"`) para el texto del pie (el requerimiento lista ambos).

**Plan**
1. Ampliar `DocumentTypeRule` con `Name/Description` (o `GetLabelAsync(code)`); proyectar en `DocumentTypeCatalog.cs:27`.
2. Cambiar el contrato del merger de `IReadOnlyList<byte[]>` a **`IReadOnlyList<(byte[] bytes, string label)>`** (o un `MergePart` con bytes+etiqueta+#páginas). El merger estampa el pie **por página** con `FlitPdfStamper` (overlay PdfSharpCore `XGraphics`), color `#557EFF` Poppins Medium 8pt, a 2,54 cm del borde derecho y 1,2 cm del inferior (lineamiento de "hojas"). **La portada se excluye** del pie.
3. Handlers: inyectar `IDocumentTypeCatalog` (hoy no inyectado en `ConsolidadoCommand`/`ConsolidadoMaestroCommand`), resolver la etiqueta por `attachment.Tipo` (+ matiz `Source` para compraventa) y pasarla al merger.
4. Tests del merger con etiquetas.

**Relación**: comparte con el punto 5 el mismo motor de overlay (`FlitPdfStamper`) y con el 6 la tipografía/colores (`FlitDocumentTheme`).
**Archivos**: `PdfExpedienteConsolidadoMerger.cs`, `IExpedienteConsolidadoMerger.cs`, `IDocumentTypeCatalog.cs`, `DocumentTypeCatalog.cs`, ambos handlers.
**Esfuerzo**: 5 SP · **Backend**.

---

## 5. Punto 5 — Marca de agua con el estado del trámite

**Estado actual**
- `ProcedureInstance.Status` (`ProcedureInstance.cs:9`), constantes en `TramiteEstado.cs` (`borrador`, `anulado`, `preparado`, `entregado`, `aprobado`, `rechazado`). El merger **no recibe** hoy el estado.
- Etiquetas/colores legibles ya existen en `ExecutiveSummaryPdfGenerator.cs` (`StatusLabel` :281-290, `StatusColor` :292-301) → extraer a helper compartido.

**Regla**: marca de agua **solo** cuando el estado **NO** es `aprobado`, `entregado` ni `preparado` → aparece en `borrador`, `rechazado`, `anulado`. Mayúscula, gris translúcido (no oculta texto), diagonal de izq-abajo a der-arriba, en **todas** las páginas.

**Plan**
1. `FlitPdfStamper.ApplyWatermark(page, text)` (overlay PdfSharpCore `XGraphics`, rotación diagonal + `XColor` gris con baja opacidad), reutilizado por ambas rutas de consolidado.
2. Propagar `instance.Status` al `Merge` (mismo cambio de firma que el punto 4; van juntos).
3. Gate: aplicar solo si `status ∉ {aprobado, entregado, preparado}` usando `TramiteEstado`.
4. Tests: presencia/ausencia de watermark según estado.

**Archivos**: `FlitPdfStamper` (nuevo, Branding), `PdfExpedienteConsolidadoMerger.cs`, `IExpedienteConsolidadoMerger.cs`, ambos handlers; helper de estado compartido.
**Esfuerzo**: 3 SP · **Backend** (se implementa junto con el punto 4: mismo overlay, misma firma).

---

## 6. Punto 6 — Rediseño de certificados con membrete (RUES, SOAT, RTM, RNMC)

**Estado actual (hallazgo de scope crítico)**
- Generadores dedicados **solo** para RUES (`RuesCertificatePdfGenerator.cs`), RNMC (`RnmcCertificatePdfGenerator.cs`) e Identidad (`IdentityCertificatePdfGenerator.cs`). Todos A4 + 2 cm + `Fonts.Arial`, **sin membrete/logo**.
- **SOAT y RTM NO tienen generador**: hoy son resultados de consulta RUNT + gates (`SoatGate.cs`) y un **documento que el usuario sube** (`certificado_vigencia_soat_rtm`, `23-HU10520...:35`). La muestra `hojas flit/ejemplo.pdf` muestra un "Certificado de vigencia SOAT Y RTM" **generado**. **✅ Decidido (D2): CREAR el generador SOAT/RTM** (datos de la consulta RUNT; tablas Datos SOAT / Datos RTM / Avalúo del vehículo como en la muestra).
- No hay tema/membrete/Poppins compartidos (grep Poppins → 0). `ExecutiveSummaryPdfGenerator` es el único con `Header/Footer` + paleta (referencia a extraer).
- **Los certificados alimentan el consolidado** (se generan sueltos y como páginas del expediente). Ojo tamaño de página: pasar de A4→Carta crea páginas mixtas si el resto del expediente sigue en otro tamaño. **✅ Decidido (D3): todo el expediente a Carta EXCEPTO el FUR, que conserva su tamaño de template.**

**Diseño objetivo** (muestra `muestra membrete documentos.pdf` + `Importante.md`): márgenes 2,54 cm los 4 lados; membrete arriba/abajo del alto de la margen; tamaño **Carta**; nombre del documento abajo-derecha `#557EFF` Poppins Medium 8pt (a 2,54 cm derecha, 1,2 cm inferior). Documentos **adjuntos por el usuario** no llevan membrete pero sí el nombre (con `FlitPdfStamper`, cualquier tamaño de hoja).

**Plan**
1. **Base Branding** (habilitador de 3/4/5/6): `FlitDocumentTheme` + `FlitLetterhead` (QuestPDF `IComponent`) + `Branding/Fonts/Poppins-*.ttf` (`EmbeddedResource`) + `FlitFonts.EnsureRegistered()` (QuestPDF `FontManager.RegisterFont`; ver patrón `FurFontResolver.cs:34-38`). Declarar los TTF como `EmbeddedResource` en `Flit.Infrastructure.csproj`.
2. Reestilizar `RuesCertificatePdfGenerator`, `RnmcCertificatePdfGenerator`, `IdentityCertificatePdfGenerator`: `PageSizes.Letter`, `Margin(2.54, Centimetre)`, `Header/Footer` = `FlitLetterhead`, fuente Poppins. (Coherencia: alinear `ExecutiveSummaryPdfGenerator`.) **El FUR NO se toca en tamaño (D3).**
3. **Crear `SoatRtmCertificatePdfGenerator`** (D2): datos de la consulta RUNT (tablas Datos SOAT / Datos RTM / Avalúo, según `ejemplo.pdf`) + interfaz en `IFurDocumentGenerator.cs` (junto a `IRues.../IRnmc...` :155-199) + DI en `InfrastructureExtensions.cs:138-141` + orden en `*ConsolidadoOrdering.cs` + enganche en `FurCommand.cs`.
4. Nombre de documentos adjuntos por el usuario: estampar con `FlitPdfStamper` (comparte con punto 4).

**Archivos**: nuevo `Documents/Branding/*` (+ fuentes + assets SVG + `.csproj`); `RuesCertificatePdfGenerator.cs`, `RnmcCertificatePdfGenerator.cs`, `IdentityCertificatePdfGenerator.cs`; **nuevo** `SoatRtmCertificatePdfGenerator.cs` + interfaz + DI + ordering + `FurCommand.cs`.
**Esfuerzo**: 8 SP (RUES/RNMC/Identidad + nuevo SOAT/RTM) · **Backend**.

---

## 7. Punto 7 — Regeneración del consolidado tras rechazo / cambios en borrador

**Estado actual**
- El consolidado se **persiste** como adjunto (`consolidado` wizard / `consolidado_maestro`). El **maestro** tiene flag de caché `ProcedureInstance.ConsolidadoMaestroVigente` (`:54`), invalidado en cada transición de estado (`TramiteLifecycleService.cs:153`), en la decisión OT (`OtClientProcedureRepository.cs:339`) y al adjuntar licencia (`LicenciaTransitoCommand.cs:86`).
- **El `consolidado` del wizard NO tiene invalidación**: solo se regenera si alguien vuelve a llamar el endpoint (idempotente). Igual para el **FUR** y el **certificado de identidad**: pueden quedar **stale** tras editar en borrador.

**Plan**
1. En `TramiteLifecycleService.cs:153` (y `OtClientProcedureRepository.cs:339`): al transicionar a `borrador`/`rechazado`, **invalidar/borrar** los adjuntos derivados (`consolidado`, `fur`, `certificado_identidad*`, `compraventa` `Source="system"`) o marcarlos stale, para forzar regeneración con datos actualizados.
2. Regenerar en el momento correcto: al re-radicar (`preparado`) o al volver a generar. Reutilizar la idempotencia existente de `ConsolidadoCommand`/`FurCommand` (ya reemplazan el previo).
3. (Opcional) Marca análoga a `ConsolidadoMaestroVigente` para el wizard si se quiere caché explícita: nueva propiedad en `ProcedureInstance.cs` + `ProcedureInstanceConfiguration.cs` + migración.
4. Tests: `TramiteLifecycleServiceTests.cs`, `ConsolidadoMaestroHandlerTests.cs`, `LicenciaTransitoHandlerTests.cs`.

**Archivos**: `TramiteLifecycleService.cs`, `OtClientProcedureRepository.cs`; opcional `ProcedureInstance.cs` + config + migración.
**Esfuerzo**: 5 SP · **Backend** · Riesgo medio (efectos en máquina de estados; cubrir con tests).

---

## 8. Orden de implementación y descomposición sugerida (HUs)

Dependencias: **la base Branding (punto 6.1) habilita 3, 4, 5 y 6.** Los puntos 1, 2B y 7 son independientes.

| # | HU | Tipo | Dep. | SP |
|---|----|------|------|----|
| HU-A | **Base Branding**: `FlitDocumentTheme` + `FlitLetterhead` + Poppins embebida + `FlitPdfStamper` | Backend | — | 5 |
| HU-B | Punto 6: reestilizar certificados (RUES/RNMC/Identidad) + **crear SOAT/RTM** | Backend | HU-A | 8 |
| HU-C | Punto 3: portada del consolidado | Backend | HU-A | 5 |
| HU-D | Puntos 4+5: pie de página + marca de agua (mismo cambio de firma del merger) | Backend | HU-A | 5 |
| HU-E | Punto 2B: compraventa autogenerada firmada + fix clobber | Backend | HU-A (membrete) | 5 |
| HU-F | Punto 7: regeneración tras rechazo/borrador | Backend | — | 5 |
| HU-G | Punto 1: visor PDF certificado de identidad en pestañas | Frontend | — | 3 |

Sugerencia de secuencia: **HU-A → (HU-B ∥ HU-C ∥ HU-D ∥ HU-E) → HU-F**; **HU-G en paralelo** (frontend independiente). Todo bajo un **Feature nuevo** "Ajustes documentales del expediente".

**Cohesión/acoplamiento**: HU-A concentra toda la identidad visual y los overlays; el resto solo consume abstracciones. El merger evoluciona a compositor con contexto (portada QuestPDF → merge PdfSharpCore → overlay pie+marca) sin que los generadores individuales se conozcan entre sí.

---

## 9. Decisiones tomadas (2026-07-22)

- **D1 (Punto 1) → Certificado del PROVEEDOR por validación.** Usar `downloadBiometricCertificado(instanceId, bio.id)` (`tramites-client.ts:913-945` → `GET /instances/{id}/biometric/{validationId}/certificado`). Ventaja: disponible en cuanto la biométrica está aprobada, sin depender de "Generar FUR". No se usa el adjunto `certificado_identidad`.
- **D2 (Punto 6) → CREAR generador SOAT/RTM.** Nuevo `SoatRtmCertificatePdfGenerator` alimentado por la consulta RUNT (ver `hojas flit/ejemplo.pdf` como diseño objetivo: tablas Datos SOAT / Datos RTM / Avalúo). Incluye interfaz + DI + ordering + enganche en `FurCommand`. +3 SP.
- **D3 (Tamaño hoja) → Todo el expediente a Carta EXCEPTO el FUR.** Los certificados (RUES/RNMC/Identidad/SOAT-RTM) y demás documentos generados pasan a `PageSizes.Letter`; **el FUR conserva su tamaño de template actual**. Se acepta convivencia de tamaño del FUR dentro del consolidado; el resto queda homogéneo en Carta.
- **D4 (Assets) → SVG vectorial.** Membrete/portada con los `Recurso *.svg`. QuestPDF renderiza SVG (SkiaSharp) en portada y en `FlitLetterhead` (Header/Footer). **Caveat**: verificar fidelidad de render de cada SVG en QuestPDF; conservar los PNG @72x como fallback puntual si algún asset no renderiza bien. Los overlays de PdfSharpCore (pie/marca de agua) son solo texto, no requieren SVG.

---

## 10. Riesgos

- Cambiar la firma de `IExpedienteConsolidadoMerger.Merge` impacta ambos handlers y tests → hacerlo en HU-A/HU-D con contrato nuevo (`MergePart`) de una vez.
- Poppins: verificar licencia de embebido (OFL) y añadir `LICENSE` como en `Fur/Fonts/`.
- Punto 2B: el documento de compraventa tiene valor jurídico; validar el cuerpo/cláusulas con negocio (adaptación de `content.buyingselling.hbs`).
- Punto 7: tocar el lifecycle puede afectar transiciones existentes; blindar con tests de `TramiteStateMachine`.
- Tamaño de página mixto en el consolidado: **aceptado** — todo va Carta salvo el FUR (D3). Verificar que el FUR (su tamaño de template) se integra sin recortes junto a las páginas Carta.
- **SVG en QuestPDF (D4)**: validar render de cada `Recurso *.svg`; conservar PNG @72x como fallback puntual.
- **Nuevo generador SOAT/RTM (D2)**: depende de que la consulta RUNT exponga los datos de vigencia SOAT/RTM y avalúo necesarios; verificar disponibilidad en el modelo de consulta antes de implementar.
