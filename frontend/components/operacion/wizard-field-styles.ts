/**
 * Clases de campo del wizard, en el lenguaje visual de la propuesta: input de radio `xl`, borde
 * de marca y foco en azul FLIT.
 *
 * Existe porque la misma cadena de clases estaba escrita a mano en 11 componentes del asistente,
 * con pequeñas divergencias entre copias (unas traían el estado inválido, otras no; unas el fondo
 * de modo oscuro, otras no). Un solo sitio evita que vuelvan a separarse.
 *
 * Son constantes de estilo, no componentes: cada formulario sigue montando su propio `<input>` /
 * `<select>` con su lógica, que es lo que NO se toca.
 */

/**
 * Campo de texto. Incluye:
 * - `focus:ring` además del cambio de borde — el borde solo es una señal de foco muy débil, y el
 *   guardián exige foco visible en todo interactivo.
 * - `aria-[invalid=true]` en naranja de alerta, para que el estado de error no dependa de que
 *   cada formulario se acuerde de pintarlo.
 */
// HU #14482 (propuesta): `.control` del prototipo — 44 px, contorno de control visible, superficie
// elevada y texto de 14px. El error sigue en el naranja de alerta (`aria-invalid`).
export const WIZARD_INPUT =
  'min-h-11 w-full rounded-xl border border-[color:var(--di-border-control)] bg-[var(--di-surface-elevated)] px-3.5 py-2.5 text-sm ' +
  'text-[color:var(--di-text-primary)] placeholder:text-[color:var(--di-text-tertiary)] outline-none transition ' +
  'focus:border-[color:var(--di-action)] focus:ring-2 focus:ring-flit-brand/20 ' +
  'aria-[invalid=true]:border-flit-alert ' +
  'disabled:cursor-not-allowed disabled:opacity-60';

/**
 * `<select>` nativo. Igual que el campo de texto, PERO con `[color-scheme:light]` forzado para
 * que el dropdown nativo use tema claro en cualquier sistema operativo — sin esto, en OS con tema
 * oscuro activado, el popup del `<select>` queda con fondo negro y texto negro, ilegible.
 * También añade `hover:border-flit-brand` (señal de foco pre-click) y elimina
 * `dark:bg-[#162744]` (el fondo oscuro del OS ya está cubierto por color-scheme:light en la caja).
 */
// HU #14482 — mismo `.control` que el campo de texto. El esquema de color sigue al tema (claro en
// claro, oscuro en oscuro), así que el desplegable nativo ya no queda negro sobre negro.
export const WIZARD_SELECT =
  'min-h-11 w-full rounded-xl border border-[color:var(--di-border-control)] bg-[var(--di-surface-elevated)] px-3.5 py-2.5 text-sm ' +
  'text-[color:var(--di-text-primary)] placeholder:text-[color:var(--di-text-tertiary)] outline-none transition ' +
  'focus:border-[color:var(--di-action)] focus:ring-2 focus:ring-flit-brand/20 ' +
  'hover:border-[color:var(--di-action)] ' +
  'aria-[invalid=true]:border-flit-alert ' +
  'disabled:cursor-not-allowed disabled:opacity-60 ' +
  '[color-scheme:light] dark:[color-scheme:dark]';

/**
 * Etiqueta sobre el campo. 12px es el piso tipográfico del sistema.
 *
 * El color es `color.text.secondary` (#59677D, 5.73:1 sobre blanco) y NO el navy al 60 %, que era
 * lo que había: mezclado sobre blanco da rgb(115,125,143) y se queda en 4.15:1, por debajo del
 * 4.5:1 que exige un texto de 12px. La regla de «no bajar de 0.7 sobre texto» también se incumple
 * cuando la transparencia se escribe como alfa del color en vez de como `opacity-*` — ahí no la ve
 * ningún grep, que es justo como había sobrevivido.
 */
export const WIZARD_LABEL = 'block text-[0.8125rem] font-semibold text-[color:var(--di-text-primary)]';

/** Texto de ayuda o error bajo el campo. */
export const WIZARD_HINT = 'mt-1 block text-xs leading-snug opacity-70';

/** Tarjeta de sección dentro de un paso. */
export const WIZARD_CARD = 'rounded-2xl border bg-card p-4';

/**
 * Botón de acción del pie del asistente (`BTN` de la propuesta): 44 px de alto, 13px semibold.
 * Los 44 px no son estéticos — es el objetivo táctil mínimo, y el pie es donde se cancela y se
 * avanza. B7 (guardián de diseño) — es el único tamaño de CTA del pie: antes convivía con un
 * segundo botón a `px-5 py-2`/12px en 6 de las 8 ramas del footer, que quedó unificado a este.
 *
 * NO trae un `focus-visible:ring-[hex]` por defecto a propósito: el pie combina botones de tono
 * distinto (el CTA azul, "Anular trámite" en rojo, "Anterior" en navy) y dos utilidades
 * `ring-color` con el mismo valor arbitrary-value en el mismo elemento no tienen un orden de
 * cascada garantizado por el nombre de la clase en Tailwind 4 (depende del orden de generación del
 * CSS, no del orden en el `className`). Cada uso de `WIZARD_BTN` DEBE declarar su propio
 * `focus-visible:ring-[hex]` — todos los usos actuales del repo lo hacen.
 */
export const WIZARD_BTN =
  'h-11 w-auto rounded-xl px-6 text-[13px] font-semibold transition ' +
  'focus:outline-none focus-visible:ring-2 focus-visible:ring-offset-2';

/**
 * Azul pleno de marca para botones de **consulta** (Consultar RUNT / RUES / Buscar).
 * PDF anotaciones + prototipo Lovable: estos botones son `#557EFF` sólido, SIN degradado.
 * El degradado (`WIZARD_CTA_GRADIENT`) queda reservado a CTAs de avance/cierre (Continuar, Radicar).
 */
export const WIZARD_BTN_SOLID = 'var(--color-flit-brand)';

/**
 * Botón de acción primario dentro de tarjetas del wizard (validación, documentos, etc.): azul
 * plano `#557EFF`, sin degradado. El degradado (`WIZARD_CTA_GRADIENT`) queda reservado al pie del
 * asistente (Continuar / Radicar / Finalizar) — HU #12729 D.3.
 */
export const WIZARD_BTN_BRAND =
  'rounded-xl px-3 py-2 text-xs font-semibold text-white transition hover:opacity-90 ' +
  'disabled:cursor-not-allowed disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2';

/** Variante outline del botón de marca para cancelar / reenviar secundario. */
export const WIZARD_BTN_BRAND_OUTLINE =
  'flex items-center gap-1.5 rounded-xl border px-3 py-1.5 text-xs font-semibold transition ' +
  'disabled:cursor-not-allowed disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2';

/**
 * Degradado del CTA primario del asistente: token FLIT `gradient.primary` (azul→cian), el mismo
 * del CTA general del producto. Antes era azul→`#2563EB` (blue-600 de Tailwind, no un token FLIT):
 * B7 (guardián de diseño) lo corrige al degradado de marca.
 */
export const WIZARD_CTA_GRADIENT = 'var(--gradient-action)';

/**
 * Homologación #14507: la propuesta no usa verde, así que el cierre lleva el mismo `--di-gradient-action`.
 * Antes: token FLIT `gradient.success` (cian→verde), descrito en el token
 * file como «CTA de cierre/finalizar» y exigido por `prototype_rules.md` para el último paso.
 * Reservado a las acciones terminales del asistente —Radicar y Finalizar—: son las únicas que
 * cierran el trámite. «Continuar» y «Guardar y continuar» siguen con el degradado primario,
 * porque avanzar no es cerrar.
 */
export const WIZARD_CTA_GRADIENT_DONE = 'var(--di-gradient-action)';

/**
 * Fondo del CTA de acción destructiva o de rechazo (homologación #14507): sólido `--destructive`, con el texto en
 * `--destructive-foreground` (blanco sobre #c23c00 en claro, ≈5,3:1; tinta sobre #ff8a50 en oscuro, ≈7:1). El
 * degradado naranja anterior con texto blanco fijo daba ≈2,2:1 en oscuro. Conserva el nombre por compatibilidad.
 */
export const WIZARD_CTA_GRADIENT_DANGER = 'var(--destructive)';
