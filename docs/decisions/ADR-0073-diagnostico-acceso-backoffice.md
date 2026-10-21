# ADR-0073: El backoffice FLIT de Diagnóstico lee entre compañías solo con permiso y tenant_type=FLIT, empresa explícita y auditoría

**Fecha**: 2026-10-21
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana, regla FLIT 15). Redactado durante la implementación de la HU #13728.
**Tags**: arquitectura, backend, seguridad, multi-tenant, auditoría
**Épica / Feature / HU**: #12881 / #13715 / #13728 (F00.1-H5); vía de escritura: #13716 / #13741 (F00.2-H2) · **Plan**: `docs/diagnosticos/plan-implementacion-diagnostico-vehicular-v2.md` D-16, D-01, §6.2, RD-3 · **Relacionados**: ADR-0069, ADR-0072

## Contexto

En Diagnóstico una sesión es una compañía (D-03): la empresa sale del token y el filtro global de EF (ADR-0072) no deja
ver filas de otra. El backoffice FLIT (`backoffice_diagnostico`, D-01) necesita dar soporte a las compañías cliente: ver
su configuración, sus diagnósticos, la auditoría y la volumetría. Eso es lectura entre compañías, que tiene que quedar
acotada a un solo lugar y con rastro (patrón ADR-0069).

Identidad no emite un token especial de backoffice. Lo que sí emite es el claim `tenant_type` de la compañía del usuario
(`OidcPrincipalFactory.cs:99`, en el access token en `:141-143`), con los valores de `ck_tenants_tenant_type` (RENTING,
CONCESIONARIO, FLIT; `Ddl/109`). Un rol se puede asignar en cualquier compañía: si bastara el permiso, una compañía
cliente que se asignara `backoffice_diagnostico` leería a las demás.

## Decisión (D-16 = a, sin cambios en Identidad)

1. **Política `diagnostico.backoffice`** (`Plataforma/PoliticaBackoffice.cs`). Exige a la vez el permiso
   `diagnostico.backoffice.ver` y el claim `tenant_type=FLIT`. El SuperAdmin cuenta como si tuviera el permiso, igual
   que en las capacidades de `/sesion`, pero también tiene que ser de la compañía FLIT. Un token de servicio nunca pasa.
   La aplica todo el grupo `/backoffice/*`, incluido un fallback: una ruta inexistente bajo `/backoffice/` responde 403
   a quien no es backoffice FLIT y 404 a quien sí. Las rutas del backoffice que exigen otro permiso (por ejemplo
   `diagnostico.configuracion.contrato.editar`) lo suman con `RequirePermiso` encima de la política.
2. **Empresa explícita** (`Modulos/Comun/EmpresaExplicita.cs`, filtro del grupo). La empresa consultada nunca sale del
   token. Se indica en `?empresa=` o en el segmento `{empresa}` de la ruta (las rutas del plan §6 como
   `/backoffice/empresas/{empresa}/configuracion/contrato`); si llegan las dos tienen que coincidir. Sin empresa, con un
   valor que no es un uuid, con el uuid cero o con `?empresa=` repetido, la respuesta es 400 `EMPRESA_REQUERIDA`. Esa
   respuesta sale antes de leer nada y no se audita. El filtro corre después de la autorización: a quien no es backoffice
   FLIT se le responde 403 aunque falte la empresa.
3. **Auditoría de cada lectura** (`Modulos/Comun/AuditoriaLecturaBackoffice.cs`). Para todo GET/HEAD del grupo, el filtro
   inserta una fila en `diagnostico.lecturas_backoffice` **antes** de que el endpoint lea. La fila lleva el usuario (`sub`),
   la empresa consultada, la ruta con su consulta (sin cuerpo, máximo 500 caracteres) y la fecha que pone la base. Si no
   se puede auditar, la petición falla y no se lee nada. Un token sin compañía responde 403 `SIN_EMPRESA` y uno sin
   usuario, 403 `SIN_USUARIO`. La fila es de la compañía del lector (FLIT), así que se guarda por la vía normal y
   `TenantFilter.ValidarEscritura` la acepta. Ninguna ruta del grupo puede olvidarse de auditar: la auditoría está en el
   grupo, no en cada handler.
4. **Un solo punto que se salta el filtro global** (`Modulos/Backoffice/LecturaEntreEmpresas.cs`, `internal`).
   `db.DeLaEmpresa<T>(empresa)` es `IgnoreQueryFilters()` + `AsNoTracking()` + `Where(TenantId == empresa)`: lee una sola
   empresa y sin seguimiento de cambios. La prueba de arquitectura `IgnoreQueryFiltersSoloEnBackofficeTests` hace un grep
   auditable de `services/core-diagnostico/src`. Falla si `IgnoreQueryFilters(` aparece fuera de `Modulos/Backoffice/`
   (los comentarios no cuentan) y también si no aparece en ningún lado, para que el escáner no quede mirando a otra parte.
5. **Ruta mínima de D0:** `GET /api/v1/diagnostico/backoffice/resumen?empresa=`. Devuelve si la compañía tiene
   configuración y cuántos diagnósticos y vehículos tiene. No entrega datos de vehículos ni de personas. F00.2 y D3
   suman sus rutas en `BackofficeEndpoints`.
6. **Prueba de fuga.** `/backoffice/*` queda fuera de `EscenariosDeFuga` (ADR-0072 punto 4) porque su función es leer otra
   compañía. Sus garantías las cubren `PoliticaBackofficeTests` (403 fuera de FLIT o sin permiso) y
   `BackofficeEmpresaExplicitaTests` (400 sin empresa; con `?empresa=B`, solo datos de B y una fila auditada).

## Condición que este diseño pone a la vía de escritura del backoffice (#13741)

ADR-0072 deja pendiente cómo escribe el backoffice sobre otra compañía. Este ADR no lo decide, pero la condiciona:

- La empresa de una escritura del backoffice sale de `EmpresaExplicita.De(http)`, nunca del cuerpo ni del token, y pasa
  por el mismo grupo, con la misma política y el mismo filtro.
- El filtro audita solo GET/HEAD en `lecturas_backoffice`. Una escritura (`PUT`, `POST`) deja su propio rastro en el
  historial de la clase (b) que corresponda (`cambios_contrato` para el contrato), con el usuario y la empresa.
- `TenantFilter.ValidarEscritura` sigue rechazando filas de otra compañía. La vía de #13741 tiene que ser un ámbito
  explícito y acotado al módulo Backoffice, verificable con una prueba de arquitectura como la de `IgnoreQueryFilters()`.
  Abrir la validación para todo el servicio queda descartado.

## Vía de escritura del backoffice (HU #13741, F00.2-H2) — sigue en Propuesto

`PUT /backoffice/empresas/{empresa}/configuracion/contrato` escribe en la fila de la compañía de la ruta. Cumple las tres
condiciones así:

1. **Empresa, política y filtro.** La ruta está en el grupo `/backoffice`
   (`Modulos/Backoffice/ContratoBackofficeEndpoints.cs`, mapeada desde `BackofficeEndpoints`). Hereda la política
   `diagnostico.backoffice` y además exige `diagnostico.configuracion.contrato.editar` con `RequirePermiso`. La empresa
   sale de `EmpresaExplicita.De(http)`: el segmento `{empresa}` y, si llega, `?empresa=` deben coincidir. Si no, la
   respuesta es 400 y no se escribe nada.
2. **Rastro propio.** El PUT no se registra en `lecturas_backoffice`, porque el filtro solo audita GET/HEAD. Cada campo
   que cambia queda en `cambios_contrato`, en el mismo guardado, con el usuario (`sub`), la compañía de la ruta, el valor
   anterior y el nuevo. Se registran `fuentes_habilitadas`, `limite_vehiculos_lote` y `redaccion_ia_suspendida`, y
   también el recorte de `fuentes_por_defecto` cuando se reducen las habilitadas. El GET del contrato sí se audita como
   lectura.
3. **Un ámbito explícito y acotado al módulo Backoffice.**
   - `DiagnosticoDb.AutorizarEscrituraDe(empresa, tipos)` abre un ámbito (`EscrituraAutorizada`) que se cierra con el
     `using`. No se anida y no acepta la empresa cero, una lista vacía ni tipos que no sean de empresa.
   - Mientras está abierto, `TenantFilter.ValidarEscritura` acepta además las filas **nuevas o modificadas** de **esa**
     empresa, solo de los tipos indicados, y solo si ya eran de esa empresa y lo siguen siendo. Nunca acepta un borrado.
   - Todo lo demás se sigue rechazando como antes, en todo el servicio. Sin ámbito, la validación no cambia.
   - Solo lo usa `Modulos/Backoffice/EscrituraEntreEmpresas.cs` (`internal`). Su `GuardarDeLaEmpresaAsync` guarda con el
     ámbito abierto y su `ParaEditarDeLaEmpresa<T>` lee con seguimiento. El contrato abre el ámbito solo para
     `ConfiguracionEmpresa` y `CambioContrato`.
   - `IgnoreQueryFilters()` sigue apareciendo únicamente en `Modulos/Backoffice/`, así que
     `IgnoreQueryFiltersSoloEnBackofficeTests` no cambia de regla.
   - **Prueba de arquitectura nueva:** `EscrituraEntreEmpresasSoloEnBackofficeTests` hace el mismo grep auditable sobre
     las llamadas `.AutorizarEscrituraDe(`, sin contar la declaración ni los comentarios. Falla si aparece una llamada
     fuera de `Modulos/Backoffice/` y también si no aparece ninguna. Además prueba que el ámbito rechaza otra empresa,
     otro tipo, un borrado y el paso de una fila de una compañía a otra.

**Reglas de la aplicación antes que la base.**

- Orden de validación de los PUT: 400 si el cuerpo no es un objeto. Después, 403 `CAMPO_NO_EDITABLE` si llega un campo
  que la ruta no edita; en la ruta operativa eso incluye los contractuales y `redaccion_ia_suspendida`. Por último, 422
  si un valor está fuera de su conjunto, si el límite no es un entero positivo o si se habilita
  `IMPUESTO_DEPARTAMENTAL` sin C3.
- Con un error no se guarda ningún campo.
- Un CHECK de la base que aun así falle (SQLSTATE 23514, por ejemplo una carrera con otra edición) se traduce a 422
  `VALOR_INVALIDO`. Lo hace el manejador global de reglas, sin exponer el nombre del CHECK.

**Tradeoffs.**

- `AutorizarEscrituraDe` es `internal` al ensamblado: el compilador no impide llamarlo desde otro módulo. El control lo
  da la prueba de arquitectura, igual que con `IgnoreQueryFilters()`.
- El ámbito vale para todo lo que se guarde mientras está abierto. Por eso se abre solo alrededor de un
  `SaveChangesAsync` y con la lista mínima de tipos.

## Alternativas consideradas

- **Solo el permiso (sin `tenant_type`).** Se descarta: una compañía cliente que se asigne el rol leería a las demás.
- **Un claim o un token propio de backoffice emitido por Identidad.** Es más explícito, pero exige cambios en Identidad y
  en el gateway que la ronda D0–D2 no tiene. El claim `tenant_type` ya viaja firmado en el access token.
- **La empresa en una cabecera (`X-Flit-Tenant-Id`).** Se descarta: el SDK ya ignora esa cabecera para usuarios, y una
  cabecera no queda en la ruta auditada ni en los logs de acceso.
- **Auditar en cada handler.** Basta un olvido para que haya una lectura sin rastro. El filtro de grupo lo impide.
- **Auditar después de leer.** Una lectura que falla a mitad no dejaría rastro. Se audita antes.

## Tradeoff aceptado

- Una lectura que se audita y luego falla deja una fila igual. Se prefiere sobreauditar a perder una lectura.
- `lecturas_backoffice` es de solo inserción por convención del código: ver «Pendiente: solo inserción» en ADR-0072.
- El SuperAdmin de una compañía que no es FLIT no entra al backoffice. Es más estricto que el resto del producto, a
  propósito.
- `tenant_type` lo decide Identidad. Si una compañía se diera de alta como FLIT por error, su backoffice leería a las
  demás. El control está en el alta de compañías de la plataforma, no aquí.

## Consecuencias

- Toda ruta nueva del backoffice va en `BackofficeEndpoints` y hereda la política, la empresa explícita y la auditoría.
- Toda lectura entre compañías usa `db.DeLaEmpresa<T>(empresa)`. Un `IgnoreQueryFilters()` en otro módulo rompe la suite.
- El contrato `contracts/openapi/diagnostico.v1.yaml` documenta la ruta, el 400 `EMPRESA_REQUERIDA` y los 403.
- Pasa por el security-agent antes del merge.
