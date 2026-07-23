"use client";

import { useEffect, useState } from "react";
import { Loader2 } from "lucide-react";
import { OtSidePanel } from "@/components/admin/transit-offices/OtSidePanel";
import { OT_INPUT_CLS } from "@/components/admin/transit-offices/ot-form-styles";
import { ApiValidationError } from "@/lib/api/types";
import { PROCEDURE_TYPES } from "@/lib/constants/procedure-types";
import type {
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
  onClose: () => void;
  onSubmit: (input: LegalRepresentativeInput) => Promise<LegalRepresentativeSaved>;
  onSaved: (saved: LegalRepresentativeSaved) => void;
  onError: (message: string) => void;
}

interface FormState {
  companyNit: string;
  companyName: string;
  companyEmail: string;
  companyAddress: string;
  companyCity: string;
  companyPhone: string;
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

const EMPTY: FormState = {
  companyNit: "",
  companyName: "",
  companyEmail: "",
  companyAddress: "",
  companyCity: "",
  companyPhone: "",
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

// La respuesta del backend no proyecta el correo/dirección/ciudad/teléfono de la compañía (solo NIT
// y nombre); al editar se precargan los datos disponibles y el resto queda editable en blanco.
function fromItem(item: LegalRepresentativeItem): FormState {
  return {
    ...EMPTY,
    companyNit: item.companyDocumentNumber,
    companyName: item.companyName,
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

  const toggleProcedureType = (id: string) =>
    setForm((f) => ({
      ...f,
      procedureTypeIds: f.procedureTypeIds.includes(id)
        ? f.procedureTypeIds.filter((x) => x !== id)
        : [...f.procedureTypeIds, id],
    }));

  // Validación en cliente (los mismos requeridos que valida el backend).
  const isValid =
    form.companyNit.trim() !== "" &&
    form.companyName.trim() !== "" &&
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
      const saved = await onSubmit({
        companyNit: form.companyNit.trim(),
        companyName: form.companyName.trim(),
        companyEmail: trimmed(form.companyEmail),
        companyAddress: trimmed(form.companyAddress),
        companyCity: trimmed(form.companyCity),
        companyPhone: trimmed(form.companyPhone),
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
          <h3 className="text-[11px] font-bold uppercase tracking-wide opacity-60">
            Compañía representada
          </h3>

          <Field id="lr-companyNit" label="NIT de la compañía" error={fieldErrors.companyNit}>
            <input
              id="lr-companyNit"
              value={form.companyNit}
              onChange={(e) => patch({ companyNit: e.target.value })}
              className={OT_INPUT_CLS}
              style={errStyle("companyNit")}
              placeholder="NIT de la compañía representada"
            />
          </Field>

          <Field id="lr-companyName" label="Nombre de la compañía" error={fieldErrors.companyName}>
            <input
              id="lr-companyName"
              value={form.companyName}
              onChange={(e) => patch({ companyName: e.target.value })}
              className={OT_INPUT_CLS}
              style={errStyle("companyName")}
              placeholder="Razón social"
            />
          </Field>

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <Field id="lr-companyEmail" label="Correo (opcional)" error={fieldErrors.companyEmail}>
              <input
                id="lr-companyEmail"
                type="email"
                value={form.companyEmail}
                onChange={(e) => patch({ companyEmail: e.target.value })}
                className={OT_INPUT_CLS}
                style={errStyle("companyEmail")}
              />
            </Field>
            <Field id="lr-companyPhone" label="Teléfono (opcional)" error={fieldErrors.companyPhone}>
              <input
                id="lr-companyPhone"
                value={form.companyPhone}
                onChange={(e) => patch({ companyPhone: e.target.value })}
                className={OT_INPUT_CLS}
                style={errStyle("companyPhone")}
              />
            </Field>
            <Field id="lr-companyAddress" label="Dirección (opcional)" error={fieldErrors.companyAddress}>
              <input
                id="lr-companyAddress"
                value={form.companyAddress}
                onChange={(e) => patch({ companyAddress: e.target.value })}
                className={OT_INPUT_CLS}
                style={errStyle("companyAddress")}
              />
            </Field>
            <Field id="lr-companyCity" label="Ciudad (opcional)" error={fieldErrors.companyCity}>
              <input
                id="lr-companyCity"
                value={form.companyCity}
                onChange={(e) => patch({ companyCity: e.target.value })}
                className={OT_INPUT_CLS}
                style={errStyle("companyCity")}
              />
            </Field>
          </div>
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
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
            {PROCEDURE_TYPES.map((pt) => {
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
