# Feature — Extraer y reusar la firma manuscrita del certificado Kyverum

> **Fecha:** 2026-09-02  
> **Rama:** `feature/AB-12040-firma-manuscrita-kyverum`  
> **ADO:** Feature #12039 · HU #12040 (registro diferido al abrir el PR).  
> **Plan:** `docs/plan-tecnico-extraccion-firma-identidad-kyverum.md`  
> **ADR:** `services/core-api/docs/adr/ADR-0054-recorte-firma-certificado-kyverum.md` (Propuesto)

Título: `[IDENTIDAD] - Extraer y reusar la firma manuscrita del certificado de validación Kyverum`

# OBJETIVO

Obtener la rúbrica que el ciudadano traza en Kyverum —que el proveedor no entrega por API— a partir del PDF del certificado de validación de identidad, custodiarla una sola vez como artefacto de imagen y reutilizarla para estampar los documentos del trámite cuando la parte firme por validación de identidad, sin recortar el PDF en cada generación ni bloquear la aprobación si el certificado aún no está listo.

# DESCRIPTION

Hoy, al aprobar una validación Kyverum, FLIT guarda estado, vigencia y la serie del certificado. El PDF se descarga al generar el expediente o al pedir el certificado, pero la rúbrica no se extrae: en FUR, mandato, solicitud virtual y compraventa la vía identidad pinta un sello de texto. La imagen de firma solo existe para el baúl.

Kyverum no expone la imagen. Este Feature cubre:

- Extraer el trazo de la sección «FIRMA Y AUTORIZACION DE TRAMITE DIGITAL» del PDF oficial (entre el texto de autorización y el nombre del firmante).
- Extraer **una vez** por validación aprobada (proveedor Kyverum). Persistir en almacenamiento de la empresa (mismo patrón que el baúl: binario fuera de la base; en la validación solo ruta e integridad). Reusar mientras la identidad esté vigente, incluido el apalancamiento entre trámites.
- Disparar la captura **después** del webhook, en el procesamiento asíncrono de la aprobación y **antes** de encadenar la generación automática de documentos. El webhook no descarga el PDF.
- Si al aprobar Kyverum aún no tiene el certificado, la aprobación no se revierte. Se reintenta en ese procesamiento y, si hace falta, en la primera descarga exitosa del PDF (certificado on-demand o generación documental). El backfill tardío **no** regenera solo documentos ya emitidos.
- En documentos: si la parte firma por identidad y hay imagen, se estampa la rúbrica con leyenda de trazabilidad (identificador de la validación, serie del certificado y fechas). Si no hay imagen, se mantiene el sello de texto. Si la parte firma por baúl, esa imagen gana y **no** se pinta la rúbrica Kyverum (una sola vía de firma por parte).
- Proveedores mock y migraciones desde el sistema anterior: no extraen. El visor del certificado sigue devolviendo el PDF completo.
- La imagen es dato personal: no va en listados ni en logs; fallo de recorte o de almacenamiento no tumba la generación documental (queda el sello de texto).

Fuera de alcance: preview en pantallas de admin o del wizard; regenerar FUR u otros PDFs cuando el recorte llegue tarde; cambiar el consolidado; recortar selfie o cédula; depender de un servicio nuevo de Kyverum.

# CRITERIOS FUNCIONALES

- [ ] Dada una validación Kyverum aprobada y un certificado PDF descargable, el sistema persiste un PNG de la rúbrica asociado a esa validación (ruta de almacenamiento e integridad), sin guardar el binario en la base de datos.
- [ ] Si al aprobar Kyverum no entrega el PDF (ausente o vacío), la validación permanece aprobada, el sello de texto sigue disponible y la extracción se reintenta en el procesamiento asíncrono y en la primera descarga exitosa del certificado.
- [ ] Un trámite posterior que reutilice la misma identidad vigente no vuelve a extraer ni a pedir el recorte a Kyverum si el artefacto ya está custodiado.
- [ ] Al generar FUR, mandato, solicitud de trámite virtual o compraventa, si la parte firma por validación de identidad y existe el PNG, se estampa la rúbrica con leyenda de trazabilidad; si no existe, se usa el sello de texto.
- [ ] Si la parte firma por baúl (elección explícita o imagen de baúl resuelta), no se estampa la rúbrica extraída del certificado Kyverum.
- [ ] Un fallo de recorte o de almacenamiento no impide generar documentos: se registra la omisión y se usa el sello de texto.
- [ ] El material de la rúbrica no se expone en listados de validaciones ni en logs; el GET del certificado de identidad sigue devolviendo el PDF completo de Kyverum.
