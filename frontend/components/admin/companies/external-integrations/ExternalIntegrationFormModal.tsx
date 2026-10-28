"use client";

// HU #14068 AC2 — Alta de la integración de Persona Natural de la compañía: modal normal (no lateral) con nombre y
// finalidad opcionales (la API pone valores por defecto). Si falla, conserva lo escrito y muestra el motivo.
import { useState } from "react";
import { PlugZap } from "lucide-react";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { PnButton } from "@/components/admin/plataforma/persona-natural/pn-componentes";
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
      title="Crear conexión de Persona Natural"
      description="Conecta el portal de Persona Natural con esta compañía. La clave secreta se genera al crear y se muestra una sola vez."
      icon={PlugZap}
      size="md"
      footer={
        <div className="flex justify-end gap-2">
          <PnButton variant="secondary" onClick={onClose} disabled={busy}>
            Cancelar
          </PnButton>
          <PnButton type="submit" form="external-integration-form" variant="primary" icon={PlugZap} loading={busy}>
            Crear conexión
          </PnButton>
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
            Opcional. Para qué se usa esta conexión (hasta {PURPOSE_MAX} caracteres).
          </span>
        </label>
        <InlineAlert tone="info">
          Esta conexión solo puede consultar la información del portal: no puede modificar nada en FLIT.
        </InlineAlert>
        {error && <InlineAlert tone="error">{error}</InlineAlert>}
      </form>
    </Modal>
  );
}
