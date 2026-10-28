"use client";

// HU #14068 AC2 — Alta de la integración de Persona Natural de la compañía: modal normal (no lateral) con nombre y
// finalidad opcionales (la API pone valores por defecto). Si falla, conserva lo escrito y muestra el motivo.
import { useState } from "react";
import { Loader2, PlugZap } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import type { NewExternalIntegration } from "@/lib/api/external-integrations";
import { externalIntegrationErrorMessage } from "./externalIntegrationErrors";

const INPUT_CLS =
  "h-11 w-full rounded-[10px] border border-[#DFE5ED] bg-white px-3 text-sm text-flit-primary " +
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 " +
  "dark:border-white/15 dark:bg-[#162744] dark:text-white";
const LABEL_CLS = "flex flex-col gap-1.5 text-sm font-semibold text-flit-primary dark:text-white";
const HINT_CLS = "text-xs font-normal text-[#59677D] dark:text-white/70";
const DISPLAY_NAME_MAX = 120;
const PURPOSE_MAX = 300;

export interface ExternalIntegrationFormModalProps {
  onClose: () => void;
  onCreate: (body: NewExternalIntegration) => Promise<void>;
}

export function ExternalIntegrationFormModal({ onClose, onCreate }: ExternalIntegrationFormModalProps) {
  const [displayName, setDisplayName] = useState("");
  const [purpose, setPurpose] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (displayName.trim().length > DISPLAY_NAME_MAX || purpose.trim().length > PURPOSE_MAX) {
      setError(`El nombre admite hasta ${DISPLAY_NAME_MAX} caracteres y la finalidad hasta ${PURPOSE_MAX}.`);
      return;
    }
    setBusy(true);
    try {
      await onCreate({
        tipo: "persona_natural",
        ...(displayName.trim() ? { displayName: displayName.trim() } : {}),
        ...(purpose.trim() ? { purpose: purpose.trim() } : {}),
      });
    } catch (err) {
      setError(externalIntegrationErrorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      busy={busy}
      title="Crear integración Persona Natural"
      description="Conecta el portal de Persona Natural con esta compañía. El secreto se genera al crear y se muestra una sola vez."
      icon={PlugZap}
      size="md"
      footer={
        <div className="flex justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            disabled={busy}
            className="rounded-full border border-[#DFE5ED] px-5 py-2.5 text-sm font-semibold text-flit-primary hover:bg-[#F4F8FF] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 disabled:opacity-70 dark:border-white/15 dark:text-white dark:hover:bg-white/5"
          >
            Cancelar
          </button>
          <button
            type="submit"
            form="external-integration-form"
            disabled={busy}
            className="inline-flex items-center gap-2 rounded-full px-5 py-2.5 text-sm font-semibold text-white shadow-[0_10px_22px_rgba(79,116,201,0.22)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 disabled:opacity-70"
            style={{ background: "linear-gradient(135deg, var(--color-flit-brand) 0%, var(--color-flit-tech) 100%)" }}
          >
            {busy && <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />}
            Crear integración
          </button>
        </div>
      }
    >
      <form id="external-integration-form" onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
        <label className={LABEL_CLS}>
          Nombre
          <input
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            maxLength={DISPLAY_NAME_MAX}
            autoComplete="off"
            className={INPUT_CLS}
            aria-describedby="external-integration-name-hint"
          />
          <span id="external-integration-name-hint" className={HINT_CLS}>
            Opcional. Si lo deja vacío se usa un nombre por defecto.
          </span>
        </label>
        <label className={LABEL_CLS}>
          Finalidad
          <textarea
            value={purpose}
            onChange={(e) => setPurpose(e.target.value)}
            maxLength={PURPOSE_MAX}
            rows={3}
            className={`${INPUT_CLS} h-auto py-2`}
            aria-describedby="external-integration-purpose-hint"
          />
          <span id="external-integration-purpose-hint" className={HINT_CLS}>
            Opcional. Para qué se usa esta integración (hasta {PURPOSE_MAX} caracteres).
          </span>
        </label>
        <p className="rounded-[10px] border border-[#DFE5ED] bg-[#EEF5FF] px-3 py-2 text-xs text-flit-primary dark:border-white/15 dark:bg-white/5 dark:text-white">
          Permiso fijo: <span className="font-mono">platform.tramites.persona_natural</span> (solo lectura de la
          fachada del portal).
        </p>
        {error && (
          <p role="alert" className="text-sm font-semibold text-[#C2410C]">
            {error}
          </p>
        )}
      </form>
    </Modal>
  );
}
