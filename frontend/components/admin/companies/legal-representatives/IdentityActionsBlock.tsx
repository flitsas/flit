"use client";

// HU #11755 — el módulo Identidad (`tramites.procedure_instance_biometric_validations`) es la única
// fuente de verdad del estado de identidad (ADR-0050). Este bloque deja de ser un panel de acciones y
// pasa a ser SOLO CONSULTA: ya no ofrece Enviar / Reenviar / Renovar / Asociar validación. Esos tres
// controles vivían aquí (HU #11180) y disparaban `POST .../identity/{send,resend,link}`; las rutas
// correspondientes se retiran a `410 Gone` en la HU #11758.
// Estados de UI: solo "lleno" (bloque con badges) y el caso especial de modo create (sin representante
// persistido) — no hay estado de carga ni de error porque no hay ninguna petición de escritura ni acción
// disparable desde aquí.

import { identityUi, vigenciaLabel } from "@/lib/admin/identity-vigencia";
import { formatFecha } from "@/lib/format/date";
import { RL_COLOR } from "./rl-flit-styles";

export interface IdentityActionsBlockProps {
  /** null en modo create: aún no hay representante persistido contra el que consultar identidad. */
  representativeId: string | null;
  identityStatus?: string | null;
  identityValidUntil?: string | null;
  firmaBaulVigente?: boolean;
  firmaBaulVigenteHasta?: string | null;
}

/**
 * Bloque de consulta de identidad (HU #11755, ADR-0050).
 *
 * - En modo create (`representativeId=null`) informa que la identidad se resolverá automáticamente al
 *   guardar, si la persona ya tiene una validación aprobada y vigente en el módulo Identidad.
 * - En modo view/edit muestra el estado y la vigencia actuales, sin ningún control de escritura.
 */
export function IdentityActionsBlock({
  representativeId,
  identityStatus,
  identityValidUntil,
  firmaBaulVigente,
  firmaBaulVigenteHasta,
}: IdentityActionsBlockProps) {
  const idUi = identityUi(identityStatus);
  const vigLabel = vigenciaLabel(identityStatus, identityValidUntil);

  // Modo create: sin representante aún.
  if (!representativeId) {
    return (
      <div
        className="rounded-xl border p-3"
        style={{ borderColor: RL_COLOR.border }}
        data-testid="rl-identity-block"
        aria-label="Validación de identidad"
      >
        <p className="text-[11px] font-bold uppercase tracking-wide opacity-60">
          Validación de identidad
        </p>
        <p className="mt-2 text-[11px] opacity-60" data-testid="rl-identity-create-note">
          Al guardar el representante, la identidad se asociará automáticamente si la persona ya
          tiene una validación aprobada y vigente en el módulo Identidad.
        </p>
      </div>
    );
  }

  return (
    <div
      className="rounded-xl border p-3 space-y-2"
      style={{ borderColor: RL_COLOR.border }}
      data-testid="rl-identity-block"
      aria-label="Validación de identidad"
    >
      <p className="text-[11px] font-bold uppercase tracking-wide opacity-60">
        Validación de identidad
      </p>

      {/* Estado actual — solo consulta, el módulo Identidad es la única fuente de verdad (ADR-0050) */}
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
    </div>
  );
}
