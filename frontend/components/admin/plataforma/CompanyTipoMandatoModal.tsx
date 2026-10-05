"use client";

import { useId, useState } from "react";
import { AlertTriangle, FileSignature } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import type { CompanyOtMandateRuleView } from "@/lib/api/admin-plataforma-mandatos";
import {
  MANDATO_TIPOS,
  resolveTipoNegocio,
  tipoNegocioLabel,
  type MandatoTipoNegocio,
} from "@/lib/plataforma/mandato-templates";

/** HU #13151 — valores que el Super Admin confirma para la regla de una compañía. */
export interface CompanyTipoMandatoValues {
  tipo: MandatoTipoNegocio;
  institutionalName: string;
  institutionalNit: string;
  chamberCity: string;
  sigla: string;
}

export interface CompanyTipoMandatoModalProps {
  /** Fila vigente de la compañía (se actualiza tras recargar por un conflicto). */
  row: CompanyOtMandateRuleView;
  busy: boolean;
  /** Mensaje de error del último intento (validación, API o conflicto). */
  error: string | null;
  /** true si el último intento terminó en conflicto de concurrencia. */
  conflict: boolean;
  onSave: (values: CompanyTipoMandatoValues) => void;
  onCancel: () => void;
  /** Descarta lo escrito y toma los valores vigentes de la fila. */
  onReload: () => void;
}

function initialValues(row: CompanyOtMandateRuleView): CompanyTipoMandatoValues {
  return {
    tipo: resolveTipoNegocio(row.assignmentMode),
    institutionalName: row.institutionalMandataryName ?? "",
    institutionalNit: row.institutionalMandataryNit ?? "",
    chamberCity: row.chamberCity ?? "",
    sigla: row.mandatarySigla ?? "",
  };
}

const FIELD =
  "w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm text-[#162244] disabled:opacity-50 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white";

export function CompanyTipoMandatoModal({
  row,
  busy,
  error,
  conflict,
  onSave,
  onCancel,
  onReload,
}: CompanyTipoMandatoModalProps) {
  const uid = useId();
  const [values, setValues] = useState<CompanyTipoMandatoValues>(() => initialValues(row));
  const [confirming, setConfirming] = useState(false);
  const [nameError, setNameError] = useState(false);

  const institucional = values.tipo === "institucional";
  const set = <K extends keyof CompanyTipoMandatoValues>(key: K, value: CompanyTipoMandatoValues[K]) =>
    setValues((prev) => ({ ...prev, [key]: value }));

  const handleSave = () => {
    if (institucional && !values.institutionalName.trim()) {
      setNameError(true);
      return;
    }
    setNameError(false);
    if (values.tipo === "abierto") {
      setConfirming(true);
      return;
    }
    onSave(values);
  };

  const handleReload = () => {
    setValues(initialValues(row));
    setNameError(false);
    setConfirming(false);
    onReload();
  };

  if (confirming) {
    return (
      <Modal
        open
        onClose={() => setConfirming(false)}
        busy={busy}
        icon={AlertTriangle}
        iconBg="#F9AC00"
        title="Confirmar Mandato abierto"
        titleClassName="text-base font-bold text-[#162744]"
        size="md"
      >
        <div className="space-y-3 text-xs" data-testid="mandato-abierto-confirm">
          <p
            className="rounded-xl border px-3 py-2 leading-relaxed"
            style={{ borderColor: "#F9AC00", background: "rgba(249,172,0,0.08)", color: "#8a6000" }}
            role="alert"
          >
            <strong>{row.companyName}</strong> dejará de exigir firma de mandatario en este
            organismo.
          </p>
          {error ? (
            <p role="alert" className="text-[11px] text-[#FF4E00]">
              {error}
            </p>
          ) : null}
          <div className="flex justify-end gap-2 pt-1">
            <button
              type="button"
              onClick={() => setConfirming(false)}
              disabled={busy}
              className="rounded-xl border px-4 py-2 text-xs font-semibold disabled:opacity-50"
            >
              Cancelar
            </button>
            <button
              type="button"
              onClick={() => onSave(values)}
              disabled={busy}
              className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
              style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
            >
              Confirmar cambio
            </button>
          </div>
        </div>
      </Modal>
    );
  }

  return (
    <Modal
      open
      onClose={onCancel}
      busy={busy}
      icon={FileSignature}
      iconBg="#557EFF"
      title="Tipo de mandato"
      titleClassName="text-base font-bold text-[#162744]"
      size="md"
    >
      <div className="space-y-3 text-xs" data-testid="mandato-tipo-editor">
        <p>
          Compañía <strong>{row.companyName}</strong>
        </p>
        <label className="block space-y-1.5" htmlFor={`${uid}-tipo`}>
          <span className="font-semibold text-[#162244]">Tipo de mandato</span>
          <select
            id={`${uid}-tipo`}
            value={values.tipo}
            onChange={(e) => {
              set("tipo", e.target.value as MandatoTipoNegocio);
              setNameError(false);
            }}
            disabled={busy}
            className={FIELD}
            data-testid="mandato-tipo-select"
          >
            {MANDATO_TIPOS.map((t) => (
              <option key={t.value} value={t.value}>
                {tipoNegocioLabel(t.value)}
              </option>
            ))}
          </select>
        </label>

        {institucional ? (
          <div className="space-y-3" data-testid="mandato-tipo-entidad">
            <label className="block space-y-1.5" htmlFor={`${uid}-nombre`}>
              <span className="font-semibold text-[#162244]">Nombre de la entidad</span>
              <input
                id={`${uid}-nombre`}
                value={values.institutionalName}
                onChange={(e) => {
                  set("institutionalName", e.target.value);
                  setNameError(false);
                }}
                disabled={busy}
                required
                aria-required="true"
                aria-invalid={nameError || undefined}
                aria-describedby={nameError ? `${uid}-nombre-err` : undefined}
                className={FIELD}
              />
              {nameError ? (
                <span id={`${uid}-nombre-err`} role="alert" className="block text-[11px] text-[#FF4E00]">
                  El nombre de la entidad es obligatorio.
                </span>
              ) : null}
            </label>
            <label className="block space-y-1.5" htmlFor={`${uid}-nit`}>
              <span className="font-semibold text-[#162244]">NIT</span>
              <input
                id={`${uid}-nit`}
                value={values.institutionalNit}
                onChange={(e) => set("institutionalNit", e.target.value)}
                disabled={busy}
                className={`${FIELD} font-mono`}
              />
            </label>
            <div className="grid gap-3 sm:grid-cols-2">
              <label className="block space-y-1.5" htmlFor={`${uid}-ciudad`}>
                <span className="font-semibold text-[#162244]">Ciudad de cámara</span>
                <input
                  id={`${uid}-ciudad`}
                  value={values.chamberCity}
                  onChange={(e) => set("chamberCity", e.target.value)}
                  disabled={busy}
                  className={FIELD}
                />
              </label>
              <label className="block space-y-1.5" htmlFor={`${uid}-sigla`}>
                <span className="font-semibold text-[#162244]">Sigla</span>
                <input
                  id={`${uid}-sigla`}
                  value={values.sigla}
                  onChange={(e) => set("sigla", e.target.value)}
                  disabled={busy}
                  className={`${FIELD} font-mono`}
                />
              </label>
            </div>
          </div>
        ) : null}

        {error ? (
          <div role="alert" className="space-y-1 text-[11px] text-[#FF4E00]" data-testid="mandato-tipo-error">
            <p>{error}</p>
            {conflict ? (
              <button
                type="button"
                onClick={handleReload}
                disabled={busy}
                className="font-semibold underline"
              >
                Recargar el tipo actual
              </button>
            ) : null}
          </div>
        ) : null}

        <div className="flex justify-end gap-2 pt-1">
          <button
            type="button"
            onClick={onCancel}
            disabled={busy}
            className="rounded-xl border px-4 py-2 text-xs font-semibold disabled:opacity-50"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={handleSave}
            disabled={busy}
            className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            Guardar
          </button>
        </div>
      </div>
    </Modal>
  );
}

export interface CompanyVolverDefaultModalProps {
  companyName: string;
  busy: boolean;
  error: string | null;
  onConfirm: () => void;
  onCancel: () => void;
}

/** Confirmación de «Volver al default» de la regla de una compañía. */
export function CompanyVolverDefaultModal({
  companyName,
  busy,
  error,
  onConfirm,
  onCancel,
}: CompanyVolverDefaultModalProps) {
  return (
    <Modal
      open
      onClose={onCancel}
      busy={busy}
      icon={AlertTriangle}
      iconBg="#F9AC00"
      title="Volver al default"
      titleClassName="text-base font-bold text-[#162744]"
      size="md"
    >
      <div className="space-y-3 text-xs" data-testid="mandato-volver-default">
        <p>
          <strong>{companyName}</strong> dejará de tener regla propia y volverá a Persona natural
          (Default) en este organismo.
        </p>
        {error ? (
          <p role="alert" className="text-[11px] text-[#FF4E00]">
            {error}
          </p>
        ) : null}
        <div className="flex justify-end gap-2 pt-1">
          <button
            type="button"
            onClick={onCancel}
            disabled={busy}
            className="rounded-xl border px-4 py-2 text-xs font-semibold disabled:opacity-50"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={onConfirm}
            disabled={busy}
            className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            Volver al default
          </button>
        </div>
      </div>
    </Modal>
  );
}
