"use client";

// HU #13200 — Alta y edición de un cliente de integración externo (AC2, AC3, AC5). En el alta se pide
// el identificador (no editable después: la bitácora lo referencia y no se reutiliza); en la edición solo
// nombre, finalidad y permisos. El permiso de lectura de trámites es obligatorio; el de datos personales es
// opcional y define si el cliente recibe los compradores sin enmascarar.
// Feature #13261: permiso opcional de envío de adjuntos (comprobante de impuesto). El PATCH reemplaza la lista
// completa, así que al editar se conservan los permisos que este formulario no administra.
import { useState } from "react";
import { Loader2, PlugZap } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import {
  EXTERNAL_CLIENT_ID_PATTERN,
  EXTERNAL_SCOPE_ATTACHMENTS,
  EXTERNAL_SCOPE_PII,
  EXTERNAL_SCOPE_READ,
  type ExternalClient,
  type ExternalClientChanges,
  type NewExternalClient,
} from "@/lib/api/external-clients";
import { externalClientErrorMessage } from "./externalClientErrors";

const INPUT_CLS =
  "h-11 w-full rounded-[10px] border border-[#DFE5ED] bg-white px-3 text-sm text-[#162744] " +
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 " +
  "disabled:bg-[#EEF5FF] dark:border-white/15 dark:bg-[#162744] dark:text-white";
const LABEL_CLS = "flex flex-col gap-1.5 text-sm font-semibold text-[#162744] dark:text-white";
const HINT_CLS = "text-xs font-normal text-[#59677D] dark:text-white/70";
const OPTIONAL_SCOPE_CLS =
  "flex cursor-pointer items-start gap-3 rounded-[10px] border border-[#DFE5ED] bg-white p-3 text-sm text-[#162744] " +
  "focus-within:ring-2 focus-within:ring-[#557EFF] focus-within:ring-offset-2 dark:border-white/15 dark:bg-[#162744] dark:text-white";
const MANAGED_SCOPES = [EXTERNAL_SCOPE_READ, EXTERNAL_SCOPE_PII, EXTERNAL_SCOPE_ATTACHMENTS];

export interface ExternalClientFormModalProps {
  /** Sin `client`: alta. Con `client`: edición de nombre, finalidad y permisos. */
  client?: ExternalClient;
  onClose: () => void;
  onCreate: (body: NewExternalClient) => Promise<void>;
  onUpdate: (id: string, body: ExternalClientChanges) => Promise<void>;
}

export function ExternalClientFormModal({ client, onClose, onCreate, onUpdate }: ExternalClientFormModalProps) {
  const editing = client !== undefined;
  const [clientId, setClientId] = useState(client?.clientId ?? "");
  const [displayName, setDisplayName] = useState(client?.displayName ?? "");
  const [purpose, setPurpose] = useState(client?.purpose ?? "");
  const [pii, setPii] = useState(client?.scopes.includes(EXTERNAL_SCOPE_PII) ?? false);
  const [attachments, setAttachments] = useState(client?.scopes.includes(EXTERNAL_SCOPE_ATTACHMENTS) ?? false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!editing && !EXTERNAL_CLIENT_ID_PATTERN.test(clientId.trim())) {
      setError("El identificador solo admite minúsculas, dígitos y guiones, de 3 a 64 caracteres (p. ej. flito-pdn).");
      return;
    }
    if (!displayName.trim() || !purpose.trim()) {
      setError("El nombre y la finalidad son obligatorios.");
      return;
    }

    const scopes = [
      EXTERNAL_SCOPE_READ,
      ...(pii ? [EXTERNAL_SCOPE_PII] : []),
      ...(attachments ? [EXTERNAL_SCOPE_ATTACHMENTS] : []),
      ...(client?.scopes.filter((s) => !MANAGED_SCOPES.includes(s)) ?? []),
    ];
    setBusy(true);
    try {
      if (editing) {
        await onUpdate(client.id, { displayName: displayName.trim(), purpose: purpose.trim(), scopes });
      } else {
        await onCreate({ clientId: clientId.trim(), displayName: displayName.trim(), purpose: purpose.trim(), scopes });
      }
    } catch (err) {
      // AC3: el formulario conserva lo escrito; solo se muestra el motivo.
      setError(externalClientErrorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      busy={busy}
      title={editing ? `Editar «${client.clientId}»` : "Nuevo cliente de integración"}
      description={
        editing
          ? "El identificador no se puede cambiar."
          : "Sistema externo que lee el feed de trámites de todas las compañías (p. ej. Flito). El secreto se genera al crear y se muestra una sola vez."
      }
      icon={PlugZap}
      size="md"
      footer={
        <div className="flex justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            disabled={busy}
            className="rounded-full border border-[#DFE5ED] px-5 py-2.5 text-sm font-semibold text-[#162744] hover:bg-[#F4F8FF] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:opacity-70 dark:border-white/15 dark:text-white dark:hover:bg-white/5"
          >
            Cancelar
          </button>
          <button
            type="submit"
            form="external-client-form"
            disabled={busy}
            className="inline-flex items-center gap-2 rounded-full px-5 py-2.5 text-sm font-semibold text-white shadow-[0_10px_22px_rgba(79,116,201,0.22)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:opacity-70"
            style={{ background: "linear-gradient(135deg, #557EFF 0%, #00DBD5 100%)" }}
          >
            {busy && <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />}
            {editing ? "Guardar cambios" : "Crear cliente"}
          </button>
        </div>
      }
    >
      <form id="external-client-form" onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
        <label className={LABEL_CLS}>
          Identificador
          <input
            value={clientId}
            onChange={(e) => setClientId(e.target.value.toLowerCase())}
            disabled={editing}
            maxLength={64}
            autoComplete="off"
            spellCheck={false}
            className={`${INPUT_CLS} font-mono`}
            aria-describedby="external-client-id-hint"
          />
          <span id="external-client-id-hint" className={HINT_CLS}>
            Uno por ambiente: flito-dev, flito-qa, flito-pdn. No se reutiliza nunca.
          </span>
        </label>
        <label className={LABEL_CLS}>
          Nombre
          <input
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            maxLength={120}
            className={INPUT_CLS}
          />
        </label>
        <label className={LABEL_CLS}>
          Finalidad
          <textarea
            value={purpose}
            onChange={(e) => setPurpose(e.target.value)}
            maxLength={300}
            rows={2}
            className={`${INPUT_CLS} h-auto py-2`}
            aria-describedby="external-client-purpose-hint"
          />
          <span id="external-client-purpose-hint" className={HINT_CLS}>
            Para qué usará los datos (Ley 1581).
          </span>
        </label>

        <fieldset className="flex flex-col gap-2">
          <legend className="mb-1 text-sm font-semibold text-[#162744] dark:text-white">Permisos</legend>
          <label className="flex items-start gap-3 rounded-[10px] border border-[#DFE5ED] bg-[#EEF5FF] p-3 text-sm text-[#162744] dark:border-white/15 dark:bg-white/5 dark:text-white">
            <input type="checkbox" checked disabled className="mt-0.5 h-4 w-4 accent-[#557EFF]" />
            <span>
              <span className="font-semibold">Lectura de trámites</span>
              <span className={`block ${HINT_CLS}`}>Obligatorio: feed de sincronización y URL de la factura.</span>
            </span>
          </label>
          <label className={OPTIONAL_SCOPE_CLS}>
            <input
              type="checkbox"
              checked={pii}
              onChange={(e) => setPii(e.target.checked)}
              className="mt-0.5 h-4 w-4 accent-[#557EFF] focus-visible:outline-none"
            />
            <span>
              <span className="font-semibold">Datos personales sin enmascarar</span>
              <span className={`block ${HINT_CLS}`}>
                Sin este permiso, documento, nombre, dirección, celular y correo de los compradores llegan enmascarados.
              </span>
            </span>
          </label>
          <label className={OPTIONAL_SCOPE_CLS}>
            <input
              type="checkbox"
              checked={attachments}
              onChange={(e) => setAttachments(e.target.checked)}
              className="mt-0.5 h-4 w-4 accent-[#557EFF] focus-visible:outline-none"
            />
            <span>
              <span className="font-semibold">Envío de adjuntos</span>
              <span className={`block ${HINT_CLS}`}>
                Permite cargar el comprobante de pago del impuesto en los trámites (solo liquidación de impuesto).
              </span>
            </span>
          </label>
        </fieldset>

        {error && (
          <p role="alert" className="text-sm font-semibold text-[#C2410C]">
            {error}
          </p>
        )}
      </form>
    </Modal>
  );
}
