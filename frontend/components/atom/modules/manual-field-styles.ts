/**
 * Campos de las Validaciones manuales legibles en tema claro y oscuro (capa dark de la línea base:
 * fondo #162744, texto #FFFFFF, borde rgba(255,255,255,0.15), foco #7C9BFF). `WIZARD_SELECT` fuerza
 * `bg-white` y `color-scheme: light` para el wizard; aquí se sobrescribe solo en oscuro, sin tocarlo.
 * En claro el aspecto es el mismo de siempre.
 */
export const MANUAL_SELECT_CLASS =
  'border-[#DFE5ED] dark:border-white/15 dark:bg-[#162744] dark:text-white dark:[color-scheme:dark] ' +
  'dark:hover:border-[#7C9BFF] dark:focus:border-[#7C9BFF] dark:focus:ring-[#7C9BFF]/30';

export const MANUAL_INPUT_CLASS =
  'border-[#DFE5ED] dark:border-white/15 dark:bg-[#162744] dark:text-white dark:placeholder:text-white/60 ' +
  'dark:focus:border-[#7C9BFF] dark:focus:ring-[#7C9BFF]/30';
