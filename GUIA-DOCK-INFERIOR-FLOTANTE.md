# Dock inferior flotante — guía de construcción portable

Cómo construir un menú principal de escritorio que vive en una **cápsula flotante al pie de la
pantalla**, abre sus paneles **hacia arriba**, **agrupa** las opciones de los módulos grandes y **se
condensa a solo iconos al bajar** por la página.

Este documento describe el mecanismo, no un proyecto concreto. Todo lo que depende de tu aplicación
—catálogo de páginas, permisos, colores, iconos, router— está aislado en el **§2 Contratos**: si
respetas esos cuatro contratos, el resto se copia tal cual. La implementación de referencia de la
que salió esta guía está en el **§13 Apéndice**.

> Los bloques marcados con ⚠️ son fallos reales que aparecieron construyéndolo, no advertencias
> teóricas. Son la parte del documento que de verdad ahorra tiempo.

---

## 1. Qué es y qué problema resuelve

```
┌──────────────────────────────────────────────────────────────┐
│  ▣ Marca            [ Buscar…  ⌘K ]                🔔  👤    │  ← cabecera (sticky)
├──────────────────────────────────────────────────────────────┤
│                                                              │
│   contenido a ancho completo, sin franja de menú             │
│                                                              │
│                    ┌───────────────────────┐                 │
│                    │ GRUPO A  │  GRUPO B   │                 │  ← panel: abre HACIA ARRIBA
│                    │ • Opción │  • Opción  │                 │     y en columnas
│                    └───────────────────────┘                 │
│      ╭───────────────────────────────────────────────╮       │
│      │ 🏠 Inicio │ 📋 Módulo ▴ │ ▰ Módulo ▴ │ ⚙ Admin ▴│      │  ← dock (fixed al pie)
│      ╰───────────────────────────────────────────────╯       │
└──────────────────────────────────────────────────────────────┘
```

Frente a una barra horizontal clásica colgada de la cabecera, el dock resuelve cuatro cosas:

| Problema típico de la barra superior | Qué hace el dock |
|---|---|
| El color de marca ocupa toda la franja: un bloque plano sin jerarquía | El color/gradiente solo rellena el **módulo activo**; el resto es vidrio neutro |
| Estado activo y hover casi idénticos | Activo = relleno de marca + sombra; hover = fondo suave. Inconfundibles |
| Un módulo con 25 opciones vuelca un muro de texto | Panel en **columnas por subgrupo** |
| La franja roba alto permanente al contenido | Es `fixed`: **no reserva alto**, y se aparta solo al bajar |

**Cuándo NO usarlo:** si tu app se usa mayoritariamente en móvil (el dock es solo para escritorio;
en pantallas pequeñas necesitas igualmente un drawer), o si tu navegación tiene un único nivel con
menos de 4 destinos (una barra simple basta y el dock añade complejidad sin ganancia).

---

## 2. Contratos: lo único que tu proyecto debe aportar

### Contrato A — Catálogo de navegación

Una lista plana de ítems, cada uno con la sección (módulo) a la que pertenece. Los nombres de campo
son tuyos; lo que importa es la forma:

```ts
type Section = string;                    // 'inicio' | 'ventas' | 'admin' | …

interface NavItem {
  to: string;                             // ruta
  label: string;
  section: Section;
  // …los campos de permiso que use tu app
}

const SECTIONS: Section[] = ['inicio', 'ventas', 'admin'];      // orden estable del dock
const SECTION_LABEL: Record<Section, string> = { /* … */ };
const ITEMS: NavItem[] = [ /* … */ ];
```

⚠️ **El orden del dock sale de `SECTIONS`, nunca del orden de `ITEMS`.** Si dependiera del array de
ítems, añadir una página al final movería un módulo de sitio sin que nadie lo pida.

Necesitas además resolver **qué sección corresponde a la ruta actual**, por prefijo más largo:

```ts
function activeSectionForPath(pathname: string, items: NavItem[]): Section | null {
  const matches = items.filter((it) =>
    it.to === '/' ? pathname === '/' : pathname === it.to || pathname.startsWith(`${it.to}/`),
  );
  if (!matches.length) return null;
  matches.sort((a, b) => b.to.length - a.to.length);   // el más específico gana
  return matches[0].section;
}
```

⚠️ Sin el «prefijo más largo», con `/ventas` y `/ventas/informes/anual` en el catálogo puede
iluminarse el módulo equivocado.

### Contrato B — Filtrado por permisos, en un solo sitio

Tu app tendrá alguna forma de decidir qué ve cada usuario. Encapsúlala en **un hook** y haz que
**todas** las navegaciones lo consuman: el dock, el drawer móvil, la paleta de comandos, un
breadcrumb… lo que tengas.

```ts
export function useNavSections() {
  const user = useCurrentUser();
  const { pathname } = useLocation();

  const visibleItems = useMemo(
    () => ITEMS.filter((it) => puedeVer(user, it)),        // ← tu regla, la que sea
    [user],
  );

  const grouped = useMemo(
    () => SECTIONS
      .map((section) => ({ section, items: visibleItems.filter((it) => it.section === section) }))
      .filter((g) => g.items.length > 0),                  // sin módulos vacíos
    [visibleItems],
  );

  const routeSection = useMemo(
    () => activeSectionForPath(pathname, visibleItems),
    [pathname, visibleItems],
  );

  return { grouped, visibleItems, routeSection };
}
```

⚠️ **Esta es la única pieza que no puedes duplicar.** Si el filtrado vive copiado en el dock y en el
drawer, tarde o temprano un rol verá en un sitio lo que no ve en el otro, y ese bug solo aparece
para el usuario que no puede reportarlo bien. Extraerlo, además, suele destapar bugs latentes: en el
proyecto de referencia apareció un `useMemo` que leía el usuario sin declararlo en sus dependencias,
así que tras cambiar de sesión el menú conservaba el filtrado anterior.

### Contrato C — Presentación por sección: icono y acento

**Separado del catálogo.** El catálogo dice *qué existe y quién lo ve*; esto dice *cómo se muestra*.
Manteniéndolos aparte, las otras navegaciones siguen leyendo el catálogo sin enterarse del dock.

```ts
export const SECTION_ICON: Record<Section, ComponentType<{ className?: string; style?: CSSProperties }>> = {
  inicio: IconHome, ventas: IconChart, admin: IconCog, /* … */
};

// Un color por módulo. Referencia tus variables de tema, no valores sueltos.
export const SECTION_ACCENT: Record<Section, string> = {
  inicio: 'var(--acento-1)', ventas: 'var(--acento-2)', admin: 'var(--acento-3)', /* … */
};
```

Requisitos de los iconos:
- **Trazo con `currentColor` o `stroke="currentColor"`**, para poder teñirlos con `color:` desde
  fuera (el icono debe volverse blanco cuando la píldora está activa).
- **Tamaño uniforme** (16–18 px en el dock) y `aria-hidden`: el nombre accesible lo pone el texto.
- ⚠️ **El mapa debe ser inyectivo**: dos módulos con el mismo icono pasan desapercibidos en un
  drawer con títulos, pero en el dock —donde el icono es la única marca en modo condensado— la
  colisión es evidente y confunde de verdad.

### Contrato D — Tokens visuales

El dock necesita estas variables. Los valores son tuyos; los nombres, orientativos:

```css
:root {
  /* superficies */
  --nav-vidrio:        rgba(255, 255, 255, 0.94);   /* fondo de la cápsula */
  --nav-vidrio-dark:   rgba(22, 39, 68, 0.90);      /* su equivalente en tema oscuro */
  --nav-panel-bg:      #FFFFFF;
  --nav-borde:         #DDE5F0;
  --nav-app-bg:        #EAF2FF;                     /* fondo del hover */

  /* texto */
  --nav-texto:         #59677D;
  --nav-texto-fuerte:  #162744;
  --nav-texto-tenue:   #667085;

  /* activo */
  --nav-activo:        linear-gradient(90deg, #4FD4CC 0%, #4F74C9 100%);  /* o un color plano */

  /* forma y profundidad */
  --nav-radio-pill:    999px;
  --nav-radio-panel:   18px;
  --nav-sombra-dock:   0 8px 24px rgba(22, 39, 68, 0.08);
  --nav-sombra-panel:  0 24px 60px rgba(22, 39, 68, 0.18);
  --nav-sombra-activo: 0 10px 22px rgba(79, 116, 201, 0.22);

  /* movimiento */
  --nav-duracion:      180ms;
  --nav-ease:          cubic-bezier(0.2, 0, 0, 1);
}
```

⚠️ **Si usas Tailwind, expón estos tokens como *utilities*, no los consumas solo con `style={{}}`
inline.** Un color que solo existe en un estilo inline **no admite variantes**: no puedes escribir
`hover:`, `focus-visible:` ni `aria-[current=page]:` sobre él, y el dock los necesita los tres. En
Tailwind 4 basta un bloque aditivo:

```css
@theme inline {
  --color-nav-app:    var(--nav-app-bg);
  --color-nav-ink:    var(--nav-texto-fuerte);
  --color-nav-soft:   var(--nav-borde);
  --radius-nav-pill:  var(--nav-radio-pill);
  /* … */
}
```

Con `inline`, Tailwind no emite CSS para las utilities que nadie usa, así que mapear el set completo
no pesa en el bundle.

> **Sin Tailwind:** todas las clases de utilidad que aparecen abajo tienen equivalente directo en
> CSS plano. Lo único que Tailwind aporta aquí es concisión; ninguna decisión del dock depende de él.

---

## 3. Anatomía: cinco piezas

| Pieza | Responsabilidad | Sabe de tu dominio |
|---|---|---|
| `useNavSections` | Filtrado por permisos + agrupación por sección | **sí** (Contrato B) |
| `sectionMeta` | Icono, acento y subgrupos por sección | **sí** (Contratos C y §5) |
| `useDisclosureNav` | Apertura/cierre, teclado, foco, click-outside | no |
| `useEdgeClamp` | Que el panel no se salga del viewport | no |
| `<Dock />` | Composición, píldoras, panel y condensado por scroll | no |

Las tres últimas son portables sin tocar una línea.

---

## 4. Paso 1 — Ubicación: dónde va el dock y en qué capa

Es el paso que más problemas silenciosos causa, así que va primero y completo.

### 4.1 El contenedor y la cápsula

```tsx
// Contenedor: ancho completo (para poder centrar) y TRANSPARENTE A LOS CLICS.
<div className="pointer-events-none fixed inset-x-0 bottom-0 z-30 hidden justify-center px-4 pb-5 lg:flex">
  <nav
    ref={navRef}
    aria-label="Navegación principal"
    className="dock-capsula pointer-events-auto relative inline-flex max-w-full flex-wrap
               items-center justify-center gap-0.5 rounded-nav-pill p-1.5
               transition-[height,padding] duration-200 ease-out motion-reduce:transition-none"
  >
    {/* píldoras */}
  </nav>
</div>
```

```css
.dock-capsula {
  background: var(--nav-vidrio);
  border: 1px solid var(--nav-borde);
  box-shadow: var(--nav-sombra-dock);
  backdrop-filter: blur(14px);
}
[data-theme='dark'] .dock-capsula { background: var(--nav-vidrio-dark); }
```

Equivalente sin Tailwind del contenedor:

```css
.dock-wrap {
  position: fixed; inset-inline: 0; bottom: 0; z-index: 30;
  display: none; justify-content: center;
  padding: 0 1rem 1.25rem;
  pointer-events: none;                      /* ← imprescindible */
}
@media (min-width: 1024px) { .dock-wrap { display: flex; } }
.dock-capsula { pointer-events: auto; }      /* ← imprescindible */
```

⚠️ **`pointer-events: none` en el contenedor y `auto` en la cápsula.** El contenedor cubre todo el
ancho porque es la forma de centrar la cápsula sin conocer su tamaño. Sin esto dejas una **banda
invisible de ~70 px al pie de la pantalla que se traga todos los clics** de la página: el usuario ve
un botón, hace clic y no pasa nada. Es un bug que cuesta horas encontrar porque no se ve nada raro.

⚠️ **`flex-wrap` como red de seguridad, pero calibrado para no usarlo.** Ajusta `gap` y `padding`
hasta que **el rol con más módulos quepa en una sola fila** en tu ancho objetivo. Un dock partido en
dos filas pierde toda su gracia: deja de leerse como una cápsula.

### 4.2 No reservar alto arriba (y el efecto dominó)

Al ser `fixed`, el dock **no ocupa sitio en el flujo**. Si tu shell tenía una franja de navegación
bajo la cabecera, probablemente haya páginas con offsets `sticky` calculados sobre su altura:

```css
top: calc(var(--alto-cabecera) + var(--alto-navegacion));
```

⚠️ **No borres la variable de altura de navegación: ponla a `0px`.**

```css
/* Alto que la navegación reserva bajo la cabecera. Desde que es un dock fijo al
   pie, no reserva ninguno. Se conserva la variable para no reescribir los calc()
   de las páginas y para que reponer una franja superior sea cambiar un valor. */
--alto-navegacion: 0px;
```

Borrarla te obliga a editar cada página con `sticky` y a acertar en todas; ponerla a cero deja todos
los `calc()` correctos de golpe y reversibles.

### 4.3 Colchón al final de la página

El dock flota **sobre** el contenido, así que al llegar al final tapa lo último que haya (pie legal,
último botón de un formulario largo). Añade padding inferior en escritorio al último bloque, o al
propio `<main>`:

```tsx
<footer className="hidden px-4 pb-4 sm:px-6 lg:block lg:px-8 lg:pb-28">…</footer>
//                                                          ↑ hueco del dock
```

⚠️ Es fácil dar esto por bueno mirando la pantalla: el contenido **se sigue pintando** debajo del
dock traslúcido, así que "se ve". La comprobación correcta no es «¿se ve?» sino «¿qué elemento hay
realmente en ese punto?» — ver §11.

### 4.4 Capa y convivencia con modales

Escalera típica:

| Capa | z-index | Dónde vive |
|---|---|---|
| Cabecera | 30 (sticky) | hijo del shell |
| Dock y sus paneles | 30 (fixed) | hijo del shell |
| Modales / overlays | 60 | **portal a `<body>`** |

⚠️ **Si tu shell es `display: flex` y los modales se renderizan dentro de `<main>`, subir su
`z-index` NO los pondrá por encima del dock.** Por la especificación de Flexbox (*painting order*),
un item flex se pinta **atómicamente**, «igual que un inline-block»: nada de su interior puede
superar a un hermano flex con `z-index` positivo — ni siquiera un `position: fixed`, ni con
`z-index: 9999`. El síntoma es desconcertante: la cabecera del modal queda bajo la barra y su botón
de cerrar se vuelve inalcanzable con el ratón.

La solución no es un número, es el árbol: **portal a `<body>`**.

```tsx
export default function OverlayPortal({ children }: { children: ReactNode }) {
  return createPortal(children, document.body);
}
```

Y no: dar `z-index` a `<main>` tampoco vale — pondría todo el contenido de la página por encima del
dock, y entonces las tablas taparían los paneles del menú.

### 4.5 Móvil

El dock es **solo escritorio** (`hidden lg:flex`). En pantallas pequeñas ocuparía una porción
enorme, competiría con la barra de gestos del sistema y no tiene sitio para desplegar paneles hacia
arriba. Mantén tu drawer para `<lg`; lo único que ambos deben compartir es el hook del Contrato B.

---

## 5. Paso 2 — Ítems contenidos: secciones, subgrupos y columnas

El dock muestra **secciones**, no páginas. Cada píldora es una sección; sus páginas viven dentro del
panel. Y los paneles grandes se parten en **subgrupos que se pintan como columnas**.

### 5.1 Declarar los subgrupos, fuera del catálogo

Indéxalos por una clave estable del ítem (la ruta es la mejor candidata):

```ts
// Solo lo necesitan los módulos grandes. El resto no declara nada.
const ITEM_GROUP: Record<string, string> = {
  '/ventas/pedidos':   'Operación',
  '/ventas/clientes':  'Maestros',
  '/ventas/informes':  'Análisis',
  // …
};

// Orden estable de las columnas por sección. Lo no listado va al final.
const GROUP_ORDER: Record<Section, string[]> = {
  ventas: ['Operación', 'Maestros', 'Análisis'],
};
```

### 5.2 La función que reparte

```ts
export interface ItemGroup { title: string | null; items: NavItem[]; }

export function groupItems(section: Section, items: NavItem[]): ItemGroup[] {
  const order = GROUP_ORDER[section];
  if (!order) return [{ title: null, items }];      // sección sin subgrupos → una columna sin título

  const buckets = new Map<string, NavItem[]>();
  const ungrouped: NavItem[] = [];
  for (const it of items) {
    const g = ITEM_GROUP[it.to] ?? null;
    if (!g) { ungrouped.push(it); continue; }
    const b = buckets.get(g);
    if (b) b.push(it); else buckets.set(g, [it]);
  }

  // Primero los conocidos en su orden; luego los que aparezcan por una clave
  // nueva sin entrada en GROUP_ORDER — así NUNCA se pierde un ítem.
  const extras = [...buckets.keys()].filter((g) => !order.includes(g)).sort();
  const result: ItemGroup[] = [];
  for (const title of [...order, ...extras]) {
    const b = buckets.get(title);
    if (b?.length) result.push({ title, items: b });
  }
  if (ungrouped.length) result.push({ title: null, items: ungrouped });
  return result;
}
```

⚠️ **Los cubos `extras` y `ungrouped` no son paranoia, son el requisito principal de esta función.**
Cuando alguien añada una página nueva y olvide declarar su subgrupo, el ítem **sigue apareciendo**
—en una columna sin título, al final— en vez de desaparecer del menú en silencio. Un menú que oculta
páginas por un olvido de metadatos genera tickets imposibles de diagnosticar («a mí no me sale esa
opción»).

⚠️ **Que una sección sin subgrupos devuelva `[{ title: null, … }]` es lo que permite renderizar
ambos casos con el mismo JSX.** Si devuelves `null` o un array vacío acabas con dos ramas de render
que divergen a la primera modificación.

### 5.3 Regla: una sola opción → enlace directo

```tsx
if (items.length === 1) {
  const it = items[0];
  return <NavLink to={it.to} …>{icono}{etiqueta(it.label)}</NavLink>;   // sin panel
}
```

Y muestra el **label del ítem**, no el de la sección: si a un rol le queda una sola página en
«Ventas», la píldora debe decir «Pedidos». Un panel de un elemento es un clic de más sin información
nueva.

### 5.4 El panel

```tsx
<div
  ref={panelRef}
  id={panelId(section)}
  className="panel-up absolute bottom-full left-0 z-30 mb-2.5 overflow-y-auto rounded-nav-panel
             border border-nav-soft bg-nav-card p-2"
  style={{
    boxShadow: 'var(--nav-sombra-panel)',
    minWidth: multiColumn ? undefined : '16rem',     // ancho mínimo cómodo si es 1 columna
    maxHeight: 'min(70vh, 32rem)',                   // + overflow-y-auto
    left: shift ? `${shift}px` : undefined,          // ← §7 useEdgeClamp
    ['--acc' as string]: SECTION_ACCENT[section],    // ← acento local del panel
  } as CSSProperties}
>
  <div className={multiColumn ? 'flex gap-1' : undefined}>
    {groups.map((g) => (
      <div key={g.title ?? '_'} className={multiColumn ? 'min-w-[13.5rem] flex-1' : undefined}>
        {g.title && multiColumn && (
          <p className="px-3 pb-1 pt-2 text-[10px] font-semibold uppercase tracking-[0.18em] text-nav-tenue">
            {g.title}
          </p>
        )}
        <ul className="flex flex-col gap-0.5">
          {g.items.map((it) => (
            <li key={it.to}>
              <NavLink
                to={it.to}
                end={it.to === '/'}
                className="group flex items-center gap-2.5 rounded-lg px-3 py-2 text-sm
                           text-nav-texto transition-colors hover:bg-nav-app hover:text-nav-ink
                           aria-[current=page]:bg-nav-app aria-[current=page]:font-semibold
                           aria-[current=page]:text-nav-ink"
              >
                <span aria-hidden="true"
                      className="h-1.5 w-1.5 shrink-0 rounded-full bg-nav-soft transition-colors
                                 group-hover:bg-[var(--acc)]
                                 group-aria-[current=page]:bg-[var(--acc)]" />
                <span className="truncate">{it.label}</span>
              </NavLink>
            </li>
          ))}
        </ul>
      </div>
    ))}
  </div>
</div>
```

Tres decisiones que merece la pena copiar:

- **`--acc` como variable local del panel.** Es lo que permite que el puntito del ítem tome el
  acento de *su* sección en `hover` y en «página actual». Con un color dinámico en `style={{}}`
  inline no podrías expresar esas dos variantes.
- **Estilar desde `aria-current="page"`** (que tu router ya pone en el link activo) en lugar de una
  clase calculada: el atributo es a la vez la semántica accesible y el gancho visual, así no pueden
  desincronizarse.
- **`max-height` + `overflow-y-auto`.** Una sección enorme en una pantalla baja se desplaza por
  dentro del panel en vez de salirse por el techo.

### 5.5 La píldora, y el contraste activo/hover

```tsx
const lit = isRouteSection || isOpen;      // iluminado = sección de la ruta actual O panel abierto

const base = `flex ${condensed ? 'h-9' : 'h-11'} items-center gap-2 whitespace-nowrap
              rounded-nav-pill px-3 text-sm transition-all duration-200 ease-out
              motion-reduce:transition-none`;

const tono = lit
  ? 'font-semibold text-white'
  : 'dock-pill font-medium text-nav-texto hover:bg-nav-app hover:text-nav-ink';

const estiloLit = lit
  ? { background: 'var(--nav-activo)', boxShadow: 'var(--nav-sombra-activo)' }
  : undefined;

// El icono se tiñe: blanco sobre el relleno activo, acento de la sección si no.
<SectionIcon className="h-[18px] w-[18px] shrink-0 transition-colors"
             style={{ color: lit ? '#FFFFFF' : SECTION_ACCENT[section] }} />
```

⚠️ **Que el activo y el hover se distingan sin pensarlo es la mitad del valor del rediseño.** La
regla práctica: el activo cambia **relleno + peso tipográfico + sombra**; el hover cambia solo el
fondo, y suave. Dos opacidades del mismo blanco (20% vs 15%) no son un estado, son ruido.

⚠️ La clase `dock-pill` del ejemplo **no pinta nada**: existe solo como asa para que el tema oscuro
pueda alcanzar el texto de las píldoras no activas (§9). La píldora activa va sobre relleno de marca
y ya es blanca en ambos temas.

---

## 6. Paso 3 — Apertura hacia arriba

Desde el pie, la única dirección posible es hacia arriba. No hace falta lógica de «arriba u abajo
según el espacio»: el dock está siempre abajo.

```
panel:  position: absolute;  bottom: 100%;  left: 0;  margin-bottom: .625rem;
```

En Tailwind: `absolute bottom-full left-0 mb-2.5`.

⚠️ **El chevron va al revés que en un menú superior.** Cerrado apunta **arriba** (porque hacia allí
abrirá), abierto vuelve a su posición neutra:

```tsx
<IconChevronDown className={`transition-transform duration-200 motion-reduce:transition-none
                             ${isOpen ? 'rotate-0' : 'rotate-180'}`} />
```

Copiar la convención de un menú desplegable de arriba deja la flecha apuntando al lado contrario al
que abre. El usuario lo nota sin saber decir por qué.

Animación de entrada, coherente con la dirección:

```css
@keyframes panel-up {
  from { opacity: 0; transform: translateY(6px) scale(0.985); }
  to   { opacity: 1; transform: translateY(0)   scale(1);     }
}
.panel-up {
  animation: panel-up var(--nav-duracion) var(--nav-ease) both;
  transform-origin: bottom center;
}
@media (prefers-reduced-motion: reduce) { .panel-up { animation: none; } }
```

⚠️ **Verifica que las clases de animación que copies existan de verdad.** Utilidades como
`animate-in` / `fade-in` / `slide-in-from-bottom-2` pertenecen a `tailwindcss-animate`; si ese
plugin no está instalado son **no-ops silenciosos** y te pasas media tarde ajustando una animación
que nunca se ejecutó. Un `grep` del nombre del keyframe en tu CSS resuelve la duda en un segundo.

---

## 7. Paso 4 — Que el panel no se salga de la pantalla

El panel cuelga de su píldora (`left: 0`). Un panel ancho en una píldora de la mitad derecha se sale
por el borde. Este hook devuelve el desplazamiento mínimo:

```ts
const GUTTER = 12;   // margen que se respeta contra el borde del viewport

export function useEdgeClamp<T extends HTMLElement>(open: string | null) {
  const ref = useRef<T>(null);
  const [shift, setShift] = useState(0);

  useLayoutEffect(() => {
    if (!open) { setShift(0); return; }
    const el = ref.current;
    const anchor = el?.offsetParent as HTMLElement | null;   // la píldora
    if (!el || !anchor) return;

    const compute = () => {
      const left = anchor.getBoundingClientRect().left;
      const overflow = left + el.offsetWidth - (window.innerWidth - GUTTER);
      setShift(overflow > 0 ? -overflow : 0);
    };
    compute();
    window.addEventListener('resize', compute);
    return () => window.removeEventListener('resize', compute);
  }, [open]);

  return { ref, shift };
}
```

⚠️ **Mide el rect del ANCLA y el `offsetWidth` del panel — nunca el rect del propio panel.** Si mides
`panel.getBoundingClientRect().left`, el desplazamiento ya aplicado contamina la siguiente medida y
el cálculo oscila.

⚠️ **Aplica el desplazamiento sobre `left`, NO sobre `transform`.** El panel entra con una animación
que anima `transform` con `animation-fill-mode: both`, y **una animación CSS gana sobre el estilo
inline**: tu `translateX` quedaría pisado para siempre por el fotograma final. Es un fallo mudo — el
estilo está ahí, en el DOM, y no hace nada.

⚠️ **`useLayoutEffect`, no `useEffect`**: hay que corregir la posición antes del paint, o el usuario
ve el panel salirse y volver.

---

## 8. Paso 5 — El condensado por scroll

El efecto: **al bajar por la página el dock se encoge y deja solo los iconos; al subir, recupera los
nombres.** Es lo que permite que un menú flotante permanente no estorbe.

```tsx
const EXPAND_ZONE = 96;               // scroll por debajo del cual está SIEMPRE expandido
const LOCKOUT_MS  = 250;              // ver ⚠️ abajo
const UMBRAL_BAJAR  = 4;              // px de scroll hacia abajo para condensar
const UMBRAL_SUBIR  = 8;              // px de scroll hacia arriba para expandir

const [condensed, setCondensed] = useState(false);
const condensedRef = useRef(false);   // lectura viva dentro del listener

useEffect(() => {
  let lastY = window.scrollY;
  let ticking = false;
  let lockUntil = 0;

  const onScroll = () => {
    if (ticking) return;              // 1 lectura por frame como máximo
    ticking = true;
    requestAnimationFrame(() => {
      const y = window.scrollY;
      const now = performance.now();

      if (now >= lockUntil) {
        let next: boolean | null = null;
        if (y <= EXPAND_ZONE)              next = false;   // zona alta: siempre expandido
        else if (y > lastY + UMBRAL_BAJAR) next = true;    // bajando  → condensa
        else if (y < lastY - UMBRAL_SUBIR) next = false;   // subiendo → expande

        if (next !== null && next !== condensedRef.current) {
          condensedRef.current = next;
          lockUntil = now + LOCKOUT_MS;                    // ⬅ el LOCKOUT
          setCondensed(next);
        }
      }
      lastY = y;
      ticking = false;
    });
  };

  window.addEventListener('scroll', onScroll, { passive: true });
  return () => window.removeEventListener('scroll', onScroll);
}, []);

// Abrir un panel fuerza la expansión: navegar entre iconos sueltos desorienta.
useEffect(() => {
  if (!openSection) return;
  condensedRef.current = false;
  setCondensed(false);
}, [openSection]);
```

Y lo que cambia visualmente al condensarse:

```tsx
const alturaPill = condensed ? 'h-9' : 'h-11';                    // píldora más baja
const etiqueta = (t: string) => <span className={condensed ? 'sr-only' : 'truncate'}>{t}</span>;
{!condensed && <IconChevronDown … />}                              // el chevron desaparece
// la cápsula transiciona: transition-[height,padding] duration-200 ease-out
```

### Las cuatro trampas del condensado

⚠️ **1. Sin el lockout, el dock entra en bucle infinito.** Al condensarse cambia el layout; el
navegador **reajusta el scroll** para compensar; ese reajuste llega al listener como un «scroll hacia
arriba» que reexpande el dock; lo que vuelve a cambiar el layout… Se manifiesta como un parpadeo
frenético e inexplicable. La cura es ignorar el scroll durante un instante después de cada cambio de
estado — justo la duración del rebote inducido. 250 ms es un punto de partida sensato; si tu
transición dura más, súbelo.

⚠️ **2. Umbrales asimétricos.** Condensar pide 4 px, expandir pide 8. Expandir es la acción «cara»
(mueve más layout y reaparece texto), así que exigirle un gesto más decidido evita el parpadeo con
el scroll inercial de los trackpads.

⚠️ **3. `condensedRef` en paralelo al estado.** El listener se registra una sola vez (deps `[]`) y
por tanto **captura el `condensed` inicial para siempre**. El ref es la lectura viva. Meter
`condensed` en las dependencias re-registraría el listener en cada cambio y perdería `lastY` y
`lockUntil`, que son estado local del listener.

⚠️ **4. La etiqueta se queda en el DOM como `sr-only`, no se desmonta.** Si la quitas, el **nombre
accesible del botón desaparece** a mitad de scroll: un lector de pantalla anuncia «botón» a secas, y
cualquier test que localice por rol y nombre (`getByRole('button', { name: 'Ventas' })`) empieza a
fallar en cuanto la página tiene scroll. `sr-only` conserva el nombre y solo deja de verse.

**Zona de expansión (`EXPAND_ZONE`).** Estar cerca del inicio de la página debe ganar siempre a la
dirección del gesto: si no, un micro-scroll hacia abajo en la primera pantalla condensa el dock sin
que haya nada que ganar en espacio.

---

## 9. Paso 6 — Teclado, foco y accesibilidad

Patrón **disclosure** de WAI-ARIA APG: `<button aria-expanded aria-controls>` + una lista de enlaces.

⚠️ **Sin `role="menu"`.** Ese rol obliga a navegación por flechas y a `role="menuitem"`, y le quita a
tus enlaces la semántica de enlace ante un lector de pantalla. Un menú de navegación con links es un
disclosure, no un menú de aplicación.

```ts
export function useDisclosureNav(idPrefix: string) {
  const { pathname } = useLocation();
  const navRef = useRef<HTMLElement>(null);
  const [openSection, setOpenSection] = useState<Section | null>(null);

  const triggerId = useCallback((s: Section) => `${idPrefix}-trigger-${s}`, [idPrefix]);
  const panelId   = useCallback((s: Section) => `${idPrefix}-panel-${s}`,   [idPrefix]);

  const toggle = useCallback((s: Section) => {
    setOpenSection((prev) => (prev === s ? null : s));
  }, []);

  // 1) Navegar cierra el panel.
  useEffect(() => { setOpenSection(null); }, [pathname]);

  // 2) pointerdown FUERA cierra.
  useEffect(() => {
    if (!openSection) return;
    const onPointerDown = (e: PointerEvent) => {
      if (!navRef.current?.contains(e.target as Node)) setOpenSection(null);
    };
    window.addEventListener('pointerdown', onPointerDown);
    return () => window.removeEventListener('pointerdown', onPointerDown);
  }, [openSection]);

  // 3) Escape cierra Y DEVUELVE EL FOCO al trigger.
  useEffect(() => {
    if (!openSection) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        const trigger = navRef.current?.querySelector<HTMLButtonElement>(
          `#${CSS.escape(triggerId(openSection))}`,
        );
        setOpenSection(null);
        trigger?.focus();
        return;
      }
      // Si tu app tiene paleta de comandos: que su atajo cierre el panel, o los
      // dos Escape compiten y el usuario tiene que pulsarlo dos veces.
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') setOpenSection(null);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [openSection, triggerId]);

  return { openSection, toggle, close: () => setOpenSection(null), navRef, triggerId, panelId };
}
```

Detalles que parecen menores:

- **`pointerdown`, no `mousedown` ni `click`.** `pointerdown` cubre táctil en tablets con viewport de
  escritorio; y se cierra en `down` y no en `click` porque un desplegable de navegación **no es un
  modal**: no le aplica la regla de «cierre explícito», debe apartarse en cuanto empiezas a
  interactuar fuera.
- **`CSS.escape`** al recomponer el id del trigger: si el nombre de una sección lleva un carácter que
  necesite escape, el `querySelector` no revienta.
- **`idPrefix` como parámetro**: permite montar el mismo hook en dos navegaciones sin colisionar en
  el DOM, y da ids estables (`dock-panel-ventas`) para los tests.
- **Foco visible obligatorio** en píldoras y enlaces del panel (WCAG 2.2). Un anillo propio si tu
  `outline` por defecto se pierde sobre el vidrio.
- **`prefers-reduced-motion`**: sin animación de panel y sin transición de la cápsula.

### Tema oscuro

⚠️ Si tu superficie de vidrio **sí** invierte en tema oscuro pero tus tokens de texto **no** tienen
variante dark, las píldoras quedan texto oscuro sobre fondo oscuro: ilegibles. Cubre el caso con una
regla acotada al dock en vez de esperar a la migración global del tema:

```css
[data-theme='dark'] .dock-capsula .dock-pill        { color: rgba(255,255,255,0.78); }
[data-theme='dark'] .dock-capsula .dock-pill:hover  { background: rgba(255,255,255,0.08); color: #fff; }
```

La píldora activa no necesita nada: va sobre el relleno de marca y ya es blanca en ambos temas.

---

## 10. Constantes a calibrar al portarlo

| Constante | Valor de referencia | Qué mirar para ajustarla |
|---|---|---|
| `EXPAND_ZONE` | 96 px | Alto de tu cabecera / cuánto «arriba» es arriba |
| `LOCKOUT_MS` | 250 ms | ≥ la duración de tu transición de condensado |
| Umbral bajar / subir | 4 / 8 px | Sensibilidad del trackpad; súbelos si parpadea |
| `GUTTER` (edge clamp) | 12 px | Respiro contra el borde del viewport |
| Alto píldora expandida / condensada | 44 / 36 px | Que la píldora siga siendo diana táctil cómoda |
| `max-height` del panel | `min(70vh, 32rem)` | Pantallas bajas (portátiles de 768 px de alto) |
| Ancho mínimo de columna | ~13.5 rem | Longitud de tus etiquetas más largas |
| Breakpoint del dock | `lg` (1024 px) | Dónde tu drawer deja de ser mejor opción |
| `gap` + `padding` de la cápsula | `gap .125rem`, `p .375rem` | Que el rol con más módulos quepa en UNA fila |

---

## 11. Cómo verificarlo (y por qué los tests obvios no sirven)

⚠️ **`toBeVisible()` da verde aunque el elemento esté tapado.** Un elemento pintado, con tamaño y sin
`display:none` es «visible» aunque el dock esté encima. Para todo lo que este componente pone en
juego —solapamientos, dirección de apertura, desbordes— hay que preguntar **geometría** o **qué hay
realmente en ese pixel**:

```ts
// 1) Dirección de apertura y contención dentro del viewport.
const dock  = (await nav.boundingBox())!;
const panel = (await page.locator('#dock-panel-ventas').boundingBox())!;
expect(panel.y + panel.height).toBeLessThanOrEqual(dock.y + 1);   // abre hacia ARRIBA
expect(panel.y).toBeGreaterThanOrEqual(0);                        // no se sale por el techo
expect(panel.x).toBeGreaterThanOrEqual(0);
expect(panel.x + panel.width).toBeLessThanOrEqual(anchoViewport);

// 2) «¿Qué elemento hay REALMENTE en ese punto?» — para el pie tapado por el dock,
//    o el botón de cerrar de un modal tapado por la navegación.
const encima = document.elementFromPoint(x, y);
const alcanzable = objetivo.contains(encima) || encima === objetivo;

// 3) La NO oscilación se prueba esperando: condensar, aguantar, volver a medir.
await page.mouse.wheel(0, 600);
await expect.poll(altoDelDock).toBeLessThan(altoExpandido);
await page.waitForTimeout(700);
expect(await altoDelDock()).toBeLessThan(altoExpandido);   // sigue condensado

// 4) Espera el fin de la animación antes de medir: los 6px de translateY falsean el rect.
await page.waitForTimeout(300);

// 5) Para probar el condensado hace falta scroll REAL: si tu página de prueba no
//    llega a un alto de pantalla, inyecta relleno.
document.querySelector('main')?.appendChild(relleno /* height: 2400px */);
```

⚠️ Al localizar el título de un subgrupo, filtra por el elemento del título y no por el texto suelto:
es muy fácil que un subgrupo **se llame igual que uno de los ítems del propio panel**.

```ts
page.locator(`#${panelId} p`).filter({ hasText: new RegExp(`^${grupo}$`) });
```

### Checklist antes de darlo por hecho

- [ ] El rol con más módulos cabe en **una fila** en tu ancho objetivo.
- [ ] El panel más ancho **no se sale** por la derecha en tu ancho mínimo de escritorio.
- [ ] La sección de la ruta actual, y solo ella, aparece rellena.
- [ ] Una sección con un único ítem es un enlace, no un botón.
- [ ] `Tab` recorre el dock, `Enter` abre, `Escape` cierra **y devuelve el foco**.
- [ ] Con `prefers-reduced-motion: reduce` no anima ni el panel ni la cápsula.
- [ ] En tema oscuro los nombres se leen.
- [ ] Al bajar se condensa y **se queda** condensado (sin bucle).
- [ ] Al final de la página, el último bloque de contenido es alcanzable, no tapado.
- [ ] Un modal grande queda por encima del dock y su botón de cerrar es clicable.
- [ ] La variable de alto de navegación vale `0px` y ningún `sticky` quedó descolocado.
- [ ] Lo que ve un rol en el dock coincide **exactamente** con las demás navegaciones.
- [ ] Ninguna dependencia nueva en `package.json`.

---

## 12. Las trampas, en una tabla

| # | Trampa | Consecuencia si se ignora | Solución |
|---|---|---|---|
| 1 | Contenedor a ancho completo sin `pointer-events: none` | Banda invisible al pie que se traga los clics | `none` fuera, `auto` en la cápsula |
| 2 | Edge clamp sobre `transform` | La animación con `fill-mode: both` lo pisa: el estilo no hace nada | Aplicarlo sobre `left` |
| 3 | Medir el rect del propio panel para el clamp | El cálculo oscila | Medir el rect del ancla + `offsetWidth` |
| 4 | Condensado sin lockout | Bucle condensar/expandir por el rebote de scroll | Bloqueo de ~250 ms tras cada cambio |
| 5 | Leer el estado `condensed` dentro del listener | Captura el valor inicial para siempre | Un `ref` en paralelo |
| 6 | Desmontar la etiqueta al condensar | El botón pierde su nombre accesible; rompe los tests | `sr-only` en vez de desmontar |
| 7 | Chevron con la convención de un menú superior | Apunta al lado contrario al que abre | Cerrado `rotate-180`, abierto `rotate-0` |
| 8 | Copiar clases de animación de otro componente | No-ops silenciosos si el plugin no está instalado | Keyframes propios y verificados |
| 9 | Subir el `z-index` del modal | Flexbox pinta el item atómicamente: sigue tapado | Portal a `<body>` |
| 10 | Borrar la variable de alto de navegación | Rompe los offsets `sticky` de varias páginas | Ponerla a `0px` |
| 11 | Duplicar el filtrado por permisos | Un rol ve en un menú lo que no ve en otro | Un hook compartido |
| 12 | Perder ítems sin subgrupo declarado | Páginas que desaparecen del menú en silencio | Cubos `extras` + `ungrouped` |
| 13 | Dos secciones con el mismo icono | Colisión invisible en un drawer, evidente en el dock | Mapa de iconos inyectivo |
| 14 | `role="menu"` en el desplegable | Fuerza navegación por flechas y rompe la semántica de enlace | Patrón disclosure APG |
| 15 | Validar solapamientos con `toBeVisible()` | Da verde con el bug puesto | Geometría + `elementFromPoint` |

---

## 13. Apéndice — Implementación de referencia

Origen de esta guía: monorepo FLITO (`apps/web`, Vite + React 18 + React Router 6 + Tailwind 4),
Feature **#11142**, HU **#11143**, commit `7a620d3` / PR #58. Correspondencia con el §3:

| Pieza de la guía | Archivo real |
|---|---|
| Catálogo (Contrato A) | `src/components/shell/navItems.ts` |
| `useNavSections` (Contrato B) | `src/components/shell/useNavSections.ts` |
| `sectionMeta` (Contrato C + §5) | `src/components/shell/sectionMeta.ts` |
| `useDisclosureNav` | `src/components/shell/useDisclosureNav.ts` |
| `useEdgeClamp` | `src/components/shell/useEdgeClamp.ts` |
| `<Dock />` | `src/components/flit/FlitNavBar.tsx` |
| Composición del shell | `src/components/flit/AppShell.tsx` |
| Portal de overlays | `src/components/flit/ModalPortal.tsx` |
| Tokens y keyframes | `src/styles/flit-tokens.css`, `src/index.css` |
| E2E | `e2e/tests/shell-navbar.spec.ts` |

Particularidades de esa implementación que **no** son requisito del patrón: 10 secciones, dos de
ellas con subgrupos (una en 4 columnas, otra en 5); ids con prefijo `flit-navbar`; variable de alto
`--flit-navbar-height: 0px`; drawer móvil por debajo de `lg` que comparte el hook de permisos pero
usa acordeón multi-abierto con persistencia en `sessionStorage` —a propósito: un drawer se recorre
con el pulgar, un dock con el cursor—; y `view-transition-name` sobre la cápsula para que persista
entre navegaciones mientras el contenido hace cross-fade.
