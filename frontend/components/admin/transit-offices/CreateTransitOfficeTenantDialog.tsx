"use client";

import { useEffect, useState } from "react";
import { Landmark, Loader2 } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { fetchTransitOffices } from "@/lib/api/admin-companies";
import type { TransitOffice } from "@/lib/api/types";
import { ApiValidationError } from "@/lib/api/types";
import {
  createTransitOfficeTenant,
  fetchTransitOfficeTenants,
  type CreateTransitOfficeRequest,
  type TransitOfficeTenantItem,
} from "@/lib/api/admin-transit-office-tenants";
import {
  hasDigit,
  hasLetterOrDigit,
  sanitizeName,
  sanitizeTaxId,
  sanitizeTenantCode,
  validateReadableName,
} from "@/lib/validation/fieldRules";

// Alta de tenant OT (refactor adminOT, SuperAdmin). Selecciona una oficina del
// catálogo que aún no tenga tenant asociado (cruza fetchTransitOffices contra
// fetchTransitOfficeTenants) y captura razón social/NIT/código. Mismo patrón de
// validación 422 por campo que CreateCompanyDialog.
export interface CreateTransitOfficeTenantDialogProps {
  open: boolean;
  onClose: () => void;
  onCreated: (tenant: TransitOfficeTenantItem) => void;
  /** Oficina del catálogo a preseleccionar (p. ej. la fila «Dar de alta» del listado). */
  initialOfficeId?: string;
}

type FieldErrors = Partial<Record<"transitOfficeId" | "legalName" | "taxId" | "code", string>>;

export function CreateTransitOfficeTenantDialog({
  open,
  onClose,
  onCreated,
  initialOfficeId,
}: CreateTransitOfficeTenantDialogProps) {
  const [availableOffices, setAvailableOffices] = useState<TransitOffice[]>([]);
  const [officesLoading, setOfficesLoading] = useState(false);
  const [officesError, setOfficesError] = useState(false);

  const [transitOfficeId, setTransitOfficeId] = useState("");
  const [legalName, setLegalName] = useState("");
  const [taxId, setTaxId] = useState("");
  const [code, setCode] = useState("");
  const [errors, setErrors] = useState<FieldErrors>({});
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (!open) {
      return;
    }
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial con AbortController
    setOfficesLoading(true);
    setOfficesError(false);
    Promise.all([
      fetchTransitOffices(undefined, controller.signal),
      fetchTransitOfficeTenants({ pageSize: 100 }, controller.signal),
    ])
      .then(([catalog, tenants]) => {
        if (controller.signal.aborted) {
          return;
        }
        const assignedIds = new Set(tenants.data.map((t) => t.transitOfficeId));
        const available = catalog.filter((office) => !assignedIds.has(office.id));
        setAvailableOffices(available);
        if (initialOfficeId && available.some((o) => o.id === initialOfficeId)) {
          setTransitOfficeId(initialOfficeId);
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setOfficesError(true);
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setOfficesLoading(false);
        }
      });
    return () => controller.abort();
  }, [open, initialOfficeId]);

  if (!open) {
    return null;
  }

  const reset = () => {
    setTransitOfficeId("");
    setLegalName("");
    setTaxId("");
    setCode("");
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
    const ln = legalName.trim();
    const tx = taxId.trim();
    const c = code.trim();
    if (!transitOfficeId) next.transitOfficeId = "Selecciona una oficina del catálogo.";
    if (!ln) next.legalName = "La razón social es obligatoria.";
    else {
      const e = validateReadableName(ln, "La razón social");
      if (e) next.legalName = e;
    }
    if (!tx) next.taxId = "El NIT es obligatorio.";
    else if (!hasDigit(tx)) next.taxId = "El NIT debe contener al menos un dígito.";
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
      const request: CreateTransitOfficeRequest = {
        transitOfficeId,
        legalName: legalName.trim(),
        taxId: taxId.trim(),
        code: code.trim(),
      };
      const created = await createTransitOfficeTenant(request);
      reset();
      onCreated(created);
    } catch (error) {
      if (error instanceof ApiValidationError) {
        const mapped: FieldErrors = {};
        for (const { field, message } of error.errors) {
          if (
            field === "transitOfficeId" ||
            field === "legalName" ||
            field === "taxId" ||
            field === "code"
          ) {
            mapped[field] = message;
          }
        }
        setErrors(
          Object.keys(mapped).length > 0
            ? mapped
            : { code: "No se pudo crear el organismo de tránsito." },
        );
      } else {
        setErrors({ code: "No se pudo crear el organismo de tránsito. Intenta de nuevo." });
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
      icon={Landmark}
      title="Dar de alta Organismo de Tránsito"
      titleClassName="text-base font-bold text-[#557EFF]"
    >
      <div className="space-y-3.5">
          <Field label="Oficina del catálogo" htmlFor="cot-office" error={errors.transitOfficeId}>
            {officesLoading ? (
              <p className="text-xs opacity-60">Cargando oficinas disponibles…</p>
            ) : officesError ? (
              <p className="text-xs" style={{ color: "#FF4E00" }}>
                No se pudo cargar el catálogo de oficinas.
              </p>
            ) : availableOffices.length === 0 ? (
              <p className="text-xs opacity-60">
                Todas las oficinas del catálogo ya tienen un tenant OT asociado.
              </p>
            ) : (
              <select
                id="cot-office"
                value={transitOfficeId}
                onChange={(e) => setTransitOfficeId(e.target.value)}
                className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
                style={{ borderColor: errors.transitOfficeId ? "#FF4E00" : undefined }}
              >
                <option value="">Selecciona una oficina…</option>
                {availableOffices.map((office) => (
                  <option key={office.id} value={office.id}>
                    {office.name} ({office.code})
                  </option>
                ))}
              </select>
            )}
          </Field>

          <Field label="Razón Social" htmlFor="cot-legal-name" error={errors.legalName}>
            <input
              id="cot-legal-name"
              type="text"
              value={legalName}
              onChange={(e) => setLegalName(sanitizeName(e.target.value))}
              maxLength={255}
              className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
              style={{ borderColor: errors.legalName ? "#FF4E00" : undefined }}
            />
          </Field>

          <Field label="NIT" htmlFor="cot-tax-id" error={errors.taxId}>
            <input
              id="cot-tax-id"
              type="text"
              value={taxId}
              onChange={(e) => setTaxId(sanitizeTaxId(e.target.value))}
              maxLength={20}
              placeholder="900123456-1"
              className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
              style={{ borderColor: errors.taxId ? "#FF4E00" : undefined }}
            />
          </Field>

          <Field
            label="Código"
            htmlFor="cot-code"
            error={errors.code}
            hint="Identificador único del tenant (máx. 32)."
          >
            <input
              id="cot-code"
              type="text"
              value={code}
              onChange={(e) => setCode(sanitizeTenantCode(e.target.value))}
              maxLength={32}
              className="w-full rounded-xl border px-3 py-2 font-mono text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
              style={{ borderColor: errors.code ? "#FF4E00" : undefined }}
            />
          </Field>

          <div className="flex justify-end gap-2 pt-1">
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
              onClick={submit}
              disabled={submitting || officesLoading}
              className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
              style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
            >
              {submitting && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
              {submitting ? "Creando…" : "Crear Organismo de Tránsito"}
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
