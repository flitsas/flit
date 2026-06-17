# Checklist de aceptación del agente FLIT

Este checklist es bloqueante. Debe usarse antes de entregar cualquier diseño, implementación frontend, refactor visual, componente, pantalla, prototipo interactivo o auditoría relacionada con FLIT.

## Criterios de aprobación

| Dimensión | Aprobación exigida | Bloqueante |
|---|---:|---|
| Trazabilidad al prototipo | 100% de pantallas con patrón base declarado | Sí |
| Tokens de color | 100% de colores desde `flit_design_tokens.json` o justificación documentada | Sí |
| Gradientes | 100% de CTAs/sidebar usando gradientes FLIT autorizados | Sí |
| Componentes | 100% de componentes derivados de patrones del prototipo | Sí |
| Layout principal | Coincidencia estructural alta con pantalla base | Sí |
| Estados semánticos | Verde/azul/naranja-rojo/gris aplicados según reglas | Sí |
| Tipografía | Escala y pesos alineados al sistema FLIT | Sí |
| Formularios | Inputs, labels, iconos, radios y CTAs alineados al prototipo | Sí |
| Modales | Blur, fondo claro, radio amplio, cierre X y CTA degradado | Sí |
| Tablas | Cabecera, filas, chips, acciones y progreso consistentes | Sí |
| Wizards | Pasos numerados, colores y progresión iguales al prototipo | Sí |
| Accesibilidad | WCAG 2.2 AA razonable, teclado, foco y nombres accesibles | Sí |
| Privacidad | Sin identificación de personas; datos como placeholders | Sí |
| Rendimiento visual | Sin efectos pesados ni modas decorativas ajenas | Condicional |

## Auditoría visual rápida

| Pregunta | Resultado esperado |
|---|---|
| ¿La pantalla conserva fondo azul claro global? | Sí. |
| ¿El título principal aparece dentro de tarjeta blanca cuando corresponde? | Sí. |
| ¿La sidebar conserva gradiente turquesa/azul y radio derecho amplio? | Sí. |
| ¿La topbar conserva rol, usuario/tenant, notificaciones y menú? | Sí. |
| ¿Las tarjetas son blancas, con radio amplio y padding generoso? | Sí. |
| ¿Los botones primarios usan pastilla degradada? | Sí. |
| ¿Los botones secundarios respetan azul marino, rojo/naranja o tarjeta blanca? | Sí. |
| ¿Los estados usan la semántica cromática del prototipo? | Sí. |
| ¿Los formularios usan inputs blancos con iconos y bordes claros? | Sí. |
| ¿Los modales tienen fondo desenfocado y contenedor claro? | Sí. |
| ¿Las tablas mantienen cabecera gris claro y acciones compactas? | Sí. |
| ¿Los wizards conservan círculos numerados y tarjetas de paso? | Sí. |

## Auditoría UX

| Área | Requisito |
|---|---|
| Arquitectura | Mantener navegación por módulos: dashboard, colaboradores, traspasos y usuarios según prototipo. |
| Flujo de traspaso | Conservar consulta vehículo, validaciones, SOAT/RTM, vendedor/comprador, documentos OCR, validaciones ID y finalización. |
| Acciones principales | Deben aparecer con CTA degradado y texto directo. |
| Alertas | Deben ser visibles, semánticas y coherentes con rojo/naranja o verde. |
| Feedback | Debe usar modales o tarjetas de éxito/error existentes. |
| Datos | Usar tablas y tarjetas; no usar listas o layouts ajenos si el prototipo define tabla. |
| Carga documental | Mantener upload boxes con borde punteado, icono y texto centrado. |
| Placa | Mantener formato mayúsculo y espaciado. |

## Auditoría técnica frontend

| Área | Requisito |
|---|---|
| Componentización | No repetir estilos ad hoc; crear componentes reutilizables. |
| Tokens | Variables CSS/Tailwind/theme deben derivar de `flit_design_tokens.json`. |
| Semántica | Usar HTML semántico para formularios, tablas, botones y diálogos. |
| ARIA | Usar ARIA solo cuando HTML nativo no sea suficiente; modales deben tener roles correctos. |
| Foco | Todo elemento interactivo debe tener foco visible dentro de la estética FLIT. |
| Teclado | Menús, modales, botones, wizard y acciones deben operarse con teclado. |
| Responsive | La estructura no debe romperse en escritorio/tablet; móvil debe adaptarse con patrones conservadores si se solicita. |
| Dependencias visuales | Si se usa librería UI, debe tematizarse estrictamente; no aceptar defaults visuales. |
| Animación | Solo transiciones breves y funcionales: hover, focus, modal, progreso y carga. |

## Reglas de corrección

Cuando una entrega falle, el agente debe corregir antes de entregar. Si el fallo depende de una decisión funcional no especificada, debe mantener el patrón más cercano del prototipo y documentar la decisión.

| Falla | Corrección esperada |
|---|---|
| Color no autorizado | Sustituir por token FLIT equivalente. |
| Botón sin gradiente | Reemplazar por `GradientButton` o variante autorizada. |
| Layout sin tarjeta título | Agregar `PageHeaderCard` si la pantalla interna lo requiere. |
| Tabla densa o ajena | Rehacer con cabecera gris, filas cómodas, chips y acciones. |
| Modal sin blur | Aplicar overlay desenfocado y contenedor `#EEF5FF`. |
| Wizard horizontal genérico | Sustituir por asistente lateral si se trata de nuevo traspaso. |
| Estado solo por color | Añadir texto, icono o label accesible. |
| Librería con estilo default | Sobrescribir theme o crear componente FLIT. |

## Veredicto requerido

Todo reporte de auditoría debe cerrar con uno de estos veredictos.

| Veredicto | Significado |
|---|---|
| Aprobado FLIT | Cumple reglas visuales, UX, accesibilidad y frontend. |
| Aprobado con observaciones menores | No hay drift bloqueante; quedan ajustes no críticos documentados. |
| No aprobado | Existe drift visual, UX o accesibilidad bloqueante; corregir antes de entregar. |
