"use client";

// Piezas compartidas de Plataforma > Persona Natural (Épica #14021): botón con la estructura estándar (icono + texto,
// deshabilitado y cargando), diálogo de confirmación para acciones que cambian lo que ve el ciudadano y encabezado de
// sección con ayuda breve. Todo sale de los tokens de `pn-ui.ts` (línea base de globals.css).
import { useState, type ButtonHTMLAttributes, type ComponentType, type ReactNode } from "react";
import { type LucideIcon } from "lucide-react";
import { Icono, type NombreIcono } from "@flit/ui/Icono";
import type { IconoComoProps } from "@flit/ui/iconoComo";
import { Modal } from "@/components/atom/Modal";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { PN_CANCEL_BTN_CLS, PN_DANGER_BTN_STYLE, PN_PRIMARY_BTN_CLS, PN_PRIMARY_BTN_STYLE } from "./pn-ui";
import { pnErrorMessage } from "./pnErrors";
import { ETIQUETA_PUNTO_CLS, etiquetaKit } from "@flit/ui/StatusBadge";

export type PnButtonVariant = "primary" | "secondary" | "danger";

export interface PnButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, "className" | "style"> {
  variant?: PnButtonVariant;
  icon?: ComponentType<IconoComoProps>;
  /** Muestra el indicador de carga en lugar del icono y deshabilita el botón. */
  loading?: boolean;
}

/** Primario = degradado de marca; secundario = borde claro; peligro = naranja de marca. Siempre icono + texto. */
export function PnButton({
  variant = "secondary",
  icon: Icon,
  loading = false,
  disabled,
  type = "button",
  children,
  ...rest
}: PnButtonProps) {
  const base =
    variant === "secondary"
      ? `${PN_CANCEL_BTN_CLS} inline-flex items-center gap-2`
      : `${PN_PRIMARY_BTN_CLS} justify-center`;
  const style = variant === "primary" ? PN_PRIMARY_BTN_STYLE : variant === "danger" ? PN_DANGER_BTN_STYLE : undefined;
  return (
    <button
      type={type}
      {...rest}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      className={`${base} disabled:cursor-not-allowed`}
      style={style}
    >
      {loading ? (
        <Icono nombre="girador" className="h-4 w-4 animate-spin" />
      ) : (
        Icon && <Icon className="h-4 w-4" aria-hidden="true" />
      )}
      {children}
    </button>
  );
}

/** Enlace-botón de texto (p. ej. «Crear tarifa» dentro de una celda). Mantiene foco visible y objetivo táctil. */
export function PnLinkButton({
  onClick,
  children,
  ariaLabel,
}: {
  onClick: () => void;
  children: ReactNode;
  ariaLabel?: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label={ariaLabel}
      className="rounded-md py-1 text-xs font-semibold text-[color:var(--flit-brand-ink)] underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2"
    >
      {children}
    </button>
  );
}

/** Encabezado de sección: título, frase que explica qué es y, a la derecha, la acción principal. */
export function PnSeccionHeader({
  id,
  titulo,
  descripcion,
  paso,
  accion,
  contador,
}: {
  id: string;
  titulo: string;
  descripcion: ReactNode;
  /** Número del paso de la configuración al que pertenece (se ve como círculo numerado). */
  paso?: number;
  accion?: ReactNode;
  /** Resumen corto junto al título, p. ej. «3 preguntas · 2 visibles». */
  contador?: string;
}) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div className="flex items-start gap-3">
        {paso !== undefined && (
          <span
            aria-hidden="true"
            className="mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-flit-brand text-sm font-bold text-white"
          >
            {paso}
          </span>
        )}
        <div className="flex max-w-3xl flex-col gap-0.5">
          <h2 id={id} className="flex flex-wrap items-center gap-2 text-base font-bold text-flit-primary">
            {titulo}
            {contador && (
              <span {...etiquetaKit("neutral")}>
                <span className={ETIQUETA_PUNTO_CLS} aria-hidden="true" />
                {contador}
              </span>
            )}
          </h2>
          <p className="text-sm text-muted-foreground">{descripcion}</p>
        </div>
      </div>
      {accion}
    </div>
  );
}

export interface PnConfirmDialogProps {
  title: string;
  icon?: LucideIcon;
  /** Ícono del kit en el disco del Modal; gana sobre `icon`. */
  icono?: NombreIcono;
  /** `danger` para lo que deja de verse o se retira; `primary` para lo que se publica o se activa. */
  tone?: "danger" | "primary";
  confirmLabel: string;
  confirmIcon?: ComponentType<IconoComoProps>;
  children: ReactNode;
  onClose: () => void;
  /** Ejecuta la acción; si lanza, el motivo se muestra aquí y el diálogo no se cierra. */
  onConfirm: () => Promise<void>;
}

/** Confirmación estándar: dice qué cambia para el ciudadano, botón de confirmar con carga y error claro. */
export function PnConfirmDialog({
  title,
  icon,
  icono,
  tone = "primary",
  confirmLabel,
  confirmIcon,
  children,
  onClose,
  onConfirm,
}: PnConfirmDialogProps) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function confirmar() {
    setBusy(true);
    setError(null);
    try {
      await onConfirm();
    } catch (err) {
      setError(pnErrorMessage(err));
      setBusy(false);
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      busy={busy}
      title={title}
      icon={icon}
      icono={icono}
      iconBg={tone === "danger" ? "var(--destructive)" : "var(--color-flit-brand)"}
      size="sm"
      footer={
        <div className="flex justify-end gap-2">
          <PnButton variant="secondary" onClick={onClose} disabled={busy}>
            Cancelar
          </PnButton>
          <PnButton
            variant={tone === "danger" ? "danger" : "primary"}
            icon={confirmIcon}
            loading={busy}
            onClick={() => void confirmar()}
          >
            {confirmLabel}
          </PnButton>
        </div>
      }
    >
      <div className="flex flex-col gap-3 text-sm text-flit-primary">
        {children}
        {error && <InlineAlert tone="error">{error}</InlineAlert>}
      </div>
    </Modal>
  );
}
