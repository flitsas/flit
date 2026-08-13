# ADR-0047: Gate de navegación dock ≡ URL

**Fecha**: 2026-08-13  
**Status**: Propuesto  
**Deciders**: Líder Técnico FLIT, Security, Frontend  
**Tags**: arquitectura, frontend, seguridad, navegacion-rbac  
**Work items**: Feature #11506 · Task #11509 · HU #11507 · HU #11508

## Contexto

El dock SPA filtra módulos por `GET /api/v1/security/modules` y claims JWT, pero la resolución de `/?m=<ModuleId>` usaba reglas distintas (`buildValidModules([]) → ALL_MODULE_IDS` y `UNIVERSAL_MODULE_IDS` ampliados). Un usuario (p. ej. radicador) podía abrir por URL módulos que no veía en el dock. La regla de producto exige: **solo se puede navegar por ruta a lo que el dock mostraría**. Las APIs siguen siendo la autorización real de datos; este ADR fija el invariante de **navegación UI**.

## Decisión

Adoptar una **fuente única en frontend** (`resolveNavigableModuleIds`) compartida entre dock y URL, con **deny-by-default** mientras carga RBAC, revalidación de `?m=` al resolver permisos, y solo `ayuda` como módulo universal de navegación. Alcance v1: SPA `/?m=` (sin reescribir middleware de `/admin` ni `/tramites`).

## Alternativas consideradas

### Opción 1: Resolver único dock ≡ URL (elegida)

**Pros:** Una sola regla; reutiliza RBAC + claims existentes; effort bajo; alinea deep-links sin edge nuevo.  
**Cons:** Enforcement solo cliente para la UI; APIs deben seguir gateando datos.  
**Esfuerzo:** S–M  
**Riesgos:** Race de carga (mitigado con hold); drift si Shell y page divergen (mitigado exportando el resolver).

### Opción 2: Middleware / edge sobre `?m=`

**Pros:** Corte antes del JS; defensa en profundidad en borde.  
**Cons:** Catálogo RBAC no vive completo en JWT; duplica lógica dock/SPA; frágil con OT hub y módulos claim-only.  
**Esfuerzo:** M–L  
**Riesgos:** Falsos 403; drift dock↔middleware.

### Opción 3: Solo endurecer APIs (sin unificar nav)

**Pros:** Poco front; datos ya parcialmente protegidos.  
**Cons:** No cumple la regla de producto (UI puede montar módulos “vacíos”); leak de existencia de módulos.  
**Esfuerzo:** S  
**Riesgos:** Falsa sensación de control de navegación.

## Tradeoff aceptado

Se elige Opción 1 porque cumple el invariante de producto con menor costo y sin duplicar el modelo de permisos en edge. Se acepta que la UI no es auth real: las APIs continúan como SoT de datos. Opción 2 queda como fase 2 si Security exige corte en borde.

## Consecuencias

### Lo que se gana
- Paridad dock ↔ URL para el mismo usuario/sesión.
- Eliminación del bypass `ALL_MODULE_IDS` en loading/error.
- Semántica clara: solo `ayuda` es universal de navegación.

### Lo que se pierde
- Deep-links “optimistas” a módulos claim-only sin permiso (comportamiento deseado).
- Fallback permisivo que facilitaba demos con RBAC lento.

### Cambios operacionales
- Nuevos módulos SPA deben entrar al resolver **y** al dock con la misma regla.
- Tests unitarios del resolver son contrato de navegación (matriz rol×módulo en QA).

## ADRs relacionados

- Ninguno supersedido. Complementa políticas SuperAdmin / Log QX / ICT ya gateadas por claim en dock y render.

## Notas para agentes

- **Backend Agent**: sin cambio obligatorio v1; no confiar en UI para autorización de datos.
- **Frontend Agent**: usar solo `resolveNavigableModuleIds`; no ampliar `UNIVERSAL_MODULE_IDS`; hold hasta `!modulesLoading`.
- **QA Agent**: matriz rol×`?m=` (radicador, OT, SuperAdmin, fallo API, loading).
- **Security Agent**: revisar que deny-by-default no reintroduzca bypass; fase 2 middleware opcional.
- **Infra Agent**: N/A v1 (sin cambio de middleware matcher).

## Referencias externas

- Feature ADO #11506 — Gate de navegación dock ≡ URL
- Código: `frontend/lib/nav/modules.ts`, `frontend/app/page.tsx`, `frontend/components/atom/Shell.tsx`
