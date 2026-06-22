"use client";

import { useState } from "react";
import { Building2, Loader2, X } from "lucide-react";
import { ToggleSwitch } from "@/components/admin/companies/ToggleSwitch";
import { ApiValidationError, TENANT_TYPE_LABELS } from "@/lib/api/types";
import type { CompanyListItem, CreateCompanyRequest, TenantType } from "@/lib/api/types";

// Modal de alta de compañía (botón "Crear compañía", #10118). Formulario completo:
// Razón Social, NIT, Código, Tipo y Estado. Validación inline en cliente + mapeo de
// errores 422 del backend por campo. La llamada a la API se inyecta vía `onCreate`
// (mismo patrón que CompanyConfigTabs/WhitelistTextarea: el componente es presentacional).
export interface CreateCompanyDialogProps {
  open: boolean;
  onClose: () => void;
  /** Persiste la compañía. Debe lanzar ApiValidationError ante un 422 del backend. */
  onCreate: (request: CreateCompanyRequest) => Promise<CompanyListItem>;
  onCreated: (company: CompanyListItem) => void;
}

type FieldErrors = Partial<Record<"razonSocial" | "nit" | "code" | "tenantType", string>>;

const TENANT_TYPES = Object.keys(TENANT_TYPE_LABELS) as TenantType[];

export function CreateCompanyDialog({ open, onClose, onCreate, onCreated }: CreateCompanyDialogProps) {
  const [razonSocial, setRazonSocial] = useState("");
  const [nit, setNit] = useState("");
  const [code, setCode] = useState("");
  const [tenantType, setTenantType] = useState<TenantType>("RENTING");
  const [estadoActivo, setEstadoActivo] = useState(true);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [submitting, setSubmitting] = useState(false);

  if (!open) {
    return null;
  }

  const reset = () => {
    setRazonSocial("");
    setNit("");
    setCode("");
    setTenantType("RENTING");
    setEstadoActivo(true);
    setErrors({});
  };

  const handleClose = () => {
    if (submitting) {
      return;
    }
    reset();
    onClose();
  };

  // Validación client-side de campos requeridos antes de llamar a la API.
  const validate = (): FieldErrors => {
    const next: FieldErrors = {};
    if (!razonSocial.trim()) next.razonSocial = "La razón social es obligatoria.";
    if (!nit.trim()) next.nit = "El NIT es obligatorio.";
    if (!code.trim()) next.code = "El código es obligatorio.";
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
      const created = await onCreate({
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
        // Mapea { field, message } del 422 a los errores por campo (no cierra el modal).
        const mapped: FieldErrors = {};
        for (const { field, message } of error.errors) {
          if (field === "razonSocial" || field === "nit" || field === "code" || field === "tenantType") {
            mapped[field] = message;
          }
        }
        setErrors(Object.keys(mapped).length > 0 ? mapped : { code: "No se pudo crear la compañía." });
      } else {
        setErrors({ code: "No se pudo crear la compañía. Intenta de nuevo." });
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
      aria-labelledby="create-company-title"
    >
      <div className="w-full max-w-md rounded-2xl border bg-white p-6 shadow-2xl dark:bg-[#0B0F14]" style={{ borderColor: "#DFE5ED" }}>
        <div className="mb-4 flex items-start justify-between">
          <div className="flex items-center gap-2">
            <span className="flex h-9 w-9 items-center justify-center rounded-xl" style={{ background: "#557EFF" }}>
              <Building2 className="h-4.5 w-4.5 text-white" />
            </span>
            <h2 id="create-company-title" className="text-base font-bold" style={{ color: "#557EFF" }}>
              Crear compañía
            </h2>
          </div>
          <button type="button" aria-label="Cerrar" onClick={handleClose} className="text-slate-400 hover:text-slate-700">
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="space-y-3.5">
          <Field label="Razón Social" htmlFor="cc-razon" error={errors.razonSocial}>
            <input
              id="cc-razon"
              type="text"
              value={razonSocial}
              onChange={(e) => setRazonSocial(e.target.value)}
              maxLength={255}
              className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
              style={{ borderColor: errors.razonSocial ? "#FF4E00" : "#DFE5ED" }}
            />
          </Field>

          <Field label="NIT" htmlFor="cc-nit" error={errors.nit}>
            <input
              id="cc-nit"
              type="text"
              value={nit}
              onChange={(e) => setNit(e.target.value)}
              maxLength={20}
              placeholder="900123456-1"
              className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
              style={{ borderColor: errors.nit ? "#FF4E00" : "#DFE5ED" }}
            />
          </Field>

          <Field label="Código" htmlFor="cc-code" error={errors.code} hint="Identificador único de la compañía (máx. 32).">
            <input
              id="cc-code"
              type="text"
              value={code}
              onChange={(e) => setCode(e.target.value)}
              maxLength={32}
              className="w-full rounded-xl border px-3 py-2 font-mono text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
              style={{ borderColor: errors.code ? "#FF4E00" : "#DFE5ED" }}
            />
          </Field>

          <Field label="Tipo de compañía" htmlFor="cc-type" error={errors.tenantType}>
            <select
              id="cc-type"
              value={tenantType}
              onChange={(e) => setTenantType(e.target.value as TenantType)}
              className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
              style={{ borderColor: errors.tenantType ? "#FF4E00" : "#DFE5ED" }}
            >
              {TENANT_TYPES.map((t) => (
                <option key={t} value={t}>
                  {TENANT_TYPE_LABELS[t]}
                </option>
              ))}
            </select>
          </Field>

          <ToggleSwitch
            id="cc-estado"
            label="Compañía activa"
            description="Si se desactiva, la compañía queda registrada pero no operativa."
            checked={estadoActivo}
            onChange={setEstadoActivo}
          />

          <div className="flex justify-end gap-2 pt-1">
            <button
              type="button"
              onClick={handleClose}
              disabled={submitting}
              className="rounded-xl border px-4 py-2 text-xs font-semibold disabled:opacity-50"
              style={{ borderColor: "#DFE5ED" }}
            >
              Cancelar
            </button>
            <button
              type="button"
              onClick={submit}
              disabled={submitting}
              className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
              style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
            >
              {submitting && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
              {submitting ? "Creando…" : "Crear compañía"}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

function Field({
  label,
  htmlFor,
  error,
  hint,
  children,
}: {
  label: string;
  htmlFor: string;
  error?: string;
  hint?: string;
  children: React.ReactNode;
}) {
  return (
    <div>
      <label htmlFor={htmlFor} className="mb-1 block text-xs font-semibold">
        {label}
      </label>
      {children}
      {hint && !error && <p className="mt-1 text-[10px] opacity-60">{hint}</p>}
      {error && (
        <p className="mt-1 text-[10px] font-medium" style={{ color: "#FF4E00" }}>
          {error}
        </p>
      )}
    </div>
  );
}
