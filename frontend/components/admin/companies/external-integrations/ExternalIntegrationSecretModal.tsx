"use client";

// HU #14068 AC2 — El secreto se muestra UNA sola vez (alta y rotación) en un modal con botón copiar. Solo vive en
// memoria mientras el modal está abierto: no se guarda en almacenamiento del navegador ni en logs, y al cerrar se
// descarta (el padre pone `null`).
import { useState } from "react";
import { Check, Copy, KeyRound } from "lucide-react";
import { Modal } from "@/components/atom/Modal";

export interface ExternalIntegrationSecretModalProps {
  clientId: string;
  secret: string;
  onClose: () => void;
}

export function ExternalIntegrationSecretModal({ clientId, secret, onClose }: ExternalIntegrationSecretModalProps) {
  const [copied, setCopied] = useState(false);

  async function copySecret() {
    await navigator.clipboard?.writeText(secret);
    setCopied(true);
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={`Credenciales de «${clientId}»`}
      icon={KeyRound}
      size="md"
      closeOnBackdrop={false}
      footer={
        <div className="flex justify-end">
          <button
            type="button"
            onClick={onClose}
            className="rounded-full px-5 py-2.5 text-sm font-semibold text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2"
            style={{ background: "linear-gradient(135deg, var(--color-flit-brand) 0%, var(--color-flit-tech) 100%)" }}
          >
            Ya lo copié, cerrar
          </button>
        </div>
      }
    >
      <div className="flex flex-col gap-3 text-sm text-flit-primary dark:text-white">
        <p
          role="alert"
          className="rounded-[10px] border px-3 py-2 text-xs font-semibold"
          style={{
            background: "var(--badge-warning-bg)",
            color: "var(--badge-warning-fg)",
            borderColor: "var(--badge-warning-border)",
          }}
        >
          Cópielo ahora: este secreto no se podrá ver de nuevo ni se puede recuperar. Si lo pierde tendrá que rotarlo.
          Entréguelo al servicio del portal por un canal seguro.
        </p>
        <div className="flex flex-col gap-1">
          <span className="text-xs font-semibold text-[#59677D] dark:text-white/70">Identificador (clientId)</span>
          <code className="break-all rounded-[10px] border border-[#DFE5ED] bg-[#EEF5FF] px-3 py-2 font-mono text-sm dark:border-white/15 dark:bg-white/5">
            {clientId}
          </code>
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-xs font-semibold text-[#59677D] dark:text-white/70">Secreto (clientSecret)</span>
          <code
            data-testid="external-integration-secret"
            className="break-all rounded-[10px] border border-[#DFE5ED] bg-[#EEF5FF] px-3 py-2 font-mono text-sm dark:border-white/15 dark:bg-white/5"
          >
            {secret}
          </code>
        </div>
        <button
          type="button"
          onClick={() => void copySecret()}
          className="inline-flex w-fit items-center gap-1.5 rounded-full border border-flit-brand px-4 py-2 text-xs font-semibold text-[color:var(--flit-brand-ink)] hover:bg-[#F4F8FF] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 dark:hover:bg-white/5"
        >
          {copied ? <Check className="h-4 w-4" aria-hidden="true" /> : <Copy className="h-4 w-4" aria-hidden="true" />}
          {copied ? "Copiado" : "Copiar secreto"}
        </button>
      </div>
    </Modal>
  );
}
