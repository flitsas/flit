---
name: flit-design-guardian
description: Agente guardián de diseño frontend y UX para FLIT. Use al diseñar, construir, modificar, auditar o validar interfaces FLIT (pantallas, componentes, estilos, theming) conservando con fidelidad la línea base vigente: tokens, dock de navegación, componentes, layout, estados, flujos, accesibilidad y reglas visuales. Se activa automáticamente al editar el frontend Next.js (frontend/app — React/TSX/CSS, Tailwind CSS 4).
globs: ["frontend/app/**/*.tsx", "frontend/app/**/*.ts", "frontend/app/**/*.css", "frontend/components/**/*.tsx", "frontend/components/**/*.ts", "frontend/postcss.config.mjs", "frontend/app/globals.css"]
alwaysApply: false
---

# FLIT Design Guardian

Usar esta habilidad cuando la tarea involucre pantallas, componentes, diseño frontend, UX, auditoría visual, implementación web/app, refactor UI, design system o validación de fidelidad para FLIT.

## Mandato principal

Conservar la **línea base vigente de FLIT** como fuente de verdad. No rediseñar, modernizar, reinterpretar ni aplicar tendencias visuales externas cuando ya existe un patrón. Construir o auditar la interfaz para que mantenga layout, colores, gradientes, tipografía, componentes, estados, wizards, modales, tablas y flujos vigentes.

La autoridad está repartida y en este orden:

| Prioridad | Fuente | Gobierna |
|---:|---|---|
| 1 | `frontend/app/globals.css` en `develop` | Valores reales |
| 2 | `references/flit_design_tokens.json` | Los mismos valores, documentados |
| 3 | `references/prototipo flit 2.0 (v4).pdf` | Composición y flujo, **no** valores |

El PDF dejó de ser autoridad sobre color y navegación; sigue siéndolo sobre cómo se compone una pantalla y en qué orden ocurre un flujo.

Antes de entregar cualquier resultado visual o frontend, verificar cumplimiento contra:

| Recurso | Cuándo leerlo |
|---|---|
| @.cursor/skills/flit-design-guardian/references/prototype_rules.md | Siempre que se diseñe, implemente o audite una pantalla o componente FLIT. |
| @.cursor/skills/flit-design-guardian/references/flit_design_tokens.json | Siempre que se definan colores, gradientes, radios, sombras, tipografía, spacing o theme. |
| @.cursor/skills/flit-design-guardian/references/acceptance_checklist.md | Siempre antes de aprobar o entregar una pantalla, componente o refactor. |
| @.cursor/skills/flit-design-guardian/templates/audit_report.md | Cuando el usuario solicite auditoría, revisión, QA visual o cumplimiento. |
| @.cursor/skills/flit-design-guardian/references/design_research.md | Cuando se necesite justificar reglas de UX, accesibilidad, design systems o tendencias. |
| @.cursor/skills/flit-design-guardian/references/prototipo flit 2.0 (v4).pdf | Cuando haga falta comparar **composición** contra el prototipo. |

## Reglas no negociables

Aplicar estas reglas como compuertas bloqueantes.

| Regla | Instrucción |
|---|---|
| Fidelidad estricta | Derivar toda pantalla de un patrón vigente. |
| Cero drift visual | No introducir paletas, componentes, iconos, radios, sombras o layouts ajenos. |
| Tokens obligatorios | Usar los tokens FLIT para colores, gradientes, tipografía, espaciado, radios y sombras. En especial: la escala `slate-*` de Tailwind **no** es paleta FLIT. |
| Dos capas de paleta | `brand` para contenido, `chrome` para el dock. Conviven a propósito; no fusionarlas sin decisión explícita. |
| Estados semánticos | Cinco tonos de badge. Siete estados de trámite mapeados sobre ellos; donde dos comparten tono, exigir icono y texto. |
| Componentización | Crear componentes reutilizables; evitar estilos ad hoc y duplicación entre flujos. |
| Accesibilidad | WCAG 2.2 AA: foco visible, teclado, nombres accesibles, semántica correcta, contraste 4.5:1 y piso tipográfico de 12px, sin cambiar identidad visual. |
| Privacidad | Tratar rostros, firmas, documentos y datos como placeholders; no identificar personas. |
| Validación final | No aprobar sin checklist de fidelidad visual, UX, accesibilidad y frontend. |

## Flujo obligatorio de trabajo

| Paso | Acción |
|---:|---|
| 1 | Identificar la pantalla o patrón base que gobierna la tarea. |
| 2 | Leer `prototype_rules.md` y, si hay implementación visual, `flit_design_tokens.json`. |
| 3 | Listar componentes reutilizables y variantes permitidas. |
| 4 | Diseñar o construir usando únicamente patrones FLIT; si falta una pantalla exacta, componer con patrones existentes. |
| 5 | Aplicar accesibilidad: labels, roles, foco, teclado, contraste y estados no dependientes solo de color. |
| 6 | Ejecutar checklist de `acceptance_checklist.md`. |
| 7 | Entregar resultado con veredicto: **Aprobado FLIT**, **Aprobado con observaciones menores** o **No aprobado**. |

## Jerarquía de decisiones

| Prioridad | Fuente |
|---:|---|
| 1 | Línea base vigente (`globals.css`) y tokens FLIT |
| 2 | Componentes derivados y patrones documentados |
| 3 | Accesibilidad WCAG/WAI-ARIA sin alterar identidad |
| 4 | Composición y flujo del prototipo v4 |
| 5 | Buenas prácticas frontend/UX |
| 6 | Tendencias contemporáneas, solo si refuerzan claridad y consistencia |

## Patrones visuales esenciales

| Patrón | Requisito |
|---|---|
| App interna | Fondo azul claro, **dock inferior flotante**, topbar derecha, título en tarjeta blanca y contenido modular en cards. |
| Autenticación | Pantalla partida con panel visual izquierdo y formulario derecho en tarjeta clara. |
| Botones | CTA primario en pastilla degradada `#557EFF → #00DBD5`; cierre en `#00DBD5 → #8CC63F`; «Anterior» en navy; cancelar/error en naranja-rojo. |
| Tablas | `<table>` semántica, cabecera `#DFE5ED`, filas cómodas, badges tintados, progreso y acciones con iconos lineales. |
| Wizards | Stepper horizontal con pasos circulares numerados y colores por estado. |
| Modales | Blur de fondo, overlay azulado, contenedor claro, radio amplio, X superior, CTA degradado, `role="dialog"` y focus trap. |
| OCR/carga | Upload boxes blancos con borde punteado azul, icono centrado y texto azul. |
| Placas | Texto en mayúscula, espaciado y visual de placa/código. |

## Política para pantallas nuevas

No inventar un diseño. Componer con la genealogía visual más cercana.

| Necesidad nueva | Patrón base obligatorio |
|---|---|
| Nueva pantalla administrativa | `AppShell` + `Dock` + `Topbar` + `PageHeaderCard` + cards/tablas |
| Nuevo formulario | Inputs y CTA de autenticación, invitación colaborador o wizard |
| Nuevo listado | Tabla de colaboradores o trámites |
| Nuevo flujo paso a paso | Wizard con `WizardStepTracker` o timeline de detalle |
| Nueva alerta | Tarjeta de alertas o modal FLIT |
| Nueva métrica | KPI card del dashboard |
| Nuevo estado | Mapear a uno de los cinco tonos de badge; si comparte tono con otro estado, añadir icono y texto |

## Rechazar automáticamente

Corregir antes de entregar si aparece alguno de estos:

- Superficie translúcida con blur fuera de las dos excepciones autorizadas (`--nav-vidrio` del dock y overlay de modal)
- Neumorphism
- Librería UI sin tematizar (shadcn/ui, Radix u otra con tokens por defecto)
- Surface oscuro fuera de la capa `dark` del token file
- Paletas nuevas, incluida la escala `slate-*` de Tailwind
- Botones rectangulares o planos como CTA primario
- Fondos blancos puros en app interna
- Texto por debajo de 12px, u opacidad bajo 0.7 sobre texto
- `outline-none` sin foco sustituto
- Modal sin `role="dialog"` y focus trap
- Grilla de `div` sustituyendo una tabla de datos
- Badge sólido con texto blanco
- Iconografía de otra familia
- Eliminación de tarjetas título
- Reordenamiento del wizard sin HU que lo respalde
- Reintroducción de la sidebar vertical
- Uso de datos personales reales innecesarios

## Salida esperada

| Campo | Contenido |
|---|---|
| Pantalla/patrón base | Patrón vigente usado como referencia |
| Componentes usados | Componentes FLIT aplicados |
| Cumplimiento visual | Fidelidad a color, layout, tipografía, estados y componentes |
| Cumplimiento UX/accesibilidad | Flujo, foco, teclado, labels, contraste y semántica |
| Veredicto | Aprobado FLIT / Aprobado con observaciones menores / No aprobado |

## Nota de privacidad

No identificar personas en imágenes, documentos, avatares o capturas. Describir únicamente la función visual del elemento: avatar, firma, evidencia documental o placeholder.
