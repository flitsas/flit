/**
 * HU #13132 (ADR-0061) — reglas del formulario de mandatario por modelo, forma de firma y vigencia.
 *
 * El backend es quien manda (`MandateSignerModelRules`, 422 por campo); esto solo las adelanta en
 * pantalla con texto corto para no enviar una petición que ya se sabe inválida.
 */
import type {
  MandateSigner,
  MandateSignerProfileFields,
  SignatureMethod,
  SignerModel,
  ValidityKind,
} from "@/lib/api/admin-mandate-signers";

export const MODELOS_MANDATARIO: ReadonlyArray<{ value: SignerModel; label: string }> = [
  { value: "natural", label: "Persona natural" },
  { value: "juridica", label: "Persona jurídica" },
  { value: "formato_blanco", label: "Formato en blanco" },
];

export const FORMAS_DE_FIRMA: ReadonlyArray<{ value: SignatureMethod; label: string }> = [
  { value: "baul", label: "Baúl de firmas" },
  { value: "biometria", label: "Validación de identidad" },
];

export const TIPOS_DE_VIGENCIA: ReadonlyArray<{ value: ValidityKind; label: string }> = [
  { value: "fixed", label: "Fija" },
  { value: "range", label: "Rango de fechas" },
];

export const NOMBRE_FORMATO_EN_BLANCO = "Formato en blanco";

/** Campos del formulario a los que se asocia un error. */
export type CampoMandatario =
  | "signerModel"
  | "fullName"
  | "documentNumber"
  | "signatureMethod"
  | "signatureVaultId"
  | "validityKind"
  | "validFrom"
  | "validTo"
  | "email"
  | "offices";

export type ErroresMandatario = Partial<Record<CampoMandatario, string>>;

export interface PerfilMandatario {
  model: SignerModel;
  method: SignatureMethod | null;
  validityKind: ValidityKind;
  validFrom: string;
  validTo: string;
  signatureVaultId: string | null;
}

/**
 * Perfil inicial del formulario. Un mandatario anterior al cambio no trae modelo: es Persona natural,
 * conserva su firma del baúl (⇒ forma «baúl») o, si solo tenía identidad, «validación de identidad».
 */
export function perfilInicial(editing: MandateSigner | null): PerfilMandatario {
  const model = editing?.signerModel ?? "natural";
  const vaultId = editing?.signatureVaultId ?? null;
  let method: SignatureMethod | null = editing?.signatureMethod ?? null;
  if (editing && model === "natural" && method === null) {
    if (vaultId) method = "baul";
    else if (editing.identityStatus === "valid" || editing.identityStatus === "pending") method = "biometria";
  }
  return {
    model,
    method,
    validityKind: editing?.validityKind ?? "fixed",
    validFrom: editing?.validFrom ?? "",
    validTo: editing?.validTo ?? "",
    signatureVaultId: vaultId,
  };
}

/**
 * Validación local de la Persona natural (AC3): forma de firma, firma elegida con baúl y fechas
 * coherentes con el rango. `exigeSelectorBaul` es falso en el hub del OT, que no muestra el baúl.
 */
export function validarPerfil(
  perfil: PerfilMandatario,
  opciones: { exigeSelectorBaul: boolean },
): ErroresMandatario {
  const errores: ErroresMandatario = {};
  if (perfil.model !== "natural") return errores;

  if (!perfil.method) {
    errores.signatureMethod = "Elige la forma de firma.";
  } else if (perfil.method === "baul" && opciones.exigeSelectorBaul && !perfil.signatureVaultId) {
    errores.signatureVaultId = "Elige una firma del baúl.";
  }

  if (perfil.validityKind === "range") {
    if (!perfil.validFrom) errores.validFrom = "Indica la fecha de inicio.";
    if (!perfil.validTo) errores.validTo = "Indica la fecha de fin.";
    if (perfil.validFrom && perfil.validTo && perfil.validTo < perfil.validFrom) {
      errores.validTo = "La fecha de fin no puede ser anterior a la de inicio.";
    }
  }
  return errores;
}

/**
 * Campos que viajan al servidor. Con modelos distintos de natural no se envía forma de firma, fechas
 * ni tipo de vigencia: el servidor responde 422 si llegan.
 */
export function camposDePerfil(perfil: PerfilMandatario): MandateSignerProfileFields {
  if (perfil.model !== "natural") return { signerModel: perfil.model };
  const campos: MandateSignerProfileFields = {
    signerModel: "natural",
    signatureMethod: perfil.method ?? undefined,
    validityKind: perfil.validityKind,
  };
  if (perfil.validityKind === "range") {
    campos.validFrom = perfil.validFrom;
    campos.validTo = perfil.validTo;
  }
  return campos;
}

const CAMPOS_CONOCIDOS: Record<string, CampoMandatario> = {
  signermodel: "signerModel",
  fullname: "fullName",
  documentnumber: "documentNumber",
  signaturemethod: "signatureMethod",
  signaturevaultid: "signatureVaultId",
  validitykind: "validityKind",
  validfrom: "validFrom",
  validto: "validTo",
  email: "email",
  transitofficeids: "offices",
  companytenantids: "offices",
};

/** Traduce el `field` de un 422 al campo del formulario; `null` si no corresponde a ninguno. */
export function campoDeError(field: string | null | undefined): CampoMandatario | null {
  if (!field) return null;
  return CAMPOS_CONOCIDOS[field.replace(/[^a-z]/gi, "").toLowerCase()] ?? null;
}
