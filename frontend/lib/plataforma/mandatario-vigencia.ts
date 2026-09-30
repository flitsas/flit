/**
 * HU #13133 (ADR-0061) — cómo se presenta el modelo y el estado de vigencia del mandatario en la
 * lista. El estado lo calcula el servidor (`validityStatus`, «por vencer» = 7 días o menos antes del
 * fin); aquí solo se traduce a texto, tono y aplicabilidad.
 */
import type { MandateSigner, SignerModel, ValidityStatus } from "@/lib/api/admin-mandate-signers";
import type { StatusTone } from "@/components/atom/StatusBadge";
import { MODELOS_MANDATARIO } from "./mandatario-modelo";

export type EstadoVigenciaVista = ValidityStatus | "sin_vigencia" | "desconocido";

export interface PresentacionVigencia {
  estado: EstadoVigenciaVista;
  /** Texto de la etiqueta: el estado nunca depende solo del color (WCAG 1.4.1). */
  texto: string;
  tone: StatusTone;
}

const POR_ESTADO: Record<ValidityStatus, { texto: string; tone: StatusTone }> = {
  vigente: { texto: "Vigente", tone: "success" },
  por_vencer: { texto: "Por vencer", tone: "warning" },
  vencido: { texto: "Vencido", tone: "danger" },
  inactivo: { texto: "Inactivo", tone: "neutral" },
  no_vigente: { texto: "Aún no vigente", tone: "info" },
};

/** Sin modelo (mandatario anterior al cambio) el mandatario es Persona natural. */
export function modeloDe(signer: Pick<MandateSigner, "signerModel">): SignerModel {
  return signer.signerModel ?? "natural";
}

export function etiquetaModelo(signer: Pick<MandateSigner, "signerModel">): string {
  const modelo = modeloDe(signer);
  return MODELOS_MANDATARIO.find((m) => m.value === modelo)?.label ?? "—";
}

/**
 * Persona jurídica y Formato en blanco no tienen vigencia: no aplica (AC4). Un mandatario inactivo
 * se muestra como tal en cualquier modelo.
 */
export function presentarVigencia(
  signer: Pick<MandateSigner, "signerModel" | "validityStatus" | "isActive">,
): PresentacionVigencia {
  if (!signer.isActive || signer.validityStatus === "inactivo") {
    return { estado: "inactivo", ...POR_ESTADO.inactivo };
  }
  if (modeloDe(signer) !== "natural") {
    return { estado: "sin_vigencia", texto: "Sin vigencia", tone: "neutral" };
  }
  if (!signer.validityStatus) {
    return { estado: "desconocido", texto: "Sin vigencia", tone: "neutral" };
  }
  return { estado: signer.validityStatus, ...POR_ESTADO[signer.validityStatus] };
}
