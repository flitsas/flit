# ADR-0072: Gate de navegación por permiso efectivo (menú, URL directa y middleware)

**Fecha**: 2026-10-16
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana — regla FLIT 15), Security, Frontend
**Tags**: arquitectura, frontend, seguridad, navegacion-rbac, middleware, epica-12753
**Work items**: Épica #12753 · Feature F-C #13102
**Supersedes**: ninguno. **Extiende** [ADR-0047](ADR-0047-gate-navegacion-dock-igual-url.md) (Propuesto): conserva su invariante dock ≡ URL y su resolver único, y cierra lo que ese ADR dejó para "fase 2" (corte en el servidor para `/admin/*` y `/empresa/*`).
**Decisión de forma**: se crea un ADR nuevo, y no una enmienda, porque cambia la decisión de ADR-0047 en un punto (el alcance v1 "sin reescribir middleware de `/admin`" deja de valer) y agrega un requisito nuevo (menú ≡ API). ADR-0047 recibe solo una línea de referencia cruzada en "ADRs relacionados".

## Contexto

Con permisos individuales (ADR-0071) el conjunto de módulos que ve una persona deja de coincidir con "lo que da su rol". La Feature F-C (#13102) exige:

1. Un módulo sin permiso efectivo no aparece en el menú.
2. La URL directa a ese módulo (SPA `/?m=`, `/admin/*`, `/empresa/*`) redirige al inicio y muestra un AlertCard "No tienes acceso a este módulo."
3. El menú no muestra módulos que la API rechazaría. Hoy hay módulos protegidos en la API solo por política de rol (no por permiso), y el cliente no tiene cómo saberlo.

Estado actual:

- ADR-0047 resolvió `/?m=` con `resolveNavigableModuleIds` (`frontend/lib/nav/modules.ts`), deny-by-default, dock ≡ URL. Es solo cliente.
- `frontend/middleware.ts` valida `/admin/*` (SuperAdmin) y `/empresa/*` (AdminCompany o SuperAdmin) únicamente por **rol**, mediante `frontend/lib/auth/guard.ts` (`evaluateAdminAccess`, `evaluateEmpresaAccess`), y cae a `/403`. Funciona con la cookie legacy `flit_token` y con la sesión OIDC (`claimsTokenFromCookieHeader`). El matcher ya cubre `/`, `/tramites`, `/profile`, `/log-qx`, `/admin`, `/empresa`. (En el árbol actual no existen páginas bajo `frontend/app/empresa`; el gate está listo y las páginas llegan con la épica.)
- `GET /api/v1/security/modules` entrega los códigos accesibles desde BD (`ListAccessibleModulesHandler`), no desde el token.
- El token lleva `permissions` y `roles` (ADR-0071 los calcula efectivos), así que el middleware tiene los datos sin llamar a la API.
- El host de la Suite (A-07/A-11): `frontend-hub/` solo redirige rutas legadas de Trámites con 308 (`FLIT_TRAMITES_HOST_ENABLED`) y lista productos con `me-apps`; no decide módulos de Trámites. El gate que importa vive en el middleware de `frontend/` (Trámites). Si más adelante otro producto muestra módulos propios, necesitará la misma tabla (ver Consecuencias).

## Decisión

Se adopta la **Opción 2**: **una tabla declarativa única de requisitos de acceso** por módulo/ruta, consumida por tres puntos, más un redirect uniforme.

1. **Tabla única** `MODULE_ACCESS` (`frontend/lib/nav/module-access.ts`): para cada módulo SPA y cada prefijo `/admin/*` y `/empresa/*`, el requisito (permiso efectivo, rol, o ambos) expresado como datos y evaluado por una función pura `canAccess(claims, requirement)`. Sin ramas sueltas en `Shell.tsx`, `page.tsx` o `guard.ts`.
2. **Consumidores**: (a) `resolveNavigableModuleIds` (menú y `/?m=`, sigue siendo la fuente única de ADR-0047, ahora alimentado por la tabla); (b) `guard.ts` / `middleware.ts` para `/admin/*` y `/empresa/*`; (c) un test de contrato que compara la tabla con las políticas reales de la API.
3. **Menú ≡ API**: los módulos hoy protegidos solo por política de rol se declaran en la tabla con ese requisito de rol, y el test de contrato (c) fallará si la API cambia su política y la tabla no. Como complemento, `GET /api/v1/security/modules` debe devolver ya filtrados los módulos que la política de API rechazaría, de modo que `accessibleCodes` sea el techo y la tabla el filtro de claims.
4. **Redirección uniforme**: sin acceso, redirigir a `/` con la marca `?acceso=denegado&modulo=<id>`. La página de inicio muestra el AlertCard "No tienes acceso a este módulo." y limpia la marca de la URL (`router.replace`). En el middleware, `/admin/*` y `/empresa/*` sin acceso usan esta redirección en lugar de `/403` cuando la persona tiene sesión válida; sin sesión siguen yendo al login. `/403` se reserva para fallos de sesión.
5. **Deny-by-default** se mantiene: claims ilegibles, sesión vencida o catálogo en carga ⇒ sin acceso (hold, como ADR-0047).
6. **Las APIs siguen siendo la autorización real.** Este gate es defensa en profundidad de la navegación, no sustituye ningún control de datos.
7. `/?m=` se queda resuelto en cliente (necesita el `accessibleCodes` de la API y el AlertCard); el middleware no duplica ese resolver, evitando el drift que ADR-0047 temía. Lo que sí hace el middleware es lo que depende solo de claims.

## Alternativas consideradas

### Opción 1: Solo cliente, resolver ampliado

**Pros:**
- Mínimo cambio: se amplía `resolveNavigableModuleIds` con los requisitos de `/admin` y `/empresa`.
- Coherente con ADR-0047 tal cual; un solo lugar.
- Fácil de probar con Vitest.

**Contras:**
- `/admin/*` y `/empresa/*` se renderizan (o parten a renderizar) antes de que el cliente corte; destello de contenido y bundle servido a quien no debería.
- Dos reglas distintas para la misma ruta: el middleware (rol) y el cliente (permiso). Justo el drift que la épica quiere evitar.
- No hay corte si el JS falla o se desactiva.

**Esfuerzo:** S
**Riesgos:** inconsistencia middleware ↔ cliente; hallazgo de Security por falta de corte en el servidor.

### Opción 2: Tabla declarativa única + middleware con decodificación del claim (elegida)

**Pros:**
- Un solo contrato de acceso para menú, `?m=` y rutas de servidor.
- El corte en `/admin` y `/empresa` ocurre antes de renderizar, con el mismo guard que ya existe.
- Sin llamadas a la API en el borde: usa los claims `permissions` y `roles` ya emitidos (ADR-0071).
- El test de contrato detecta que el menú ofrezca algo que la API rechaza.

**Contras:**
- El middleware depende de que el token lleve los permisos efectivos (ADR-0071): si un token pasa de 4 KB o cambia de formato, hay que ajustar.
- Requiere mantener la tabla alineada con las políticas de API (mitigado con el test).
- En modo OIDC el token del middleware es por producto (`"tramites"`), así que `/admin` y `/empresa` deben apoyarse en permisos de ese producto y de `plataforma`.

**Esfuerzo:** M
**Riesgos:** falsos negativos si la tabla queda desactualizada tras agregar módulos (mitigado: un módulo sin entrada en la tabla se niega).

### Opción 3: Layout / server component por ruta

**Pros:**
- Puede consultar la API (`/security/modules`) en el servidor y decidir con el catálogo completo.
- No depende del tamaño del token.

**Contras:**
- Una consulta de red por navegación en el servidor; más latencia y un punto de fallo.
- Dispersa la decisión en cada layout (`admin`, `empresa`, futuras): fácil olvidar uno.
- No cubre `/?m=` (query en la raíz) ni evita que el middleware y los layouts discrepen.

**Esfuerzo:** M-L
**Riesgos:** rutas nuevas sin layout protegido; costo de rendimiento.

## Tradeoff aceptado

La Opción 2 mueve parte de la responsabilidad al borde, que ADR-0047 evitó por miedo a duplicar el modelo de permisos. Aquí no se duplica: se **centraliza** en una tabla que el resolver también usa, y se acota el middleware a lo que se decide solo con claims. Se acepta mantener esa tabla alineada con la API mediante un test de contrato, a cambio de una única regla y un corte real en el servidor. La Opción 1 queda como mínimo viable si Security no exige corte en el servidor.

## Consecuencias

### Lo que se gana
- Misma regla para el dock, `/?m=`, `/admin/*` y `/empresa/*`.
- Experiencia clara: inicio + AlertCard en vez de un 403 seco.
- Protección contra menú que ofrece algo que la API rechaza.

### Lo que se pierde
- El detalle de acceso depende de la frescura del token (hasta el próximo inicio de sesión, ver ADR-0071).
- Un lugar más a actualizar al crear un módulo (la tabla), con un test que lo obliga.

### Cambios operacionales
- Si `frontend-hub` o un producto nuevo muestra módulos propios, deberá consumir la misma tabla; se recomienda moverla a `packages/auth` cuando exista un segundo consumidor (hoy solo `frontend/`). Decisión diferida.
- Sin cambio de infraestructura: el middleware ya corre en Node y lee sesión legacy u OIDC.

## ADRs relacionados

- ADR-0047: extendido por este ADR (no supersedido).
- ADR-0071: define los permisos efectivos que viajan en el token.
- ADR-0023: catálogo global de roles (los requisitos de rol de la tabla se refieren a él).

## Notas para agentes

- **Frontend Agent**: crear `module-access.ts`; migrar `resolveNavigableModuleIds`, `guard.ts` y `Shell.tsx` a la tabla; AlertCard + limpieza de la marca; no ampliar `UNIVERSAL_MODULE_IDS`; módulo sin entrada ⇒ negado.
- **Backend Agent**: `GET /security/modules` debe devolver filtrado por las mismas políticas de API; exponer (o documentar) la política de cada módulo para el test de contrato.
- **QA Agent**: matriz rol × permiso individual × (menú, `/?m=`, `/admin/*`, `/empresa/*`, sin sesión, token vencido, modo legacy y OIDC).
- **Security Agent**: confirmar que el gate de borde es defensa en profundidad y que ninguna ruta de datos depende de él.
- **Infra Agent**: N/A.

## Dudas abiertas para el Líder Técnico

1. ¿Se mantiene `/403` para sesión válida sin acceso a `/admin/*`, o se unifica con inicio + AlertCard como propone el PO para F-C?
2. ¿Se cambia `GET /security/modules` para filtrar por política de rol (opción propuesta) o se prefiere publicar los requisitos y que el cliente los evalúe?
3. ¿Mover la tabla a `packages/auth` desde ya (para el hub) o esperar a un segundo consumidor?

## Referencias

- ADR-0047, ADR-0071
- `frontend/lib/nav/modules.ts`, `frontend/lib/auth/guard.ts`, `frontend/middleware.ts`, `frontend-hub/middleware.ts`
- `services/core-api/src/Flit.Modules.Security.Application/Modules/ListAccessibleModulesHandler.cs`
- Feature ADO #13102
