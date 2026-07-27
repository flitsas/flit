# Plan técnico — FUR multi-plantilla (AUTOMOTOR + MAQUINARIA + REMOLQUES)

**Origen:** `Fur-req.txt` · **Fecha:** 2026-07-24 · **Estado:** Aprobado el enfoque (D1–D5) por el usuario; pendiente formalizar Feature en ADO.

## 1. Objetivo

1. **Ajustar** el formato del FUR actual (único: AUTOMOTOR) a la muestra oficial `FUR/Formulario traspaso de vehiculos 2.pdf`, recalibrando posiciones de marcación si hace falta.
2. **Agregar dos plantillas nuevas** de FUR: **MAQUINARIA** (`FUR/FUNT-MAQUINARIA-AMARILLA-AGRICOLA 2.pdf`) y **REMOLQUES** (`FUR/FUNT-REMOLQUE-SEMIREMOLQUE-4 3.pdf`).
3. **Seleccionar la plantilla según la clasificación del vehículo** usando la tabla de equivalencia `FUR/vehicle_classification_fur.csv` (96 clasificaciones → AUTOMOTOR / MAQUINARIA / REMOLQUES), que debe crearse en BD (migración + datos).

Aplica a matrícula y traspaso en las 3 plantillas.

## 2. Cómo funciona el FUR hoy (baseline)

Generación por **overlay con PdfSharpCore** sobre PDFs en blanco pre-renderizados (NO HTML/Handlebars en runtime). Todo en `services/core-api/src/Flit.Infrastructure/Documents/Fur/`:

- `FurOverlayDocumentGenerator : IFurDocumentGenerator` (singleton DI, `InfrastructureExtensions.cs:134`) — carga **UN** manifest + **UN** par de plantillas fijas en el ctor.
- `FurOverlayRenderer` — dibuja con `XGraphics` sobre la página 1; anexa la página 2 (instrucciones) sin overlay.
- `fur-field-manifest.json` (EmbeddedResource) — ~90 campos `{id, page, type(text/checkbox/multiline/image), x, y, w, h, fontSize, bold, align}`. `PageWidth=1008, PageHeight=612, origin=top-left`.
- `FurTemplatePaths` — nombres constantes `fur-formulario-p1-blank.pdf` / `fur-instrucciones-p2-blank.pdf`, copiadas al output (Content, no embebidas).
- `FurFieldMapper.Map(FurDocumentData) → dict token→valor` — único, ramifica con `IsTraspaso`. Marca trámite (`requested_process_*`), clase (`vehicle_class_*`, solo 3), combustible, servicio, tipo doc, firmas, etc.
- **`vehicle_class`** (field_value) ya se hidrata desde Kyverum/Intempo/Verifik (texto crudo: "AUTOMOVIL", "CAMIONETA"…). Hoy solo alimenta 3 checkboxes; **no** elige plantilla.
- Guardias: `FurManifestGuardTests` — IDs únicos, campos dentro de la página, **todo token del mapper tiene placement**, y **baseline de geometría congelada** (huella por campo).

**Los 4 acoplamientos "una sola plantilla" a romper:** (1) generador singleton con manifest fijo; (2) nombres de plantilla constantes; (3) manifest único con baseline única en el guard; (4) `vehicle_class` no selecciona plantilla.

## 3. Las 3 plantillas son formularios oficiales distintos

| Plantilla | Formulario oficial | Secciones propias (vs AUTOMOTOR) |
|-----------|--------------------|----------------------------------|
| **AUTOMOTOR** | Registro Nacional Automotor | (actual) 18 trámites, clase vehículo, cilindrada, blindaje, carrocería, motor/chasis/serie/VIN, tipo servicio, empresa vinculadora |
| **MAQUINARIA** | Reg. Nal. Maquinaria Agrícola/Construcción Autopropulsada | clase (agrícola/industrial/construcción), tipo tracción (llantas/orugas/cilindros/mixto), largo/ancho/alto, cabina, No. ejes, peso bruto, capacidad carga; **sin** carrocería/servicio/blindaje |
| **REMOLQUES** | Reg. Nal. Remolques/Semirremolques/Multimodulares | clase (remolque/semi/multimodular/similar), referencia, No. ejes, peso vacío, capacidad diseño, serie de fabricación |

Cada una tiene numeración de secciones y grilla de casillas propia → **cada plantilla = su PDF base + su manifest de coordenadas + su lógica de mapper**.

**Cuidado con el CSV:** respetar el mapeo literal, no inferir por nombre. Ej.: `"MAQ. CONSTRUCCION O MINERA"` → **REMOLQUES** (no MAQUINARIA).

## 4. Decisiones (aprobadas)

- **D1** — Fuente de verdad del tipo de FUR: **backend** resuelve por la tabla; el frontend solo refleja para UX (no decide).
- **D2** — `vehicle_class` sin match en el catálogo (es texto libre del proveedor): **default AUTOMOTOR** + observabilidad (log/evento), no bloquear.
- **D3** — Campos que los formatos nuevos piden y hoy no se consultan (tracción, largo/ancho/alto, cabina, capacidad diseño, serie de fabricación…): **EN BLANCO** (regla ya vigente), sin extender consultas ahora.
- **D4** — Tabla de clasificación: **seed SQL idempotente**, catálogo **global** (sin tenant, como el CSV), no editable por ahora.
- **D5** — Alcance/rama: **Feature nueva** (separada de #10852), rama nueva desde develop; formalizar en ADO vía `/requirement-to-delivery`.

## 5. Diseño técnico

### 5.1 Catálogo de clasificación → plantilla
- Tabla `vehicle_classification_fur` (global): `id, classification (unique), template_format, created_at, updated_at, deleted_at`. Seed con las 96 filas del CSV (idempotente `INSERT ... ON CONFLICT DO NOTHING`, patrón de tablas de catálogo del repo).
- `template_format` ∈ {AUTOMOTOR, MAQUINARIA, REMOLQUES}.
- Entidad + repo/lectura + **`FurTemplateResolver`**: normaliza `vehicle_class` (upper + sin tildes + trim) y busca en el catálogo → `FurTemplateFormat`. Sin match → `AUTOMOTOR` (D2) + señal observable.

### 5.2 Refactor multi-plantilla del generador
- `enum FurTemplateFormat { Automotor, Maquinaria, Remolques }`.
- `FurDocumentData` gana `TemplateFormat` (resuelto en `FurCommand.AssembleData` desde `vehicle_class` vía `FurTemplateResolver`).
- `FurOverlayDocumentGenerator` deja de tener manifest/plantillas fijas: selecciona **manifest + par de plantillas + estrategia de mapper** según `data.TemplateFormat`. 3 manifests embebidos (`fur-field-manifest.automotor.json`, `.maquinaria.json`, `.remolques.json`); loader por formato; `FurTemplatePaths` por formato.
- `FurManifestGuardTests` → iterar sobre los 3 manifests (baseline + cobertura de tokens por manifest+mapper).

### 5.3 Mapper
- Base compartida (datos comunes a los 3: organismo, placa, partes comprador/vendedor, tipo de documento, importación/remate, datos de alerta, firmas/sellos, observaciones, trámite matrícula/traspaso).
- Specifics por formato: AUTOMOTOR (carrocería, servicio, blindaje, motor/chasis/serie/VIN); MAQUINARIA (clase maquinaria, tracción, cabina, largo/ancho/alto, ejes, peso bruto); REMOLQUES (clase no-automotor, referencia, ejes, peso vacío, capacidad diseño, serie fabricación).
- Campos sin dato → EN BLANCO (D3).

### 5.4 Plantillas base (assets)
- Usar los **3 PDF provistos tal cual** como blanks (p1 formulario + p2 instrucciones). Medir el tamaño de página real de cada uno para fijar `PageWidth/PageHeight` de su manifest (el actual es 1008×612 landscape).
- No se regeneran con Handlebars (no hay `.hbs` de maquinaria/remolques); los PDF provistos son la fuente. El script `tools/fur-assets/` queda como referencia histórica del AUTOMOTOR.

### 5.5 Calibración de coordenadas (el grueso del esfuerzo)
- Por plantilla, definir (x,y[,w,h]) de cada campo/casilla. Método iterativo visual: render del blank con overlay de prueba → PNG (pymupdf) → ajustar → repetir. Igual que la calibración de los certificados.
- AUTOMOTOR: partir del manifest actual; recalibrar solo si el PDF provisto difiere del blank actual.

### 5.6 Frontend
- Mostrar/validar qué FUR aplica según `classification` del vehículo (endpoint que exponga el `template_format` resuelto, o mapping espejo para UX). El backend sigue siendo la fuente de verdad (D1).

## 6. Fases / descomposición propuesta en HUs

| HU | Título | Capa | Dep. |
|----|--------|------|------|
| **1** | Catálogo `vehicle_classification_fur` + seed 96 + `FurTemplateResolver` | DB+BE | — |
| **2** | Refactor multi-plantilla del generador + guardia extendida (AUTOMOTOR intacto) | BE | 1 |
| **3** | Plantilla AUTOMOTOR: incorporar PDF provisto + recalibrar | BE | 2 |
| **4** | Plantilla MAQUINARIA: blank + manifest + mapper (calibración) | BE | 2 |
| **5** | Plantilla REMOLQUES: blank + manifest + mapper (calibración) | BE | 2 |
| **6** | Frontend: tipo de FUR por clasificación | FE | 1 |
| **7** | Integración + tests + evidencias | BE+QA | 3,4,5,6 |

Orden de arranque: HU1 → HU2 → (HU3/HU4/HU5 en paralelo) + HU6 → HU7. Esfuerzo grueso ~30–40 SP; el peso está en la calibración de HU4/HU5.

## 7. Riesgos
- **Calibración manual** de ~90 campos × 2 plantillas nuevas = trabajo iterativo y sensible (mitiga: método render→ajustar ya probado con certificados).
- `vehicle_class` es texto libre del proveedor → el matching normalizado puede no cubrir variantes; D2 (default AUTOMOTOR + observabilidad) acota el riesgo funcional.
- `FurManifestGuardTests` congela geometría → al agregar manifests hay que extender la guardia deliberadamente (no romperla en silencio).
- Singleton del generador: pasar a selección por formato sin romper el registro DI ni el rendimiento (cachear manifests/plantillas por formato).

## 8. Tests
- Resolver: mapeo por clasificación (incl. `MAQ. CONSTRUCCION O MINERA`→REMOLQUES, sin tildes, default AUTOMOTOR).
- Generador: por formato, PDF `%PDF` + páginas correctas + tokens dentro de la página; regresión AUTOMOTOR matrícula/traspaso.
- Guardia de manifests (3) + cobertura token↔placement por formato.
- Seed idempotente (db-schema-validator).
