import { StatusBadge } from "@/components/atom/StatusBadge";
import { iconoDeTono } from "@/components/admin/companies/ConfigUi";
import type { MandateSigner } from "@/lib/api/admin-mandate-signers";
import { formatFechaCalendario } from "@/lib/format/date";
import { presentarVigencia } from "@/lib/plataforma/mandatario-vigencia";

type Props = Pick<MandateSigner, "signerModel" | "validityStatus" | "isActive" | "validityKind" | "validTo">;

/**
 * HU #13133 — etiqueta de vigencia: texto + ícono + color (verde vigente, naranja por vencer, rojo
 * vencido e inactivo). Sin vigencia (Persona jurídica / Formato en blanco) muestra un guion.
 */
export function MandatarioVigenciaBadge({ signer }: { signer: Props }) {
  const vista = presentarVigencia(signer);

  if (vista.estado === "sin_vigencia" || vista.estado === "desconocido") {
    return (
      <span data-testid="mandatario-vigencia" data-estado={vista.estado}>
        <span aria-hidden="true">—</span>
        <span className="sr-only">{vista.texto}</span>
      </span>
    );
  }

  const hasta =
    signer.validityKind === "range" && signer.validTo ? formatFechaCalendario(signer.validTo) : null;

  return (
    <span className="inline-flex flex-col items-start gap-0.5" data-testid="mandatario-vigencia" data-estado={vista.estado}>
      <StatusBadge
        tone={vista.tone}
        icono={iconoDeTono(vista.tone)}
        ariaLabel={`Vigencia: ${vista.texto}`}
        label={vista.texto}
      />
      {hasta ? <span className="text-xs text-muted-foreground">Hasta {hasta}</span> : null}
    </span>
  );
}
