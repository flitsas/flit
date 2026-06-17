---
name: flit-azure-devops
description: Integración con Azure DevOps Boards para agentes FLIT. Prioridad de conexión: MCP (user-ado) → REST API (PAT) → Azure CLI. Lee parámetros desde .env.user-identity. Invocar antes de crear, actualizar o consultar work items en ADO desde cualquier skill (feature-creator, flit-crear-hu, bug-reporter, etc.).
---

# Azure DevOps — MCP primero, API segundo, CLI último recurso

Todas las skills que toquen Azure DevOps **deben** seguir este contrato. No usar `az boards` como vía principal ni alternativa directa a MCP.

## Parámetros (`.env.user-identity`)

Leer en la raíz del repo (no versionar el archivo real; solo `.env.user-identity.example`):

| Variable | Uso |
|----------|-----|
| `USER_REAL_NAME` | Trazabilidad en comentarios HTML |
| `USER_REAL_EMAIL` | Asignación (`System.AssignedTo`) y menciones |
| `AZURE_ORG_URL` | Base, ej. `https://dev.azure.com/FlitDevOps` |
| `AZURE_PROJECT_NAME` | Nombre **exacto** del proyecto (respetar espacios), ej. `FLIT - EVOLUTION` |
| `AZURE_PAT` | Personal Access Token (scopes: Work Items Read & Write) |

Si falta el archivo o `AZURE_PAT`: intentar primero MCP (`user-ado`); si MCP también falla, entregar borrador local `.md` y **no** intentar ADO por REST/CLI.

**Nunca** imprimir ni commitear el PAT.

### Validar nombre de proyecto

Si la API responde `TF200016` (proyecto inexistente), listar proyectos y corregir `AZURE_PROJECT_NAME`:

```bash
# Solo en fallback CLI o diagnóstico
export AZURE_DEVOPS_EXT_PAT="<desde .env>"
az devops project list --organization "${AZURE_ORG_URL}" -o table
```

## Estrategia de ejecución (obligatoria)

```
1. MCP (user-ado)         ← SIEMPRE primero — verificar conexión antes de usar
2. REST API (JSON Patch)  ← Si MCP no está disponible o falla
3. Azure CLI (az boards)  ← SOLO si la API también falla y el entorno tiene `az` + extensión azure-devops
4. Borrador .md local     ← Si todos fallan o no hay credenciales
```

**Motivo orden MCP → API:** MCP `user-ado` es la integración nativa del IDE; no requiere gestión manual de PAT ni encoding. Si MCP no responde o no está habilitado, la REST API aplica el cuerpo completo con control total sobre encoding UTF-8. `az boards work-item create/update --description` trunca HTML multilínea en PowerShell, por eso es último recurso.

## Verificación de conexión MCP (paso obligatorio)

Antes de ejecutar cualquier operación con MCP, **verificar** que la conexión `user-ado` está activa:

1. Intentar llamar a `core_list_projects` (herramienta de bajo impacto) para validar conectividad:

```
CallMcpTool(server="user-ado", toolName="core_list_projects", arguments={})
```

2. **Si responde con lista de proyectos** → MCP disponible; continuar con MCP en todos los pasos siguientes.
3. **Si lanza error de autenticación / timeout / herramienta no encontrada** → pasar a REST API inmediatamente; **no** reintentar MCP en la misma sesión.

### Equivalencia MCP → REST API

Las herramientas MCP del servidor `user-ado` mapean directamente sobre los endpoints REST:

| Operación | Herramienta MCP | Equivalente REST |
|-----------|-----------------|------------------|
| Crear work item | `wit_create_work_item` | `POST /_apis/wit/workitems/$Type` |
| Actualizar work item | `wit_update_work_item` | `PATCH /_apis/wit/workitems/{id}` |
| Consultar por ID | `wit_get_work_item` | `GET /_apis/wit/workitems/{id}` |
| Consultar lote | `wit_get_work_items_batch_by_ids` | `POST /_apis/wit/workitemsbatch` |
| WIQL (buscar duplicados) | `wit_query_by_wiql` | `POST /_apis/wit/wiql` |
| Agregar comentario | `wit_add_work_item_comment` | `POST /_apis/wit/workitems/{id}/comments` |
| Actualizar comentario | `wit_update_work_item_comment` | `PATCH /_apis/wit/workitems/{id}/comments/{commentId}` |
| Vincular ítems | `wit_work_items_link` | `PATCH /_apis/wit/workitems/{id}` (relations) |
| Listar proyectos | `core_list_projects` | `GET /_apis/projects` |

> Cuando MCP esté activo, usar **siempre** las herramientas MCP en lugar de llamadas REST manuales; no mezclar ambas en la misma operación.

## Idempotencia al crear work items (obligatorio)

**Regla:** nunca repetir `POST` de creación sin verificar si el ítem ya existe. Un fallo en pasos posteriores (historial, asignación, vínculo padre) **no** autoriza un segundo `POST`.

### Dónde va la lógica

| Capa | Responsabilidad |
|------|-----------------|
| **`flit-azure-devops` (esta skill)** | Contrato: pasos, WIQL, qué hacer si falla cada fase |
| **Skills de dominio** (`feature-creator`, `flit-crear-hu`, …) | Enlazar este contrato; checklist específico del tipo de ítem |
| **Agente / script** | Implementar el contrato; persistir `id` en cuanto el `POST` responda 200/201 |

### Flujo en 3 fases (separar try/catch)

```
Fase A — POST crear     → guardar workItemId de la respuesta
Fase B — PATCH campos   → descripción, tags, AssignedTo (reintentar solo PATCH)
Fase C — PATCH History  → comentario de trazabilidad (reintentar solo PATCH)
```

- **Prohibido:** un solo `try/catch` que envuelva A+B+C y, al fallar, vuelva a ejecutar A.
- Si B o C fallan: `GET /workitems/{id}` y continuar desde la fase que faltó; **no** crear otro ítem.

### Antes del POST (opcional pero recomendado)

Buscar duplicado reciente por título exacto (evita doble clic humano + reintentos ciegos):

```http
POST {AZURE_ORG_URL}/{projectEncoded}/_apis/wit/wiql?api-version=7.1
```

```json
{
  "query": "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project AND [System.WorkItemType] = 'Feature' AND [System.Title] = @title AND [System.State] <> 'Removed' ORDER BY [System.CreatedDate] DESC"
}
```

Variables: `@project` = nombre del proyecto, `@title` = título planificado.

- Si hay **exactamente 1** resultado → usar ese `id` (Fase B/C); **no** hacer `POST`.
- Si hay **0** → `POST` crear.
- Si hay **>1** → detener, informar al usuario con la lista de IDs; no crear más.

### Si el POST falla o la respuesta es ambigua

1. **Tienes `id` en la respuesta** (aunque el script crashee después) → tratar como creado; ir a Fase B/C.
2. **Error de red / timeout sin `id`** → WIQL por título (últimas 24 h o `CreatedDate` reciente).
3. **WIQL encuentra candidato** → `GET` ese ítem y confirmar título; reutilizar `id`.
4. **WIQL no encuentra nada** → un solo `POST` adicional (máximo **1** reintento de creación por sesión).
5. **Nunca** más de 1 reintento de `POST` sin WIQL intermedio.

### PowerShell — persistir id en cuanto exista

```powershell
$created = Invoke-RestMethod -Uri $createUri -Method Post -Headers $headers -Body $body
$workItemId = $created.id   # guardar ANTES de History / AssignedTo
# Si el siguiente PATCH falla, NO repetir Post; usar $workItemId en Patch
```

### Node — mismo patrón

```typescript
const created = await createWorkItem(patch);
const workItemId = created.id;
await patchWorkItem(workItemId, historyPatch); // fallo aquí → reintentar solo esto
```

### JSON Patch en PowerShell

- Preferir cuerpo en archivo `.json` UTF-8 (`Get-Content -Raw -Encoding UTF8`).
- Evitar `ConvertTo-Json` para el documento Patch completo (puede serializar un array como objeto y provocar 400 en pasos posteriores, dejando el ítem ya creado).

## Encoding — regla obligatoria (tildes y caracteres especiales)

Todos los cuerpos JSON enviados a ADO **deben** preservar caracteres UTF-8 sin escaparlos a `\uXXXX`. De lo contrario, Azure DevOps renderiza secuencias de escape literales en lugar de la letra real (`\u00e9` en vez de `é`).

| Lenguaje | MAL | BIEN |
|----------|-----|------|
| **Python** | `json.dumps(patch)` | `json.dumps(patch, ensure_ascii=False)` |
| **Node** | `JSON.stringify(patch)` (ok en v18+ pero fragil) | `JSON.stringify(patch)` + header `charset=utf-8` |
| **PowerShell** | `Get-Content file` (ANSI por defecto) | `Get-Content file -Encoding UTF8 -Raw` |

Regla adicional: el header `Content-Type` debe incluir el charset:

```
Content-Type: application/json-patch+json; charset=utf-8
```

### Causa del mal formato HTML

Azure DevOps ignora o escapa el HTML si el campo contiene comillas dobles `"` no escapadas dentro de un string JSON. Usar entidades HTML (`&quot;`) o comillas simples para atributos HTML dentro de la descripción.

---

## Fallback nivel 2 — REST API (contrato común)

### Autenticación

- Header: `Authorization: Basic <base64(:PAT)>`
- `Content-Type: application/json-patch+json; charset=utf-8` en PATCH/POST de work items
- `api-version=7.1`

### URLs

- Proyecto en path: codificar con `[uri]::EscapeDataString($project)` (PowerShell) o `encodeURIComponent` (Node).
- Crear ítem: `POST {AZURE_ORG_URL}/{projectEncoded}/_apis/wit/workitems/$Feature?api-version=7.1`  
  (cambiar `$Feature` por `$User%20Story`, `$Bug`, etc.)
- Actualizar: `PATCH {AZURE_ORG_URL}/{projectEncoded}/_apis/wit/workitems/{id}?api-version=7.1`
- Consultar: `GET` misma URL sin tipo en path.

### JSON Patch (ejemplos)

**Crear Feature** — un documento con operaciones `add`:

```json
[
  { "op": "add", "path": "/fields/System.Title", "value": "[FRONTEND] - Título" },
  { "op": "add", "path": "/fields/System.Description", "value": "<h2>OBJETIVO</h2><p>...</p>" },
  { "op": "add", "path": "/fields/System.AssignedTo", "value": "usuario@dominio.com" },
  { "op": "add", "path": "/fields/System.Tags", "value": "DOR; adopcion-ia; fase-1-diseño" }
]
```

**Actualizar descripción** (tras crear si hiciera falta un segundo paso):

```json
[{ "op": "replace", "path": "/fields/System.Description", "value": "<html completo>" }]
```

**Comentario de trazabilidad** (`System.History` → módulo **Discussion**):

```json
[{ "op": "add", "path": "/fields/System.History", "value": "<div>🤖 Acción registrada por @Agente ...</div>" }]
```

**Evidencias de tests** (`Custom.Evidences` → módulo **Evidences**; skill `@dev-tester`):

```json
[{ "op": "add", "path": "/fields/Custom.Evidences", "value": "<html reporte PASO 6>" }]
```

En el HTML de evidencias, las tablas deben llevar **`style` inline con `border:1px solid #cccccc` en cada `<th>` y `<td>`** (ADO elimina `border="1"` en `<table>`). Detalle en `@dev-tester` PASO 7.

No intercambiar destinos: Discussion ≠ Evidences. Un `400` en PATCH no autoriza publicar evidencias en `System.History`.

**Vínculo padre–hijo:**

```json
[{
  "op": "add",
  "path": "/relations/-",
  "value": {
    "rel": "System.LinkTypes.Hierarchy-Reverse",
    "url": "{AZURE_ORG_URL}/{projectEncoded}/_apis/wit/workitems/{parentId}"
  }
}]
```

### PowerShell (referencia)

```powershell
$lines = Get-Content ".env.user-identity" -Encoding UTF8 -Raw
# Extraer AZURE_PAT, AZURE_ORG_URL, AZURE_PROJECT_NAME, USER_REAL_EMAIL con regex
$pair = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":$pat"))
$headers = @{
  Authorization  = "Basic $pair"
  "Content-Type" = "application/json-patch+json; charset=utf-8"
}
$projectEncoded = [uri]::EscapeDataString($project)
$uri = "$org/$projectEncoded/_apis/wit/workitems/`$Feature?api-version=7.1"
# Leer body en UTF-8 para preservar tildes y caracteres especiales
$body = Get-Content "patch-create.json" -Encoding UTF8 -Raw
Invoke-RestMethod -Uri $uri -Method Post -Headers $headers -Body ([System.Text.Encoding]::UTF8.GetBytes($body))
```

### Node / fetch (referencia)

```typescript
const auth = Buffer.from(`:${pat}`).toString("base64");
const projectEncoded = encodeURIComponent(project);
await fetch(`${org}/${projectEncoded}/_apis/wit/workitems/$Feature?api-version=7.1`, {
  method: "POST",
  headers: {
    Authorization: `Basic ${auth}`,
    "Content-Type": "application/json-patch+json; charset=utf-8",
  },
  // ensure_ascii equivalente en Node: JSON.stringify no escapa UTF-8 por defecto,
  // pero el header charset=utf-8 garantiza que ADO lo interprete correctamente.
  body: JSON.stringify(patch),
});
```

## Fallback nivel 3 — Azure CLI (último recurso)

Usar **solo** si MCP **y** REST API fallan (red, 401, TLS) y `az` está instalado. **Nunca** como alternativa directa a MCP:

```bash
export AZURE_DEVOPS_EXT_PAT="<desde .env.user-identity>"
az devops configure --defaults organization="${AZURE_ORG_URL}" project="${AZURE_PROJECT_NAME}"

# Crear (título y campos simples; evitar --description HTML largo)
az boards work-item create --type "Feature" --title "[MODULO] - Titulo" -o json

# Completar descripción: preferir segundo PATCH por API; si no, archivo temporal + REST
az boards work-item update --id <ID> --discussion "<html trazabilidad>"
```

En PowerShell, pasar `--description` como argumento único (`& az ... --description $desc`) para reducir truncamiento; si persiste, **volver a REST**.

## Tags FLIT por defecto en Features

`DOR; adopcion-ia; fase-1-diseño` (vía API en el mismo PATCH de creación).

## Skills que dependen de este contrato

- `feature-creator` — crear Features
- `flit-crear-hu` — User Stories hijas
- `bug-reporter`, `tc-formatter`, `planification-wiki`, `flit-dor-dod-validator` — lectura/escritura ADO

Al implementar o modificar cualquiera de ellas, **enlazar** `@flit-azure-devops` y no duplicar instrucciones de autenticación.