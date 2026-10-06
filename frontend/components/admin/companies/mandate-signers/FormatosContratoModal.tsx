"use client";

import { FileText } from "lucide-react";
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
      icon={FileText}
      iconBg="#557EFF"
      title="Formatos de contrato"
      titleClassName="text-base font-bold text-[#162744] dark:text-white"
      size="md"
    >
      <div className="space-y-3 text-xs" data-testid="formatos-contrato-compania">
        <p className="text-[#59677D] dark:text-white/65">
          Formato con el que se redacta el contrato de mandato de tu compañía en cada organismo de tránsito.
        </p>
        <ul className="divide-y divide-[#E3EAF5] overflow-hidden rounded-xl border border-[#E3EAF5] dark:divide-white/10 dark:border-white/10">
          {offices
            .filter((o) => o.formatName)
            .map((o) => (
              <li
                key={o.transitOfficeId}
                className="flex flex-col gap-0.5 px-3 py-2.5 sm:flex-row sm:items-center sm:justify-between sm:gap-3"
              >
                <span className="font-medium text-[#162744] dark:text-white">{o.name}</span>
                <span
                  className="w-fit rounded-full px-2.5 py-0.5 text-[11px] font-semibold"
                  style={{ background: "rgba(85,126,255,0.10)", color: "#3F5FD0" }}
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
