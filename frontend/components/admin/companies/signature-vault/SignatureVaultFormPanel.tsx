"use client";

import { useEffect, useState } from "react";
import { Loader2 } from "lucide-react";
import { OtSidePanel } from "@/components/admin/transit-offices/OtSidePanel";
import { OT_INPUT_CLS } from "@/components/admin/transit-offices/ot-form-styles";
import { ApiValidationError } from "@/lib/api/types";
import type {
  SignatureVaultCreated,
  SignatureVaultInput,
} from "@/lib/api/admin-signature-vault";
import { SignatureCapture } from "./SignatureCapture";

// Tipos de documento del apoderado — mismos que en el resto de la app (ActorsForm).
const DOC_TYPE_OPTIONS: { value: string; label: string }[] = [
  { value: "CC", label: "Cédula de ciudadanía (CC)" },
  { value: "CE", label: "Cédula de extranjería (CE)" },
  { value: "NIT", label: "NIT" },
  { value: "PAS", label: "Pasaporte (PAS)" },
  { value: "TI", label: "Tarjeta de identidad (TI)" },
];

export interface SignatureVaultFormPanelProps {
  open: boolean;
  onClose: () => void;
  onSubmit: (input: SignatureVaultInput) => Promise<SignatureVaultCreated>;
  onSaved: (saved: SignatureVaultCreated) => void;
  onError: (message: string) => void;
}

interface FormState {
  documentType: string;
  documentNumber: string;
  nitEmpresa: string;
  fullName: string;
  vigenciaDesde: string;
  vigenciaHasta: string;
}

const EMPTY: FormState = {
  documentType: "CC",
  documentNumber: "",
  nitEmpresa: "",
  fullName: "",
  vigenciaDesde: "",
  vigenciaHasta: "",
};

/**
 * Panel de alta de firma del baúl (HU #10644): datos del apoderado + captura del
 * artefacto (dibujo o PNG). El botón queda deshabilitado hasta que el formulario es
 * válido y hay artefacto. Los errores 422 del backend se muestran por campo y el
 * caso `firma_activa_existente` se resalta como mensaje amistoso.
 */
export function SignatureVaultFormPanel({
  open,
  onClose,
  onSubmit,
  onSaved,
  onError,
}: SignatureVaultFormPanelProps) {
  const [form, setForm] = useState<FormState>(EMPTY);
  const [artefacto, setArtefacto] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [banner, setBanner] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (!open) return;
    // Limpia el formulario al abrir.
    // eslint-disable-next-line react-hooks/set-state-in-effect -- reinicia el formulario al abrir el panel
    setForm(EMPTY);
    setArtefacto(null);
    setFieldErrors({});
    setBanner(null);
  }, [open]);

  const patch = (p: Partial<FormState>) => setForm((f) => ({ ...f, ...p }));

  // Validación en cliente (los mismos requisitos que valida el backend).
  const vigenciaInvalida =
    form.vigenciaDesde !== "" &&
    form.vigenciaHasta !== "" &&
    form.vigenciaHasta < form.vigenciaDesde;

  const isValid =
    form.documentType.trim() !== "" &&
    form.documentNumber.trim() !== "" &&
    form.nitEmpresa.trim() !== "" &&
    form.fullName.trim() !== "" &&
    form.vigenciaDesde !== "" &&
    form.vigenciaHasta !== "" &&
    !vigenciaInvalida;

  const canSubmit = isValid && artefacto !== null && !submitting;

  const handleSubmit = async () => {
    if (!artefacto) return;
    setSubmitting(true);
    setBanner(null);
    setFieldErrors({});
    try {
      const saved = await onSubmit({
        documentType: form.documentType,
        documentNumber: form.documentNumber.trim(),
        nitEmpresa: form.nitEmpresa.trim(),
        fullName: form.fullName.trim(),
        vigenciaDesde: form.vigenciaDesde,
        vigenciaHasta: form.vigenciaHasta,
        artefactoFirmaBase64: artefacto,
        mandateSignerId: null,
      });
      onSaved(saved);
    } catch (err) {
      if (err instanceof ApiValidationError) {
        const mapped: Record<string, string> = {};
        let friendly: string | null = null;
        for (const e of err.errors) {
          const code = (e as { code?: string }).code;
          if (code === "firma_activa_existente") {
            friendly =
              "Ya existe una firma activa para este apoderado en esta empresa. Anúlala antes de registrar una nueva.";
          }
          if (e.field) {
            mapped[e.field] = e.message;
          }
        }
        setFieldErrors(mapped);
        setBanner(friendly ?? "Revisa los campos marcados: hay valores inválidos.");
      } else {
        onError("No se pudo registrar la firma. Intenta de nuevo.");
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
      Registrar firma
    </button>
  );

  const errStyle = (field: string) =>
    fieldErrors[field] ? { borderColor: "#FF4E00" } : undefined;

  return (
    <OtSidePanel
      open={open}
      title="Registrar firma"
      ariaLabel="Registrar firma del apoderado"
      onClose={onClose}
      disabled={submitting}
      footer={footer}
    >
      <div className="space-y-4">
        {banner && (
          <p
            role="alert"
            className="rounded-xl border px-3 py-2 text-[11px] font-medium"
            style={{ borderColor: "#FF4E00", color: "#FF4E00" }}
          >
            {banner}
          </p>
        )}

        <div>
          <label htmlFor="sv-doctype" className="mb-1 block text-xs font-semibold">
            Tipo de documento
          </label>
          <select
            id="sv-doctype"
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
          <FieldError message={fieldErrors.documentType} />
        </div>

        <div>
          <label htmlFor="sv-docnumber" className="mb-1 block text-xs font-semibold">
            Número de documento
          </label>
          <input
            id="sv-docnumber"
            value={form.documentNumber}
            onChange={(e) => patch({ documentNumber: e.target.value })}
            className={OT_INPUT_CLS}
            style={errStyle("documentNumber")}
            placeholder="Número de documento del apoderado"
            inputMode="numeric"
          />
          <FieldError message={fieldErrors.documentNumber} />
        </div>

        <div>
          <label htmlFor="sv-nit" className="mb-1 block text-xs font-semibold">
            NIT de la empresa
          </label>
          <input
            id="sv-nit"
            value={form.nitEmpresa}
            onChange={(e) => patch({ nitEmpresa: e.target.value })}
            className={OT_INPUT_CLS}
            style={errStyle("nitEmpresa")}
            placeholder="NIT de la empresa mandante"
          />
          <FieldError message={fieldErrors.nitEmpresa} />
        </div>

        <div>
          <label htmlFor="sv-fullname" className="mb-1 block text-xs font-semibold">
            Nombre del apoderado
          </label>
          <input
            id="sv-fullname"
            value={form.fullName}
            onChange={(e) => patch({ fullName: e.target.value })}
            className={OT_INPUT_CLS}
            style={errStyle("fullName")}
            placeholder="Nombre completo"
          />
          <FieldError message={fieldErrors.fullName} />
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <div>
            <label htmlFor="sv-desde" className="mb-1 block text-xs font-semibold">
              Vigencia desde
            </label>
            <input
              id="sv-desde"
              type="date"
              value={form.vigenciaDesde}
              onChange={(e) => patch({ vigenciaDesde: e.target.value })}
              className={OT_INPUT_CLS}
              style={errStyle("vigenciaDesde")}
            />
            <FieldError message={fieldErrors.vigenciaDesde} />
          </div>
          <div>
            <label htmlFor="sv-hasta" className="mb-1 block text-xs font-semibold">
              Vigencia hasta
            </label>
            <input
              id="sv-hasta"
              type="date"
              value={form.vigenciaHasta}
              min={form.vigenciaDesde || undefined}
              onChange={(e) => patch({ vigenciaHasta: e.target.value })}
              className={OT_INPUT_CLS}
              style={vigenciaInvalida || fieldErrors.vigenciaHasta ? { borderColor: "#FF4E00" } : undefined}
              aria-invalid={vigenciaInvalida || Boolean(fieldErrors.vigenciaHasta)}
            />
            <FieldError
              message={
                vigenciaInvalida
                  ? "La fecha final debe ser igual o posterior a la inicial."
                  : fieldErrors.vigenciaHasta
              }
            />
          </div>
        </div>

        <fieldset className="space-y-2">
          <legend className="mb-1 text-xs font-semibold">Artefacto de firma</legend>
          <SignatureCapture
            value={artefacto}
            onChange={setArtefacto}
            disabled={submitting}
            error={fieldErrors.artefactoFirmaBase64}
          />
        </fieldset>
      </div>
    </OtSidePanel>
  );
}

function FieldError({ message }: { message?: string }) {
  if (!message) return null;
  return (
    <p className="mt-1 text-[11px] font-medium" style={{ color: "#FF4E00" }} role="alert">
      {message}
    </p>
  );
}
