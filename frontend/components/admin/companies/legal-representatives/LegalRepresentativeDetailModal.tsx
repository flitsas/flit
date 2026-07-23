"use client";

import { UserSquare } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { StatusBadge } from "@/components/atom/StatusBadge";
import type { LegalRepresentativeItem } from "@/lib/api/admin-legal-representatives";
import { fullName, procedureTypeLabels, signatureStatus } from "./legalRepresentativesDisplay";

export interface LegalRepresentativeDetailModalProps {
  item: LegalRepresentativeItem | null;
  onClose: () => void;
}

/**
 * Detalle de un representante legal (HU #10904). Muestra los datos de la compañía representada, del
 * representante, los tipos de trámite y el estado de firma/identidad. El número de documento se
 * muestra completo aquí (vista de gestión SuperAdmin autenticada); nunca se descarga artefacto alguno.
 */
export function LegalRepresentativeDetailModal({ item, onClose }: LegalRepresentativeDetailModalProps) {
  if (!item) return null;
  const status = signatureStatus(item.hasSignatureOrIdentity);
  const tramites = procedureTypeLabels(item.procedureTypeIds);

  return (
    <Modal
      open
      onClose={onClose}
      icon={UserSquare}
      title="Detalle del representante"
      description={fullName(item)}
      size="md"
    >
      <dl className="grid grid-cols-1 gap-3 text-xs sm:grid-cols-2">
        <Field label="Representante" value={fullName(item)} />
        <div>
          <dt className="font-semibold opacity-60">Firma / Identidad</dt>
          <dd className="mt-0.5">
            <StatusBadge tone={status.tone} label={status.label} />
          </dd>
        </div>
        <Field label="Documento" value={`${item.documentType} ${item.documentNumber}`} />
        <Field label="Correo" value={item.email ?? "—"} />
        <Field label="Compañía" value={item.companyName} />
        <Field label="NIT compañía" value={item.companyDocumentNumber} />
        <Field label="Teléfono" value={item.phone ?? "—"} />
        <Field label="Ciudad" value={item.city ?? "—"} />
        <div className="sm:col-span-2">
          <dt className="font-semibold opacity-60">Tipos de trámite que puede firmar</dt>
          <dd className="mt-1 flex flex-wrap gap-1.5">
            {tramites.length === 0 ? (
              <span className="opacity-60">Ninguno</span>
            ) : (
              tramites.map((t, i) => <StatusBadge key={`${t}-${i}`} tone="info" label={t} />)
            )}
          </dd>
        </div>
      </dl>

      <div className="mt-5 flex justify-end">
        <button
          type="button"
          onClick={onClose}
          className="rounded-xl px-5 py-2.5 text-sm font-semibold text-white"
          style={{ background: "#557EFF" }}
        >
          Cerrar
        </button>
      </div>
    </Modal>
  );
}

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="font-semibold opacity-60">{label}</dt>
      <dd className="mt-0.5">{value}</dd>
    </div>
  );
}
