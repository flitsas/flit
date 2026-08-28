"use client";

import { useEffect, useState } from "react";
import { FileSignature } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { fetchMandateSignerSignatureImage } from "@/lib/api/admin-mandate-signers";
import type { MandateSigner } from "@/lib/api/admin-mandate-signers";
import { ApiError } from "@/lib/api/types";
import {
  etiquetaTipoFirma,
  tipoDeFirmaMandatario,
  type TipoFirmaMandatario,
} from "@/lib/plataforma/mandatario-firma";

export function MandatarioFirmaPreviewDialog({
  signer,
  officeId,
  onClose,
}: {
  signer: MandateSigner;
  officeId: string;
  onClose: () => void;
}) {
  const tipo = tipoDeFirmaMandatario(signer, officeId);
  const aMano = signer.physicalSignatureOfficeIds?.includes(officeId) ?? false;
  const [imageUrl, setImageUrl] = useState<string | null>(null);
  const [imageStatus, setImageStatus] = useState<"idle" | "loading" | "ready" | "empty" | "error">(
    signer.signatureVaultId ? "loading" : "idle",
  );
  const [imageError, setImageError] = useState<string | null>(null);

  useEffect(() => {
    if (!signer.signatureVaultId) {
      setImageStatus("idle");
      setImageUrl(null);
      return;
    }

    let cancelled = false;
    let objectUrl: string | null = null;
    setImageStatus("loading");
    setImageError(null);
    setImageUrl(null);

    void fetchMandateSignerSignatureImage(officeId, signer.id)
      .then((blob) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setImageUrl(objectUrl);
        setImageStatus("ready");
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        if (err instanceof ApiError && err.status === 404) {
          setImageStatus("empty");
          return;
        }
        setImageError(err instanceof ApiError ? err.message : "No se pudo cargar la imagen de la firma.");
        setImageStatus("error");
      });

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [officeId, signer.id, signer.signatureVaultId]);

  return (
    <Modal
      open
      onClose={onClose}
      icon={FileSignature}
      title={`Firma de ${signer.fullName}`}
      description="Cómo se verá en el contrato de mandato"
      size="md"
    >
      <dl className="grid grid-cols-1 gap-3 text-xs">
        <Field label="Tipo de firma" value={etiquetaTipoFirma(tipo)} />
        <Field label="Tipo de documento" value={signer.documentType?.trim() || "—"} />
        <Field label="Documento" value={signer.documentNumber?.trim() || "—"} />
        {signer.identityStatus === "valid" && signer.identityValidUntil ? (
          <Field
            label="Identidad vigente hasta"
            value={new Date(signer.identityValidUntil).toLocaleDateString("es-CO")}
          />
        ) : null}
      </dl>

      <div className="mt-4" data-testid="ot-mandatos-firma-preview-image">
        {imageStatus === "loading" ? (
          <p className="text-xs text-[#59677D] dark:text-white/65">Cargando imagen de la firma…</p>
        ) : null}
        {imageStatus === "error" ? (
          <p className="text-xs text-[#C81E1E]">{imageError}</p>
        ) : null}
        {imageStatus === "empty" ? (
          <p className="text-xs text-[#59677D] dark:text-white/65">No hay imagen custodiada para este mandatario.</p>
        ) : null}
        {imageStatus === "ready" && imageUrl ? (
          // PNG del baúl: no es HTML de usuario.
          // eslint-disable-next-line @next/next/no-img-element
          <img
            src={imageUrl}
            alt={`Firma de ${signer.fullName}`}
            className="max-h-40 w-auto max-w-full rounded-lg border border-[#DFE5ED] bg-white p-3 dark:border-white/10"
          />
        ) : null}
      </div>

      <p className="mt-4 text-xs leading-relaxed text-[#59677D] dark:text-white/65" data-testid="ot-mandatos-firma-preview-body">
        {textoPreview(tipo, aMano)}
      </p>
      <div className="mt-5 flex justify-end">
        <button
          type="button"
          onClick={onClose}
          className="rounded-xl px-5 py-2.5 text-sm font-semibold text-white bg-[#557EFF]"
        >
          Cerrar
        </button>
      </div>
    </Modal>
  );
}

function textoPreview(tipo: TipoFirmaMandatario, aManoEnEsteOt: boolean): string {
  switch (tipo) {
    case "baul":
      return "El contrato estampa la imagen de la firma custodiada en el baúl.";
    case "identidad":
      return "El contrato estampa el sello de la validación de identidad vigente.";
    case "identidad_pendiente":
      return "La validación de identidad está en curso. Cuando quede vigente, el contrato estampará el sello.";
    case "a_mano":
      return "No hay imagen ni sello. El contrato deja la línea para firmar en papel.";
    default:
      return "No hay medio de firma. El recuadro del mandatario queda en blanco (Sin firmar).";
  }
}

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="font-semibold text-[#59677D] dark:text-white/55">{label}</dt>
      <dd className="mt-0.5 text-[#162244] dark:text-white">{value}</dd>
    </div>
  );
}
