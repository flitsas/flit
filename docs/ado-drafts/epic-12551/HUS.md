# Descomposición HU — Épica #12551

> **Estado:** registradas en ADO (2026-09-18). Estado `New`. Commits y Evidences vacíos.  
> **Sprint:** Sprint 7 (instrucción del supervisor).  
> **AssignedTo:** Willyn Londoño Calle. Tags: `DOR`. `Refinement=true`.  
> **Diseño técnico:** el glosario `inventario-glosario.md` (sin schema ni ADR de persistencia).  
> **Regla:** una fila sin Ganador no se implementa; el AC nombra el ID del glosario, no inventa copy.

Totales: **12 HUs** · 8 / 16 / 12 / 8 SP por Feature (ninguna supera 8 HUs).

| Feature | HUs ADO |
|---|---|
| #12689 Glosario | [#12693](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12693) · [#12694](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12694) |
| #12690 Trámites | [#12695](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12695) · [#12696](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12696) · [#12697](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12697) · [#12698](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12698) |
| #12692 Transversal | [#12699](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12699) · [#12700](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12700) · [#12701](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12701) · [#12702](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12702) |
| #12691 Correos/PDFs | [#12703](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12703) · [#12704](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12704) |

---

## Feature #12689 — Glosario canónico (2 HUs · 8 SP)

### HU-12689-1 — [#12693](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12693) `[FRONTEND] – Homologación – Fuente única de copy canónico`

| Campo | Valor |
|---|---|
| SP | 5 |
| Depende | Ninguna (bloquea al resto) |

**Como** operador OT o gestor  
**quiero** que los textos compartidos salgan de un único catálogo en código  
**para** que un cambio de glosario no se aplique en una vista y se olvide en la otra

```gherkin
AC1 — positivo
Dado el glosario con Ganador en las filas que esta Feature cubre
Cuando se renderiza un label homologable en vista OT y en vista gestor
Entonces ambas leen el mismo símbolo del catálogo de copy
Y no hay un string literal duplicado para ese concepto en las dos superficies

AC2 — negativo
Dado una fila del Bloque A sin Ganador
Cuando un desarrollador intenta usarla en el catálogo
Entonces esa clave no se publica como texto de UI
Y no se despliega un sinónimo inventado

AC3 — borde
Dado el Bloque B (estados ya unificados en estados.ts)
Cuando se publica el catálogo
Entonces reutiliza estadoLabel / ESTADO_LABELS
Y no duplica Borrador, Aprobado ni el resto del catálogo de negocio
```

### HU-12689-2 — [#12694](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12694) `[FRONTEND] – Homologación – Lista QA de campos cambiados`

| Campo | Valor |
|---|---|
| SP | 3 |
| Depende | HU-12689-1 |

**Como** analista de QA  
**quiero** la lista de campos cuyo texto cambió según el glosario  
**para** certificar la épica #12551 sin barrer la plataforma a ciegas

```gherkin
AC1 — positivo
Dado el glosario con filas Ganador distintas del texto actual
Cuando QA abre la lista de campos cambiados
Entonces cada fila tiene ID de glosario, superficie, texto anterior y texto ganador

AC2 — negativo
Dado solo filas RN-07 o Ganador igual al actual
Cuando se genera la lista
Entonces esas filas no aparecen como cambio a certificar

AC3 — borde
Dado el criterio de la épica «especificar campos para pruebas de QA»
Cuando la Feature #12689 está Resolved
Entonces la lista está en el repo junto al glosario y se cita en Discussion
```

---

## Feature #12690 — Textos en trámites (4 HUs · 16 SP)

### HU-12690-1 — [#12695](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12695) `[FRONTEND] – Homologación – Cabeceras de listado OT y gestor`

| Campo | Valor |
|---|---|
| SP | 5 |
| Depende | HU-12689-1 |

A01–A06. Layout A02 (Vehículo vs VIN/Placa) no se unifica.

```gherkin
AC1 — positivo
Dado Ganadores de A01 a A06
Cuando el gestor abre /tramites y el OT abre la bandeja
Entonces el mismo dato usa el mismo vocablo (Vendedor/Propietario, Gestor, Secretaría/Organismo, fechas)
Y las columnas compuestas distintas se conservan si A02 es layout RN-07

AC2 — negativo
Dado una columna que solo un rol ve (Marcas, Fuente, Paso, Preasignación)
Cuando se homologan cabeceras
Entonces esa columna no se clona al otro rol

AC3 — borde
Dado Excel export del listado gestor y del OT
Cuando se generan
Entonces las cabeceras exportadas coinciden con las de pantalla para el mismo dato
```

### HU-12690-2 — [#12696](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12696) `[FRONTEND] – Homologación – Labels de detalle y wizard`

| Campo | Valor |
|---|---|
| SP | 5 |
| Depende | HU-12689-1 |

A07–A11, A09, A10.

```gherkin
AC1 — positivo
Dado Ganadores de A07 a A11
Cuando se abre el detalle gestor y el modal/detalle OT del mismo trámite
Entonces secciones y campos compartidos (actores, especificaciones, motor/chasis/serie, capacidad, FUR/expediente) muestran el texto ganador

AC2 — negativo
Dado un campo solo del wizard o solo del OCR LT
Cuando el otro rol no lo ve
Entonces no se exige el mismo rótulo de sección (RN-07)

AC3 — borde
Dado N. Motor en detalle y Nº Motor en wizard
Cuando A09 tiene Ganador
Entonces las dos superficies del gestor y la del OT usan ese único formato
```

### HU-12690-3 — [#12697](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12697) `[FRONTEND] – Homologación – Acciones compartidas y copy interno OT`

| Campo | Valor |
|---|---|
| SP | 3 |
| Depende | HU-12689-1 |

A12, A13, E01, E02.

```gherkin
AC1 — positivo
Dado Ganadores de A12 y A13
Cuando gestor y OT ven Ver consolidado y Exportar
Entonces el label es idéntico en fila, detalle y estado de carga (sin Abriendo… distinto si el ganador no lo permite)

AC2 — negativo
Dado Aprobar / Rechazar / Asignar placa / Enviar al OT
Cuando el otro rol no tiene esa acción
Entonces no se pinta en su menú

AC3 — borde
Dado el menú de fila OT «Aprobar» y el pie de detalle «Aprobar trámite»
Cuando E01/E02 aplican
Entonces el OT usa un solo largo canónico en las dos superficies
```

### HU-12690-4 — [#12698](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12698) `[FRONTEND] – Homologación – KPI y chips de estado de la bandeja OT`

| Campo | Valor |
|---|---|
| SP | 3 |
| Depende | HU-12689-1 |

A14–A16. No reescribir `estados.ts`: B01–B14 cerrados por el PO (2026-09-18).

```gherkin
AC1 — positivo
Dado Ganadores de A14 A15 A16
Cuando el OT mira tarjetas de bandeja y el gestor mira chips
Entonces el estado de negocio sigue el catálogo B
Y la tarjeta «Por decidir» o el plural «Asignados» solo cambian si el Ganador lo pide

AC2 — negativo
Dado un trámite rechazado desde preasignación
Cuando A16 no homologa el distintivo al OT
Entonces el OT no inventa un quinto estado
Y el gestor conserva o retira «Rechazado preasignación» según Ganador

AC3 — borde
Dado el chip de estado en /tramites y en la bandeja
Cuando el status es entregado
Entonces el chip dice Entregado en ambos
Aunque la tarjeta KPI OT tenga otro nombre de cola
```

---

## Feature #12692 — Operación transversal (4 HUs · 12 SP)

### HU-12692-1 — [#12699](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12699) `[FRONTEND] – Homologación – Dock e Identidad`

| Campo | Valor |
|---|---|
| SP | 2 |
| Depende | HU-12689-1 |

A17, B21.

```gherkin
AC1 — positivo
Dado Ganador de A17
Cuando el usuario ve la píldora del dock y el H1 del módulo
Entonces usan el mismo nombre (Identidad o Validaciones, el que gane)

AC2 — negativo
Dado un Admin OT sin validaciones.read
Cuando carga el dock
Entonces Identidad no aparece
Y no se considera conflicto de homologación

AC3 — borde
Dado las píldoras Trámites, Reportes, Usuarios y Ayuda
Cuando OT y gestor las ven
Entonces el label del dock es el mismo (B21)
```

### HU-12692-2 — [#12700](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12700) `[FRONTEND] – Homologación – Palabras de estado en dashboard`

| Campo | Valor |
|---|---|
| SP | 3 |
| Depende | HU-12689-1 |

A18, A19.

```gherkin
AC1 — positivo
Dado Ganadores de A18 y A19
Cuando se abre el dashboard gestor y el dashboard OT
Entonces si un KPI nombra un estado de negocio, usa el catálogo B
Y los títulos de hero solo se unifican si A18 lo pide

AC2 — negativo
Dado métricas distintas (Total trámites vs Esperan mi decisión)
Cuando A19 marca RN-07 de métrica
Entonces no se fuerza el mismo nombre de KPI
Y sí se corrige «Entregados» vs «Entregado» si el Ganador lo unifica

AC3 — borde
Dado «Total Trámites» en dashboard y «Total trámites» en reportes gestor
Cuando E03 aplica
Entonces el casing queda único en el gestor
```

### HU-12692-3 — [#12701](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12701) `[FRONTEND] – Homologación – Títulos de Reportes y Usuarios`

| Campo | Valor |
|---|---|
| SP | 5 |
| Depende | HU-12689-1 |

A20–A23.

```gherkin
AC1 — positivo
Dado Ganadores de A20 a A23
Cuando gestor abre Reportes SPA y OT abre el hub de reportes
Entonces las pestañas que ambos conceptos comparten usan el texto ganador
Y Consultas personalizadas permanece igual si ya coincide

AC2 — negativo
Dado pestañas solo de un rol (Uso del aplicativo, Análisis, Revisores si A21 es N/A)
Cuando el otro no las tiene
Entonces no se clonan

AC3 — borde
Dado el H1 Usuarios vs «Administración OT — Usuarios»
Cuando A23 tiene Ganador
Entonces el título visible al usuario sigue ese Ganador
```

### HU-12692-4 — [#12702](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12702) `[FRONTEND] – Homologación – Términos canónicos en Ayuda`

| Campo | Valor |
|---|---|
| SP | 2 |
| Depende | HU-12689-1 |

A25.

```gherkin
AC1 — positivo
Dado Ganador de A25
Cuando se abre la intro del Centro de Ayuda
Entonces Gestor y Organismo de Tránsito se nombran con los términos canónicos

AC2 — negativo
Dado artículos solo-OT o solo-gestor
Cuando se homologa la intro
Entonces no se reescribe el cuerpo de cada manual de audiencia (RN-07)

AC3 — borde
Dado DR. FLIT (Gestor) y DR. FLIT (OT)
Cuando son audiencias distintas
Entonces el sufijo de audiencia se conserva
```

---

## Feature #12691 — Correos y documentos (2 HUs · 8 SP)

### HU-12691-1 — [#12703](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12703) `[BACKEND] – Homologación – Correos de aprobado y rechazado`

| Campo | Valor |
|---|---|
| SP | 3 |
| Depende | HU-12689-1 |

D01, D02. D03–D05 fuera.

```gherkin
AC1 — positivo
Dado plantillas tramites.aprobado y tramites.rechazado
Cuando se compone el correo
Entonces asunto y cuerpo usan Aprobado y Rechazado del catálogo B
Y no un sinónimo en mayúsculas distinto del Ganador

AC2 — negativo
Dado que el disparador productivo aún no existe
Cuando se implementa esta HU
Entonces se alinea el composer
Y no se inventa un envío masivo nuevo

AC3 — borde
Dado correos de invitación, reset, analítica o Kyverum
Cuando se homologa el trámite
Entonces esas plantillas no se modifican
```

### HU-12691-2 — [#12704](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12704) `[BACKEND] – Homologación – Labels FUR y expediente consolidado`

| Campo | Valor |
|---|---|
| SP | 5 |
| Depende | HU-12689-1, HU-12690-2 |

D06, D07, A05, A24.

```gherkin
AC1 — positivo
Dado Ganadores de A05 y A24
Cuando OT o gestor descarga FUR o consolidado
Entonces organismo y SOAT se nombran con el texto ganador
Y no aparece Secretaría en un PDF y Organismo en el otro para el mismo concepto

AC2 — negativo
Dado un tipo documental que solo un rol ve en UI
Cuando se genera el PDF
Entonces no se exige paridad de una pantalla que no existe (RN-07)
Y sí se exige paridad del artefacto si ambos lo abren

AC3 — borde
Dado el título de consolidado en PDF y el botón de A12
Cuando ambos nombran el expediente
Entonces no se contradicen
```

---

## Orden de implementación

```
12689-1 → 12689-2
       → 12690-1, 12690-2, 12690-3, 12690-4
       → 12692-1, 12692-2, 12692-3, 12692-4
       → 12691-1
12690-2 → 12691-2
```

## DoR de las HUs (al crear)

PASS previsto: título FRONTEND/BACKEND, Como/quiero/para, AC +/–, Fibonacci, Refinement, dependencias, AssignedTo humano, tag DOR, sin TODO.  
Sprint 7 por instrucción explícita (no el «siguiente» de la convención).  
Parent Feature en `New` (no Active): se crea la HU igual; no se activa implementación sin Ganador ni Motivo A.
