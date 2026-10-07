// Etiquetas legibles del historial de cambios de una compañía. El backend audita con nombres de
// columna/tabla (`tenant_operational_policies.generate_improntas`); aquí se traducen para que se
// entiendan sin conocer la base de datos. Fuente de los campos: Flit.Admin.Application
// (`SettingsDiff.cs` y los emisores de auditoría). Los desconocidos caen a un texto legible.

/** Campo (columna) → etiqueta. */
const FIELD_LABELS: Record<string, string> = {
  allow_initial_registration: "Matrícula inicial",
  allow_misc_new_vehicles: "Vehículos nuevos varios",
  only_own_vehicles: "Solo vehículos propios",
  only_own_vehicles_matriculas: "Solo vehículos propios (matrículas)",
  only_own_vehicles_otros: "Solo vehículos propios (otros trámites)",
  block_procedure_family_traspaso: "Bloqueo de traspasos",
  block_procedure_family_otros: "Bloqueo de otros trámites",
  signature_vault_enabled: "Bóveda de firmas",
  validate_soat_with_runt: "Validar SOAT con RUNT",
  personalized_documents_enabled: "Documentos personalizados",
  tramite_approved_emails_enabled: "Correos de trámite aprobado",
  tramite_rejected_emails_enabled: "Correos de trámite rechazado",
  tramite_state_email_recipients: "Destinatarios de correos de estado",
  tramites_module_enabled: "Módulo de trámites",
  comparendos_module_enabled: "Módulo de comparendos",
  resoluciones_module_enabled: "Módulo de resoluciones",
  generate_improntas: "Generación de improntas",
  fines_query_source: "Fuente de consulta de comparendos",
  notification_channel: "Canal de notificación",
  notification_target: "Destino de notificación",
  payment_methods: "Medios de pago",
  runt_failover_timeout_ms: "Tiempo de espera de respaldo RUNT",
  consultation_provider_config: "Proveedores de consulta",
  avaluo_provider_config: "Proveedores de avalúo",
  transit_office_id: "Organismo de tránsito",
  transit_office_ids: "Organismos de tránsito",
  document_optional: "Documento opcional",
  is_active: "Estado de la compañía",
  accept_terms: "Aceptación de términos y condiciones",
  login: "Inicio de sesión",
  email: "Correo electrónico",
  created: "Creación",
  updated: "Actualización",
};

/** Entidad (tabla) → nombre corto, para desambiguar y para el texto de apoyo de la fila. */
const ENTITY_LABELS: Record<string, string> = {
  tenant_operational_policies: "Políticas operativas",
  tenant_settings: "Configuración",
  tenant_transit_office_grants: "Concesión de organismos",
  tenant_transit_office_blocks: "Bloqueo de organismos",
  tenant_transit_office_blocking_policies: "Política de bloqueo",
  tenant_transit_office_consultation_restrictions: "Restricción de consultas",
  tenant_transit_office_prenda_document_policies: "Documentos de prenda",
  tenant_whitelist_users: "Lista blanca de usuarios",
  tenant_module_grants: "Módulos habilitados",
  procedure_terms_acceptance: "Términos y condiciones",
  runt_confirmation_settings: "Confirmación RUNT",
  session: "Sesión",
  mandate_signer: "Mandatario",
  mandate_format: "Formato de mandato",
  personalized_document: "Documento personalizado",
  company_ot_mandate_rule: "Regla de mandato por organismo",
  hierarchy_switch: "Cambio de jerarquía",
  TenantDomain: "Dominio",
  TenantProduct: "Producto",
  tenant: "Compañía",
};

/** Campos booleanos que representan algo que se «habilita» (Habilitada/Deshabilitada). */
const ENABLED_STYLE = (field: string) =>
  field.endsWith("_enabled") ||
  field === "generate_improntas" ||
  field === "validate_soat_with_runt" ||
  field === "allow_initial_registration" ||
  field === "allow_misc_new_vehicles";

/** Campos booleanos de restricción: «Sí» es el estado que limita al gestor. */
const RESTRICTION_STYLE = (field: string) =>
  field.startsWith("block_procedure_family_") || field.startsWith("only_own_vehicles");

const VALUE_WORDS: Record<string, string> = {
  internal: "Interna",
  external: "Externa",
  email: "Correo",
  sms: "SMS",
  whatsapp: "WhatsApp",
};

/** `snake_case`/`camelCase` → «Texto con espacios» (fallback de campos no catalogados). */
export function humanizeKey(key: string): string {
  const spaced = key
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[_.]+/g, " ")
    .trim()
    .toLowerCase();
  return spaced ? spaced.charAt(0).toUpperCase() + spaced.slice(1) : key;
}

export function fieldLabel(fieldName: string): string {
  return FIELD_LABELS[fieldName] ?? humanizeKey(fieldName);
}

export function entityLabel(entityName: string): string {
  return ENTITY_LABELS[entityName] ?? humanizeKey(entityName);
}

/** Clave técnica completa (`entidad.campo`), solo para tooltip, detalle y búsqueda. */
export function technicalKey(entityName: string | undefined, fieldName: string): string {
  return entityName ? `${entityName}.${fieldName}` : fieldName;
}

/**
 * Etiqueta del evento. Si el campo es genérico (id, email, is_active…) se antepone la entidad para
 * que se entienda de qué cambio habla; en los demás basta el campo.
 */
export function eventLabel(entityName: string | undefined, fieldName: string): string {
  const field = fieldLabel(fieldName);
  const generic = ["transit_office_id", "transit_office_ids", "email", "is_active", "created", "updated", "document_optional"];
  if (entityName && generic.includes(fieldName) && ENTITY_LABELS[entityName]) {
    return `${ENTITY_LABELS[entityName]}: ${field.toLowerCase()}`;
  }
  return field;
}

export type ParsedAuditValue =
  | { kind: "empty" }
  | { kind: "boolean"; value: boolean; positive: string; negative: string; tone: "success" | "warning" | "neutral" }
  | { kind: "text"; text: string }
  | { kind: "complex"; pretty: string; summary: string };

function safeParse(raw: string): { ok: true; value: unknown } | { ok: false } {
  try {
    return { ok: true, value: JSON.parse(raw) };
  } catch {
    return { ok: false };
  }
}

const MAX_INLINE_LENGTH = 48;

/** Interpreta el valor auditado (JSON crudo) para mostrarlo sin volcar el JSON en la fila. */
export function parseAuditValue(raw: string | null | undefined, fieldName: string): ParsedAuditValue {
  if (raw === null || raw === undefined || raw === "") return { kind: "empty" };
  const parsed = safeParse(raw);
  const value: unknown = parsed.ok ? parsed.value : raw;

  if (value === null) return { kind: "empty" };

  if (typeof value === "boolean") {
    if (ENABLED_STYLE(fieldName)) {
      return { kind: "boolean", value, positive: "Habilitada", negative: "Deshabilitada", tone: value ? "success" : "neutral" };
    }
    if (fieldName === "is_active") {
      return { kind: "boolean", value, positive: "Activa", negative: "Inactiva", tone: value ? "success" : "neutral" };
    }
    if (RESTRICTION_STYLE(fieldName)) {
      return { kind: "boolean", value, positive: "Sí", negative: "No", tone: value ? "warning" : "neutral" };
    }
    return { kind: "boolean", value, positive: "Sí", negative: "No", tone: value ? "success" : "neutral" };
  }

  if (typeof value === "number") {
    const text = fieldName.endsWith("_ms") ? `${value} ms` : String(value);
    return { kind: "text", text };
  }

  if (typeof value === "string") {
    const text = VALUE_WORDS[value.toLowerCase()] ?? value;
    if (text.length > MAX_INLINE_LENGTH) return { kind: "complex", pretty: text, summary: "Texto largo" };
    return { kind: "text", text };
  }

  if (Array.isArray(value)) {
    const allSimple = value.every((v) => typeof v === "string" || typeof v === "number");
    const joined = value.map(String).join(", ");
    if (allSimple && joined.length <= MAX_INLINE_LENGTH) {
      return value.length === 0 ? { kind: "text", text: "Ninguno" } : { kind: "text", text: joined };
    }
    return { kind: "complex", pretty: JSON.stringify(value, null, 2), summary: `${value.length} elementos` };
  }

  if (value && typeof value === "object" && Object.keys(value).length === 0) {
    return { kind: "text", text: "Sin configuración" };
  }
  return { kind: "complex", pretty: JSON.stringify(value, null, 2), summary: "Varios datos" };
}

/** Texto plano de un valor, para la búsqueda en cliente. */
export function auditValueText(raw: string | null | undefined, fieldName: string): string {
  const p = parseAuditValue(raw, fieldName);
  switch (p.kind) {
    case "empty":
      return "";
    case "boolean":
      return p.value ? p.positive : p.negative;
    case "text":
      return p.text;
    case "complex":
      return p.pretty;
  }
}
