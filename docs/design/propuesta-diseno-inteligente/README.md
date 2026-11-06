# Propuesta: rediseño completo de FLIT 2.0 con el Diseño Inteligente

Rama `propuesta`, 2026-11-05. Es una propuesta navegable de todo el sistema, vista como Super Admin (con acceso a todos los módulos). No toca el frontend de producción: sirve para aprobar el diseño antes de pasarlo a `frontend/` y a `packages/ui`.

## Cómo verla

Con datos de muestra:

```bash
cd docs/design/propuesta-diseno-inteligente
python3 -m http.server 8731
# abrir http://127.0.0.1:8731/
```

Con tus datos reales de FLIT, en solo lectura:

```bash
node docs/design/propuesta-diseno-inteligente/servidor-local.mjs            # producción (api.flitsas.com)
FLIT_API=https://api.qa.flitsas.online node docs/design/propuesta-diseno-inteligente/servidor-local.mjs   # QA
# abrir http://127.0.0.1:8732/ e ingresar con tu usuario de FLIT
```

`servidor-local.mjs` (Node 18 o más, sin dependencias) escucha solo en este equipo y reenvía a la API real dos cosas: el ingreso y las consultas `GET`. Cualquier otro método hacia `/api` responde 405 ahí mismo y nunca sale del equipo, así que la propuesta no puede cambiar nada en FLIT. El token se queda en la memoria del proceso y el navegador recibe una cookie de sesión aleatoria `HttpOnly`. El registro en consola muestra método, ruta y código, nunca contraseñas ni tokens. Con datos reales, Inicio, Trámites (con filtros) y el detalle de cada trámite leen la API; los demás módulos muestran la pastilla "Datos de muestra".

El botón de la luna cambia entre tema claro y oscuro; el tema oscuro hace parte de la propuesta. Ctrl K abre el buscador. Los datos son ficticios.

## Qué cubre

El árbol de módulos sale de `docs/arbol-menu-por-rol.md`. Son 62 pantallas y pestañas, todas navegables desde el menú inferior, el buscador o el mapa de módulos de Inicio.

| Zona | Pantallas |
|---|---|
| Ingreso y cuenta | Ingreso, Mi perfil, Cambiar contraseña, Ayuda (manual), Juego de marca |
| Inicio | Cifras que filtran, accesos rápidos, actividad y mapa de todos los módulos |
| Trámites | Listado con embudo de estados, Nuevo trámite (modalidades), asistente del traspaso (6 pasos), seguimiento del trámite (línea de avance, documentos, firmas), Historial por placa, Revocatorias, Consultar placa |
| Identidad | Prevalidación de identidad |
| Reportes | Resumen general, Operación, Organismo de tránsito, Uso del aplicativo, Productividad, Reportes detallados, Programar envío |
| Usuarios | Usuarios, Roles y permisos, Clientes ICT, Eliminados, Invitar colaborador |
| Compañías y tránsito | Compañías y la consola de cada una (Matrícula inicial, Traspasos, Configuración, Documentos, Placas preasignadas, Representantes legales, Mandatarios, Usuarios, Historial de cambios), Organismos de tránsito y su consola (Trámites, Reglas, Requisitos, Preasignación, Usuarios, Configuración), Causales de rechazo |
| Documentos | Documental, Improntas (generar e historial), Generación documental, Quipux |
| Plataforma | Tipos de trámite, Confirmación RUNT, Mandatos, FUR, Notificaciones (con vista previa del correo), Banners |
| Seguridad y operación | Roles y permisos (matriz por módulo), Auditoría, Procesos periódicos |
| Integraciones | Log QX, Log ICT, Trazabilidad ICT, Reportes ICT |
| Productos de la suite | Comparendos y Diagnóstico, con el selector de productos de la barra superior |

## Colores: solo los corporativos

| Uso | Color |
|---|---|
| Títulos, franja de los diálogos, pestaña activa | Tinta `#162744` |
| Botón principal y menú activo | Degradado del azul FLIT hacia el turquesa: `#3D5FD9` a `#0F6FA8` (5,4:1 con texto blanco). El `#557EFF` a `#00DBD5` original se queda en el isotipo y las barras, porque con texto blanco da 3,6:1 y 1,7:1 |
| Antetítulos y enlaces | Azul FLIT legible `#3D5FD9`; titulares grandes en el azul `#557EFF` |
| Completo | Pastilla cielo `#E4EDFB` con texto tinta y check azul. No se usa verde |
| Activo o en proceso | Azul |
| Pendiente | Pastilla cielo con texto tinta y punto en naranja FLIT `#FF4E00` |
| Rechazado o error | Pastilla blanca con aro y equis en naranja FLIT y texto tinta. El naranja va solo como marca, nunca como texto ni fondo |
| Fondo | Azul claro `#EEF5FF` y blanco |
| Placa | Blanca con borde y texto tinta |

No quedan verde, amarillo ni rojo genérico.

## Archivos

| Archivo | Qué es |
|---|---|
| `tokens.css` | Roles del Diseño Inteligente con los valores de FLIT, en claro y oscuro |
| `propuesta.css` | Componentes. Sin colores literales: todo pasa por los tokens |
| `datos.js` | Datos de muestra y definición de cada módulo (columnas, estados, acciones, formularios) |
| `propuesta.js` | Navegación, menú inferior con submenús, listas con filtros, formularios, diálogos y tema |
| `index.html` | La propuesta |
| `assets/logos/` | 14 logos en SVG construidos con los trazos oficiales, más PNG del horizontal para correos |
| `assets/ilustraciones/` | Ilustraciones de módulo (WebP de 192 px) y el fondo de la placa |
| `assets/iconos/` | `favicon.ico`, PNG de 16/32/48, `apple-touch-icon` e íconos de 192 y 512 px |
| `revision-ux.md` | Revisión UX, recorrido completo y handoff |

## Logos

Los logos son una familia en vector construida sobre los trazos oficiales:

- El isotipo y la palabra `flit` son los trazos oficiales de `frontend/public/assets/logo-flit-dark.svg` y `favicon.svg`, sin redibujar. Se verifica buscando cada trazo oficial, carácter por carácter, dentro de cada SVG nuevo, y está en todos.
- Los nombres de producto van en Poppins Medium convertida a trazos, así no dependen de que la fuente esté instalada. Colores: `#3D5FD9` sobre claro y `#8FB0FF` sobre oscuro.
- Construcción: la unidad es H, el alto de la palabra `flit`. El isotipo mide 1 H, la separación 0,3 H y el margen de protección 0,25 H. El divisor de los productos es azul FLIT en claro y turquesa en oscuro.
- Piezas: horizontal y vertical en claro y oscuro, isotipo, ícono de aplicación, monocromo tinta y blanco, y Trámites, Comparendos y Diagnóstico en claro y oscuro.
- `assets/iconos/` sale de estos mismos vectores: favicon SVG e ICO (el SVG oficial), `apple-touch-icon` a sangre, 192, 512 y maskable de 512.

## Recorrido completo (2026-11-05)

Playwright pasó por las 62 pantallas en 1440 claro, 1440 oscuro y 375:

- 186 capturas y 186 pasadas de axe-core (WCAG 2.2 AA): 0 violaciones.
- 0 desbordes horizontales y 0 errores de JavaScript.
- Inicio: los módulos van en acordeón de grupo (contador a la izquierda, pendientes a la derecha, uno abierto a la vez y el abierto en la dirección `?grupo=`).
- 120 filtros pulsados: cada uno muestra exactamente las filas que cuenta.
- 195 acciones pulsadas: cada una abre su formulario, diálogo o detalle, o da su aviso.
- 32 formularios enviados vacíos: cada uno marca los campos obligatorios y lleva el foco al primero.
- Los 4 submenús del menú inferior, el selector de productos, el menú de usuario y el buscador, en escritorio y en celular.

## Pendientes

1. Pasar la propuesta al frontend real: tokens en `packages/ui/src/styles/tokens.css` (respetando `--brand-*` de la marca blanca), `Modal`, `DataTable`, `StatusBadge`, el dock de `@flit/shell` y las pantallas de `frontend/app`.
2. Recorrido con lector de pantalla y zoom al 200 %, y prueba con gestores (ver `revision-ux.md`).
3. Las vistas de Admin de compañía, Admin del organismo y Radicador: la propuesta muestra la de Super Admin; las otras son la misma pantalla con menos módulos.
