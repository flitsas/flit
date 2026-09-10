"use client";

import { useEffect, useRef, useState } from "react";
import { Building2, Loader2 } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { ToggleSwitch } from "@/components/admin/companies/ToggleSwitch";
import { TransitGrantsPicker } from "@/components/admin/companies/TransitGrantsPicker";
import { addTransitGrant } from "@/lib/api/admin-companies";
import {
  ApiError,
  ApiValidationError,
  isHeadTenantType,
  selectableTenantTypes,
  TENANT_TYPE_LABELS,
  tenantTypeLabel,
  type TenantType,
} from "@/lib/api/types";
import type { CompanyListItem, UpdateCompanyRequest } from "@/lib/api/types";
import { hasDigit, sanitizeName, sanitizeTaxId, validateReadableName } from "@/lib/validation/fieldRules";

export interface EditCompanyDialogProps {
  open: boolean;
  company: CompanyListItem;
  onClose: () => void;
  onUpdate: (tenantId: string, request: UpdateCompanyRequest) => Promise<CompanyListItem>;
  onUpdated: (company: CompanyListItem) => void;
  /** HU #12357 — tipos Concesión / Marca Blanca solo SuperAdmin. */
  isSuperAdmin?: boolean;
  /** HU #12357 AC8 — deshabilita cambio de tipo de cabeza con hijos vigentes. */
  activeChildrenCount?: number;
}

type FieldErrors = Partial<Record<"razonSocial" | "nit" | "tenantType" | "transitGrants", string>>;

type WizardStep = "form" | "ot";

const LEGACY_TYPE_LABELS: Record<string, string> = {
  standard: "Estándar (sistema)",
  transit_office: "Organismo de tránsito",
};

const legacyTenantTypeLabel = (value: string): string =>
  TENANT_TYPE_LABELS[value as TenantType] ?? LEGACY_TYPE_LABELS[value] ?? value;

export function EditCompanyDialog({
  open,
  company,
  onClose,
  onUpdate,
  onUpdated,
  isSuperAdmin = true,
  activeChildrenCount = 0,
}: EditCompanyDialogProps) {
  const [razonSocial, setRazonSocial] = useState(company.razonSocial);
  const [nit, setNit] = useState(company.nit);
  const [tenantType, setTenantType] = useState<string>(company.tenantType);
  const [estadoActivo, setEstadoActivo] = useState(company.estadoActivo);
  const [transitGrantIds, setTransitGrantIds] = useState<string[]>([]);
  const [step, setStep] = useState<WizardStep>("form");
  const [errors, setErrors] = useState<FieldErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const firstFieldRef = useRef<HTMLInputElement>(null);

  const initialWasHead = isHeadTenantType(company.tenantType);
  const changingToConcesion = tenantType === "CONCESION" && company.tenantType !== "CONCESION";
  const needsOtStep = changingToConcesion;
  const isMarcaBlanca = tenantType === "MARCA_BLANCA";
  const typeChangeBlocked = initialWasHead && activeChildrenCount > 0;

  const catalogTypes = selectableTenantTypes(isSuperAdmin);
  const typeOptions: string[] = catalogTypes.includes(tenantType as TenantType)
    ? [...catalogTypes]
    : [tenantType, ...catalogTypes];

  useEffect(() => {
    if (open) {
      setRazonSocial(company.razonSocial);
      setNit(company.nit);
      setTenantType(company.tenantType);
      setEstadoActivo(company.estadoActivo);
      setTransitGrantIds([]);
      setStep("form");
      setErrors({});
      setFormError(null);
      firstFieldRef.current?.focus();
    }
  }, [open, company]);

  if (!open) {
    return null;
  }

  const handleClose = () => {
    if (submitting) {
      return;
    }
    onClose();
  };

  const validate = (): FieldErrors => {
    const next: FieldErrors = {};
    const rs = razonSocial.trim();
    const n = nit.trim();
    if (!rs) next.razonSocial = "La razón social es obligatoria.";
    else {
      const e = validateReadableName(rs, "La razón social");
      if (e) next.razonSocial = e;
    }
    if (!n) next.nit = "El NIT es obligatorio.";
    else if (!hasDigit(n)) next.nit = "El NIT debe contener al menos un dígito.";
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
    setFormError(null);
    try {
      const updated = await onUpdate(company.id, {
        razonSocial: razonSocial.trim(),
        nit: nit.trim(),
        tenantType,
        estadoActivo,
        rowVersion: company.rowVersion,
      });

      if (needsOtStep) {
        for (const otId of transitGrantIds) {
          await addTransitGrant(updated.id, otId);
        }
      }

      onUpdated(updated);
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        setFormError(
          "La compañía fue modificada por otra persona. Cierra el diálogo y vuelve a abrirlo para ver los datos actuales.",
        );
      } else if (error instanceof ApiValidationError) {
        const mapped: FieldErrors = {};
        for (const { field, message } of error.errors) {
          if (field === "razonSocial" || field === "nit" || field === "tenantType") {
            mapped[field] = message;
          }
        }
        setErrors(
          Object.keys(mapped).length > 0
            ? mapped
            : { razonSocial: "No se pudieron guardar los cambios." },
        );
        setStep("form");
      } else {
        setErrors({ razonSocial: "No se pudieron guardar los cambios. Intenta de nuevo." });
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
      title={step === "ot" ? "Organismos de la Concesión" : "Editar compañía"}
      titleClassName="text-base font-bold text-[#557EFF]"
    >
      <div className="space-y-3.5">
        {step === "form" && (
          <>
            <Field label="Razón Social" htmlFor="ec-razon" error={errors.razonSocial}>
              <input
                ref={firstFieldRef}
                id="ec-razon"
                type="text"
                value={razonSocial}
                onChange={(e) => setRazonSocial(sanitizeName(e.target.value))}
                maxLength={255}
                className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
                style={{ borderColor: errors.razonSocial ? "#FF4E00" : "#DFE5ED" }}
              />
            </Field>

            <Field label="NIT" htmlFor="ec-nit" error={errors.nit}>
              <input
                id="ec-nit"
                type="text"
                value={nit}
                onChange={(e) => setNit(sanitizeTaxId(e.target.value))}
                maxLength={20}
                placeholder="900123456-1"
                className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
                style={{ borderColor: errors.nit ? "#FF4E00" : "#DFE5ED" }}
              />
            </Field>

            <Field label="Código" htmlFor="ec-code" hint="El código es el identificador único del tenant y no se puede modificar.">
              <input
                id="ec-code"
                type="text"
                value={company.code}
                readOnly
                disabled
                className="w-full cursor-not-allowed rounded-xl border px-3 py-2 font-mono text-xs opacity-60 outline-none"
                style={{ background: "rgba(223,229,237,0.35)" }}
              />
            </Field>

            <Field label="Tipo de compañía" htmlFor="ec-type" error={errors.tenantType}>
              <select
                id="ec-type"
                value={tenantType}
                onChange={(e) => setTenantType(e.target.value)}
                disabled={typeChangeBlocked}
                title={
                  typeChangeBlocked
                    ? "No puedes cambiar el tipo mientras la cabeza tenga clientes hijos vigentes."
                    : undefined
                }
                aria-describedby={typeChangeBlocked ? "ec-type-blocked" : undefined}
                className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20 disabled:cursor-not-allowed disabled:opacity-60"
                style={{ borderColor: errors.tenantType ? "#FF4E00" : "#DFE5ED" }}
              >
                {typeOptions.map((t) => (
                  <option key={t} value={t}>
                    {legacyTenantTypeLabel(t)}
                  </option>
                ))}
              </select>
              {typeChangeBlocked && (
                <p id="ec-type-blocked" className="mt-1 text-[10px] opacity-70">
                  Desvincula o da de baja a los clientes hijos antes de cambiar el tipo de cabeza (
                  {activeChildrenCount} vigente{activeChildrenCount === 1 ? "" : "s"}).
                </p>
              )}
            </Field>

            {isMarcaBlanca && (
              <div
                className="space-y-2 rounded-xl border px-3 py-2.5"
                style={{ borderColor: "#DFE5ED", background: "rgba(85,126,255,0.04)" }}
              >
                <p className="text-xs font-semibold">Dominio de integración</p>
                <input
                  type="text"
                  disabled
                  placeholder="Pendiente de registro — punto de integración"
                  className="w-full cursor-not-allowed rounded-lg border px-3 py-2 text-xs opacity-60"
                  aria-describedby="ec-domain-hint"
                />
                <p id="ec-domain-hint" className="text-[10px] opacity-60">
                  Opera en todos los organismos habilitados por la plataforma salvo los bloqueados.
                </p>
              </div>
            )}

            <p className="text-[10px] opacity-60">
              Tipo actual en ficha: <strong>{tenantTypeLabel(company.tenantType)}</strong>
            </p>

            <ToggleSwitch
              id="ec-estado"
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

        {formError && (
          <p
            role="alert"
            className="rounded-lg px-3 py-2 text-[11px] font-medium"
            style={{ background: "rgba(255,78,0,0.1)", color: "#FF4E00" }}
          >
            {formError}
          </p>
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
            {submitting ? "Guardando…" : step === "form" && needsOtStep ? "Siguiente" : "Guardar cambios"}
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
