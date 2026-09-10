"use client";

// HU #11757 (ADR-0050) — el mandatario adopta la misma regla que la ficha del representante legal
// (HU #11755/#11756): el bloque de identidad pasa a SOLO CONSULTA. Ya no ofrece Enviar / Reenviar /
// Vincular — esos 3 controles llamaban a `mandateSignerIdentityAction(tenantId, id, accion)`, que
// también responderá 410 Gone (HU #11758). Reutiliza el MISMO módulo de copy que la ficha del RL
// (`lib/admin/identity-vigencia.ts`) para no duplicar la lógica de precedencia D8 (baúl > identidad)
// ni el copy por estado, incluido el caso NIT (aquí especialmente real: `CompanyMandatarioForm`
// admite documentType="NIT").
//
// Nota de datos: `MandateSigner` no expone una fecha "vigente hasta" propia para la firma del baúl
// (solo `signatureVaultId`, presencia/ausencia). Por eso `firmaBaulVigente` se deriva de
// `Boolean(signer.signatureVaultId)` y el rótulo no puede prometer una fecha — ver informe de la HU.

import {
  identidadRotulo,
  firmaBaulRotulo,
  identityCopy,
  IDENTITY_MODULE_HREF,
} from "@/lib/admin/identity-vigencia";
import type { MandateSigner } from "@/lib/api/admin-mandate-signers";

export function MandatarioIdentidadBlock({
  signer,
}: {
  signer: MandateSigner;
}) {
  const firmaBaulVigente = Boolean(signer.signatureVaultId);
  const copy = identityCopy({
    identityStatus: signer.identityStatus,
    firmaBaulVigente,
    documentType: signer.documentType,
  });

  return (
    <div className="rounded-xl border p-3" data-testid="mandatario-identidad">
      <p className="mb-1 text-xs font-semibold">Validación de identidad</p>

      {/* Los dos rótulos SIEMPRE, sin fusionarse en una sola cadena (HU #11756, CF-04) */}
      <div className="flex flex-wrap items-center gap-2">
        <span
          className="text-[11px] font-medium"
          style={{ color: "#3559c7" }}
          data-testid="mandatario-identidad-rotulo"
        >
          {identidadRotulo(signer.identityStatus)}
        </span>
        <span
          className="text-[11px] font-medium"
          style={{ color: firmaBaulVigente ? "#5B8A1F" : "#7D8798" }}
          data-testid="mandatario-firma-baul-rotulo"
        >
          {/* Sin fecha "hasta" disponible en MandateSigner: ver nota de datos arriba. */}
          {firmaBaulRotulo(firmaBaulVigente, null)}
        </span>
      </div>

      {/* Copy por estado (CF-03): invita al módulo Identidad solo cuando aplica (D8 ADR-0025 manda) */}
      {copy.message && (
        <p className="mt-1 text-[11px] opacity-70" data-testid="mandatario-identidad-copy">
          {copy.message}
          {copy.showLink && (
            <>
              {" "}
              <a
                href={IDENTITY_MODULE_HREF}
                className="font-semibold underline"
                style={{ color: "#557EFF" }}
                data-testid="mandatario-identidad-module-link"
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
