---
name: dev-tester
description: Obligatorio al terminar cualquier HU implementada (misma sesión, modo encadenado). Crea/ejecuta tests unitarios desde AC Gherkin, publica evidencias PASO 6 en ADO (un bloque por AC con tablas). No pedir confirmación previa si hu_id ya se conoce. Prohibido posponer u ofrecer como opcional. Backend .NET 10/xUnit y frontend Next.js (Jest o Vitest + RTL). Triggers dev-tester, pruebas unitarias, tests HU, evidencias unitarias, PASO 6, evidences-template, cierre técnico HU.
---

# dev-tester

Genera, ejecuta y publica evidencias de tests unitarios al cerrar técnicamente una Historia de Usuario. Se activa **inmediatamente después** de que el agente de implementación termina el código — **en la misma sesión**, sin delegar al usuario.

```
backend-agent / frontend-agent (implementa HU)
        │
        ▼  (OBLIGATORIO — misma sesión, sin preguntar si se omite)
  dev-tester  ──► PASO 1…7 completos
        │         ├── PASS → PASO 6 (plantilla completa, bloque/AC) → PASO 6b → ADO Evidences
        └────────► FAIL / BUG → informar al usuario con diagnóstico + sugerencia de corrección → BLOQUEADO (no corregir)
```

---

## Puerta bloqueante — Definition of Done técnico

**Una HU NO está implementada** hasta cumplir **todos** estos ítems. Correr `pnpm test:frontend` / `dotnet test` en local **no sustituye** este skill.

| # | Criterio | Si falla |
|---|----------|----------|
| 1 | Specs generados o actualizados según AC | Informar al usuario — no corregir el código de implementación |
| 2 | Tests ejecutados (PASO 4) con 0 fallos en nuevos y regresión del módulo | Informar al usuario con diagnóstico del bug y sugerencia de corrección — no corregir código ni tests |
| 3 | Evidencias PASO 6 completas (un bloque `### AC n` con tablas por cada AC) | Completar plantilla |
| 4 | PASO 6b (autocheck) sin placeholders `{...}` | Corregir evidencias |
| 5 | Publicado en ADO **solo** en `Custom.Evidences` (módulo Evidences) — PASO 7 | Reintentar con JSON UTF-8 correcto; si `TF26027`, estado BLOQUEADO en chat (no usar Discussion) |

**Prohibido para cualquier agente que implemente una HU:**

- Ofrecer evidencias o dev-tester como “próximo paso” o “cuando quieras”.
- Dar por cerrada la HU con solo un resumen en el chat (tests Pass/Fail sin tablas por AC).
- Cancelar, posponer o asumir que el usuario publicará evidencias manualmente.
- Interpretar “tests locales OK” como cierre técnico.

---

## Inputs requeridos

| Campo | Requerido | Descripción |
|---|---|---|
| `hu_id` | Siempre | ID de la HU en Azure DevOps |
| `branch` | Recomendado | Rama de trabajo (para `git diff`) |
| `ambiente` | Implícito | Siempre `DEV` — nunca contra QA/PDN |

Leer de `.env.user-identity`: `AZURE_ORG_URL`, `AZURE_PROJECT_NAME`, `AZURE_PAT`, `USER_REAL_NAME`, `USER_REAL_EMAIL`.
Si falta `AZURE_PAT` → mostrar plantilla completada al usuario en chat y **no** intentar ADO.

---

## PASO 0 — Identificar la HU y modo de ejecución

### Modo encadenado (por defecto tras implementar una HU)

**Aplica cuando:** el usuario pidió implementar/desarrollar una HU (`#9086`, “historia 9086”, etc.) o `frontend-agent` / `backend-agent` acaba de entregar código.

| Acción | Regla |
|--------|--------|
| `hu_id` | Usar el ID de la HU en curso. **No preguntar** si ya se conoce. |
| Confirmación al usuario | **No pedir** “¿procedo con dev-tester?”. **Ejecutar PASO 1→7 de inmediato.** |
| Notificación | Informar en una línea: `Ejecutando dev-tester para HU #{hu_id} (tests + evidencias ADO).` |
| Fin de sesión | **Prohibido** cerrar la respuesta al usuario sin haber completado PASO 7 o documentado bloqueo (sin PAT, sin AC, tests FAIL). |

### Modo standalone (solo pruebas / evidencias)

**Aplica cuando:** el usuario invoca explícitamente `dev-tester`, “publica evidencias”, “corre tests de la HU X” **sin** pedir implementación en el mismo hilo.

- Si falta `hu_id` → **una sola pregunta** pidiendo el ID; luego ejecutar PASO 1→7 **sin** segunda confirmación.
- **No usar** modo standalone para posponer lo que debió hacerse al terminar una implementación.

### Si no hay `hu_id` en ningún modo

```
dev-tester: Necesito el ID de la Historia de Usuario en Azure DevOps para continuar.
```

**No avanzar hasta recibir el ID.**

---

## PASO 1 — Obtener AC Gherkin de la HU

```http
GET {AZURE_ORG_URL}/{projectEncoded}/_apis/wit/workitems/{hu_id}?$expand=all&api-version=7.1
Authorization: Basic <base64(:PAT)>
```

Extraer `System.Title`, `Microsoft.VSTS.Common.AcceptanceCriteria` (parsear escenarios `Given/When/Then`) y `System.Tags` (para inferir módulo).
Si los AC no están en Gherkin, tratar cada ítem de lista como escenario.
**Si no hay AC → advertir y detener; no continuar.**

---

## PASO 2 — Detectar archivos modificados

```bash
git diff develop...HEAD --name-only
```

| Prefijo de ruta | Tipo |
|---|---|
| `frontend/app/` o `frontend/lib/` o `frontend/components/` | Frontend (Next.js) |
| `services/core-api/src/` | Backend (.NET) |
| `services/core-api/tests/` o `frontend/**/*.test.{ts,tsx}` o `frontend/**/__tests__/**` | Test existente — NO sobreescribir |

Sin cambios detectados → preguntar al usuario qué archivos implementó antes de continuar.

---

## PASO 3 — Generar tests unitarios

**Reglas:**
- Ubicar spec junto al archivo que testea: `*.spec.ts` / `*.spec.tsx` (FE) o `*Tests.cs` (BE).
- No sobreescribir specs existentes; agregar al final o crear archivo con sufijo `-extra.spec`.
- Mínimo por AC: **1 happy path + 1 edge case + 1 contrato**.
- Incluir `// Uso de ejemplo` (TS) o XML doc (C#) al inicio de cada describe/clase.

### 3a — Frontend (Next.js — Jest o Vitest + @testing-library/react)

**Ubicación:** `frontend/**/*.test.{ts,tsx}` o `frontend/**/__tests__/*` (según configuración del proyecto; respetar `jest.config.*` o `vitest.config.*` si existe).

#### Función pura / utilidad

```typescript
import { describe, expect, it } from "@jest/globals"; // o "vitest" según el runner del proyecto
import { <nombreFuncion> } from "./<archivo>";

// Uso de ejemplo: <nombreFuncion>({ campo: "valor" }) → resultado esperado

describe("<nombreFuncion>", () => {
  it("<AC positivo>", () => {
    expect(<nombreFuncion>(<inputValido>)).<matcher>(<valorEsperado>);
  });
  it("retorna <fallback> cuando el input es null/undefined", () => {
    expect(<nombreFuncion>(null as never)).<matcher>(<fallback>);
  });
  it("maneja string vacío sin lanzar excepción", () => {
    expect(() => <nombreFuncion>("")).not.toThrow();
  });
  it("retorna objeto con las propiedades del contrato", () => {
    const result = <nombreFuncion>(<inputValido>);
    expect(result).toHaveProperty("<prop1>");
    expect(result).toHaveProperty("<prop2>");
  });
});
```

#### Componente React

```typescript
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "@jest/globals"; // o "vitest"
import { <NombreComponente> } from "./<NombreComponente>";

// Uso de ejemplo: <NombreComponente> recibe { prop1, onAction } y renderiza ...

const defaultProps = { /* props mínimas */ };

describe("<NombreComponente>", () => {
  it("renderiza sin errores con props válidas", () => {
    render(<NombreComponente {...defaultProps} />);
    expect(screen.getByRole("<role>", { name: /<texto>/i })).toBeInTheDocument();
  });
  it("muestra estado vacío cuando no hay datos", () => {
    render(<NombreComponente {...defaultProps} items={[]} />);
    expect(screen.getByText(/<sin resultados|vacío>/i)).toBeInTheDocument();
  });
  it("no lanza cuando onAction es undefined", () => {
    expect(() =>
      render(<NombreComponente {...defaultProps} onAction={undefined} />)
    ).not.toThrow();
  });
  it("todos los botones tienen aria-label o texto visible", () => {
    render(<NombreComponente {...defaultProps} />);
    screen.getAllByRole("button").forEach((btn) => {
      expect(btn.getAttribute("aria-label") || btn.textContent?.trim()).toBeTruthy();
    });
  });
});
```

#### Zod schema (contrato de respuesta backend)

```typescript
import { describe, expect, it } from "@jest/globals"; // o "vitest"
import { <NombreSchema> } from "./schemas";

// Uso de ejemplo: NombreSchema.parse(apiResponse)

describe("<NombreSchema> — contrato backend", () => {
  it("acepta respuesta válida del backend", () => {
    expect(() => <NombreSchema>.parse({ /* payload válido */ })).not.toThrow();
  });
  it("rechaza respuesta cuando falta campo obligatorio", () => {
    const { campoObligatorio: _, ...sinCampo } = { /* payload completo */ };
    expect(() => <NombreSchema>.parse(sinCampo)).toThrow();
  });
  it("coerciona fecha string a Date si el schema lo define", () => {
    const result = <NombreSchema>.parse({ /* payload con fecha string */ });
    // verificar tipo del campo fecha
  });
});
```

### 3b — Backend (.NET 10 / xUnit)

**Proyecto:** `services/core-api/tests/Flit.<Modulo>.Tests/` (o proyecto de tests existente en la solución `Flit.slnx`).
Si no existe, crearlo:

```bash
cd services/core-api
dotnet new xunit -n Flit.<Modulo>.Tests -o tests/Flit.<Modulo>.Tests
dotnet sln Flit.slnx add tests/Flit.<Modulo>.Tests/Flit.<Modulo>.Tests.csproj
dotnet add tests/Flit.<Modulo>.Tests/Flit.<Modulo>.Tests.csproj reference \
  src/Flit.<Modulo>.Application/Flit.<Modulo>.Application.csproj \
  src/Flit.<Modulo>.Domain/Flit.<Modulo>.Domain.csproj
```

#### Use Case (Application layer)

```csharp
using FluentAssertions;
using Moq;
using Xunit;
using FLIT.<Modulo>.Application.<Accion><Entidad>;
using FLIT.<Modulo>.Domain;

namespace FLIT.<Modulo>.Tests;

/// <summary>
/// Uso de ejemplo:
/// var uc = new <Accion><Entidad>UseCase(repoMock.Object);
/// var result = await uc.ExecuteAsync(new <Accion><Entidad>Command { ... });
/// </summary>
public class <Accion><Entidad>UseCaseTests
{
    private readonly Mock<I<Entidad>Repository> _repoMock = new();

    [Fact]
    public async Task ExecuteAsync_ConDatosValidos_DebeRetornar<ResultadoEsperado>()
    {
        _repoMock.Setup(r => r.<Metodo>(It.IsAny<...>())).ReturnsAsync(<entidadValida>);
        var result = await new <Accion><Entidad>UseCase(_repoMock.Object)
            .ExecuteAsync(new <Command> { /* props */ });
        result.Should().NotBeNull();
        result.<Propiedad>.Should().Be(<valorEsperado>);
    }

    [Fact]
    public async Task ExecuteAsync_CuandoNoExiste_DebeLanzar<NotFoundException>()
    {
        _repoMock.Setup(r => r.<Metodo>(It.IsAny<...>())).ReturnsAsync((I<Entidad>?)null);
        await Assert.ThrowsAsync<<NotFoundException>>(
            () => new <Accion><Entidad>UseCase(_repoMock.Object)
                      .ExecuteAsync(new <Command> { /* props */ }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ExecuteAsync_ConCampoVacioONulo_DebeLanzarValidationException(string? campoInvalido)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => new <Accion><Entidad>UseCase(_repoMock.Object)
                      .ExecuteAsync(new <Command> { Campo = campoInvalido }));
    }

    [Fact]
    public async Task ExecuteAsync_DebeInvocarRepositorioUnaVez()
    {
        _repoMock.Setup(r => r.<Metodo>(It.IsAny<...>())).ReturnsAsync(<entidad>);
        await new <Accion><Entidad>UseCase(_repoMock.Object)
            .ExecuteAsync(new <Command> { /* props */ });
        _repoMock.Verify(r => r.<Metodo>(It.IsAny<..>()), Times.Once);
    }
}
```

---

## PASO 4 — Ejecutar los tests

### Frontend

```bash
pnpm test:frontend -- --reporter=verbose <ruta/al/archivo.test.tsx> 2>&1 | tee /tmp/dev-tester-frontend-results.txt
echo "EXIT_CODE:$?"
```

Omitir la ruta para correr toda la suite. Capturar: `passed` / `failed` / `skipped`, mensajes de error y cobertura (`pnpm test:coverage` desde raíz si disponible).

### Backend

```bash
dotnet test services/core-api/Flit.slnx --logger "console;verbosity=detailed" \
  2>&1 | tee /tmp/dev-tester-backend-results.txt
echo "EXIT_CODE:$?"
```

### Detección de regresiones

```bash
# Frontend: tests que importan los módulos modificados
rg "<nombre-modulo>" frontend --glob "*.test.*" -l
# Backend: suite del módulo afectado
dotnet test services/core-api/Flit.slnx --filter "FullyQualifiedName~<Modulo>"
```

Test preexistente que falla = **regresión de contrato** — reportar con severidad alta antes de continuar.

---

## PASO 5 — Evaluar resultados

| Resultado            | Criterio                                       | Acción |
|----------------------|------------------------------------------------|--------|
| ✅ Todo PASS         | 0 fallos en nuevos y preexistentes             | PASO 6 + PASO 6b (checklist) → publicar en ADO (PASO 7) |
| ⚠️ Nuevos FAIL       | Al menos un nuevo test falla                   | Informar al usuario con diagnóstico del bug y sugerencia de corrección — **no corregir** — estado BLOQUEADO |
| 🔴 Regresión contrato | Un test preexistente que antes pasaba ahora falla | Informar como regresión con severidad alta y sugerencia — **no corregir** — estado BLOQUEADO |
| ⏭ Sin tests nuevos  | No se generó ningún spec                       | Reportar advertencia al usuario |

```
dev-tester: [FALLO] {N} test(s) fallaron en HU #{hu_id}

Archivos afectados:
  - <ruta/al/spec.ts o archivo.cs>: "<nombre del test>" → <mensaje de error>

🐛 Bug detectado: <descripción clara del problema encontrado>

Sugerencia de corrección:
  - <archivo afectado>, línea <n>: <qué cambiar y por qué>
  - Ejemplo: cambiar `<código actual>` por `<código correcto>`

⚠️  dev-tester NO modifica el código de implementación.
    Aplica la corrección manualmente y vuelve a ejecutar /dev-tester.
    No mover la HU a Resolved hasta que todos los tests pasen.
```

---

## PASO 6 — Preparar evidencias con la plantilla (OBLIGATORIO)

**Formato canónico FLIT:** el mismo que otras HUs del proyecto (ej. Tiendas): **un bloque completo por cada AC** con tablas de entrada/salida HTTP. **Prohibido** publicar solo resumen (tabla Pass/Fail + lista de nombres de tests).

Lee y completa **solo** la plantilla canónica [evidences-template.md](assets/evidences-template.md): **todos** los placeholders `{...}` y **todas** las secciones (encabezado, resumen, tests generados, regresiones, salida completa, un bloque por AC). **No modificar la estructura** del archivo.

Luego convierte ese markdown completado a HTML según las reglas de conversión del **PASO 7** (tablas con **`style` inline en `<table>`, `<th>` y `<td>`** — ADO elimina `border="1"`; `### AC n` → `<h3>`; logs en `<div style="...">` con `<br/>`). Ese HTML es lo que se publica en `Custom.Evidences`.

**Prohibido:** HTML resumido o inventado (solo título y tres filas), omitir secciones de la plantilla, o publicar markdown crudo en ADO.

### 6.1 — Placeholders globales

Completar **todos** los placeholders `{...}` del encabezado, resumen, specs, regresiones y salida completa:

| Placeholder | Valor a sustituir |
|-------------|-------------------|
| `{hu_id}` | ID numérico de la HU |
| `{System.Title}` | Título obtenido de ADO en PASO 1 |
| `{YYYY-MM-DD HH:MM}` | Fecha y hora actual |
| `{USER_REAL_NAME}` | Valor de `.env.user-identity` |
| `{branch}` | Rama activa (`git branch --show-current`) |
| `{fe_specs}` / `{be_specs}` | Cantidad de archivos spec creados o ejecutados |
| `{fe_tests}` / `{be_tests}` | Cantidad de tests (`it` / `[Fact]`) |
| `{fe_pass}` / `{be_pass}` | Tests que pasaron |
| `{fe_fail}` / `{be_fail}` | Tests que fallaron |
| `{fe_skip}` / `{be_skip}` | Tests omitidos |
| `{total_*}` | Suma de frontend + backend |
| `{lista_specs_frontend}` | Lista markdown de rutas `.spec.ts(x)` |
| `{lista_specs_backend}` | Lista markdown de rutas `.cs` de test |
| `{regresiones}` | `Ninguna` o lista de tests preexistentes que fallaron |
| `{output_frontend}` | Salida **completa** de `pnpm test:frontend` (archivo temporal o tee) |
| `{output_backend}` | Salida **completa** de `dotnet test` (archivo temporal o tee) |

### 6.2 — Un bloque por cada AC (obligatorio)

Por **cada** criterio de aceptación de la HU (AC1, AC2, …), incluir la sección completa de la plantilla:

```markdown
### AC {n} — {título del escenario}

- **Tipo de test:** Happy path | Edge case | Contrato
- **Resultado:** ✅ Pass | ❌ Fail | ⏭ N/A (con motivo explícito)

#### Datos de entrada
| Campo | Valor |
| Tipo de petición | GET / POST / … |
| Endpoint | ruta canónica del contrato (ej. `/api/v1/procedures`) |
| Parámetros de ruta | … o `—` |
| Parámetros de búsqueda | … o `—` |
| Cuerpo (body) | JSON o `—` |

#### Datos de salida esperados
| Código de respuesta | … |
| Cuerpo de la respuesta | forma esperada según AC |

#### Datos de salida obtenidos
| Código de respuesta | … ✓ |
| Cuerpo de la respuesta | resultado real o equivalente del test |
```

**Tests solo unitarios (sin HTTP):** igualmente rellenar las tablas mapeando el **contrato del AC** en “esperado” y el **resultado del test/use case** en “obtenido”. Indicar en una línea: _Verificación vía `[Fact]` / `it` — no integración HTTP en esta ejecución._ Citar el nombre del test.

**AC no ejecutable en esta HU** (ej. export sin endpoint): bloque con **Resultado: ⏭ N/A** y motivo; no omitir el AC.

**Vincular tests:** en cada bloque AC, mencionar el `[Fact]` o `it` que lo cubre.

El documento resultante (plantilla completada) es la **única** evidencia que se publica en ADO. **No crear archivos locales adicionales** en el repo.

---

## PASO 6b — Autocheck antes de publicar (puerta obligatoria)

**No pasar al PASO 7** hasta marcar mentalmente todos los ítems. Si alguno falla, completar PASO 6 antes de continuar.

- [ ] Existe **un bloque `### AC n`** por cada AC de la HU (incluidos N/A documentados)
- [ ] Cada bloque tiene tablas **Datos de entrada**, **salida esperados** y **salida obtenidos**
- [ ] No queda ningún placeholder `{...}` sin sustituir
- [ ] La sección **Salida completa** incluye el log íntegro de `dotnet test` / `pnpm test:frontend` (no solo 3–5 líneas resumidas)
- [ ] El HTML **no** es un informe alternativo (resumen + lista de tests sin tablas por AC)
- [ ] El HTML refleja **todas** las secciones de `evidences-template.md` (resumen, tests generados, regresiones, salida completa, AC1..n)
- [ ] Los logs usan `<div style="font-family:Consolas,...">` (no `<pre>`)
- [ ] **Todas** las tablas del HTML llevan borde visible vía `style` inline en cada celda (no solo `border="1"` en `<table>`)

Si el autocheck falla, mostrar al usuario qué ítem falta y corregir antes de PATCH a ADO.

---

## PASO 7 — Publicar evidencias en ADO

Usar el contrato de `flit-azure-devops` (REST API como primera opción).

### Destino obligatorio: módulo **Evidences** (`Custom.Evidences`)

| Campo ADO | Módulo en la UI | Uso |
|-----------|-----------------|-----|
| `Custom.Evidences` | **Evidences** | **Único destino** del reporte PASO 6 (tests unitarios, E2E, etc.) |
| `System.History` | **Discussion** | **Prohibido** para evidencias de dev-tester — solo trazabilidad breve de otros skills (`@flit-crear-hu`, merge, etc.) |

**Prohibido** publicar el HTML del PASO 6 en `System.History` salvo que la API devuelva **explícitamente** `TF26027` (campo `Custom.Evidences` inexistente en el proceso). Un `400 Bad Request` **no** autoriza usar Discussion: suele ser JSON mal formado (p. ej. `ConvertTo-Json` en PowerShell) — corregir y reintentar en `Custom.Evidences`.

### 7a — PATCH a `Custom.Evidences`

```http
PATCH {AZURE_ORG_URL}/{projectEncoded}/_apis/wit/workitems/{hu_id}?api-version=7.1
Content-Type: application/json-patch+json; charset=utf-8

[{ "op": "add", "path": "/fields/Custom.Evidences", "value": "<html del reporte>" }]
```

**Serialización JSON (obligatoria):**

- Python: `json.dumps(patch, ensure_ascii=False).encode("utf-8")`
- PowerShell: cuerpo en archivo `.json` UTF-8 (`Get-Content -Raw -Encoding UTF8`); **no** `ConvertTo-Json` del documento Patch completo
- Node: `JSON.stringify(patch)` + header `charset=utf-8`

Si el ítem ya tenía contenido en Evidences, usar `"op": "replace"` en lugar de `"add"`.

### 7b — Si `Custom.Evidences` no existe (`TF26027` únicamente)

**No** usar `System.History` como sustituto. Acciones:

1. Declarar en el chat: `BLOQUEADO: el proceso no tiene campo Custom.Evidences (TF26027)`.
2. Publicar la plantilla PASO 6 **completa** en el chat para copia manual.
3. **No** dar por cerrada la HU técnicamente hasta que un humano confirme dónde registrar evidencias.

### 7c — Errores que NO activan Discussion

| Código / síntoma | Causa habitual | Acción |
|------------------|----------------|--------|
| `400` / patch document invalid | JSON mal serializado | Reintentar con archivo UTF-8 o Python `ensure_ascii=False` |
| `401` / `403` | PAT o permisos | Verificar `.env.user-identity`; estado BLOQUEADO |
| Timeout / red | Conectividad | Reintentar PATCH; no publicar en Discussion |

**Conversión markdown → HTML para ADO:**

- **Tablas (obligatorio — bordes visibles):** Azure DevOps **elimina** el atributo `border` de `<table>` (igual que `style` en `<pre>`). Usar **siempre** estilos inline en la tabla y en **cada** `<th>` / `<td>`:

```html
<table style="border-collapse:collapse;width:100%;margin:8px 0;">
  <tr>
    <th style="border:1px solid #cccccc;padding:6px 8px;background-color:#f0f0f0;text-align:left;">Campo</th>
    <th style="border:1px solid #cccccc;padding:6px 8px;background-color:#f0f0f0;text-align:left;">Valor</th>
  </tr>
  <tr>
    <td style="border:1px solid #cccccc;padding:6px 8px;">…</td>
    <td style="border:1px solid #cccccc;padding:6px 8px;">…</td>
  </tr>
</table>
```

  - **Prohibido** confiar en `border="1"`, `cellpadding` o `cellspacing` solos (el espaciado se ve, los bordes no).
  - Aplicar el mismo patrón a la tabla **Resumen**, tablas por AC y cualquier tabla auxiliar.
- Bloques `### AC n` → `<h3>AC n — …</h3>`
- Salida de consola → `<div style='font-family:Consolas,monospace;white-space:pre-wrap;background-color:#f5f5f5;border:1px solid #ccc;padding:10px;font-size:12px;line-height:1.5'>…</div>` (saltos de línea como `<br/>`)
- **No usar** `<pre>` para logs (ADO puede eliminar estilos y dejar el bloque vacío)

---

## Output final al usuario

### Todo PASS

```
dev-tester: [OK] Tests unitarios completados para HU #{hu_id}

Specs creados: {lista de archivos}
Tests totales: {n_total} — ✅ {n_pass} pass | ❌ {n_fail} fail | ⏭ {n_skip} skip
Regresiones: ninguna

Evidencias publicadas en ADO (HU #{hu_id} — módulo Evidences / Custom.Evidences)
Formato: plantilla completa PASO 6 (bloques por AC con tablas HTTP)

La HU puede avanzar a estado Resolved si el usuario/DoD lo confirma.
```

### Con fallos

```
dev-tester: [FALLO] {n_fail} test(s) fallaron en HU #{hu_id}

Tests fallidos:
{lista de tests fallidos con mensaje de error completo}

Regresiones de contrato detectadas: {ninguna | lista}

🐛 Bug(s) detectado(s):
{descripción del problema: qué falló, en qué archivo, en qué línea}

Sugerencia de corrección:
{para cada bug:
  - Archivo: <ruta>
  - Problema: <qué está mal>
  - Corrección sugerida: cambiar `<código actual>` por `<código correcto>`
  - Motivo: <explicación breve>}

⚠️  dev-tester NO modifica el código de implementación.
    Aplica la(s) corrección(es) manualmente y vuelve a ejecutar /dev-tester.
    No mover la HU a Resolved hasta que todos los tests pasen.
```

---

## Integración con agentes de implementación

Cuando `frontend-agent` o `backend-agent` terminen el código:

1. **Leer esta skill completa** (no resumir de memoria).
2. **Ejecutar dev-tester en modo encadenado** con el mismo `hu_id`.
3. **No** proponer PR, rama ni “resumen de implementación” como mensaje final hasta pasar la puerta bloqueante de arriba.
4. Si los tests fallan por un bug en el código: mostrar el diagnóstico y la sugerencia de corrección al usuario. **No corregir el código** — declarar estado `BLOQUEADO: tests fallidos — corrección pendiente por el desarrollador`.
5. Si ADO falla por credenciales: publicar en el chat la plantilla PASO 6 **completa** y declarar `BLOQUEADO: evidencias no publicadas en ADO — falta .env.user-identity`.

---

## Restricciones

- **Nunca** corregir bugs encontrados en el código de implementación — solo diagnosticar e informar al usuario con sugerencia de corrección.
- **Nunca** modificar archivos de implementación (`src/`, `app/`, etc.) como parte de la ejecución de dev-tester.
- **Nunca** re-ejecutar los tests automáticamente después de detectar un bug — esperar que el usuario aplique la corrección y vuelva a invocar `/dev-tester`.
- **Nunca** sobreescribir specs existentes — solo agregar al final o crear archivo con sufijo `-extra.spec`.
- **Nunca** hacer mock de la función que se está testeando (solo dependencias externas).
- **Nunca** usar datos reales de producción en fixtures de tests.
- **Nunca** publicar HTML abreviado sin completar antes `assets/evidences-template.md` íntegra.
- **Nunca** publicar evidencias PASO 6 en `System.History` (Discussion) — destino exclusivo: `Custom.Evidences` (salvo `TF26027` documentado).
- **Nunca** interpretar un `400` en PATCH como permiso para usar Discussion.
- **Nunca** publicar en ADO sin al menos un test ejecutado con éxito.
- **Nunca** marcar la HU como Resolved si hay tests fallidos.
- **Nunca** publicar en ADO un informe resumido (solo tabla Pass/Fail + lista de nombres de tests) **sin** los bloques `### AC n` con tablas de entrada/salida del PASO 6.
- **Nunca** saltar el PASO 6b (autocheck) ni el PASO 7 si el autocheck no pasa.
- **Nunca** omitir un AC de la HU en evidencias — si no aplica, bloque con **⏭ N/A** y motivo.
- **Siempre** incluir el bloque de uso de ejemplo (`// Uso de ejemplo` / XML doc) en cada spec.
- **Siempre** correr los tests preexistentes del módulo afectado para detectar regresiones.
- **Siempre** completar la plantilla `assets/evidences-template.md` (PASO 6) antes de cualquier PATCH a ADO.
- Si `.env.user-identity` no existe: mostrar la **plantilla PASO 6 completa** al usuario en el chat, marcar estado **BLOQUEADO** y **no** dar por cerrada la HU; **no** crear archivos en el repo.
- **Nunca** pedir confirmación previa en modo encadenado (prohibido PASO 0b tipo “¿procedo?”).
- Si el proyecto de tests backend no existe: crearlo siguiendo la estructura del repo antes de escribir specs.
- Mínimo recomendado por HU: **1 happy path + 1 edge case + 1 contrato** por AC.
