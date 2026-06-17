# Evidencias de Tests Unitarios — HU #{hu_id}

<!-- NOTA ADO (tablas): al publicar en Custom.Evidences, convertir cada tabla markdown a HTML con
     style inline en <table>, <th> y <td> (border:1px solid #cccccc; padding:6px 8px).
     ADO elimina border="1" en <table>; sin style inline los bordes no se ven (solo queda el espaciado).
     Ver PASO 7 en dev-tester/SKILL.md — ejemplo canónico de <table>. -->

**Historia:** {System.Title}
**Fecha:** {YYYY-MM-DD HH:MM}
**Ejecutado por:** dev-tester bajo supervisión de @{USER_REAL_NAME}
**Rama:** {branch}

---

## Resumen

| Capa      | Specs nuevos | Tests nuevos | ✅ Pass | ❌ Fail | ⏭ Skip |
|-----------|-------------|-------------|--------|--------|--------|
| Frontend  | {fe_specs}  | {fe_tests}  | {fe_pass} | {fe_fail} | {fe_skip} |
| Backend   | {be_specs}  | {be_tests}  | {be_pass} | {be_fail} | {be_skip} |
| **Total** | **{total_specs}** | **{total_tests}** | **{total_pass}** | **{total_fail}** | **{total_skip}** |

---

## Tests generados

### Frontend

{lista_specs_frontend}

> Ejemplo de entrada esperada:
> - `frontend/lib/<feature>/<modulo>.test.ts` — {n} tests

### Backend

{lista_specs_backend}

> Ejemplo de entrada esperada:
> - `services/core-api/tests/Flit.<Modulo>.Tests/<Entidad>Tests.cs` — {n} tests

---

## Regresiones detectadas

{regresiones}

> Valores posibles: `Ninguna` | lista de tests preexistentes que fallaron tras los cambios de la HU.

---

## Salida completa

<!-- NOTA ADO: usar <div style="font-family:monospace;white-space:pre-wrap;..."> en lugar de <pre>.
     Azure DevOps elimina el atributo style de <pre> y <code>, dejando el bloque en blanco.
     El style en <div> sí se preserva. Convertir saltos de línea a <br/>. -->

**Frontend — pnpm test:frontend ({fe_pass} pass / {fe_fail} fail)**

<div style="font-family:Consolas,monospace;white-space:pre-wrap;background-color:#f5f5f5;border:1px solid #cccccc;padding:10px;font-size:12px;line-height:1.5">{output_frontend}</div>

**Backend — dotnet test ({be_pass} pass / {be_fail} fail)**

<div style="font-family:Consolas,monospace;white-space:pre-wrap;background-color:#f5f5f5;border:1px solid #cccccc;padding:12px 14px;font-size:12px;line-height:1.9">{output_backend}</div>

<!-- NOTA íconos: usar ✅ (U+2705) antes de cada línea PASS y agrupar por AC separando con línea vacía.
     Ejemplo de línea: ✅ CreateTienda_ConDatosValidos_RetornaResponseConId [3 ms]
     Saltos de línea dentro del <div>: usar <br/> como separador. -->

---

## Criterios de Aceptación cubiertos

### AC {ac_id_1} — {escenario_1}

- **Tipo de test:** {tipo_test_1} _(Happy path | Edge case | Contrato)_
- **Resultado:** {resultado_1} _(✅ Pass | ❌ Fail)_

#### Datos de entrada _(solo si aplica)_

| Campo | Valor |
|-------|-------|
| Tipo de petición | `{método_http_1}` _(GET \| POST \| PUT \| PATCH \| DELETE)_ |
| Endpoint | `{endpoint_1}` |
| Parámetros de ruta | `{path_params_1}` |
| Parámetros de búsqueda | `{query_params_1}` |
| Cuerpo (body) | `{body_1}` |

#### Datos de salida esperados

| Campo | Valor |
|-------|-------|
| Código de respuesta | `{expected_status_1}` |
| Cuerpo de la respuesta | `{expected_body_1}` |

#### Datos de salida obtenidos

| Campo | Valor |
|-------|-------|
| Código de respuesta | `{actual_status_1}` |
| Cuerpo de la respuesta | `{actual_body_1}` |

---

### AC {ac_id_n} — {escenario_n}

- **Tipo de test:** {tipo_test_n} _(Happy path | Edge case | Contrato)_
- **Resultado:** {resultado_n} _(✅ Pass | ❌ Fail)_

#### Datos de entrada _(solo si aplica)_

| Campo | Valor |
|-------|-------|
| Tipo de petición | `{método_http_n}` _(GET \| POST \| PUT \| PATCH \| DELETE)_ |
| Endpoint | `{endpoint_n}` |
| Parámetros de ruta | `{path_params_n}` |
| Parámetros de búsqueda | `{query_params_n}` |
| Cuerpo (body) | `{body_n}` |

#### Datos de salida esperados

| Campo | Valor |
|-------|-------|
| Código de respuesta | `{expected_status_n}` |
| Cuerpo de la respuesta | `{expected_body_n}` |

#### Datos de salida obtenidos

| Campo | Valor |
|-------|-------|
| Código de respuesta | `{actual_status_n}` |
| Cuerpo de la respuesta | `{actual_body_n}` |

---
