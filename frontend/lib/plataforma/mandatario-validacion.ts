/**
 * HU #13248 (Feature F9 #13245) — cómo se presenta la validación de identidad PROPIA del mandatario
 * y los textos de sus avisos. Solo cuenta la validación lanzada para ese mandatario; el servidor
 * calcula `identityStatus`, aquí solo se traduce a texto y se decide cuándo aplica el bloque.
 */
import type {
  MandateSigner,
  MandateSignerIdentityResend,
  MandateSignerSaved,
  SignatureMethod,
} from "@/lib/api/admin-mandate-signers";
import type { StatusTone } from "@/components/atom/StatusBadge";
import { ApiError, ApiValidationError } from "@/lib/api/types";
import { modeloDe } from "./mandatario-vigencia";

type Estado = MandateSigner["identityStatus"];

export interface PresentacionValidacion {
  estado: Estado;
  texto: string;
  tone: StatusTone;
  /** Frase corta bajo la etiqueta. */
  detalle: string;
}

const POR_ESTADO: Record<Estado, Omit<PresentacionValidacion, "estado">> = {
  valid: {
    texto: "Aprobada",
    tone: "success",
    detalle: "La persona validó su identidad.",
  },
  pending: {
    texto: "En curso",
    tone: "info",
    detalle: "Enviamos el enlace. Falta que la persona complete la validación.",
  },
  expired: {
    texto: "Enlace vencido",
    tone: "warning",
    detalle: "El enlace venció sin completarse. Reenvía la validación.",
  },
  none: {
    texto: "Pendiente de validación",
    tone: "danger",
    detalle: "Sin firma válida hasta que la persona valide su identidad.",
  },
};

export function presentarValidacion(estado: Estado | null | undefined): PresentacionValidacion {
  const e: Estado = estado && estado in POR_ESTADO ? estado : "none";
  return { estado: e, ...POR_ESTADO[e] };
}

/** Forma de firma efectiva: un mandatario anterior sin forma y sin baúl se trata como validación de identidad. */
export function formaDeFirmaDe(
  signer: Pick<MandateSigner, "signerModel" | "signatureMethod" | "signatureVaultId">,
): SignatureMethod | null {
  if (modeloDe(signer) !== "natural") return null;
  if (signer.signatureMethod) return signer.signatureMethod;
  return signer.signatureVaultId ? "baul" : "biometria";
}

/** El bloque de validación solo aplica a la Persona natural con validación de identidad. */
export function requiereValidacionPropia(
  signer: Pick<MandateSigner, "signerModel" | "signatureMethod" | "signatureVaultId">,
): boolean {
  return formaDeFirmaDe(signer) === "biometria";
}

/** «Reenviar validación» se ofrece solo si aplica, el mandatario está activo, no está aprobada y el actor puede editar. */
export function puedeReenviarValidacion(
  signer: Pick<
    MandateSigner,
    "signerModel" | "signatureMethod" | "signatureVaultId" | "isActive" | "identityStatus" | "puedeEditar"
  >,
): boolean {
  return (
    requiereValidacionPropia(signer) &&
    signer.isActive &&
    signer.identityStatus !== "valid" &&
    signer.puedeEditar !== false
  );
}

/** Mensaje de éxito del reenvío. */
export function mensajeReenvio(result: MandateSignerIdentityResend | undefined, email: string | null): string {
  if (result?.identity === "queued") {
    return "No pudimos enviar el enlace ahora. Reintentaremos el envío automáticamente.";
  }
  return email ? `Enviamos el enlace de validación a ${email}.` : "Enviamos el enlace de validación.";
}

/** Mensaje de error del reenvío, sin códigos ni jerga. */
export function mensajeErrorReenvio(error: unknown): string {
  if (error instanceof ApiValidationError) {
    if (error.errors.some((e) => e.field?.toLowerCase() === "email")) {
      return "Falta el correo del mandatario. Edítalo, escribe el correo, guarda y vuelve a reenviar.";
    }
    return error.errors[0]?.message || "No se pudo reenviar la validación.";
  }
  if (error instanceof ApiError) {
    const code = (error.body as { code?: string } | null | undefined)?.code;
    if (error.status === 403) {
      return code === "mandatario_configurado_por_organismo"
        ? "Este mandatario lo configuró el organismo de tránsito: solo el organismo puede reenviar su validación."
        : "No tienes permiso para reenviar la validación.";
    }
    if (error.status === 409) return "Este mandatario no requiere validación de identidad.";
    if (error.status === 422) return "Falta el correo del mandatario. Edítalo, guarda y vuelve a reenviar.";
    if (error.status === 502) return "No pudimos enviar el enlace. Intenta de nuevo en unos minutos.";
    if (error.status === 404) return "El mandatario ya no existe. Actualiza la lista.";
  }
  return "No se pudo reenviar la validación. Intenta de nuevo.";
}

/** Qué dispara una validación nueva al guardar (AC4); `null` si no se dispara ninguna. */
export type DisparoDeValidacion = "alta" | "desde_baul" | "cambio_documento";

export function disparoDeValidacion(args: {
  editing: MandateSigner | null;
  metodo: SignatureMethod | null;
  esNatural: boolean;
  tipoDocumento: string;
  numeroDocumento: string;
}): DisparoDeValidacion | null {
  const { editing, metodo, esNatural, tipoDocumento, numeroDocumento } = args;
  if (!esNatural || metodo !== "biometria") return null;
  if (!editing) return "alta";
  const antes = formaDeFirmaDe(editing);
  const eraNatural = modeloDe(editing) === "natural";
  if (eraNatural && antes === "baul") return "desde_baul";
  const cambiaDocumento =
    editing.documentType !== tipoDocumento || (editing.documentNumber ?? "") !== numeroDocumento.trim();
  if (cambiaDocumento && eraNatural) return "cambio_documento";
  return null;
}

/**
 * Aviso previo al guardado (AC4): cuándo el servidor va a lanzar una validación nueva. `null` si no
 * se dispara ninguna. En el alta con validación de identidad también avisa, sin hablar de la anterior.
 * HU #13248b: con `correo` escrito, el alta lo nombra («le enviaremos el enlace a ana@…»).
 */
export function avisoDeNuevaValidacion(args: {
  editing: MandateSigner | null;
  metodo: SignatureMethod | null;
  esNatural: boolean;
  tipoDocumento: string;
  numeroDocumento: string;
  correo?: string;
}): string | null {
  const disparo = disparoDeValidacion(args);
  const correo = args.correo?.trim();
  switch (disparo) {
    case "alta":
      return correo
        ? `Al guardar le enviaremos el enlace a ${correo}.`
        : "Al guardar enviaremos el enlace de validación al correo del mandatario.";
    case "desde_baul":
      return "Al guardar enviaremos una nueva validación al correo del mandatario.";
    case "cambio_documento":
      return "Al guardar enviaremos una nueva validación al correo del mandatario. La anterior deja de contar.";
    default:
      return null;
  }
}

/** Frase corta para el resumen del pie del formulario («se enviará la validación»…). */
export function consecuenciaDeGuardar(disparo: DisparoDeValidacion | null): string | null {
  switch (disparo) {
    case "alta":
      return "se enviará la validación";
    case "desde_baul":
      return "pasará a validación de identidad";
    case "cambio_documento":
      return "la validación anterior deja de contar";
    default:
      return null;
  }
}

/** Mensaje tras guardar, según lo que informó el servidor en `identity`. `null` si no hay nada que añadir. */
export function mensajeValidacionTrasGuardar(
  saved: Pick<MandateSignerSaved, "identity"> | undefined,
  email: string | null,
): string | null {
  switch (saved?.identity) {
    case "sent":
      return email ? `Enviamos el enlace de validación a ${email}.` : "Enviamos el enlace de validación.";
    case "queued":
      return "No pudimos enviar el enlace ahora. Reintentaremos el envío automáticamente.";
    case "failed":
      return "Se guardó, pero no se pudo enviar la validación. Usa «Reenviar validación».";
    default:
      return null;
  }
}
