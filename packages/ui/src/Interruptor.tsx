"use client";

import type { ButtonHTMLAttributes } from "react";
import { cn } from "./cn";

/**
 * Interruptor del Manual UI Kit 2026 (propuesta-2, `.interruptor`): 68 × 36 px, dibujo del paquete de íconos — verde
 * encendido, gris apagado — como fondo; sin perilla propia. Mismo contrato que los interruptores de siempre:
 * `role="switch"` con `aria-checked`, se pulsa con clic, Espacio o Enter (es un `<button>`).
 *
 * Las versiones propias que ya tienen su lógica pueden quedarse con su `<button role="switch">` y cambiar SOLO la clase
 * a `INTERRUPTOR_CLS`, ocultando su perilla con `INTERRUPTOR_PERILLA_CLS`.
 */
export const INTERRUPTOR_CLS =
  "kit-interruptor relative inline-block h-9 w-[68px] shrink-0 cursor-pointer rounded-full border-0 p-0 " +
  "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--di-action)] " +
  "disabled:cursor-not-allowed disabled:opacity-50";
/** Variante de alerta: encendido en naranja, para cuando encender es una emergencia (suspender, bloquear). */
export const INTERRUPTOR_ALERTA_CLS = "kit-interruptor--alerta";
/** Para la perilla de una versión propia: el dibujo del kit ya la trae. */
export const INTERRUPTOR_PERILLA_CLS = "hidden";

export interface InterruptorProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, "onChange" | "role"> {
  checked: boolean;
  onChange: (checked: boolean) => void;
}

export function Interruptor({ checked, onChange, className, disabled, ...rest }: InterruptorProps) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={cn(INTERRUPTOR_CLS, className)}
      {...rest}
    />
  );
}
