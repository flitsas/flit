# ADR-0070: Multi-proveedor de IA por herramienta (config en caliente, secretos solo env)

**Fecha**: 2026-10-12  
**Status**: Propuesto  
**Deciders**: Líder Técnico FLIT (pendiente validar) · Architecture Agent · Producto (marco Épica #13272 / Feature #13508)  
**Tags**: arquitectura, backend, frontend, llm, ocr, integracion-externa, modulo-admin-ia, seguridad  
**Relacionado**: [ADR-0060] (Dr. FLIT + Anthropic; Nivel 2 «override en caliente» queda materializado y generalizado aquí) · [ADR-0058] (tabla global `admin` sin tenant) · patrón `OCR_PROVIDER` / `ANTHROPIC_API_KEY`  
**Diseño detallado**: `docs/ado-drafts/epic-13272-admin-ia/DISENO-TECNICO.md`

## Contexto

OCR semántico y DR. FLIT invocan hoy un único vendor (Anthropic) cableado en `AddOcr` / `AnthropicMessagesClient` / `AnthropicDrFlitChatModel`, con secreto `ANTHROPIC_API_KEY` y flag `OCR_PROVIDER`. Un fallo del proveedor bloquea las herramientas de IA.

La Épica #13272 y el Feature #13508 exigen: configuración **independiente por herramienta**, al menos cuatro proveedores (anthropic, openai, kimi, deepseek), failover automático ordenado por prioridad **solo si hay API key en env**, cambio manual sin reinicio, auditoría e alerta in-app al Super Admin. Los secretos no pueden persistirse ni viajar al frontend.

ADR-0060 dejó un «Nivel 2» opcional (override Super Admin en BD). Este ADR **acepta ese nivel como base** y lo amplía a multi-proveedor, sin introducir un gateway externo.

## Decisión

Adoptar un **router multi-proveedor en core-api** con **configuración persistida en schema `admin`** (global, sin `tenant_id`), secretos **exclusivamente en variables de entorno**, UI Super Admin en **Administradores → Plataforma → Configuración de IA**, y efecto de cambios de prioridad/activo en la **siguiente solicitud** de IA. Failover en la misma request hacia el siguiente proveedor con key y capacidad; historial y alertas en tablas dedicadas; rastro administrativo unificado vía `IAdminAuditWriter` en escrituras manuales.

## Alternativas consideradas

### Opción 1: Router en core-api + settings en `admin.*` *(elegida)*

**Pros:**
- Cumple cambio en caliente, prioridad editable, historial y alertas sin redeploy.
- Reutiliza SuperAdminPolicy, patrón ICT job-settings, dock Plataforma, Anthropic tipado existente.
- Extensible a nuevas tools (`tool_code`) sin menú nuevo.
- Secretos siguen 12-factor; API solo máscara/presencia.

**Cons:**
- Migración + adapters OpenAI/Kimi/DeepSeek + UI (esfuerzo M).
- Dos fuentes de verdad (env = keys; BD = activo/prioridad) a documentar.
- Matriz de capacidades (visión vs chat) por vendor.

**Esfuerzo:** M  
**Riesgos:** Calidad/costo desigual entre vendors; allowlist de modelos obligatoria.

### Opción 2: Solo env vars (`OCR_PROVIDER` / listas de prioridad en env)

**Pros:**
- Esfuerzo S; cero schema; alinea con el wiring actual.

**Cons:**
- No cumple cambio inmediato sin reinicio ni UI de prioridad/historial/alertas del Feature.
- No modela config independiente OCR vs DR. FLIT con failover ordenado.

**Esfuerzo:** S  
**Riesgos:** Entrega incompleta frente a CF del Feature #13508.

### Opción 3: Gateway externo (LiteLLM / OpenRouter / proxy)

**Pros:**
- Routing/failover maduro fuera de core-api; menos adapters C#.

**Cons:**
- Nueva dependencia de infra y posible SPOF; secretos/PII en hop adicional.
- Doble configuración (gateway + admin FLIT); viola preferencia de no dependencias nuevas.

**Esfuerzo:** L  
**Riesgos:** Cumplimiento Habeas Data; costo operativo; onboarding.

## Tradeoff aceptado

Se elige la Opción 1 porque es la mínima que satisface CF-01…CF-16 reutilizando patrones ya vigentes (`AdminIctJobSettingsEndpoints`, excepciones globales `admin.*`, `Cfg()` env-first). Se acepta el costo de adapters y una migración a cambio de no introducir un gateway externo ni dejar la resiliencia atada a redeploy. La Opción 2 queda solo como defaults cuando no hay fila en BD (`OCR_PROVIDER=mock` sigue gobernando CI).

## Consecuencias

### Lo que se gana
- Resiliencia multi-proveedor por herramienta sin reinicio.
- Super Admin ve keys solo como máscara; failover auditable y alertado in-app.
- Camino claro para añadir tools/providers vía catálogo en código + seed.

### Lo que se pierde
- Simplicidad del cableado «un solo HttpClient Anthropic».
- Garantía homogénea de calidad OCR entre vendors (hay que probar y allowlist).

### Cambios operacionales
- Nuevas env: `OPENAI_API_KEY`, `KIMI_API_KEY`, `DEEPSEEK_API_KEY` (+ base URLs opcionales).
- Compose/VPS deben declarar las vars (vacías si no aplican).
- Excepción de schema: tablas globales sin `tenant_id`/RLS (precedente ADR-0058 / DDL mandate formats).

## ADRs relacionados

- [ADR-0060] — Dr. FLIT LLM backend; este ADR materializa y generaliza el Nivel 2.
- [ADR-0058] — Tablas globales en `admin` sin tenant.
- Patrón operativo ICT job-settings (HU #12512) — config en caliente Super Admin.

## Notas para agentes

- **Database Agent**: Materializar `admin.ai_tool_provider_settings`, `admin.ai_provider_transitions`, `admin.ai_failover_alerts` según diseño; citar este ADR en la excepción A4/A10; seed `ocr` + `dr_flit`.
- **Backend Agent**: Implementar router + adapters; no loguear keys; failover solo errores de proveedor; invalidar caché en writes; mantener mock OCR en CI.
- **Frontend Agent**: Menú Plataforma → Configuración de IA; máscaras; confirmación manual; DataTable historial; polling/ack de alertas; 4 estados UI + WCAG.
- **QA Agent**: Cubrir CF SuperAdmin/403, key ausente fuera de cola, failover, 503 sin alternativas, historial paginado, alerta.
- **Security Agent**: Verificar cero leak de secretos en API/logs; allowlist provider/modelo; vendors adicionales en marco PII.
- **Infra Agent**: Propagar env vars en compose/VPS; no desplegar LiteLLM en v1.

## Referencias externas

- Feature ADO #13508 — [ADMIN-IA] Configuración Multi-Proveedor de Inteligencia Artificial  
- Épica ADO #13272 — Resiliencia Multi-Proveedor de Inteligencia Artificial  
- `docs/ado-drafts/epic-13272-admin-ia/DISENO-TECNICO.md`
