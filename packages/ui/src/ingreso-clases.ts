/**
 * Clases del Ingreso (UI Kit 2026, `.ingreso*`), en un módulo SIN "use client": una página de servidor puede importarlas
 * e interpolarlas en un string. Importadas desde `Ingreso.tsx` (cliente) llegan como referencia de cliente y la clase
 * sale rota. `Ingreso.tsx` las reexporta para no romper a quien ya las toma de ahí.
 */

/** Textos del panel de tinta (prop `marca` de IngresoLayout). */
export const INGRESO_MARCA_ANTETITULO_CLS =
  "mb-2 text-xs font-medium uppercase tracking-[0.08em] text-[color:var(--di-ingreso-texto-2)]";
export const INGRESO_MARCA_TITULO_CLS = "m-0 text-[clamp(2rem,3.4vw,3rem)] font-extrabold leading-[1.08] tracking-[-0.01em]";
/** Título de la tarjeta de ingreso (`.ingreso__tarjeta h2`). */
export const INGRESO_MARCA_TEXTO_CLS = "mt-3.5 max-w-[46ch] text-base text-[color:var(--di-ingreso-texto-2)]";

export const INGRESO_TITULO_CLS = "m-0 text-2xl font-bold text-[color:var(--di-heading)]";
/** Etiqueta de campo (`.campo label`). */
export const INGRESO_ETIQUETA_CLS = "mb-1.5 block text-[0.8125rem] font-semibold text-[color:var(--di-text-primary)]";
/** Control con ícono a la izquierda (`.control-icono .control`). */
export const INGRESO_CONTROL_CLS =
  "w-full min-h-11 rounded-[9px] border border-[color:var(--di-border-control)] bg-[color:var(--di-surface-elevated)] " +
  "py-2.5 pl-[42px] pr-3.5 text-base text-[color:var(--di-text-primary)] outline-none transition " +
  "placeholder:text-[color:var(--kit-gris)] focus:border-[color:var(--di-action)] focus:shadow-[var(--kit-sombra)] " +
  "aria-invalid:border-[color:var(--kit-naranja)]";
/** Ícono dentro del control (`.control-icono svg`). */
export const INGRESO_ICONO_CLS =
  "pointer-events-none absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-[color:var(--kit-azul-texto)]";
/** Enlace de texto (`.enlace`). */
export const INGRESO_ENLACE_CLS =
  "inline-flex min-h-11 items-center text-sm font-medium text-[color:var(--di-action)] underline underline-offset-[3px]";
/** Mensaje de error del formulario (`.ingreso__error`). */
export const INGRESO_ERROR_CLS =
  "m-0 flex items-center gap-2 rounded-[9px] border border-[color:var(--kit-naranja)] bg-[#ff4e001a] px-3.5 py-2.5 text-[0.8125rem] font-semibold text-[color:var(--badge-warning-fg)]";
/** Aviso de acceso (bloqueo, rol desactivado, dominio propio): misma caja suave del error. */
export const INGRESO_AVISO_CLS = "flex gap-3 rounded-[9px] p-5";
/** Botón de enviar (`.ingreso__boton`), sobre `buttonClass()`. */
export const INGRESO_BOTON_CLS = "w-full min-h-[50px] text-base";
