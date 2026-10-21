# ADR-0072: Diagnóstico aísla por empresa con un filtro global de EF, una prueba de fuga por endpoint y una regla propia de schema

**Fecha**: 2026-10-21
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana, regla FLIT 15). Redactado durante la implementación de la HU #13722.
**Tags**: arquitectura, backend, datos, seguridad, multi-tenant
**Épica / Feature / HU**: #12881 / #13715 / #13722 (F00.1-H2), ampliado en #13736 (configuración por compañía) y #13738 (cabecera e historial de estados), y convertido en regla general del schema `diagnostico` para D1 y D2 · **Plan**: `docs/diagnosticos/plan-implementacion-diagnostico-vehicular-v2.md` §4 (b), §5, RD-3

## Contexto

`core-diagnostico` es un servicio nuevo con base y esquema propios (`diagnostico`), dueño de sus tablas. Varias empresas
comparten la misma base. En el repo ya hubo fugas por un filtro olvidado: el aislamiento de `core-api` dependía de RLS sin
`FORCE ROW LEVEL SECURITY` con la app como dueña de las tablas. Comparendos filtra a mano con `Where(TenantId)` en cada
consulta. El backoffice FLIT necesita leer entre empresas (D-16), pero solo de forma acotada y auditada (ADR-0069).

El checklist de datos (`.claude/skills/db-schema-validator/checklist-validacion-schema.md`) está pensado para `core-api`:
columnas genéricas `created_*`/`updated_*`/`deleted_*`, `row_version` con trigger, `audit_log` común y FK con `ON UPDATE`
explícito. Ese modelo no encaja con un servicio que no tiene `audit_log`, no borra nada (R-18) y lleva su propia
auditoría de negocio. Justificar las mismas desviaciones tabla por tabla en cada migración no escala: D1 y D2 añaden
vehículos, celdas, intentos, dictámenes, derivaciones y evidencias (plan §5). Por eso este ADR fija una **regla general
del schema `diagnostico`** y deja las excepciones por tabla solo para lo que se salga de ella.

## Decisión

### Parte 1: aislamiento por empresa

1. **Filtro global (B2).** Toda entidad de negocio implementa `IEntidadDeEmpresa` (`TenantId`). `TenantFilter.Aplicar`
   le pone por convención un `HasQueryFilter` con la empresa del token (`IPlatformTenantAccessor`, vía
   `DiagnosticoDb.EmpresaActual`). Es cerrado por defecto: sin empresa no se ve ninguna fila. El SuperAdmin **no** se lo salta.
2. **Escritura.** Antes de `SaveChanges`, `TenantFilter.ValidarEscritura` hace tres cosas. Una entidad nueva sin
   `TenantId` toma el del token. Una fila de otra empresa, nueva, modificada o borrada, hace fallar todo el guardado
   (`EscrituraEntreEmpresasException`). Sin empresa en el token no se escribe nada.
3. **Lectura entre empresas.** Solo en `/backoffice/*`, con `IgnoreQueryFilters()`, la empresa explícita en `?empresa=` y
   una fila en `diagnostico.lecturas_backoffice` (F00.1-H5, patrón ADR-0069). `IgnoreQueryFilters()` no aparece en ningún
   otro módulo, y una prueba de arquitectura lo vigila (F00.1-H5). La **escritura** del backoffice sobre otra empresa no
   está decidida: ver «Pendiente: vía de escritura del backoffice».
4. **Pruebas obligatorias** en `tests/Flit.Diagnostico.Tests/Aislamiento/`:
   - `EntidadesConTenantTests` falla si una entidad mapeada que no es del SDK no implementa `IEntidadDeEmpresa` o no tiene el filtro.
   - `FugaEntreEmpresasTests` enumera los GET registrados (`EndpointDataSource`), salvo `/health*` y `/backoffice/*`. Si un
     GET no tiene escenario en `EscenariosDeFuga.Todos`, falla y lo nombra. Con dos empresas sembradas en el mismo
     Postgres, ninguna respuesta con el token de A contiene identificadores de B.
5. **CI no omite.** Con `CI=true`, el fixture falla si falta `ConnectionStrings__Core` o si Postgres no responde.

### Parte 2: regla del schema `diagnostico` frente al checklist de datos

Toda tabla del schema con `tenant_id` pertenece a una de dos clases, y solo a una.

**(a) Entidades de negocio** (estado mutable: `configuracion_empresa`, `diagnosticos`, y en D1/D2 `vehiculos`,
`consultas`, `dictamenes`, `derivaciones`, `evidencias`).

- **Concurrencia con `xmin`** mapeada con `IsRowVersion()`, en lugar de `row_version` + trigger `BEFORE UPDATE` (A16, B8).
  `xmin` es la columna de sistema que Postgres cambia en cada `UPDATE`, también los que llegan por SQL crudo, sin
  columna ni trigger que mantener. La comparación es por igualdad, así que el desbordamiento de 32 bits no afecta, y
  `VACUUM FREEZE` no reescribe `xmin` desde Postgres 9.4. B8 se cumple en lo funcional.
- **Autoría propia del dominio**, en lugar de `created_*`/`updated_*` genéricos (A5). Cada tabla guarda quién y cuándo
  con columnas que nombran el hecho de negocio (`usuario_creador_id`/`creado_en`, `contrato_actualizado_por/en`,
  `operativa_actualizada_por/en`, `usuario_override_id`/`override_en`, `canjeado_en`…), en `timestamptz` y con el usuario
  como seudónimo (`claim sub`, comentario `@pii:low`). Un `updated_*` único mezclaría hechos con dueños distintos. Cuando
  la historia de un cambio importa, la lleva un historial de la clase (b), no un trigger.
- **Sin borrado lógico** (A5, A6). No hay `deleted_at/by`: el producto no tiene flujo de baja ni retención ni purga
  (R-18). Lo que deja de valer se marca con su propio estado de negocio (`vigente`, `estado`, `cerrado_en`), y las FK con
  `RESTRICT` impiden además el borrado físico de filas con historia.
- **Sin triggers de auditoría genéricos** (A16). El schema no tiene `audit_log` (es otro servicio y otra base). La
  auditoría la dan los historiales de la clase (b) y las columnas de autoría.

**(b) Historiales y auditorías** (`lecturas_backoffice`, `cambios_contrato`, `historial_estado_diagnostico`, y en D2
`intentos_consulta`).

- **Solo inserción.** No llevan `updated_*`, `deleted_*`, `row_version`/`xmin` ni trigger de `audit_log` (A5, A6, A16):
  son ellas mismas el registro. Sus columnas de actor e instante (`usuario_id`, `fecha`/`en`/`inicio`) cumplen el papel
  de `created_by/at`; el actor es nulo solo cuando el hecho lo produce el procesador.
- La entidad expone solo `init` (salvo `TenantId`, que asigna `ValidarEscritura`) y el código no las modifica ni las borra.
  Hoy la base **no** lo impide: ver «Pendiente: solo inserción».

**(c) FK internas** (entre tablas del schema).

- **Preferiblemente compuestas con `tenant_id`**: la hija apunta a `(tenant_id, <padre>_id)` y el padre expone
  `uq_<tabla>_tenant_id UNIQUE (tenant_id, id)` (en EF, `HasPrincipalKey(x => new { x.TenantId, x.Id })`). Así la fila y su
  padre son de la misma empresa también ante SQL crudo, que el filtro global no cubre. La FK simple solo vale cuando la
  clave del padre ya es `tenant_id` (caso `cambios_contrato` → `configuracion_empresa`). El DDL de referencia del plan §5
  dibuja FK simples en `vehiculos`, `consultas`, `intentos_consulta`, `dictamenes` y `derivaciones`; al materializarlas,
  el `database-agent` las crea compuestas o las justifica como excepción en este ADR.
- **`ON UPDATE` implícito (`NO ACTION`)** (A8). EF Core no modela `ON UPDATE` y no lo emite. Es correcto porque las claves
  referenciadas son inmutables: `id` es uuid v7 generado por la aplicación (`ValueGeneratedNever`, sin setter público) y
  `tenant_id` no se reasigna (`ValidarEscritura` rechaza el cambio). Si alguien la cambiara por SQL crudo, `NO ACTION`
  rechaza el cambio; en una FK no diferible, `NO ACTION` y `RESTRICT` solo se distinguen en el momento de la comprobación.
- **`ON DELETE` explícito.** `RESTRICT` por defecto, y siempre que la hija sea un historial o auditoría (b): `CASCADE`
  reescribiría la historia. `CASCADE` solo se admite para una hija de composición pura de una entidad (a), sin valor de
  auditoría propio, y debe figurar en la tabla de aplicabilidad. Hoy ninguna tabla lo usa.

**(d) Criterios para que una tabla nueva quede cubierta sin tocar este ADR.** Una tabla con `tenant_id` queda cubierta
si cumple todos; si falla uno, la migración no se mergea sin una excepción escrita en la sección de excepciones por tabla.

1. `tenant_id uuid NOT NULL`, la entidad implementa `IEntidadDeEmpresa` y `EntidadesConTenantTests` pasa.
2. Pertenece a una sola clase, (a) o (b), y el comentario de la tabla lo dice (las de (b) terminan en «Solo inserción.»).
3. Clase (a): `xmin` con `IsRowVersion()`; autoría con columnas del dominio en `timestamptz`; sin `deleted_*`; sin triggers.
4. Clase (b): sin token de concurrencia, sin columnas de edición ni borrado, con actor e instante; entidad con `init`.
5. Toda columna `<tabla>_id` que apunte a una tabla del schema tiene FK, compuesta con `tenant_id` según (c), con
   `ON DELETE` explícito y sin `ON UPDATE` emitido.
6. PK `id uuid` v7 generada por la aplicación (`ValueGeneratedNever`), salvo relación 1:1 con la empresa (PK `tenant_id`).
7. Nombres en `snake_case` y en español según el plan §5; enumeraciones cerradas con `CHECK`; `jsonb` (nunca `json`) con
   `CHECK` de forma (`jsonb_typeof`); PII con comentario `@pii:*` y documentos cifrados (plan §9.3).
8. Si un GET la expone, su escenario está en `EscenariosDeFuga.Todos`.

**Fuera de la regla:** las tablas sin `tenant_id` (catálogos globales del plan §5: `reglas_dictamen`, `organismos`,
`departamentos_impuesto`) y las del SDK (outbox, inbox, historial de migraciones). Las primeras necesitan su propia
justificación al materializarse, porque `EntidadesConTenantTests` las rechazaría: `organismos` y `departamentos_impuesto`
ya la tienen (HU #13723, ver excepciones por tabla).

### Aplicabilidad a las tablas existentes

| Tabla | HU · migración | Clase | Concurrencia | Autoría | FK internas | Excepciones particulares |
|-------|----------------|-------|--------------|---------|-------------|--------------------------|
| `lecturas_backoffice` | #13722 · `D0_AislamientoYAuditoria` | (b) | — | `usuario_id`, `fecha` | Ninguna (`empresa_consultada` es otra empresa, sin FK) | `tenant_id` es la empresa **del lector** (FLIT), no la consultada |
| `configuracion_empresa` | #13736 · `D1_ConfiguracionEmpresa` | (a) | `xmin` | `contrato_actualizado_por/en`, `operativa_actualizada_por/en` | — | A3: PK `tenant_id`; A2: nombre en singular |
| `cambios_contrato` | #13736 · `D1_ConfiguracionEmpresa` | (b) | — | `usuario_id`, `fecha` | `tenant_id` → `configuracion_empresa(tenant_id)`, `RESTRICT` (simple, válida por (c)) | A13: `valor_anterior`/`valor_nuevo` en `jsonb` |
| `diagnosticos` | #13738 · `D1_CabeceraDiagnostico` | (a) | `xmin` | `usuario_creador_id`, `creado_en`, `cerrado_en`; transiciones en el historial | — (expone `uq_diagnosticos_tenant_id`) | Ninguna |
| `historial_estado_diagnostico` | #13738 · `D1_CabeceraDiagnostico` | (b) | — | `usuario_id` (nulo si fue el procesador), `en` | `(tenant_id, diagnostico_id)` → `diagnosticos(tenant_id, id)`, `RESTRICT` | Ninguna |
| `vehiculos` | #13735 · `D1_Vehiculos`; #13723 · `D2_MotorConsultas`; #13730b · `D2_LocatarioDeclaradoRespaldo` | (a) | `xmin` | Del lote (`diagnosticos`); `runt_consultado_en` | `(tenant_id, diagnostico_id)` → `diagnosticos(tenant_id, id)`, `RESTRICT`; expone `uq_vehiculos_tenant_id` (D2) | Ninguna. PII `@pii:high`: `propietario_doc_cifrado`, `locatario_doc_cifrado` y, desde #13730b, `locatario_declarado_doc_cifrado` (respaldo del declarado cuando el proveedor trae el suyo; `ck_vehiculos_locatario_declarado` y `ck_vehiculos_locatario_declarado_con_proveedor`) |
| `consultas` | #13723 · `D2_MotorConsultas` | (a) | `xmin` | `actualizado_en`, `usuario_manual_id`, `dato_obtenido_en`; intentos en `intentos_consulta` | `(tenant_id, vehiculo_id)` → `vehiculos`, `(tenant_id, diagnostico_id)` → `diagnosticos`, `(tenant_id, reutilizada_de_id)` → `consultas`; todas `RESTRICT`; expone `uq_consultas_tenant_id` | A11 en `ix_cola` (ver excepciones) |
| `intentos_consulta` | #13723 · `D2_MotorConsultas`; #13737 · `D2_IntentosConsultaRegistro` | (b) | — | `usuario_id` (nulo si fue el procesador), `inicio`/`fin` | `(tenant_id, consulta_id)` → `consultas`, `(tenant_id, vehiculo_id)` → `vehiculos`, `(tenant_id, diagnostico_id)` → `diagnosticos`; todas `RESTRICT` | Ninguna |
| `organismos` | #13723 · `D2_MotorConsultas` | Catálogo global (sin `tenant_id`) | — | — | Ninguna | A3, A4, A5, A20 (ver excepciones) |
| `departamentos_impuesto` | #13723 · `D2_MotorConsultas` | Catálogo global (sin `tenant_id`) | — | `actualizado_en` | Ninguna | A3, A4, A5, A20 (ver excepciones) |

### Excepciones por tabla

- **`configuracion_empresa`, A3 (PK `tenant_id`, no `id uuid`).** La relación con la empresa es 1:1 por definición (plan
  §5: «una fila por compañía»). La unicidad la da la propia clave, el filtro global coincide con la PK y la lectura es
  una búsqueda por clave. Un `id` sustituto no lo referencia nadie: `cambios_contrato` apunta a la empresa, no a la fila.
- **`configuracion_empresa`, A2 (singular).** Sigue el plan §5; el singular expresa que hay una fila por empresa.
- **`configuracion_empresa`, autoría por nivel.** Tiene dos dueños con permisos distintos (D-04): el contractual lo edita
  el backoffice FLIT y el operativo el admin de la compañía. La fila nace con los DEFAULT de la base, el primer cambio
  contractual queda en `cambios_contrato` y los operativos conservan solo el último autor y momento (plan §5). Si el LT
  exige historial operativo, la vía es ampliar `cambios_contrato` con una columna `nivel`, no un trigger genérico.
- **`cambios_contrato`, A4 (FK local, no a `identity.tenants`).** `identity.tenants` está en otra base; la FK local
  garantiza lo que sí puede: no se audita un cambio de una configuración que no existe.
- **`cambios_contrato`, A13 (`jsonb`).** Los campos auditados tienen tipos distintos (arreglo de fuentes, entero,
  booleano); `jsonb` guarda cada valor con su tipo. `valor_anterior` es nulo solo cuando no había valor.
- **`organismos` y `departamentos_impuesto`, catálogos globales (HU #13723).** Son datos de FLIT, iguales para todas las
  empresas: la réplica de organismos de tránsito con su departamento (patrón Comparendos HU13459) y la caché del rpc de
  capacidad de impuesto de core-consultas (R-9, R-23). No llevan `tenant_id` ni filtro global, y `EntidadesConTenantTests`
  las excluye por una **lista cerrada** (`CatalogosGlobales`): sumar otro catálogo exige ampliar esta excepción. A3: la
  clave es la natural del catálogo (código del organismo, código DIVIPOLA del departamento), que es la que usa quien busca.
  A5/A20: no tienen `created_*`, `is_active` ni `external_refs`; la réplica se reemplaza desde su origen y
  `departamentos_impuesto.actualizado_en` marca el último refresco. No se editan a mano.
- **`consultas`, A11 en `ix_cola` (HU #13723).** El índice parcial de la cola `(disponible_desde) WHERE estado IN
  ('PENDIENTE','EN_CURSO')` no empieza por `tenant_id`: el procesador (#13727) toma celdas de todas las empresas en orden
  de disponibilidad con `FOR UPDATE SKIP LOCKED`, y luego fija la empresa de cada celda en su scope (Tradeoff aceptado,
  «jobs sin usuario»). Los demás índices de la tabla empiezan por `tenant_id`.
- **`consultas`, unicidad `(vehiculo_id, fuente)`.** Se materializa como `ux_consulta_vehiculo_fuente (tenant_id,
  vehiculo_id, fuente)`: como el vehículo fija la empresa, la unicidad es la misma, y el índice cubre la FK compuesta
  al vehículo (igual que `ux_vehiculo_por_lote`).

## Retención de datos personales

**Decisión del usuario del 2026-10-21, sigue en Propuesto.** Los documentos de propietario y locatario
(`vehiculos.propietario_doc_cifrado`, `vehiculos.locatario_doc_cifrado` y el respaldo del declarado
`vehiculos.locatario_declarado_doc_cifrado` de HU #13730b, comentario `@pii:high`) y los demás datos del
diagnóstico se **conservan indefinidamente**. Es lo coherente con la Parte 2: sin borrado lógico, historiales de solo
inserción y FK con `RESTRICT`. Y es lo que dice R-18 del plan («No se borra nada»; plan §1, §5, §9.3 y §10): no hay
`retencion_dias`, `retencion_dias_max`, `ck_retencion` ni job de purga.

**Justificación.**

- **Trazabilidad del dictamen.** El veredicto, las reglas disparadas y un override se explican con los datos con los que
  se dictaminó, incluido a quién se consultó.
- **Consultas pagadas.** Cada consulta a un proveedor tiene costo y alimenta la volumetría y la conversión del
  backoffice. Borrar el documento rompería la conciliación de lo consumido.
- **Auditoría.** `historial_estado_diagnostico`, `lecturas_backoffice`, `cambios_contrato` y, en D2, `intentos_consulta`
  son de solo inserción. Un historial que apunta a filas purgadas deja de ser auditoría.
- **Reproducibilidad del PDF.** Un PDF de evidencia (D7) se tiene que poder regenerar igual, también cuando la redacción
  IA lo invalida y lo vuelve a generar (R-2b).

**Controles compensatorios** (plan §9.3).

- **Cifrado en aplicación** con ASP.NET DataProtection. El anillo de llaves vive en `diagnostico.data_protection_keys`
  (HU #13735) y se protege con un certificado que está **fuera de la base**: quien lea la base no lee los documentos.
- **Fuera de logs y eventos.** El documento nunca va en logs, eventos del outbox, DTO de listado ni prompt del LLM (R-2).
  El contenido de las filas de carga no se escribe en logs.
- **Enmascarado en las respuestas** («****» y los 4 últimos), también en el detalle de celda (R-13).
- **Acceso por permiso y aislamiento por empresa.** Las lecturas pasan por el permiso de la ruta y por el filtro global
  de la Parte 1.
- **Lecturas del backoffice auditadas** en `lecturas_backoffice`, antes de leer (ADR-0073).

**Consecuencias y riesgos frente a la Ley 1581.**

- Choca con el **principio de finalidad y temporalidad**: el dato se conserva sin un plazo atado a la finalidad (plan
  §11.1, riesgo «Conservación indefinida de documentos cifrados»).
- El **derecho de supresión** del titular no tiene vía hoy. Ningún endpoint ni proceso suprime un documento, y las FK con
  `RESTRICT` y los historiales impiden borrar la fila del vehículo.
- **Requiere el aval del Líder Técnico y la revisión de Legal** antes de liberar a clientes. R-18 figura como «PO + Legal,
  pendiente de ratificar».
- **Si Legal exige supresión** (fuera del alcance de D0–D2, entraría como decisión nueva que modifique R-18): la vía
  compatible con esta regla es el **borrado criptográfico**. Cada documento se cifra con una llave propia, por titular o
  por empresa, y suprimir es destruir esa llave. La fila, los historiales y las FK quedan intactos y el documento queda
  ilegible. Hace falta un modelo de llaves por titular o empresa (hoy el anillo es único del servicio), un registro de
  solicitudes de supresión con su propia auditoría, y definir qué muestran el detalle y el PDF de un documento suprimido.
  La otra vía, un plazo fijo con job de purga, choca con los historiales de solo inserción y con las FK `RESTRICT`.

**Estado en el código (2026-10-21).** Las columnas y la tabla `data_protection_keys` existen (`D1_Vehiculos`,
`DataProtectionKeyConfig.cs`). Desde la HU #13751 (commit 7180d4dfa) Data Protection está conectado
(`Plataforma/ProteccionDocumentos.cs`): `PersistKeysToDbContext<DiagnosticoDb>()` guarda el anillo en la base y
`ProtectKeysWithCertificate` lo protege con un certificado `.pfx` montado como secreto, fuera de la base
(`Diagnostico:DataProtection:CertificadoRuta` y `CertificadoClave`). Es fail-closed: sin certificado válido el servicio no
arranca. El documento del locatario ya se cifra con un `IDataProtector`.

## Alternativas consideradas

### Para el aislamiento

- **B1, filtro manual (como Comparendos).** Es simple, pero basta un olvido para que haya una fuga. Se descarta.
- **B3, RLS con `FORCE ROW LEVEL SECURITY` y un rol de app que no sea dueño.** Defiende incluso ante SQL crudo. Pero exige
  otro usuario de base y `SET app.tenant` en cada conexión, choca con el pool y complica las migraciones. Queda como
  endurecimiento futuro de la plataforma.

### Para la forma de las tablas

- **C1, aplicar el checklist tal cual:** `id` sustituto en todas, `created_*`/`updated_*`/`deleted_*`/`row_version` con su
  trigger. Cumple la letra de A3, A5, A6 y A16, pero añade claves que nadie usa, un `updated_*` que mezcla hechos con
  dueños distintos, un borrado lógico sin flujo que lo dispare (R-18) y columnas de edición en tablas que no se editan.
  Se descarta.
- **C2, `ON UPDATE` explícito con SQL crudo en cada migración** (`ALTER TABLE … DROP CONSTRAINT` y volver a crearla con
  `ON UPDATE RESTRICT`). Cumple A8 a la letra, pero el modelo de EF y el `ModelSnapshot` dejan de describir la base: un
  cambio futuro de la FK generaría una migración que la vuelve a crear sin la cláusula. El beneficio es nulo porque las
  claves no cambian. Se descarta; si la plataforma lo exige, la vía es un `IMigrationsSqlGenerator` propio que emita la
  cláusula en todas las FK del servicio, no un parche por migración.
- **C3, excepciones tabla por tabla** (lo que hacía este ADR hasta #13736). Es explícito, pero cada tabla de D1/D2 repite
  la misma justificación y obliga a reabrir el ADR en cada migración. Se descarta en favor de C4.
- **C4, la opción elegida:** regla general por clase de tabla (a)/(b), FK según (c) y criterios (d), con excepciones
  solo para lo que se salga. Mantiene el modelo de EF como fuente de verdad del esquema y deja la integridad en CHECKs y
  FK con `RESTRICT`.

## Tradeoff aceptado

- El filtro **no cubre SQL crudo**. Por eso `FromSql`/`ExecuteSql` sobre tablas de negocio tienen que filtrar por `tenant_id`
  de forma explícita, y el code review lo revisa. Las FK compuestas de (c) cierran el hueco en la integridad entre tablas.
- Los **jobs sin usuario** (cola de D2, consumidores del bus) no tienen token. Deben fijar la empresa de forma explícita
  con su propio `IPlatformTenantAccessor` en el scope; si no lo hacen, no ven ni escriben nada.
- **Sin RLS (A10) y sin FK a `identity.tenants` (A4):** la empresa vive en otro servicio y en otra base. El aislamiento lo
  dan el filtro, la validación al guardar, las FK compuestas y la prueba de fuga.
- El `db-schema-validator` seguirá marcando A5, A6, A8 y A16 en este schema; una tabla que cumpla (d) se valida contra
  esta regla y no requiere excepción nueva.

## Pendientes (no decididos)

### Pendiente: «solo inserción» no está garantizado por la base

Las tablas de la clase (b) son de solo inserción por convención del código; nada en la base impide un `UPDATE` o un
`DELETE`, y la aplicación es dueña de las tablas (sin RLS). Esta parte **no se justifica**: para una tabla de auditoría la
garantía tiene que estar en la base. El cambio propuesto, con precedente en el repo (trigger de inmutabilidad de
`procedure_instance_field_values`), es un trigger `BEFORE UPDATE OR DELETE` que lance excepción en cada tabla de la clase
(b) (`tr_<tabla>_solo_insercion`: hoy `lecturas_backoffice`, `cambios_contrato` e `historial_estado_diagnostico`; en D2
`intentos_consulta`), creado por `migrationBuilder.Sql` en una migración nueva del `database-agent`, con prueba de
integración que intente ambos comandos. **Propuesta pendiente de decisión del usuario.** Si se acepta, pasa a ser el
criterio 4 bis de (d).

### Pendiente: vía de escritura del backoffice

El backoffice FLIT tiene permisos que escriben datos de otra empresa (`ConfiguracionContratoEditar`, `ConsultasLlenar`,
`DictamenOverride`), pero `ValidarEscritura` rechaza toda fila de otra empresa y el punto 3 solo abre la lectura. Falta
decidir la vía: por ejemplo, un ámbito explícito de escritura en `/backoffice/*` que fije la empresa de `?empresa=` y deje
rastro en la auditoría de la clase (b) correspondiente, frente a delegarla a un rpc de la propia empresa. No se decide
aquí; hasta entonces, ningún endpoint de backoffice escribe entre empresas.

## Consecuencias

- Una consulta nueva queda filtrada sin que nadie tenga que acordarse.
- Cada GET nuevo de D1/D2 tiene que registrar una línea en `EscenariosDeFuga.Todos`; si no, la suite falla.
- Cada migración nueva del schema se revisa contra los criterios (d) y se añade una fila a la tabla de aplicabilidad;
  solo lo que no cumpla abre una excepción por tabla.
- `database-agent`: al materializar `vehiculos`, `consultas`, `intentos_consulta`, `dictamenes` y `derivaciones`, expone
  `uq_<tabla>_tenant_id` en los padres y crea las FK compuestas.
- Comparendos podría migrar a este patrón, pero esa deuda queda fuera de este ADR.

## ADRs relacionados

- ADR-0069: lectura entre compañías, acotada y auditada.
- ADR-0070: gRPC entre servicios y REST hacia afuera.
