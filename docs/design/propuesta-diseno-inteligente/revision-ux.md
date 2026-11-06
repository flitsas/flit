# Revisión UX de la propuesta

Fecha: 2026-11-05. Rama `propuesta`. Modo: evaluación experta y entrega a diseño de interfaz, sobre la propuesta navegable de esta carpeta.

## Alcance y decisión que habilita

Decidir si la propuesta de Diseño Inteligente con los colores corporativos de FLIT sirve de base para rediseñar todo el sistema sin romper los flujos que ya conocen los gestores, los administradores y los organismos.

Primera ronda (detallada más abajo): ingreso, Inicio con cifras, Trámites con el embudo de estados, el asistente lateral del traspaso, la consulta de placa y el buscador.

Segunda ronda: el sistema completo como Super Admin, con 62 pantallas y pestañas. Ver la sección "Segunda ronda: todo el sistema".

## Registro de evidencia

| Afirmación | Tipo | Fuente |
|---|---|---|
| El gestor de un concesionario o de una red es el rol principal y su tarea crítica es llevar un traspaso del borrador a la radicación | Evidencia indirecta | `docs/contexto-funcional-flit.md`, `docs/arbol-menu-por-rol.md` y las reglas del prototipo (páginas 11 a 18) |
| El asistente lateral y el embudo horizontal son patrones conocidos por los usuarios actuales | Evidencia indirecta | Prototipo FLIT y el frontend en `develop` |
| Ver lo pendiente antes que el total reduce el tiempo para encontrar qué hacer | Hipótesis | Sin datos de uso; se valida con la prueba de la sección Validación |
| 0 violaciones automáticas de WCAG 2.2 AA en 15 combinaciones (5 pantallas × claro, oscuro y 375) | Evidencia directa | axe-core 4 con Playwright, 2026-11-05 |
| Teclado: el buscador abre con foco en el campo, las flechas recorren los resultados, Escape cierra y devuelve el foco, y un filtro conserva el foco y queda en la dirección | Evidencia directa | Prueba con Playwright, 2026-11-05 |
| Ningún usuario real probó la propuesta | Límite declarado | No hubo sesiones con usuarios |

Una pasada automática no certifica conformidad WCAG. Falta un recorrido con lector de pantalla (VoiceOver y NVDA) y con zoom al 200 %.

## Hallazgos y lo que se hizo

| # | Tarea afectada | Hallazgo | Severidad | Estado |
|---|---|---|---|---|
| 1 | Navegar en celular | Los enlaces del menú inferior se quedaban sin nombre accesible, porque el texto se ocultaba con `display: none` | 4: un lector de pantalla anunciaba seis enlaces sin nombre | Corregido: el texto queda solo para lectores |
| 2 | Buscar | En el campo de búsqueda, Escape solo borraba el texto y el diálogo no se cerraba | 3: el usuario de teclado quedaba atrapado | Corregido |
| 3 | Buscar | Los títulos de grupo estaban dentro de un `listbox` (ARIA inválido) y el campo no se anunciaba como combobox | 3 | Corregido: combobox con lista, grupos con nombre y `aria-activedescendant` |
| 4 | Registrar al comprador | La caja de carga estiraba la fila y los campos de correo y celular medían 100 px de alto | 2 | Corregido: la carga ocupa su propia fila |
| 5 | Ingresar | El logo salía deformado, estirado a lo ancho de la columna | 2: daña la confianza en la pantalla donde más importa | Corregido |
| 6 | Navegar en escritorio | El menú inferior se encogía y montaba los textos sobre las ilustraciones | 3 | Corregido: el menú mide lo que mide su contenido |
| 7 | Leer la actividad en celular | Las filas se desbordaban 46 px a lo ancho | 2 | Corregido |
| 8 | Avanzar en el traspaso | Los pasos Firmas y Radicación no se pueden pulsar y nada decía por qué | 2 | Corregido: nota bajo los pasos |
| 9 | Revisar la lista en celular | Cada trámite ocupaba seis filas con etiquetas repetidas | 2 | Corregido: el tipo pasa a la etiqueta de la placa y la acción va a la derecha |
| 10 | Encontrar lo pendiente | La cifra "Esperan firma" dice "Enviar recordatorio" pero lleva a la lista filtrada, donde está el botón | 1 | Se mantiene: la barra de acción aparece justo ahí. Se valida en la prueba |
| 11 | Toda la plataforma | El prototipo FLIT rechaza un tema oscuro integral; el Diseño Inteligente lo exige | Decisión de producto | Se entrega con tema oscuro y se marca como decisión pendiente de producto |

## Arquitectura de información propuesta

- Menú inferior flotante (estándar FLIT): Inicio, Trámites, Comparendos, Diagnóstico, Colaboradores y Reportes. Las secciones que dependen del rol se ocultan; no se muestran deshabilitadas.
- Buscador con Ctrl K, en tres grupos: secciones, acciones (nuevo traspaso, consultar placa, trámites que esperan firma, documentos por revisar) y placas. La placa es lo que el gestor tiene a mano y entra por ahí.
- La campana lleva a lo que espera firma, igual que la cifra de Inicio, y el filtro queda en la dirección (`#tramites?vista=firma`) para compartirlo.

## Flujo crítico: traspaso

1. Precondición: un gestor con permiso de crear trámites y la placa del vehículo.
2. Entrada: Inicio, Nuevo traspaso; Trámites, Nuevo traspaso; o Consultar placa, Iniciar traspaso.
3. Pasos, en el asistente lateral: consulta, SOAT y revisión, vendedor, comprador, firmas y radicación. Solo se pueden pulsar los pasos ya alcanzados.
4. Errores: un correo inválido muestra el mensaje bajo el campo, con `role="alert"`, y el foco vuelve al campo. La placa con formato incorrecto se trata igual.
5. Recuperación: Guardar borrador en cualquier paso. Cada botón tiene su propio estado de carga.
6. Salida: validación enviada (aviso con `role="status"`) y el trámite pasa a "Pendiente de firma".

## Validación

| Objetivo | Señal | Métrica | Decisión |
|---|---|---|---|
| Encontrar lo pendiente | Clic en una cifra o chip pendiente | Tiempo desde Inicio hasta abrir un trámite que espera firma | Si en la prueba la mediana pasa de 20 s, se revisa la jerarquía de las cifras |
| Completar el traspaso | Envío de la validación del comprador | Finalización sin ayuda en la prueba con 5 gestores (ciclo cualitativo, no estadístico) | Con 2 o más tropiezos en el mismo paso, se rediseña ese paso antes de implementar |
| Accesibilidad | Recorrido con lector de pantalla y con zoom al 200 % | Tareas completadas sin pérdida de foco | Cualquier pérdida de foco bloquea la implementación |

## Handoff

- **Diseño de interfaz:** mantener las condiciones del Diseño Inteligente y los valores de `tokens.css`. Al pasar al frontend real, llevar los tokens a `packages/ui/src/styles/tokens.css` sin romper las variables `--brand-*` de la marca blanca, y `Modal` de `@flit/ui` al patrón de diálogo de esta propuesta.
- **Frontend:** filtros en la dirección; `aria-pressed` en los chips; diálogos nativos con `showModal()`; el buscador como combobox; nada de deshabilitar otros botones mientras uno carga.
- **QA:** repetir `axe` y la prueba de teclado en 1440 claro, 1440 oscuro y 375; agregar VoiceOver y NVDA; zoom al 200 %; el traspaso con un correo vacío.
- **Producto:** decidir sobre el tema oscuro (hallazgo 11) y sobre si los logos por producto reemplazan a los actuales.

## Segunda ronda: todo el sistema

Alcance: rediseño completo con acceso a todos los módulos, tema oscuro incluido y solo colores corporativos.

### Arquitectura de información

- El menú inferior conserva el reparto de FLIT: a la izquierda del botón de Inicio, Trámites, Identidad y Reportes; a la derecha, Usuarios, Administradores e Integraciones. Los grupos de varios ítems abren un submenú hacia arriba.
- Administradores tiene 16 entradas. Se ordenan en cuatro secciones con nombre (Compañías y tránsito, Documentos, Plataforma, Seguridad y operación), para que no sean una lista plana de 16.
- Compañía y organismo de tránsito son consolas con pestañas, igual que hoy. La pestaña activa queda en la dirección (`?tab=`).
- Comparendos y Diagnóstico se abren desde el selector de productos de la barra superior.
- Inicio tiene todos los módulos en un acordeón por grupo, para quien no recuerda en qué grupo está cada cosa.

### Patrón único de módulo

Cada módulo de listado sigue el mismo contrato:
1. cabecera con ilustración y la acción principal;
2. búsqueda;
3. conteos por estado que filtran;
4. barra de acción cuando lo filtrado es un pendiente;
5. tabla de máximo 10 filas por vista, que en celular se ve como tarjetas;
6. Ver o Abrir en cada fila.

Así quien aprende un módulo ya sabe usar los demás.

### Hallazgos de la segunda ronda

| # | Hallazgo | Severidad | Estado |
|---|---|---|---|
| 12 | Colores fuera de la marca: verde para lo completo y tonos verdosos en el botón y los antetítulos | 3: rompe la identidad | Corregido: completo en pastilla cielo con check azul, pendiente y error en tinta con marca naranja, botón azul hacia turquesa. La placa usa el amarillo del objeto real |
| 13 | Reportes no cargaba: error de JavaScript al leer las cifras | 4 | Corregido |
| 14 | Con el puntero sobre una fila, las pastillas de estado y el botón Ver bajaban de 4,5:1 | 3 | Corregido: al pasar el puntero cambia el borde, no el fondo |
| 15 | Tema oscuro: el texto pequeño sobre la caja turquesa bajaba de 4,5:1 | 3 | Corregido |
| 16 | Los conteos mostraban estados que esa lista no tiene ("0 Publicado" en Causales) | 2 | Corregido: solo aparecen los estados presentes. En Trámites se conserva el embudo completo |
| 17 | En celular las pestañas de una compañía ocupaban cinco renglones | 2 | Corregido: una fila que se desliza |
| 18 | La matriz de permisos mostraba "Eliminar" en todo para el Radicador | 1 | Corregido con permisos realistas |

### Recorrido completo

Playwright, 62 pantallas en 1440 claro, 1440 oscuro y 375:

- 186 pasadas de axe sin violaciones;
- sin desbordes ni errores;
- 120 filtros y 204 acciones verificados;
- 32 formularios que marcan los obligatorios y llevan el foco al primero;
- todos los submenús y el buscador, en escritorio y en celular.

## Riesgos pendientes

- No hubo pruebas con usuarios. Todo lo marcado como hipótesis sigue sin validar.
- Comparendos, Diagnóstico, Colaboradores y Reportes solo tienen la cabecera y el estado vacío.
