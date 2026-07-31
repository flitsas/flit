# FEATURE 05 — Comparendos, RNMC y Restricciones por OT

| Campo | Valor |
|---|---|
| **Fase / Entrega** | **Fase 2** — viernes 17 de julio de 2026, 14:00 |
| **Desarrollador asignado** | **Willyn Londoño** |
| **Módulos afectados** | Trámites, Comparendos, Config compañía, Integraciones |
| **Requerimientos cubiertos** | Objetivos2.0 filas 6, 7, 14, 15 y 16; Caracteristicas.docx §3 (regla especial del SIMIT) |
| **Rama sugerida** | `feature/F05-comparendos-rnmc-restricciones` |
| **Dependencias** | Usa el parámetro `fines_query_source` creado en FEATURE 02 (mismo desarrollador — dependencia internalizada) |
| **Feature en ADO** | [#10754](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/10754) — 9 HUs ([#10755](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/10755)–[#10763](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/10763)), 34 SP |

## 0. Correcciones al diseño tras la implementación

Tres premisas de este documento resultaron falsas al contrastarlas con el código. Se dejan registradas porque cambian el diseño:

1. **No existe un módulo interno de comparendos en la plataforma.** No hay tablas ni entidades de multas en `core-api`; el módulo DGC ([#9735](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/9735)) vive en otro producto (flit-vialix) y gestiona la cartera que *emite* un OT, no consulta los comparendos de un ciudadano. La fuente `internal` se resolvió consumiendo el **API de registro de FLIT** heredado de FLIT 1 (`api/v1/registration/simit`), confirmado con el usuario.
2. **El certificado de identidad no lo genera FLIT**: es el PDF binario que descarga Kyverum, así que no admite una sección RNMC. El generador propio (`IdentityCertificatePdfGenerator`) existe pero no tiene uso en producción. El RNMC se emitió como **certificado suelto**, opción que el AC3 ya contemplaba.
3. **Quitar el bloqueo de las multas podía romper la radicación en silencio**: el gate `simit_multas` se derivaba del mismo `fail` que había que convertir en `warn`, y ningún test existente lo habría detectado. Se desacopló el gate para que se derive de la *clave* del check, con pruebas de amarre.

Además, el API interno tiene **campos agregados que no son fiables**: `totalMultasPagar` devuelve la cantidad de multas, no el monto. El mapper calcula siempre desde el detalle.

## 1. Objetivo

Hacer efectiva la consulta de comparendos según la fuente configurada por compañía (interna/externa), conectar el API de **KYVERUM** para personas jurídicas y verificar el de personas naturales, cerrar la parametrización de **RNMC** (VERIFIK), y permitir **restricciones de consultas por compañía+OT** cuyo incumplimiento **advierte pero no bloquea** la creación del trámite.

## 2. Requerimientos de origen

1. **Consulta comparendos en trámites** (Objetivos2.0 — "Trámites"): cuando el actor sea PJ, validar en la configuración de la compañía la fuente configurada para la consulta de comparendos; traer y advertir la información de acuerdo a ese parámetro.
2. **API de comparendos** (Objetivos2.0 — "Comparendos"): configurar el API de consulta de comparendos (**KYVERUM = PJ**) y verificar la de PN.
3. **Parametrización RNMC** (Objetivos2.0 — "Config compañía"): hoy apunta a VERIFIK, pendiente entrega de Johan; definir dónde se plasma el certificado de RNMC (en el certificado de VID o certificado suelto).
4. **Configuración compañía + OT** (Objetivos2.0 — "Config compañía"): nuevo apartado para restricciones referentes a un OT específico (ej.: desde la compañía X inhabilitar la consulta RNMC para el OT X).
5. **No bloqueo** (Objetivos2.0 — "Config compañía"): las restricciones/hallazgos del punto anterior **no bloquean** la creación del trámite (ej.: no tiene SOAT/RTM, tiene multas, no inscripción en RUNT → advertir, no impedir).

## 3. Alcance

### Incluido
- **Regla de fuente de comparendos** en el preflight/consulta del trámite: si el actor es PJ y `fines_query_source = internal`, usar la fuente base del módulo de comparendos de la plataforma; si es `external`, consultar en línea (SIMIT vía provider). Mostrar la información como **advertencia** en el wizard.
- **Provider KYVERUM comparendos (PJ)**: cliente + provider bajo `IConsultationProvider` (los clientes Kyverum ya existen para RUNT/identidad — extender con el servicio de comparendos), con modo mock conmutable. **Verificación PN**: probar `VerifikSimitConsultationProvider` existente contra casos reales y documentar resultado.
- **RNMC**: dejar operativa la consulta con `VerifikRnmcConsultationProvider` (ya existe) y **plasmar el resultado en certificado**: decisión propuesta — sección RNMC dentro del certificado de identidad (VID) existente, con opción de generar certificado suelto reutilizando el mismo builder (confirmar con producto al arrancar).
- **Restricciones compañía + OT**: nueva configuración por tenant+OT que permite **inhabilitar consultas específicas** (RNMC, SIMIT, etc.) para un OT concreto. Se administra en un apartado nuevo de la configuración de compañía.
- **Advertencias no bloqueantes**: los hallazgos (multas, sin SOAT/RTM, no inscrito en RUNT) y las consultas inhabilitadas se muestran como **warnings amarillos** en el preflight/wizard (modelo green/yellow/red de `ConsultationResult`, ADR-0020) y **no** se agregan como blockers del `SubmitGate` para la creación.

### Excluido
- Carga/gestión de la fuente base interna de comparendos (módulo de comparendos existente; solo se consume).
- Cobro/gestión de consumos de estas consultas → FEATURE 06.

## 4. Diseño técnico propuesto

### Backend (`services/core-api`)
- Tabla `admin.tenant_transit_office_consultation_restrictions`: `tenant_id`, `transit_office_id`, `consultation_kind`, `enabled`, auditoría. Handler + endpoints CRUD en `Flit.Admin.Application`.
- En el orquestador de consultas del preflight: filtrar consultas restringidas para el par tenant+OT (integrar con `TenantConsultationOverrideProvider` existente) y anotar en el resultado que la consulta fue omitida por configuración.
- Provider `KyverumFinesConsultationProvider` (PJ, por NIT) en `Flit.Infrastructure/Consultations/Kyverum/`, registrado en `ConsultationProviderRegistry`, resultado normalizado.
- Ajuste del `SubmitGate`/preflight: los hallazgos de comparendos/SOAT/RTM/RUNT se clasifican **yellow (advertencia)**, nunca red, para la **creación** del trámite (los gates de radicación al OT no cambian en esta feature).
- Certificado RNMC: extender el generador del certificado de identidad con la sección RNMC (datos + fecha de consulta + fuente).

### Frontend (`frontend`)
- Config compañía: apartado "Restricciones por Organismo de Tránsito" (selector de OT + toggles por tipo de consulta).
- Wizard: banner de advertencias amarillas con el detalle de hallazgos (multas, SOAT/RTM, inscripción RUNT) y de consultas omitidas por restricción; nunca impide continuar.

## 5. Criterios de aceptación

1. Con fuente **interna**, la consulta de comparendos de una PJ usa la base del módulo de comparendos; con fuente **externa**, consulta en línea. En ambos casos la información se muestra como advertencia en el trámite.
2. La consulta de comparendos PJ funciona vía KYVERUM (o su mock si el proveedor no entrega credenciales a tiempo, conmutables por configuración); la de PN queda verificada y documentada.
3. El resultado de RNMC queda plasmado en certificado (sección en el certificado de VID o suelto, según se confirme) y descargable desde el trámite.
4. Desde la compañía X puedo inhabilitar la consulta RNMC (u otra) para el OT Y; el trámite para ese OT omite esa consulta y lo indica.
5. Tener multas, no tener SOAT/RTM o no estar inscrito en RUNT **no bloquea** crear el trámite: se advierte con claridad y el usuario decide continuar.
6. Dark mode y responsive correctos en las pantallas tocadas (criterio transversal FLIT 2.0).

## 6. Riesgos y mitigaciones

- **Dependencia KYVERUM (PJ) y entrega de Johan (RNMC)**: se planifica asumiendo que llegan a tiempo (decisión acordada). Mitigación: todos los providers nacen con **modo mock conmutable** — la demo del viernes funciona aunque las credenciales lleguen tarde, y conectar el real es cambio de `appsettings`, no de código.
- **Decisión certificado RNMC (VID vs. suelto)**: confirmar con producto el miércoles; el diseño propuesto soporta ambas con el mismo builder.
- **Alcance del "no bloqueo"**: aplica a la **creación** del trámite; confirmar si la radicación al OT mantiene sus gates actuales (supuesto: sí los mantiene).

## 7. Definición de hecho

- PRs contra `develop` (separar restricciones OT y providers si superan 800 líneas), migraciones aplicadas, build + tests verdes, revisión de un compañero.
- Pruebas: unit de la regla de fuente interna/externa, unit del filtro de restricciones tenant+OT, unit del provider KYVERUM (HTTP mock) + E2E manual documentado en el PR.
