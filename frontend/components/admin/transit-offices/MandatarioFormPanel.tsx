"use client";

import { useEffect, useState } from "react";
import { Loader2 } from "lucide-react";
import { OtSidePanel } from "./OtSidePanel";
import { OT_INPUT_CLS } from "./ot-form-styles";
import { ApiValidationError } from "@/lib/api/types";
import type {
  MandateSigner,
  MandateSignerInput,
  MandateSignerSaved,
  OtCompany,
} from "@/lib/api/admin-mandate-signers";

export interface MandatarioFormPanelProps {
  open: boolean;
  /** Mandatario en edición, o `null` para alta. */
  editing: MandateSigner | null;
  companies: OtCompany[];
  onClose: () => void;
  onSubmit: (input: MandateSignerInput) => Promise<MandateSignerSaved>;
  onSaved: (saved: MandateSignerSaved) => void;
  onError: (message: string) => void;
}

/**
 * Formulario mínimo de mandatario (ADR-0023): nombre + número de documento + huella
 * (readonly, autogenerada) + multiselect de compañías del OT. Las compañías ya tomadas por
 * OTRO mandatario, o bloqueadas/inactivas en el OT (RF33), aparecen deshabilitadas con su
 * motivo (patrón OTMatrix).
 */
export function MandatarioFormPanel({
  open,
  editing,
  companies,
  onClose,
  onSubmit,
  onSaved,
  onError,
}: MandatarioFormPanelProps) {
  const [fullName, setFullName] = useState("");
  const [documentNumber, setDocumentNumber] = useState("");
  const [selected, setSelected] = useState<Set<string>>(() => new Set());
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (!open) {
      return;
    }
    // Precarga (edición) o limpieza (alta) al abrir el panel.
    // eslint-disable-next-line react-hooks/set-state-in-effect -- sincroniza el formulario con el registro editado al abrir
    setFullName(editing?.fullName ?? "");
    setDocumentNumber(editing?.documentNumber ?? "");
    setSelected(new Set(editing?.companyTenantIds ?? []));
  }, [open, editing]);

  const toggleCompany = (companyTenantId: string) => {
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(companyTenantId)) {
        next.delete(companyTenantId);
      } else {
        next.add(companyTenantId);
      }
      return next;
    });
  };

  const handleSubmit = async () => {
    setSubmitting(true);
    try {
      const saved = await onSubmit({
        fullName: fullName.trim(),
        documentNumber: documentNumber.trim(),
        companyTenantIds: [...selected],
      });
      onSaved(saved);
    } catch (err) {
      const serverMessage =
        err instanceof ApiValidationError ? err.errors[0]?.message : undefined;
      onError(serverMessage ?? "No se pudo guardar el mandatario.");
    } finally {
      setSubmitting(false);
    }
  };

  const canSubmit =
    fullName.trim().length > 0 && documentNumber.trim().length > 0 && selected.size > 0 && !submitting;

  const footer = (
    <button
      type="button"
      disabled={!canSubmit}
      onClick={() => void handleSubmit()}
      className="flex w-full items-center justify-center gap-2 rounded-xl py-2.5 text-xs font-semibold text-white disabled:opacity-50"
      style={{ background: "#557EFF" }}
    >
      {submitting && <Loader2 className="h-4 w-4 animate-spin" />}
      {editing ? "Guardar cambios" : "Registrar mandatario"}
    </button>
  );

  return (
    <OtSidePanel
      open={open}
      title={editing ? "Editar mandatario" : "Nuevo mandatario"}
      ariaLabel={editing ? "Editar mandatario" : "Registrar mandatario"}
      onClose={onClose}
      disabled={submitting}
      footer={footer}
    >
      <div className="space-y-4">
        <div>
          <label htmlFor="ms-fullname" className="mb-1 block text-xs font-semibold">
            Nombre del mandatario
          </label>
          <input
            id="ms-fullname"
            value={fullName}
            onChange={(e) => setFullName(e.target.value)}
            className={OT_INPUT_CLS}
            placeholder="Nombre completo"
          />
        </div>

        <div>
          <label htmlFor="ms-document" className="mb-1 block text-xs font-semibold">
            Número de documento
          </label>
          <input
            id="ms-document"
            value={documentNumber}
            onChange={(e) => setDocumentNumber(e.target.value)}
            className={OT_INPUT_CLS}
            placeholder="Número de documento"
            inputMode="numeric"
          />
        </div>

        <div>
          <label htmlFor="ms-hash" className="mb-1 block text-xs font-semibold">
            Huella de integridad
          </label>
          <input
            id="ms-hash"
            readOnly
            value={editing?.integrityHash ?? ""}
            placeholder="Se generará automáticamente al guardar"
            aria-describedby="ms-hash-help"
            className={`${OT_INPUT_CLS} font-mono opacity-70`}
          />
          <p id="ms-hash-help" className="mt-1 text-[10px] opacity-60">
            SHA-256 de nombre + documento + fecha de registro. Se regenera al editar; los
            mandatos ya emitidos conservan su huella.
          </p>
        </div>

        <fieldset className="space-y-2">
          <legend className="mb-1 text-xs font-semibold">Compañías gestoras</legend>
          {companies.length === 0 ? (
            <p className="rounded-xl border p-3 text-center text-[11px] opacity-60">
              El organismo de tránsito no tiene compañías habilitadas.
            </p>
          ) : (
            <ul className="space-y-2" data-testid="ms-company-list">
              {companies.map((company) => {
                const takenByOther =
                  company.assignedSignerId !== null && company.assignedSignerId !== editing?.id;
                const blockedInOt = !company.isEnabled || !company.isActive;
                const checked = selected.has(company.companyTenantId);
                const disabled = takenByOther || (blockedInOt && !checked);
                const reason = takenByOther
                  ? `ya tiene mandatario: ${company.assignedSignerName}`
                  : blockedInOt
                    ? "bloqueada o inactiva en el OT"
                    : null;
                return (
                  <li
                    key={company.companyTenantId}
                    className="flex items-center gap-3 rounded-xl border px-3 py-2 text-xs dark:bg-[#0B0F14]"
                  >
                    <input
                      id={`ms-company-${company.companyTenantId}`}
                      type="checkbox"
                      checked={checked}
                      disabled={disabled}
                      aria-describedby={reason ? `ms-company-${company.companyTenantId}-reason` : undefined}
                      onChange={() => toggleCompany(company.companyTenantId)}
                      className="h-4 w-4 accent-[#557EFF]"
                    />
                    <label
                      htmlFor={`ms-company-${company.companyTenantId}`}
                      className={`flex-1 ${disabled ? "cursor-not-allowed opacity-60" : "cursor-pointer"}`}
                    >
                      <span className="font-semibold">{company.legalName}</span>
                      {reason && (
                        <span
                          id={`ms-company-${company.companyTenantId}-reason`}
                          className="ml-2 inline-block rounded-full border px-2 py-0.5 text-[10px] font-semibold bg-[#FF4E00]/10 text-[#FF4E00]"
                        >
                          {reason}
                        </span>
                      )}
                    </label>
                  </li>
                );
              })}
            </ul>
          )}
        </fieldset>
      </div>
    </OtSidePanel>
  );
}
