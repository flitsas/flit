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
export const WIZARD_INPUT =
  'w-full rounded-xl border bg-white px-3 py-2 text-xs outline-none transition focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20 ' +
  'aria-[invalid=true]:border-[#FF4E00] ' +
  'disabled:cursor-not-allowed disabled:opacity-60 dark:bg-[#162744]';

/**
 * `<select>` nativo. Igual que el campo de texto, PERO con `[color-scheme:light]` forzado para
 * que el dropdown nativo use tema claro en cualquier sistema operativo — sin esto, en OS con tema
 * oscuro activado, el popup del `<select>` queda con fondo negro y texto negro, ilegible.
 * También añade `hover:border-[#557EFF]` (señal de foco pre-click) y elimina
 * `dark:bg-[#162744]` (el fondo oscuro del OS ya está cubierto por color-scheme:light en la caja).
 */
export const WIZARD_SELECT =
  'w-full rounded-xl border bg-white px-3 py-2 text-xs outline-none transition ' +
  'focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20 ' +
  'hover:border-[#557EFF] ' +
  'aria-[invalid=true]:border-[#FF4E00] ' +
  'disabled:cursor-not-allowed disabled:opacity-60 ' +
  '[color-scheme:light]';

/**
 * Etiqueta sobre el campo. 12px es el piso tipográfico del sistema.
 *
 * El color es `color.text.secondary` (#59677D, 5.73:1 sobre blanco) y NO el navy al 60 %, que era
 * lo que había: mezclado sobre blanco da rgb(115,125,143) y se queda en 4.15:1, por debajo del
 * 4.5:1 que exige un texto de 12px. La regla de «no bajar de 0.7 sobre texto» también se incumple
 * cuando la transparencia se escribe como alfa del color en vez de como `opacity-*` — ahí no la ve
 * ningún grep, que es justo como había sobrevivido.
 */
export const WIZARD_LABEL = 'block text-xs font-medium text-[#59677D] dark:text-white/70';

/** Texto de ayuda o error bajo el campo. */
export const WIZARD_HINT = 'mt-1 block text-xs leading-snug opacity-70';

/** Tarjeta de sección dentro de un paso. */
export const WIZARD_CARD = 'rounded-2xl border bg-white p-4 dark:bg-[#162744]';

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
export const WIZARD_BTN_SOLID = '#557EFF';

/**
 * Degradado del CTA primario del asistente: token FLIT `gradient.primary` (azul→cian), el mismo
 * del CTA general del producto. Antes era azul→`#2563EB` (blue-600 de Tailwind, no un token FLIT):
 * B7 (guardián de diseño) lo corrige al degradado de marca.
 */
export const WIZARD_CTA_GRADIENT = 'linear-gradient(135deg, #557EFF 0%, #00DBD5 100%)';

/**
 * Degradado del CTA de cierre: token FLIT `gradient.success` (cian→verde), descrito en el token
 * file como «CTA de cierre/finalizar» y exigido por `prototype_rules.md` para el último paso.
 * Reservado a las acciones terminales del asistente —Radicar y Finalizar—: son las únicas que
 * cierran el trámite. «Continuar» y «Guardar y continuar» siguen con el degradado primario,
 * porque avanzar no es cerrar.
 */
export const WIZARD_CTA_GRADIENT_DONE = 'linear-gradient(135deg, #00DBD5 0%, #8CC63F 100%)';
