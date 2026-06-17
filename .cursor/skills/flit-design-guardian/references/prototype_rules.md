# Reglas visuales y UX del prototipo FLIT

Este archivo convierte el PDF `prototipo_flit_original.pdf` en reglas de implementación. Debe leerse cuando una tarea solicite diseñar, construir, modificar, auditar o validar pantallas frontend de FLIT.

## Fuente única de verdad

El prototipo FLIT es la autoridad máxima. El agente no debe rediseñar, reinterpretar ni modernizar la interfaz si el PDF ya define un patrón. Las tendencias externas solo sirven para accesibilidad, documentación, consistencia y validación, nunca para cambiar colores, layout, jerarquía o estilo.

> Regla central: toda pantalla nueva debe declarar su pantalla base o patrón base del prototipo. Si no existe pantalla exacta, debe componerse con componentes ya presentes en el prototipo.

## Identidad visual global

| Elemento | Regla obligatoria |
|---|---|
| Fondo global | Usar azul muy claro aproximado `#EAF2FF` / `#EEF5FF`; evitar blanco puro como fondo de app interna. |
| Tarjetas | Usar fondo blanco, radio amplio, borde o sombra suave y padding generoso. |
| Sidebar | Usar gradiente vertical turquesa a azul, iconos lineales blancos, radio derecho amplio y modo compacto/expandido según pantalla. |
| Topbar | Ubicar a la derecha toggle, campana, rol, usuario/tenant y menú de tres puntos. |
| Títulos | Presentar títulos principales dentro de tarjeta blanca, color azul medio, peso semibold/bold. |
| Botones primarios | Usar pastilla grande con gradiente turquesa → azul o turquesa → verde, texto blanco. |
| Botones secundarios | Usar azul marino para “Anterior”, blanco/borde para opciones, rojo/naranja para cancelar o alerta. |
| Iconografía | Usar iconos lineales simples; no introducir iconos sólidos decorativos incompatibles. |
| Estados | Verde = válido/completo; azul = acción/activo/proceso; naranja/rojo = alerta/error/pendiente crítico; gris = inactivo/borrador. |
| Modales | Usar fondo desenfocado azulado, contenedor `#EEF5FF`, radio amplio, cierre X y CTA degradado centrado. |

## Pantallas y patrones base

| Página del PDF | Pantalla/patrón | Reglas que gobierna |
|---:|---|---|
| 1 | Login | Pantalla partida: panel visual izquierdo con gradiente/imagen y formulario derecho en tarjeta clara; campos con iconos; CTA degradado. |
| 2 | Registro | Misma estructura de autenticación; formulario con campos agrupados, labels claros, botón degradado. |
| 3 | Seguridad/contraseña | Mantener composición auth; mensajes de seguridad y acciones principales con estética FLIT. |
| 4 | Dashboard | AppShell con sidebar, topbar, título en tarjeta, tarjetas KPI, gráficos y bloques de actividad. |
| 5 | Dashboard/operación | Reforzar estructura de métricas, tarjetas, gráficos y módulos en fondo azul claro. |
| 6 | Menú lateral expandido | Sidebar con logo, módulos, iconos, labels y gradiente; patrón para navegación principal. |
| 7 | Gestión de colaboradores | Tabla con filtros, búsqueda, botón “Nuevo colaborador”, chips de estado y acciones por fila. |
| 8 | Detalle/edición colaborador | Tarjetas de información, formularios, opciones de rol/acceso y acciones. |
| 9 | Modal invitar colaborador | Modal dos columnas, opciones tipo tarjeta, selección verde, botón degradado y blur de fondo. |
| 10 | Estados/listados administrativos | Continuidad de tablas, tarjetas y acciones. |
| 11 | Gestión integral de traspasos | Embudo horizontal de estados, alertas importantes, tabla de trámites y acciones “Nuevo Traspaso”, OCR y búsqueda por placa. |
| 12 | Detalle de traspaso | Tarjeta vehículo, timeline horizontal, validación documental, propietario/comprador y firmas. |
| 13 | Modal consulta vehículo | Modal pequeño con campo placa espaciado y botón “Consultar”. |
| 14 | Wizard nuevo traspaso: consulta | Asistente lateral con pasos, datos del vehículo, propietario, multas y tipo de traspaso. |
| 15 | Wizard SOAT/RTM/OCR | Tarjetas de SOAT/RTM, upload boxes con borde punteado, botones “Anterior” y “Continuar”. |
| 16 | Wizard vendedor/comprador incompleto | Tarjetas vendedor/comprador, validaciones, botón “Enviar Validación”, guardar borrador. |
| 17 | Wizard completado | Todos los pasos verdes, comprador diligenciado, validaciones completas y botón “Finalizar”. |
| 18 | Modal traspaso completado | Modal éxito con placa espaciada, mensaje verde y CTA “Ir al Dashboard”. |
| 19 | Modal invitar colaborador | Confirmación del patrón modal reutilizable. |

## Componentes obligatorios

| Familia | Componentes permitidos/esperados |
|---|---|
| Layout | `AppShell`, `AuthShell`, `Sidebar`, `Topbar`, `PageHeaderCard`, `ContentGrid`, `SecurityFooter`. |
| Acciones | `GradientButton`, `NavyButton`, `DangerButton`, `IconButton`, `ActionMenu`. |
| Datos | `KpiCard`, `StatusChip`, `ProgressBar`, `DataTable`, `FunnelStatusBar`, `SearchInput`. |
| Formularios | `TextInput`, `PasswordInput`, `PlateInput`, `SelectCard`, `DateRangeField`, `UploadBox`. |
| Trámites | `WizardSidebar`, `WizardStep`, `TimelineProcess`, `VehicleCard`, `PersonCard`, `ValidationCard`, `SignatureEvidence`. |
| Feedback | `AlertCard`, `VehicleQueryModal`, `InviteCollaboratorModal`, `SuccessModal`, `Toast`. |

## Reglas de layout

La aplicación interna siempre debe usar una composición de fondo azul claro, sidebar a la izquierda y topbar superior derecha. El contenido debe organizarse en tarjetas blancas con grillas amplias. El título de la pantalla no debe ir flotando directamente sobre el fondo; debe ubicarse en una tarjeta blanca cuando el prototipo lo hace.

| Caso | Regla |
|---|---|
| Pantalla interna estándar | Sidebar + topbar + `PageHeaderCard` + tarjetas/tablas. |
| Gestión con datos | Acciones arriba, búsqueda/filtros visibles y tabla con chips. |
| Detalle de entidad | Tarjetas blancas en columnas con información y validaciones. |
| Flujo largo | Usar wizard lateral o timeline horizontal según patrón existente. |
| Confirmación | Usar modal con blur; no navegar a pantallas de éxito visualmente ajenas. |

## Reglas de color por estado

| Estado | Color semántico | Uso en el prototipo |
|---|---|---|
| Completado/válido/aprobado | Verde `#70CF3A` aproximado | Steps completados, checks, estado vigente, finalizar. |
| Acción principal/proceso | Azul medio `#4F74C9` aproximado | Botones OCR, estados activos, iconos de proceso. |
| Alerta/pendiente crítico | Naranja-rojo `#F05A35` aproximado | Multas, firma pendiente, documentos OCR activos, cancelar. |
| Rechazado/error | Rojo `#E43D30` aproximado | Rechazados, no validado, errores. |
| Borrador/inactivo | Gris azulado `#59677D` aproximado | Borrador, pasos pendientes, textos auxiliares. |
| Fondo | Azul muy claro `#EAF2FF` / `#EEF5FF` | Fondo de app y modales. |
| Texto principal | Azul marino `#162744` aproximado | Títulos secundarios, labels, botón anterior. |

## Reglas de formularios

Los formularios deben usar campos blancos con bordes claros y radios medios. Los iconos se ubican dentro del input o al inicio. Las labels usan azul marino y peso semibold. El campo de placa debe mostrar caracteres en mayúscula, espaciados y con apariencia de placa/código, como `A B C 1 2 3`.

| Componente | Regla específica |
|---|---|
| Input de búsqueda | Icono lupa a la izquierda y placeholder claro. |
| Input de placa | Mayúsculas, tracking alto, centrado si está en modal. |
| Select tipo tarjeta | Opción seleccionada en verde; opciones no seleccionadas blancas con borde. |
| Upload box | Borde punteado azul, icono centrado, texto azul, fondo blanco. |
| Password | Icono de ojo/seguridad si aplica; conservar patrón de autenticación. |

## Reglas de tablas

Las tablas del prototipo usan cabecera gris claro, filas blancas, separación limpia, chips y acciones compactas. No usar tablas densas tipo sistema operativo ni grids con bordes fuertes.

| Elemento | Regla |
|---|---|
| Cabecera | Fondo gris muy claro, texto azul/gris, peso semibold. |
| Filas | Fondo blanco, altura cómoda, separación suave. |
| Chips | Radio completo y color semántico. |
| Acciones | Ojo, lápiz, menú de tres puntos u otros iconos lineales pequeños. |
| Progreso | Barra horizontal con color semántico y porcentaje si aplica. |

## Reglas de wizards y trámites

El wizard de traspaso es un patrón crítico. Debe conservar el asistente lateral con círculos numerados, tarjetas de etiqueta y colores por estado. No reemplazar por stepper horizontal genérico salvo que el prototipo lo use en detalle de traspaso.

| Estado de paso | Visual esperado |
|---|---|
| Completado | Círculo verde y texto/etiqueta verde. |
| Activo normal | Círculo azul o rojo/naranja según fase crítica, etiqueta coloreada. |
| Pendiente | Círculo azul/gris oscuro y etiqueta azul/gris. |
| Finalizado | Todos los pasos verdes; CTA final en gradiente turquesa/verde. |

## Reglas de modales

Los modales deben desenfocar la pantalla de fondo y superponer un contenedor claro con radio amplio. El cierre X debe estar arriba a la derecha. El CTA principal debe usar gradiente. Los modales pequeños centrados se usan para consulta de vehículo y éxito; el modal ancho se usa para invitar colaborador.

| Modal | Regla |
|---|---|
| Consulta vehículo | Título, subtítulo, placa espaciada, botón “Consultar”. |
| Traspaso completado | Título de éxito, placa asociada, mensaje verde, botón “Ir al Dashboard”. |
| Invitar colaborador | Dos columnas, inputs, rol, tipo de acceso, fecha, aviso de acceso y botón “Enviar Instrucciones”. |

## Reglas de privacidad

Los rostros, documentos, firmas y datos personales del prototipo son placeholders visuales. El agente puede describir su función visual, por ejemplo “avatar”, “firma”, “evidencia documental” o “imagen de documento”, pero no debe identificar personas ni inferir datos biométricos. En implementaciones reales, usar datos simulados o anonimizados salvo instrucción explícita y legítima del usuario.

## Reglas de rechazo automático

| Rechazar si aparece | Motivo |
|---|---|
| Dark mode integral no definido | Cambia fondo, tarjetas y tono del prototipo. |
| Glassmorphism/neumorphism | No pertenece al sistema FLIT y puede afectar legibilidad. |
| Librería visual sin tematizar | Introduce radios, colores y espaciados ajenos. |
| Botones rectangulares o colores planos para CTAs | Rompe patrón de pastillas degradadas. |
| Fondos blancos puros en app interna | Rompe la atmósfera visual azul clara. |
| Nueva paleta sin tokens FLIT | Rompe la identidad. |
| Reordenamiento de wizard | Rompe UX operativa y trazabilidad. |
| Iconos de otro estilo | Rompe consistencia visual. |
