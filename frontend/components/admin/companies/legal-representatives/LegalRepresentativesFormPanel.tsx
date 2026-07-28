"use client";

import { useEffect, useState } from "react";
import { Loader2, Plus, Trash2 } from "lucide-react";
import { OtSidePanel } from "@/components/admin/transit-offices/OtSidePanel";
import { OT_INPUT_CLS } from "@/components/admin/transit-offices/ot-form-styles";
import { ApiValidationError } from "@/lib/api/types";
import type {
  AssignableProcedureType,
  LegalRepresentativeCompanyInput,
  LegalRepresentativeInput,
  LegalRepresentativeItem,
  LegalRepresentativeSaved,
} from "@/lib/api/admin-legal-representatives";

// Tipos de documento del representante — mismos que en el resto de la app (ActorsForm / Baúl).
const DOC_TYPE_OPTIONS: { value: string; label: string }[] = [
  { value: "CC", label: "Cédula de ciudadanía (CC)" },
  { value: "CE", label: "Cédula de extranjería (CE)" },
  { value: "PAS", label: "Pasaporte (PAS)" },
  { value: "TI", label: "Tarjeta de identidad (TI)" },
];

export interface LegalRepresentativesFormPanelProps {
  open: boolean;
  /** Representante a editar; `null` = alta. */
  editing: LegalRepresentativeItem | null;
  /** Catálogo de tipos de trámite asignables (activos + publicados) cargado del backend. */
  procedureTypes: AssignableProcedureType[];
  onClose: () => void;
  onSubmit: (input: LegalRepresentativeInput) => Promise<LegalRepresentativeSaved>;
  onSaved: (saved: LegalRepresentativeSaved) => void;
  onError: (message: string) => void;
}

// Una fila de empresa dentro del formulario (HU #10934): el representante se crea una vez y se le
// agregan empresas. La primera fila es la compañía primaria.
interface CompanyRow {
  nit: string;
  name: string;
  email: string;
  address: string;
  city: string;
  phone: string;
}

interface FormState {
  companies: CompanyRow[];
  documentType: string;
  documentNumber: string;
  firstLastName: string;
  secondLastName: string;
  name: string;
  email: string;
  address: string;
  city: string;
  phone: string;
  procedureTypeIds: string[];
}

const EMPTY_COMPANY: CompanyRow = { nit: "", name: "", email: "", address: "", city: "", phone: "" };

const EMPTY: FormState = {
  companies: [{ ...EMPTY_COMPANY }],
  documentType: "CC",
  documentNumber: "",
  firstLastName: "",
  secondLastName: "",
  name: "",
  email: "",
  address: "",
  city: "",
  phone: "",
  procedureTypeIds: [],
};

// La respuesta del backend proyecta las compañías del representante (NIT + razón social); el contacto
// de cada compañía no se proyecta, así que al editar se precargan NIT y nombre y el resto queda en
// blanco. Si por compatibilidad el detalle no trae `companies`, cae a la compañía primaria denormalizada.
function fromItem(item: LegalRepresentativeItem): FormState {
  const companies: CompanyRow[] =
    item.companies && item.companies.length > 0
      ? item.companies.map((c) => ({ ...EMPTY_COMPANY, nit: c.nit, name: c.name }))
      : [{ ...EMPTY_COMPANY, nit: item.companyDocumentNumber, name: item.companyName }];
  return {
    companies,
    documentType: item.documentType || "CC",
    documentNumber: item.documentNumber,
    firstLastName: item.firstLastName,
    secondLastName: item.secondLastName ?? "",
    name: item.name,
    email: item.email ?? "",
    address: item.address ?? "",
    city: item.city ?? "",
    phone: item.phone ?? "",
    procedureTypeIds: [...item.procedureTypeIds],
  };
}

/**
 * Panel de alta/edición de un representante legal (HU #10904): datos de la compañía representada,
 * datos del representante y los tipos de trámite que puede firmar. Los errores 422 del backend se
 * muestran por campo. El guardado puede emitir la señal `sin_firma_ni_identidad` (no bloqueante):
 * la resuelve el contenedor tras `onSaved`.
 */
export function LegalRepresentativesFormPanel({
  open,
  editing,
  procedureTypes,
  onClose,
  onSubmit,
  onSaved,
  onError,
}: LegalRepresentativesFormPanelProps) {
  const [form, setForm] = useState<FormState>(EMPTY);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [banner, setBanner] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (!open) return;
    // Reinicia el formulario al abrir (alta en blanco o edición precargada).
    // eslint-disable-next-line react-hooks/set-state-in-effect -- sincroniza el formulario al abrir el panel
    setForm(editing ? fromItem(editing) : EMPTY);
    setFieldErrors({});
    setBanner(null);
  }, [open, editing]);

  const patch = (p: Partial<FormState>) => setForm((f) => ({ ...f, ...p }));

  const patchCompany = (index: number, p: Partial<CompanyRow>) =>
    setForm((f) => ({
      ...f,
      companies: f.companies.map((c, i) => (i === index ? { ...c, ...p } : c)),
    }));

  const addCompany = () =>
    setForm((f) => ({ ...f, companies: [...f.companies, { ...EMPTY_COMPANY }] }));

  const removeCompany = (index: number) =>
    setForm((f) => ({
      ...f,
      // Nunca queda sin empresas: si se elimina la última, se deja una fila en blanco.
      companies: f.companies.length <= 1 ? [{ ...EMPTY_COMPANY }] : f.companies.filter((_, i) => i !== index),
    }));

  const toggleProcedureType = (id: string) =>
    setForm((f) => ({
      ...f,
      procedureTypeIds: f.procedureTypeIds.includes(id)
        ? f.procedureTypeIds.filter((x) => x !== id)
        : [...f.procedureTypeIds, id],
    }));

  // Validación en cliente (los mismos requeridos que valida el backend): al menos una empresa con NIT
  // y razón social, y los datos del representante-persona.
  const companiesValid =
    form.companies.length > 0 &&
    form.companies.every((c) => c.nit.trim() !== "" && c.name.trim() !== "");
  const isValid =
    companiesValid &&
    form.documentType.trim() !== "" &&
    form.documentNumber.trim() !== "" &&
    form.firstLastName.trim() !== "" &&
    form.name.trim() !== "";

  const canSubmit = isValid && !submitting;

  const handleSubmit = async () => {
    setSubmitting(true);
    setBanner(null);
    setFieldErrors({});
    try {
      const trimmed = (v: string) => (v.trim() === "" ? null : v.trim());
      const companies: LegalRepresentativeCompanyInput[] = form.companies.map((c) => ({
        nit: c.nit.trim(),
        name: c.name.trim(),
        email: trimmed(c.email),
        address: trimmed(c.address),
        city: trimmed(c.city),
        phone: trimmed(c.phone),
      }));
      const primary = companies[0];
      const saved = await onSubmit({
        companies,
        // Retrocompatibilidad: la primera compañía también viaja en los campos planos.
        companyNit: primary.nit,
        companyName: primary.name,
        companyEmail: primary.email,
        companyAddress: primary.address,
        companyCity: primary.city,
        companyPhone: primary.phone,
        documentType: form.documentType,
        documentNumber: form.documentNumber.trim(),
        firstLastName: form.firstLastName.trim(),
        secondLastName: trimmed(form.secondLastName),
        name: form.name.trim(),
        email: trimmed(form.email),
        address: trimmed(form.address),
        city: trimmed(form.city),
        phone: trimmed(form.phone),
        procedureTypeIds: form.procedureTypeIds,
      });
      onSaved(saved);
    } catch (err) {
      if (err instanceof ApiValidationError) {
        const mapped: Record<string, string> = {};
        for (const e of err.errors) {
          if (e.field) mapped[e.field] = e.message;
        }
        setFieldErrors(mapped);
        setBanner("Revisa los campos marcados: hay valores inválidos.");
      } else {
        onError(
          editing
            ? "No se pudo actualizar el representante. Intenta de nuevo."
            : "No se pudo registrar el representante. Intenta de nuevo.",
        );
      }
    } finally {
      setSubmitting(false);
    }
  };

  const footer = (
    <button
      type="button"
      disabled={!canSubmit}
      onClick={() => void handleSubmit()}
      className="flex w-full items-center justify-center gap-2 rounded-xl py-2.5 text-xs font-semibold text-white disabled:opacity-50"
      style={{ background: "#557EFF" }}
    >
      {submitting && <Loader2 className="h-4 w-4 animate-spin" />}
      {editing ? "Guardar cambios" : "Registrar representante"}
    </button>
  );

  const errStyle = (field: string) =>
    fieldErrors[field] ? { borderColor: "#FF4E00" } : undefined;

  return (
    <OtSidePanel
      open={open}
      title={editing ? "Editar representante legal" : "Nuevo representante legal"}
      ariaLabel={editing ? "Editar representante legal" : "Registrar representante legal"}
      onClose={onClose}
      disabled={submitting}
      footer={footer}
    >
      <div className="space-y-5">
        {banner && (
          <p
            role="alert"
            className="rounded-xl border px-3 py-2 text-[11px] font-medium"
            style={{ borderColor: "#FF4E00", color: "#FF4E00" }}
          >
            {banner}
          </p>
        )}

        <section className="space-y-3">
          <div className="flex items-center justify-between gap-2">
            <h3 className="text-[11px] font-bold uppercase tracking-wide opacity-60">
              Empresas representadas
            </h3>
            <button
              type="button"
              onClick={addCompany}
              className="flex items-center gap-1 rounded-lg border px-2.5 py-1.5 text-[11px] font-semibold"
              style={{ color: "#557EFF", borderColor: "#557EFF" }}
            >
              <Plus className="h-3.5 w-3.5" /> Agregar empresa
            </button>
          </div>
          <p className="text-[11px] opacity-60">
            El representante se registra una sola vez; agrégale todas las empresas que representa. La
            primera es la compañía primaria.
          </p>

          {form.companies.map((company, index) => {
            const err = (suffix: string) =>
              fieldErrors[`companies[${index}].${suffix}`] ??
              (index === 0 ? fieldErrors[`company${suffix.charAt(0).toUpperCase()}${suffix.slice(1)}`] : undefined);
            return (
              <div
                key={index}
                className="space-y-3 rounded-xl border px-3 py-3"
                style={{ borderColor: "#DFE5ED" }}
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="text-[11px] font-semibold" style={{ color: "#162744" }}>
                    {index === 0 ? "Empresa primaria" : `Empresa ${index + 1}`}
                  </span>
                  {form.companies.length > 1 && (
                    <button
                      type="button"
                      onClick={() => removeCompany(index)}
                      aria-label={`Quitar empresa ${index + 1}`}
                      className="flex items-center gap-1 rounded-lg border px-2 py-1 text-[11px] font-semibold"
                      style={{ color: "#FF4E00", borderColor: "#f0c38e" }}
                    >
                      <Trash2 className="h-3.5 w-3.5" /> Quitar
                    </button>
                  )}
                </div>

                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  <Field id={`lr-companyNit-${index}`} label="NIT de la compañía" error={err("nit")}>
                    <input
                      id={`lr-companyNit-${index}`}
                      value={company.nit}
                      onChange={(e) => patchCompany(index, { nit: e.target.value })}
                      className={OT_INPUT_CLS}
                      style={err("nit") ? { borderColor: "#FF4E00" } : undefined}
                      placeholder="NIT"
                    />
                  </Field>
                  <Field id={`lr-companyName-${index}`} label="Razón social" error={err("name")}>
                    <input
                      id={`lr-companyName-${index}`}
                      value={company.name}
                      onChange={(e) => patchCompany(index, { name: e.target.value })}
                      className={OT_INPUT_CLS}
                      style={err("name") ? { borderColor: "#FF4E00" } : undefined}
                      placeholder="Razón social"
                    />
                  </Field>
                  <Field id={`lr-companyEmail-${index}`} label="Correo (opcional)" error={err("email")}>
                    <input
                      id={`lr-companyEmail-${index}`}
                      type="email"
                      value={company.email}
                      onChange={(e) => patchCompany(index, { email: e.target.value })}
                      className={OT_INPUT_CLS}
                    />
                  </Field>
                  <Field id={`lr-companyPhone-${index}`} label="Teléfono (opcional)" error={err("phone")}>
                    <input
                      id={`lr-companyPhone-${index}`}
                      value={company.phone}
                      onChange={(e) => patchCompany(index, { phone: e.target.value })}
                      className={OT_INPUT_CLS}
                    />
                  </Field>
                  <Field id={`lr-companyAddress-${index}`} label="Dirección (opcional)" error={err("address")}>
                    <input
                      id={`lr-companyAddress-${index}`}
                      value={company.address}
                      onChange={(e) => patchCompany(index, { address: e.target.value })}
                      className={OT_INPUT_CLS}
                    />
                  </Field>
                  <Field id={`lr-companyCity-${index}`} label="Ciudad (opcional)" error={err("city")}>
                    <input
                      id={`lr-companyCity-${index}`}
                      value={company.city}
                      onChange={(e) => patchCompany(index, { city: e.target.value })}
                      className={OT_INPUT_CLS}
                    />
                  </Field>
                </div>
              </div>
            );
          })}
        </section>

        <section className="space-y-3">
          <h3 className="text-[11px] font-bold uppercase tracking-wide opacity-60">
            Representante legal
          </h3>

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <Field id="lr-doctype" label="Tipo de documento" error={fieldErrors.documentType}>
              <select
                id="lr-doctype"
                value={form.documentType}
                onChange={(e) => patch({ documentType: e.target.value })}
                className={OT_INPUT_CLS}
                style={errStyle("documentType")}
              >
                {DOC_TYPE_OPTIONS.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.label}
                  </option>
                ))}
              </select>
            </Field>
            <Field id="lr-docnumber" label="Número de documento" error={fieldErrors.documentNumber}>
              <input
                id="lr-docnumber"
                value={form.documentNumber}
                onChange={(e) => patch({ documentNumber: e.target.value })}
                className={OT_INPUT_CLS}
                style={errStyle("documentNumber")}
                inputMode="numeric"
              />
            </Field>
          </div>

          <Field id="lr-name" label="Nombres" error={fieldErrors.name}>
            <input
              id="lr-name"
              value={form.name}
              onChange={(e) => patch({ name: e.target.value })}
              className={OT_INPUT_CLS}
              style={errStyle("name")}
              placeholder="Nombres del representante"
            />
          </Field>

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <Field id="lr-firstLastName" label="Primer apellido" error={fieldErrors.firstLastName}>
              <input
                id="lr-firstLastName"
                value={form.firstLastName}
                onChange={(e) => patch({ firstLastName: e.target.value })}
                className={OT_INPUT_CLS}
                style={errStyle("firstLastName")}
              />
            </Field>
            <Field id="lr-secondLastName" label="Segundo apellido (opcional)" error={fieldErrors.secondLastName}>
              <input
                id="lr-secondLastName"
                value={form.secondLastName}
                onChange={(e) => patch({ secondLastName: e.target.value })}
                className={OT_INPUT_CLS}
                style={errStyle("secondLastName")}
              />
            </Field>
          </div>

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <Field id="lr-email" label="Correo (opcional)" error={fieldErrors.email}>
              <input
                id="lr-email"
                type="email"
                value={form.email}
                onChange={(e) => patch({ email: e.target.value })}
                className={OT_INPUT_CLS}
                style={errStyle("email")}
                placeholder="Para la validación de identidad"
              />
            </Field>
            <Field id="lr-phone" label="Teléfono (opcional)" error={fieldErrors.phone}>
              <input
                id="lr-phone"
                value={form.phone}
                onChange={(e) => patch({ phone: e.target.value })}
                className={OT_INPUT_CLS}
                style={errStyle("phone")}
              />
            </Field>
            <Field id="lr-address" label="Dirección (opcional)" error={fieldErrors.address}>
              <input
                id="lr-address"
                value={form.address}
                onChange={(e) => patch({ address: e.target.value })}
                className={OT_INPUT_CLS}
                style={errStyle("address")}
              />
            </Field>
            <Field id="lr-city" label="Ciudad (opcional)" error={fieldErrors.city}>
              <input
                id="lr-city"
                value={form.city}
                onChange={(e) => patch({ city: e.target.value })}
                className={OT_INPUT_CLS}
                style={errStyle("city")}
              />
            </Field>
          </div>
        </section>

        <fieldset className="space-y-2">
          <legend className="text-[11px] font-bold uppercase tracking-wide opacity-60">
            Tipos de trámite que puede firmar
          </legend>
          {fieldErrors.procedureTypeIds && (
            <p className="text-[11px] font-medium" style={{ color: "#FF4E00" }} role="alert">
              {fieldErrors.procedureTypeIds}
            </p>
          )}
          {procedureTypes.length === 0 ? (
            <p className="text-[11px] opacity-60">
              No hay tipos de trámite habilitados en el módulo de trámites. Publica al menos un tipo
              (activo y publicado) para poder asignarlo al representante.
            </p>
          ) : (
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
              {procedureTypes.map((pt) => {
                const checked = form.procedureTypeIds.includes(pt.id);
                return (
                  <label
                    key={pt.id}
                    className="flex cursor-pointer items-center gap-2 rounded-xl border px-3 py-2 text-xs"
                    style={checked ? { borderColor: "#557EFF" } : undefined}
                  >
                    <input
                      type="checkbox"
                      checked={checked}
                      onChange={() => toggleProcedureType(pt.id)}
                      className="h-3.5 w-3.5 accent-[#557EFF]"
                    />
                    <span className="font-medium">{pt.name}</span>
                  </label>
                );
              })}
            </div>
          )}
        </fieldset>
      </div>
    </OtSidePanel>
  );
}

function Field({
  id,
  label,
  error,
  children,
}: {
  id: string;
  label: string;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div>
      <label htmlFor={id} className="mb-1 block text-xs font-semibold">
        {label}
      </label>
      {children}
      {error && (
        <p className="mt-1 text-[11px] font-medium" style={{ color: "#FF4E00" }} role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
