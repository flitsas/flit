# ADR-0050: Fuente única de identidad y disparo único desde el módulo Identidad

**Fecha**: 2026-08-21
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana — regla FLIT 15), equipo core-api, equipo tramites
**Tags**: arquitectura, backend, frontend, identidad, modulo-companias, tramites, feature-11687, feature-11688, feature-11689
**Supersedes**: `ADR-0034-validacion-identidad-admin-desacoplada.md` (Aceptado)
**Enmienda**: `ADR-0039-precedencia-unica-decision-envio-identidad.md` (Aceptado) — su acotación «la identidad administrativa queda fuera de alcance» pierde la premisa
**Relacionado**: `ADR-0025-baul-firmas-custodia-y-consumo.md` (D8, precedencia baúl > identidad), `ADR-0036-prevalidacion-natural-tracking-desacoplado-instancia.md` (restricción a persona natural), `ADR-0040-tracking-identidad-por-persona.md` (Propuesto — dependencia declarada), `ADR-0042-documentos-personalizados-por-compania.md`
**HU origen**: Features #11687, #11688, #11689 — HUs #11751 a #11761
**Refinamiento**: `.claude/state/refine-identidad-admin-v2-aprobado.md` (Fase 4 cerrada por el PO humano el 2026-08-20)

---

## Contexto

El `ADR-0034` decidió crear una **validación de identidad administrativa desacoplada del trámite**:
una entidad propia (`admin.admin_identity_validations`), un servicio `IAdminIdentityValidationService`
y un disparo `POST` desde el área admin que hacía que Kyverum enviara el correo al representante legal
o al mandatario. La intención era razonable: permitir validar a nivel admin, antes de que exista un
trámite.

El resultado en producción es que **hay dos almacenes de identidad para la misma persona**:

| Almacén | Quién escribe | Quién lee |
|---|---|---|
| `tramites.procedure_instance_biometric_validations` | el módulo Identidad y el flujo del trámite | `SubmitGate` → `IdentityApprovalResolver` (gate de comprador y vendedor) |
| `admin.admin_identity_validations` | el disparo admin del `ADR-0034` | la ficha del representante legal y la del mandatario, vía `MandateSignerDirectory` |

Los dos no se hablan. La consecuencia observada, y que originó esta ola: **un operador prevalida a una
persona desde el módulo Identidad y el rótulo de su ficha admin no se mueve**, porque la ficha lee el
otro almacén. Desde la superficie de negocio eso se lee como un defecto del módulo Identidad, cuando en
realidad es una duplicación de fuente decidida en su día.

A eso se suma que el disparo admin es **un sexto disparador de correo de validación**, justo el
problema que el `ADR-0039` vino a acotar: ese ADR dejó fuera de alcance «la identidad administrativa»
precisamente porque vivía en otro agregado. Mantener las dos fuentes obliga a duplicar la precedencia,
la ventana de vigencia y la normalización del documento en dos sitios, y a mantenerlas sincronizadas
para siempre.

## Decisión

**El módulo Identidad (`tramites.procedure_instance_biometric_validations`) es la única fuente de
verdad del estado de identidad de cualquier persona, incluidos representantes legales y mandatarios.
El área admin pasa a ser solo consulta.**

En concreto:

1. **Lectura única.** `MandateSignerDirectory` y toda su cascada resuelven la identidad contra el
   almacén del módulo Identidad. Ningún consumidor de la ruta de radicación lee ya
   `admin.admin_identity_validations`.
2. **Un solo endpoint de consulta admin.** El área admin expone una consulta de **vigencia por
   documento** que devuelve **un único registro**, deriva el tenant **de la ruta admin** (sin exigir
   `X-Tenant-Id` al cliente), normaliza el par (tipo, número) con `Trim` + mayúsculas invariantes según
   el `ADR-0039`, y resuelve **exactamente cuatro estados**: `sin validación`, `en curso`,
   `aprobada y vigente`, `vencida`.
3. **Disparo único.** El área admin **deja de disparar y de vincular** validaciones. Las once rutas de
   disparo, reenvío, vinculación y `mock` responden **`410 Gone`** con `code: endpoint_deprecado`. La
   única forma de originar una validación de identidad es el módulo Identidad.
4. **Sin migración de datos.** Ni backfill ni lectura de compatibilidad hacia la tabla admin.
5. **La precedencia del baúl no cambia.** La D8 del `ADR-0025` (baúl > identidad) sigue gobernando
   tanto la resolución de firma como el copy de la ficha: a quien tiene firma de baúl vigente no se le
   pide prevalidar.

## Alternativas consideradas

### Opción 1 — Fuente única en el módulo Identidad, admin en solo consulta (ELEGIDA)

**Pros:** elimina la duplicación de raíz; una sola implementación de vigencia, precedencia y
normalización; cierra el sexto disparador que el `ADR-0039` no pudo acotar; el rótulo de la ficha
refleja lo que el operador acaba de hacer en Identidad, que es el requerimiento original.
**Cons:** revoca una decisión aceptada; los mandatarios validados solo en la tabla admin pierden el
certificado del sello hasta reprevalidar (ver Consecuencias); obliga a reordenar los Features #11348 y
#11349 detrás de esta ola.
**Esfuerzo:** L. **Riesgos:** medio — mitigados por el hecho de que el sello ya falla en silencio.

### Opción 2 — Sincronizar los dos almacenes (proyección o evento)

Mantener ambas tablas y propagar de una a otra.
**Pros:** no revoca el `ADR-0034`; sin cambio observable en las rutas admin.
**Cons:** conserva la duplicación y le añade una máquina de sincronización con sus propios modos de
fallo; la pregunta «¿cuál manda si difieren?» no tiene respuesta buena; los seis disparadores de correo
siguen vivos. Es la opción que más código añade para dejar el problema donde estaba.
**Rechazada.**

### Opción 3 — Lectura de compatibilidad: leer Identidad y, si no hay, caer a admin

**Pros:** ningún mandatario pierde el certificado del sello; migración implícita y gradual.
**Cons:** perpetúa el segundo almacén sin fecha de retirada, y con él la ambigüedad de precedencia;
cada consumidor nuevo hereda la doble lectura. El PO evaluó explícitamente el coste de no tenerla
(decisión DA-4) y lo aceptó.
**Rechazada por decisión del PO humano el 2026-08-20.**

## Consecuencias

### Aceptadas

- **Un mandatario cuya identidad solo exista en `admin.admin_identity_validations` queda sin
  certificado en el sello del mandato** hasta que prevalide en el módulo Identidad. No hay error
  visible: el sello ya devuelve `(null, null, null)` sin excepción, así que el efecto es *menos sellos
  con certificado*, no un fallo. Decisión **DA-4**, tomada por el PO humano con el coste a la vista.
- **La UI de documentos personalizados se oculta sin cerrar su API** (HU #11686 bajo el Feature
  #11309): los documentos ya cargados **siguen aplicándose** en la generación documental. Consecuencia
  aceptada por el PO humano el 2026-08-20.
- Las once rutas retiradas devuelven `410 Gone`, no `404`: el cliente distingue «esto existió y se
  retiró» de «esto nunca existió». Decisión **DA-1**.

### A vigilar

- Si nadie prevalida, **el envío del correo se corre al momento de radicar**. Eso desplaza carga y
  modos de fallo al gate de radicación, y es un caso de enrutamiento que el Feature **#11348** debe
  contemplar. El Feature #11689 lo inventaría antes de comprometer alcance.
- El código admin que queda huérfano (`IAdminIdentityValidationProvider`,
  `KyverumAdminIdentityValidationProvider`, las suites `Flit.Admin.Tests/Identity/*`) se retira en esta
  misma ola (decisión **DA-5**), **salvo** la lógica de intentos fallidos de la HU #11504 si resulta
  ser compartida con el módulo Identidad.

## Dependencias

- **`ADR-0040-tracking-identidad-por-persona.md` debe pasar a `Aceptado`** por el Líder Técnico humano:
  es dependencia declarada de la consulta por documento y **bloquea el merge del Feature #11687**.
- El `ADR-0036` restringe la prevalidación a **persona natural**. De ahí que el copy deba contemplar el
  caso **NIT**: un mandatario puede registrarse con tipo de documento NIT, y a esa persona jurídica no
  le aplica prevalidación. Enlazarla al módulo Identidad sería mandarla a un flujo imposible.
