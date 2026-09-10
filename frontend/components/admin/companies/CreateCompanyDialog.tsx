"use client";

import { useState } from "react";
import { Building2, Loader2 } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { ToggleSwitch } from "@/components/admin/companies/ToggleSwitch";
import { TransitGrantsPicker } from "@/components/admin/companies/TransitGrantsPicker";
import { addTransitGrant } from "@/lib/api/admin-companies";
import {
  ApiValidationError,
  isHeadTenantType,
  selectableTenantTypes,
  TENANT_TYPE_LABELS,
  type TenantType,
} from "@/lib/api/types";
import type { CompanyListItem, CreateCompanyRequest } from "@/lib/api/types";
import {
  hasDigit,
  hasLetterOrDigit,
  sanitizeName,
  sanitizeTaxId,
  sanitizeTenantCode,
  validateReadableName,
} from "@/lib/validation/fieldRules";

export interface CreateCompanyDialogProps {
  open: boolean;
  onClose: () => void;
  /** Persiste la compañía. Debe lanzar ApiValidationError ante un 422 del backend. */
  onCreate: (request: CreateCompanyRequest) => Promise<CompanyListItem>;
  onCreated: (company: CompanyListItem) => void;
  /** HU #12357 — tipos Concesión / Marca Blanca solo SuperAdmin. */
  isSuperAdmin?: boolean;
}

type FieldErrors = Partial<Record<"razonSocial" | "nit" | "code" | "tenantType" | "transitGrants", string>>;

type WizardStep = "form" | "ot" | "review";

export function CreateCompanyDialog({
  open,
  onClose,
  onCreate,
  onCreated,
  isSuperAdmin = true,
}: CreateCompanyDialogProps) {
  const [razonSocial, setRazonSocial] = useState("");
  const [nit, setNit] = useState("");
  const [code, setCode] = useState("");
  const [tenantType, setTenantType] = useState<TenantType>("RENTING");
  const [estadoActivo, setEstadoActivo] = useState(true);
  const [transitGrantIds, setTransitGrantIds] = useState<string[]>([]);
  const [step, setStep] = useState<WizardStep>("form");
  const [errors, setErrors] = useState<FieldErrors>({});
  const [submitting, setSubmitting] = useState(false);

  const tenantTypes = selectableTenantTypes(isSuperAdmin);
  const needsOtStep = tenantType === "CONCESION";
  const isMarcaBlanca = tenantType === "MARCA_BLANCA";

  if (!open) {
    return null;
  }

  const reset = () => {
    setRazonSocial("");
    setNit("");
    setCode("");
    setTenantType("RENTING");
    setEstadoActivo(true);
    setTransitGrantIds([]);
    setStep("form");
    setErrors({});
  };

  const handleClose = () => {
    if (submitting) {
      return;
    }
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

  const validateOt = (): FieldErrors => {
    if (needsOtStep && transitGrantIds.length === 0) {
      return { transitGrants: "Selecciona al menos un organismo de tránsito para la Concesión." };
    }
    return {};
  };

  const goNext = () => {
    const clientErrors = validate();
    if (Object.keys(clientErrors).length > 0) {
      setErrors(clientErrors);
      return;
    }
    setErrors({});
    if (needsOtStep) {
      setStep("ot");
      return;
    }
    void submit();
  };

  const submit = async () => {
    const clientErrors = { ...validate(), ...validateOt() };
    if (Object.keys(clientErrors).length > 0) {
      setErrors(clientErrors);
      if (clientErrors.transitGrants) {
        setStep("ot");
      }
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

      if (needsOtStep) {
        for (const otId of transitGrantIds) {
          await addTransitGrant(created.id, otId);
        }
      }

      reset();
      onCreated(created);
    } catch (error) {
      if (error instanceof ApiValidationError) {
        const mapped: FieldErrors = {};
        for (const { field, message } of error.errors) {
          if (
            field === "razonSocial" ||
            field === "nit" ||
            field === "code" ||
            field === "tenantType"
          ) {
            mapped[field] = message;
          }
        }
        setErrors(Object.keys(mapped).length > 0 ? mapped : { code: "No se pudo crear la compañía." });
        setStep("form");
      } else {
        setErrors({ code: "No se pudo crear la compañía. Intenta de nuevo." });
        setStep("form");
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
      title={step === "ot" ? "Organismos de la Concesión" : "Crear compañía"}
      titleClassName="text-base font-bold text-[#557EFF]"
    >
      <div className="space-y-3.5">
        {step === "form" && (
          <>
            <Field label="Razón Social" htmlFor="cc-razon" error={errors.razonSocial}>
              <input
                id="cc-razon"
                type="text"
                value={razonSocial}
                onChange={(e) => setRazonSocial(sanitizeName(e.target.value))}
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
                onChange={(e) => setNit(sanitizeTaxId(e.target.value))}
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
                onChange={(e) => setCode(sanitizeTenantCode(e.target.value))}
                maxLength={32}
                className="w-full rounded-xl border px-3 py-2 font-mono text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
                style={{ borderColor: errors.code ? "#FF4E00" : "#DFE5ED" }}
              />
            </Field>

            <Field label="Tipo de compañía" htmlFor="cc-type" error={errors.tenantType}>
              <select
                id="cc-type"
                value={tenantType}
                onChange={(e) => {
                  const next = e.target.value as TenantType;
                  setTenantType(next);
                  if (!isHeadTenantType(next)) {
                    setTransitGrantIds([]);
                  }
                }}
                className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
                style={{ borderColor: errors.tenantType ? "#FF4E00" : "#DFE5ED" }}
              >
                {tenantTypes.map((t) => (
                  <option key={t} value={t}>
                    {TENANT_TYPE_LABELS[t]}
                  </option>
                ))}
              </select>
            </Field>

            {isMarcaBlanca && (
              <div
                className="space-y-2 rounded-xl border px-3 py-2.5"
                style={{ borderColor: "#DFE5ED", background: "rgba(85,126,255,0.04)" }}
              >
                <p className="text-xs font-semibold">Dominio de integración</p>
                <input
                  id="cc-domain"
                  type="text"
                  disabled
                  placeholder="Pendiente de registro — punto de integración"
                  className="w-full cursor-not-allowed rounded-lg border px-3 py-2 text-xs opacity-60"
                  style={{ borderColor: "#DFE5ED" }}
                  aria-describedby="cc-domain-hint"
                />
                <p id="cc-domain-hint" className="text-[10px] opacity-60">
                  Marca Blanca opera en todos los organismos habilitados por la plataforma salvo los
                  bloqueados. La gestión de bloqueos está disponible en la ficha tras el alta.
                </p>
              </div>
            )}

            <ToggleSwitch
              id="cc-estado"
              label="Compañía activa"
              description="Si se desactiva, la compañía queda registrada pero no operativa."
              checked={estadoActivo}
              onChange={setEstadoActivo}
            />
          </>
        )}

        {step === "ot" && (
          <TransitGrantsPicker
            selectedIds={transitGrantIds}
            onChange={setTransitGrantIds}
            error={errors.transitGrants}
            disabled={submitting}
          />
        )}

        <div className="flex justify-end gap-2 pt-1">
          {step === "ot" && (
            <button
              type="button"
              onClick={() => setStep("form")}
              disabled={submitting}
              className="rounded-xl border px-4 py-2 text-xs font-semibold disabled:opacity-50"
            >
              Atrás
            </button>
          )}
          <button
            type="button"
            onClick={handleClose}
            disabled={submitting}
            className="rounded-xl border px-4 py-2 text-xs font-semibold disabled:opacity-50"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={step === "form" ? goNext : () => void submit()}
            disabled={submitting}
            className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            {submitting && <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden />}
            {submitting ? "Creando…" : step === "form" && needsOtStep ? "Siguiente" : "Crear compañía"}
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
