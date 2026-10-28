"use client";

// Piezas compartidas de Plataforma > Persona Natural (Épica #14021): botón con la estructura estándar (icono + texto,
// deshabilitado y cargando), diálogo de confirmación para acciones que cambian lo que ve el ciudadano y encabezado de
// sección con ayuda breve. Todo sale de los tokens de `pn-ui.ts` (línea base de globals.css).
import { useState, type ButtonHTMLAttributes, type ReactNode } from "react";
import { Loader2, type LucideIcon } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { PN_CANCEL_BTN_CLS, PN_DANGER_BTN_STYLE, PN_PRIMARY_BTN_CLS, PN_PRIMARY_BTN_STYLE } from "./pn-ui";
import { pnErrorMessage } from "./pnErrors";

export type PnButtonVariant = "primary" | "secondary" | "danger";

export interface PnButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, "className" | "style"> {
  variant?: PnButtonVariant;
  icon?: LucideIcon;
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
        <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
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
  tarjeta = false,
}: {
  id: string;
  titulo: string;
  descripcion: ReactNode;
  /** Número del paso de la configuración al que pertenece (se ve como círculo numerado). */
  paso?: number;
  accion?: ReactNode;
  /** Resumen corto junto al título, p. ej. «3 preguntas · 2 visibles». */
  contador?: string;
  /** Presenta el encabezado como tarjeta (secciones de contenido); la tabla va debajo, sin envolver. */
  tarjeta?: boolean;
}) {
  return (
    <div
      className={
        "flex flex-wrap items-start justify-between gap-3" +
        (tarjeta
          ? " rounded-[18px] border border-[#DFE5ED] bg-white p-4 shadow-[0_8px_24px_rgba(22,39,68,0.08)] dark:border-white/10 dark:bg-[#0B0F14]"
          : "")
      }
    >
      <div className="flex items-start gap-3">
        {paso !== undefined && (
          <span
            aria-hidden="true"
            className="mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-[#557EFF] text-sm font-bold text-white"
          >
            {paso}
          </span>
        )}
        <div className="flex max-w-3xl flex-col gap-0.5">
          <h2 id={id} className="flex flex-wrap items-center gap-2 text-base font-bold text-flit-primary dark:text-white">
            {titulo}
            {contador && (
              <span className="rounded-full bg-[#EEF5FF] px-2.5 py-0.5 text-xs font-semibold text-[color:var(--flit-brand-ink)] dark:bg-white/10 dark:text-white">
                {contador}
              </span>
            )}
          </h2>
          <p className="text-sm text-[#59677D] dark:text-white/78">{descripcion}</p>
        </div>
      </div>
      {accion}
    </div>
  );
}

export interface PnConfirmDialogProps {
  title: string;
  icon: LucideIcon;
  /** `danger` para lo que deja de verse o se retira; `primary` para lo que se publica o se activa. */
  tone?: "danger" | "primary";
  confirmLabel: string;
  confirmIcon?: LucideIcon;
  children: ReactNode;
  onClose: () => void;
  /** Ejecuta la acción; si lanza, el motivo se muestra aquí y el diálogo no se cierra. */
  onConfirm: () => Promise<void>;
}

/** Confirmación estándar: dice qué cambia para el ciudadano, botón de confirmar con carga y error claro. */
export function PnConfirmDialog({
  title,
  icon,
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
      iconBg={tone === "danger" ? "#FF4E00" : "var(--color-flit-brand)"}
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
      <div className="flex flex-col gap-3 text-sm text-flit-primary dark:text-white">
        {children}
        {error && <InlineAlert tone="error">{error}</InlineAlert>}
      </div>
    </Modal>
  );
}
