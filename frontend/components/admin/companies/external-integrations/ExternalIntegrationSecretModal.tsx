"use client";

// HU #14068 AC2 — El secreto se muestra UNA sola vez (alta y rotación) en un modal con botón copiar. Solo vive en
// memoria mientras el modal está abierto: no se guarda en almacenamiento del navegador ni en logs, y al cerrar se
// descarta (el padre pone `null`).
import { useState } from "react";
import { Check, Copy, KeyRound } from "lucide-react";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { PnButton } from "@/components/admin/plataforma/persona-natural/pn-componentes";
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
          <PnButton variant="primary" icon={Check} onClick={onClose}>
            Ya la copié, cerrar
          </PnButton>
        </div>
      }
    >
      <div className="flex flex-col gap-3 text-sm text-flit-primary dark:text-white">
        <InlineAlert tone="warning">
          Cópiala ahora: esta clave no se podrá ver de nuevo ni se puede recuperar. Si la pierdes tendrás que generar
          una nueva. Entrégala al servicio del portal por un canal seguro.
        </InlineAlert>
        <div className="flex flex-col gap-1">
          <span className="text-xs font-semibold text-[#59677D] dark:text-white/70">Identificador de la conexión</span>
          <code className="break-all rounded-[10px] border border-[#DFE5ED] bg-[#EEF5FF] px-3 py-2 font-mono text-sm dark:border-white/15 dark:bg-white/5">
            {clientId}
          </code>
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-xs font-semibold text-[#59677D] dark:text-white/70">Clave secreta</span>
          <code
            data-testid="external-integration-secret"
            className="break-all rounded-[10px] border border-[#DFE5ED] bg-[#EEF5FF] px-3 py-2 font-mono text-sm dark:border-white/15 dark:bg-white/5"
          >
            {secret}
          </code>
        </div>
        <div>
          <PnButton variant="secondary" icon={copied ? Check : Copy} onClick={() => void copySecret()}>
            {copied ? "Copiada" : "Copiar clave"}
          </PnButton>
        </div>
      </div>
    </Modal>
  );
}
