"use client";

import { AlertCircle, FileText, Paperclip, X } from "lucide-react";
import { useId, useRef, useState, type ReactNode } from "react";
import { DR_FLIT_SUPPORT_MAX_ATTACHMENTS } from "@/lib/api/dr-flit-client";
import type {
  DrFlitSupportCaseDraft,
  DrFlitSupportFrequency,
  DrFlitSupportPriority,
} from "./dr-flit-chat-types";
import {
  DR_FLIT_FREQUENCY_LABELS,
  DR_FLIT_SUPPORT_LIMITS,
  validateAttachmentFile,
  validateSupportDraft,
  type DrFlitSupportErrors,
  type DrFlitSupportField,
} from "./dr-flit-support-case";

const FREQUENCIES: DrFlitSupportFrequency[] = ["una_vez", "a_veces", "siempre"];
const PRIORITIES: DrFlitSupportPriority[] = ["Alta", "Media", "Baja"];

/** Orden en que se enfoca el primer campo con error. */
const FIELD_ORDER: DrFlitSupportField[] = [
  "nombre", "email", "compania", "telefono", "titulo", "detalle", "resultadoEsperado", "frecuencia", "prioridad",
];

const inputStyle = {
  borderColor: "var(--dr-flit-border-input)",
  background: "var(--dr-flit-card-bg)",
  color: "var(--dr-flit-text)",
} as const;

const inputClass =
  "mt-1 w-full rounded-[10px] border px-3 py-2 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)]";

function formatSize(bytes: number): string {
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/**
 * HU #12929 — formulario del caso de soporte dentro del chat. Nombre, correo y compañía llegan
 * prellenados; la fecha se fija sola. Los adjuntos se suben al elegirlos y en el borrador solo queda
 * su id. «Continuar» no llama a ningún endpoint: lleva al resumen de confirmación (HU #12930).
 */
export function DrFlitSupportCaseForm({
  draft,
  onChange,
  onContinue,
  onCancel,
  onAttach,
}: {
  draft: DrFlitSupportCaseDraft;
  onChange: (draft: DrFlitSupportCaseDraft) => void;
  onContinue: () => void;
  onCancel: () => void;
  /** Sube el archivo y lo agrega al borrador; devuelve el motivo si falló. */
  onAttach: (file: File) => Promise<string | null>;
}) {
  const id = useId();
  const [showErrors, setShowErrors] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [attachError, setAttachError] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const formRef = useRef<HTMLFormElement>(null);

  const errors: DrFlitSupportErrors = showErrors ? validateSupportDraft(draft) : {};
  const set = <K extends keyof DrFlitSupportCaseDraft>(key: K, value: DrFlitSupportCaseDraft[K]) =>
    onChange({ ...draft, [key]: value });
  const fieldId = (field: string) => `${id}-${field}`;
  const errorId = (field: string) => `${id}-${field}-error`;

  const handleContinue = () => {
    const found = validateSupportDraft(draft);
    setShowErrors(true);
    const first = FIELD_ORDER.find((f) => found[f]);
    if (first) {
      queueMicrotask(() =>
        formRef.current?.querySelector<HTMLElement>(`[data-field="${first}"]`)?.focus(),
      );
      return;
    }
    onContinue();
  };

  const handleFiles = async (files: FileList | null) => {
    if (!files || files.length === 0) return;
    setAttachError(null);
    let count = draft.attachments.length;
    setUploading(true);
    try {
      for (const file of Array.from(files)) {
        const localError = validateAttachmentFile(file, count);
        if (localError) {
          setAttachError(localError);
          continue;
        }
        const uploadError = await onAttach(file);
        if (uploadError) setAttachError(uploadError);
        else count += 1;
      }
    } finally {
      setUploading(false);
      if (fileInputRef.current) fileInputRef.current.value = "";
    }
  };

  const errorText = (field: DrFlitSupportField) =>
    errors[field] ? (
      <span id={errorId(field)} className="mt-1 flex items-center gap-1 text-xs" style={{ color: "var(--dr-flit-text)" }}>
        <AlertCircle className="h-3.5 w-3.5 shrink-0" style={{ color: "var(--dr-flit-accent)" }} aria-hidden="true" />
        {errors[field]}
      </span>
    ) : null;

  const invalidStyle = (field: DrFlitSupportField) =>
    errors[field] ? { ...inputStyle, borderColor: "var(--dr-flit-accent)" } : inputStyle;

  const textField = (
    field: DrFlitSupportField & keyof DrFlitSupportCaseDraft,
    label: string,
    options: { type?: string; required?: boolean; maxLength?: number; autoComplete?: string } = {},
  ) => (
    <div>
      <label htmlFor={fieldId(field)} className="block text-xs font-semibold" style={{ color: "var(--dr-flit-text)" }}>
        {label}
        {options.required !== false ? <span aria-hidden="true"> *</span> : <span className="font-normal"> (opcional)</span>}
      </label>
      <input
        id={fieldId(field)}
        data-field={field}
        type={options.type ?? "text"}
        value={draft[field] as string}
        onChange={(e) => set(field, e.target.value as never)}
        required={options.required !== false}
        maxLength={options.maxLength}
        autoComplete={options.autoComplete}
        aria-invalid={errors[field] ? true : undefined}
        aria-describedby={errors[field] ? errorId(field) : undefined}
        className={inputClass}
        style={invalidStyle(field)}
      />
      {errorText(field)}
    </div>
  );

  const textArea = (field: "detalle" | "resultadoEsperado", label: string, max: number) => (
    <div>
      <label htmlFor={fieldId(field)} className="block text-xs font-semibold" style={{ color: "var(--dr-flit-text)" }}>
        {label}
        <span aria-hidden="true"> *</span>
      </label>
      <textarea
        id={fieldId(field)}
        data-field={field}
        value={draft[field]}
        onChange={(e) => set(field, e.target.value)}
        required
        maxLength={max}
        rows={3}
        aria-invalid={errors[field] ? true : undefined}
        aria-describedby={errors[field] ? errorId(field) : undefined}
        className={`${inputClass} resize-y`}
        style={invalidStyle(field)}
      />
      {errorText(field)}
    </div>
  );

  const choices = <T extends string>(
    field: "frecuencia" | "prioridad",
    legend: string,
    values: T[],
    labelOf: (v: T) => string,
  ): ReactNode => (
    <fieldset aria-describedby={errors[field] ? errorId(field) : undefined}>
      <legend className="text-xs font-semibold" style={{ color: "var(--dr-flit-text)" }}>
        {legend}
        <span aria-hidden="true"> *</span>
      </legend>
      <div className="mt-1.5 flex flex-wrap gap-2">
        {values.map((value, index) => {
          const checked = draft[field] === value;
          return (
            <label
              key={value}
              className="cursor-pointer rounded-full border px-3 py-1.5 text-xs font-semibold focus-within:ring-2 focus-within:ring-[var(--dr-flit-focus)]"
              style={{
                borderColor: checked ? "var(--dr-flit-brand-blue)" : "var(--dr-flit-border-input)",
                background: checked ? "var(--dr-flit-icon-tint)" : "var(--dr-flit-card-bg)",
                color: checked ? "var(--dr-flit-brand)" : "var(--dr-flit-text-secondary)",
              }}
            >
              <input
                type="radio"
                name={fieldId(field)}
                value={value}
                checked={checked}
                onChange={() => set(field, value as never)}
                data-field={index === 0 ? field : undefined}
                className="sr-only"
              />
              {labelOf(value)}
            </label>
          );
        })}
      </div>
      {errorText(field)}
    </fieldset>
  );

  const hasErrors = showErrors && Object.keys(validateSupportDraft(draft)).length > 0;

  return (
    <form
      ref={formRef}
      noValidate
      aria-label="Formulario del caso de soporte"
      onSubmit={(e) => {
        e.preventDefault();
        handleContinue();
      }}
      className="space-y-3 rounded-[var(--dr-flit-radius-card)] border p-4"
      style={{
        borderColor: "var(--dr-flit-border)",
        background: "var(--dr-flit-card-bg)",
        boxShadow: "var(--dr-flit-shadow-card)",
      }}
    >
      <p className="text-xs font-semibold uppercase tracking-[0.12em]" style={{ color: "var(--dr-flit-text-muted)" }}>
        Caso de soporte · {draft.fecha}
      </p>

      {hasErrors ? (
        <p role="alert" className="flex items-center gap-2 rounded-lg border px-3 py-2 text-xs" style={{ borderColor: "var(--dr-flit-accent)", color: "var(--dr-flit-text)" }}>
          <AlertCircle className="h-4 w-4 shrink-0" style={{ color: "var(--dr-flit-accent)" }} aria-hidden="true" />
          Revisa los campos marcados antes de continuar.
        </p>
      ) : null}

      {textField("nombre", "Nombre", { autoComplete: "name" })}
      {textField("email", "Correo", { type: "email", autoComplete: "email" })}
      {textField("compania", "Compañía", { autoComplete: "organization" })}
      {textField("telefono", "Teléfono", { type: "tel", required: false, maxLength: DR_FLIT_SUPPORT_LIMITS.telefono, autoComplete: "tel" })}
      {textField("titulo", "Título del caso", { maxLength: DR_FLIT_SUPPORT_LIMITS.titulo })}
      {textArea("detalle", "Detalle del error", DR_FLIT_SUPPORT_LIMITS.detalle)}
      {textArea("resultadoEsperado", "Resultado esperado", DR_FLIT_SUPPORT_LIMITS.resultadoEsperado)}
      {choices("frecuencia", "¿Con qué frecuencia pasa?", FREQUENCIES, (v) => DR_FLIT_FREQUENCY_LABELS[v])}
      {choices("prioridad", "Prioridad", PRIORITIES, (v) => v)}

      <fieldset>
        <legend className="text-xs font-semibold" style={{ color: "var(--dr-flit-text)" }}>
          ¿Quieres adjuntar archivos?
        </legend>
        <div className="mt-1.5 flex gap-2">
          {[true, false].map((value) => {
            const checked = draft.adjuntar === value;
            return (
              <label
                key={String(value)}
                className="cursor-pointer rounded-full border px-3 py-1.5 text-xs font-semibold focus-within:ring-2 focus-within:ring-[var(--dr-flit-focus)]"
                style={{
                  borderColor: checked ? "var(--dr-flit-brand-blue)" : "var(--dr-flit-border-input)",
                  background: checked ? "var(--dr-flit-icon-tint)" : "var(--dr-flit-card-bg)",
                  color: checked ? "var(--dr-flit-brand)" : "var(--dr-flit-text-secondary)",
                }}
              >
                <input
                  type="radio"
                  name={fieldId("adjuntar")}
                  checked={checked}
                  onChange={() => set("adjuntar", value)}
                  className="sr-only"
                />
                {value ? "Sí" : "No"}
              </label>
            );
          })}
        </div>
      </fieldset>

      {draft.adjuntar ? (
        <div className="space-y-2">
          <div
            className="flex flex-col items-center gap-1.5 rounded-[var(--dr-flit-radius-card)] border-2 border-dashed px-3 py-4 text-center"
            style={{ borderColor: "var(--dr-flit-brand-blue)", background: "var(--dr-flit-card-bg)" }}
          >
            <Paperclip className="h-5 w-5" style={{ color: "var(--dr-flit-brand-blue)" }} aria-hidden="true" />
            <label htmlFor={fieldId("archivos")} className="cursor-pointer text-sm font-semibold" style={{ color: "var(--dr-flit-brand-blue)" }}>
              {uploading ? "Subiendo…" : "Elegir archivos"}
            </label>
            <input
              ref={fileInputRef}
              id={fieldId("archivos")}
              type="file"
              multiple
              accept=".png,.jpg,.jpeg,.webp,.pdf,.txt,image/png,image/jpeg,image/webp,application/pdf,text/plain"
              disabled={uploading || draft.attachments.length >= DR_FLIT_SUPPORT_MAX_ATTACHMENTS}
              onChange={(e) => void handleFiles(e.target.files)}
              className="sr-only"
            />
            <span className="text-xs" style={{ color: "var(--dr-flit-text-secondary)" }}>
              PNG, JPG, WEBP, PDF o TXT · hasta 20 MB · máximo {DR_FLIT_SUPPORT_MAX_ATTACHMENTS} archivos
            </span>
          </div>
          {attachError ? (
            <p role="alert" className="flex items-center gap-1 text-xs" style={{ color: "var(--dr-flit-text)" }}>
              <AlertCircle className="h-3.5 w-3.5 shrink-0" style={{ color: "var(--dr-flit-accent)" }} aria-hidden="true" />
              {attachError}
            </p>
          ) : null}
          {draft.attachments.length > 0 ? (
            <ul className="m-0 list-none space-y-1.5 p-0" aria-label="Archivos adjuntos">
              {draft.attachments.map((a) => (
                <li
                  key={a.id}
                  className="flex items-center gap-2 rounded-lg border px-2.5 py-1.5 text-xs"
                  style={{ borderColor: "var(--dr-flit-border)", color: "var(--dr-flit-text)" }}
                >
                  <FileText className="h-4 w-4 shrink-0" style={{ color: "var(--dr-flit-brand-blue)" }} aria-hidden="true" />
                  <span className="min-w-0 flex-1 truncate">{a.filename}</span>
                  <span style={{ color: "var(--dr-flit-text-secondary)" }}>{formatSize(a.sizeBytes)}</span>
                  <button
                    type="button"
                    onClick={() => set("attachments", draft.attachments.filter((x) => x.id !== a.id))}
                    aria-label={`Quitar ${a.filename}`}
                    className="rounded-full p-1 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)]"
                  >
                    <X className="h-3.5 w-3.5" aria-hidden="true" />
                  </button>
                </li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : null}

      <div className="flex flex-col gap-2 pt-1">
        <button
          type="submit"
          disabled={uploading}
          className="w-full rounded-full px-4 py-3 text-sm font-semibold text-white transition-opacity hover:opacity-95 disabled:opacity-60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2"
          style={{ background: "var(--dr-flit-gradient-primary)" }}
        >
          Continuar
        </button>
        <button
          type="button"
          onClick={onCancel}
          className="w-full rounded-full border px-4 py-2.5 text-sm font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--dr-flit-focus)] focus-visible:ring-offset-2"
          style={{ borderColor: "var(--dr-flit-brand)", color: "var(--dr-flit-brand)", background: "var(--dr-flit-card-bg)" }}
        >
          Cancelar
        </button>
      </div>
    </form>
  );
}
