"use client";

import { FileSignature } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import type { SignatureVaultItem } from "@/lib/api/admin-signature-vault";
import { ESTADO_LABELS, formatDate } from "./signatureVaultDisplay";

export interface SignatureVaultDetailModalProps {
  item: SignatureVaultItem | null;
  onClose: () => void;
}

/**
 * Detalle de una firma del baúl (HU #10644): solo metadatos. El artefacto de firma
 * (PNG) nunca se descarga al cliente; el detalle expone su referencia de almacenamiento.
 */
export function SignatureVaultDetailModal({ item, onClose }: SignatureVaultDetailModalProps) {
  if (!item) return null;
  const registro = item.fechaRegistro ?? item.createdAt ?? null;

  return (
    <Modal
      open
      onClose={onClose}
      icon={FileSignature}
      title="Detalle de la firma"
      description={item.fullName}
      size="md"
    >
      <dl className="grid grid-cols-1 gap-3 text-xs sm:grid-cols-2">
        <Field label="Apoderado" value={item.fullName} />
        <Field label="Estado" value={ESTADO_LABELS[item.estado] ?? item.estado} />
        <Field label="Documento" value={`${item.documentType} ${item.documentNumber}`} />
        <Field label="NIT empresa" value={item.nitEmpresa} />
        <Field label="Vigencia desde" value={formatDate(item.vigenciaDesde)} />
        <Field label="Vigencia hasta" value={formatDate(item.vigenciaHasta)} />
        <Field label="Fecha de registro" value={registro ? formatDate(registro) : "—"} />
        {item.storagePath && (
          <div className="sm:col-span-2">
            <dt className="font-semibold opacity-60">Referencia de almacenamiento</dt>
            <dd className="mt-0.5 break-all font-mono text-[11px]">{item.storagePath}</dd>
          </div>
        )}
      </dl>
      <p className="mt-4 text-[11px] opacity-60">
        Por seguridad, el archivo de la firma no se muestra ni se descarga desde esta consola.
      </p>
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
