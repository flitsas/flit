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
  'w-full rounded-xl border bg-white px-3 py-2 text-xs outline-none transition ' +
  'focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20 ' +
  'aria-[invalid=true]:border-[#FF4E00] ' +
  'disabled:cursor-not-allowed disabled:opacity-60 dark:bg-[#0B0F14]';

/** Igual que el campo de texto, para `<select>` nativos. */
export const WIZARD_SELECT = WIZARD_INPUT;

/** Etiqueta sobre el campo. 12px es el piso tipográfico del sistema. */
export const WIZARD_LABEL = 'block text-xs font-medium text-[#162744]/60 dark:text-white/50';

/** Texto de ayuda o error bajo el campo. */
export const WIZARD_HINT = 'mt-1 block text-xs leading-snug opacity-70';

/** Tarjeta de sección dentro de un paso. */
export const WIZARD_CARD = 'rounded-2xl border bg-white p-4 dark:bg-[#0B0F14]';

/**
 * Botón de acción del pie del asistente (`BTN` de la propuesta): 44 px de alto, 13px semibold.
 * Los 44 px no son estéticos — es el objetivo táctil mínimo, y el pie es donde se cancela y se
 * avanza.
 */
export const WIZARD_BTN =
  'h-11 w-auto rounded-xl px-6 text-[13px] font-semibold transition ' +
  'focus:outline-none focus-visible:ring-2 focus-visible:ring-offset-2';

/**
 * Degradado del CTA primario del asistente. La propuesta lo hace azul→azul; el token
 * `gradient.primary` del guardián (azul→cian) es el del CTA general del producto. Se sigue el repo
 * por decisión explícita para este rediseño, y queda anotado el desvío.
 */
export const WIZARD_CTA_GRADIENT = 'linear-gradient(to right, #557EFF, #2563EB)';
