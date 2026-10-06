import { CalendarClock, CheckCircle2, Clock3, MinusCircle, XCircle } from "lucide-react";
import type { ComponentType } from "react";
import { StatusBadge } from "@/components/atom/StatusBadge";
import type { MandateSigner } from "@/lib/api/admin-mandate-signers";
import { formatFechaCalendario } from "@/lib/format/date";
import {
  presentarVigencia,
  type EstadoVigenciaVista,
} from "@/lib/plataforma/mandatario-vigencia";

const ICONO: Partial<Record<EstadoVigenciaVista, ComponentType<{ className?: string; "aria-hidden"?: boolean }>>> = {
  vigente: CheckCircle2,
  por_vencer: Clock3,
  vencido: XCircle,
  inactivo: MinusCircle,
  no_vigente: CalendarClock,
};

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

  const Icono = ICONO[vista.estado];
  const hasta =
    signer.validityKind === "range" && signer.validTo ? formatFechaCalendario(signer.validTo) : null;

  return (
    <span className="inline-flex flex-col items-start gap-0.5" data-testid="mandatario-vigencia" data-estado={vista.estado}>
      <StatusBadge
        tone={vista.tone}
        ariaLabel={`Vigencia: ${vista.texto}`}
        label={
          <span className="inline-flex items-center gap-1">
            {Icono ? <Icono className="h-3.5 w-3.5 shrink-0" aria-hidden={true} /> : null}
            {vista.texto}
          </span>
        }
      />
      {hasta ? <span className="text-[11px] text-[#59677D] dark:text-white/55">Hasta {hasta}</span> : null}
    </span>
  );
}
