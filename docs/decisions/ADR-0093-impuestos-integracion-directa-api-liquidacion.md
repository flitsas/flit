# ADR-0093: Impuestos integra directo el API de liquidación con credenciales y webhook por compañía (excepción a ADR-0065)

**Fecha**: 2026-11-24
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (la aceptación es exclusiva de un humano, regla FLIT 15), PO, Security
**Tags**: arquitectura, backend, seguridad, integraciones, impuestos, core-impuestos, plataforma
**Base de código**: `develop@6bc0a9163`
**Relación**: excepción parcial a las decisiones 6 y 7 de `ADR-0065-consultas-externas-capacidad-de-plataforma.md` (Propuesto). No reemplaza ese ADR y no contradice ningún ADR Aceptado.

> Número: `ADR-0092` no se usa porque lo citan `contracts/proto/flit/identidad/v1/credenciales_producto.proto`, `contracts/openapi/platform.v1.yaml` y `ListTenantProductsHandler` sin que exista el archivo en `docs/decisions/` (deuda aparte para el LT).

## Contexto

El producto nuevo **Impuestos** liquida el impuesto vehicular por placa y en lote contra el API de liquidación de FLIT (`docs/integraciones/guia-integracion-clientes.md`). Ese API:

- usa tres credenciales: el `INTEGRATION_TOKEN`, que es del ambiente y solo sirve para dar de alta empresas, y la `api_key` y el `webhook_secret`, que son **por empresa** y se entregan **una sola vez** en el alta;
- avisa los resultados por un webhook firmado con el secreto **de cada empresa**, con reintentos durante unas 21 h;
- no es idempotente al liquidar.

`ADR-0065` (Propuesto) concentra en `core-consultas` los proveedores externos. Su decisión 6 hace que los avisos de proveedores lleguen a Consultas, y su decisión 7 deja los secretos de proveedores solo en Consultas. Su Opción 1 («cada producto integra sus propios proveedores») quedó descartada.

El usuario decidió (B2=a, DA-1=a, DA-2=b) que Impuestos tiene cliente propio en `core-impuestos` y que guarda las credenciales en su propia base. El RUNT sigue pasando por `core-consultas`.

Restricciones vinculantes:
- D-CRED-DIAG: copiar la llave de Diagnóstico si existe.
- DA-3 residual: leerla aunque Diagnóstico esté apagado.
- DA-7=b: PII del propietario sin enmascarar, cifrada.
- D-DESACT: la suspensión es reversible.

## Decisión

`core-impuestos` integra **directamente** el API de liquidación:
- custodia por compañía la `api_key`, el `webhook_secret` y el `company.id`, cifrados con un Data Protection propio con certificado;
- recibe el webhook en `/api/v1/impuestos/avisos/{token}`, una ruta propia del gateway, con un token opaco por ítem;
- guarda `INTEGRATION_TOKEN` como secreto de ambiente del servicio.

Todo lo demás de ADR-0065 sigue vigente para Impuestos: RUNT por `RuntIntempoService` de `core-consultas` con medición por producto, gRPC entre servicios (`ADR-0070-grpc-entre-servicios-rest-hacia-afuera.md`) y datos separados (`ADR-0064-datos-separados-eventos-y-reportes.md`).

## Alternativas consideradas

### Opción 1: Integración en `core-consultas` (ADR-0065 tal cual)

Consultas tendría el cliente, los secretos por empresa y el receptor del webhook, y publicaría el resultado por el bus a `core-impuestos`.

**Pros:**
- Un solo lugar para secretos y proveedores; la medición de consumo queda centralizada.
- Se ajusta a ADR-0065 sin excepción.
- El patrón del receptor de avisos ya existe (`/api/v1/consultas/avisos/**`).

**Cons:**
- Consultas pasaría a custodiar secretos **por empresa** para un solo producto (hoy custodia secretos por ambiente) y a correr la máquina de estados del alta, que es lógica de negocio de Impuestos.
- La copia de la llave de Diagnóstico, la carga manual, el «Incierto» y la conciliación quedarían repartidos entre dos servicios.
- Va contra B2=a y DA-2=b, que son decisiones del usuario.

**Esfuerzo:** L
**Riesgos:** Consultas crece como punto único de falla; acoplamiento de despliegue entre los dos productos.

### Opción 2: `core-impuestos` integra directo con credenciales y webhook propios — **elegida**

**Pros:**
- Un solo dueño de todo el ciclo: alta, credenciales, envío sin idempotencia, «Incierto», webhook, conciliación y PDFs. Las transacciones son locales; por ejemplo, el 201 del alta y las credenciales se guardan en el mismo `SaveChanges`, lo que resuelve el veto A-H02.
- Con el producto suspendido, las credenciales siguen disponibles para terminar lotes y verificar webhooks sin tocar las reglas de plataforma (DA-3).
- Producto independiente (EP-CF1); sin cambios en Consultas.

**Cons:**
- Un segundo cliente del mismo API en la suite (Diagnóstico usa el de `core-consultas/Impuesto`), sin código compartido.
- Secretos de un proveedor fuera de Consultas: excepción a ADR-0065 §7.
- Un receptor público más que asegurar.

**Esfuerzo:** M
**Riesgos:** contratos que divergen si el API cambia; se mitiga con el stub del contrato y sus pruebas. Hay una empresa compartida con Diagnóstico (mismo `tax_id`): rotar en la consola del API puede afectar a Diagnóstico.

### Opción 3: Credenciales en la plataforma (`platform.tenant_product_credentials`, DDL 204 ampliado) y orquestación en `core-impuestos`

Era DA-2=a.

**Pros:**
- Un solo almacén de credenciales de producto en la suite.
- Reutiliza el PUT y la pantalla del admin de compañías.

**Cons:**
- No hay rpc de escritura. El 201 llega una sola vez a `core-impuestos` y obliga a un resguardo local más una escritura remota idempotente, con riesgo de perder las credenciales.
- `GetTenantProductCredentialHandler` es fail-closed con el producto apagado y rompe D-DESACT si no se cambia una regla compartida.
- El usuario la descartó (DA-2=b).

**Esfuerzo:** M–L
**Riesgos:** pérdida de credenciales entre dos servicios; cambio en una regla de plataforma compartida.

## Tradeoff aceptado

Se acepta tener **dos clientes del mismo API** y **secretos de un proveedor fuera de Consultas** a cambio de que el ciclo del alta, que no es idempotente y entrega los secretos una sola vez, viva en una sola transacción y en un solo servicio. Con eso, la suspensión no depende de reglas de plataforma. La excepción solo aplica a proveedores que entregan **un secreto por empresa y avisan por webhook por empresa**; cualquier otro proveedor nuevo sigue ADR-0065.

## Consecuencias

### Lo que se gana

- El alta guarda las credenciales en la misma transacción en que recibe el 201.
- Lotes, webhooks y PDFs siguen funcionando con la compañía suspendida.
- La correlación del webhook es inequívoca: un token opaco por ítem, guardado como hash SHA-256 y respondido con 410 si no se conoce.
- Las pruebas locales corren contra el stub del contrato (`scripts/dev/stub-impuestos/`, puerto 8898).

### Lo que se pierde

- La custodia centralizada de secretos de proveedores para este caso.
- La medición del API de liquidación en Consultas (la del RUNT sí queda medida). `core-impuestos` expone sus propias métricas.

### Cambios operacionales

- Ruta del gateway `/api/v1/impuestos/{**catch-all}` → `core-impuestos-cluster`. No choca con `/api/v1/webhooks/**`, que va a core-api.
- Host público https del webhook por ambiente (DEV en la oleada; QA y PDN con `/deploy-env`).
- Secretos de ambiente nuevos con prefijo `IMPUESTOS_SVC_*`, que no colisiona con el `IMPUESTO_*` de core-consultas: `API_BASE_URL`, `INTEGRATION_TOKEN`, `AVISOS_URL_BASE` y el certificado de Data Protection.
- Plataforma:
  - el PUT de productos deja el trabajo `impuestos.producto.cambiado` en la outbox, en la misma transacción y en los dos hosts que lo exponen;
  - rpc nuevos en core-identity para `svc-impuestos`: datos del alta, lectura auditada de `diagnostico/impuesto_api_key` aunque Diagnóstico esté apagado (solo al aprovisionar) y reporte del estado de aprovisionamiento;
  - DDL 205.
- **PII (DA-7=b, decisión del usuario 2026-11-24):**
  - Se persisten cifrados el tipo y el número de documento del propietario, su nombre y el resultado completo.
  - Se muestran sin enmascarar en el grid, el detalle, los reportes y los exportes: el cliente responde por sus datos.
  - La dirección, el correo y los teléfonos no se persisten; la dirección nunca se exporta.
  - Los logs no llevan PII, payload ni secretos.
  - Retención: la de Diagnóstico, que hoy es no borrar (R-18). Queda registrado para el gate de Security (Ley 1581).

## Aplicabilidad y excepciones a las convenciones de datos

**Aplicabilidad.** Las excepciones de esta sección rigen solo para el schema `impuestos` de `core-impuestos` y para `platform.tenant_product_provisioning` (DDL 205 de core-api). Las referencias de la lista siguen `.claude/skills/db-schema-validator/checklist-validacion-schema.md`. Cualquier otra tabla de esos servicios sigue el checklist sin excepción. El `db-schema-validator` seguirá marcando estos puntos; las tablas citadas aquí se validan contra esta sección y no necesitan otra excepción. Anclas verificadas sobre `feature/AB-14694-producto-impuestos@6bdb63efb`.

### E1. FKs simples, no compuestas con `tenant_id` (decisión del usuario)

- **Dónde:** `services/core-impuestos/src/Flit.Impuestos.Api/Persistence/Configuraciones/NotificacionConfig.cs:34` (`fk_notificaciones_liquidaciones`) y `.../Configuraciones/LiquidacionItemConfig.cs:113` (`fk_liquidacion_items_liquidaciones`). La autorreferencia `fk_liquidacion_items_relanzado_desde` (`LiquidacionItemConfig.cs:115`) también es simple.
- **Motivo:** el usuario la decidió. Se aparta de la forma (c) de `ADR-0072`, que pide FK compuestas.
- **Alcance:** las FKs entre tablas del schema `impuestos`.
- **Mitigación:** el `HasQueryFilter` de la empresa del token (`Modulos/Comun/TenantFilter.cs`, cerrado por defecto) en las lecturas, y `AND tenant_id = …` explícito en cada escritura, también en SQL crudo. El code review lo revisa. Si en algún momento una escritura pudiera colgar una fila de un padre de otra empresa, la excepción se revisa y se pasa a FK compuesta.

### E2. A8: FKs con `ON UPDATE NO ACTION`

- **Dónde:** las FKs que genera EF, por ejemplo la que añade `Persistence/Migrations/20261125160959_R2_Revision.cs:267-275`. EF no emite `ON UPDATE` y PostgreSQL aplica `NO ACTION`.
- **Motivo:** las PK son `uuid` que se generan en la aplicación (`ValueGeneratedNever`) y nunca se actualizan. Un `ON UPDATE CASCADE` no tendría efecto.
- **Alcance:** todas las FKs del schema `impuestos`.
- **Mitigación:** ningún flujo actualiza una PK. Si alguno llegara a necesitarlo, se define el `ON UPDATE` en SQL explícito en la migración.

### E3. A11: `ix_notificaciones_liquidacion` empieza por `liquidacion_id`

- **Dónde:** `NotificacionConfig.cs:36` y `R2_Revision.cs:216`.
- **Motivo:** el índice sirve a la FK `RESTRICT` (A9). Con `liquidacion_id` como primera columna, la verificación al borrar una liquidación no tiene que recorrer la tabla. En este caso A9 pesa más que A11.
- **Alcance:** solo este índice.
- **Mitigación:** las lecturas por empresa usan `ix_notificaciones_tenant_creada (tenant_id, creada_en)` (`NotificacionConfig.cs:37`), que sí empieza por `tenant_id`.

### E4. A5/A6: `platform.tenant_product_provisioning` sin `created_by`/`updated_by` ni soft delete

- **Dónde:** `services/core-api/src/Flit.Infrastructure/Persistence/Sql/Ddl/205-impuestos-producto-y-aprovisionamiento.sql:23-26`. El propio DDL declara ahí la excepción.
- **Motivo:** la escribe un servicio y no un usuario, con un upsert monotónico por versión (rpc `ReportarEstadoAprovisionamiento`). Guarda solo el último estado y no hay nada que borrar de forma lógica.
- **Alcance:** solo esa tabla.
- **Mitigación:** `audit.audit_logs` (`trg_audit_log`) guarda el rastro técnico de cada cambio.

### E5. Idioma de los nombres

- **Dónde:** las tablas y columnas del schema `impuestos` van en español (`liquidaciones`, `liquidacion_items`, `notificaciones`, `avisos_recibidos`, `credenciales_empresa`…). El DDL 205 de core-api va en inglés, como el resto de su esquema (decisión del usuario, 2026-11-25). El contrato gRPC que lo alimenta sigue en español.
- **Motivo:** `core-impuestos` ya nombraba todo en español desde su migración inicial (`20261006233452_Inicial_EsquemaConOutboxEInbox`). Renombrarlo cambiaría todas las migraciones sin ganar nada. Cada base conserva la convención que ya tenía.
- **Alcance:** el schema `impuestos` usa español. Los esquemas de core-api, incluido `platform`, usan inglés.
- **Mitigación:** no hay lecturas cruzadas entre esquemas (`ADR-0064`). El mapeo entre los dos idiomas queda solo en el contrato gRPC.

### E6. A10: `core-impuestos` sin RLS

- **Dónde:** el schema `impuestos` completo.
- **Motivo:** se sigue el precedente de `ADR-0072` («Sin RLS (A10) y sin FK a `identity.tenants` (A4)»). La empresa vive en otro servicio y en otra base, y la aplicación es dueña de las tablas. Un RLS sin `FORCE` sería decorativo.
- **Alcance:** todas las tablas de negocio de `core-impuestos`.
- **Mitigación:** el filtro de EF cerrado por defecto, `tenant_id` explícito en las escrituras y en SQL crudo, y los jobs sin usuario fijan la empresa en el scope. A diferencia de `ADR-0072`, aquí no hay FK compuestas (ver E1), así que el control en las escrituras tiene más peso.

### E7. Riesgo de datos: R2 no limpia datos previos

- **Dónde:** `R2_Revision.cs`. Ahí se crean el índice único `ux_avisos_recibidos_consulta_attempt` (`:259-265`), los 4 CHECK (`:221`, `:227`, `:247`, `:253`) y la FK nueva `fk_notificaciones_liquidaciones` (`:267-275`).
- **Motivo:** la migración no corre en ningún ambiente compartido. Fuera de las bases locales y de prueba no hay filas que puedan violar esas restricciones.
- **Alcance:** la migración `R2_Revision` mientras no se haya aplicado en DEV, QA ni PDN.
- **Mitigación:** si `R0`/`R1` llegan a un ambiente compartido antes que `R2`, se añade a `R2` un paso de limpieza previo (deduplicar avisos, corregir valores fuera del CHECK, quitar huérfanos de la FK) antes de crear las restricciones. Database Agent lo verifica en el gate de despliegue.

## ADRs relacionados

- `ADR-0065-consultas-externas-capacidad-de-plataforma.md` — este ADR es su excepción parcial (§6 y §7).
- `ADR-0064-datos-separados-eventos-y-reportes.md` — sin lectura de esquemas ajenos; los datos del alta llegan por rpc y no viajan en el evento.
- `ADR-0070-grpc-entre-servicios-rest-hacia-afuera.md` — gRPC hacia la plataforma y hacia Consultas.
- `ADR-0057-almacenamiento-de-archivos-por-ambiente-y-contabo.md` (Aceptado) — los PDFs se guardan en el file-manager, categoría `impuestos`.
- `ADR-0072-diagnostico-aislamiento-por-tenant.md` y `ADR-0073-diagnostico-acceso-backoffice.md` — forma que se copia para el aislamiento y el Super Admin.
- `ADR-0076-diagnostico-respuesta-completa-de-la-fuente-con-pii-cifrada.md` — precedente de PII cifrada en reposo.
- `ADR-0080-departamentos-impuesto-por-tabla.md` — Impuestos no la reutiliza; usa `GET /fuentes` y su réplica propia de organismos.
- Plan: `docs/impuestos/plan-implementacion.md`.

## Notas para agentes

- **Backend Agent**:
  - Sin `ProjectReference` a `core-diagnostico` ni a `core-consultas/Impuesto` (hay un test de arquitectura).
  - Ante un timeout o un 5xx tras enviar, no se reintenta nada (ni el alta ni la consulta): el estado pasa a `INCIERTO`. Solo se reintenta lo que con certeza no salió.
  - El webhook se verifica sobre el cuerpo crudo y en tiempo constante, con tolerancia de ±300 s.
- **Frontend Agent**: la api_key y el secreto nunca llegan al navegador; el formulario de carga no los vuelve a mostrar.
- **QA Agent**:
  - Vector de firma `[API §6]`, `attempt` desordenado, token desconocido → 410.
  - Suspensión con un lote en curso.
  - Copia de la llave de Diagnóstico con Diagnóstico apagado.
- **Security Agent**:
  - Revisar DA-7=b (PII sin enmascarar, sin purga) y la lectura de la llave de Diagnóstico por `svc-impuestos` con el producto apagado.
  - Revisar el canal h2c interno en claro (el mismo riesgo que ya declara `credenciales_producto.proto`) y el rate limit del receptor público.
- **Infra Agent**:
  - Host público https sin redirecciones; certificado de Data Protection por ambiente, con respaldo.
  - Permisos del broker: `tramites` y `plataforma` escriben en `flit.impuestos`.
  - Puertos 4033, 4034 y 8089 por confirmar.

## Referencias externas

- `docs/integraciones/guia-integracion-clientes.md` (§1–§10). El contrato normativo `04-api.md` todavía no está en el repositorio.
