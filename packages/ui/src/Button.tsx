import type { ButtonHTMLAttributes } from "react";
import { cn } from "./cn";

/**
 * Botones del Manual UI Kit 2026 (propuesta-2, ui-kit-2026.css `.btn`): píldora plana, sin sombra ni degradado.
 *
 * - `primary`: azul (#1D53FF, AA con blanco) — avanza.
 * - `secondary` y `navy`: azul oscuro (#162744) — navega o acción secundaria. En oscuro lleva un aro de 1 px.
 * - `positiva`: verde (#8CC63F) con texto tinta — confirma.
 * - `alerta`: naranja (#D44100) — alerta o acción destructiva.
 * - `ghost`: solo texto de acción subrayado.
 * Al pasar el cursor se oscurece (brillo 0,9) y al pulsar baja 1 px. Deshabilitado: gris del kit con texto blanco.
 */
export type ButtonVariant = "primary" | "secondary" | "navy" | "positiva" | "alerta" | "ghost";
export type ButtonSize = "sm" | "md";

const BASE =
  "inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-full border-0 font-semibold no-underline shadow-none " +
  "transition-[filter,transform,background] duration-150 ease-[cubic-bezier(0.2,0,0,1)] " +
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--kit-azul-relleno)] focus-visible:ring-offset-2 " +
  "focus-visible:ring-offset-[var(--di-surface-elevated)] " +
  "disabled:cursor-not-allowed aria-busy:cursor-progress";

const RELLENO =
  "hover:brightness-90 active:brightness-[.78] active:translate-y-px " +
  "disabled:bg-[color:var(--kit-gris)] disabled:text-white disabled:hover:brightness-100 disabled:active:translate-y-0";

const VARIANT: Record<ButtonVariant, string> = {
  primary: `bg-[color:var(--kit-azul-relleno)] text-white ${RELLENO}`,
  secondary: `bg-[color:var(--kit-tinta)] text-white dark:shadow-[inset_0_0_0_1px_#3a5688] ${RELLENO}`,
  navy: `bg-[color:var(--kit-tinta)] text-white dark:shadow-[inset_0_0_0_1px_#3a5688] ${RELLENO}`,
  positiva: `bg-[color:var(--kit-verde)] text-[color:var(--kit-tinta)] ${RELLENO}`,
  alerta: `bg-[color:var(--kit-naranja-boton)] text-white ${RELLENO}`,
  ghost:
    "bg-transparent text-[color:var(--di-action)] underline underline-offset-[3px] hover:bg-[color:var(--kit-hover)] " +
    "disabled:text-[color:var(--kit-gris)] disabled:hover:bg-transparent",
};

const SIZE: Record<ButtonSize, string> = {
  // .btn.sm: 36 px de alto (barras densas y pies de diálogo).
  sm: "min-h-9 px-[18px] py-1.5 text-sm",
  // .btn: 44 px, el área táctil del sistema.
  md: "min-h-11 px-6 py-2.5 text-base",
};

const GHOST_PAD: Record<ButtonSize, string> = { sm: "px-3", md: "px-3" };

/** Clases de la familia `.btn`, para pintar como botón un `<a>`, un `<Link>` o un botón propio. */
export function buttonClass({
  variant = "primary",
  size = "md",
  className,
}: { variant?: ButtonVariant; size?: ButtonSize; className?: string } = {}): string {
  return cn(BASE, VARIANT[variant], SIZE[size], variant === "ghost" && GHOST_PAD[size], className);
}

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
}

export function Button({ variant = "primary", size = "md", className, type = "button", ...rest }: ButtonProps) {
  return <button type={type} className={buttonClass({ variant, size, className })} {...rest} />;
}
