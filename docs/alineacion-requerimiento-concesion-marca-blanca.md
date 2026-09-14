# Alineación — `Requerimiento_Concesion_Marca_Blanca.md` vs. épicas #12235 y #12237

> Generado: 2026-09-10 · rama `feature/AB-12235-concesion-jerarquia-clientes` @ `a9ebf2b2`
> · Contrasta el requerimiento escrito del PO (`docs/Requerimiento_Concesion_Marca_Blanca.md`) contra:
> las decisiones D1-D15 (`docs/analisis-jerarquia-companias-concesionario.md` §8), las decisiones 1-9
> de marca blanca (`docs/epica-12237-marca-blanca-criterios-propuestos.md`), las 24 HUs vigentes de
> #12235 y el código verificado. **Sustituye** a `docs/alineacion-po-concesion-marca-blanca.md`
> (definición verbal). **Nada modificado en ADO.**

Marcas: ✅ cubierto · ✅⁰ ya existe hoy · ⚠️ decisión de diseño pendiente · ❌ contradice una decisión
cerrada · ➕ nuevo, sin HU.

---

## 0. Veredicto en una línea

**Concesión (#12235): alineada en el 90 %.** Lo que falta es pequeño y concreto (3 reglas transversales
sin HU y una decisión de trazabilidad). **Marca Blanca (#12237): alineada en identidad visual y
dominio, pero el requerimiento le añade dos cosas que hoy no existen en ningún sitio** — la política de
OT **por exclusión** y los **correos con marca** — y una que estaba cerrada en contra: **documentos de
las hijas**. Esas tres son las que mueven estimación y la que más importa (documentos) mueve
cumplimiento legal.

Y hay un cambio conceptual que afecta a las dos épicas: **el requerimiento define dos clases de cabeza
de grupo con reglas de OT distintas**. Hoy F0 solo sabe "es cabeza de grupo" (`is_group_parent`). Hace
falta que sepa **de qué clase** (§3.1).

---

## 1. Módulo de Concesiones

| RF | Enunciado | Estado | Dónde vive / qué falta |
|---|---|---|---|
| CON-01 | Crear organización "de tipo Concesión" con nombre, estado, contacto, OT, admin y usuarios | ✅ | Todo existe en `identity.tenants` + grants + usuarios. **Matiz de vocabulario:** por D10 no se crea un valor nuevo de `tenant_type`; "tipo" = capacidad de cabeza de grupo (`is_group_parent`, #12318) + clase (§3.1) |
| CON-02 | El admin de plataforma asocia OT; Concesión e hijas solo radican ahí; retirar un OT no toca históricos | ✅ | D14 + #12346/#12347/#12348 + #12350. **Ojo #12350:** hoy revocar un grant **oculta de la bandeja OT trámites ya entregados** (`OtClientProcedureRepository.cs:1383-1418`); CA-TRANS-01 exige lo contrario → ese fix es parte obligatoria de #12350 |
| CON-03 | Admin de Concesión crea hijas, edita, activa/inactiva, crea usuarios | ✅ / ⚠️ | #12345, #12353, #12354, #12356. **Verificar que #12353 incluya activar/inactivar la hija** (hoy habla de "configuración") |
| CON-03 | Las hijas no administran OT propios | ✅ | #12346 AC2/AC3 |
| CON-04 | Concesión radica a su nombre; hijas al suyo; selector acotado | ✅⁰ / ✅ | Ya existe (hija = tenant completo, S1) + #12348/#12351 |
| CON-04 | Cada trámite conserva la **Concesión padre**, compañía radicadora, usuario y OT | ⚠️ | Compañía, usuario y OT ya se guardan. **El padre no:** el diseño lo deriva en consulta desde `tenants.parent_tenant_id`, no lo guarda en el trámite → §3.2 |
| CON-05 | Consulta consolidada: trámites propios + de cada hija, reportes | ✅ | #12358, #12359, #12360, #12362-#12364 |
| CON-05 | …**detalle y documentos** de los trámites bajo su estructura | ❌ → pendiente | Contradice D2. El propio documento lo deja **abierto en el pendiente 13** ("mismo alcance que marca blanca"). Tratar junto con MB-05 (§3.3) |
| CON-05 | La hija solo consulta su propia operación | ✅ | F0 fail-closed (#12321) + suite anti-fuga (#12322) |
| CON-06 | Notificaciones sin cambios para Concesión | ✅⁰ | Cero trabajo; entra como criterio de **no regresión** en la suite de paridad |

## 2. Módulo de Marca Blanca

| RF | Enunciado | Estado | Dónde vive / qué falta |
|---|---|---|---|
| MB-01 | Crear "organización de tipo Marca Blanca" con dominio, identidad visual y **restricciones de OT** | ✅ / ➕ | Dominio e identidad: criterios de #12237. Restricciones de OT: **nuevo** (MB-03). Mismo matiz de "tipo" que CON-01 |
| MB-02 | Crea hijas, usuarios propios y de hijas; sin acceso fuera de su red | ✅ | Hereda el bloque A de #12235 íntegro + F0 |
| MB-03 | **Lista por exclusión:** opera en todos los OT de la plataforma **excepto los restringidos**; las hijas heredan; el bloqueo se propaga; la hija no puede habilitar un bloqueado; solo futuras radicaciones | ➕ | **No existe.** Hoy solo hay lista por inclusión (`tenant_transit_office_grants`); sin grant = no radica. Hace falta un mecanismo de **bloqueos** y que el punto único (`TransitOfficeGrantGate.IsEnabledForTenantAsync`, `GET /tramites/transit-offices`) resuelva "todos los operables − bloqueos de la cabeza" → §3.1. **8-13 SP** (BE 5-8, FE 3-5) |
| MB-04 | MB e hijas radican; selector según restricciones heredadas; a nombre de quien radica; relación con la MB padre; usuario responsable | ✅ / ⚠️ | Igual que CON-04; la "relación con el padre" es la misma decisión de §3.2 |
| MB-05 | Admin MB consulta sus trámites, los de todas sus hijas, **detalle completo y documentos** | ✅ / ❌ | Trámites y detalle: #12358/#12362. **Documentos: contradice D2 y aquí es firme** (CA-MB-03) → §3.3. **8-13 SP** + D1 |
| MB-06 | Logo, colores, dominio; aplica a acceso/navegación, encabezados y **notificaciones**; validaciones de formato/peso/dimensiones/color/dominio | ✅ / ❌ / ➕ | Interfaz y dominio: criterios de #12237. **Notificaciones: contradice la decisión 8** ("solo interfaz") → §3.4. Validaciones: detalle nuevo, entra en la HU de configuración |
| MB-07 | Notificaciones con nombre, logo, colores y **plantilla/estructura visual de la marca**; misma infraestructura; no se presenta como Flit; datos funcionales iguales; **plantilla de respaldo** | ❌ ➕ | Es exactamente el **"tema de correo"** (no editor libre): 5-8 SP. El pendiente 9 deja abierto el editor; recomendación: **no** en esta fase → §3.4 |
| MB-08 | Configurador sencillo: cargar logo, elegir colores, **previsualizar, guardar y publicar**; aplica a sesiones nuevas | ➕ | UI nueva (5-8 SP). "Publicar" implica borrador/publicado (dos estados, no versionado completo) |

## 3. Los cuatro puntos que cambian el diseño o la estimación

### 3.1 Dos clases de cabeza de grupo → F0 necesita saber cuál (➕, pequeño, urgente)

El requerimiento (§1, RF-CON-02 vs RF-MB-03) fija reglas de OT **opuestas**: Concesión = lista por
**inclusión**; Marca Blanca = lista por **exclusión** sobre todos los OT de la plataforma. Además, solo
la MB tiene marca y dominio. Hoy `identity.tenants` solo tiene `is_group_parent` (booleano, #12318).

> **Actualización 2026-09-10 (tarde):** el usuario decidió que la clase sea un **valor de `tenant_type`**
> (`CONCESION` | `MARCA_BLANCA`, catálogo de 5 valores) en lugar de una columna `group_kind`; `is_group_parent`
> queda acoplado al tipo por CHECK. Ver D19 en `analisis-jerarquia-companias-concesionario.md` §8.1 y la
> revisión de ADR-0057. La propuesta original se conserva a continuación para trazabilidad.

~~**Propuesta:** columna `group_kind`~~ (`CONCESION` | `MARCA_BLANCA`, nullable, obligatoria cuando
`is_group_parent = true`, forzada por CHECK) en una migración pequeña (DDL 109) **dentro de F0**, que
sigue abierto. Respeta D10 (`tenant_type` intacto), es lo que consultan el gate de OT, la resolución
de marca y el login por dominio. **3 SP**, HU nueva en #12254. Alternativa descartada: deducir la clase
de "tiene fila de branding" — frágil y acopla la seguridad de OT a la configuración visual.

Y la política de OT se implementa **una vez** en el punto único que ya identificó el brief C:

| Clase | Lista efectiva de la red |
|---|---|
| Concesión | `grants(cabeza)` (inclusión) — como hoy, heredada por #12347 |
| Marca Blanca | `OT operables (IOtOperabilityGate) − bloqueos(cabeza)` (exclusión) — **tabla nueva** `admin.tenant_transit_office_blocks` |
| Sin jerarquía | `grants(tenant)` — sin cambios |

**Impacto en HUs vigentes:** #12347 y #12348 pasan a "lista efectiva **según la clase** de la cabeza"
(+2 SP cada una) y aparece **una HU nueva** en #12256 para los bloqueos (SuperAdmin crea/retira bloqueo,
propagación automática, hija no puede habilitar uno bloqueado, auditoría): **5 SP BE** + **3 SP FE** (#12351
crece o HU aparte). El pendiente 5 del requerimiento (restricciones adicionales en la hija) **queda fuera
hasta que se decida**; el modelo lo admite después sin migración nueva si los bloqueos llevan `tenant_id`.

### 3.2 "Cada trámite conserva la Concesión/MB padre" (⚠️ decisión antes de #12348)

RF-CON-04, RF-MB-04, la regla transversal 7 y CA-TRANS-01 piden que la trazabilidad del padre
**sobreviva** a cambios de jerarquía. El diseño actual **deriva** el padre en la consulta
(`tenants.parent_tenant_id`), y #12355 AC3 dice explícitamente que al desvincular una hija "la cabeza
deja de tenerla en su alcance de lectura". Son compatibles solo si el padre se **guarda en el trámite al
radicar**.

**Propuesta:** columna `parent_tenant_id_at_creation` (nullable, sin FK activa, solo trazabilidad) en
`tramites.procedure_instances`, escrita en el mismo punto donde #12348 valida el OT. **+1 SP en
#12348.** Lo que **sí** requiere decisión del PO (**D16**): tras desvincular una hija, ¿la ex-cabeza
sigue viendo los trámites radicados mientras era su hija? Guardar la columna es barato y hay que
hacerlo ahora (no se puede reconstruir después); la regla de lectura se decide cuando quieran.

### 3.3 Documentos de las hijas (❌ D2 revocada para MB; pendiente 13 para Concesión)

RF-MB-05 y CA-MB-03 son firmes: la MB ve **los documentos** de los trámites de sus hijas. Para la
Concesión queda en el pendiente 13. Consecuencias ya mapeadas el 2026-09-09:

- **D1 (Ley 1581) pasa de "no bloquea estimar" a bloqueante de producción** del bloque B: cédulas,
  firmas y datos biométricos del titular que autorizó a la hija, no al padre. Hace falta la figura
  jurídica (encargado/autorización en cadena) **antes de exponerlo**.
- FUR, mandato, compraventa, solicitud virtual y certificado Kyverum **no tienen endpoint propio**: son
  adjuntos. Abrir la descarga de anexos abre todos los documentos generados a la vez.
- `preview-url` es una **presigned S3 anónima sin revocación** → la lectura de red debe ir **solo** por
  `GET …/download` proxeado por la API.
- **Las descargas no se auditan hoy.** #12361 (auditoría del acceso consolidado) deja de ser deseable y
  pasa a **obligatoria y ampliada a descargas** (+2 SP).
- El download standalone **niega cross-tenant incluso al SuperAdmin**: dos criterios de aislamiento
  conviviendo; hay que unificarlos al abrir la ruta de red.

**HU nueva en #12257:** "Consulta y descarga de documentos de trámites de la red (solo lectura,
proxeada, auditada)" **8-13 SP**, con AC condicionado a la clase (`MARCA_BLANCA` sí; `CONCESION` según
pendiente 13). El bloque B sube de 32 a **~45 SP**.

### 3.4 Correos con marca (❌ decisión 8 revocada; RF-MB-06/07)

Lo que pide RF-MB-07 es **tema**, no editor: nombre, logo, colores y una estructura visual fija de la
marca, con **plantilla de respaldo** cuando la configuración esté incompleta. Es lo recomendable y lo
barato. Estado del código: plantillas globales, solo SuperAdmin, solo listado y muestra
(`/admin/plataforma/notificaciones/plantillas`); no hay nada por tenant.

**Propuesta (5-8 SP):** las plantillas actuales reciben un **tema** resuelto por red (la hija resuelve
por `parent_tenant_id`; sin red o sin tema → tema FLIT). El tema cubre: nombre visible del remitente,
logo, color principal, encabezado/pie. Sin HTML libre → sin sanitización ni riesgo de phishing con
marca del cliente. Resuelve los pendientes 9 (solo logo/colores/nombre), 11 (aplica a envíos nuevos,
histórico intacto — el correo ya enviado no cambia) y 12 (respaldo = tema FLIT).

**Lo que queda fuera y hay que decirlo en #12237:** dirección de remitente con dominio del cliente
(SPF/DKIM/DMARC por cliente = "infraestructura de correo independiente", que el §8 del requerimiento
excluye). Remitente = nombre de la marca, dirección de FLIT. Pendiente 10 resuelto por exclusión.

## 4. Reglas transversales y no funcionales

| # | Regla | Estado | Comentario |
|---|---|---|---|
| 1 | Una hija pertenece a un único padre | ✅ | `parent_tenant_id` es una columna, no una tabla puente (D12, #12318). **Responde el pendiente 3 del requerimiento: no** |
| 2 | Un usuario solo actúa donde tiene permisos | ✅ | Existente + #12320/#12321 |
| 3 | Trámite a nombre de quien radica, no del padre | ✅⁰ | Hija = tenant completo (S1) |
| 4 | Vista consolidada del padre | ✅ | Bloque B |
| 5 | **Inactivar el padre impide radicar a las hijas** | ➕ | **No existe.** No hay comprobación de `tenants.is_active` ni en el login ni en la creación del trámite; la única es sobre el tenant OT receptor (`OtOperabilityGate.cs:62`). HU nueva **3 SP** (gate en creación de trámite: padre activo; conviene meterla en #12348) |
| 6 | Inactivar una hija impide radicar a sus usuarios | ⚠️ | Mismo hallazgo: hoy inactivar una compañía **no** bloquea nada en el runtime de trámites. Es comportamiento actual de todas las compañías; corregirlo es un cambio transversal (2-3 SP) que conviene en la misma HU que la regla 5 |
| 7 | Cambios de permisos no borran trazabilidad | ⚠️ | §3.2 |
| 8 | Auditoría de configuración, permisos, restricciones e identidad visual | ✅ / ➕ | Vínculos: #12323 (`tenant_hierarchy_audit`). Grants: `tenant_config_audit_logs` existente. Bloqueos y branding: **nuevos**, entran en sus HUs |
| NF | Aislamiento entre concesiones, MB y compañías | ✅ | F0 + #12322 |
| NF | Auditoría con valores anteriores y nuevos | ⚠️ | `tenant_hierarchy_audit` registra LINK/UNLINK sin "antes/después" (no aplica: es un vínculo). Para branding y bloqueos: diseñar con `old_value/new_value` desde el inicio |
| NF | Falla al cargar la personalización → respaldo | ✅ | Ya en criterios de #12237 ("URL desconocida presenta identidad FLIT"); ampliar a "error de carga" |
| NF | Compatibilidad en clientes de correo | ➕ | Criterio de QA para la HU de tema de correo (Outlook/Gmail/Apple Mail: CSS inline, sin fuentes externas) |
| 8 (fuera) | Sin infraestructura de correo por MB; MB no administra OT directamente | ✅ | Coherente con §3.4 y con D14 (el admin de plataforma restringe) |

## 5. Lo que el requerimiento **no dice** y ya está decidido — confirmar que sigue

| Decisión | Fuente | Riesgo si se pierde |
|---|---|---|
| El dominio **acota el login**: en `app.cliente.com` solo autentican usuarios de esa red; esa red no entra por el dominio FLIT | Decisiones 4-6 (2026-09-10) | Sin esto la marca blanca tiene un agujero: cualquier usuario de la red ve FLIT entrando por el dominio general |
| **Dominio propio del cliente**, no subdominio de FLIT; verificación de titularidad y certificados por cliente | Decisión 9 | Responde al pendiente 6 del requerimiento |
| La hija **hereda la marca sin anular** ni configurar nada propio | Decisión 7 | Coherente con RF-MB-08 (solo el admin MB configura) |
| Anti-enumeración: la resolución de marca por host no revela qué compañías o correos existen | Criterio ➕ de #12237 | Seguridad |
| Profundidad 2 forzada por BD; SuperAdmin sin cambios; hija no ve al padre ni a hermanas | D12, D6, F0 | Ya implementado en la rama |
| El FUR no se personaliza (formato oficial, Anexo 46); mandato y PDF siguen con marca FLIT | Fuera de alcance de #12237 | El requerimiento solo pide marca en plataforma y correos: coherente |

## 6. Pendientes del requerimiento (§9) que ya tienen respuesta

| # | Pendiente | Respuesta disponible |
|---|---|---|
| 1 | Matriz de roles | = **D3**. Implementado bajo "cualquier AdminCompany de la cabeza puede crear hijas e invitar". Si quieren "administrador de red" aparte: HU nueva + migración de usuarios |
| 3 | Hija en más de un padre | **No** — lo fija la regla transversal 1 del mismo documento y `parent_tenant_id` |
| 4 | Alcance de reportes | #12359/#12360/#12364 (estadísticas agregadas, reportes filtrables por hija) |
| 5 | Restricciones adicionales en la hija | Fuera hasta decidir; el modelo lo admite después (§3.1) |
| 6 | Dominio personalizado | Decisión 9: dominio propio, verificación de titularidad, certificados por cliente, instrucciones DNS |
| 9 | Nivel de personalización de correos | Recomendación: **solo nombre, logo, colores y estructura fija** (§3.4) |
| 10 | Remitente | Nombre de la marca + dirección de FLIT; dominio propio de envío **fuera** (§8 del requerimiento) |
| 11 | Cambiar la identidad tras enviar | Los correos enviados no cambian; aplica a envíos nuevos (RF-MB-08 ya lo dice) |
| 12 | Plantilla de respaldo | Tema FLIT actual |
| 13 | Documentos para la Concesión | **Decisión del PO pendiente** + D1 (Ley 1581) para ambas clases |
| — | **D16 (nuevo):** ¿la ex-cabeza sigue viendo los trámites radicados mientras la hija estaba vinculada? | §3.2 |

Siguen sin respuesta y sí importan: **2** (datos obligatorios de alta), **7/8** (formatos de logo y
contraste — recomendación: WCAG AA 4.5:1 validado en el configurador, PNG/SVG ≤ 512 KB), **13** y **D1**.

## 7. Ajustes aplicados en ADO (2026-09-10, confirmados por el usuario)

Plan del tech-lead-agent: `.claude/state/concesion-decomp/ado-cambios-2026-09-10.json` (19 updates, 27 creates, 5 comentarios, 3 relaciones retiradas, 15 sin cambio). Resultado:

| Feature | HUs | SP | HUs nuevas |
|---|---|---|---|
| #12254 F0 | 7 | 33 | **#12406** ~~`group_kind`~~ tipo de cabeza en `tenant_type` (D19) + `parent_tenant_id_at_creation` (5) |
| #12255 A | 6 | 28 | — (título y CF para ambas clases; #12345/#12355/#12357 ajustadas) |
| #12256 C | 6 (+2 Removed) | 35 | **#12407** bloqueos de OT MB (5) · **#12408** consola SuperAdmin bloqueos (3) · **#12409** gate compañía/cabeza activa (3); #12347 → 8 SP, #12350 → 5 SP |
| #12257 B | 9 | 48 | **#12410** documentos de la red proxeados y auditados (8) · **#12411** FE documentos (3); #12361 → 5 SP obligatoria |
| **#12235** | **28** | **144** | |
| #12366 Identidad | 5 | 23 | #12412 modelo/borrador/publicar · #12413 validaciones · #12418 resolución por dominio + respaldo · #12414 configurador · #12429 suite paridad/anti-enumeración |
| #12367 Aplicación | 3 | 10 | #12415 inventario · #12419 tema desde primera pintura · #12420 saneamiento |
| #12368 Dominio | 3 | 11 | #12416 registro · #12417 sello Gateway/CORS · #12421 proxy de borde |
| #12369 Acceso | 3 | 14 | #12422 login/recuperación acotados · #12423 enlaces por red · #12424 FE |
| #12370 Provisión | 3 | 12 | #12425 titularidad DNS · #12426 certificados · #12427 FE estado |
| **#12405** `[NOTIFICACIONES]` Tema de correo (nuevo) | 3 | 10 | #12428 resolutor + estructura + respaldo · #12430 remitente · #12431 previsualización |
| **#12237** | **20** | **80** | |

**Segundo ajuste (2026-09-10, tarde) — D19:** plan `.claude/state/concesion-decomp/ado-cambios-2026-09-10-tenant-type.json`. La clase de la cabeza pasa a `tenant_type` (5 valores, sin `group_kind`); reescritas #12406 (SP 5→8; F0 = 36 SP, #12235 = 147 SP), #12355, #12357, #12345, #12347, #12407, #12412, #12318 (AC7), #12427, Features #12254/#12255/#12366 y las dos épicas (solo la propuesta vigente). 14 comentarios de trazabilidad. Detalle en el `.md` homónimo.

**Épicas:** #12235 reescrita (rev 5) por autorización del usuario del 2026-09-10 — título «Capacidad de Concesión: cabeza de red, organismos asociados y gestión de clientes hijos», descripción original del PO tachada y visible, propuesta vigente debajo, sección «Pendientes del PO» (D1, D16, pendiente 13). #12237 (rev 7) con el mismo formato: original del PO tachado + texto vigente.

### Propuesta original (para trazabilidad)

| Dónde | Ajuste | SP |
|---|---|---|
| **F0 #12254** | HU nueva: `group_kind` (CONCESION / MARCA_BLANCA) en `identity.tenants` + CHECK + snapshot del padre en el trámite (`parent_tenant_id_at_creation`) | +3 |
| **C #12256** | #12347 y #12348: lista efectiva **según la clase** de la cabeza (+2 c/u). HU nueva: bloqueos de OT para Marca Blanca (SuperAdmin, propagación, auditoría) BE 5 + FE 3. #12348: gate "padre activo / hija activa" (regla 5 y 6) +3. #12350: incluir el fix de la bandeja OT (CA-TRANS-01) | +15 → **~33 SP** |
| **B #12257** | HU nueva: documentos de trámites de la red, solo lectura, proxeada y auditada (clase MB firme; Concesión según pendiente 13). #12361 obligatoria y ampliada a descargas | +10-15 → **~45 SP** |
| **#12237** | Reescribir descripción: MB = cabeza de grupo de clase `MARCA_BLANCA` + política de OT por exclusión (vive en C) + identidad visual + dominio + **tema de correo**. Dependencias: F0 + A + **C** (no solo F0 + A). Sacar de "fuera de alcance" los correos; dejar fuera remitente con dominio propio, PDF/mandato/FUR. Descomponer: dominio/login (ya analizado), branding + configurador (5-8), tema de correo (5-8), validaciones | por descomponer |
| **Análisis §8** | D2 revocada para MB (documentos), D8-marca-blanca revocada (correos), D16 abierta, D1 bloqueante | — |
| **Épica #12235** | Anotar D1 como bloqueante de producción del bloque B; título/descripción siguen contradiciendo D10 ("tipo nuevo") | — |

**Total #12235: de ~109 a ~135 SP.** #12237 sin descomponer todavía; orden de magnitud 40-55 SP con
dominio propio y certificados.

## 8. Opinión

El documento es mejor que las definiciones verbales: resuelve el Modelo 2 (fuera), fija quién asocia
los OT y hace explícito que Marca Blanca y Concesión tienen **reglas de OT distintas** — eso último
es lo más valioso, porque hasta hoy tratábamos la MB como "Concesión + marca" y no lo es.

Lo que pediría antes de seguir construyendo por encima de F0: (1) **D1**, porque con documentos de
las hijas ya no es un pendiente, es un bloqueo de producción; (2) confirmar el **tema de correo** como
alcance de RF-MB-07 y cerrar el editor fuera; (3) **D16** (qué ve la ex-cabeza) para no tener que
migrar datos después; (4) el pendiente 13. Con eso, los cambios de §7 se aplican en ADO en una sesión
y F0 absorbe el `group_kind` sin reabrir nada ya committeado.
