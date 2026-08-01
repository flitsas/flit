"use client";

// HU #11180 — AC4, AC5, AC6: bloque de identidad con estado y acciones de validación.
// Muestra estado actual, vigencia y ofrece botones: Enviar / Reenviar / Asociar validación.
// Los 4 estados de UI: vacío (sin representante), cargando (acción en curso), error y lleno.

import { useState } from "react";
import { Loader2 } from "lucide-react";
import { ApiError } from "@/lib/api/types";
import {
  sendLegalRepresentativeIdentity,
  resendLegalRepresentativeIdentity,
  linkLegalRepresentativeIdentity,
} from "@/lib/api/admin-legal-representatives";
import { identityUi, vigenciaLabel } from "@/lib/admin/identity-vigencia";
import { formatFecha } from "@/lib/format/date";

export interface IdentityActionsBlockProps {
  tenantId: string;
  /** null en modo create: las acciones no están disponibles hasta guardar. */
  representativeId: string | null;
  identityStatus?: string | null;
  identityValidUntil?: string | null;
  firmaBaulVigente?: boolean;
  firmaBaulVigenteHasta?: string | null;
  /** Correo del representante — necesario para la validación. */
  email?: string | null;
  /** Callback para refrescar el detalle tras una acción exitosa. */
  onRefresh: () => void;
}

type ActionResult = { type: "success"; message: string } | { type: "error"; message: string };

/**
 * Bloque de validación de identidad (HU #11180).
 *
 * - AC4: en modo create (representativeId=null) el bloque informa que la identidad se resolverá
 *   automáticamente al guardar.
 * - AC5: botones Enviar / Reenviar según el estado actual; tras la acción muestra confirmación y
 *   llama a onRefresh() para que el panel actualice el estado.
 * - AC6: botón «Asociar validación» disponible cuando el representante está persistido; llama a
 *   POST .../identity/link y tras éxito refresca el panel.
 */
export function IdentityActionsBlock({
  tenantId,
  representativeId,
  identityStatus,
  identityValidUntil,
  firmaBaulVigente,
  firmaBaulVigenteHasta,
  email,
  onRefresh,
}: IdentityActionsBlockProps) {
  const [actionLoading, setActionLoading] = useState<string | null>(null);
  const [result, setResult] = useState<ActionResult | null>(null);

  const idUi = identityUi(identityStatus);
  const vigLabel = vigenciaLabel(identityStatus, identityValidUntil);
  const isValid = identityStatus === "valid";
  const hasPrior = identityStatus !== "none" && identityStatus !== null && identityStatus !== undefined;

  async function runAction(key: string, fn: () => Promise<unknown>) {
    setActionLoading(key);
    setResult(null);
    try {
      await fn();
      let message = "Acción realizada con éxito.";
      if (key === "send") message = "Solicitud de validación enviada. El representante recibirá un correo.";
      if (key === "resend") message = "Solicitud de validación reenviada. El representante recibirá un correo.";
      if (key === "link") message = "Validación de identidad asociada correctamente.";
      setResult({ type: "success", message });
      onRefresh();
    } catch (err) {
      let message = "Ocurrió un error. Intenta de nuevo.";
      if (err instanceof ApiError) {
        if (err.status === 409) {
          message = "No hay una validación de identidad aprobada y vigente para asociar.";
        } else if (err.status === 422) {
          message = "El representante no tiene correo registrado. Agrega el correo y vuelve a intentarlo.";
        }
      }
      setResult({ type: "error", message });
    } finally {
      setActionLoading(null);
    }
  }

  // AC4 — modo create: sin representante aún
  if (!representativeId) {
    return (
      <div
        className="rounded-xl border border-[#DFE5ED] p-3"
        data-testid="rl-identity-block"
        aria-label="Validación de identidad"
      >
        <p className="text-[11px] font-bold uppercase tracking-wide opacity-60">
          Validación de identidad
        </p>
        <p className="mt-2 text-[11px] opacity-60" data-testid="rl-identity-create-note">
          Al guardar el representante, la identidad se asociará automáticamente si la persona ya
          tiene una validación aprobada y vigente.
        </p>
      </div>
    );
  }

  return (
    <div
      className="rounded-xl border border-[#DFE5ED] p-3 space-y-2"
      data-testid="rl-identity-block"
      aria-label="Validación de identidad"
    >
      <p className="text-[11px] font-bold uppercase tracking-wide opacity-60">
        Validación de identidad
      </p>

      {/* Estado actual */}
      <div className="flex flex-wrap items-center gap-2">
        <span
          className="inline-flex items-center rounded-full px-2.5 py-1 text-[10px] font-semibold"
          style={idUi.style}
          data-testid="rl-identity-status-badge"
        >
          {idUi.label}
        </span>
        {vigLabel && (
          <span className="text-[10px] opacity-60" data-testid="rl-identity-vigencia">
            {vigLabel}
          </span>
        )}
        {firmaBaulVigente && firmaBaulVigenteHasta && (
          <span className="text-[10px] opacity-50">
            · Firma vigente hasta {formatFecha(firmaBaulVigenteHasta)}
          </span>
        )}
      </div>

      {/* Feedback de acciones */}
      {result && (
        <p
          role="alert"
          className="text-[11px] font-medium"
          style={{ color: result.type === "success" ? "#3f7a15" : "#FF4E00" }}
          data-testid={result.type === "success" ? "rl-identity-success" : "rl-identity-error"}
        >
          {result.message}
        </p>
      )}

      {/* Acciones — AC5 y AC6 */}
      <div className="flex flex-wrap gap-2">
        {/* Enviar (primera vez) — solo cuando no hay identidad previa */}
        {!hasPrior && (
          <ActionButton
            label="Enviar validación"
            loading={actionLoading === "send"}
            disabled={!!actionLoading}
            testId="rl-identity-send"
            onClick={() =>
              runAction("send", () =>
                sendLegalRepresentativeIdentity(tenantId, representativeId),
              )
            }
          />
        )}

        {/* Reenviar / Renovar — cuando hay identidad previa y no está vigente */}
        {hasPrior && !isValid && (
          <ActionButton
            label={identityStatus === "pending" ? "Reenviar validación" : "Renovar validación"}
            loading={actionLoading === "resend"}
            disabled={!!actionLoading}
            testId="rl-identity-resend"
            onClick={() =>
              runAction("resend", () =>
                resendLegalRepresentativeIdentity(tenantId, representativeId),
              )
            }
          />
        )}

        {/* Reenviar incluso si está vigente (siempre disponible en view/edit para representante persistido) */}
        {isValid && (
          <ActionButton
            label="Reenviar validación"
            loading={actionLoading === "resend"}
            disabled={!!actionLoading}
            testId="rl-identity-resend"
            onClick={() =>
              runAction("resend", () =>
                resendLegalRepresentativeIdentity(tenantId, representativeId),
              )
            }
          />
        )}

        {/* Asociar validación — AC6: disponible cuando no está válida (link de identidad aprobada) */}
        {!isValid && (
          <ActionButton
            label="Asociar validación de identidad"
            loading={actionLoading === "link"}
            disabled={!!actionLoading}
            testId="rl-identity-link"
            variant="secondary"
            onClick={() =>
              runAction("link", () =>
                linkLegalRepresentativeIdentity(tenantId, representativeId),
              )
            }
          />
        )}
      </div>
    </div>
  );
}

// ── Componente auxiliar de botón de acción ───────────────────────────────────

interface ActionButtonProps {
  label: string;
  loading: boolean;
  disabled: boolean;
  testId: string;
  variant?: "primary" | "secondary";
  onClick: () => void;
}

function ActionButton({ label, loading, disabled, testId, variant = "primary", onClick }: ActionButtonProps) {
  const isPrimary = variant === "primary";
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      data-testid={testId}
      aria-label={label}
      className="inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-[11px] font-semibold disabled:opacity-50"
      style={
        isPrimary
          ? { background: "#557EFF", color: "#fff" }
          : { background: "transparent", color: "#557EFF", border: "1px solid #557EFF" }
      }
    >
      {loading && <Loader2 className="h-3 w-3 animate-spin" aria-hidden="true" />}
      {label}
    </button>
  );
}
