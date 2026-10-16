# Borrador HUs — Feature #13508 (pendiente aprobación)

Parent: Feature #13508 · Épica #13272 · Sprint 11 · Tag `DOR` · AssignedTo: jorman.copete@flitsas.com  
Branch base: `feature/AB-13316-consultas-notificaciones`  
Diseño: `DISENO-TECNICO.md` · ADR-0070 · Schema: `130-Feature13508-*.sql` (ya en repo)

Orden de implementación: HU-A → HU-B → HU-C / HU-D (C y D pueden paralelizarse tras A) → HU-E.

---

## HU-A — [BACKEND] Persistencia y API Super Admin de configuración IA

**SP:** 5  
**Deps:** Schema Fase 2b (DDL 130 ya escrito; esta HU valida/cablea EF + endpoints)

### Descripción
Como Super Admin FLIT  
quiero consultar y actualizar la configuración de proveedores IA por herramienta (OCR, DR. FLIT) vía API  
para administrar prioridad, proveedor activo y retorno al primario sin reiniciar el servicio.

### Acceptance Criteria

```gherkin
Scenario: Listar tools con máscara de keys (positivo)
  Given existen settings seed para ocr y dr_flit
  And ANTHROPIC_API_KEY está definida y OPENAI_API_KEY no
  When un Super Admin llama GET /api/v1/admin/plataforma/ai/tools
  Then la respuesta 200 incluye ambas tools con activeProvider y prioridades
  And anthropic.hasApiKey=true con apiKeyMask enmascarada
  And openai.hasApiKey=false sin valor en claro

Scenario: Activar proveedor manualmente (positivo)
  Given openai tiene API key presente
  When Super Admin POST .../tools/ocr/activate con providerCode=openai
  Then active_provider pasa a openai sin reinicio
  And se registra transición reason=manual con actor_user_id

Scenario: No Super Admin recibe 403 (negativo)
  Given un usuario autenticado sin rol Super Admin
  When llama GET /api/v1/admin/plataforma/ai/tools
  Then responde 403

Scenario: Activar proveedor sin key (negativo)
  Given kimi no tiene KIMI_API_KEY
  When Super Admin intenta activar kimi en ocr
  Then responde 400/422 con código de negocio y no cambia active_provider
```

**Alcance técnico:** entidades EF + repos; endpoints GET tools, GET/PUT settings, POST activate; OpenAPI; IAdminAuditWriter; nunca devolver secretos; compose/.env.example con nuevas vars.

---

## HU-B — [BACKEND] Router multi-proveedor, failover y adapters

**SP:** 8  
**Deps:** HU-A (lectura de settings + escritura de transitions)

### Descripción
Como plataforma FLIT  
quiero invocar OCR y DR. FLIT a través de un router con failover automático  
para no interrumpir el servicio si Anthropic (u otro activo) falla.

### Acceptance Criteria

```gherkin
Scenario: Failover al siguiente con key (positivo)
  Given ocr activo=anthropic y openai tiene key
  And la llamada a anthropic falla con timeout o 5xx
  When se ejecuta una solicitud OCR
  Then el router reintenta con openai
  And persiste transición reason=failover_auto
  And crea alerta in-app ai_failover_alerts
  And la solicitud OCR no falla por el error del primario

Scenario: Sin candidatos disponibles (negativo)
  Given solo anthropic está enabled y su key es inválida/falla
  And el resto de providers no tienen key o están disabled
  When se ejecuta OCR
  Then responde error estructurado ai_provider_unavailable (503)
  And no se lanza excepción no controlada al cliente

Scenario: Prioridad actualizada aplica en siguiente request (positivo)
  Given se reordenó prioridad vía PUT settings
  When llega la siguiente request de IA
  Then el orden de intento refleja el nuevo providers_json
```

**Alcance técnico:** `IAiProviderRouter`; adapters openai/kimi/deepseek (OpenAI-compatible donde aplique); wiring OCR + DrFlit; matriz de capacidades; tests unitarios de cadena de failover; env OPENAI_/KIMI_/DEEPSEEK_.

---

## HU-C — [FRONTEND] Menú y pantalla Configuración de IA

**SP:** 5  
**Deps:** HU-A (contratos API)

### Descripción
Como Super Admin FLIT  
quiero acceder a Administradores → Plataforma → «Configuración de IA»  
para ver por herramienta el proveedor activo, prioridades y estado de API keys enmascaradas.

### Acceptance Criteria

```gherkin
Scenario: Entrada de menú solo Super Admin (positivo)
  Given un Super Admin autenticado
  When abre el dock Administradores → Plataforma
  Then ve la opción "Configuración de IA"
  And al navegar carga la página con secciones OCR y DR. FLIT

Scenario: Badges de key y 4 estados UI (positivo)
  Given la API devuelve tools con hasApiKey mixtos
  When la página carga
  Then cada provider muestra Configurada/No configurada y máscara si aplica
  And existen estados vacío/cargando/error/lleno

Scenario: Usuario no Super Admin (negativo)
  Given un usuario sin Super Admin
  When consulta el menú Plataforma
  Then no ve "Configuración de IA"
  And al forzar la URL recibe denegación de acceso
```

**Alcance técnico:** `tramitesNav.ts` + ruta `/admin/plataforma/configuracion-ia`; PageHeaderCard; secciones por tool; client API; WCAG 2.1 AA; flit-design-guardian.

---

## HU-D — [FRONTEND] Cambio manual, historial y alertas

**SP:** 5  
**Deps:** HU-A (+ alertas/transitions de HU-B para datos reales de failover)

### Descripción
Como Super Admin FLIT  
quiero cambiar el proveedor activo con confirmación, ver el historial paginado y acusar alertas de failover  
para operar incidentes sin redeploy.

### Acceptance Criteria

```gherkin
Scenario: Cambio manual con confirmación (positivo)
  Given un provider disponible con key
  When el Super Admin confirma "Activar este proveedor"
  Then se llama POST activate y la UI refleja el nuevo activo

Scenario: Historial paginado (positivo)
  Given existen ≥20 transitions
  When abre el DataTable de historial
  Then ve pageSize por defecto 20 con paginación

Scenario: Ack de alerta (positivo)
  Given hay alertas no acked
  When el Super Admin marca ack
  Then la alerta deja de mostrarse como pendiente

Scenario: Cancelar confirmación (negativo)
  Given el diálogo de activación está abierto
  When cancela
  Then no se llama activate y el activo no cambia
```

---

## HU-E — [BACKEND] OpenAPI, compose y documentación operativa de env

**SP:** 2  
**Deps:** HU-A, HU-B

### Descripción
Como equipo de plataforma  
quiero contratos OpenAPI y variables documentadas en compose/.env.example  
para desplegar y operar los cuatro proveedores de forma segura.

### Acceptance Criteria

```gherkin
Scenario: OpenAPI describe endpoints sin secretos (positivo)
  Given el contrato core-api.v1.yaml actualizado
  When se revisan los paths /admin/plataforma/ai/*
  Then los schemas incluyen hasApiKey y apiKeyMask y no incluyen apiKey en claro

Scenario: Env example lista las cuatro keys (positivo)
  Given .env.anthropic.example / docker-compose
  Then documenta OPENAI_API_KEY, KIMI_API_KEY, DEEPSEEK_API_KEY y bases URL opcionales
```

---

## Totales

| HU | Tipo | SP |
|----|------|-----|
| A | BACKEND | 5 |
| B | BACKEND | 8 |
| C | FRONTEND | 5 |
| D | FRONTEND | 5 |
| E | BACKEND | 2 |
| **Total** | | **25** |

Máx 8 HUs: OK (5).
