"use client";

import type { LucideIcon } from "lucide-react";

// HU #10844 — Botón de "crear / agregar" unificado. Centraliza el patrón que cada
// pantalla repetía a mano (mismo gradiente de marca + texto + icono), con tamaño y
// `aria` consistentes. Reemplaza el icono genérico "+" por un icono *semántico* por
// acción (p. ej. Building2 compañía, Landmark OT, FilePlus documento), que se pasa vía
// la prop `icon`. El texto visible (`label`) es siempre el nombre accesible; el icono
// va `aria-hidden`. Si no se pasa icono, el botón queda solo con texto (variante
// minimalista válida).

export type CreateButtonSize = "sm" | "md";

const SIZE_CLASS: Record<CreateButtonSize, string> = {
  // Compacto (toolbars densas: scheduling, filtros)
  sm: "gap-1.5 px-3 py-1.5 text-[11px]",
  // Estándar (headers de módulo: compañías, documentos, OT)
  md: "gap-1.5 px-4 py-2 text-xs",
};

const ICON_SIZE: Record<CreateButtonSize, string> = {
  sm: "h-3.5 w-3.5",
  md: "h-4 w-4",
};

export interface CreateButtonProps {
  /** Texto visible y nombre accesible del botón (p. ej. "Crear compañía"). */
  label: string;
  /** Acción de creación. */
  onClick?: () => void;
  /** Icono semántico de la acción (Lucide). Va `aria-hidden`; opcional. */
  icon?: LucideIcon;
  /** Densidad del botón. `md` por defecto. */
  size?: CreateButtonSize;
  type?: "button" | "submit";
  disabled?: boolean;
  /** Clases extra opcionales (posicionamiento, ancho, etc.). */
  className?: string;
}

export function CreateButton({
  label,
  onClick,
  icon: Icon,
  size = "md",
  type = "button",
  disabled = false,
  className = "",
}: CreateButtonProps) {
  return (
    <button
      type={type}
      onClick={onClick}
      disabled={disabled}
      className={`inline-flex items-center justify-center rounded-xl font-semibold text-white shadow-sm transition-transform hover:scale-[1.02] disabled:cursor-not-allowed disabled:opacity-50 ${SIZE_CLASS[size]} ${className}`}
      style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
    >
      {Icon && <Icon className={ICON_SIZE[size]} aria-hidden="true" />}
      <span>{label}</span>
    </button>
  );
}
