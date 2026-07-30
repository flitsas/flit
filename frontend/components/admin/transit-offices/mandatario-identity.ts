// Presentación del estado de validación de identidad de un mandatario (HU #10994/#11000).
//
// HU #11060 — la lógica se generalizó a `@/lib/admin/identity-vigencia`, compartida con el
// representante legal (HU #11059): los dos sujetos validan identidad con el mismo servicio y vencen
// con las mismas reglas. Este módulo queda como fachada del hub Admin OT para no tocar sus imports.

export {
  hasPriorIdentity,
  identityUi,
  puedeRenovarIdentidad,
  vigenciaLabel,
  type AdminIdentityUi as MandateSignerIdentityUi,
  type AdminIdentityStatus as MandateSignerIdentityStatus,
} from "@/lib/admin/identity-vigencia";
