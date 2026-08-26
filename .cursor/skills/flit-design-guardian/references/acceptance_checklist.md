# Checklist de aceptación del agente FLIT

Este checklist es bloqueante. Debe usarse antes de entregar cualquier diseño, implementación frontend, refactor visual, componente, pantalla, prototipo interactivo o auditoría relacionada con FLIT.

## Criterios de aprobación

| Dimensión | Aprobación exigida | Bloqueante |
|---|---:|---|
| Trazabilidad al patrón base | 100% de pantallas con patrón base declarado | Sí |
| Tokens de color | 100% de colores desde `flit_design_tokens.json` o justificación documentada | Sí |
| Separación brand/chrome | La paleta `chrome` solo en el dock; la de marca solo en contenido | Sí |
| Gradientes | 100% de CTAs primarios y píldora activa del dock con gradientes autorizados | Sí |
| Componentes | 100% derivados de patrones vigentes, sin duplicación entre flujos | Sí |
| Layout principal | Coincidencia estructural alta con el patrón base | Sí |
| Estados semánticos | Cinco tonos de badge; estados que comparten tono llevan icono y texto | Sí |
| Tipografía | Escala y pesos alineados; **ningún texto bajo 12px** | Sí |
| Formularios | Inputs, labels reales, iconos, radios y CTAs alineados | Sí |
| Modales | Blur, fondo claro, radio amplio, cierre X, CTA degradado, `role="dialog"` y focus trap | Sí |
| Tablas | `<table>` semántica, cabecera, filas, badges, acciones y progreso consistentes | Sí |
| Wizards | Pasos numerados, colores y secuencia vigentes | Sí |
| Tema oscuro | Todo surface oscuro sale de la capa `dark` del token file | Sí |
| Accesibilidad | WCAG 2.2 AA, teclado, foco visible y nombres accesibles | Sí |
| Privacidad | Sin identificación de personas; datos como placeholders | Sí |
| Rendimiento visual | Sin efectos pesados ni modas decorativas ajenas | Condicional |

## Auditoría visual rápida

| Pregunta | Resultado esperado |
|---|---|
| ¿La pantalla conserva fondo azul claro global? | Sí |
| ¿El título principal aparece dentro de tarjeta blanca cuando corresponde? | Sí |
| ¿El dock conserva píldoras redondas, gradiente activo y superficie casi opaca? | Sí |
| ¿La topbar conserva rol, usuario/tenant, notificaciones y menú? | Sí |
| ¿Las tarjetas son blancas, con radio amplio y padding generoso? | Sí |
| ¿Los CTA primarios usan pastilla degradada? | Sí |
| ¿Los botones de **consulta** (Consultar RUNT/RUES/Buscar) usan azul sólido `#557EFF` sin degradado? | Sí |
| ¿Los botones secundarios respetan navy, naranja-rojo o tarjeta blanca? | Sí |
| ¿Los badges usan la forma tintada y no relleno sólido con texto blanco? | Sí |
| ¿El badge `success` es verde tintado (`#F3FBE8`/`#4F7A12`), no cian? | Sí |
| ¿Los formularios usan inputs blancos con iconos, bordes claros y `<label>` real? | Sí |
| ¿Los modales tienen fondo desenfocado, contenedor claro y rol de diálogo? | Sí |
| ¿Las tablas son `<table>` con cabecera `#DFE5ED` y acciones compactas? | Sí |
| ¿El wizard conserva círculos numerados y la secuencia vigente? | Sí |

## Auditoría UX

| Área | Requisito |
|---|---|
| Arquitectura | Mantener navegación por módulos vía dock: dashboard, trámites, reportes, validaciones, usuarios y ayuda |
| Flujo de trámite | Conservar consulta de vehículo, actores y validación, documentos, datos comerciales, FUR y expediente |
| Acciones principales | Visibles, con CTA degradado y texto directo |
| Gestión con datos | Búsqueda y filtros visibles en todo listado |
| Alertas | Visibles, semánticas y coherentes con los tonos definidos |
| Feedback | Modales o tarjetas de éxito/error existentes |
| Datos | Tablas y tarjetas; no listas ni layouts ajenos donde el patrón define tabla |
| Carga documental | Upload boxes con borde punteado, icono y texto centrado |
| Placa | Formato mayúsculo y espaciado |

## Auditoría técnica frontend

| Área | Requisito |
|---|---|
| Componentización | No repetir estilos ad hoc; no duplicar el mismo componente entre dos flujos |
| Tokens | Variables CSS/Tailwind/theme derivadas de `flit_design_tokens.json` |
| Semántica | HTML semántico para formularios, tablas, botones y diálogos |
| ARIA | Solo cuando el HTML nativo no basta; los modales deben tener roles correctos |
| Foco | Anillo de 2px `#557EFF` con offset 2px en todo interactivo |
| Teclado | Dock, menús, modales, botones, wizard y acciones operables por teclado |
| Responsive | La estructura no debe romperse en escritorio ni tablet |
| Dependencias visuales | Toda librería UI debe tematizarse estrictamente; no aceptar defaults |
| Animación | Solo transiciones breves y funcionales, respetando `prefers-reduced-motion` |

## Verificaciones mecánicas

Comprobaciones que no dependen de criterio y conviene correr antes de emitir veredicto.

| Qué se busca | Cómo verificar | Umbral |
|---|---|---|
| Texto bajo el piso tipográfico | `grep -roh "text-\[1[01]px\]"` | 0 |
| Foco eliminado sin sustituto | comparar conteo de `outline-none` contra `focus-visible` | sustituto en el 100% |
| Modales sin rol | comparar `fixed inset-0` contra `role="dialog"` | 1:1 |
| Paleta ajena | `grep -roE "(text\|bg\|border)-(slate\|violet\|amber\|emerald\|rose\|indigo\|zinc\|gray)-[0-9]+"` | 0 |
| Hexes fuera de token | `grep -rohE "#[0-9A-Fa-f]{6}"` y contrastar contra el token file | 0 fuera de tokens |
| Tablas sin semántica | comparar listados de datos contra apariciones de `<table` | 1:1 |
| Surfaces oscuros inventados | buscar hexes oscuros fuera de la capa `dark` | 0 |

## Reglas de corrección

Cuando una entrega falle, corregir antes de entregar. Si el fallo depende de una decisión funcional no especificada, mantener el patrón vigente más cercano y documentar la decisión.

| Falla | Corrección esperada |
|---|---|
| Color no autorizado | Sustituir por token FLIT equivalente |
| Botón sin gradiente | Reemplazar por `GradientButton` o variante autorizada |
| Layout sin tarjeta título | Agregar `PageHeaderCard` |
| Tabla densa, ajena o sin semántica | Rehacer con `<table>`, cabecera de marca, filas cómodas, badges y acciones |
| Modal sin blur o sin rol | Aplicar overlay desenfocado, `role="dialog"`, `aria-modal`, focus trap y Escape |
| Badge sólido con texto blanco | Sustituir por la forma tintada de `color.badge` |
| Estado solo por color | Añadir texto, icono o label accesible |
| Texto bajo 12px | Elevar al piso tipográfico |
| Librería con estilo default | Sobrescribir theme o crear componente FLIT |
| Surface oscuro inventado | Sustituir por la capa `dark` del token file |

## Veredicto requerido

| Veredicto | Significado |
|---|---|
| Aprobado FLIT | Cumple reglas visuales, UX, accesibilidad y frontend |
| Aprobado con observaciones menores | No hay drift bloqueante; quedan ajustes no críticos documentados |
| No aprobado | Existe drift visual, UX o accesibilidad bloqueante; corregir antes de entregar |
