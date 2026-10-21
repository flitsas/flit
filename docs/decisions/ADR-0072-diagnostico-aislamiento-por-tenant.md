# ADR-0072: Diagnóstico aísla por empresa con un filtro global de EF y una prueba de fuga por endpoint

**Fecha**: 2026-10-21
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana, regla FLIT 15). Redactado durante la implementación de la HU #13722.
**Tags**: arquitectura, backend, datos, seguridad, multi-tenant
**Épica / Feature / HU**: #12881 / #13715 / #13722 (F00.1-H2) · **Plan**: `docs/diagnosticos/plan-implementacion-diagnostico-vehicular-v2.md` §4 (b), RD-3

## Contexto

`core-diagnostico` es un servicio nuevo con base y esquema propios (`diagnostico`), dueño de sus tablas. Varias empresas
comparten la misma base. En el repo ya hubo fugas por un filtro olvidado: el aislamiento de `core-api` dependía de RLS sin
`FORCE ROW LEVEL SECURITY` con la app como dueña de las tablas. Comparendos filtra a mano con `Where(TenantId)` en cada
consulta. El backoffice FLIT necesita leer entre empresas (D-16), pero solo de forma acotada y auditada (ADR-0069).

## Decisión

1. **Filtro global (B2).** Toda entidad de negocio implementa `IEntidadDeEmpresa` (`TenantId`). `TenantFilter.Aplicar`
   le pone por convención un `HasQueryFilter` con la empresa del token (`IPlatformTenantAccessor`, vía
   `DiagnosticoDb.EmpresaActual`). Es cerrado por defecto: sin empresa no se ve ninguna fila. El SuperAdmin **no** se lo salta.
2. **Escritura.** Antes de `SaveChanges`, `TenantFilter.ValidarEscritura` hace tres cosas. Una entidad nueva sin
   `TenantId` toma el del token. Una fila de otra empresa, nueva, modificada o borrada, hace fallar todo el guardado
   (`EscrituraEntreEmpresasException`). Sin empresa en el token no se escribe nada.
3. **Lectura entre empresas.** Solo en `/backoffice/*`, con `IgnoreQueryFilters()`, la empresa explícita en `?empresa=` y
   una fila en `diagnostico.lecturas_backoffice` (F00.1-H5, patrón ADR-0069). `IgnoreQueryFilters()` no aparece en ningún
   otro módulo, y una prueba de arquitectura lo vigila (F00.1-H5).
4. **Pruebas obligatorias** en `tests/Flit.Diagnostico.Tests/Aislamiento/`:
   - `EntidadesConTenantTests` falla si una entidad mapeada que no es del SDK no implementa `IEntidadDeEmpresa` o no tiene el filtro.
   - `FugaEntreEmpresasTests` enumera los GET registrados (`EndpointDataSource`), salvo `/health*` y `/backoffice/*`. Si un
     GET no tiene escenario en `EscenariosDeFuga.Todos`, falla y lo nombra. Con dos empresas sembradas en el mismo
     Postgres, ninguna respuesta con el token de A contiene identificadores de B.
5. **CI no omite.** Con `CI=true`, el fixture falla si falta `ConnectionStrings__Core` o si Postgres no responde.

## Alternativas consideradas

- **B1, filtro manual (como Comparendos).** Es simple, pero basta un olvido para que haya una fuga. Se descarta.
- **B3, RLS con `FORCE ROW LEVEL SECURITY` y un rol de app que no sea dueño.** Defiende incluso ante SQL crudo. Pero exige
  otro usuario de base y `SET app.tenant` en cada conexión, choca con el pool y complica las migraciones. Queda como
  endurecimiento futuro de la plataforma.

## Tradeoff aceptado y excepciones al checklist de datos

- El filtro **no cubre SQL crudo**. Por eso `FromSql`/`ExecuteSql` sobre tablas de negocio tienen que filtrar por `tenant_id`
  de forma explícita, y el code review lo revisa.
- Los **jobs sin usuario** (cola de D2, consumidores del bus) no tienen token. Deben fijar la empresa de forma explícita
  con su propio `IPlatformTenantAccessor` en el scope; si no lo hacen, no ven ni escriben nada.
- **Sin RLS (A10) y sin FK a `identity.tenants` (A4):** la empresa vive en otro servicio y en otra base. El aislamiento lo
  dan el filtro, la validación al guardar y la prueba de fuga.
- **`lecturas_backoffice`** es auditoría de solo inserción. No lleva `updated_*`, `deleted_*` ni `row_version` (A5, A6,
  A16) ni trigger de `audit_log`, porque es ella misma la auditoría. El nombre en español sigue el modelo del plan §5,
  igual que el resto del esquema `diagnostico`.

## Consecuencias

- Una consulta nueva queda filtrada sin que nadie tenga que acordarse.
- Cada GET nuevo de D1/D2 tiene que registrar una línea en `EscenariosDeFuga.Todos`; si no, la suite falla.
- Comparendos podría migrar a este patrón, pero esa deuda queda fuera de este ADR.

## ADRs relacionados

- ADR-0069: lectura entre compañías, acotada y auditada.
- ADR-0070: gRPC entre servicios y REST hacia afuera.
