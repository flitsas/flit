"use client";

import { Modal } from "@/components/atom/Modal";
import type { CompanyTransitOfficeOption } from "@/lib/api/admin-mandate-signers";

/**
 * HU #13174 — formato de contrato de mandato que cada organismo de tránsito aplica a la compañía.
 * Vive en un modal (botón «Formatos de contrato») para no ensuciar la pestaña con una lista suelta.
 */
export function FormatosContratoModal({
  offices,
  onClose,
}: {
  offices: CompanyTransitOfficeOption[];
  onClose: () => void;
}) {
  return (
    <Modal
      open
      onClose={onClose}
      icono="documento"
      iconBg="var(--color-flit-brand)"
      title="Formatos de contrato"
      titleClassName="text-base font-bold text-flit-primary"
      size="md"
    >
      <div className="space-y-3 text-xs" data-testid="formatos-contrato-compania">
        <p className="text-muted-foreground">
          Formato con el que se redacta el contrato de mandato de tu compañía en cada organismo de tránsito.
        </p>
        <ul className="divide-y divide-border overflow-hidden rounded-xl border border-border">
          {offices
            .filter((o) => o.formatName)
            .map((o) => (
              <li
                key={o.transitOfficeId}
                className="flex flex-col gap-0.5 px-3 py-2.5 sm:flex-row sm:items-center sm:justify-between sm:gap-3"
              >
                <span className="font-medium text-flit-primary">{o.name}</span>
                <span
                  className="w-fit rounded-full px-2.5 py-0.5 text-xs font-semibold"
                  style={{ background: "color-mix(in srgb, var(--color-flit-brand) 10%, transparent)", color: "var(--flit-brand-ink)" }}
                >
                  {o.formatName}
                </span>
              </li>
            ))}
        </ul>
        <div className="flex justify-end pt-1">
          <button
            type="button"
            onClick={onClose}
            className="rounded-xl border px-4 py-2 text-xs font-semibold"
          >
            Cerrar
          </button>
        </div>
      </div>
    </Modal>
  );
}
