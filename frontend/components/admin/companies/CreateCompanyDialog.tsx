"use client";

import { useState } from "react";
import { Building2, Loader2 } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { ToggleSwitch } from "@/components/admin/companies/ToggleSwitch";
import { TransitGrantsPicker } from "@/components/admin/companies/TransitGrantsPicker";
import { addTransitGrant } from "@/lib/api/admin-companies";
import { domainErrorCode, domainErrorMessage, isValidHostFormat, registerAdminDomain } from "@/lib/api/domain-client";
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
  sanitizeName,
  sanitizeTaxId,
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

/** `general`: error sin campo propio (HU #13607 — antes se mostraba bajo el campo Código, que ya no existe). */
type FieldErrors = Partial<
  Record<"razonSocial" | "nit" | "tenantType" | "transitGrants" | "domainHost" | "general", string>
>;

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
  const [tenantType, setTenantType] = useState<TenantType>("RENTING");
  const [estadoActivo, setEstadoActivo] = useState(true);
  const [transitGrantIds, setTransitGrantIds] = useState<string[]>([]);
  const [domainHost, setDomainHost] = useState("");
  const [step, setStep] = useState<WizardStep>("form");
  const [errors, setErrors] = useState<FieldErrors>({});
  const [submitting, setSubmitting] = useState(false);
  /** HU #12427 AC3 — el registro del dominio en el alta es opcional y omisible: si el PUT falla
   * tras crear la compañía, esta NO se deshace. Se pausa el cierre del diálogo con un aviso en
   * vez de perder ese error, y se deja al usuario continuar (reintentar desde la ficha). */
  const [pendingCreated, setPendingCreated] = useState<CompanyListItem | null>(null);
  const [domainWarning, setDomainWarning] = useState<string | null>(null);

  const tenantTypes = selectableTenantTypes(isSuperAdmin);
  const needsOtStep = tenantType === "CONCESION";
  const isMarcaBlanca = tenantType === "MARCA_BLANCA";

  if (!open) {
    return null;
  }

  const reset = () => {
    setRazonSocial("");
    setNit("");
    setTenantType("RENTING");
    setEstadoActivo(true);
    setTransitGrantIds([]);
    setDomainHost("");
    setStep("form");
    setErrors({});
    setPendingCreated(null);
    setDomainWarning(null);
  };

  const finishAfterDomainWarning = () => {
    if (!pendingCreated) return;
    const created = pendingCreated;
    reset();
    onCreated(created);
  };

  const handleClose = () => {
    if (submitting) {
      return;
    }
    if (pendingCreated) {
      // La compañía ya se creó (el PUT del dominio opcional falló) — cerrar debe seguir
      // notificando al padre, no perder la creación.
      finishAfterDomainWarning();
      onClose();
      return;
    }
    reset();
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
    if (isMarcaBlanca) {
      const host = domainHost.trim();
      if (host && !isValidHostFormat(host)) {
        next.domainHost =
          "El dominio no tiene un formato válido. Usa solo el nombre de host, sin esquema ni puerto.";
      }
    }
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
        tenantType,
        estadoActivo,
      });

      if (needsOtStep) {
        for (const otId of transitGrantIds) {
          await addTransitGrant(created.id, otId);
        }
      }

      const host = domainHost.trim().toLowerCase();
      if (isMarcaBlanca && host) {
        try {
          await registerAdminDomain(created.id, { host });
        } catch (domainError) {
          // La compañía YA se creó — no se deshace. Se pausa el cierre para que el usuario vea
          // el motivo y sepa que puede reintentar el registro del dominio desde la ficha.
          setPendingCreated(created);
          setDomainWarning(
            domainErrorMessage(domainErrorCode(domainError)) || "No se pudo registrar el dominio.",
          );
          setSubmitting(false);
          return;
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
            field === "tenantType"
          ) {
            mapped[field] = message;
          }
        }
        setErrors(Object.keys(mapped).length > 0 ? mapped : { general: "No se pudo crear la compañía." });
        setStep("form");
      } else {
        setErrors({ general: "No se pudo crear la compañía. Intenta de nuevo." });
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
      title={pendingCreated ? "Compañía creada" : step === "ot" ? "Organismos de la Concesión" : "Crear compañía"}
      titleClassName="text-base font-bold text-[#557EFF]"
    >
      <div className="space-y-3.5">
        {pendingCreated && (
          <div
            className="space-y-3 rounded-xl border p-3 text-xs"
            style={{ borderColor: "#F9AC00", background: "rgba(249,172,0,0.08)" }}
            role="alert"
          >
            <p style={{ color: "#162744" }}>
              La compañía <strong>{pendingCreated.razonSocial}</strong> se creó correctamente, pero no se
              pudo registrar el dominio.
            </p>
            <p className="font-medium" style={{ color: "#8a6000" }}>
              {domainWarning}
            </p>
            <p style={{ color: "#162744" }}>
              Puedes registrar el dominio más tarde desde la ficha de la compañía.
            </p>
            <div className="flex justify-end">
              <button
                type="button"
                onClick={() => {
                  finishAfterDomainWarning();
                  onClose();
                }}
                className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
                style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
              >
                Continuar
              </button>
            </div>
          </div>
        )}

        {!pendingCreated && step === "form" && (
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
                style={{ borderColor: errors.domainHost ? "#FF4E00" : "#DFE5ED", background: "rgba(85,126,255,0.04)" }}
              >
                <label htmlFor="cc-domain" className="block text-xs font-semibold">
                  Dominio de la red (opcional)
                </label>
                <input
                  id="cc-domain"
                  type="text"
                  value={domainHost}
                  onChange={(e) => setDomainHost(e.target.value)}
                  maxLength={253}
                  placeholder="app.tudominio.com"
                  className="w-full rounded-lg border px-3 py-2 font-mono text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
                  style={{ borderColor: errors.domainHost ? "#FF4E00" : "#DFE5ED" }}
                  aria-describedby="cc-domain-hint"
                  aria-invalid={Boolean(errors.domainHost)}
                />
                {errors.domainHost ? (
                  <p role="alert" id="cc-domain-hint" className="text-xs font-medium" style={{ color: "#FF4E00" }}>
                    {errors.domainHost}
                  </p>
                ) : (
                  <p id="cc-domain-hint" className="text-xs opacity-60">
                    Puedes dejarlo en blanco y registrarlo después desde la ficha de la compañía.
                  </p>
                )}
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

        {!pendingCreated && step === "ot" && (
          <TransitGrantsPicker
            selectedIds={transitGrantIds}
            onChange={setTransitGrantIds}
            error={errors.transitGrants}
            disabled={submitting}
          />
        )}

        {!pendingCreated && errors.general && (
          <p role="alert" className="text-xs font-medium" style={{ color: "#FF4E00" }}>
            {errors.general}
          </p>
        )}

        {!pendingCreated && (
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
        )}
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
      {hint && !error && <p className="mt-1 text-xs opacity-60">{hint}</p>}
      {error && (
        <p className="mt-1 text-xs font-medium" style={{ color: "#FF4E00" }}>
          {error}
        </p>
      )}
    </div>
  );
}
