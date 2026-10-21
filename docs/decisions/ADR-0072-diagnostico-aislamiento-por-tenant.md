# ADR-0072: Diagnóstico aísla por empresa con un filtro global de EF y una prueba de fuga por endpoint

**Fecha**: 2026-10-21
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana, regla FLIT 15). Redactado durante la implementación de la HU #13722.
**Tags**: arquitectura, backend, datos, seguridad, multi-tenant
**Épica / Feature / HU**: #12881 / #13715 / #13722 (F00.1-H2), ampliado en #13736 (configuración por compañía) · **Plan**: `docs/diagnosticos/plan-implementacion-diagnostico-vehicular-v2.md` §4 (b), §5, RD-3

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

## Excepciones al checklist en `configuracion_empresa` y `cambios_contrato` (HU #13736)

Migración `D1_ConfiguracionEmpresa` (`20261021083622`); mapeo en `Persistence/Configuraciones/ConfiguracionEmpresaConfig.cs`
y `CambioContratoConfig.cs`. El DDL sigue el plan §5 (D-04 = b, R-2, R-11b, R-18). Ambas entidades implementan
`IEntidadDeEmpresa`, así que el filtro global y `ValidarEscritura` las cubren igual que al resto (por eso A4 y A10 quedan
cubiertos por la sección anterior y no se repiten aquí).

### `diagnostico.configuracion_empresa`

| Ítem | Desviación | Justificación |
|------|------------|---------------|
| A3 | La PK es `tenant_id`, no `id uuid` | La relación con la empresa es 1:1 por definición (plan §5: «una fila por compañía»). Con `tenant_id` como PK la unicidad la da la propia clave, el filtro global coincide con la PK y la lectura es una búsqueda por clave. Un `id` sustituto no lo referencia nadie: `cambios_contrato` apunta a la empresa, no a la fila. |
| A5 | Sin `created_at/by` ni `updated_at/by` genéricos | La tabla tiene dos dueños con permisos distintos (D-04): el contractual lo edita el backoffice FLIT y el operativo el admin de la compañía. Un único `updated_*` borraría quién cambió qué nivel; por eso hay `contrato_actualizado_por/en` y `operativa_actualizada_por/en`. La creación no es un evento de negocio: la fila nace con los DEFAULT de la base, y el primer cambio contractual queda en `cambios_contrato`. |
| A5, A6 | Sin `deleted_at/by` (sin borrado lógico) | No hay flujo de baja: la configuración vive lo que vive la empresa y no tiene retención ni purga (R-18). Un borrado lógico obligaría a filtrar `deleted_at` en una tabla con exactamente una fila viva por clave, y la FK `ON DELETE RESTRICT` desde `cambios_contrato` impide además el borrado físico de una configuración auditada. |
| A5, A16, B8 | Concurrencia con `xmin` en vez de `row_version` + trigger `BEFORE UPDATE` | `xmin` es la columna de sistema que Postgres cambia en cada `UPDATE`, también los que llegan por SQL crudo, sin columna ni trigger que mantener. Npgsql la mapea con `IsRowVersion()`, que la marca como token de concurrencia: B8 se cumple en lo funcional. La comparación es por igualdad, así que el desbordamiento de 32 bits no afecta, y `VACUUM FREEZE` no reescribe `xmin` desde Postgres 9.4. |
| A16 | Sin trigger de `audit_log` | El esquema `diagnostico` no tiene tabla `audit_log` (es otro servicio y otra base). Los cambios contractuales, que son los que fijan alcance y cobro, quedan con valor anterior y nuevo en `cambios_contrato`. Los operativos son preferencias de la compañía y conservan solo el último autor y momento (plan §5). Si el LT exige historial operativo, la vía es ampliar `cambios_contrato` con una columna `nivel`, no un trigger genérico. |
| A2 | Nombre en singular y en español | Sigue el modelo del plan §5, como el resto del esquema; el singular expresa que hay una fila por empresa. |

### `diagnostico.cambios_contrato`

| Ítem | Desviación | Justificación |
|------|------------|---------------|
| A4 | FK a `configuracion_empresa(tenant_id)`, no a `identity.tenants` | `identity.tenants` está en otra base. La FK local garantiza lo que sí puede: no se audita un cambio de una configuración que no existe. |
| A5, A6, A16 | Sin `updated_*`, `deleted_*`, `row_version` ni trigger de `audit_log` | Igual que `lecturas_backoffice`: es auditoría de solo inserción y ella misma es el registro. `usuario_id` y `fecha` cumplen el papel de `created_by/at`. |
| A13 | `valor_anterior` y `valor_nuevo` en `jsonb` | Los campos auditados tienen tipos distintos (arreglo de fuentes, entero, booleano). `jsonb` guarda cada valor con su tipo sin una columna por campo; A13 prohíbe `json`, no `jsonb`. `valor_anterior` es nulo solo cuando no había valor. |
| A8 | `ON DELETE RESTRICT` explícito; `ON UPDATE` implícito (`NO ACTION`) | EF Core no modela `ON UPDATE` y no lo emite. El efecto es el correcto: la clave referenciada es la empresa, que es inmutable (`ValueGeneratedNever`, nunca se reasigna), y si alguien intentara cambiarla con SQL crudo, `NO ACTION` rechaza el cambio. Para una FK no diferible, `NO ACTION` y `RESTRICT` solo se distinguen en el momento de la comprobación. `CASCADE` sería incorrecto: reescribiría la historia auditada. `RESTRICT` en el borrado hace lo mismo con el borrado: una configuración con auditoría no se puede eliminar. |

### Alternativas consideradas para estas tablas

- **C1, aplicar el checklist tal cual:** `id` sustituto con `uq_configuracion_empresa_tenant`, columnas `created_*`/`updated_*`/
  `deleted_*`/`row_version` con su trigger. Cumple la letra de A3, A5, A6 y A16, pero añade una clave que nadie usa, un
  `updated_*` que mezcla los dos niveles de D-04 y un borrado lógico sin flujo que lo dispare. Se descarta.
- **C2, `ON UPDATE` explícito con SQL crudo en la migración** (`ALTER TABLE … DROP CONSTRAINT` y volver a crearla con
  `ON UPDATE RESTRICT`). Cumple A8 a la letra, pero el modelo de EF y el `ModelSnapshot` dejan de describir la base: un
  cambio futuro de la FK generaría una migración que la vuelve a crear sin la cláusula. El beneficio es nulo porque la
  clave no cambia. Se descarta; si la plataforma lo exige, la vía es un `IMigrationsSqlGenerator` propio que emita la
  cláusula en todas las FK del servicio, no un parche en una migración.
- **C3, la opción elegida:** el modelo del plan §5 con las desviaciones documentadas arriba. Mantiene el modelo de EF como
  la fuente de verdad del esquema y deja la integridad en CHECKs de la base (subconjunto, límite positivo, códigos
  válidos) y en la FK con `RESTRICT`.

### Pendiente: «solo inserción» no está garantizado por la base

`cambios_contrato` y `lecturas_backoffice` son de solo inserción por convención del código; nada en la base impide un
`UPDATE` o un `DELETE`, y la aplicación es dueña de las tablas (sin RLS, sección anterior). Esta parte **no se justifica**:
para una tabla de auditoría la garantía tiene que estar en la base. El cambio propuesto, con precedente en el repo
(trigger de inmutabilidad de `procedure_instance_field_values`), es un trigger `BEFORE UPDATE OR DELETE` que lance
excepción en las dos tablas (`tr_cambios_contrato_solo_insercion`, `tr_lecturas_backoffice_solo_insercion`), creado por
`migrationBuilder.Sql` en una migración nueva del `database-agent`, con prueba de integración que intente ambos comandos.

## Consecuencias

- Una consulta nueva queda filtrada sin que nadie tenga que acordarse.
- Cada GET nuevo de D1/D2 tiene que registrar una línea en `EscenariosDeFuga.Todos`; si no, la suite falla.
- Comparendos podría migrar a este patrón, pero esa deuda queda fuera de este ADR.

## ADRs relacionados

- ADR-0069: lectura entre compañías, acotada y auditada.
- ADR-0070: gRPC entre servicios y REST hacia afuera.
