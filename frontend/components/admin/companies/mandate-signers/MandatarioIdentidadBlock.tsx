"use client";

import { useState } from "react";
import {
  mandateSignerIdentityAction,
  type MandateSigner,
} from "@/lib/api/admin-mandate-signers";
import { formatDate } from "@/components/admin/companies/signature-vault/signatureVaultDisplay";

/**
 * Estado de la validación de identidad del mandatario y sus acciones, dentro del configurador de la
 * COMPAÑÍA.
 *
 * <p>Los métodos existían desde la HU #10911 pero colgaban solo de la ruta del organismo y ningún
 * componente los llamaba: la empresa registraba a su mandatario y no tenía forma de pedirle la
 * validación ni de vincular la que esa persona ya tuviera.</p>
 *
 * <p>Solo se ofrece al EDITAR: en el alta el mandatario aún no tiene id contra el que actuar. El envío
 * inicial ya lo dispara el propio alta cuando hay correo.</p>
 */
export function MandatarioIdentidadBlock({
  tenantId,
  signer,
  onRefresh,
}: {
  tenantId: string;
  signer: MandateSigner;
  onRefresh: () => void;
}) {
  const [running, setRunning] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const ejecutar = async (accion: "send" | "resend" | "link") => {
    setRunning(accion);
    setMsg(null);
    setError(null);
    try {
      const r = await mandateSignerIdentityAction(tenantId, signer.id, accion);
      setMsg(
        accion === "link"
          ? "Identidad vinculada."
          : r.reutilizada
            ? "Ya tenía una validación vigente: se reutilizó."
            : "Validación enviada al correo del mandatario.",
      );
      onRefresh();
    } catch {
      setError(
        accion === "link"
          ? "Esa persona no tiene una validación de identidad aprobada y vigente que vincular."
          : "No se pudo completar la acción. Inténtalo de nuevo.",
      );
    } finally {
      setRunning(null);
    }
  };

  const vigente = signer.identityStatus === "valid";
  const sinCorreo = !signer.email || signer.email.trim() === "";

  return (
    <div className="rounded-xl border p-3" data-testid="mandatario-identidad">
      <p className="mb-1 text-xs font-semibold">Validación de identidad</p>

      <p className="text-[11px]" style={{ color: vigente ? "#5B8A1F" : "#F9AC00" }}>
        {vigente
          ? `Vigente${signer.identityValidUntil ? ` hasta el ${formatDate(signer.identityValidUntil)}` : ""}`
          : signer.identityStatus === "pending"
            ? "Enviada: pendiente de que el mandatario la complete"
            : signer.identityStatus === "expired"
              ? "Vencida o rechazada: se puede volver a enviar"
              : "Sin validación de identidad"}
      </p>

      {/* Con firma del baúl o firma física no hace falta: se informa para que nadie la pida de más. */}
      {signer.signatureVaultId && !vigente && (
        <p className="mt-1 text-[11px] opacity-70">
          Tiene firma del baúl asociada, así que puede firmar sin validar identidad.
        </p>
      )}

      <div className="mt-2 flex flex-wrap gap-2">
        <Boton
          label={vigente ? "Reenviar" : "Enviar validación"}
          disabled={sinCorreo}
          loading={running === (vigente ? "resend" : "send")}
          onClick={() => void ejecutar(vigente ? "resend" : "send")}
        />
        <Boton
          label="Vincular existente"
          disabled={false}
          loading={running === "link"}
          onClick={() => void ejecutar("link")}
        />
      </div>

      {sinCorreo && (
        <p className="mt-1 text-[11px] opacity-70">
          Sin correo no se puede enviar la validación. Vincular una existente sí funciona.
        </p>
      )}
      {msg && (
        <p role="status" className="mt-1 text-[11px]" style={{ color: "#5B8A1F" }}>
          {msg}
        </p>
      )}
      {error && (
        <p role="alert" className="mt-1 text-[11px] font-medium" style={{ color: "#FF4E00" }}>
          {error}
        </p>
      )}
    </div>
  );
}

function Boton({
  label,
  disabled,
  loading,
  onClick,
}: {
  label: string;
  disabled: boolean;
  loading: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      className="rounded-lg border px-3 py-1.5 text-[11px] font-semibold disabled:opacity-60"
      style={{ color: "#557EFF", borderColor: "#557EFF" }}
      disabled={disabled || loading}
      onClick={onClick}
    >
      {loading ? "…" : label}
    </button>
  );
}
