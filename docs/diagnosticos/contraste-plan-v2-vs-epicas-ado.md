# Contraste plan v2 ↔ épicas de ADO — Diagnóstico Vehicular

**Fecha:** 2026-10-21
**Autor:** po-agent (asistente del PO). El veredicto es una recomendación: aprueba el PO humano.
**Requerimiento (fuente de verdad):** las 10 épicas del proyecto ADO `FLIT - DIAGNOSTICO`, texto íntegro volcado el 2026-10-21 (solo lectura).
**Propuesta técnica contrastada:** `docs/diagnosticos/plan-implementacion-diagnostico-vehicular-v2.md` (2026-10-20, 1.101 líneas), incluido su «Registro de decisiones (2026-10-20)».
**Contexto:** `propuesta_implementacion_diagnostico_vehicular.md` (v1, 2026-09-24) y `epica-habilitacion-suite-y-ajustes.md` (MOD00 + 23 ajustes del 2026-09-28, **no aplicados en ADO**).
**Sistemas externos:** cerrados. Nada de este documento se publicó en ADO. La sección 7 es una propuesta.

Convención de las tablas de contraste:

| Estado | Significa |
|---|---|
| **Cubierto** | El plan lo implementa tal como lo pide la épica. Se indica § u ola |
| **Cubierto con cambio** | El plan lo implementa distinto. Se indica el respaldo: **[D-xx]** decisión del 2026-10-20 (pendiente de ratificar), **[09-28]** ajuste propuesto el 2026-09-28 (no aplicado), **[ADR prop.]** decisión técnica recomendada y abierta, **[supuesto]** sin respaldo |
| **Contradice** | El plan dice lo contrario y no hay decisión registrada que lo respalde |
| **No cubierto** | La épica lo pide y el plan no lo menciona |
| **Diferido** | El plan lo pospone explícitamente a una ola o frente posterior |

---

## 1. Resumen ejecutivo

- **Veredicto global: el plan v2 está alineado con ajustes con las 9 épicas funcionales y desalineado con la épica paraguas 12881.**
- Se contrastaron **166 criterios** de las 10 épicas: **109 Cubierto**, **39 Cubierto con cambio**, **12 Diferido**, **3 No cubierto**, **3 Contradice**.
- Las 3 contradicciones están en **12881**. Su texto es la propuesta del **2026-09-23** (anterior a la v1 local del 09-24): aplicativo satélite con bandera de compañía, login propio en el subdominio y alta de compañías desde Diagnóstico. La v1, MOD00 y el plan v2 dicen lo contrario y ninguna decisión del 10-20 lo registra.
- De los 39 cambios, **23 dependen de decisiones del 10-20 que nadie ha ratificado** (D-01, D-02, D-03, D-04, D-08, D-G). **6 dependen de ajustes del 09-28 que nunca se aplicaron en ADO**. 9 son técnicos y 1 es un supuesto.
- El cambio de negocio más grave es **D-01**: el admin de la compañía cliente (`admin_diagnostico`) puede reprocesar celdas. Cuatro textos vigentes lo prohíben (12637, 12638, 12669 y el CA 11 de 12881).
- El segundo es **D-G**: el veredicto deja de ser de IA. Pasa a reglas deterministas, el LLM solo redacta y viene apagado por defecto (`dictamen_llm=false`). Choca con 12637 y 12678 («un agente de IA evalúa») y con la etiqueta «Con Análisis IA» que 12680 usa para facturar.
- El tercero es **D-02**: el locatario queda en N/A sin fecha. Para clientes RENTING o con leasing, SIMIT locatario es justamente la consulta que importa.
- **Hay 3 criterios sin cubrir**: el clic en el semáforo que muestra los campos extraídos (12680, única línea añadida a mano en una épica), evitar la segunda consulta al RUNT en el wizard (12679) y el UI kit e iconos que Diseño compartió en 12638.
- **Backlog:** 10 épicas sin Features, sin HUs, sin enlaces y sin tag `DOR`. Tres están en `Active` (12881, 12637, 12638) sin nada que implementar debajo.
- El §12 del plan se escribió sin leer ADO. Tiene tres errores: dice «12637 sin cambios», omite 12881 y redirige mal el ajuste 12679 #1, que en realidad corresponde a 12638.

---

## 2. Estado del backlog en ADO

| Hecho | Valor | Implicación |
|---|---|---|
| Proyecto | `FLIT - DIAGNOSTICO`, no `FLIT - EVOLUTION` | El plan v2 dice «Proyecto: FLIT Suite» y MOD00 dice «FLIT - EVOLUTION». Las épicas se movieron a DIAGNOSTICO el 2026-09-28 (rev 3 de 12641). Las Features nuevas se crean en DIAGNOSTICO |
| Work items | Exactamente 10, todos `Epic` (WIQL de control: 0 adicionales) | No existe ninguna Feature ni HU. `/decompose-feature` no tiene sobre qué operar todavía |
| Jerarquía | Sin relaciones Parent/Child/Related. 12881 es paraguas solo en su texto | Sin rollup de avance ni de esfuerzo |
| Estados | `Active`: 12881, 12637, 12638 (activadas el 2026-09-29, «Implementation started»). `New`: las otras 7 | Hay épicas activas sin hijos: el estado no refleja trabajo real. Pasarlas a `New` es decisión del PO; yo no hago transiciones |
| Iteraciones | `Sprint 1`: 12637, 12638, 12640, 12641. Raíz: 12881, 12659, 12669, 12678, 12679, 12680 | 12641 se movió a Sprint 1 el 2026-10-19: señal de que MOD02 va primero. Concuerda con D1 del plan. No verifiqué si Sprint 1 es el sprint activo (sistemas cerrados) |
| Tags | Ninguna épica tiene tags | Ninguna tiene `DOR`. La regla FLIT 2 («tag DOR antes de Active») está incumplida en las 3 activas, si el PO la aplica a épicas |
| Acceptance Criteria | Ninguna usa el campo. Los criterios van en la descripción | Los ajustes de la sección 7 se redactan para pegar en la descripción, al final de cada sección |
| Effort | 12637 y 12638: 0.2. 12640 y 12679: 2.5. 12641, 12659, 12669 y 12680: 3. 12678: 4. 12881: vacío | No guarda relación con los ≈164–177 SP del plan. Es una escala propia del PO; no la reinterpreto |
| Asignación | Todas a Juan Felipe Montoya Garcia (humano) | Cumple la regla 4 |
| Cambios recientes | 12638: comentario de Diseño (2026-10-01) con biblioteca de iconos y manual UI kit. 12641: solo iteración (10-19). Las descripciones no cambiaron después del 09-28 | Los 23 ajustes del 09-28 siguen sin aplicar. El único texto añadido a mano después de crear la épica es la línea de 12680 sobre el clic en el semáforo |

**DoR.** Las épicas no tienen un conjunto DoR propio en `flit-dor-dod-validator`. Las Features que se creen (sección 7) deben cumplir los 10 criterios DoR-Feature antes de `Active`: módulo, objetivo, descripción, ≥ 3 criterios, sprint siguiente, Area, tag `DOR`, AssignedTo humano, sin placeholders y sin datos sensibles.

---

## 3. Contraste por épica

### 3.1 — 12881 · [DIAGNOSTICO] - Aplicativo Diagnóstico Vehicular (Active)

**Resumen.** Es la propuesta de implementación con fecha **2026-09-23**, pegada como descripción el 09-24 por el orchestrator-agent. Describe un aplicativo satélite de FLIT 2.0 en su propio subdominio. La puerta es una bandera en la configuración de la compañía, la sesión vive en una cookie del dominio padre, hay login propio en el subdominio y Diagnóstico puede dar de alta compañías. Trae 9 principios, el modelo de dominio, MOD01–MOD07, plan en 7 fases y 16 CA globales.

**¿Qué versión es?** No es la v1 local. Es una **versión anterior (v0)**:

| Tema | 12881 (v0, 09-23) | v1 local (09-24) | Plan v2 (10-20) |
|---|---|---|---|
| Encuadre | Aplicativo satélite de FLIT 2.0 | Producto de la Suite (hub `flitsas.online`) | Producto de la Suite, con Comparendos como plantilla |
| Puerta | Bandera de compañía en FLIT 2.0 | Suscripción del producto en el hub | Suscripción (`IProductosHabilitados`, guard propio hasta P2) |
| Login sin sesión | Formulario propio en el subdominio | Redirige al login del hub | Middleware → login PKCE del hub |
| Alta de compañías | Desde Diagnóstico, con `origen=DIAGNOSTICO` | No: viven en el hub | No |
| Adaptadores de proveedores | Dentro del aplicativo | En la plataforma de consultas | En core-consultas (por ProjectReference a core-api) |
| Cambio de compañía | Lo resuelve FLIT 2.0 | Lo resuelve el hub | No existe: una sesión es una compañía (D-03) |
| CA globales | 16 | 20 | Se mapean los 20 de la v1 (§9.2) |

**Contraste.**

| # | Criterio de 12881 | Estado | Dónde / por qué |
|---|---|---|---|
| 81-01 | CA 1 — Bandera apagada: la compañía no aparece, no hay menú y no abre el home | Cubierto con cambio [ADR prop.] | Suscripción en lugar de bandera (§2 #5, D0). Mismo efecto observable. El respaldo es la arquitectura de la Suite ya en código (DDL 119, HU12987), no una decisión del 10-20 |
| 81-02 | CA 2 — Desde Diagnóstico solo se edita la configuración del producto | Cubierto | §6.1 `PUT /configuracion` (403/422), D1 |
| 81-03 | CA 3 — Una compañía creada en Diagnóstico queda visible en FLIT 2.0 | **Contradice** | El plan no ofrece alta de compañías (§5 «Fuera del modelo»; CA 3 y 4 de la v1 en D0). Ninguna decisión del 10-20 lo registra |
| 81-04 | CA 4 — Sin sesión, el login ocurre en el subdominio | **Contradice** | §3.2 `middleware.ts` → login del hub. La entrada sin segundo login sí está cubierta (D0). Sin decisión registrada |
| 81-05 | CA 5 — Con análisis sin tipo, o sin RUNT, no se guarda | Cubierto | D1, CHECK `ck_tipo_con_analisis`, 422 |
| 81-06 | CA 6 — Un identificador inválido no consulta | Cubierto | D1, J1 |
| 81-07 | CA 7 — RUNT falla, nada más se llama | Cubierto | D2 |
| 81-08 | CA 8 — Un fallo no repite lo que ya está en verde | Cubierto | D3 |
| 81-09 | CA 9 — Servicio público deja el impuesto en N/A | Cubierto | D2 (regla) y D5 |
| 81-10 | CA 10 — Departamento no soportado: error + llenado manual del admin | Cubierto | D5 |
| 81-11 | CA 11 — El cliente no reprocesa, no llena y no cambia el dictamen | Cubierto con cambio [D-01] | `admin_diagnostico` (rol de la compañía cliente) **sí reprocesa** (§6.2) |
| 81-12 | CA 12 — Dictamen solo en Con análisis y con el vehículo en verde | Cubierto | D6 |
| 81-13 | CA 13 — Un reproceso posterior reemplaza el dictamen | Cubierto | D6 |
| 81-14 | CA 14 — El trámite guarda `origen_diagnostico_id` y la fila muestra el número | Cubierto con cambio [ADR prop.] | `Origin='diagnostico'` + `ExternalRef=vehiculo_id` (§2 #18, H1-a). Equivalente funcional |
| 81-15 | CA 15 — El PDF no revela el origen manual | Cubierto | D4 |
| 81-16 | CA 16 — La volumetría separa Simple y cada tipo | Cubierto | D8 |
| 81-17 | §4 — Un adaptador por camino de consulta dentro del aplicativo | Cubierto con cambio [ADR prop.] | Los proveedores viven en la plataforma (§2 #15, §3.1). Respaldado también por MOD00 §6 [09-28] |
| 81-18 | §4 — El cambio de compañía activa lo resuelve FLIT 2.0 | Cubierto con cambio [D-03] | Una sesión es una compañía. No hay selector |
| 81-19 | §4 — Un canal en tiempo real empuja las celdas | Cubierto con cambio [ADR prop. c] | Polling incremental cada 3 s |
| 81-20 | §8 — El Admin FLIT crea diagnósticos y carga vehículos | **Contradice** | §6.2: el backoffice no crea **[supuesto]**. Ninguna decisión lo respalda |
| 81-21 | §10 fase 6 — Ingesta desde FLITO | Diferido [D-09] | Ola D9 |
| 81-22 | §6 MOD06 — *Sanear* redirige al saneamiento de FLIT 2.0 | Cubierto con cambio [D-08] | Salida informativa S2 |

**Extras del plan frente a 12881:** puerta mínima en paralelo (D-P), tres perfiles (D-01), configuración en dos niveles (D-04), celdas RUNT persona, cola durable, vista padre → hijas, retención, D9. El detalle está en la sección 5.

**¿Paraguas o reemplazo?**

| Opción | Pros | Contras |
|---|---|---|
| A. Mantener 12881 como paraguas: reemplazar su descripción por un resumen del plan v2 ratificado y enlazar las otras 9 + MOD00 | Conserva el ID que ya circula. Da un punto de entrada único | Hay que reescribirla entera: hoy contradice a MOD00 y al plan en 3 CA |
| B. Parent-Child Epic → Epic | Rollup en consultas | ADO permite el vínculo entre dos Epic, pero el backlog de Épicas no los anida y avisa de jerarquía del mismo nivel. Hay que probarlo en el proceso del proyecto antes |
| C. Retirar 12881 y dejar la visión en 12637 | Quita duplicidad | 12637 tiene su propio encuadre (módulo dentro de FLIT 2.0) y habría que reescribirla igual. Retirar es decisión del PO |

**Recomendación: A, con vínculos `Related` (no Parent-Child)** desde 12881 hacia las 9 épicas y MOD00. La descripción se reemplaza por el texto de la sección 7.1: resumen v2 + registro de decisiones ratificadas + enlace al plan en el repo. No conviene pegar las 1.101 líneas del plan. Si el PO quiere rollup, se prueba B en un ítem de ensayo antes de enlazar las 10.

**Ajustes en ADO:**
- [contraste] Reemplazar la descripción completa (sección 7.1). Retirar bandera, login propio, alta de compañías y adaptadores propios.
- [10-20] Reflejar D-01, D-03, D-08, D-09 y la puerta mínima D-P.
- [contraste] Renumerar los CA globales contra el texto nuevo. Hoy el plan cita «CA 6, 7, 18, 19» con la numeración de la v1, y esa numeración no existe en ADO.

**Veredicto 12881: Desalineada (requiere decisión del PO).** Su contenido es la v0 del 09-23 y tiene 3 contradicciones sin decisión que las respalde.

---

### 3.2 — 12637 · Visión General y Arquitectura del Módulo (Active)

**Resumen.** Introduce Diagnósticos como módulo nuevo **en FLIT 2.0**. Una empresa consulta RUNT, SIMIT, avalúo e impuesto departamental sobre su flota y obtiene un reporte (Simple) o un dictamen de IA (Con análisis). El RUNT es el paso 1 obligatorio y de él salen los documentos del propietario y del locatario. Define 7 submódulos con flujo estricto, vocabulario, estados del lote, recálculo de IA, ZIP, la base de conocimiento como ADR y dos roles: Admin FLIT y Usuario Cliente.

| # | Criterio de 12637 | Estado | Dónde / por qué |
|---|---|---|---|
| 37-01 | Módulo nuevo «en FLIT 2.0» | Cubierto con cambio [ADR prop.] | Producto propio con subdominio (§3). Lo respaldan v1 §1 y 12881; no hay decisión del 10-20 |
| 37-02 | Fuentes RUNT, SIMIT y avalúo | Cubierto | D2, D3 |
| 37-03 | Fuente impuesto departamental | Diferido [ADR prop. f] | D5 manual primero (F3). El automático espera a C3. El ADR f sigue abierto |
| 37-04 | Vehículo por placa o VIN como unidad central | Cubierto | D1 |
| 37-05 | El documento del propietario sale del RUNT, paso inicial | Cubierto con cambio [D-02] | RUNT por placa vía Intempo + hidratar `propietariosActuales` (C-PER) |
| 37-06 | El documento del locatario sale del RUNT | Diferido [D-02] | Ningún proveedor lo trae. N/A hasta que exista el endpoint |
| 37-07 | Modo Simple sin juicio | Cubierto | D4 libera Simple |
| 37-08 | Con análisis: un agente de IA evalúa y emite el dictamen | Cubierto con cambio [D-G] | El veredicto es determinista. El LLM solo redacta, es opcional y viene apagado por defecto |
| 37-09 | 7 submódulos MOD01–MOD07 | Cubierto con cambio [D-P, D-09] | Suma MOD00 (habilitación) y la ola D9 (integración FLITO) |
| 37-10 | Flujo estricto: ningún módulo opera sin el anterior (salvo MOD05) | Cubierto con cambio [09-28] | MOD04 desde la cola, derivación por vehículo, evidencia parcial (ajustes 12679 #1 y 12680 #2, no aplicados) |
| 37-11 | La cabecera aplica a todo el lote | Cubierto | D1 |
| 37-12 | Semaforización Pendiente / OK / Error / N/A | Cubierto | D2. `EN_CURSO` es interno y no cambia lo que ve el usuario |
| 37-13 | Dictamen Viable o Bloqueado, solo en Con análisis | Cubierto | D6 |
| 37-14 | Express sin crear lote | Cubierto con cambio [09-28] | Se persiste como diagnóstico de 1 vehículo con nombre autogenerado (ajustes 12641 #3 y 12640 #1) |
| 37-15 | Apalancador: inyecta al wizard nativo | Cubierto | D7 + T1 |
| 37-16 | Estados En Proceso / Completado / Incompleto | Cubierto con cambio [supuesto] | Suma `CREADO` y la vuelta `INCOMPLETO`/`COMPLETADO` → `EN_PROCESO` (§3.5: «[Supuesto: el PO lo acepta]») |
| 37-17 | Recálculo automático tras un reproceso exitoso | Cubierto | D6 |
| 37-18 | ZIP de PDFs del lote | Cubierto | D4 |
| 37-19 | Base de conocimiento como ADR del LT antes de MOD05 | Cubierto | ADR g, antes de D6 |
| 37-20 | Admin FLIT: reprocesar, llenar, override, facturación | Cubierto con cambio [D-01] | Se reparte entre `admin_diagnostico` (reprocesa) y `backoffice_diagnostico` (el resto) |
| 37-21 | El Usuario Cliente no actúa sobre consultas en error ni sobre el dictamen | Cubierto con cambio [D-01] | El admin de la compañía cliente sí reprocesa. **Cambia una regla de negocio explícita** |
| 37-22 | El Usuario Cliente crea, monitorea y descarga | Cubierto | D1–D4 |

**Extras:** MOD00, tercer perfil, configuración en dos niveles, retención. Todos requieren aprobación del PO (sección 5).

**Ajustes en ADO:**
- [09-28] La épica de ajustes dejó 12637 «sin cambios». **El contraste no lo sostiene.**
- [contraste] Encuadre: módulo → producto de la Suite en su subdominio.
- [10-20] Roles en tres perfiles (D-01).
- [10-20] Con análisis: veredicto por reglas y redacción opcional por LLM (D-G).
- [10-20] Locatario no disponible mientras ningún proveedor lo entregue (D-02).
- [09-28] Flujo por vehículo y evidencia parcial.
- [contraste] Estado `CREADO` y vuelta a `EN_PROCESO`, si el PO la acepta.

**Veredicto 12637: Alineada con ajustes.** Depende de ratificar D-01, D-02 y D-G. El plan §12 tiene que dejar de decir «sin cambios».

---

### 3.3 — 12638 · Diseño y Experiencia Visual (Active)

**Resumen.** Diseño de todas las pantallas, con el sistema de diseño de FLIT y sus colores (#557eff, #ff4e00, #8cc63f), en dos perfiles de vista. Pantallas: alta del diagnóstico, home con express, plantilla, resumen de validación, grilla de semáforos, vistas Cliente y Admin, dictamen, override, derivación, listado, descargas y dashboard admin. Trae adjunto el manual UI kit y un comentario de Diseño (10-01) con la biblioteca y el repositorio de iconos y el loader.

| # | Criterio de 12638 | Estado | Dónde / por qué |
|---|---|---|---|
| 38-01 | Sistema de diseño y colores corporativos | Cubierto | D0, front clonado con `@flit/ui` y `@flit/brand` |
| 38-02 | Manual UI kit, biblioteca de iconos, repositorio de iconos y loader (adjunto + comentario del 10-01) | **No cubierto** | El plan no los menciona. El front se clona de Comparendos |
| 38-03 | Dos perfiles de vista (Cliente y Admin FLIT) | Cubierto con cambio [D-01] | Tres perfiles: usuario, admin de la compañía, backoffice |
| 38-04 | MOD01 en tres secciones y nombre obligatorio con indicador | Cubierto | D1 |
| 38-05 | Alcance en tres grupos (vehículo; SIMIT propietario; SIMIT locatario) | Cubierto con cambio [D-02; D-15 abierta; D-04] | Los grupos Propietario y Locatario suman RUNT persona. Solo se ofrecen las fuentes habilitadas |
| 38-06 | RUNT bloqueado y activo, con tooltip | Cubierto | D1 |
| 38-07 | Radios + combobox animado con los 6 tipos de trámite | Diferido [D-G] | Con análisis arranca con la familia TRASPASO. Prenda va en la v2 del catálogo y cancelación espera a T3 |
| 38-08 | Input express y modal compacto | Cubierto | D1 |
| 38-09 | Descargar Plantilla Base, con los formatos | Cubierto | D1 |
| 38-10 | Resumen de validación con conteo y dos salidas | Cubierto | D1 |
| 38-11 | Grilla matriz con ícono por estado | Cubierto | D2. Hasta 8 columnas con RUNT persona |
| 38-12 | Indicador global con tres estados | Cubierto | D2 |
| 38-13 | Vista Cliente sin botones sobre celdas en error | Cubierto con cambio [D-01] | El admin de la compañía ve *Reprocesar* |
| 38-14 | Vista Admin con Reprocesar y Llenar datos (modal) | Cubierto con cambio [D-01] | *Llenar* es solo del backoffice. D3 |
| 38-15 | Panel de dictamen verde/rojo y tarjetas de alertamiento | Cubierto | D6 |
| 38-16 | Override con confirmación explícita | Cubierto | D6, backoffice |
| 38-17 | *Crear trámite* por fila «una vez **el diagnóstico** está completo o en Simple» | Cubierto con cambio [09-28] | Por vehículo. El ajuste del 09-28 lo puso en 12679, pero la frase que hay que cambiar está aquí |
| 38-18 | Modal de tipo en Simple; confirmación directa en Viable | Cubierto | D7 |
| 38-19 | *Sanear* redirige a saneamiento con la deuda inyectada | Cubierto con cambio [D-08] | Panel informativo + exportación |
| 38-20 | Etiqueta «Derivado al Trámite #XXXXX» | Cubierto | D7 |
| 38-21 | Listado con columnas y filtros | Cubierto | D4 |
| 38-22 | PDF individual, ZIP y Excel | Cubierto | D4 |
| 38-23 | Dashboard admin con filtros por cliente y volumetría | Cubierto | D8, backoffice |

**Extras:** pantalla 10 con pestañas Contrato y Operación (D-04), selección de empresa del backoffice, panel de *Sanear* informativo, estado N/A del locatario con su motivo, vista padre → hijas (D8).

**Ajustes en ADO:**
- [09-28] Pantalla 10: configuración del producto (sin usuarios, roles ni suscripción).
- [10-20] La pantalla 10 tiene dos pestañas. Se suman las pantallas de backoffice, *Sanear* informativo y N/A del locatario. Los grupos de alcance suman RUNT persona.
- [contraste] Cambiar «una vez el diagnóstico está completo» por «una vez **ese vehículo** cierra sus consultas aplicables».
- [contraste] Dejar como criterio el uso del manual UI kit y de la biblioteca de iconos del comentario del 10-01.

**Veredicto 12638: Alineada con ajustes.**

---

### 3.4 — 12640 · [1] Parametrización y Reglas (New, Sprint 1)

**Resumen.** Es la cabecera del diagnóstico. Nombre obligatorio. Alcance a la carta en tres grupos, con RUNT bloqueado. Modo Simple o Con análisis, con un combobox dinámico de tipos activos (6 valores en esta etapa). La configuración es uniforme e inmutable después de `CREADO`. La justificación es el costo por consulta: «RUNT vía Intempo, SIMIT, Avalúo, IA».

| # | Criterio de 12640 | Estado | Dónde / por qué |
|---|---|---|---|
| 40-01 | Nombre del diagnóstico obligatorio | Cubierto con cambio [09-28] | En express se autogenera y queda editable |
| 40-02 | RUNT bloqueado y obligatorio | Cubierto | D1, CA 6 de la v1 |
| 40-03 | SIMIT vehículo opcional | Cubierto | D3 |
| 40-04 | Avalúo opcional | Cubierto | D3 |
| 40-05 | Impuesto departamental opcional | Diferido [ADR prop. f] | D5 |
| 40-06 | SIMIT propietario con el documento del RUNT | Cubierto con cambio [D-02] | El documento llega por Intempo (C-PER) |
| 40-07 | SIMIT locatario con el documento del RUNT | Diferido [D-02] | N/A sin proveedor |
| 40-08 | Radios Simple / Con análisis, excluyentes | Cubierto | D1 |
| 40-09 | Combobox dinámico desde los tipos activos | Cubierto | `GET /tipos-tramite` (publicados + `wizardEnabled`) |
| 40-10 | Los 6 tipos válidos en esta etapa | Diferido [D-G] | Solo la familia TRASPASO al inicio |
| 40-11 | Combobox obligatorio en Con análisis | Cubierto | D1 |
| 40-12 | Configuración uniforme para todo el lote | Cubierto | Principio 1 |
| 40-13 | Trámites distintos → diagnósticos separados | Cubierto | Principio 1 |
| 40-14 | Guardar crea la cabecera en `CREADO` | Cubierto | §3.5, DDL |
| 40-15 | La cabecera es inmutable después de `CREADO` | Cubierto | §3.5 |
| 40-16 | Costo: «RUNT vía Intempo» | Cubierto | D-02 coincide con la épica. **Es el único respaldo textual de D-02 en ADO** |

**Extras:** `fuentes_habilitadas` y `fuentes_por_defecto` (D-04). Limitan qué ofrece el alcance «a la carta», así que son alcance nuevo. RUNT persona como checkbox (D-15 abierta). Si se adopta D-18, RUNT propietario quedaría obligatorio en Con análisis y eso contradiría «a la carta».

**Ajustes en ADO:**
- [09-28] Nombre autogenerado en express.
- [09-28] Grupos Propietario y Locatario dependientes del RUNT.
- [10-20] El alcance solo ofrece las fuentes contratadas y premarca las de por defecto (D-04).
- [10-20] En esta etapa, Con análisis ofrece la familia TRASPASO (D-G).
- [contraste] Las fuentes sin proveedor (locatario) o sin automatización (impuesto hasta C3) se muestran como no disponibles o no se ofrecen. El PO decide cuál de las dos.

**Veredicto 12640: Alineada con ajustes.**

---

### 3.5 — 12641 · [2] Captura e Ingesta (New, Sprint 1)

**Resumen.** Tres vías de ingreso: express, carga masiva (plantilla `.xlsx`, acepta `.xlsx`, `.xls` y `.csv`, con pre-validación, resumen, *Corregir ahora* y *Continuar solo con los válidos*) y FLITO (selección en el inventario + *Ejecutar Diagnóstico*). Detecta solo placa de carro, moto o remolque, o VIN. Entrega una lista limpia a la cola, con RUNT primero.

| # | Criterio de 12641 | Estado | Dónde / por qué |
|---|---|---|---|
| 41-01 | Input express en la pantalla principal | Cubierto | D1 |
| 41-02 | Modal compacto sin crear un lote formal | Cubierto con cambio [09-28] | Diagnóstico de 1 vehículo |
| 41-03 | Inicia de inmediato y lleva al dashboard | Cubierto | D1 (`POST /express`). Las consultas reales llegan en D2 |
| 41-04 | Plantilla `.xlsx` con `Identificador_Vehiculo` | Cubierto | D1 |
| 41-05 | Acepta `.xlsx`, `.xls` y `.csv` | Cubierto | D1 |
| 41-06 | Pre-validación antes de cualquier consulta | Cubierto | D1 |
| 41-07 | Resumen con *Corregir ahora* (en pantalla) y *Continuar solo con los válidos* | Cubierto | D1, `carga/validar-filas` |
| 41-08 | Sin ningún válido: error y no continúa | Cubierto | D1 |
| 41-09 | Vía 3: FLITO desde el inventario «de la plataforma» | Diferido [D-09] | Ola D9 por API de integración. El plan verificó que FLITO es **externo**: «integración interna» no es exacto |
| 41-10 | Detección automática placa / VIN | Cubierto | J1 |
| 41-11 | Formatos de placa: carro, moto, remolque | Cubierto | J1. D-05 sigue abierta y la recomendación (unión con `fieldRules.ts`) **ampliaría** lo que pide la épica |
| 41-12 | VIN de 17 caracteres sin I, O ni Q | Cubierto | J1 |
| 41-13 | Inválido → error de pre-validación | Cubierto | D1 |
| 41-14 | Lista limpia asociada al `diagnostico_id` y en cola asíncrona | Cubierto | D1 / D2 |
| 41-15 | RUNT es la primera consulta | Cubierto | D2 |

**Extras:** límite de vehículos por lote (500, D-04), estimado de consultas en el resumen (§10, «propuesta, PO», **sin decisión**), deduplicación y normalización [09-28].

**Ajustes en ADO:**
- [09-28] Normalización, duplicados y express como diagnóstico de 1 vehículo (3 criterios).
- [10-20] La vía 3 sale de esta épica y pasa a la Feature de integración FLITO (D-09).
- [contraste] Corregir «integración interna»: FLITO es un sistema externo.
- [10-20] Límite por lote.
- [contraste] El estimado de consultas solo entra si el PO lo aprueba.

**Veredicto 12641: Alineada con ajustes.** Depende de ratificar D-09: hoy la épica promete tres vías y el producto saldría con dos.

---

### 3.6 — 12659 · [3] Motor de Consultas y Orquestación (New, raíz)

**Resumen.** Es el motor asíncrono. Primero consulta el RUNT (placa o VIN), que debe entregar la placa definitiva y los documentos del propietario y del locatario. Si el RUNT falla, el vehículo queda en error y no se consulta nada más. Después lanza en paralelo las fuentes opcionales, con exención del impuesto si el servicio es público y una tabla maestra de secretarías. Lleva semáforo por consulta, eventos en tiempo real, log de auditoría por intento (con un enum que incluye `FASECOLDA`) y trigger de IA por vehículo.

| # | Criterio de 12659 | Estado | Dónde / por qué |
|---|---|---|---|
| 59-01 | Motor asíncrono en segundo plano | Cubierto | D2, cola durable (ADR a, abierta) |
| 59-02 | RUNT paso 1 con el identificador (placa o VIN) | Cubierto con cambio [D-02] | La placa va sin documento y responde Intempo. Kyverum y Verifik se saltan porque exigen documento |
| 59-03 | RUNT entrega la placa definitiva si se consultó por VIN | Cubierto | D2 |
| 59-04 | RUNT entrega el documento del propietario | Cubierto con cambio [D-02] | C-PER hidrata `propietariosActuales`, sin el nombre |
| 59-05 | RUNT entrega el documento del locatario | Diferido [D-02] | Ningún proveedor lo trae |
| 59-06 | RUNT falla → vehículo en error, nada más se dispara | Cubierto | D2 |
| 59-07 | En paralelo: SIMIT vehículo, SIMIT propietario, avalúo | Cubierto | D3 |
| 59-08 | En paralelo: SIMIT locatario | Diferido [D-02] | D5 en N/A (`SIN_PROVEEDOR_LOCATARIO`) |
| 59-09 | Impuesto con placa + departamento **extraído del RUNT** | Cubierto con cambio [técnico] | El RUNT no trae el departamento. Se deriva del organismo contra la réplica de `transit_offices` (§2.1). Emparejar por nombre es frágil; C-PER suma `transit_office_code` |
| 59-10 | Departamento con integración automatizada → se lanza la consulta | Diferido [ADR prop. f] | F3: manual primero. Hasta C3, **todo** vehículo con impuesto queda en error + manual |
| 59-11 | Servicio público → impuesto N/A | Cubierto | D2 / D5 |
| 59-12 | Departamento no soportado → error + llenado manual del Admin FLIT | Cubierto | D5, backoffice |
| 59-13 | Estado por consulta: Pendiente, Completado (API o manual), Error | Cubierto | D2. `EN_CURSO` es interno |
| 59-14 | Un error no afecta ni repite lo completado | Cubierto | D3 |
| 59-15 | Emite eventos en tiempo real para MOD04 | Cubierto con cambio [ADR prop. c] | Entre módulos no hay bus (§6.4). El tablero hace polling incremental |
| 59-16 | Log: diagnóstico, placa/VIN, fuente (enum con `FASECOLDA`), inicio/fin en ms, estado, mensaje, transacción | Cubierto con cambio [09-28 + D-02/D-15] | `AVALUO` reemplaza a `FASECOLDA`, se suma `servicio` y se suman `RUNT_PROPIETARIO`/`RUNT_LOCATARIO`. La placa o el VIN se obtienen por `vehiculo_id`; el documento consultado no se guarda en claro |
| 59-17 | Trigger de IA por vehículo, solo en Con análisis | Cubierto | D6. Una celda en N/A cuenta como cerrada |

**Extras:** cola durable con lease y `EN_CURSO`, idempotencia `(vehiculo_id, fuente)` [09-28], celdas RUNT persona (D-15 abierta), consumo medido por la plataforma [09-28], concurrencia de 5 vehículos por tenant, fuentes simuladas deterministas. Son necesidades técnicas, salvo las celdas RUNT persona: esas son una fuente nueva, visible y con costo, y requieren aprobación del PO.

**Ajustes en ADO:**
- [09-28] 6 criterios (enum `AVALUO`, `servicio`, puerta de consultas, consumo, idempotencia, auditoría del manual).
- [10-20] Enum + `RUNT_PROPIETARIO` / `RUNT_LOCATARIO`.
- [10-20] RUNT por placa sin documento vía Intempo.
- [10-20] Locatario en N/A.
- [ADR prop.] Cola durable y lease.
- [contraste] El departamento se deriva del organismo de tránsito.
- [contraste] El impuesto empieza manual, si el PO acepta la carga operativa (o se oculta la fuente hasta C3).

**Veredicto 12659: Alineada con ajustes.**

---

### 3.7 — 12669 · [4] Dashboard y Monitoreo (New, raíz)

**Resumen.** Es la sala de control. Grilla de círculos vehículo × consulta que se actualiza en tiempo real, estado global del lote en tres valores y panel que no se cierra solo. El Usuario Cliente solo monitorea y no distingue un dato manual de uno de API. El Admin FLIT reprocesa o llena datos en las celdas rojas, y el manual queda auditado. Con todo en verde, notifica a la IA.

| # | Criterio de 12669 | Estado | Dónde / por qué |
|---|---|---|---|
| 69-01 | Matriz vehículos × consultas seleccionadas | Cubierto | D2 |
| 69-02 | Colores: gris en proceso, verde, rojo, gris claro N/A | Cubierto | D2 |
| 69-03 | Tiempo real sin recargar la página | Cubierto con cambio [ADR prop. c] | Polling cada 3 s, solo con celdas en `PENDIENTE` o `EN_CURSO`. SSE opcional en D8 |
| 69-04 | Estado global En Proceso / Completado / Incompleto | Cubierto | D2 |
| 69-05 | El panel no se cierra solo | Cubierto | D2, `cerrar-panel` |
| 69-06 | El Usuario Cliente no tiene acciones sobre celdas rojas | Cubierto con cambio [D-01] | El admin de la compañía reprocesa |
| 69-07 | Un dato manual se ve igual que uno de API para el cliente | Cubierto | D3 / D4. Defensa en DTO y en UI |
| 69-08 | Admin FLIT: *Reprocesar* solo esa fuente y ese vehículo | Cubierto con cambio [D-01] | También lo hace `admin_diagnostico`, con consumo contra su empresa |
| 69-09 | Admin FLIT: *Llenar datos* en un modal; la celda pasa a verde al guardar | Cubierto | D3, solo backoffice |
| 69-10 | El manual se registra con `MANUAL` + `usuario_id` y nunca se expone al cliente | Cubierto | D3 |
| 69-11 | Todo en verde (API o manual) → se notifica a la IA | Cubierto | D6 |

**Extras:** test de fuga entre empresas [09-28/ADR b], vuelta a `EN_PROCESO` [supuesto], estado global calculado [09-28].

**Ajustes en ADO:**
- [09-28] La restricción va también en la API. El estado global se calcula.
- [10-20] Tres perfiles: reprocesan el admin de la compañía y el backoffice; llena solo el backoffice (D-01).
- [ADR prop.] «Tiempo real» se cumple con polling incremental.
- [contraste] Si el PO acepta la vuelta a `EN_PROCESO` después de un reproceso, se agrega.

**Veredicto 12669: Alineada con ajustes.** Depende de ratificar D-01.

---

### 3.8 — 12678 · [5] Motor de IA y Dictamen (New, raíz)

**Resumen.** Es el «cerebro». Solo corre en Con análisis y por vehículo, cuando todas sus celdas están en verde. El LLM recibe el tipo de trámite, el JSON consolidado y la base de conocimiento (ADR previo del LT), y emite Viable o Bloqueado con alertamientos en lenguaje humano, «simulando el juicio de un experto legal de tránsito». Recalcula después de un reproceso. El Admin FLIT puede hacer override con confirmación; el override queda invisible para el cliente y auditado en base de datos.

| # | Criterio de 12678 | Estado | Dónde / por qué |
|---|---|---|---|
| 78-01 | Solo en Con análisis | Cubierto | D6 |
| 78-02 | Por vehículo, sin esperar al lote | Cubierto | D6 |
| 78-03 | Un vehículo Incompleto no dispara el dictamen | Cubierto | D6 |
| 78-04 | El LLM recibe tipo de trámite + JSON consolidado + base de conocimiento | Cubierto con cambio [D-G] | El veredicto lo da un evaluador determinista sobre el consolidado. El LLM, si está encendido, recibe solo hallazgos estructurados sin PII |
| 78-05 | Base de conocimiento como ADR del LT antes de construir | Cubierto | ADR g |
| 78-06 | Simula el juicio de un experto legal de tránsito | Cubierto con cambio [D-G] | Catálogo de reglas versionado, sembrado desde `BlockingCriteria`, `ConsultationBlockingPolicy`, `SoatGate` y limitaciones RUNT. Las reglas las valida el PO |
| 78-07 | Veredicto Viable / Bloqueado (Requiere Saneamiento) | Cubierto | D6 |
| 78-08 | Alertamientos en lenguaje humano (montos SIMIT, embargo RUNT, impuesto adeudado) | Cubierto | D6. Los montos dependen de **C1** (abierto) y el impuesto de D5 |
| 78-09 | Aplica a los tipos de trámite de 12640 | Diferido [D-G] | v1 del catálogo: familia TRASPASO. Prenda en v2. Cancelación con T3 |
| 78-10 | Recálculo automático tras un reproceso exitoso | Cubierto | D6 |
| 78-11 | El dictamen anterior queda reemplazado | Cubierto | Versión archivada, `ux_dictamen_vigente` |
| 78-12 | Override del Admin FLIT: veredicto y alertamientos | Cubierto | D6, backoffice |
| 78-13 | El override exige confirmación explícita | Cubierto | `confirmado=true` |
| 78-14 | El cliente no ve ningún indicador de intervención | Cubierto | DTO sin campos de override. `redactado_por` tampoco debe exponerse |
| 78-15 | En base de datos: `analisis_override`, `usuario_id`, timestamp | Cubierto | DDL `dictamenes` |

**Extras:** bandera `dictamen_llm` por compañía (contractual, `false` por defecto), plantilla de respaldo, versión del catálogo guardada en cada dictamen. La bandera es alcance nuevo con efecto comercial: un «Con análisis» sin LLM, ¿se sigue llamando y cobrando «IA»?

**Ajustes en ADO:**
- [09-28] Sin ajustes («cubierta 1:1»). **El contraste no lo sostiene** después de D-G.
- [10-20] El veredicto lo dan reglas deterministas versionadas. El LLM es opcional y solo redacta, con plantilla de respaldo. Alcance de la v1: familia TRASPASO. El override es solo del backoffice. Al LLM no le llegan documentos ni nombres.

**Veredicto 12678: Alineada con ajustes, condicionada a que el PO ratifique D-G.** Si no la ratifica, pasa a Desalineada: D-G cambia la naturaleza de la épica (de «motor de IA» a «motor de reglas con redacción opcional»).

---

### 3.9 — 12679 · [6] Derivación y Accionabilidad (New, raíz)

**Resumen.** Convierte el diagnóstico en acción. *Crear Trámite* aparece por fila cuando el vehículo tiene su diagnóstico completo: en Con análisis + Viable va directo al wizard, y en Simple pide el tipo con un modal. Se redirige al wizard nativo inyectando el JSON consolidado para **evitar una segunda consulta al RUNT**. *Sanear* aparece con Bloqueado y lleva la deuda exacta al módulo de saneamiento. Hay trazabilidad con `origen_diagnostico_id` y la etiqueta «Derivado al Trámite #XXXXX».

| # | Criterio de 12679 | Estado | Dónde / por qué |
|---|---|---|---|
| 79-01 | *Crear Trámite* cuando **un vehículo** tiene su diagnóstico completo | Cubierto | D7. **El texto vigente ya dice «un vehículo»**: el ajuste 12679 #1 del 09-28 partió de una lectura equivocada. La frase a corregir está en 12638 |
| 79-02 | Escenarios: Con análisis + Viable, y Simple siempre | Cubierto | D7 |
| 79-03 | En Simple, modal para elegir el tipo | Cubierto | D7 |
| 79-04 | En Viable, confirmación y redirección directa | Cubierto | D7 |
| 79-05 | Inyecta el JSON consolidado para precargar placa, VIN, datos del vehículo y del propietario | Cubierto con cambio [D-02 + ADR prop. h] | Token opaco de un solo uso (H1) y canje en el wizard (T1, equipo de Trámites). Del propietario viaja el documento, no el nombre |
| 79-06 | **Evita una segunda consulta al RUNT** | **No cubierto** | T1 precarga y crea el borrador, pero el plan no dice que el wizard deje de consultar el RUNT cuando llega `ctx` |
| 79-07 | El usuario completa el resto y guarda en el wizard | Cubierto | T1 |
| 79-08 | Con Bloqueado aparece *Sanear* | Cubierto | D7 |
| 79-09 | *Sanear* redirige al saneamiento con la deuda exacta (SIMIT o impuesto) | Cubierto con cambio [D-08] | No existe módulo de saneamiento. Se muestra un panel con la deuda por fuente y su monto, y se puede exportar. Depende de C1 |
| 79-10 | `origen_diagnostico_id` en base de datos | Cubierto con cambio [ADR prop.] | `Origin='diagnostico'` + `ExternalRef=vehiculo_id`, con índice único `(tenant_id, external_ref)` |
| 79-11 | Etiqueta «Derivado al Trámite #XXXXX» | Cubierto | H1-a |
| 79-12 | Base para reportes gerenciales de conversión | Cubierto | D8 + T2 |

**Extras:** *Sanear* también en **Simple con deuda** (§7.4). La épica lo ata a Bloqueado y D-08 no lo menciona: es alcance nuevo **sin decisión**. El índice único `(tenant_id, external_ref)` impide derivar dos trámites del mismo vehículo, cosa que la épica no define: hay que preguntarlo al PO.

**Ajustes en ADO:**
- [09-28] #1: reformular como aclaración y moverlo a 12638.
- [09-28] #2 a #5: Crear Trámite oculto en Bloqueado; contexto de un solo uso; monto + fuente juntos; contrato del wizard antes de construir.
- [10-20] *Sanear* informativo S2 hasta que exista saneamiento (D-08).
- [contraste] El wizard no repite la consulta al RUNT si recibe el contexto, o el PO acepta que la repita.
- [contraste] Un vehículo deriva un solo trámite (o no). Lo decide el PO.

**Veredicto 12679: Alineada con ajustes.** Depende de D-08 y de cerrar el criterio sin cubrir (79-06).

---

### 3.10 — 12680 · [7] Reportes y Trazabilidad (New, raíz)

**Resumen.** Cierre y monetización. El cliente ve un listado con filtros. El Admin FLIT ve un dashboard con volumetría por cliente, por tipo de análisis («Con Análisis IA-Traspaso»…) y por fallos de proveedor, que es la base de la tarifa. Entregables: PDF dinámico por vehículo, Excel del lote y ZIP. En base de datos quedan la configuración original, el origen API o manual y el usuario de cada acción. **Línea añadida a mano:** «para la administración es necesario que al darle clic al semáforo de la consulta este muestre los campos de la consulta que se extrajieron».

| # | Criterio de 12680 | Estado | Dónde / por qué |
|---|---|---|---|
| 80-01 | Listado del cliente con sus columnas | Cubierto | D4 |
| 80-02 | Filtros: fechas, tipo, estado, nombre | Cubierto | D4 |
| 80-03 | El cliente ve «todos sus lotes», con la columna de usuario creador | Cubierto con cambio [D-04] | `visibilidad_usuario` vale `PROPIOS` por defecto: cada usuario ve solo los suyos hasta que su admin elija `EMPRESA` |
| 80-04 | Dashboard Admin FLIT con filtros (cliente, fechas, tipo, trámite) | Cubierto | D8 |
| 80-05 | Volumetría: vehículos por cliente, por tipo de análisis, reprocesos y fallos por proveedor | Cubierto | D8, `intentos_consulta.servicio` |
| 80-06 | La agrupación por tipo de análisis define la tarifa | Cubierto | D8. Ver la pregunta sobre «IA» y `dictamen_llm` |
| 80-07 | **Clic en el semáforo muestra los campos extraídos (administración)** | **No cubierto** | No hay endpoint ni pantalla de detalle de celda. `GET /consultas/{cid}/intentos` es auditoría del backoffice, no los campos extraídos |
| 80-08 | PDF por vehículo, dinámico, con encabezado obligatorio | Cubierto | D4 |
| 80-09 | Excel del lote, columnas según la configuración | Cubierto | D4 |
| 80-10 | ZIP de PDFs | Cubierto | D4 |
| 80-11 | Se guarda la configuración JSON original | Cubierto | `diagnosticos.configuracion` |
| 80-12 | Origen API o MANUAL, oculto en el PDF del cliente | Cubierto | D4 |
| 80-13 | `usuario_id` en creación, llenado manual y override | Cubierto | DDL |

**Extras:** conversión diagnóstico → trámite [09-28, T2], evidencia parcial e invalidación por huella [09-28], **retención y purga** (`retencion_dias` 180 / máximo 365), **vista padre → hijas** (D-03, D8, ≈5 SP). La purga choca con «trazabilidad para auditar y facturar»: hay que definir qué se purga (solo documentos de persona o el diagnóstico entero) antes de D4.

**Ajustes en ADO:**
- [09-28] 4 criterios: conversión, evidencia parcial, ciclo de vida de los archivos, aislamiento por compañía.
- [10-20] La volumetría es del backoffice. Vista de la empresa padre en solo lectura (D-03). La conversión depende de T2.
- [contraste] Precisar quién es «la administración» del clic en el semáforo y qué campos ve.
- [contraste] Retención: qué se purga y cuándo.

**Veredicto 12680: Alineada con ajustes.** Tiene 1 criterio sin cubrir.

---

## 4. Matriz global

### 4.1 Conteo por épica y estado

| Épica | Cubierto | Cubierto con cambio | Diferido | No cubierto | Contradice | Total | Veredicto |
|---|---|---|---|---|---|---|---|
| 12881 Paraguas | 11 | 7 | 1 | 0 | **3** | 22 | **Desalineada** |
| 12637 Visión | 11 | 9 | 2 | 0 | 0 | 22 | Alineada con ajustes |
| 12638 Diseño | 15 | 6 | 1 | **1** | 0 | 23 | Alineada con ajustes |
| 12640 MOD01 | 11 | 2 | 3 | 0 | 0 | 16 | Alineada con ajustes |
| 12641 MOD02 | 13 | 1 | 1 | 0 | 0 | 15 | Alineada con ajustes |
| 12659 MOD03 | 9 | 5 | 3 | 0 | 0 | 17 | Alineada con ajustes |
| 12669 MOD04 | 8 | 3 | 0 | 0 | 0 | 11 | Alineada con ajustes |
| 12678 MOD05 | 12 | 2 | 1 | 0 | 0 | 15 | Alineada con ajustes (condicionada a D-G) |
| 12679 MOD06 | 8 | 3 | 0 | **1** | 0 | 12 | Alineada con ajustes |
| 12680 MOD07 | 11 | 1 | 0 | **1** | 0 | 13 | Alineada con ajustes |
| **Total** | **109** | **39** | **12** | **3** | **3** | **166** | |

### 4.2 Respaldo de los 39 cambios

| Respaldo | Nº | Criterios |
|---|---|---|
| D-01 (tres perfiles) | 8 | 81-11, 37-20, 37-21, 38-03, 38-13, 38-14, 69-06, 69-08 |
| D-02 (RUNT por placa vía Intempo) | 6 | 37-05, 38-05, 40-06, 59-02, 59-04, 79-05 |
| D-G (dictamen determinista) | 3 | 37-08, 78-04, 78-06 |
| D-08 (*Sanear* informativo) | 3 | 81-22, 38-19, 79-09 |
| D-03 (una sesión, una compañía) | 1 | 81-18 |
| D-04 (configuración en dos niveles) | 1 | 80-03 |
| D-P / D-09 (MOD00 y D9) | 1 | 37-09 |
| Ajustes del 09-28 (no aplicados) | 6 | 37-10, 37-14, 38-17, 40-01, 41-02, 59-16 |
| Técnico / ADR propuesto y abierto | 9 | 81-01, 81-14, 81-17, 81-19, 37-01, 59-09, 59-15, 69-03, 79-10 |
| Supuesto sin respaldo | 1 | 37-16 (vuelta a `EN_PROCESO`) |

### 4.3 Diferidos (12)

| Motivo | Criterios | Ola o frente que los retoma |
|---|---|---|
| Locatario sin proveedor (D-02) | 37-06, 40-07, 59-05, 59-08 | D5 en N/A; se activa con el endpoint nuevo (C-PER +3 SP) |
| Familia TRASPASO primero (D-G) | 38-07, 40-10, 78-09 | Catálogo v2 (prenda), T3 (cancelación) |
| Impuesto manual primero (ADR f, **abierto**) | 37-03, 40-05, 59-10 | D5 (manual), C3 (automático) |
| FLITO por API externa (D-09) | 81-21, 41-09 | D9 |

Los 3 diferidos del impuesto **no tienen decisión registrada**: el ADR f está «Recomendada, abierta». Se suman a la lista de la sección 6.

### 4.4 Criterio → ola

| Ola / frente | Criterios de épica que entrega |
|---|---|
| D0 Esqueleto | 81-01, 81-02, 81-04, 81-18, 37-01, 38-01, 38-02 (si se acepta el ajuste) |
| D1 Parametrización, captura, configuración | 81-05, 81-06, 37-04, 37-11, 37-14, 38-04 a 38-10, 40-01 a 40-16 (cabecera), 41-01 a 41-08, 41-10 a 41-14, 80-11 |
| D2 RUNT + tablero | 81-07, 81-19, 37-05, 37-12, 37-16, 38-11, 38-12, 59-01 a 59-04, 59-06, 59-09, 59-11, 59-13, 59-15, 59-16, 41-15, 69-01 a 69-05 |
| D3 RUNT persona, SIMIT, avalúo, reproceso, manual | 81-08, 81-11, 37-02, 37-20, 37-21, 38-13, 38-14, 40-03, 40-04, 40-06, 59-07, 59-14, 69-06 a 69-10 |
| D4 Evidencias (libera Simple) | 81-15, 37-07, 37-18, 38-21, 38-22, 80-01 a 80-03, 80-08 a 80-10, 80-12, 80-13 |
| D5 Impuesto + locatario N/A | 81-09, 81-10, 37-03, 37-06, 40-05, 40-07, 59-05, 59-08, 59-10, 59-12 |
| D6 Dictamen | 81-12, 81-13, 37-08, 37-13, 37-17, 37-19, 38-15, 38-16, 59-17, 69-11, 78-01 a 78-15 |
| D7 Derivación (libera Con análisis) | 81-14, 81-22, 37-15, 38-17 a 38-20, 79-01 a 79-11 |
| D8 Admin | 81-16, 38-23, 79-12, 80-04 a 80-06 |
| D9 Integración FLITO | 81-21, 41-09 |
| Sin ola (contradicen o no cubiertos) | 81-03, 81-20, 79-06, 80-07; 38-02 hasta que se acepte |

---

## 5. Alcance nuevo que introduce el plan

Clasificación: **Necesario** (sin esto no se cumple una épica), **Técnico** (decisión del LT, sin efecto de negocio visible) y **Alcance nuevo** (cambia lo que el cliente ve, paga o puede hacer: lo aprueba el PO).

| # | Extra | Origen | Clase | Quién aprueba | Comentario |
|---|---|---|---|---|---|
| X-01 | Épica MOD00: host, identidad, suscripción, roles, configuración, puerta de consultas, handoff | MOD00 [09-28], recortada por D-P | Necesario | PO (crear la épica) + LT (D-P) | Ninguna épica cubre la plataforma. Sin MOD00 no hay D0 |
| X-02 | Rol `backoffice_diagnostico` + `tenant_type=FLIT` | D-01, D-16 | Necesario | LT (D-16), PO (D-01) | Es la forma técnica del «Admin FLIT» de 12637 |
| X-03 | Tercer perfil `admin_diagnostico` que **reprocesa** | D-01 | **Alcance nuevo** | PO | Cambia una regla explícita en 4 textos vigentes. Además consume contra la empresa cliente |
| X-04 | Configuración en dos niveles (fuentes habilitadas, límite por lote, `dictamen_llm`, retención; visibilidad, fuentes por defecto) | D-04 | **Alcance nuevo** | PO | 12881 y MOD00 dejaban «los campos» al PO. Aquí se fijan 7 campos y una pantalla con dos pestañas |
| X-05 | Celdas `RUNT_PROPIETARIO` y `RUNT_LOCATARIO` (RUNT persona) | D-02 / D-15 (abierta) / D-18 (abierta) | **Alcance nuevo** | PO + LT | Dos fuentes nuevas visibles, con costo por consulta, columna en la grilla y sección en el PDF |
| X-06 | Cola durable en Postgres con lease, estado `EN_CURSO` | ADR a | Técnico | LT | Corrige la pérdida de trabajo de Comparendos |
| X-07 | Filtro global por tenant + test de fuga | ADR b | Técnico (seguridad) | LT | Obligatorio por Ley 1581 |
| X-08 | Polling incremental en lugar de push | ADR c | Técnico | LT | Cumple «sin recargar» |
| X-09 | Empresa padre ve los diagnósticos de sus hijas (solo lectura) | D-03 (mejora) | **Alcance nuevo** | PO | ≈5 SP. Ninguna épica lo pide |
| X-10 | *Sanear* informativo con exportación | D-08 | **Alcance nuevo** (reemplazo) | PO | Sustituye la redirección que piden 12638 y 12679 |
| X-11 | *Sanear* también en Simple con deuda | §7.4 | **Alcance nuevo sin decisión** | PO | La épica lo ata a Bloqueado. D-08 no lo menciona |
| X-12 | Integración FLITO por API externa (ola D9) | D-09 | **Alcance nuevo** (reemplazo) | PO + equipo FLITO | Sustituye la vía 3 «interna» de 12641 |
| X-13 | Retención y purga (`retencion_dias`, `retencion_dias_max`) | D-04, §9.3 | **Alcance nuevo** | PO + Legal | Puede borrar trazabilidad que 12680 y 12679 usan para facturar y medir conversión |
| X-14 | Límite de vehículos por lote (500 por defecto) | D-04 | **Alcance nuevo** | PO | Ninguna épica fija un límite |
| X-15 | Estimado de consultas en el resumen de validación | §10, «propuesta, PO» | **Alcance nuevo sin decisión** | PO | Útil para el costo (12640), pero sin aprobar |
| X-16 | Bandera `dictamen_llm` por compañía, apagada por defecto | D-G | **Alcance nuevo** | PO | Afecta el nombre y la tarifa «Con Análisis IA» (12680) |
| X-17 | Vuelta `INCOMPLETO`/`COMPLETADO` → `EN_PROCESO` | §3.5 [supuesto] | **Alcance nuevo sin decisión** | PO | Cambia la máquina de estados de 12637 |
| X-18 | Conversión diagnóstico → trámite en el dashboard admin | 12680 #1 [09-28] + T2 | Necesario (cierra un hueco 12679 ↔ 12680) | PO | Ya estaba propuesto el 09-28 |
| X-19 | Evidencia parcial e invalidación por huella | 12680 #2-3 [09-28] | Necesario | PO | Ya estaba propuesto el 09-28 |
| X-20 | Frentes externos P, C-PER, C1, C3, T1, T2 | §7 | Necesario | LT + PO (prioridad entre equipos) | Trabajo en core-api, core-consultas y Trámites. ≈30–40 SP fuera del producto |
| X-21 | Concurrencia de 5 vehículos en vuelo por tenant | §10 | Técnico | LT | Afecta cuánto tarda un lote grande: conviene contárselo al PO |
| X-22 | Fuentes simuladas deterministas por último dígito | §9.1 | Técnico | LT | Solo DEV/QA |
| X-23 | SSE opcional en D8 | ADR c | Técnico | LT | Solo si el PO pide menos de 3 s |

**Resumen:** 12 extras son alcance nuevo para el PO (X-03, X-04, X-05, X-09 a X-17). Tres no tienen ninguna decisión detrás (X-11, X-15, X-17).

---

## 6. Contradicciones y decisiones que el PO o el LT deben resolver o ratificar

### 6.1 Contradicciones sin decisión que las respalde

| # | Tema | Épica dice | Plan dice | Opciones | Recomendación | Decide |
|---|---|---|---|---|---|---|
| K-1 | Alta de compañías desde Diagnóstico | 12881 CA 3 y §2: el alta llama a la creación de FLIT 2.0 con `origen=DIAGNOSTICO` | No hay alta. El catálogo es el del hub (§5; v1; MOD00 CA 6) | a) Ratificar el plan y editar 12881 · b) Mantener el alta y abrir un contrato con identidad | **a**. El hub ya es dueño de compañías (HU12967, DDL 126) | PO |
| K-2 | Login sin sesión | 12881 CA 4: login en el subdominio contra los usuarios de FLIT | Redirige al login PKCE del hub (patrón Comparendos) | a) Login del hub · b) Formulario propio | **a**. Ya existe y evita una segunda superficie de credenciales | PO + LT |
| K-3 | El Admin FLIT crea diagnósticos | 12881 §8 y v1 §8: «Crear diagnóstico: Admin Sí» | El backoffice **no** crea [supuesto, §6.2] | a) No crea · b) Crea en nombre del cliente con `?empresa=` auditado | **a** en la v1 del producto: el consumo se cobra al cliente y crear en su nombre exige reglas de facturación. Registrar la decisión | PO |

### 6.2 Cambios con respaldo débil (solo propuesta o supuesto)

| # | Tema | Épica | Plan | Respaldo actual | Recomendación | Decide |
|---|---|---|---|---|---|---|
| K-4 | Flujo por vehículo y evidencia parcial | 12637: «ningún módulo puede operar sin que el anterior haya completado» | MOD04 desde la cola, derivación por vehículo, evidencia parcial | Ajustes 09-28 sin aplicar | Ratificar. La propia 12679 ya habla por vehículo | PO |
| K-5 | Vuelta a `EN_PROCESO` tras reproceso o manual | 12637: tres estados, sin regreso | `INCOMPLETO`/`COMPLETADO` → `EN_PROCESO` | Supuesto (§3.5) | Aceptar: sin esto, un lote reparado queda marcado Incompleto | PO |
| K-6 | Impuesto manual primero (F3) | 12659: si el departamento está soportado, se consulta | Hasta C3, todo vehículo con impuesto queda en error + manual del backoffice | ADR f «Recomendada, abierta» | a) Manual para todos · b) No ofrecer la fuente hasta C3 (`fuentes_habilitadas`) · c) Manual solo en el departamento piloto. **b**: no vender una fuente que hoy solo sale a mano | PO + LT |
| K-7 | *Sanear* en Simple con deuda | 12638, 12679: *Sanear* con Bloqueado | También en Simple con deuda | Ninguno | Decidir. Si se acepta, agregarlo a D-08 | PO |
| K-8 | Estimado de consultas en el resumen | — | Propuesto en §10 | Ninguno | Aceptar: el costo es la razón de ser de 12640 | PO |
| K-9 | `admin_diagnostico` asignado solo a todo AdminCompany | MOD00 §4: «el rol de Diagnóstico no se hereda del rol de Trámites»; CA 8 | DDL 126 lo asigna automáticamente | Código ya mergeado | a) Aceptar la asignación automática y ajustar MOD00 · b) Asignación explícita por producto | PO + LT |

### 6.3 Decisiones del 2026-10-20 por ratificar (afectan texto vigente)

| # | Tema | Épicas afectadas | Plan | Opciones | Recomendación | Ratifica |
|---|---|---|---|---|---|---|
| D-01 | Tres perfiles; el admin de la compañía reprocesa | 12637, 12638, 12669, 12881 CA 11 | §6.2 | a) Ratificar · b) Volver a dos perfiles (solo backoffice reprocesa) | **a**, con dos condiciones: el reproceso solo repite la consulta real (nunca escribe datos) y queda en el log con `disparador=REPROCESO` y usuario | PO |
| D-02 | RUNT por placa vía Intempo; locatario en N/A | 12637, 12640, 12659 | C-PER, D2, D5 | a) Ratificar · b) Pedir el documento al usuario como respaldo | **a**. 12640 ya dice «RUNT vía Intempo». Añadir: medir el impacto en clientes RENTING y priorizar el endpoint del locatario | PO + LT |
| D-03 | Una sesión, una compañía; padre ve a sus hijas | 12881 §4 | D0, D8 | a) Ratificar · b) Esperar el cambio de compañía (P6) | **a**. La vista padre → hijas va como Feature aparte en D8, no en el compromiso inicial | PO |
| D-04 | Configuración en dos niveles | 12640, 12680 | D1 | a) Ratificar · b) Solo el nivel operativo | **a**, con `visibilidad_usuario=EMPRESA` por defecto para no contradecir 12680 («todos sus lotes») | PO |
| D-08 | *Sanear* informativo | 12638, 12679 | D7 | a) S2 · b) S3 enlace externo | **a** | PO |
| D-09 | FLITO diferido a D9 | 12641, 12881 | D9 | a) Ratificar · b) X3 exportar a Excel | **a**. Corregir «integración interna» en 12641 | PO |
| D-G | Veredicto determinista; LLM opcional; familia TRASPASO | 12637, 12638, 12640, 12678, 12680 | D6 | a) Ratificar · b) LLM obligatorio como redactor · c) Ampliar a prenda en la v1 | **a**. Además, el PO fija cómo se llama y se cobra «Con análisis» cuando `dictamen_llm=false` | PO (alcance) + LT (ADR) |
| D-16 | Backoffice = usuarios FLIT con `tenant_type=FLIT` | 12637 | D0 | — | Ratificar | LT |
| D-P | Puerta mínima en paralelo; P3 condición dura | MOD00 | §7.1 | — | Ratificar | LT |

### 6.4 Huecos y decisiones abiertas que el contraste suma

| # | Tema | Épica | Pregunta | Recomendación | Decide |
|---|---|---|---|---|---|
| K-10 | Clic en el semáforo muestra los campos extraídos | 12680 | ¿Quién es «la administración» (backoffice o admin de la compañía)? ¿Qué campos: el payload normalizado de la celda? | Endpoint `GET /consultas/{cid}` con el payload normalizado. Backoffice y admin de la compañía, sin origen manual. En D3 | PO |
| K-11 | Evitar la segunda consulta al RUNT | 12679 | ¿El wizard deja de consultar el RUNT si llega `ctx`? | Agregarlo a T1 como criterio. Si Trámites no puede, el PO acepta la doble consulta y su costo | PO + equipo de Trámites |
| K-12 | UI kit e iconos de Diseño | 12638 | ¿Se adoptan la biblioteca de iconos y el loader del comentario del 10-01? | Sí, como insumo de la HU de front clonado en D0 | PO + Diseño |
| K-13 | Un trámite por vehículo | 12679 | ¿Se puede derivar dos veces el mismo vehículo? | Uno por vehículo (lo impone el índice único). Si hace falta otro, se crea otro diagnóstico | PO |
| K-14 | D-18: ¿RUNT propietario obligatorio en Con análisis? | 12640 («a la carta») | Si se vuelve obligatoria, contradice «a la carta» | Obligatoria solo si el tipo de trámite la exige, con tooltip igual que el RUNT | PO |
| K-15 | Locatario siempre en N/A | 12640, 12659 | ¿Se ofrece un checkbox que nunca devuelve datos? | Ocultarlo con `fuentes_habilitadas` hasta que exista proveedor | PO |
| K-16 | Retención | 12680 | ¿Qué se purga: solo documentos de persona o el diagnóstico completo? | Purgar documentos; conservar el diagnóstico anonimizado para volumetría y conversión | PO + Legal |
| K-17 | D-05 formatos de placa | 12641 | La unión con `fieldRules.ts` amplía la épica | Unión, enmendando 12641 con los formatos nuevos | PO |

### 6.5 Resolución del 2026-10-21 (R-1 a R-24)

Decididas una por una en sesión; quedan pendientes de ratificar por el PO y el LT, igual que las del 2026-10-20. Los textos de la sección 7 ya las incorporan.

| # | Resuelve | Decisión |
|---|---|---|
| R-1 | D-01 | Se mantiene: el admin de la compañía reprocesa celdas en error. El reproceso solo repite la consulta real y queda en el log con `disparador=REPROCESO` y el usuario. Llenar a mano y override, solo backoffice FLIT |
| R-2 | D-G | Se mantiene el nombre «Con Análisis IA» y la IA redacta siempre en ese plan. El veredicto sigue saliendo del motor de reglas. `dictamen_llm` deja de ser opción del contrato |
| R-2b | derivada de R-2 | Si la IA falla, el dictamen sale con redacción de plantilla marcada «redacción IA pendiente» y un reintento la completa |
| R-3 | D-02, K-15 | Documento del locatario opcional en la captura. Si viene, se consultan RUNT y SIMIT del locatario; si no, N/A |
| R-4 | K-1 | Sin alta de compañías en Diagnóstico |
| R-5 | K-2 | Sin login propio: login del hub |
| R-6 | K-3 | El backoffice no crea diagnósticos en la v1 |
| R-7 | K-4 | Avance por vehículo |
| R-8 | K-5 | Un lote cerrado vuelve a `EN_PROCESO` al repararlo y se recalcula |
| R-9 | K-6 | El impuesto no se ofrece hasta que exista su consulta; por departamento sin soporte, N/A |
| R-10 | K-7 | *Sanear* también en Simple con deuda |
| R-11 | K-8 | Estimado de consultas configurable (`NO_MOSTRAR` · `ANTES_DE_LANZAR` · `AL_FINALIZAR`), nivel operativo, por defecto `NO_MOSTRAR` |
| R-12 | K-9 | Se acepta la asignación automática de `admin_diagnostico` a todo AdminCompany |
| R-13 | K-10 | Clic en el semáforo: todo usuario con acceso al diagnóstico ve el resultado normalizado, con documentos enmascarados |
| R-14 | K-11 | Trámites reutiliza el RUNT de Diagnóstico si tiene menos de 24 h |
| R-15 | K-12 | Iconos y loader de Diseño dentro del estándar visual de la suite |
| R-16 | K-13 | Un trámite por vehículo |
| R-17 | K-14, D-18 | En Con Análisis IA, las fuentes que exige la familia son obligatorias. La lista de TRASPASO la valida `expert-doc-engine` antes de D6 |
| R-18 | K-16, X-13 | No se borra nada: la retención sale del plan. Ninguna épica la menciona; era alcance nuevo |
| R-19 | K-17, D-05 | Mismos formatos de placa que Trámites (`frontend/lib/validation/fieldRules.ts:81`) |
| R-20 | tensión R-13 ↔ CA 16 / 12669 | El cliente no ve el origen manual en ningún lugar (grilla, detalle de celda ni PDF). Solo el backoffice FLIT y la auditoría |
| R-21 | A-10, 12680 80-03 | `visibilidad_usuario` = `EMPRESA` por defecto; el admin de la compañía puede bajarlo a `PROPIOS` |
| R-22 | derivada de R-18 | Tampoco se purgan los tokens de derivación; siguen el TTL de 10 min y el uso único |
| R-23 | D-07 | Los departamentos con impuesto los decide solo core-consultas; Diagnóstico solo cachea su rpc de capacidad |
| R-24 | D-15 | RUNT persona del propietario y del locatario en celda propia; si falla, no bloquea el vehículo |

---

## 7. Propuesta de acciones en ADO (NO ejecutar)

> Requiere confirmación humana y que el PO y el LT ratifiquen antes las decisiones de la sección 6. Proyecto: `FLIT - DIAGNOSTICO`. Toda Feature nueva: sprint **siguiente** al activo, tag `DOR`, AssignedTo humano, Area `FLIT - DIAGNOSTICO`. Las transiciones de estado las hace el PO.

### 7.1 Épica 12881: reemplazar la descripción

Texto para pegar (reemplaza el actual completo):

```markdown
**Estado:** paraguas del producto Diagnóstico Vehicular. Plan vigente: `docs/diagnosticos/plan-implementacion-diagnostico-vehicular-v2.md` (2026-10-20). Reemplaza la propuesta del 2026-09-23.

## Qué se construye
Un producto de la Suite FLIT con subdominio propio (`diagnostico.flitsas.online`). Una empresa consulta RUNT, SIMIT, avalúo e impuesto departamental sobre su flota y obtiene un reporte (Simple) o un dictamen por tipo de trámite (Con Análisis IA). Identidad, compañías, usuarios y suscripción viven en el hub: Diagnóstico no los crea ni los edita.

## Épicas del producto
- [0] Habilitación en la Suite (MOD00) — nueva
- 12637 Visión · 12638 Diseño
- 12640 [1] Parametrización · 12641 [2] Captura · 12659 [3] Consultas · 12669 [4] Dashboard
- 12678 [5] Dictamen · 12679 [6] Derivación · 12680 [7] Reportes

## Decisiones vigentes (acordadas el 2026-10-20 y el 2026-10-21)
- Puerta: suscripción del producto. Sin sesión, login del hub (no hay login propio en el subdominio). Una sesión es una compañía.
- Compañías: se crean en el hub y ahí se enciende el producto. Diagnóstico no da de alta compañías.
- Tres perfiles: usuario (crea, ve, descarga, deriva) · admin de la compañía (reprocesa consultas en error, configura la operación) · backoffice FLIT (llena datos, override, auditoría, volumetría; no crea diagnósticos). El reproceso solo repite la consulta real y queda registrado con su usuario.
- El diagnóstico avanza por vehículo: cada vehículo pasa a dictamen y a derivación cuando cierra sus consultas, sin esperar al lote.
- RUNT por placa vía Intempo; el documento del propietario sale de esa respuesta. El documento del locatario lo puede declarar el cliente en la captura; si no lo declara, las consultas del locatario quedan en N/A.
- Impuesto departamental: se ofrece solo en los departamentos con consulta automatizada.
- Con Análisis IA: el veredicto lo da un motor de reglas versionado; la IA redacta y explica siempre el dictamen. Si la IA no responde, el dictamen se entrega con redacción de plantilla y se completa después. Primera entrega: familia TRASPASO.
- *Sanear*: salida informativa con la deuda por fuente hasta que exista el módulo de saneamiento.
- Un vehículo del diagnóstico origina como máximo un trámite.
- FLITO: API de integración, después del producto.

## Entrega por olas
D0 esqueleto · D1 parametrización y captura · D2 RUNT + tablero · D3 SIMIT, avalúo, reproceso · D4 evidencias (libera Simple) · D5 locatario e impuesto por departamento · D6 dictamen · D7 derivación (libera Con Análisis IA) · D8 admin · D9 FLITO.

## Criterios de aceptación del producto
1. Con la suscripción suspendida, el hub no muestra Diagnóstico y el subdominio no abre el home, aunque haya sesión.
2. Con la suscripción activa, el menú del hub entra sin otro login. Sin sesión, el subdominio redirige al login del hub y, al autenticarse, regresa a Diagnóstico.
3. Diagnóstico no crea ni modifica compañías, usuarios, roles ni suscripciones.
4. Un identificador inválido nunca dispara una consulta externa.
5. Si el RUNT de un vehículo falla, ninguna otra fuente de ese vehículo se consulta.
6. Ningún usuario de la compañía puede llenar datos a mano ni cambiar el dictamen, ni por API ni por pantalla.
7. El modo Simple se libera con RUNT, SIMIT y avalúo; Con Análisis IA, con dictamen y derivación.
8. El backoffice FLIT no crea diagnósticos: todo diagnóstico lo crea un usuario de la compañía que lo paga.
```

Vínculos: `Related` desde 12881 hacia 12637, 12638, 12640, 12641, 12659, 12669, 12678, 12679, 12680 y la nueva MOD00. Parent-Child Epic → Epic solo si el PO lo pide después de probarlo (sección 3.1).

### 7.2 Épica nueva MOD00 — Diagnóstico Vehicular — [0] Habilitación en la Suite FLIT

Parte del texto de `epica-habilitacion-suite-y-ajustes.md` Parte 1. Estos son los **cambios** que hay que hacerle antes de crearla:

| Sección del borrador 09-28 | Cambio | Fuente |
|---|---|---|
| «Precondición de arranque» (7 casillas de la puerta completa) | Reemplazar por: «Puerta mínima. P1 (cliente `svc-diagnostico`, exchange y usuario de BD) y P4 (OIDC en DEV y QA) corren junto con D0. P3 (el gateway valida firma y audiencia) es **condición dura** para exponer en QA y para liberar. Mientras no exista P2, el producto aplica su propio guard de suscripción por petición.» | D-P, plan §7.1 |
| §1 «sobre la plantilla `flit-product`» | «sobre la plantilla de Comparendos (`flit-servicio` + frontend clonado)». `flit-product` no existe | Plan §2 #7 |
| §1 | Agregar: «Usar la biblioteca de iconos y el loader del manual UI kit de Diseño (comentario del 2026-10-01 en 12638), dentro del estándar visual de la suite (SuiteShell, tokens y componentes compartidos). Si un componente del UI kit choca con el estándar, decide Diseño.» | 12638, R-15 |
| §2 «El cambio de compañía activa lo resuelve el hub» | «Una sesión es una compañía. Para trabajar en otra, el usuario cierra sesión y vuelve a entrar.» | D-03 |
| §4 tabla de roles (2 columnas) | Tres columnas: Usuario · Admin de la compañía (`admin_diagnostico`: reprocesa celdas en error, edita la configuración operativa, ve todo lo de su empresa) · Backoffice FLIT (`backoffice_diagnostico`, solo en compañías con `tenant_type=FLIT`: llena datos, override, auditoría, volumetría, entre compañías con auditoría) | D-01, D-16 |
| §4 «El rol de Diagnóstico no se hereda del rol de Trámites» | «Los roles de Trámites no dan permisos de Diagnóstico. El AdminCompany recibe `admin_diagnostico` por diseño; si la compañía no tiene Diagnóstico encendido, el rol no da acceso.» | R-12 |
| §5 «Los campos concretos los define el PO» | Nivel contractual (backoffice): fuentes habilitadas, límite de vehículos por lote. Nivel operativo (admin de la compañía): visibilidad de diagnósticos, fuentes por defecto (subconjunto de las habilitadas), estimado de consultas (no mostrar · antes de lanzar · al finalizar; por defecto no mostrar) | D-04, R-2, R-11, R-18 |
| §7 mecanismo de salto | Agregar: «Token opaco de 256 bits, guardado con hash, TTL de 10 min, ligado a compañía, usuario y tipo de trámite. Lo canjea Trámites por gRPC. Con ese token, Trámites lee el RUNT del vehículo en Diagnóstico y no lo vuelve a consultar si tiene menos de 24 h.» | ADR h (prop.), R-14 |
| Criterios de aceptación | Mantener los 11. Agregar los 3 de abajo | Plan §12 |

CA que se suman (para pegar):

```markdown
12. El admin de la compañía no puede llenar datos, hacer override ni editar la configuración contractual: la API responde 403.
13. Un usuario con el rol de backoffice en una compañía que no es FLIT recibe 403 en toda ruta de backoffice.
14. Las fuentes por defecto de una compañía son siempre un subconjunto de sus fuentes habilitadas.
```

Features hijas de MOD00: **F00.1** Esqueleto del producto (D0) · **F00.2** Acceso, suscripción y configuración (guard propio en D0, configuración en D1) · **F00.3** Handoff entre subdominios (D7). La Feature de plataforma (7.4-a) va enlazada como `Related`.

### 7.3 Ajustes a las épicas existentes (texto para pegar)

Cada bloque va al final de la sección indicada de la descripción. **[09-28]** es el texto ya redactado en `epica-habilitacion-suite-y-ajustes.md` Parte 2 (se pega tal cual, salvo donde se indica). **[nuevo]** sale de las decisiones del 10-20 o de este contraste.

**12637 — Visión** (el 09-28 decía «sin cambios»; ahora sí cambia)

```markdown
### Ajustes 2026-10 (plan v2)
- [nuevo] Diagnósticos es un producto de la Suite FLIT con subdominio propio, no una pantalla de FLIT 2.0. Identidad, compañías y suscripción viven en el hub.
- [nuevo] Roles: Usuario (crea, monitorea, descarga, deriva) · Admin de la compañía (además reprocesa consultas en error y edita la configuración operativa) · Backoffice FLIT (llena datos a mano, override del dictamen, auditoría y volumetría; no crea diagnósticos). Ningún usuario de la compañía cliente llena datos ni cambia el dictamen. El reproceso solo repite la consulta real y queda registrado con el usuario que lo lanzó.
- [nuevo] Con Análisis IA: el veredicto (Viable o Bloqueado) lo da un motor de reglas versionado por tipo de trámite; la IA redacta y explica siempre el dictamen. Reemplaza «un agente de IA evalúa». Primera entrega: TRASPASO, TRASPASO UNILATERAL y TRANSFERENCIA DE DOMINIO.
- [nuevo] Locatario: ningún proveedor lo entrega hoy. El cliente puede declarar el documento del locatario en la captura; si lo declara, se consultan RUNT y SIMIT del locatario; si no, esas consultas quedan en N/A.
- [nuevo] El flujo es por vehículo: la regla «ningún módulo opera sin que el anterior haya completado» se aplica a cada vehículo, no al lote. El dashboard se ve desde que el lote entra en cola, cada vehículo se dictamina y se deriva al cerrar sus consultas, y los reportes pueden emitirse parciales.
- [nuevo] Estados: CREADO al guardar la cabecera. Un diagnóstico Incompleto o Completado vuelve a En Proceso si se reprocesa o se llena una consulta, y se recalcula al terminar. Cada cambio de estado queda en el historial con su disparador y su usuario.
```

**12638 — Diseño**

```markdown
### Ajustes 2026-10 (plan v2)
- [09-28] Se diseña una décima pantalla: Configuración del producto de la compañía activa. No contiene usuarios, roles ni suscripción.
- [nuevo] La pantalla 10 tiene dos pestañas: Contrato (solo backoffice FLIT, de solo lectura para la compañía) y Operación (admin de la compañía).
- [nuevo] Tres perfiles de vista: Usuario (grilla sin acciones), Admin de la compañía (Reprocesar en celdas rojas) y Backoffice FLIT (Reprocesar, Llenar datos, override, selector de empresa).
- [nuevo] El botón Crear Trámite aparece por fila en cuanto ESE vehículo cierra sus consultas aplicables, sin esperar al lote. (Reemplaza «una vez el diagnóstico está completo».)
- [nuevo] Sanear aparece con veredicto Bloqueado (Con Análisis IA) o cuando el vehículo tiene deuda (Simple). Abre un panel con la deuda por fuente (SIMIT vehículo, SIMIT propietario, SIMIT locatario, impuesto), cada una con su monto, y permite exportarlo.
- [nuevo] Los grupos Por Propietario y Por Locatario incluyen RUNT persona además de SIMIT, cada una en su propia celda; si falla, no bloquea el vehículo. Las fuentes sin proveedor se muestran como no disponibles.
- [nuevo] La celda en N/A muestra el motivo (servicio público, sin documento de locatario, departamento no soportado, sin proveedor).
- [nuevo] Al hacer clic en una celda del semáforo, cualquier usuario con acceso al diagnóstico ve los campos extraídos de esa consulta, con los documentos de persona enmascarados.
- [nuevo] Se usan la biblioteca y el repositorio de iconos y el loader del manual UI kit compartidos el 2026-10-01, dentro del estándar visual de la suite (SuiteShell, tokens y componentes compartidos).
```

**12640 — MOD01**

```markdown
### Ajustes 2026-10 (plan v2)
- [09-28] En Diagnóstico Express el nombre se autogenera a partir del identificador y la fecha, y queda editable. El campo sigue siendo obligatorio.
- [09-28, ajustado] Los checkboxes se presentan en los tres grupos (Por Vehículo, Por Propietario, Por Locatario). Propietario depende del RUNT y Locatario del documento declarado en la captura; ninguno puede marcarse como fuente independiente.
- [nuevo] El alcance solo ofrece las fuentes que la compañía tiene habilitadas y premarca las fuentes por defecto de la compañía.
- [nuevo] En esta etapa, el combobox de Con Análisis IA ofrece TRASPASO, TRASPASO UNILATERAL y TRANSFERENCIA DE DOMINIO. REGISTRO y LEVANTAMIENTO DE PRENDA entran en la siguiente versión del catálogo; CANCELACION DE MATRICULA cuando el wizard la habilite.
- [nuevo] En Con Análisis IA, las fuentes que exige el tipo de trámite elegido quedan marcadas y bloqueadas, con un tooltip que explica por qué (igual que el RUNT). Las demás siguen a la carta. Para la familia TRASPASO: RUNT vehículo, SIMIT vehículo, RUNT propietario y SIMIT propietario.
- [nuevo] Las consultas Por Locatario solo se ejecutan para los vehículos en los que el cliente declare el documento del locatario.
- [nuevo] Si la compañía lo configura, el resumen muestra antes de lanzar el estimado de consultas por fuente (vehículos válidos × fuentes elegidas, menos las que quedarán en N/A). Por defecto no se muestra.
```

**12641 — MOD02**

```markdown
### Ajustes 2026-10 (plan v2)
- [09-28] Los identificadores se normalizan a mayúsculas y sin espacios antes de clasificarlos.
- [09-28] Los duplicados de un mismo archivo se consolidan en una fila y se reportan en el Resumen de Validación.
- [09-28] El Diagnóstico Express se persiste como un diagnóstico de un vehículo; «sin creación de lote» aplica a la experiencia, no al modelo.
- [nuevo] La Vía 3 (FLITO) sale de esta épica: FLITO es un sistema externo y entrará por una API de integración del producto, después de la primera liberación. Las vías de esta épica son Express y Carga masiva.
- [nuevo] Un lote no supera el límite de vehículos de la compañía; el resumen lo informa antes de encolar.
- [nuevo] Si la compañía lo configura (antes de lanzar), el Resumen de Validación muestra el estimado de consultas externas por fuente antes de encolar. Por defecto no se muestra.
- [nuevo] La plantilla de carga masiva suma una columna opcional Documento_Locatario (tipo y número) y el Express un campo opcional equivalente. El documento se guarda cifrado y marcado como declarado por el cliente.
- [nuevo] Reglas de detección de Placa: se amplían a los mismos formatos que acepta el wizard de Trámites: carro o público (ABC123), moto (ABC12D y ABC12), remolque o semirremolque (R o S + 5 o 6 números, ejemplo: R12345, S123456) y maquinaria (2 letras + 6 números, ejemplo: MC029554). El RUNT valida en definitiva.
```

**12659 — MOD03** (el primer bloque **corrige** el texto vigente)

```markdown
### Ajustes 2026-10 (plan v2)
- [09-28, ampliado] Enum de fuentes del log: RUNT, RUNT_PROPIETARIO, RUNT_LOCATARIO, SIMIT_VEHICULO, SIMIT_PROPIETARIO, SIMIT_LOCATARIO, AVALUO, IMPUESTO_DEPARTAMENTAL. FASECOLDA sale del enum: es un proveedor, no una fuente.
- [09-28] El log incorpora el campo servicio (VERIFIK, KYVERUM, INTEMPO, SMARTCLOUD, FASECOLDA, SCRAPING, entre otros). Es nulo en el llenado manual.
- [09-28] Toda consulta externa sale por la API de consultas de la plataforma. El módulo no integra proveedores ni guarda sus credenciales.
- [09-28] El consumo de cada consulta queda medido por empresa y por producto.
- [09-28] El motor es idempotente por (vehículo, fuente): un segundo disparo no repite una consulta ya en Completado o N/A.
- [09-28] El llenado manual escribe registro de auditoría igual que la consulta automática, sin ID de transacción del proveedor.
- [nuevo] Paso 1: el RUNT por placa se consulta sin documento (lo resuelve Intempo); el documento del propietario sale de esa respuesta y alimenta RUNT persona y SIMIT propietario. Por VIN sigue la cadena normal.
- [nuevo] RUNT persona del propietario es una consulta propia: si falla, no bloquea al vehículo.
- [nuevo] Locatario: si el cliente declaró su documento en la captura, se consultan RUNT Locatario y SIMIT Locatario con ese documento; si no, quedan en N/A con el motivo «sin documento de locatario» y no consumen. Cuando un proveedor entregue el locatario por placa, el dato se toma de ahí.
- [nuevo] El departamento se deriva del organismo de tránsito del RUNT contra la tabla de organismos.
- [nuevo] Impuesto departamental: la fuente solo se ofrece en los departamentos con consulta automatizada. Un vehículo de un departamento sin consulta queda en N/A con el motivo «departamento no soportado», no en Error.
- [nuevo] Cada reproceso queda en el log con disparador REPROCESO y el usuario que lo lanzó; solo repite la consulta real.
- [nuevo] El trabajo encolado sobrevive a un reinicio y a dos réplicas sin doble consumo; una consulta tomada y no terminada se retoma al vencer su plazo.
```

**12669 — MOD04**

```markdown
### Ajustes 2026-10 (plan v2)
- [09-28] La restricción del Usuario se aplica también en la API: una llamada directa a reprocesar o llenar datos sin el permiso es rechazada.
- [09-28] El estado global del lote se calcula; ninguna acción de usuario lo edita.
- [nuevo] Reprocesar lo pueden hacer el admin de la compañía y el backoffice FLIT; Llenar datos, solo el backoffice FLIT. (Reemplaza «solo el Admin FLIT reprocesa».) El reproceso solo repite la consulta real.
- [nuevo] Un lote Completado o Incompleto vuelve a En Proceso al reprocesar o llenar una consulta, y su estado se recalcula al terminar.
- [nuevo] «Tiempo real» se cumple con actualización incremental de solo las celdas que cambiaron, sin recargar la página y con no más de 3 s de retraso.
- [nuevo] Ninguna lectura de la grilla devuelve datos de otra compañía (prueba de fuga obligatoria).
```

**12678 — MOD05**

```markdown
### Ajustes 2026-10 (plan v2)
- [nuevo] El veredicto (Viable o Bloqueado) lo dan reglas deterministas versionadas por tipo de trámite. El mismo consolidado produce siempre el mismo veredicto. Reemplaza «un agente de IA evalúa»: la IA redacta y explica, no decide.
- [nuevo] En Con Análisis IA, la IA redacta siempre los alertamientos y la explicación del dictamen a partir de los hallazgos. El LLM no recibe documentos ni nombres.
- [nuevo] Si la IA falla o no responde, el dictamen se entrega con el veredicto y una redacción de plantilla del catálogo, marcado «redacción IA pendiente»; un reintento en segundo plano la completa y regenera el PDF.
- [nuevo] Primera versión del catálogo: TRASPASO, TRASPASO UNILATERAL y TRANSFERENCIA DE DOMINIO. Las reglas las valida el PO antes de liberarlas.
- [nuevo] El dictamen guarda la versión del catálogo con la que se emitió.
- [nuevo] El override es exclusivo del backoffice FLIT.
```

**12679 — MOD06** (el 09-28 #1 pasa a 12638: aquí el texto ya dice «un vehículo»)

```markdown
### Ajustes 2026-10 (plan v2)
- [09-28] En Con Análisis con veredicto Bloqueado no se ofrece Crear Trámite; solo Sanear.
- [09-28] El salto a Trámites viaja como contexto de un solo uso entre subdominios y se canjea en destino; un segundo canje se rechaza.
- [09-28] Hacia saneamiento, el monto de la deuda y su fuente viajan juntos.
- [09-28] El contrato de campos de entrada del wizard se cierra antes de construir este módulo.
- [nuevo] Mientras no exista un módulo de saneamiento, Sanear muestra la deuda desglosada por fuente con su monto y permite exportarla. Aparece con veredicto Bloqueado (Con Análisis IA) o cuando el vehículo tiene deuda (Simple).
- [nuevo] El wizard que recibe el contexto no vuelve a consultar el RUNT del vehículo si la consulta de Diagnóstico tiene menos de 24 horas; si es más vieja, consulta normalmente.
- [nuevo] Un vehículo del diagnóstico origina como máximo un trámite. Tras derivar, el botón muestra «Ver trámite #N». Si el trámite se cancela o hace falta otro tipo de trámite, se crea un diagnóstico nuevo.
```

**12680 — MOD07**

```markdown
### Ajustes 2026-10 (plan v2)
- [09-28] El dashboard admin reporta los trámites originados en diagnósticos, por cliente y tipo de trámite, con la tasa de conversión.
- [09-28] Los reportes pueden emitirse con lo ya completado, sin esperar al cierre del lote.
- [09-28] Los archivos se generan bajo demanda y no se regeneran si la evidencia no cambió; un recálculo de dictamen o un llenado manual invalida el PDF de ese vehículo.
- [09-28] Todo listado y detalle filtra por la compañía del usuario; solo el tablero de facturación del backoffice puede ver varias.
- [nuevo] El dashboard de facturación y control es del backoffice FLIT.
- [nuevo] Al hacer clic en una celda del semáforo, cualquier usuario con acceso al diagnóstico (reemplaza «la administración») ve los campos extraídos de esa consulta. Los documentos de persona salen enmascarados y nunca se muestra la respuesta cruda del proveedor. No se revela si la celda se llenó a mano.
- [nuevo] Si la compañía lo configura (al finalizar), el detalle del lote y el reporte muestran las consultas consumidas por fuente.
- [nuevo] Una empresa padre ve en solo lectura los diagnósticos de sus hijas; una hija no ve a su padre ni a sus hermanas. Entra en la ola D8, después de liberar Con Análisis IA.
```

### 7.4 Features nuevas de los frentes (borradores en formato `feature-creator`)

**7.4-a · Plataforma**

```markdown
Título: [PLATAFORMA] - Puerta mínima de la Suite para el producto Diagnóstico

# OBJETIVO
Dejar la plataforma lista para que el producto Diagnóstico arranque en DEV y se libere en QA sin esperar la puerta de salida completa de la Suite.

# DESCRIPTION
Cliente de servicio, exchange de eventos, OIDC por ambiente, validación de firma en el gateway, guard de suscripción en el SDK y retiro de la tarjeta «Próximamente». El cambio de compañía, las pantallas de administración del hub y la plantilla flit-product quedan como backlog sin compromiso.

# CRITERIOS FUNCIONALES
- [ ] P1: existe el cliente svc-diagnostico con sus scopes, el exchange flit.diagnostico y el usuario de BD dueño del schema; la consulta de consumo muestra una consulta con producto diagnostico. [fuente: plan v2 §7.1 P1]
- [ ] P4: el login PKCE funciona desde el subdominio en DEV y en QA. [fuente: plan v2 §7.1 P4]
- [ ] P3: un token sin firma recibe 401 en el gateway. Condición para exponer en QA y liberar. [fuente: plan v2 §7.1 P3, D-P]
- [ ] P2: al suspender el producto, la API responde 403 en menos de 2 minutos sin volver a iniciar sesión. [fuente: plan v2 §7.1 P2; MOD00 CA 5]
- [ ] P5: al liberar, la tarjeta del hub abre el producto. [fuente: plan v2 §7.1 P5]
```

Padre sugerido: MOD00. `Related` con el Frente A de la Suite (A-01 #12894, proyecto FLIT - EVOLUTION) si el LT decide que vive allí.

**7.4-b · Consultas**

```markdown
Título: [CONSULTAS] - RUNT por placa sin documento y total adeudado de SIMIT para Diagnóstico

# OBJETIVO
Que la plataforma de consultas entregue, a partir de solo la placa, el documento del propietario del vehículo y el total adeudado en SIMIT, para que Diagnóstico pueda consultar RUNT persona y SIMIT propietario y emitir alertamientos con monto.

# DESCRIPTION
C-PER: cuando llega una placa sin documento, la cadena de ConsultarVehiculo salta los proveedores que exigen documento y responde Intempo. El mapper de Intempo entrega tipo y número de documento del propietario, sin el nombre. Se suma el código del organismo de tránsito. C1: ConsultarMultas entrega el total adeudado. El impuesto departamental (C3) va en una Feature aparte, con validación previa de Legal.

# CRITERIOS FUNCIONALES
- [ ] Una consulta de RUNT por placa sin documento responde con Intempo y trae el tipo y el número de documento del propietario. [fuente: plan v2 D-02, §7.2 C-PER; épica 12640 «RUNT vía Intempo»]
- [ ] La respuesta no trae el nombre del propietario. [fuente: plan v2 §9.3]
- [ ] Los fixtures del mapper se construyen con datos ficticios y pasan la revisión de seguridad (Ley 1581). [fuente: plan v2 §11.1]
- [ ] La respuesta del RUNT incluye el código del organismo de tránsito. [fuente: plan v2 §2.1]
- [ ] ConsultarMultas entrega el total adeudado por placa o por documento. [fuente: plan v2 §7.2 C1]
- [ ] Una consulta por VIN sigue la cadena actual sin cambios. [fuente: plan v2 §2.1]
```

Padre sugerido: 12659. `Related` con 12678 y 12679 (por C1). El código vive en core-api / core-consultas: el LT decide si la Feature se crea en DIAGNOSTICO o en EVOLUTION.

**7.4-c · Trámites**

```markdown
Título: [TRAMITES] - Canje del contexto de Diagnóstico en el wizard y reporte por origen

# OBJETIVO
Que el wizard de Trámites reciba el contexto de un diagnóstico, precargue el formulario sin volver a consultar el RUNT y deje el trámite vinculado al vehículo diagnosticado, y que Trámites informe cuántos trámites nacieron de Diagnóstico.

# DESCRIPTION
T1: el wizard acepta el contexto de un solo uso, lo canjea con Diagnóstico, precarga placa, VIN, datos del vehículo y documento del propietario, reutiliza la consulta RUNT de Diagnóstico si tiene menos de 24 horas (cambio en RunConsultationCommand) y crea el borrador con origen diagnostico y la referencia del vehículo. Expone la consulta por referencia externa. T2: conteo por empresa y tipo de trámite con origen diagnostico.

# CRITERIOS FUNCIONALES
- [ ] El wizard precarga placa, VIN, datos del vehículo y documento del propietario desde el contexto. [fuente: épica 12679; plan v2 §6.5]
- [ ] Un segundo canje del mismo contexto se rechaza. [fuente: MOD00 CA 10; plan v2 ADR h]
- [ ] Un contexto de otra compañía se rechaza. [fuente: plan v2 §9.1]
- [ ] Con contexto y una consulta RUNT de Diagnóstico de menos de 24 horas, el wizard la reutiliza y no vuelve a consultar el RUNT; si tiene más de 24 horas, consulta normalmente. El plazo es configurable. [fuente: épica 12679 «evita una segunda consulta al RUNT»; R-14]
- [ ] El trámite creado guarda el origen diagnostico y la referencia del vehículo, y Diagnóstico puede leer su número. [fuente: épica 12679; plan v2 H1-a]
- [ ] Trámites reporta el conteo de trámites con origen diagnostico por empresa y por tipo. [fuente: plan v2 §7.3 T2; ajuste 12680 #1]
```

Padre sugerido: 12679. `Related` con 12680 (T2). T3 (cancelación de matrícula) queda enlazada a la épica existente de tipos dinámicos.

**7.4-d · Integración FLITO (sin sprint)**

```markdown
Título: [DIAGNOSTICO] - Integración FLITO por API de integración

# OBJETIVO
Que FLITO cree un diagnóstico con una lista de placas de su inventario y reciba el aviso del resultado, sin cargar un archivo.

# DESCRIPTION
FLITO es un sistema externo. Entra por una API de integración del producto con cliente externo (patrón ADR-0067) y recibe el aviso por webhook firmado (patrón ADR-0071). Reemplaza la Vía 3 de la épica 12641. Se refina con el equipo de FLITO después de liberar el modo Simple.

# CRITERIOS FUNCIONALES
- [ ] FLITO crea un diagnóstico de N placas sin intervención y recibe un enlace al diagnóstico. [fuente: épica 12641 Vía 3; plan v2 D-09, §7.5]
- [ ] Al cerrar el diagnóstico, FLITO recibe un aviso firmado con el resultado. [fuente: plan v2 §7.5]
- [ ] Las placas que llegan por la integración pasan por el mismo validador que la carga masiva. [fuente: épica 12641 «Auto-detección»]
- [ ] Hay pruebas de contrato acordadas con el equipo de FLITO. [fuente: plan v2 D9]
```

Padre sugerido: 12641. **Sin sprint** hasta refinarla (D-09). No cumple DoR todavía, y es correcto: no debe pasar a `Active`.

**DoR-Feature preliminar de los 4 borradores** (sin acceso a ADO no se puede verificar sprint, Area ni tag):

| Criterio DoR-Feature | 7.4-a | 7.4-b | 7.4-c | 7.4-d |
|---|---|---|---|---|
| Módulo en el título | PASS | PASS | PASS | PASS |
| Objetivo | PASS | PASS | PASS | PASS |
| Descripción extendida | PASS | PASS | PASS | PASS |
| ≥ 3 criterios funcionales | PASS (5) | PASS (6) | PASS (6) | PASS (4) |
| Sprint siguiente al activo | FAIL (por asignar) | FAIL (por asignar) | FAIL (por asignar) | FAIL (sin sprint, a propósito) |
| Area FLIT | FAIL (por asignar) | FAIL (falta decidir proyecto) | FAIL (falta decidir proyecto) | FAIL (por asignar) |
| Tag `DOR` | FAIL | FAIL | FAIL | FAIL |
| AssignedTo humano | FAIL (por asignar) | FAIL | FAIL | FAIL |
| Sin placeholders | PASS | PASS | PASS | PASS |
| Sin datos sensibles | PASS | PASS | PASS | PASS |

Los FAIL se resuelven al registrar (criterios de forma). Ninguno es de contenido.

### 7.5 Features por ola y su épica padre

| Ola | Feature (título sugerido) | Padre |
|---|---|---|
| D0 | [DIAGNOSTICO] - Esqueleto del producto, sesión y roles | MOD00 (F00.1) |
| D0–D1 | [DIAGNOSTICO] - Acceso, suscripción y configuración en dos niveles | MOD00 (F00.2) |
| D1 | [DIAGNOSTICO] - Parametrización del diagnóstico | 12640 |
| D1 | [DIAGNOSTICO] - Captura express y carga masiva | 12641 |
| D2 | [DIAGNOSTICO] - Motor RUNT con cola durable | 12659 |
| D2 | [DIAGNOSTICO] - Tablero de semáforos | 12669 |
| D3 | [DIAGNOSTICO] - RUNT persona, SIMIT y avalúo | 12659 |
| D3 | [DIAGNOSTICO] - Reproceso y llenado manual | 12669 |
| D4 | [DIAGNOSTICO] - Evidencias y listado (libera Simple) | 12680 |
| D5 | [DIAGNOSTICO] - Locatario declarado e impuesto por departamento | 12659 |
| D6 | [DIAGNOSTICO] - Dictamen familia TRASPASO | 12678 |
| D7 | [DIAGNOSTICO] - Derivación y Sanear informativo | 12679 (+ MOD00 F00.3 para el handoff) |
| D8 | [DIAGNOSTICO] - Volumetría, conversión y vista padre | 12680 |
| D9 | 7.4-d | 12641 |

12637 y 12638 no tienen Feature propia. 12637 es visión y no se implementa. 12638 se cumple dentro de cada Feature de front: cada HU de UI la enlaza como `Related`.

### 7.6 Orden recomendado

1. **Ratificación** de D-01, D-02, D-03, D-04, D-08, D-09, D-G (PO) y D-16, D-P, ADR a/b/c (LT), y de R-1 a R-24 (sección 6.5). Los choques K-1 a K-17 ya tienen decisión; falta solo la ratificación. Sin ella, editar las épicas fija decisiones que pueden cambiar.
2. **12881**: reemplazar la descripción (7.1). Es el documento que más contradice hoy.
3. **Crear MOD00** (7.2) y la Feature de plataforma (7.4-a): desbloquean D0.
4. **12637 y 12638** (Active): ajustes de 7.3. Fijan vocabulario y perfiles para todo lo demás.
5. **12640 y 12641** (Sprint 1): ajustes de 7.3. Son la ola D1.
6. **Feature de Consultas** (7.4-b): C-PER está en el camino crítico de D2. Requiere además el JSON de ejemplo de Intempo, anonimizado.
7. **12659 y 12669**: ajustes antes de D2.
8. **12680, 12678, 12679**: ajustes antes de D4, D6 y D7, respectivamente.
9. **Feature de Trámites** (7.4-c) cuando arranque D5: T1 corre con D5–D6.
10. **Features por ola** (7.5), cada una una ola antes de ejecutarla, por `/decompose-feature`. No todas al inicio (lección de Comparendos).
11. **Integración FLITO** (7.4-d), sin sprint.
12. **Vínculos**: `Related` 12881 ↔ las 10. Parent-Child Feature → Épica según 7.5.
13. **Estados**: el PO decide si 12881, 12637 y 12638 siguen en `Active` sin hijos o vuelven a `New` hasta que exista su primera Feature.

---

## 8. Ajustes recomendados al plan v2

Aplicada al plan el 2026-10-21 junto con R-1 a R-24 (sección 6.5). A-16 queda anulada por R-18; A-6, A-9, A-12 a A-15, A-18 y A-19 quedan cerradas por las decisiones R.

| # | Dónde | Ajuste | Por qué |
|---|---|---|---|
| A-1 | Cabecera, línea 10 | Reemplazar «No se pudo leer el texto vigente…» por: «Contrastado contra ADO el 2026-10-21 (`contraste-plan-v2-vs-epicas-ado.md`)» | Ya se leyó |
| A-2 | Cabecera «Proyecto: FLIT Suite» | Agregar: «Work items en el proyecto ADO `FLIT - DIAGNOSTICO`» | Las épicas viven allí, no en EVOLUTION |
| A-3 | §12, fila 12637 | «Sin cambios» → «Ajuste» con los 6 puntos de 7.3 | Encuadre, roles, IA, locatario, flujo y estados cambian |
| A-4 | §12 | Agregar una fila para **12881**: reemplazar la descripción (v0 del 09-23) y enlazar como paraguas | El plan no la menciona y es la que más contradice |
| A-5 | §12, filas 12638 y 12679 | El ajuste «Crear Trámite por vehículo» va en 12638; en 12679 ya está | 12679 dice «Una vez un vehículo tiene su diagnóstico completo» |
| A-6 | §6.1 y D3 | Agregar `GET /consultas/{cid}` (campos extraídos de la celda) con permiso por decidir (K-10) | Cubre 12680 80-07 |
| A-7 | §7.3 T1 y §6.5 | Agregar el criterio «con `ctx`, el wizard no reconsulta el RUNT» | Cubre 12679 79-06, el valor central de MOD06 |
| A-8 | D0 (front clonado) | Insumo: manual UI kit + biblioteca y repositorio de iconos + loader (comentario del 10-01 en 12638) | Cubre 12638 38-02 |
| A-9 | §7.4 | Quitar «o en Simple con deuda», o registrarlo como parte de D-08 | Alcance sin decisión (K-7) |
| A-10 | §5 DDL `visibilidad_usuario` | Valor por defecto `EMPRESA`, o registrar por qué `PROPIOS` | 12680: el cliente ve «todos sus lotes» con la columna de usuario creador |
| A-11 | §6.2 `diagnosticos.crear` del backoffice | Cerrar el supuesto con una decisión (K-3) | 12881 y la v1 dicen lo contrario |
| A-12 | §4 (f) y D5 | Evaluar no ofrecer `IMPUESTO_DEPARTAMENTAL` (`fuentes_habilitadas`) hasta C3, en lugar de llenar a mano todos los vehículos (K-6). Convertir F3 en decisión registrada | Hoy es la única fuente diferida sin decisión |
| A-13 | D5 | Locatario: ocultar la fuente mientras no haya proveedor (K-15). Agregar a §11.1 el riesgo para tenants `RENTING`, que son el mercado principal (la 12640 pone como ejemplo «Lote Traspasos Renting») | En renting y leasing la consulta que importa es la del locatario |
| A-14 | §6.2 y MOD00 | Resolver la tensión entre «no se hereda del rol de Trámites» y la asignación automática de `admin_diagnostico` a todo AdminCompany (K-9) | MOD00 CA 8 |
| A-15 | §4 (g) y D8 | Fijar con el PO el nombre y la agrupación de facturación de Con análisis cuando `dictamen_llm=false` | 12680 agrupa por «Con Análisis IA-…» |
| A-16 | §9.3 | Precisar qué purga `retencion_dias`: documentos de persona o el diagnóstico completo (K-16) | La purga total rompe volumetría y conversión (12680, 12679) |
| A-17 | §9.2 | Renumerar los CA contra el texto que quede en ADO (12881 nueva + MOD00) | Hoy cita la numeración de la v1 (20 CA), que no existe en ADO (12881 tiene 16, con otro orden) |
| A-18 | §3.5 y §11.2 | Pasar «vuelta a `EN_PROCESO`» de supuesto a decisión abierta del PO (K-5) | Cambia la máquina de estados de 12637 |
| A-19 | §11.2 | Agregar como abiertas: K-3, K-6, K-7, K-8, K-10, K-11, K-13, K-15, K-16 | No figuran en el plan |

---

## Apéndice — Fuentes citadas

- Épicas: `…/scratchpad/epicas/00-indice.md` y `<id>.md` (volcado de solo lectura del 2026-10-21). Líneas clave: 12679.md:26 («Una vez un vehículo tiene su diagnóstico completo»), 12638.md:58 («una vez el diagnóstico está completo»), 12640.md:20 («RUNT vía Intempo»), 12680.md:34 (clic en el semáforo).
- Plan v2: `docs/diagnosticos/plan-implementacion-diagnostico-vehicular-v2.md` (registro de decisiones, líneas 14–40; §6.2, líneas 742–765; §12, líneas 1072–1097).
- v1: `docs/diagnosticos/propuesta_implementacion_diagnostico_vehicular.md` (§8 roles, §12 CA 1–20).
- Ajustes del 09-28: `docs/diagnosticos/epica-habilitacion-suite-y-ajustes.md` (Parte 1 MOD00, Parte 2 con 23 criterios).

*Documento sin publicar en ADO. Los veredictos son recomendaciones del po-agent; el PO humano aprueba alcance, prioridad y cierre.*
