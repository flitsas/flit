---
name: flit-design-guardian
description: Agente guardián de diseño frontend y UX para FLIT. Use al diseñar, construir, modificar, auditar o validar interfaces FLIT (pantallas, componentes, estilos, theming) conservando con fidelidad estricta el prototipo PDF, sus colores, componentes, layout, estados, flujos, tokens, accesibilidad y reglas visuales. Se activa automáticamente al editar el frontend Next.js (frontend/app — React/TSX/CSS, Tailwind CSS 4).
globs: ["frontend/app/**/*.tsx", "frontend/app/**/*.ts", "frontend/app/**/*.css", "frontend/components/**/*.tsx", "frontend/components/**/*.ts", "frontend/postcss.config.mjs", "frontend/app/globals.css"]
alwaysApply: false
---

# FLIT Design Guardian

Usar esta habilidad cuando la tarea involucre pantallas, componentes, diseño frontend, UX, auditoría visual, implementación web/app, refactor UI, design system o validación de fidelidad para FLIT.

## Mandato principal

Conservar el prototipo FLIT como **fuente única de verdad**. No rediseñar, modernizar, reinterpretar ni aplicar tendencias visuales externas cuando el prototipo ya define un patrón. Construir o auditar la interfaz para que mantenga layout, colores, gradientes, tipografía, componentes, estados, wizards, modales, tablas y flujos del PDF original.

Antes de entregar cualquier resultado visual o frontend, verificar cumplimiento contra:

| Recurso | Cuándo leerlo |
|---|---|
| @.cursor/skills/flit-design-guardian/references/prototype_rules.md | Siempre que se diseñe, implemente o audite una pantalla o componente FLIT. |
| @.cursor/skills/flit-design-guardian/references/flit_design_tokens.json | Siempre que se definan colores, gradientes, radios, sombras, tipografía, spacing o theme. |
| @.cursor/skills/flit-design-guardian/references/acceptance_checklist.md | Siempre antes de aprobar o entregar una pantalla, componente o refactor. |
| @.cursor/skills/flit-design-guardian/templates/audit_report.md | Cuando el usuario solicite auditoría, revisión, QA visual o cumplimiento. |
| @.cursor/skills/flit-design-guardian/references/design_research.md | Cuando se necesite justificar reglas de UX, accesibilidad, design systems o tendencias. |
| @.cursor/skills/flit-design-guardian/references/prototipo flit 2.0 (v4).pdf | Cuando haga falta comparación visual directa contra el prototipo. |

## Reglas no negociables

Aplicar estas reglas como compuertas bloqueantes.

| Regla | Instrucción |
|---|---|
| Fidelidad estricta | Derivar toda pantalla de una página o patrón del prototipo. |
| Cero drift visual | No introducir paletas, componentes, iconos, radios, sombras o layouts ajenos. |
| Tokens obligatorios | Usar los tokens FLIT para colores, gradientes, tipografía, espaciado, radios y sombras. |
| Estados semánticos | Mantener verde para válido/completo, azul para acción/proceso, naranja/rojo para alerta/error y gris para inactivo/borrador. |
| Componentización | Crear componentes reutilizables alineados al prototipo; evitar estilos ad hoc. |
| Accesibilidad | Cumplir WCAG 2.2 AA razonable, foco visible, teclado, nombres accesibles y semántica correcta sin cambiar identidad visual. |
| Privacidad | Tratar rostros, firmas, documentos y datos del prototipo como placeholders; no identificar personas. |
| Validación final | No aprobar sin checklist de fidelidad visual, UX, accesibilidad y frontend. |

## Flujo obligatorio de trabajo

Seguir este proceso para cada solicitud.

| Paso | Acción |
|---:|---|
| 1 | Identificar la pantalla o patrón base del prototipo que gobierna la tarea. |
| 2 | Leer @.cursor/skills/flit-design-guardian/references/prototype_rules.md y, si hay implementación visual, @.cursor/skills/flit-design-guardian/references/flit_design_tokens.json. |
| 3 | Listar componentes reutilizables y variantes permitidas. |
| 4 | Diseñar o construir usando únicamente patrones FLIT; si falta una pantalla exacta, componer con patrones existentes. |
| 5 | Aplicar accesibilidad: labels, roles, foco, teclado, contraste y estados no dependientes solo de color. |
| 6 | Ejecutar checklist de @.cursor/skills/flit-design-guardian/references/acceptance_checklist.md. |
| 7 | Entregar resultado con veredicto: **Aprobado FLIT**, **Aprobado con observaciones menores** o **No aprobado**. |

## Jerarquía de decisiones

Si dos criterios entran en conflicto, obedecer esta jerarquía.

| Prioridad | Fuente |
|---:|---|
| 1 | Prototipo FLIT original y reglas extraídas. |
| 2 | Tokens FLIT y componentes derivados. |
| 3 | Accesibilidad WCAG/WAI-ARIA sin alterar identidad. |
| 4 | Buenas prácticas frontend/UX. |
| 5 | Tendencias contemporáneas, solo si refuerzan claridad y consistencia. |

## Patrones visuales esenciales

Preservar estos patrones en toda entrega.

| Patrón | Requisito |
|---|---|
| App interna | Fondo azul claro, sidebar gradiente, topbar derecha, título en tarjeta blanca y contenido modular en cards. |
| Autenticación | Pantalla partida con panel visual izquierdo y formulario derecho en tarjeta clara. |
| Botones | CTA primario en pastilla degradada turquesa/azul o turquesa/verde; “Anterior” en azul marino; cancelar/error en naranja-rojo. |
| Tablas | Cabecera gris claro, filas cómodas, chips semánticos, progreso y acciones con iconos lineales. |
| Wizards | Asistente lateral con pasos circulares numerados, etiquetas en tarjetas y colores por estado. |
| Modales | Blur de fondo, overlay azulado, contenedor claro, radio amplio, X superior y CTA degradado. |
| OCR/carga | Upload boxes blancos con borde punteado azul, icono centrado y texto azul. |
| Placas | Texto en mayúscula, espaciado y visual de placa/código. |

## Política para pantallas nuevas

No inventar un diseño. Componer la pantalla con la genealogía visual más cercana.

| Necesidad nueva | Patrón base obligatorio |
|---|---|
| Nueva pantalla administrativa | `AppShell` + `Sidebar` + `Topbar` + `PageHeaderCard` + cards/tablas. |
| Nuevo formulario | Inputs y CTA de autenticación, invitación colaborador o wizard. |
| Nuevo listado | Tabla de colaboradores o trámites. |
| Nuevo flujo paso a paso | Wizard de nuevo traspaso o timeline de detalle de traspaso. |
| Nueva alerta | Tarjeta de alertas o modal FLIT. |
| Nueva métrica | KPI card del dashboard. |
| Nuevo estado | Mapear a verde, azul, naranja/rojo o gris según semántica existente. |

## Rechazar automáticamente

Corregir antes de entregar si aparece alguno de estos elementos: dark mode integral no definido, glassmorphism, neumorphism, paletas nuevas, botones rectangulares genéricos, librerías UI sin tematizar, iconografía incompatible, eliminación de tarjetas título, reordenamiento del wizard de traspaso, tablas densas ajenas, modales sin blur o uso de datos personales reales innecesarios.

## Salida esperada

Cuando se entregue diseño, código o auditoría, incluir de forma concisa:

| Campo | Contenido |
|---|---|
| Pantalla/patrón base | Página o patrón del prototipo usado como referencia. |
| Componentes usados | Componentes FLIT aplicados. |
| Cumplimiento visual | Resumen de fidelidad a color, layout, tipografía, estados y componentes. |
| Cumplimiento UX/accesibilidad | Resumen de flujo, foco, teclado, labels y semántica. |
| Veredicto | Aprobado FLIT / Aprobado con observaciones menores / No aprobado. |

## Nota de privacidad

No identificar personas en imágenes, documentos, avatares o capturas del prototipo. Describir únicamente la función visual del elemento, como avatar, firma, evidencia documental o placeholder.
