# Reglas visuales y UX de FLIT

Este archivo convierte la línea base vigente de FLIT en reglas de implementación. Debe leerse cuando una tarea solicite diseñar, construir, modificar, auditar o validar pantallas frontend.

## Fuente de verdad

La jerarquía es esta, y en este orden:

| Prioridad | Fuente | Qué gobierna |
|---:|---|---|
| 1 | `frontend/app/globals.css` en `develop` | Valores reales: color, radios, sombras, motion, tema oscuro |
| 2 | `flit_design_tokens.json` | Los mismos valores, documentados y auditables |
| 3 | `prototipo flit 2.0 (v4).pdf` | **Composición y flujo**, no valores |
| 3b | `20-agosto-notas-diseno-mi-traspasos.pdf` | **Botones de consulta y colores de badges/etiquetas** en el wizard MI/Traspaso — manda sobre el prototipo v4 cuando hay contradicción explícita (verde OK en vez de cian, consulta sólida en vez de degradada) |

> El PDF dejó de ser autoridad sobre color y navegación. Sigue siendo autoridad sobre **cómo se compone una pantalla**: qué información va junta, qué jerarquía tiene y en qué orden ocurre un flujo. Cuando el PDF y el token file discrepen en un valor, manda el token file. Cuando discrepen en una composición, manda el PDF.

> **Excepción para wizard MI/Traspaso:** las anotaciones del PDF 20 ago mandan sobre el prototipo v4 en dos áreas específicas: (a) colores de badges/etiquetas (`success = verde`, **NO cian**), y (b) jerarquía de botones de acción (consulta sólida vs CTA degradado). Fuera del wizard siguen vigentes las reglas generales.

> Regla central, sin cambios: toda pantalla nueva debe declarar su patrón base. Si no existe pantalla exacta, debe componerse con patrones ya existentes.

## Identidad visual global

| Elemento | Regla obligatoria |
|---|---|
| Fondo global | `#EEF5FF` más el doble radial-gradient de `.app-bg`. En oscuro, `#0A1428`. Evitar blanco puro como fondo de app interna. |
| Tarjetas | Fondo blanco (oscuro: `#162744`), radio 18px, borde `#DFE5ED` o sombra suave, padding generoso. |
| **Navegación principal** | **Dock inferior flotante** centrado, píldoras redondas, FAB central de marca. Ver Contrato D. |
| Topbar | Logo a la izquierda; a la derecha toggle de tema, campana, rol, usuario/tenant y menú de tres puntos. |
| Títulos | Título principal dentro de tarjeta blanca, color `#557EFF`, peso bold. |
| Botones primarios (CTA avance/cierre) | Pastilla con gradiente `#557EFF → #00DBD5`, texto blanco, radio 999px. Solo para: Continuar, Radicar, Finalizar. |
| **Botones de consulta** | **Azul sólido `#557EFF` SIN degradado** (PDF 20 ago). Aplica a: Consultar RUNT, Consultar RUES, Buscar, Actualizar (consulta). Implementar con `style={{ background: '#557EFF' }}` + clase Tailwind `bg-[#557EFF]`. Ver §Jerarquía de botones. |
| Botones secundarios | Navy `#162744` para «Anterior», blanco con borde para opciones, `#FF4E00` para cancelar o alerta. |
| Iconografía | Iconos lineales (lucide-react). No mezclar familias ni introducir iconos sólidos decorativos. |
| Estados | Verde = válido/completo; azul = acción/proceso; ámbar = pendiente; naranja-rojo = alerta/rechazo; gris = inactivo/borrador. |
| Modales | Overlay `rgba(22,39,68,0.45)` con `blur(6px)`, contenedor claro, radio amplio, cierre X arriba a la derecha, CTA degradado. |

## Paleta: dos capas que conviven

FLIT usa **dos** familias de azul/cian a propósito. No son un error y no deben fusionarse sin decisión explícita del Líder Técnico, porque unificarlas cambia píxeles en producción.

| Capa | Valores | Dónde se usa |
|---|---|---|
| **Marca** | `#557EFF` · `#00DBD5` · `#8CC63F` · `#FF4E00` · `#162744` · `#DFE5ED` | Contenido: CTAs, títulos, chips, KPIs, wizard, tablas |
| **Chrome** | `#4FD4CC` · `#4F74C9` · `#EAF2FF` · `#DDE5F0` · `#59677D` | Solo el dock de navegación (tokens `--nav-*`) |

Cualquier color que no salga de una de esas dos capas, de `color.badge` o de `color.table` es desviación. En particular: **la escala `slate-*` de Tailwind no es paleta FLIT**.

## Pantallas y patrones base

| Pantalla/patrón | Reglas que gobierna |
|---|---|
| Login / Registro / Seguridad | Pantalla partida: panel visual izquierdo con gradiente y formulario derecho en tarjeta clara; campos con iconos; CTA degradado. |
| Dashboard | AppShell con dock, topbar, título en tarjeta, KPI cards, gráficos y bloques de actividad. |
| Gestión de colaboradores | Tabla con filtros, búsqueda, botón «Nuevo colaborador», chips de estado y acciones por fila. |
| Detalle/edición colaborador | Tarjetas de información, formularios, opciones de rol/acceso y acciones. |
| Modal invitar colaborador | Modal de dos columnas, opciones tipo tarjeta, selección verde, CTA degradado y blur de fondo. |
| **Gestión integral de trámites** | KPIs por estado, tabs por tipo de trámite, tabla de trámites, acción «Nuevo trámite», búsqueda por placa/VIN. |
| **Detalle de trámite** | Modal ancho: tarjeta vehículo, timeline, validación documental, actores y firmas. |
| Modal consulta vehículo | Modal pequeño con campo placa espaciado y botón «Consultar». |
| **Wizard de trámite** | Ver «Reglas de wizards». |
| Modal de éxito | Placa espaciada, mensaje verde y CTA de salida. |

## Componentes obligatorios

| Familia | Componentes permitidos/esperados |
|---|---|
| Layout | `AppShell`, `AuthShell`, `Dock`, `Topbar`, `PageHeaderCard`, `ContentGrid`, `SecurityFooter` |
| Acciones | `GradientButton`, `NavyButton`, `DangerButton`, `IconButton`, `ActionMenu` |
| Datos | `KpiCard`, `StatusBadge`, `ProgressBar`, `DataTable`, `SearchInput` |
| Formularios | `TextInput`, `PasswordInput`, `PlateInput`, `SelectCard`, `DateRangeField`, `UploadBox` |
| Trámites | `WizardStepTracker`, `TimelineProcess`, `VehicleCard`, `PersonCard`, `ValidationCard`, `SignatureEvidence` |
| Feedback | `AlertCard`, `VehicleQueryModal`, `InviteCollaboratorModal`, `SuccessModal`, `Toast` |

## Reglas de layout

La app interna usa fondo azul claro, **dock inferior** y topbar superior. El contenido se organiza en tarjetas blancas con grillas amplias. El título de pantalla no va flotando sobre el fondo: va en tarjeta blanca.

| Caso | Regla |
|---|---|
| Pantalla interna estándar | Topbar + `PageHeaderCard` + tarjetas/tablas + dock |
| Gestión con datos | Acciones arriba, búsqueda y filtros **visibles**, tabla con badges |
| Detalle de entidad | Tarjetas blancas en columnas con información y validaciones |
| Flujo largo | Wizard con stepper (ver abajo) |
| Confirmación | Modal con blur; no navegar a pantallas de éxito visualmente ajenas |

### Contrato D — dock inferior

| Aspecto | Regla |
|---|---|
| Posición | Flotante, centrado horizontalmente, sobre el contenido |
| Superficie | `rgba(255,255,255,0.94)` en claro, `rgba(22,39,68,0.90)` en oscuro |
| Forma | Píldoras redondas, radio 999px; panel desplegable radio 18px |
| Activo | Gradiente `90deg #4FD4CC → #4F74C9` y marcador puntual |
| Accesibilidad | Cada píldora con nombre accesible; el dock es navegable por teclado y no atrapa el foco |

La sidebar vertical con gradiente del prototipo v4 es **histórica**. No reintroducirla salvo decisión explícita.

## Reglas de color por estado

| Estado | Tono | Uso |
|---|---|---|
| Completado/válido/aprobado | `success` | Pasos completados, checks, estado vigente. **Color: verde tintado (#F3FBE8/#4F7A12) — NO cian.** El cian es acento tecnológico, no semántica de éxito. |
| Acción/proceso/activo | `info` | Botones de acción, estados en curso |
| Pendiente | `warning` | Firma pendiente, documentos por cargar. Color: ámbar #F9AC00 familia. |
| Alerta/rechazo/error | `danger` | Rechazados, no validado, multas, cancelar |
| Borrador/inactivo | `neutral` | Borrador, pasos pendientes, textos auxiliares |

### Estados de trámite

Cada estado tiene **tono propio**: son siete estados operativos que el gestor distingue de un vistazo, y reutilizar una escala de cinco obligaba a que dos pares compartieran color. Fuente única: `ESTADO_CHIP_STYLES` en `lib/tramites/estados.ts`.

| Estado | Tono | `accent` (gráfico) | `color` (texto del chip) |
|---|---|---|---|
| Borrador | Gris | `#94A3B8` | `#64748B` |
| Preparado | Azul de marca | `#557EFF` | `#3B4FD6` |
| Entregado | Violeta | `#7C3AED` | `#6D28D9` |
| Aprobado | Verde | `#16A34A` | `#15803D` |
| En subsanación | Cian | `#00DBD5` | `#0F766E` |
| Rechazado | Naranja de marca | `#FF4E00` | `#C2410C` |
| Anulado | Vino | `#991B1B` | `#991B1B` |

Las dos columnas no son redundancia: `accent` es el tono exacto del diseño y se usa en elementos **gráficos** (icono del KPI, puntos, barras), donde el umbral es 3:1. `color` es ese mismo tono oscurecido para **texto**, donde el umbral es 4.5:1 — varios de los puros se quedan cortos (`#557EFF` ≈ 3.7:1, `#16A34A` ≈ 3.1:1, `#FF4E00` ≈ 3.5:1, `#00DBD5` muy por debajo). Ajustar la luminosidad conservando el matiz es lo que corresponde cuando un color del diseño no cumple contraste en un uso concreto; sustituirlo por otro matiz, no.

Estos siete tonos son cerrados: no inventar uno nuevo por componente ni pasar el `accent` como color de texto.

## Reglas de formularios

Campos blancos con bordes claros y radios medios. Iconos dentro del input o al inicio. Labels en navy y peso semibold. El campo de placa muestra caracteres en mayúscula, espaciados y con apariencia de placa.

| Componente | Regla específica |
|---|---|
| Input de búsqueda | Icono lupa a la izquierda y placeholder claro |
| Input de placa | Mayúsculas, tracking alto, centrado si está en modal |
| Select tipo tarjeta | Opción seleccionada en verde; no seleccionadas blancas con borde. **No usar radios nativos sin estilizar** |
| Upload box | Borde punteado azul, icono centrado, texto azul, fondo blanco |
| Password | Icono de ojo/seguridad si aplica |
| Todos | `<label>` real o nombre accesible. Un `placeholder` **no** es un label |

## Jerarquía de botones (PDF 20 ago — aplica especialmente en wizard MI/Traspaso)

La distinción entre **consulta** y **acción primaria** es cromática y obligatoria:

| Tipo de botón | Visual | Ejemplos | Token |
|---|---|---|---|
| **CTA de avance/cierre** | Degradado `#557EFF → #00DBD5` (o `#00DBD5 → #8CC63F` en último paso) | Continuar, Radicar, Finalizar | `component.button.primaryGradient` / `WIZARD_CTA_GRADIENT` |
| **Consulta** | Azul sólido `#557EFF` **SIN degradado** | Consultar RUNT, Consultar RUES, Buscar, Actualizar (consulta) | `component.button.consultSolid` / `WIZARD_BTN_SOLID` + clase `bg-[#557EFF]` |
| **Secundario/anterior** | Navy `#162744` con borde o texto | Anterior, cerrar | – |

Regla de implementación: los botones de consulta **siempre** llevan `style={{ background: '#557EFF' }}` **y** la clase Tailwind `bg-[#557EFF]` para que el sólido gane sobre cualquier degradado residual por HMR o cascade.

## Reglas de badges/etiquetas (PDF 20 ago)

El badge `success` es **verde tintado**, NO cian. El cian (`#00DBD5`) es acento tecnológico (segundo stop de gradientes, chrome del dock); usarlo como success fue un error de implementación corregido en v2.1.0.

| Badge | bg | fg | border | Uso |
|---|---|---|---|---|
| `success` | `#F3FBE8` | `#4F7A12` | `#CDEB9C` | Validado, vigente, OK, aprobado |
| `warning` | `rgba(249,172,0,0.15)` | `#B45309` | `rgba(249,172,0,0.40)` | Pendiente, advertencia |
| `danger` | – (sin cambio) | – | – | Rechazado, error |
| `info` | – (sin cambio) | – | – | En proceso, dato reutilizado |
| `neutral` | – (sin cambio) | – | – | Borrador, inactivo |

Rechazar automáticamente: badge `success` con tint cian (`rgba(0,219,213,…)`); botón Consultar con degradado.

## Reglas de tablas

Cabecera gris de marca, filas cómodas, badges y acciones compactas. No usar tablas densas tipo sistema operativo ni grids con bordes fuertes.

| Elemento | Regla |
|---|---|
| Cabecera | `#DFE5ED` con texto navy semibold. Este gris es deliberado (HU #10844): el casi blanco se aplanaba sobre paneles blancos |
| Filas | Blancas, altura cómoda, borde `#DFE5ED`, hover `#F4F8FF` |
| Badges | Forma tintada de `color.badge`; radio completo. **Prohibido el badge sólido con texto blanco** |
| Acciones | Iconos lineales pequeños, con nombre accesible |
| Progreso | Barra horizontal con color semántico y porcentaje si aplica |
| **Semántica** | `<table>`, `<thead>`, `<th scope>` reales. Una grilla de `div` con `role="button"` por fila **no** es una tabla y rompe el lector de pantalla |

## Reglas de wizards y trámites

El wizard es un patrón crítico: conserva círculos numerados, etiquetas y colores por estado.

| Aspecto | Regla vigente |
|---|---|
| Orientación | **Stepper horizontal** con círculos numerados y conector |
| Pasos — trámite general | Trámite y Vehículo → Actores y Validación → Documentos → Datos Comerciales → FUR y Expediente |
| Pasos — matrícula inicial | Consulta VIN y Placa → Comprador y Rep. Legal → Documentos → FUR y Expediente |
| Reordenar | Prohibido reordenar o renombrar pasos sin HU que lo respalde: rompe trazabilidad operativa |

| Estado de paso | Visual esperado |
|---|---|
| Completado | Círculo `#8CC63F` con check |
| Activo | Círculo `#557EFF` con número, etiqueta en azul y bold |
| Pendiente | Círculo con borde gris y número gris |
| Bloqueado | Círculo gris con candado y motivo accesible |
| Finalizado | Todos verdes; CTA final en gradiente `#00DBD5 → #8CC63F` |

## Reglas de modales

Overlay `rgba(22,39,68,0.45)` con `blur(6px)` y contenedor claro de radio amplio. Cierre X arriba a la derecha. CTA principal con gradiente.

| Modal | Regla |
|---|---|
| Consulta vehículo | Título, subtítulo, placa espaciada, botón «Consultar» |
| Trámite completado | Título de éxito, placa asociada, mensaje verde, CTA de salida |
| Invitar colaborador | **Dos columnas**: inputs, rol, tipo de acceso, fecha, aviso de acceso y botón «Enviar Instrucciones» |
| Detalle de trámite | Modal ancho con tarjetas internas |

Todos, sin excepción: `role="dialog"`, `aria-modal="true"`, focus trap, cierre con Escape y retorno del foco al disparador.

## Reglas de accesibilidad

No son recomendaciones. Son compuertas.

| Área | Regla |
|---|---|
| Foco visible | Anillo de 2px `#557EFF` con offset 2px en **todo** interactivo. `outline-none` sin sustituto es desviación bloqueante |
| Contraste | 4.5:1 en texto normal, 3:1 en texto grande y componentes gráficos |
| Badges | Usar `color.badge`. Un relleno de marca con texto blanco no pasa: `#00DBD5` da ≈1.7:1 y `#F59E0B` ≈2.0:1 |
| Tipografía | Piso de **12px**. `text-[10px]` y `text-[11px]` son desviación |
| Opacidad | No bajar de 0.7 sobre texto |
| Estados | Nunca solo color: acompañar con texto o icono |
| Teclado | Dock, modales, formularios, tablas con acciones y wizard operables por teclado |
| Movimiento | Toda animación ambiental respeta `prefers-reduced-motion` |
| Imágenes | `alt` funcional. **No** poner nombres de personas en `alt`; usar «avatar», «firma», «evidencia documental» |

## Reglas de privacidad

Rostros, documentos, firmas y datos personales son placeholders visuales. Describir su función, nunca identificar personas. En implementaciones y demos usar datos simulados o anonimizados. **No** usar servicios externos de avatares con nombres reales asociados, ni asociar nombres a datos biométricos en pantallas de ejemplo.

## Reglas de rechazo automático

| Rechazar si aparece | Motivo |
|---|---|
| Superficie translúcida con blur fuera de las dos excepciones | Glassmorphism. Únicas excepciones: `--nav-vidrio` del dock (0.94, casi opaco) y el overlay de modal (`blur(6px)`). `bg-white/70` con `backdrop-blur-xl` sobre contenido queda fuera |
| Librería visual sin tematizar | shadcn/ui, Radix o cualquier otra con sus tokens por defecto introduce radios, colores y grises ajenos |
| Surface oscuro fuera de la capa `dark` | Los `#0B0F14` y `#05060A` inventados por componente son deriva, no tema |
| Nueva paleta sin tokens FLIT | Incluye la escala `slate-*` de Tailwind |
| Botones rectangulares o planos para CTAs primarios | Rompe el patrón de pastilla degradada |
| Fondos blancos puros en app interna | Rompe la atmósfera azul clara |
| Texto por debajo de 12px | Legibilidad y contraste efectivo |
| `outline-none` sin foco sustituto | Barrera de teclado |
| Modal sin `role="dialog"` y focus trap | Barrera de lector de pantalla |
| Grilla de `div` sustituyendo una tabla de datos | Pérdida de semántica |
| Badge sólido con texto blanco | No cumple contraste |
| Badge `success` con tint cian | Incorrecto: success = verde tintado (`#F3FBE8`/`#4F7A12`). El cian es acento tecnológico, no semántica de éxito (PDF 20 ago) |
| Botón Consultar/Buscar con degradado | Incorrecto: consulta = `#557EFF` sólido; degradado reservado a CTA de avance/cierre (PDF 20 ago) |
| Reordenamiento de wizard sin HU | Rompe trazabilidad operativa |
| Iconos de otra familia | Rompe consistencia visual |
| Reintroducir la sidebar vertical | El patrón vigente es el dock; cambiarlo requiere decisión explícita |
