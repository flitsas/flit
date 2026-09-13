"use client";

import { useState } from "react";
import { Building2, Loader2 } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { ToggleSwitch } from "@/components/admin/companies/ToggleSwitch";
import { ApiValidationError, TENANT_TYPE_LABELS, defaultChildTenantType } from "@/lib/api/types";
import type { ChildTenantType, CompanyListItem, CreateCompanyRequest } from "@/lib/api/types";
import {
  hasDigit,
  hasLetterOrDigit,
  sanitizeName,
  sanitizeTaxId,
  sanitizeTenantCode,
  validateReadableName,
} from "@/lib/validation/fieldRules";

export interface CreateChildCompanyDialogProps {
  open: boolean;
  headTenantId: string;
  headTenantType: string;
  onClose: () => void;
  onCreate: (headTenantId: string, request: CreateCompanyRequest) => Promise<CompanyListItem>;
  onCreated: (company: CompanyListItem) => void;
}

type FieldErrors = Partial<Record<"razonSocial" | "nit" | "code" | "tenantType", string>>;

/** HU #12356 AC3 — alta de cliente hijo desde el panel de red. */
export function CreateChildCompanyDialog({
  open,
  headTenantId,
  headTenantType,
  onClose,
  onCreate,
  onCreated,
}: CreateChildCompanyDialogProps) {
  const [razonSocial, setRazonSocial] = useState("");
  const [nit, setNit] = useState("");
  const [code, setCode] = useState("");
  const [tenantType, setTenantType] = useState<ChildTenantType>(() =>
    defaultChildTenantType(headTenantType),
  );
  const [estadoActivo, setEstadoActivo] = useState(true);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [submitting, setSubmitting] = useState(false);

  if (!open) return null;

  const reset = () => {
    setRazonSocial("");
    setNit("");
    setCode("");
    setTenantType(defaultChildTenantType(headTenantType));
    setEstadoActivo(true);
    setErrors({});
  };

  const handleClose = () => {
    if (submitting) return;
    reset();
    onClose();
  };

  const validate = (): FieldErrors => {
    const next: FieldErrors = {};
    const rs = razonSocial.trim();
    const n = nit.trim();
    const c = code.trim();
    if (!rs) next.razonSocial = "La razón social es obligatoria.";
    else {
      const e = validateReadableName(rs, "La razón social");
      if (e) next.razonSocial = e;
    }
    if (!n) next.nit = "El NIT es obligatorio.";
    else if (!hasDigit(n)) next.nit = "El NIT debe contener al menos un dígito.";
    if (!c) next.code = "El código es obligatorio.";
    else if (!hasLetterOrDigit(c)) next.code = "El código debe contener al menos una letra o número.";
    return next;
  };

  const submit = async () => {
    const clientErrors = validate();
    if (Object.keys(clientErrors).length > 0) {
      setErrors(clientErrors);
      return;
    }

    setSubmitting(true);
    setErrors({});
    try {
      const created = await onCreate(headTenantId, {
        razonSocial: razonSocial.trim(),
        nit: nit.trim(),
        code: code.trim(),
        tenantType,
        estadoActivo,
      });
      reset();
      onCreated(created);
    } catch (error) {
      if (error instanceof ApiValidationError) {
        const mapped: FieldErrors = {};
        for (const { field, message } of error.errors) {
          if (field === "razonSocial" || field === "nit" || field === "code" || field === "tenantType") {
            mapped[field] = message;
          }
        }
        setErrors(Object.keys(mapped).length > 0 ? mapped : { code: "No se pudo crear el cliente." });
      } else {
        setErrors({ code: "No se pudo crear el cliente. Intenta de nuevo." });
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal
      open={open}
      onClose={handleClose}
      busy={submitting}
      icon={Building2}
      title="Agregar cliente a la red"
      titleClassName="text-base font-bold text-[#557EFF]"
    >
      <div className="space-y-3.5">
        <Field label="Razón Social" htmlFor="nc-razon" error={errors.razonSocial}>
          <input
            id="nc-razon"
            type="text"
            value={razonSocial}
            onChange={(e) => setRazonSocial(sanitizeName(e.target.value))}
            maxLength={255}
            className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
            style={{ borderColor: errors.razonSocial ? "#FF4E00" : "#DFE5ED" }}
          />
        </Field>

        <Field label="NIT" htmlFor="nc-nit" error={errors.nit}>
          <input
            id="nc-nit"
            type="text"
            value={nit}
            onChange={(e) => setNit(sanitizeTaxId(e.target.value))}
            maxLength={20}
            className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
            style={{ borderColor: errors.nit ? "#FF4E00" : "#DFE5ED" }}
          />
        </Field>

        <Field label="Código" htmlFor="nc-code" error={errors.code}>
          <input
            id="nc-code"
            type="text"
            value={code}
            onChange={(e) => setCode(sanitizeTenantCode(e.target.value))}
            maxLength={32}
            className="w-full rounded-xl border px-3 py-2 font-mono text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
            style={{ borderColor: errors.code ? "#FF4E00" : "#DFE5ED" }}
          />
        </Field>

        <Field label="Tipo de compañía" htmlFor="nc-type" error={errors.tenantType}>
          <select
            id="nc-type"
            value={tenantType}
            disabled
            aria-describedby="nc-type-hint"
            className="w-full cursor-not-allowed rounded-xl border px-3 py-2 text-xs opacity-60 outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
            style={{ borderColor: "#DFE5ED" }}
          >
            <option value={tenantType}>{TENANT_TYPE_LABELS[tenantType]}</option>
          </select>
          <p id="nc-type-hint" className="mt-1 text-xs opacity-60">
            El tipo queda fijado según la cabeza de grupo.
          </p>
        </Field>

        <ToggleSwitch
          id="nc-estado"
          label="Cliente activo"
          checked={estadoActivo}
          onChange={setEstadoActivo}
        />

        <div className="flex justify-end gap-2 pt-1">
          <button type="button" onClick={handleClose} disabled={submitting} className="rounded-xl border px-4 py-2 text-xs font-semibold disabled:opacity-50">
            Cancelar
          </button>
          <button
            type="button"
            onClick={() => void submit()}
            disabled={submitting}
            className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            {submitting && <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden />}
            {submitting ? "Creando…" : "Crear cliente"}
          </button>
        </div>
      </div>
    </Modal>
  );
}

function Field({
  label,
  htmlFor,
  error,
  children,
}: {
  label: string;
  htmlFor: string;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div>
      <label htmlFor={htmlFor} className="mb-1 block text-xs font-semibold">
        {label}
      </label>
      {children}
      {error && (
        <p className="mt-1 text-[10px] font-medium" style={{ color: "#FF4E00" }}>
          {error}
        </p>
      )}
    </div>
  );
}
