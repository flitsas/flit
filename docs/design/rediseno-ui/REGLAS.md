# Rediseño visual FLIT (UI kit «Diseño Inteligente»): reglas

Toda sesión que trabaje en este rediseño lee este archivo antes de tocar código.

> **Cambio de rumbo (2026-11-09, decisión de Samuel): MANDA LA PROPUESTA.** El aspecto final es el del prototipo de la rama `propuesta` (`docs/design/propuesta-diseno-inteligente/`: `tokens.css`, `propuesta.css`, `propuesta.js`, `datos.js`, `assets/`): paleta (sin verde ni rojo; completo en cielo con check, pendiente con punto naranja, error con aro y equis naranja), componentes, ilustraciones, dock y barra superior. Los textos cortos del prototipo (antetítulos, descripciones del submenú) se toman tal cual. Los tokens del prototipo están en `packages/ui/src/styles/tokens.css` con prefijo `--di-*` (claro y oscuro): al portar `propuesta.css`, `var(--color-x)` → `var(--di-x)`. Ilustraciones: `public/assets/ilustraciones/*.webp` en las 4 apps y la tabla `ilustracionDe(clave)` en `@flit/ui/ilustraciones`. Prototipo navegable: `python3 -m http.server 8731` dentro de esa carpeta (en local corre en http://127.0.0.1:8731). Lo que sigue de este documento sobre el kit queda como historia.

Fuente de diseño (primera vuelta): `~/Downloads/ui-kit-flit 2/` (`tokens.css`, `components.css`, `index.html`, assets). Donde el kit no dice nada se consulta el prototipo de la rama `propuesta` (`docs/design/propuesta-diseno-inteligente/`). **En colores y componentes manda el kit.**

## Alcance

- Apps: `frontend`, `frontend-comparendos`, `frontend-diagnostico`, `frontend-hub` y `packages/{ui,shell,brand}`. **Fuera:** `frontend-personanatural`.
- Temas claro y oscuro (se prende `DARK_MODE_ENABLED` en `packages/shell/src/theme-script.ts`).
- **Solo cambia el aspecto.** Excepciones de UX aprobadas: el dock nuevo, el buscador Ctrl K, el acordeón de Inicio, las cifras de Inicio que filtran y tablas como tarjetas en celular.
- Siguen fuera: máximo de 10 filas, estructura nueva del asistente de traspaso y plantillas de correo del backend. Solo se reemplazan los PNG de `public/email-assets`.

## Reglas de diff

1. Solo pueden cambiar: `className`, objetos `style`, CSS, tokens, assets, fuentes. El dock y Ctrl K viven en `packages/shell`.
2. Nunca se cambia un hook, un handler, `lib/api`, una condición, un texto visible, una ruta ni un permiso. Si un cambio visual obliga a tocar uno, se para y se pregunta.
3. Solo la sesión coordinadora (S0) toca `packages/ui`, `packages/brand` y los tokens. Una zona que necesita un token o un componente nuevo lo anota en `PEDIDOS.md`.
4. Nada de prettier en los frontends (no hay config y reformatea a 80 columnas). Se valida con `tsc`, `eslint` y vitest.
5. Poppins y las fuentes nuevas van con `next/font/local`. CI prohíbe `next/font/google`.
6. La marca blanca no se rompe: los tokens conservan `var(--brand-x, fallback)`.
7. Un color del kit que no da contraste AA no se corrige por cuenta propia: se reporta con la cifra.

## Cómo se traduce el kit al repo

El kit y el repo usan **nombres de token distintos**. Se conservan los nombres del repo y solo se cambian sus valores, para que los ~1.800 `var(--…)` existentes se re-pinten sin tocarlos.

| Kit (`--color-…`) | Valor del kit | Token del repo | Hoy |
|---|---|---|---|
| `surface` | `#eef5ff` | `--color-flit-bg` / `--nav-app-bg` | `#eef5ff` / `#eaf2ff` |
| `surface-elevated` | `#ffffff` | `--card`, `--popover`, `--background` | `oklch(1 0 0)` |
| `surface-sunken` | `#e4edfb` | `--secondary`, `--muted`, `--accent` | `oklch(0.968 …)` |
| `text-primary` | `#162744` | `--foreground`, `--card-foreground` | `oklch(0.129 …)` |
| `text-secondary` / `tertiary` | `#4a5878` / `#5d6b86` | `--muted-foreground` | `oklch(0.554 …)` |
| `accent` / `heading` | `#162744` | `--primary`, `--color-flit-primary` | `oklch(0.208 …)` / `#162744` |
| `action` | `#557eff` | `--color-flit-brand`, `--flit-brand-ink` | `#557eff` / `#476bd9` |
| `gradient-action` | `#557eff → #0f6fa8` | `--nav-activo` (hoy `#557eff → #00dbd5`) | ver nota 1 |
| `border` / `border-strong` | `#dfe5ed` / `#c9d3e3` | `--border`, `--table-row-border`, `--color-flit-gray` | `#dfe5ed` |
| `border-control` | `#74839f` | `--input` | `oklch(0.929 …)` |
| `success` | `#8cc63f` | `--badge-success-*`, `--flit-success-ink` | `#f3fbe8 / #4f7a12 / #cdeb9c` |
| `warning` | `#ff4e00` | `--badge-warning-*` (hoy ámbar `#f9ac00`), `--badge-pending-*` | ver nota 2 |
| `danger` | `#c10000` | `--badge-danger-*`, `--destructive` (hoy naranja `#c2410c`) | ver nota 2 |
| `draft` | `#91a5b5` | `--badge-neutral-*` | `#445569` |
| `placa-fondo` | `#ffd100` | (no existe; placa nueva) | — |
| `radius-sm/md/lg/pill` | 8 / 12 / 20 / 9999 px | `--radius-*` (base `--radius: 0.625rem`) | 6 / 8 / 10 px |
| `shadow-card/modal/button` | ver kit | `--nav-sombra-*` y sombras sueltas | — |

El kit además trae `badge-purple` y `badge-tinta`, que el repo no tiene: se agregan como tonos nuevos sin quitar ninguno.

**Notas que deben resolverse en F1, con quien pasó el kit:**

1. El degradado del botón del kit (`#557eff → #0f6fa8`) y el del dock del repo (`#557eff → #00dbd5`) son distintos. Con texto blanco, el turquesa da 1,7:1. Se usa el del kit en botones con texto y se conserva el de marca en isotipo y barras.
2. El kit usa dos criterios de color de estado que se contradicen: la guía (`DESIGN_SYSTEM_GUIDE.md`) dice «sin verde ni rojo genérico», y `components.css` define `badge-success` verde y `badge-danger` rojo `#c10000`. **Manda `components.css`** (decisión de Samuel: manda el kit), pero hay que confirmarlo.
3. El modo oscuro del kit solo trae ~12 variables y deja varias en claro (por ejemplo `--color-warning`, `--color-danger`, `--color-border-control`). Los valores que falten salen de `docs/design/propuesta-diseno-inteligente/tokens.css`, bloque oscuro.
4. El kit activa el tema oscuro con `[data-theme="dark"]` y el repo con la clase `.dark`. **Se conserva `.dark`.**
5. El `tailwind.config.js` del kit no se copia: las 4 apps usan Tailwind v4 con `@theme inline`, sin config.

## Contraste del kit (medido 2026-11-09)

Pares de texto del kit que **no** llegan a 4,5:1 (WCAG AA, texto normal). Se aplican tal cual dice el kit y quedan aquí para que decida quien lo entregó; la propuesta ya traía una alternativa que sí cumple.

| Dónde | Par del kit | Contraste | Alternativa de la propuesta |
|---|---|---|---|
| Badge pendiente/alerta | `#d83b00` sobre naranja 12 % | 3,98:1 | `#c23c00` (5,33:1) |
| Badge info | `#557eff` sobre azul 12 % | 3,16:1 | `#3d5fd9` (5,47:1) |
| Botón principal (inicio del degradado) | blanco sobre `#557eff` | 3,61:1 | degradado `#3d5fd9 → #0f6fa8` (5,4:1) |
| Enlaces y texto de acción | `#557eff` sobre blanco | 3,61:1 | `#3d5fd9` (5,47:1) |

Cumplen: success 4,75:1, danger 5,13:1, neutral 6,03:1, purple 7,16:1, texto secundario 6,47:1, cabecera de tabla 14,92:1, y todo el tema oscuro (≥ 7:1).

## Verificación de cada bloque

1. `pnpm -r typecheck lint build` en verde.
2. Vitest de la app tocada con el mismo conteo de pasan y fallan que la línea base.
3. Recorrido Playwright: el árbol de accesibilidad igual que en la línea base (salvo dock, buscador y sección nueva de Inicio) y capturas en 1440 claro, 1440 oscuro y 375.
4. axe en claro y oscuro.
5. Un host de marca blanca revisado.
6. `CHECKLIST.md` actualizado.
