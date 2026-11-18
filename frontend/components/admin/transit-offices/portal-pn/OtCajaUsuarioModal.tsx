"use client";

// HU #14455 — modal normal para invitar a un usuario de caja (correo y nombre, ambos obligatorios).
import { useId, useState, type FormEvent } from "react";
import { Send, type LucideIcon } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { PnButton } from "@/components/admin/plataforma/persona-natural/pn-componentes";
import { PN_FIELD_ERROR_CLS, PN_INPUT_CLS, PN_INPUT_ERROR_CLS, PN_LABEL_CLS } from "@/components/admin/plataforma/persona-natural/pn-ui";
import type { CajaInvitacion } from "@/lib/api/admin-ot-portal-caja";
import { cajaErrorMessage } from "./cajaErrores";

export const CAJA_ERROR_NOMBRE = "Escribe el nombre de la persona.";
export const CAJA_ERROR_CORREO = "Escribe un correo válido.";

export interface OtCajaUsuarioModalProps {
  icon?: LucideIcon;
  onClose: () => void;
  onInvitar: (v: CajaInvitacion) => Promise<void>;
}

export function OtCajaUsuarioModal({ icon, onClose, onInvitar }: OtCajaUsuarioModalProps) {
  const id = useId();
  const [fullName, setFullName] = useState("");
  const [email, setEmail] = useState("");
  const [errNombre, setErrNombre] = useState<string | null>(null);
  const [errCorreo, setErrCorreo] = useState<string | null>(null);
  const [errServidor, setErrServidor] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function enviar(e: FormEvent) {
    e.preventDefault();
    const nombreOk = fullName.trim().length > 0;
    const correoOk = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim());
    setErrNombre(nombreOk ? null : CAJA_ERROR_NOMBRE);
    setErrCorreo(correoOk ? null : CAJA_ERROR_CORREO);
    setErrServidor(null);
    if (!nombreOk || !correoOk) return;
    setBusy(true);
    try {
      await onInvitar({ fullName, email });
    } catch (err) {
      setErrServidor(cajaErrorMessage(err));
      setBusy(false);
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      busy={busy}
      title="Crear usuario de caja"
      icon={icon}
      size="sm"
      footer={
        <div className="flex justify-end gap-2">
          <PnButton variant="secondary" onClick={onClose} disabled={busy}>
            Cancelar
          </PnButton>
          <PnButton variant="primary" icon={Send} type="submit" form={`${id}-form`} loading={busy}>
            Enviar invitación
          </PnButton>
        </div>
      }
    >
      <form id={`${id}-form`} onSubmit={(e) => void enviar(e)} noValidate className="flex flex-col gap-3">
        <label className={PN_LABEL_CLS} htmlFor={`${id}-nombre`}>
          Nombre
          <input
            id={`${id}-nombre`}
            className={`${PN_INPUT_CLS} ${errNombre ? PN_INPUT_ERROR_CLS : ""}`}
            value={fullName}
            onChange={(e) => setFullName(e.target.value)}
            aria-invalid={errNombre ? true : undefined}
            aria-describedby={errNombre ? `${id}-nombre-err` : undefined}
            autoComplete="off"
          />
          {errNombre && (
            <span id={`${id}-nombre-err`} role="alert" className={PN_FIELD_ERROR_CLS}>
              {errNombre}
            </span>
          )}
        </label>
        <label className={PN_LABEL_CLS} htmlFor={`${id}-correo`}>
          Correo
          <input
            id={`${id}-correo`}
            type="email"
            className={`${PN_INPUT_CLS} ${errCorreo ? PN_INPUT_ERROR_CLS : ""}`}
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            aria-invalid={errCorreo ? true : undefined}
            aria-describedby={errCorreo ? `${id}-correo-err` : undefined}
            autoComplete="off"
          />
          {errCorreo && (
            <span id={`${id}-correo-err`} role="alert" className={PN_FIELD_ERROR_CLS}>
              {errCorreo}
            </span>
          )}
        </label>
        {errServidor && <InlineAlert tone="error">{errServidor}</InlineAlert>}
      </form>
    </Modal>
  );
}
