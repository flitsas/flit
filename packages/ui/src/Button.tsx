import type { ButtonHTMLAttributes } from "react";
import { cn } from "./cn";

/**
 * Familia `.btn` del prototipo «Diseño Inteligente» (rama `propuesta`, propuesta.css), HU #14476:
 *
 * - `primary`: píldora con `--di-gradient-action`, sombra de botón y un levantón de 1 px al pasar el cursor.
 * - `secondary`: píldora blanca con contorno de control y texto de acción (el «Cancelar» de los diálogos).
 * - `navy`: píldora tinta sólida.
 * - `ghost`: solo texto de acción (el enlace o «Cerrar» del pie de los diálogos).
 *
 * El texto del principal va con `--di-on-action` (no `text-white`) para que, cuando el oscuro pase al degradado claro
 * del prototipo, baste con cambiar el token.
 */
export type ButtonVariant = "primary" | "secondary" | "navy" | "ghost";
export type ButtonSize = "sm" | "md";

const BASE =
  "inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-full font-semibold no-underline " +
  "transition-[transform,box-shadow,background] duration-150 ease-[cubic-bezier(0.2,0,0,1)] " +
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--di-action)] focus-visible:ring-offset-2 " +
  "focus-visible:ring-offset-[var(--di-surface-elevated)] " +
  "disabled:cursor-not-allowed disabled:opacity-50 disabled:hover:translate-y-0 aria-busy:cursor-progress";

const VARIANT: Record<ButtonVariant, string> = {
  primary:
    "border-0 [background:var(--di-gradient-action)] text-[color:var(--di-on-action)] shadow-[var(--shadow-button)] " +
    "hover:[background:var(--di-gradient-action-hover)] hover:-translate-y-px disabled:hover:[background:var(--di-gradient-action)]",
  secondary:
    "border border-[color:var(--di-border-control)] bg-[color:var(--di-surface-elevated)] text-[color:var(--di-action)] " +
    "hover:bg-[color:var(--di-accent-soft)] disabled:hover:bg-[color:var(--di-surface-elevated)]",
  navy: "border-0 bg-[color:var(--di-accent)] text-[color:var(--di-on-accent)] hover:bg-[color:var(--di-accent-hover)]",
  ghost: "border-0 bg-transparent text-[color:var(--di-action)] hover:bg-[color:var(--di-accent-soft)] disabled:hover:bg-transparent",
};

const SIZE: Record<ButtonSize, string> = {
  // .btn.sm: 36 px de alto (barras densas y pies de diálogo).
  sm: "min-h-9 px-3.5 py-1.5 text-xs",
  // .btn: 44 px, el área táctil del sistema.
  md: "min-h-11 px-5 py-2.5 text-sm",
};

/** Clases de la familia `.btn`, para pintar como botón un `<a>`, un `<Link>` o un botón propio. */
export function buttonClass({
  variant = "primary",
  size = "md",
  className,
}: { variant?: ButtonVariant; size?: ButtonSize; className?: string } = {}): string {
  return cn(BASE, VARIANT[variant], SIZE[size], className);
}

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
}

export function Button({ variant = "primary", size = "md", className, type = "button", ...rest }: ButtonProps) {
  return <button type={type} className={buttonClass({ variant, size, className })} {...rest} />;
}
