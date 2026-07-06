"use client";

import { useEffect, useRef, useState } from "react";
import { Building2, Loader2 } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { ToggleSwitch } from "@/components/admin/companies/ToggleSwitch";
import { ApiError, ApiValidationError, TENANT_TYPE_LABELS } from "@/lib/api/types";
import type { CompanyListItem, TenantType, UpdateCompanyRequest } from "@/lib/api/types";
import { hasDigit, sanitizeName, sanitizeTaxId, validateReadableName } from "@/lib/validation/fieldRules";

// Modal de edición de compañía (botón "Editar" del listado, #10118). Edita Razón
// Social, NIT, Tipo y Estado; el Código es inmutable (campo de solo lectura). Valida
// requeridos en cliente y mapea los errores 422 del backend por campo. La llamada a la
// API se inyecta vía `onUpdate` (componente presentacional, igual que CreateCompanyDialog).
export interface EditCompanyDialogProps {
  open: boolean;
  /** Compañía a editar; prellena el formulario. */
  company: CompanyListItem;
  onClose: () => void;
  /** Persiste los cambios. Debe lanzar ApiValidationError ante un 422 del backend. */
  onUpdate: (tenantId: string, request: UpdateCompanyRequest) => Promise<CompanyListItem>;
  onUpdated: (company: CompanyListItem) => void;
}

type FieldErrors = Partial<Record<"razonSocial" | "nit" | "tenantType", string>>;

const TENANT_TYPES = Object.keys(TENANT_TYPE_LABELS) as TenantType[];

// Etiquetas legibles para tipos heredados que existen en BD pero no pertenecen al
// catálogo B2B (el listado los incluye). Se muestran tal cual para no perderlos al editar.
const LEGACY_TYPE_LABELS: Record<string, string> = {
  standard: "Estándar (sistema)",
  transit_office: "Organismo de tránsito",
};

const tenantTypeLabel = (value: string): string =>
  TENANT_TYPE_LABELS[value as TenantType] ?? LEGACY_TYPE_LABELS[value] ?? value;

export function EditCompanyDialog({
  open,
  company,
  onClose,
  onUpdate,
  onUpdated,
}: EditCompanyDialogProps) {
  const [razonSocial, setRazonSocial] = useState(company.razonSocial);
  const [nit, setNit] = useState(company.nit);
  // string (no TenantType): el tipo actual puede ser un valor heredado fuera del catálogo.
  const [tenantType, setTenantType] = useState<string>(company.tenantType);
  const [estadoActivo, setEstadoActivo] = useState(company.estadoActivo);
  const [errors, setErrors] = useState<FieldErrors>({});
  // Error a nivel de formulario (no asociado a un campo), p. ej. el 409 de concurrencia.
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const firstFieldRef = useRef<HTMLInputElement>(null);

  // Sincroniza el formulario al abrir o cambiar la compañía en edición, y deja el foco
  // en el primer campo (accesibilidad: foco inicial dentro del diálogo).
  useEffect(() => {
    if (open) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- prellenado del formulario al abrir el modal (mismo patrón que documents)
      setRazonSocial(company.razonSocial);
      setNit(company.nit);
      setTenantType(company.tenantType);
      setEstadoActivo(company.estadoActivo);
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

  // Opciones del select: catálogo B2B y, si el tipo actual es heredado (fuera del
  // catálogo), se añade al inicio para que se muestre correctamente y se preserve.
  const typeOptions: string[] = TENANT_TYPES.includes(tenantType as TenantType)
    ? TENANT_TYPES
    : [tenantType, ...TENANT_TYPES];

  // Validación client-side de campos requeridos antes de llamar a la API.
  const validate = (): FieldErrors => {
    const next: FieldErrors = {};
    const rs = razonSocial.trim();
    const n = nit.trim();
    if (!rs) next.razonSocial = "La razón social es obligatoria.";
    else { const e = validateReadableName(rs, "La razón social"); if (e) next.razonSocial = e; }
    if (!n) next.nit = "El NIT es obligatorio.";
    else if (!hasDigit(n)) next.nit = "El NIT debe contener al menos un dígito.";
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
    setFormError(null);
    try {
      const updated = await onUpdate(company.id, {
        razonSocial: razonSocial.trim(),
        nit: nit.trim(),
        tenantType,
        estadoActivo,
        rowVersion: company.rowVersion,
      });
      onUpdated(updated);
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        // Edición concurrente: otra persona modificó la compañía mientras se editaba.
        setFormError(
          "La compañía fue modificada por otra persona. Cierra el diálogo y vuelve a abrirlo para ver los datos actuales.",
        );
      } else if (error instanceof ApiValidationError) {
        // Mapea { field, message } del 422 a los errores por campo (no cierra el modal).
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
      } else {
        setErrors({ razonSocial: "No se pudieron guardar los cambios. Intenta de nuevo." });
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
      title="Editar compañía"
      titleClassName="text-base font-bold text-[#557EFF]"
    >
      <div className="space-y-3.5">
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
              className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
              style={{ borderColor: errors.tenantType ? "#FF4E00" : "#DFE5ED" }}
            >
              {typeOptions.map((t) => (
                <option key={t} value={t}>
                  {tenantTypeLabel(t)}
                </option>
              ))}
            </select>
          </Field>

          <ToggleSwitch
            id="ec-estado"
            label="Compañía activa"
            description="Si se desactiva, la compañía queda registrada pero no operativa."
            checked={estadoActivo}
            onChange={setEstadoActivo}
          />

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
              disabled={submitting}
              className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
              style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
            >
              {submitting && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
              {submitting ? "Guardando…" : "Guardar cambios"}
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
