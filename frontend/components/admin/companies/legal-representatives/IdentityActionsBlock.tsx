"use client";

// HU #11755 — el módulo Identidad (`tramites.procedure_instance_biometric_validations`) es la única
// fuente de verdad del estado de identidad (ADR-0050). Este bloque deja de ser un panel de acciones y
// pasa a ser SOLO CONSULTA: ya no ofrece Enviar / Reenviar / Renovar / Asociar validación. Esos tres
// controles vivían aquí (HU #11180) y disparaban `POST .../identity/{send,resend,link}`; las rutas
// correspondientes se retiran a `410 Gone` en la HU #11758.
// HU #11756 — matriz de rótulos + copy por estado (CF-03/CF-04): los dos rótulos ("Identidad: ..." y
// "Firma del baúl: ...") se muestran SIEMPRE, sin fusionarse en una sola cadena que confunda las dos
// fuentes, y el copy de invitación al módulo Identidad respeta la precedencia D8 del ADR-0025
// (baúl > identidad).
// Estados de UI: solo "lleno" (bloque con rótulos) y el caso especial de modo create (sin representante
// persistido) — no hay estado de carga ni de error porque no hay ninguna petición de escritura ni acción
// disparable desde aquí.

import {
  identidadRotulo,
  firmaBaulRotulo,
  identityCopy,
  identityUi,
  IDENTITY_MODULE_HREF,
} from "@/lib/admin/identity-vigencia";
import { RL_COLOR } from "./rl-flit-styles";

export interface IdentityActionsBlockProps {
  /** null en modo create: aún no hay representante persistido contra el que consultar identidad. */
  representativeId: string | null;
  identityStatus?: string | null;
  identityValidUntil?: string | null;
  firmaBaulVigente?: boolean;
  firmaBaulVigenteHasta?: string | null;
  /** Tipo de documento de la persona; "NIT" ⇒ persona jurídica, sin prevalidación (ADR-0036). */
  documentType?: string | null;
}

/**
 * Bloque de consulta de identidad (HU #11755/#11756, ADR-0050).
 *
 * - En modo create (`representativeId=null`) informa que la identidad se resolverá automáticamente al
 *   guardar, si la persona ya tiene una validación aprobada y vigente en el módulo Identidad.
 * - En modo view/edit muestra SIEMPRE los dos rótulos (identidad + firma del baúl) y, cuando aplica,
 *   el copy que invita a ir al módulo Identidad — sin ningún control de escritura.
 */
export function IdentityActionsBlock({
  representativeId,
  identityStatus,
  firmaBaulVigente,
  firmaBaulVigenteHasta,
  documentType,
}: IdentityActionsBlockProps) {
  const idUi = identityUi(identityStatus);
  const copy = identityCopy({ identityStatus, firmaBaulVigente, documentType });

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

      {/* Los dos rótulos SIEMPRE, sin fusionarse en una sola cadena (HU #11756, CF-04) */}
      <div className="flex flex-wrap items-center gap-2">
        <span
          className="inline-flex items-center rounded-full px-2.5 py-1 text-[10px] font-semibold"
          style={idUi.style}
          data-testid="rl-identity-status-badge"
        >
          {identidadRotulo(identityStatus)}
        </span>
        <span
          className="inline-flex items-center rounded-full px-2.5 py-1 text-[10px] font-semibold"
          style={
            firmaBaulVigente
              ? { background: RL_COLOR.successBg, color: RL_COLOR.successText }
              : { background: RL_COLOR.tableHeader, color: RL_COLOR.muted }
          }
          data-testid="rl-identity-firma-baul-badge"
        >
          {firmaBaulRotulo(firmaBaulVigente, firmaBaulVigenteHasta)}
        </span>
      </div>

      {/* Copy por estado (CF-03): invita al módulo Identidad solo cuando aplica (D8 ADR-0025 manda) */}
      {copy.message && (
        <p className="text-[11px] opacity-70" data-testid="rl-identity-copy">
          {copy.message}
          {copy.showLink && (
            <>
              {" "}
              <a
                href={IDENTITY_MODULE_HREF}
                className="font-semibold underline"
                style={{ color: RL_COLOR.brand }}
                data-testid="rl-identity-module-link"
              >
                Ir al módulo Identidad
              </a>
            </>
          )}
        </p>
      )}
    </div>
  );
}
