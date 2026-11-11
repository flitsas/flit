# Propuesta: FLIT 2.0 con el Manual UI Kit Ecosistema FLIT (edición 2026)

Rama `diseno/ui-kit-2026`, 2026-11-11. Ejecutor: Johan Jimenez.

Es la propuesta navegable de todo FLIT 2.0 (vista de Super Admin, 62 pantallas y pestañas; el árbol de módulos sale de `docs/arbol-menu-por-rol.md`) vestida con el Manual UI Kit Ecosistema FLIT 2026, con la logometría del repositorio de íconos de FLIT (`github.com/yulojiar91/ICONOSFLIT`) y con un juego de íconos redibujado para la propuesta a partir de ese paquete. No toca `frontend/` ni `packages/ui`: sirve para ver cómo pinta el manual sobre las pantallas reales antes de pasarlo a producción.

## Cómo verla

```bash
cd docs/design/propuesta-ui-kit-2026
python3 -m http.server 8733
# abrir http://127.0.0.1:8733/
```

Con datos reales de FLIT, en solo lectura:

```bash
node docs/design/propuesta-ui-kit-2026/servidor-local.mjs            # producción (api.flitsas.com)
FLIT_API=https://api.qa.flitsas.online node docs/design/propuesta-ui-kit-2026/servidor-local.mjs   # QA
# abrir http://127.0.0.1:8732/ e ingresar con tu usuario de FLIT
```

`servidor-local.mjs` (Node 18 o más, sin dependencias) escucha solo en este equipo y reenvía a la API real el ingreso y las consultas `GET`. Cualquier otro método hacia `/api` responde 405 ahí mismo, así que la propuesta no puede cambiar nada en FLIT. Con datos reales, Inicio, Trámites y el detalle de cada trámite leen la API; los demás módulos muestran la pastilla «Datos de muestra».

## Qué cambia frente a la primera versión de la propuesta

| Pieza | Antes (Diseño Inteligente) | Ahora (Manual UI Kit 2026) |
|---|---|---|
| Logo e isotipo | Logos reconstruidos en vector | Los del repositorio de íconos (`assets/kit/logo-*.svg`, `isotipo.svg`, `isotipo-blanco.svg`, `favicon.svg`) |
| Íconos | 45 ilustraciones | Juego de relleno plano en color heredado en `assets/kit/kit-iconos.js`: 41 módulos con 41 íconos distintos, más los de apoyo; las flechas y la marca de verificación son controles de interfaz |
| Estados | Pastillas cielo, sin verde ni rojo | Etiqueta con punta, ícono de estado del repositorio y color semántico al 10 % |
| Botones | Degradado azul a turquesa | Píldora plana: azul avanza, verde confirma, azul oscuro navega, naranja alerta |
| Bordes y radios | `#DFE5ED`, radios 8 a 28 px | `#A9C9FC` de 1 px y radio 9 px |
| Sombras | En tarjetas, botones y modales | Solo en el menú principal y sus menús (3, 3, 3, `#557EFF` al 30 %) |
| Tablas | Filas sueltas | Contenedor con borde, encabezado `#A9C9FC` y filas separadas por línea |
| Modales | Franja tinta con ilustración | Contenedor blanco, ícono centrado, fondo azul al 50 % con desenfoque |
| Menú principal | Activo con relleno degradado | Píldora blanca, activo en azul con punto |
| Pestañas | Píldoras | Línea inferior, activa en azul |
| Interruptor | Azul | Verde ON, gris OFF |
| Carga | Aro giratorio | Pista azul fija, vehículo y línea turquesa en sentidos contrarios |
| Ingreso | Fondo tinta | Degradado de identidad azul a turquesa |

| Tablero | Saludo en una cabecera | Banner en carrusel con su propio fondo, cifras a la derecha y gráficos de dona y barras |
| Reportes | Solo barras horizontales | Donas por categoría y tendencia mensual en barras |
| Cabecera de página | Antetítulo y «Volver a» | Miga de pan: niveles anteriores en gris, actual en azul con línea |
| Asistente del traspaso | Pasos en columna lateral | Línea de progreso horizontal con la píldora de datos del trámite |
| Notificaciones | Enlace directo | Panel desplegable desde la campana |

## Cuadrícula y tamaños

- Márgenes laterales iguales que siguen el ancho de la pantalla (16 a 32 px) y contenido hasta 1680 px.
- Separaciones solo de la escala del manual: 4, 8, 16, 24 y 32 px.
- Tipografía fija del manual en escritorio: 32 bold, 24 medium, 16 regular, 12 light.
- Tres cortes: 1100 px (cifras en fila), 760 px (una columna) y 640 px (teléfono: estados y pasos se deslizan dentro de su cuadro, menú solo con íconos).
- Medido el 11/11 en 390, 800, 1280 y 1920 px sobre 14 vistas: ninguna desborda a lo ancho.

Todo vive en tres archivos: `ui-kit-2026.css` (valores y formas, se carga después de `tokens.css` y `propuesta.css`), `assets/kit/` (recursos del repositorio de íconos) y los parches marcados con «UI Kit 2026» en `propuesta.js`.

## Recursos del kit (`assets/kit/`)

- Logo, isotipo, favicon e interruptores son los del paquete de íconos de FLIT, sin cambios.
- Íconos de módulo y de apoyo (`kit-iconos.js`): relleno plano de construcción geométrica, un solo color principal por ícono. Cada módulo tiene un ícono propio: Compañías, Organismos de tránsito y Fuentes oficiales ya no comparten edificio; Biometría (silueta con marco de lectura) se separa de Identidad (huella); Configuración (engranaje) de Usuarios (grupo de personas); Roles y permisos (llave) de Seguridad (escudo); FUR (formulario) de Documentos.
- Las pantallas que compartían ícono con su módulo tienen el suyo: Reportes detallados (tabla), Reportes ICT (gráfico con nodos), Quipux (bandeja de radicación), Log QX (mensajes), Log ICT (ventana de llamadas) y Tipos de trámite (capas).
- Cada cifra de Reportes usa un ícono según lo que mide (finalizados, tiempo, en curso, más rápido, devoluciones, ingresos, celular, más productivo, correcciones); el reparto está en `ICONOS_REPORTE` de `propuesta.js`.
- En `kit-iconos.js` el azul del glifo es `currentColor`, el naranja y el verde de acento se conservan y los detalles blancos son recortes (máscara), de modo que el mismo ícono sirve sobre claro, oscuro y botones de color. Cada ícono se encuadra a su dibujo.
- Estados, avisos y trámites (`estado-*.svg`, `aviso-*.svg`, `tramite-*.svg`, `persona-nueva.svg`): dibujo blanco sobre disco de 100 × 100 con el color de cada estado. Matrícula, Otros trámites y Transformación comparten el mismo carro de frente.
- Cada estado tiene su propio ícono. Los del trámite que ya traía el paquete siguen como disco (`estado-*.svg`); los demás (activo, inactivo, invitado, suspendido, publicado, programado, pagado, en cobro, bloqueado, con error, en reintento, entre otros) son un glifo de `kit-iconos.js` sobre un disco con el color de su tono, según `GLIFO_ESTADO` de `propuesta.js`. El filtro «Todos» usa su propio glifo de lista.
- Los SVG se guardan sin hojas de estilo internas, títulos ni identificadores, con el color como atributo.
- La pantalla «Juego de marca» (`#marca`) muestra todo el juego.

## Lo que el manual no define y aquí se derivó

- Tema oscuro: el manual trae el interruptor de sol y luna pero no la paleta oscura. Los valores oscuros de `ui-kit-2026.css` son una derivación propia.
- La placa amarilla con ciudad se conserva de la primera versión; el manual solo muestra la placa como texto espaciado en los modales.
- La escena del ingreso (`heroe-ingreso.webp`) se conserva; el manual no trae pantalla de ingreso.
- El logo del repositorio viene solo en blanco. En el encabezado claro se usa el mismo trazo en tinta `#162744`.

## Contraste medido (WCAG 2.2, texto normal pide 4,5:1)

El manual pide el color semántico pleno en texto y botones. Estos pares quedan por debajo de AA y hay que decidirlos antes de producción:

| Par | Relación |
|---|---|
| Blanco sobre azul `#557EFF` (botón principal) | 3,61:1 |
| Blanco sobre verde `#8CC63F` (botón positivo) | 2,05:1 |
| Blanco sobre naranja `#FF4E00` (botón de alerta) | 3,31:1 |
| Azul `#557EFF` sobre blanco (títulos, enlaces) | 3,61:1 |
| Azul `#557EFF` sobre su fondo al 10 % (etiqueta) | 3,23:1 |
| Naranja `#FF4E00` sobre su fondo al 10 % (etiqueta) | 2,92:1 |
| Verde `#8CC63F` sobre su fondo al 10 % (etiqueta) | 1,91:1 |
| Amarillo `#F9AC00` sobre su fondo al 10 % (etiqueta) | 1,80:1 |
| Gris `#91A5B5` sobre blanco (pestaña inactiva) | 2,54:1 |

Sí cumplen: blanco sobre tinta `#162744` (14,92:1), tinta sobre el encabezado de tabla `#A9C9FC` (8,84:1) y rojo `#C10000` sobre su fondo al 10 % (5,35:1).
