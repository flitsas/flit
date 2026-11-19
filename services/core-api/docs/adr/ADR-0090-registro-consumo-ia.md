# ADR-0090: Registro de consumo de APIs de IA (contrato ia-consumo v1)

**Fecha**: 2026-11-18  
**Status**: Propuesto  
**Deciders**: Líder Técnico FLIT (pendiente validar) · Architecture  
**Tags**: arquitectura, backend, llm, ocr, observabilidad, costos, plataforma-sdk  
**Contrato**: [`docs/contratos/ia-consumo.v1.md`](../../../../docs/contratos/ia-consumo.v1.md) (común con FLITO)  
**Relacionados**: ADR-0070 (multi-proveedor IA por herramienta) · ADR-0060 (Dr. FLIT) · `consultas.consumo` (HU #13345, precedente de medición que nunca tumba la operación)

## Contexto

FLIT 2.0 llama a proveedores de IA desde tres servicios (core-api: OCR de documentos, clasificación y OCR del lote,
enderezado de páginas, Dr. FLIT; core-comparendos: OCR de comparendos y pruebas de prompt; core-diagnostico: redacción
del dictamen) y FLITO desde otro aplicativo. Hoy no hay forma de responder cuánto cuesta cada módulo, qué usuario o placa
lo consumió, cuántas llamadas fallan por 429/5xx/timeout ni cuánto pesa el failover. Los tokens solo se leen en el chat,
y el OCR en streaming los descarta.

## Decisión

1. **Un contrato único** (`ia-consumo` v1) con tres tablas idénticas en cada base (`ia_consumo_log`,
   `ia_consumo_detalle`, `ia_tarifa_modelo`) y catálogos cerrados, para consolidar FLIT y FLITO sin traducir.
2. **Una fila por cada llamada HTTP real** al proveedor, registrada en el cliente HTTP (`AnthropicMessagesClient`,
   `OpenAiCompatibleClient`; en PR 2, los clientes propios de comparendos y diagnóstico). Así queda cubierto también lo
   que no pasa por el router (clasificador y sonda de orientación). Reintentos, segunda pasada y failover son filas
   distintas con el mismo `correlacion_id`; el router aporta `pasada` (posición del proveedor en la cola).
3. **Contexto de negocio ambiental** (`IIaCallContext`, AsyncLocal, anidable) que abre quien llama: los endpoints de OCR
   (`ocr_documento` / `ocr_lote`), los adaptadores de Infrastructure (`clasificar_lote`, `orientacion_pagina`,
   `dr_flit/chat`) y la verificación de la Licencia de Tránsito (`ocr_licencia_transito`, con placa y trámite, vía el
   puerto `IIaConsumoAmbito` de Application para no acoplar Application al SDK). Usuario y tenant salen de la petición
   (`HttpContext`), no de parámetros: ninguna firma pública cambia.
4. **Pieza común en `Flit.Platform.Sdk/IaConsumo`**: modelo EF + `AddFlitIaConsumo(schema)` en el `ModelBuilder` (mismo
   patrón que la outbox), registrador singleton, cola `Channel` acotada (10 000, descarta y cuenta al llenarse), escritor
   `BackgroundService` por lotes (200) que calcula el costo con tarifas cacheadas 10 min y vacía la cola al apagar (tope
   5 s). Sin purga: el registro y el detalle se conservan (decisión del 2026-11-19). La persistencia es `EfIaConsumoAlmacen<TContext>`: cada servicio la usa sobre su
   propio `DbContext` con `AddFlitIaConsumo<TContext>(configuration, "flit", "<servicio>")`.
5. En core-api las tablas viven en **`admin`** (globales de plataforma sin RLS, como `admin.ai_tool_provider_settings`;
   `tenant_id` es texto informativo), las crea el DDL `203-ia-consumo-log.sql` y EF las mapea con
   `ExcludeFromMigrations`.
6. **Banderas** leídas en cada uso (sin congelar al arrancar): `IA_CONSUMO_LOG` (maestra, `true`), `IA_CONSUMO_DETALLE`
   (`true` desde el 2026-11-19; antes `false`) e `IA_CONSUMO_AMBIENTE` (si falta, `DR_FLIT_DEPLOY_ENVIRONMENT`; si
   tampoco, `local`).

## Alternativas consideradas

- **Registrar solo en el router** — no cubre el clasificador ni la sonda (llaman al cliente directo) ni ve los reintentos
  HTTP internos del cliente. Descartada.
- **Escritura síncrona en la misma transacción** (como `consultas.consumo`) — el OCR no tiene transacción propia y una base
  lenta frenaría la lectura del documento. Descartada: la regla es que el registro nunca bloquea la llamada.
- **Implementación Npgsql genérica con SQL a mano** — evita EF pero duplica el mapeo y obliga a cada servicio a configurar
  esquema y conexión aparte. Se prefirió el `DbContext` que cada servicio ya tiene (patrón outbox).
- **Gateway / proxy de IA externo** — descartado por lo mismo que en ADR-0070 (nuevo salto con datos personales).

## Consecuencias

- Costo por módulo/proceso/usuario/placa y tasa de fallos por proveedor consultables con SQL directo.
- La semilla de tarifas trae SOLO Anthropic con precio oficial; openai/kimi/deepseek quedan con costo NULL hasta cargar
  su tarifa. Claude Haiku 5.5 (precio por tramos de tamaño de prompt) se siembra con el tramo ≤100K tokens, igual que
  FLITO: un prompt mayor queda subestimado.
- Si la cola se llena o la base falla se pierden filas (log de advertencia con contador): es telemetría, no contabilidad.
- El detalle (prompt/respuesta) puede contener datos personales y se conserva sin límite: encendido por defecto y
  sin purga por decisión del 2026-11-19. Se apaga con `IA_CONSUMO_DETALLE=false`; el DDL 203 conserva en su
  `COMMENT` la redacción anterior (los DDL aplicados no se editan).
- Cambiar columnas o catálogos exige `ia-consumo` v2 en el contrato común, nunca a mano en un solo servicio.
