# Alineación con el PO — Concesión y Marca Blanca

> **Sustituido** el 2026-09-10 por `docs/alineacion-requerimiento-concesion-marca-blanca.md` (contraste contra el requerimiento escrito `docs/Requerimiento_Concesion_Marca_Blanca.md`). Se conserva por trazabilidad de D14/D15.

> Generado: 2026-09-10 · rama `feature/AB-12235-concesion-jerarquia-clientes` @ `a9ebf2b2`
> · Contrasta las definiciones que el PO entregó verbalmente el 2026-09-10 contra: las decisiones
> cerradas el 2026-09-09 (`docs/analisis-jerarquia-companias-concesionario.md` §8), las del
> 2026-09-10 sobre marca blanca (`docs/epica-12237-marca-blanca-criterios-propuestos.md`), las 26 HUs
> de la épica #12235 y el código verificado.
> · **Actualizado el mismo día** con dos decisiones cerradas por el PO (D14, D15) y su aplicación en ADO.

Marcas: ✅ alineado y cubierto · ✅⁰ alineado y **ya existe hoy** (cero trabajo) · ⚠️ matiz que hay que
decidir · ❌ **contradice una decisión anterior** · ➕ nuevo, sin cobertura.

---

## 1. Concesión

| # | Definición del PO | Estado | Dónde vive |
|---|---|---|---|
| C1 | Una Concesión es una compañía con Organismos de Tránsito asociados | ✅⁰ | Modelo 1 (§5.4). `admin.tenant_transit_office_grants` ya es exactamente eso |
| C2 | Crea sus propios usuarios y solo radica en sus OT asociados | ✅⁰ | Comportamiento de cualquier compañía hoy: `TramiteLifecycleService.EvaluarEntregaAsync` rechaza con `OrganismoNoHabilitado` si no hay grant |
| C3 | **Los OT los asocia el SuperAdmin** al crear la empresa como Concesión | ✅ **D14** | Aplicado: #12346 AC7 (rechazo en servidor si la Concesión edita su lista; 2→3 SP) y #12357 AC6/AC7 (selección de OT en el alta/marca de Concesión; solo lectura para la Concesión; 3→5 SP). Las compañías sin jerarquía conservan la autogestión de #11228 |
| C4 | Crea compañías hijas | ✅ | #12345 (política de cabeza de grupo) + #12356 (panel de red) |
| C5 | Crea usuarios AdminCompany para sus hijas | ✅ | #12354 (invitación con destino y rol acotados) |
| C6 | Administra las compañías hijas | ✅ | #12353 (configuración de hijos) — escritura sobre configuración, D11 |
| C7 | Ve sus trámites y los de sus hijas | ✅ | #12358 (lectura de red, rechazo de toda escritura) + #12362/#12363. Solo lectura por D11 |
| C8 | …**incluyendo la documentación** | ❌ | **Contradice D2 (2026-09-09): "los artefactos quedan fuera; solo trámites y estadísticas"**. Esa decisión quitó ~10 SP del bloque B y es la que mantiene D1 (Ley 1581) en "no bloquea estimar". Ver D-C8 |
| C9 | La hija crea sus radicadores desde su AdminCompany | ✅⁰ | Es el comportamiento de cualquier compañía; nada que construir |
| C10 | La hija radica a los OT de su padre | ✅ | #12346 (grant gobernado por el sistema, inmutable para el hijo) + #12347 (lista efectiva heredada) + #12348 (validación en la creación) + #12351 (wizard acotado). Interruptor `inherited_configuration` ya en BD (#12323) |

### Lo que el PO **no mencionó** y está en alcance hoy

| | Qué | HUs | Pregunta |
|---|---|---|---|
| N1 | **Modelo 2 — un OT como cabeza de grupo** (D9, 2026-09-09) | #12349, #12352 | **Resuelto — D15: fuera.** Ambas HUs en `Removed` (−6 SP; #12256 queda en 18 SP). La cobertura anti-fuga de #12352 la lleva #12322 |
| N2 | **D3** — qué AdminCompany de la Concesión puede crear hijos e invitar | #12345, #12354 | Implementado bajo el supuesto "todos, sin distinguir". Confirmar |
| N3 | Estadísticas y reportes consolidados de la red | #12359, #12360, #12364 (16 SP) | El PO dijo "ver trámites"; no dijo estadísticas. ¿Siguen? |
| N4 | Auditoría de quién de la Concesión consultó qué trámite de qué hijo | #12361 | Si entra C8 (documentación), esta HU pasa de deseable a **obligatoria** |
| N5 | Una hija **no ve nada** del padre ni de sus hermanas | F0 (#12321, #12322) | Es el diseño fail-closed y lo que verifica la suite anti-fuga. Confirmar que es lo esperado |

### Decisiones que hacen falta

**D-C3 · Quién asocia los OT.** *Resuelto (D14):* exclusivo del SuperAdmin para Concesiones e hijas, en el flujo de alta; las compañías sin jerarquía siguen como hoy. Aplicado en #12346 y #12357.

**D-C8 · "Incluyendo la documentación".** Antes de estimar hay que saber qué significa:

- (a) **Ver el estado documental** del trámite (checklist de requisitos, qué documento está cargado, cuál falta, observaciones). Eso es parte del "trámite" y **entra en #12358/#12362 sin costo extra**.
- (b) **Abrir o descargar** los documentos: FUR, mandato, compraventa, certificados de identidad, anexos. Eso es lo que D2 sacó. Si vuelve a entrar:
  - Reabre **D1 (Ley 1581)** con toda su fuerza: cédulas, firmas y datos biométricos del titular que autorizó al hijo, no al padre. Hace falta la figura jurídica **antes de producción**, no después.
  - Arrastra hallazgos ya mapeados de la ruta de descarga: FUR/mandato/compraventa/certificado **no tienen endpoint propio**, son adjuntos (abrir uno abre todos); `preview-url` es una **presigned S3 anónima sin revocación**; **las descargas no se auditan**; el standalone **niega cross-tenant hasta al SuperAdmin** (dos criterios de aislamiento conviviendo).
  - Estimación: +8-13 SP en el bloque B (descarga solo por `GET …/download` proxeado, nunca `preview-url`; auditoría de descarga obligatoria ampliando #12361; sin granularidad por tipo salvo trabajo adicional).

**Recomendación:** confirmar (a) como incluido y tratar (b) como HU nueva del bloque B, condicionada a D1. No mezclarlo en #12358.

---

## 2. Marca Blanca

| # | Definición del PO | Estado | Comentario |
|---|---|---|---|
| M1 | No tiene OT asociados: **radica en todos los OT activos** de la plataforma | ➕ ⚠️ | Hoy **no existe** una compañía "sin grants que radique en todo": sin grants el wizard devuelve `{ items: [] }` y la radicación falla. Es una **política de OT nueva** ("todos los operables") que no es solo estética: es la única diferencia funcional real entre Marca Blanca y Concesión. Ver D-M1 |
| M2 | Crea hijas, sus AdminCompany, las administra, ve trámites (+documentación) | ✅ / ❌ | Hereda íntegro el mecanismo de red de #12235 (criterio ya propuesto). Aplican C4-C8 con la misma observación sobre **documentación (D-C8)** |
| M3 | Ella y sus compañías/usuarios solo acceden desde su dominio configurado | ✅ | Decisiones 1-6 y 9 del 2026-09-10: dominio propio del cliente (`app.cliente.com`), único por red, acota el login, sin entrada por el dominio FLIT, certificados por cliente en alcance |
| M4 | Cambia colores y logos por los de su marca | ✅ | Criterios "Personalización visual" (logo, paleta, nombre; resolución antes del login; vive solo en el padre; la hija hereda sin anular). Nota: **no existe hoy ninguna tabla ni endpoint de branding**; todo es nuevo |
| M5 | Edita el **cuerpo de los correos** de notificación (imágenes, encabezados…) con un editor online | ❌ ➕ | **Contradice la decisión 8 del 2026-09-10: "alcance de la marca: solo interfaz; correos, PDF y FUR siguen con marca FLIT (salvo el enlace)"**. Ver D-M5 |

### Decisiones que hacen falta

**D-M1 · Política "todos los OT".** Dos formas de construirlo:

1. **Bandera en la cabeza de grupo** (`all_transit_offices = true`): `TransitOfficeGrantGate` y `GET /tramites/transit-offices` la interpretan como "cualquier OT del catálogo que pase `IOtOperabilityGate`". Las hijas la heredan por #12347 igual que heredan una lista. Robusta ante OT nuevos. **3-5 SP backend + 2 SP frontend.**
2. Grants automáticos a todos los OT existentes: frágil (cada OT nuevo obliga a regenerar) y ensucia la tabla. Descartar.

Preguntas al PO: ¿"activos" = operables según la ventana/feature-flags del OT (`IOtOperabilityGate` sigue mandando)? ¿Puede una Marca Blanca tener **también** lista acotada, o "marca blanca ⇒ todos" siempre? ¿Y una Concesión puede pedir "todos"? Si la respuesta es "la política es independiente de la marca", entonces es una **propiedad de la cabeza de grupo** y va en el Feature C de #12235, no en #12237.

**D-M5 · Correos con marca.** Estado del código: las plantillas son **globales**, solo SuperAdmin, y los endpoints solo **listan y muestran** (`/admin/plataforma/notificaciones/plantillas`, `GET` + `GET /{id}/muestra`). No hay edición, ni por tenant, ni versionado. Lo que pide el PO implica:

- Plantillas **por red** (tabla nueva con `tenant_id` de la cabeza; la hija resuelve por `parent_tenant_id` al enviar) con **fallback** a la de FLIT.
- Lista explícita de **qué correos aplican** ("que aplique" no es criterio): invitación, recuperación de contraseña, cambio de estado del trámite, asignación de placa, firma… cada uno con sus variables.
- **Editor online = HTML arbitrario en correos**: sanitización obligatoria (XSS al previsualizar, inyección de enlaces de phishing con la marca del cliente), imágenes hospedadas (S3 + URLs públicas o CID), vista previa con datos de muestra, versionado y quién cambió qué.
- Remitente: si además quieren `noreply@cliente.com`, entra SPF/DKIM/DMARC por dominio del cliente — es otro producto.

Estimación honesta: **13-21 SP** con editor libre. **Recomendación: dos fases.**
- *Fase 1 — "tema de correo"* (5-8 SP): logo, color principal, encabezado y pie de texto, nombre visible del remitente, aplicados sobre las plantillas existentes de FLIT. Cubre "que salgan con su marca" sin HTML arbitrario y sin nueva superficie de ataque. El enlace ya apunta al dominio de la red (decidido).
- *Fase 2 — editor* (8-13 SP): solo si tras la fase 1 el negocio lo sigue pidiendo; con bloques de lista blanca, no HTML libre.

Y hay que **reescribir la decisión 8** en #12237: pasa de "solo interfaz" a "interfaz + correos (tema)"; PDF, mandato y FUR siguen fuera (el FUR es formato oficial, Anexo 46).

---

## 3. Qué cambia en lo construido y en ADO

**En lo ya implementado (F0, rama `feature/AB-12235-…`, 5/6 HUs): nada.** F0 es fundación invisible
(jerarquía en BD, `TenantScope`, resolución única de tenant, interruptores, arnés y suite anti-fuga) y
sirve igual a Concesión y a Marca Blanca. Ninguna de las definiciones del PO toca el modelo padre-hija de
profundidad 2, el fail-closed ni el "la hija no ve al padre".

**En ADO, según lo que responda el PO:**

| Si el PO confirma… | Cambio |
|---|---|
| ~~C3~~ | **Hecho (D14):** #12346 AC7, #12357 AC6/AC7, análisis §8.1 |
| C8 = descargar documentación de hijos | Revertir D2 en el análisis; HU nueva en #12257 (8-13 SP) con auditoría obligatoria; **D1 pasa a bloqueante formal** y se anota en la épica |
| ~~N1~~ | **Hecho (D15):** #12349 y #12352 en `Removed` con comentario; #12256 en 18 SP |
| M1 = "todos los OT" | HU nueva (política de OT de la cabeza de grupo) en #12256 si es independiente de la marca, o en #12237 si es exclusiva de Marca Blanca (5-7 SP) |
| M5 = correos con marca | Reescribir el criterio "Fuera de alcance → correos" de #12237; Feature o HUs de "tema de correo" (5-8 SP) y, aparte, "editor" (8-13 SP) |
| Todo lo demás | Sin cambios: ya está cubierto o ya existe |

Pendientes previos que siguen abiertos: **D1** (Ley 1581 — bloquea B en producción, y más si entra C8),
**Feature de endurecimiento del aislamiento** (no creado en ADO), título y tag `DOR` de #12237.

---

## 4. Preguntas para cerrar con el PO (en orden de impacto)

1. **"Incluyendo la documentación"**: ¿ver el estado documental del trámite, o abrir/descargar FUR, mandato, anexos y certificados? *(0 vs 8-13 SP y reabre Ley 1581)*
2. **Correos con marca**: ¿basta un tema (logo, color, encabezado/pie, remitente) sobre las plantillas de FLIT, o necesitan editar el cuerpo libremente? ¿Qué correos exactamente? *(5-8 vs 13-21 SP)*
3. **"Todos los OT activos"**: ¿es exclusivo de Marca Blanca o cualquier cabeza de grupo puede tener esa política? ¿Respeta la operabilidad del OT?
4. ~~Quién asocia los OT~~ — **resuelto (D14)**: el SuperAdmin, al crear la Concesión.
5. ~~OT como cabeza de grupo~~ — **resuelto (D15)**: fuera.
6. **Estadísticas y reportes consolidados**: ¿siguen en alcance para Concesión y Marca Blanca?
7. **D3**: ¿cualquier AdminCompany de la cabeza puede crear hijas e invitar, o hace falta un "administrador de red"?
8. **Ley 1581 (D1)**: ¿bajo qué figura ve la cabeza los datos personales de los trámites de sus hijas? Sin respuesta, el bloque B no sale a producción.

## 5. Opinión

Las dos definiciones están **mayoritariamente alineadas** con lo decidido y lo construido: de 15
enunciados, 10 están cubiertos o ya existen y 2 son matices. Los tres puntos que importan son los que
**contradicen decisiones cerradas hace uno y dos días** — documentación de los hijos (D2), correos con
marca (decisión 8) — y la **política "todos los OT"**, que es la única diferencia funcional real entre
Marca Blanca y Concesión y no estaba en ningún documento. Ninguno de los tres afecta F0; los tres
afectan estimación y dos de ellos afectan cumplimiento (Ley 1581, seguridad de correo). Conviene
cerrarlos por escrito en la épica antes de arrancar el bloque B y antes de estimar #12237.
