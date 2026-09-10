# Investigación aplicada: frontend, UX y sistemas de diseño para FLIT

Este archivo resume la investigación que el agente debe usar para trabajar con criterio profesional sin desviarse del prototipo. Debe leerse cuando sea necesario justificar reglas de sistema de diseño, accesibilidad, validación frontend o UX.

## Síntesis profesional

El diseño de FLIT debe entenderse como un **sistema de diseño operativo**. La UI gobierna layout, color, tipografía, interacción y fidelidad del prototipo; la UX gobierna arquitectura de información, flujos, validación, pruebas y mejora continua. Esta distinción es importante porque el agente no debe “embellecer” la UI, sino preservar la UI del prototipo mientras mejora implementación, accesibilidad y consistencia.

Las fuentes revisadas coinciden en que una guía frontend madura debe contener componentes modulares, colores, tipografía, responsive layout, reglas de uso, especificaciones y criterios de implementación. También coinciden en que la accesibilidad debe estar integrada desde el sistema de diseño, no añadida al final. En FLIT, esto significa que cada componente debe salir del prototipo y luego recibir semántica, foco, contraste y teclado conforme a WCAG/WAI-ARIA.

## Categorías actuales que debe cubrir el agente

| Categoría | Interpretación práctica para FLIT |
|---|---|
| Sistema de diseño | Transformar el prototipo en componentes, tokens, patrones y reglas reutilizables. |
| Frontend style guide | Documentar implementación, variaciones, estados y dos/don’ts por componente. |
| Design tokens | Mantener una fuente única para colores, gradientes, radios, sombras, tipografía, spacing y estados. |
| UX operativa | Preservar flujos de dashboard, colaboradores, traspasos, OCR, firma, validaciones y modales. |
| Accesibilidad sistémica | Exigir WCAG 2.2 AA como base, foco visible, navegación por teclado, labels y semántica ARIA cuando aplique. |
| Arquitectura de información | Mantener dock de navegación, topbar, título en tarjeta, cards, tablas, filtros, wizards y timelines. |
| IA como auditor | Usar IA para detectar drift visual, documentar decisiones y revisar checklist, no para crear un estilo nuevo. |
| Rendimiento frontend | Evitar efectos pesados, visual noise y dependencias visuales sin tematizar. |
| Privacidad | Tratar datos personales, firmas, avatares y documentos como placeholders visuales. |

## Principios investigados que aplican

| Principio | Fuente conceptual | Regla FLIT |
|---|---|---|
| UI como apariencia e interacción | Figma | Controlar layout, colores, tipografía, botones, inputs y fidelidad visual. |
| UX como experiencia total | Figma | Mantener arquitectura de información, flujos, pruebas y consistencia. |
| Guía frontend viva | Nielsen Norman Group | Centralizar componentes para evitar inconsistencias en desarrollo. |
| Componentes modulares | Nielsen Norman Group | Usar `AppShell`, `Dock`, `Topbar`, `DataTable`, `WizardStepTracker`, `Modal`, etc. |
| WCAG 2.2 | W3C/WAI | Implementar criterios verificables de accesibilidad bajo principios perceptible, operable, comprensible y robusto. |
| ARIA APG | W3C/WAI | Aplicar roles, estados, nombres accesibles y soporte de teclado a widgets. |
| Accesibilidad desde el sistema | UXPin | Validar colores, tipografía, spacing, tamaños y componentes antes de pantallas completas. |
| Tokens interoperables | DTCG/W3C | Escalar decisiones visuales de FLIT entre diseño y código. |
| Evitar modas sin utilidad | Lyssna | Rechazar glassmorphism, sobrecomplejidad, IA sin caso de uso y rediseños visuales. |

## Reglas de accesibilidad que no deben alterar el prototipo

La accesibilidad debe reforzar el prototipo, no reemplazarlo. Si un color exacto del prototipo no cumple contraste en un uso específico, el agente debe ajustar el uso semántico de texto, peso, tamaño, fondo o estado dentro de la paleta FLIT antes de introducir colores ajenos.

| Área | Requisito |
|---|---|
| Contraste | Apuntar a WCAG 2.2 AA: 4.5:1 en texto normal y 3:1 en texto grande o componentes gráficos relevantes. |
| Foco | Todo botón, link, campo, select, tab, modal y acción de tabla debe tener foco visible coherente con azul FLIT. |
| Teclado | Modales, formularios, tablas con acciones, menús y wizards deben ser operables por teclado. |
| Labels | Inputs, upload boxes, plate inputs, búsqueda y selects deben tener label o nombre accesible. |
| Modales | Usar `role="dialog"`, `aria-modal="true"`, focus trap y retorno de foco al cerrar. |
| Tablas | Usar headers semánticos, captions/labels y acciones con nombres accesibles. |
| Estados | No depender solo del color; acompañar con texto, icono o label. |
| Documentos/imágenes | Incluir alt descriptivo funcional; no describir ni identificar personas. |

## Tendencias 2026 aceptadas y rechazadas

| Tendencia | Veredicto FLIT | Motivo |
|---|---|---|
| IA como colaborador | Aceptada | Útil para auditoría, checklist y validación de consistencia. |
| Agentes que ejecutan tareas | Aceptada con control | Puede guiar flujos de trámite, pero sin cambiar UI base. |
| IA para accesibilidad | Aceptada | Ayuda a detectar contraste, labels y semántica. |
| Transparencia en IA | Aceptada | Si se agrega IA OCR/validación, debe explicar estado y resultado. |
| Microinteracciones | Aceptada con moderación | Solo hover, focus, carga, progreso y modales suaves. |
| Interfaces multimodales | Condicional | Solo si el producto lo requiere y se integra con patrones FLIT. |
| Glassmorphism | Rechazada | No pertenece al prototipo y afecta legibilidad. |
| Sobrecarga visual | Rechazada | FLIT requiere claridad operativa. |
| IA sin caso de uso | Rechazada | El prototipo ya define OCR/validación; no agregar widgets innecesarios. |
| Rediseños estéticos de moda | Rechazados | Rompen fidelidad visual. |

## Referencias

[1]: https://www.figma.com/resource-library/difference-between-ui-and-ux/ "Figma — What is the difference between UI and UX?"
[2]: https://www.nngroup.com/articles/front-end-style-guides/ "Nielsen Norman Group — Front-End Style-Guides: Definition, Requirements, Component Checklist"
[3]: https://www.w3.org/WAI/standards-guidelines/wcag/ "W3C WAI — WCAG 2 Overview"
[4]: https://www.w3.org/WAI/ARIA/apg/ "W3C WAI — ARIA Authoring Practices Guide"
[5]: https://www.uxpin.com/studio/blog/design-system-accessibility/ "UXPin — Design System Accessibility"
[6]: https://www.designtokens.org/ "Design Tokens Community Group"
[7]: https://www.w3.org/community/design-tokens/2025/10/28/design-tokens-specification-reaches-first-stable-version/ "W3C — Design Tokens specification reaches first stable version"
[8]: https://www.lyssna.com/blog/ux-design-trends/ "Lyssna — UX design trends 2026"
