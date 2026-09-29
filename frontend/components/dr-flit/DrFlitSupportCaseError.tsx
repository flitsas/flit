"use client";

import { AlertCircle } from "lucide-react";
import { DrFlitSupportPanel } from "./DrFlitSupportPanel";

/**
 * HU #12930 AC3 — el caso no se pudo radicar. Se ofrecen los canales de soporte como salida de
 * emergencia y se conserva el formulario: «Reintentar» vuelve a radicar lo mismo y «Editar el caso»
 * vuelve al formulario, sin reescribir nada.
 */
export function DrFlitSupportCaseError({
  message,
  onRetry,
  onEdit,
  onOpen,
}: {
  message: string;
  onRetry: () => void;
  onEdit: () => void;
  onOpen: (href: string) => void;
}) {
  return (
    <div className="space-y-3">
      <p
        role="alert"
        className="flex items-start gap-2 rounded-[var(--dr-flit-radius-card)] border p-3 text-sm"
        style={{ borderColor: "var(--dr-flit-accent)", background: "var(--dr-flit-card-bg)", color: "var(--dr-flit-text)" }}
      >
        <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" style={{ color: "var(--dr-flit-accent)" }} aria-hidden="true" />
        {message}
      </p>
      <div className="flex flex-col gap-2">
        <button
          type="button"
          onClick={onRetry}
          className="w-full rounded-full px-4 py-3 text-sm font-semibold text-white transition-opacity hover:opacity-95 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2"
          style={{ background: "var(--dr-flit-gradient-primary)" }}
        >
          Reintentar
        </button>
        <button
          type="button"
          onClick={onEdit}
          className="w-full rounded-full border px-4 py-2.5 text-sm font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2"
          style={{ borderColor: "var(--dr-flit-brand)", color: "var(--dr-flit-brand)", background: "var(--dr-flit-card-bg)" }}
        >
          Editar el caso
        </button>
      </div>
      <DrFlitSupportPanel onOpenCase={onOpen} />
    </div>
  );
}
