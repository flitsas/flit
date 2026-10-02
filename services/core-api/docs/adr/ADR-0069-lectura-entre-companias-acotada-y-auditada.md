# ADR-0069: Lectura de trámites entre compañías acotada a un componente exclusivo y auditada

**Fecha**: 2026-09-30
**Status**: Propuesto
**Deciders**: David Alejandro Chica Hernandez (PO), Claude Code (agente de implementación). Aceptación: Líder Técnico.
**Tags**: arquitectura, backend, seguridad, multi-tenant, integraciones, habeas-data
**Épica / Feature**: #12737 / #13066, #13067 (HU #13076, #13077, #13086, #13080) · **Contrato**: `docs/integraciones/external-api-tramites-sync.md` (v3.1)

## Contexto

Toda lectura de trámites en core-api es por compañía: el tenant sale del JWT, el
`TenantEnforcementMiddleware` lo fija y RLS lo respalda. El feed externo rompe esa regla a propósito:
Flito necesita los trámites de **todas** las compañías en una sola consulta. Restricciones:

1. La excepción no puede volverse un atajo que otro código reutilice para saltarse el aislamiento.
2. Debe quedar rastro de qué se entregó y a quién (Ley 1581, HU #13086).
3. El rol de core-api es **propietario** de las tablas de `tramites`, que no declaran `FORCE ROW LEVEL
   SECURITY`: las políticas ya no se le aplican. El aislamiento real en código lo dan los filtros de
   EF y el middleware, no RLS.
4. No hay presupuesto para una base réplica ni un servicio aparte en esta fase.

## Decisión

1. **Un solo componente puede leer entre compañías**: `ProcedureSyncReadRepository`, dentro de
   `ExternalSyncReadScope` (internal): transacción `REPEATABLE READ READ ONLY` y
   `SET LOCAL row_security = off` como **aserción** (si el rol dejara de saltarse RLS, la consulta
   falla en vez de devolver un feed vacío). SQL parametrizado, solo lectura.
2. **Exclusividad verificada por prueba de arquitectura** (`ExternalSyncScopeArchitectureTests`):
   `ExternalSyncReadScope` solo puede aparecer en su archivo y en el repositorio, y no puede ser público.
3. **Alcance fijo en el SQL**, igual en todas sus consultas: trámites radicados alguna vez, nunca los
   migrados desde FLIT 1; la factura (HU #13077), además, sin borrado lógico y solo de tipo `factura`.
4. **Puerta de entrada única**: `/api/v1/external/*`, con el pase de un cliente de integración (ADR-0067)
   y su permiso `external.tramites.read`. Los datos personales de compradores van enmascarados salvo
   `external.tramites.pii.read`.
5. **Auditoría por solicitud** en `integrations.external_access_log` (DDL 127): cliente, endpoint, rango
   de versiones, cantidad, compañías tocadas, si hubo datos personales en claro, IP, duración y código;
   también los rechazos.

## Alternativas consideradas

### Opción 1: Recorrer compañía por compañía con el tenant fijado (RLS por tenant)

**Pros:** - No crea ninguna excepción al aislamiento: cada consulta ve una sola compañía.
**Cons:** - Rompe el cursor global (ADR-0068): habría un cursor por compañía y el consumidor tendría
que conocer y seguir la lista de compañías, que cambia. - N consultas por página; coste lineal en el
número de compañías. - El rol propietario se salta RLS de todas formas, así que no añade garantía real.
**Esfuerzo:** L
**Riesgos:** Feed incoherente entre compañías; compañías nuevas olvidadas por el consumidor.

### Opción 2: Rol de base de datos dedicado con `BYPASSRLS` y conexión propia

**Pros:** - La excepción queda en la base: otro código de core-api no puede usarla sin esa conexión.
- Permisos de solo lectura por `GRANT`.
**Cons:** - Segunda cadena de conexión y segundo rol que gestionar en DEV, QA y PDN (secretos,
rotación, pool). - Hoy RLS no protege al rol principal (restricción 3): el rol dedicado no cambia el
modelo de amenaza mientras el principal siga siendo propietario. - Migraciones y pruebas deben
conocer el rol.
**Esfuerzo:** M
**Riesgos:** Operación más compleja sin ganancia de seguridad efectiva en esta fase. Queda como
evolución natural si algún día se activa `FORCE ROW LEVEL SECURITY` en `tramites`.

### Opción 3: Componente exclusivo en código + prueba de arquitectura + bitácora (elegida)

**Pros:** - Una consulta por página, coherente con el cursor global. - La exclusividad se verifica en
cada build. - Sin infraestructura nueva. - La bitácora cubre la obligación de rendición de cuentas.
**Cons:** - La garantía es de código, no de base de datos: un desarrollador podría escribir otra
consulta sin tenant fuera del ámbito (la prueba solo vigila el uso del ámbito, no toda consulta sin
filtro). - Depende de que la prueba de arquitectura no se desactive.
**Esfuerzo:** M
**Riesgos:** Mitigado por revisión de código y por la prueba; revisable si se adopta la opción 2.

## Tradeoff aceptado

Se acepta una garantía de código verificada en CI, en lugar de una garantía del motor, porque en el
estado actual (rol propietario sin `FORCE RLS`) la garantía del motor no existe para ningún componente.
A cambio se obtiene un feed coherente de una sola consulta y sin operación nueva. La bitácora hace que
cada entrega sea trazable.

## Consecuencias

### Lo que se gana
- Una sola puerta, un solo componente y una sola forma de leer entre compañías, todas verificables.
- Rastro de cada entrega para Habeas Data.

### Lo que se pierde
- Defensa en profundidad a nivel de motor para esta lectura (tampoco existe hoy para las demás).

### Cambios operacionales
- Revisar `integrations.external_access_log` ante cualquier consulta de Habeas Data sobre Flito.
- Depuración de la bitácora a 12 meses: fase 2.
- Si se activa `FORCE ROW LEVEL SECURITY` en `tramites`, esta lectura fallará a propósito (aserción
  `row_security = off`) y habrá que adoptar la opción 2.

## ADRs relacionados

- ADR-0066 — marca de agua de sincronización: qué se lee.
- ADR-0067 — clientes de integración externos: quién lee.
- ADR-0068 — cursor keyset opaco: cómo se pagina la lectura.

## Notas para agentes

- **Backend Agent**: toda lectura nueva entre compañías va DENTRO de `ProcedureSyncReadRepository` y
  con el mismo alcance en SQL. No usar `IgnoreQueryFilters()` ni consultas sin tenant fuera de él.
- **QA Agent**: verificar que un adjunto de otro trámite, un trámite migrado o uno nunca radicado dan
  404/no aparecen, y que cada solicitud deja su fila en la bitácora (también 401/403/429).
- **Security Agent**: la prueba `ExternalSyncScopeArchitectureTests` es parte del control; su
  desactivación es un hallazgo. La bitácora no guarda cuerpos, datos de trámites, secretos ni pases.
- **Infra Agent**: sin rol ni conexión nuevos. No activar `FORCE ROW LEVEL SECURITY` en `tramites`
  sin coordinar la opción 2.
- **Frontend Agent**: sin impacto.

## Referencias externas

- PostgreSQL, Row Security Policies (propietarios y `FORCE ROW LEVEL SECURITY`): https://www.postgresql.org/docs/16/ddl-rowsecurity.html
- Ley 1581 de 2012 (Colombia), protección de datos personales.
