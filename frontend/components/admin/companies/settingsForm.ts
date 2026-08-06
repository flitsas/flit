// Estado unificado del formulario de configuración multi-pestaña (HU #10194, AC2).
// Las 4 pestañas de config comparten este estado para un único PUT atómico.
import type {
  EnrutamientoSMTP,
  FinesQuerySource,
  NotificationTarget,
  TenantSettings,
  TenantSettingsUpdate,
} from "@/lib/api/types";

// FEATURE 02 — fuente de comparendos. Default 'external' (SIMIT en línea).
export const DEFAULT_FINES_QUERY_SOURCE: FinesQuerySource = "external";

// ── HU #10478: proveedores de consulta RUNT ─────────────────────────────────
export const CONSULTA_MODULE = "Proveedores de consulta RUNT";
export const DEFAULT_VEHICLE_PROVIDER = "kyverum_runt";
export const DEFAULT_CONDUCTOR_PROVIDER = "kyverum_runt_conductor";
export const DEFAULT_FAILOVER_MS = 60_000;
export const FAILOVER_MIN_MS = 500;
export const FAILOVER_MAX_MS = 60000;

/** Opción de proveedor para un selector (Intempo se muestra deshabilitado: aún no disponible). */
export interface ConsultationProviderOption {
  value: string;
  label: string;
  disabled: boolean;
}

/** Proveedores para consultas de vehículo (VIN y placa). Kyverum es el default. */
export const CONSULTATION_VEHICLE_PROVIDERS: ConsultationProviderOption[] = [
  { value: "kyverum_runt", label: "Kyverum RUNT", disabled: false },
  { value: "verifik", label: "Verifik", disabled: false },
  { value: "intempo", label: "Intempo (próximamente)", disabled: true },
];

/** Proveedores para consulta de conductor. Kyverum es el default. */
export const CONSULTATION_CONDUCTOR_PROVIDERS: ConsultationProviderOption[] = [
  { value: "kyverum_runt_conductor", label: "Kyverum RUNT", disabled: false },
  { value: "verifik_conductor", label: "Verifik", disabled: false },
  { value: "intempo", label: "Intempo (próximamente)", disabled: true },
];

/** Etiqueta legible de cada provider key (para el resumen de cambios y la UI). */
export const CONSULTATION_PROVIDER_LABELS: Record<string, string> = {
  kyverum_runt: "Kyverum RUNT",
  verifik: "Verifik",
  kyverum_runt_conductor: "Kyverum RUNT",
  verifik_conductor: "Verifik",
  intempo: "Intempo",
};

// ── Feature #10707: proveedores de avalúo comercial ─────────────────────────
export const AVALUO_MODULE = "Proveedores de avalúos";
/** Proveedor base: siempre habilitado y sugerido por defecto. */
export const AVALUO_BASE_PROVIDER = "fasecolda";
export const DEFAULT_AVALUO_PRIMARY = AVALUO_BASE_PROVIDER;

/** Opción de proveedor de avalúo. `locked` = base (Fasecolda), no se puede desactivar. */
export interface AvaluoProviderOption {
  value: string;
  label: string;
  locked: boolean;
  /** Nota bajo la etiqueta (p. ej. "estimación por tabla" / "publicaciones"). */
  hint?: string;
}

/**
 * Proveedores de avalúo soportados. Fasecolda es el base (siempre activo); los demás se habilitan
 * por compañía. Para sumar un proveedor nuevo basta con agregarlo aquí y registrar su IAvaluoProvider
 * en backend (ADR-0029) — la UI y el guardado lo toman automáticamente.
 */
export const AVALUO_PROVIDERS: AvaluoProviderOption[] = [
  { value: "fasecolda", label: "Fasecolda", locked: true, hint: "Guía de valores por VIN" },
  { value: "base_gravable", label: "Base gravable", locked: false, hint: "Estimación por base gravable" },
  { value: "mercado_libre", label: "Mercado Libre", locked: false, hint: "Mediana de publicaciones" },
];

export const AVALUO_PROVIDER_LABELS: Record<string, string> = Object.fromEntries(
  AVALUO_PROVIDERS.map((p) => [p.value, p.label]),
);

const KNOWN_AVALUO_PROVIDERS = AVALUO_PROVIDERS.map((p) => p.value);

/** Normaliza la lista de habilitados: solo keys conocidas y Fasecolda siempre presente. */
function normalizeAvaluoEnabled(enabled: string[] | undefined): string[] {
  const set = new Set<string>([AVALUO_BASE_PROVIDER]);
  for (const key of enabled ?? []) {
    if (KNOWN_AVALUO_PROVIDERS.includes(key)) set.add(key);
  }
  // Orden estable según AVALUO_PROVIDERS.
  return KNOWN_AVALUO_PROVIDERS.filter((k) => set.has(k));
}

// El fallback se deriva del primario elegido: el "otro" proveedor real de la misma familia, para
// que siempre exista contingencia (la cadena que consume el backend es [primary, ...fallback]).
const VEHICLE_FALLBACK: Record<string, string[]> = {
  kyverum_runt: ["verifik"],
  verifik: ["kyverum_runt"],
};
const CONDUCTOR_FALLBACK: Record<string, string[]> = {
  kyverum_runt_conductor: ["verifik_conductor"],
  verifik_conductor: ["kyverum_runt_conductor"],
};

function fallbackFor(family: "vehicle" | "conductor", primary: string): string[] {
  const table = family === "vehicle" ? VEHICLE_FALLBACK : CONDUCTOR_FALLBACK;
  return table[primary] ?? [];
}

export interface SettingsForm {
  allowInitialRegistration: boolean;
  allowMiscNewVehicles: boolean;
  /** Espejo legado de onlyOwnVehiclesTraspaso (PUT `onlyOwnVehicles`). */
  onlyOwnVehicles: boolean;
  onlyOwnVehiclesMatriculas: boolean;
  onlyOwnVehiclesTraspaso: boolean;
  onlyOwnVehiclesOtros: boolean;
  /**
   * Bloqueo de creación por familia (`true` = no permitir crear).
   * Matrículas se deriva de `!allowInitialRegistration` al serializar.
   */
  blockProcedureFamilyMatriculas: boolean;
  blockProcedureFamilyTraspaso: boolean;
  blockProcedureFamilyOtros: boolean;
  baulFirmasActivo: boolean;
  preasignacionPlacaActiva: boolean;
  /** Con placa completa → Terminado directo (omite Asignado). */
  plateFlowSkipToTerminado: boolean;
  validarSoatConRunt: boolean;
  enrutamientoSMTP: EnrutamientoSMTP;
  notificationTarget: NotificationTarget;
  metodosRecaudo: string[];
  // HU #10478 — proveedor PRIMARIO por tipo de consulta RUNT (el fallback se deriva).
  consultaVin: string;
  consultaPlaca: string;
  consultaConductor: string;
  runtFailoverTimeoutMs: number;
  // Feature #10707 — proveedores de avalúo habilitados (incluye siempre Fasecolda) + sugerido.
  avaluoEnabled: string[];
  avaluoPrimary: string;
  // FEATURE 02 — fuente de comparendos (internal | external).
  finesQuerySource: FinesQuerySource;
}

/** Construye el estado del formulario a partir de la configuración cargada. */
export function formFromSettings(settings: TenantSettings): SettingsForm {
  const cfg = settings.consultationProviderConfig ?? {};
  const byFamily = settings.switchesMatricula.onlyOwnVehiclesByFamily;
  const onlyTraspaso = byFamily?.traspaso ?? settings.switchesMatricula.onlyOwnVehicles;
  const block = settings.switchesMatricula.blockProcedureFamily;
  return {
    allowInitialRegistration: settings.switchesMatricula.allowInitialRegistration,
    allowMiscNewVehicles: settings.switchesMatricula.allowMiscNewVehicles,
    onlyOwnVehicles: onlyTraspaso,
    onlyOwnVehiclesMatriculas: byFamily?.matriculas ?? settings.switchesMatricula.onlyOwnVehicles,
    onlyOwnVehiclesTraspaso: onlyTraspaso,
    onlyOwnVehiclesOtros: byFamily?.otros ?? settings.switchesMatricula.onlyOwnVehicles,
    blockProcedureFamilyMatriculas: block?.matriculas ?? !settings.switchesMatricula.allowInitialRegistration,
    blockProcedureFamilyTraspaso: block?.traspaso ?? false,
    blockProcedureFamilyOtros: block?.otros ?? false,
    baulFirmasActivo: settings.baulFirmasActivo,
    preasignacionPlacaActiva: settings.preasignacionPlacaActiva,
    plateFlowSkipToTerminado: settings.plateFlowSkipToTerminado ?? false,
    validarSoatConRunt: settings.validarSoatConRunt ?? false,
    enrutamientoSMTP: settings.enrutamientoSMTP,
    notificationTarget: settings.notificationTarget,
    metodosRecaudo: [...settings.metodosRecaudo],
    consultaVin: cfg.vehicle_vin?.primary ?? DEFAULT_VEHICLE_PROVIDER,
    consultaPlaca: cfg.vehicle_plate?.primary ?? DEFAULT_VEHICLE_PROVIDER,
    consultaConductor: cfg.conductor?.primary ?? DEFAULT_CONDUCTOR_PROVIDER,
    runtFailoverTimeoutMs: settings.runtFailoverTimeoutMs ?? DEFAULT_FAILOVER_MS,
    ...avaluoFromSettings(settings.avaluoProviderConfig),
    finesQuerySource: settings.finesQuerySource ?? DEFAULT_FINES_QUERY_SOURCE,
  };
}

/** Deriva el estado de avalúo (habilitados + sugerido) de la config, con defaults sanos. */
function avaluoFromSettings(config: TenantSettings["avaluoProviderConfig"]): {
  avaluoEnabled: string[];
  avaluoPrimary: string;
} {
  const avaluoEnabled = normalizeAvaluoEnabled(config?.enabled);
  const primary =
    config?.primary && avaluoEnabled.includes(config.primary)
      ? config.primary
      : DEFAULT_AVALUO_PRIMARY;
  return { avaluoEnabled, avaluoPrimary: primary };
}

/** Serializa el formulario al payload del PUT settings. */
export function formToUpdate(form: SettingsForm): TenantSettingsUpdate {
  return {
    switchesMatricula: {
      allowInitialRegistration: !form.blockProcedureFamilyMatriculas,
      allowMiscNewVehicles: form.allowMiscNewVehicles,
      onlyOwnVehicles: form.onlyOwnVehiclesTraspaso,
      onlyOwnVehiclesByFamily: {
        matriculas: form.onlyOwnVehiclesMatriculas,
        traspaso: form.onlyOwnVehiclesTraspaso,
        otros: form.onlyOwnVehiclesOtros,
      },
      blockProcedureFamily: {
        matriculas: form.blockProcedureFamilyMatriculas,
        traspaso: form.blockProcedureFamilyTraspaso,
        otros: form.blockProcedureFamilyOtros,
      },
    },
    baulFirmasActivo: form.baulFirmasActivo,
    preasignacionPlacaActiva: form.preasignacionPlacaActiva,
    plateFlowSkipToTerminado: form.plateFlowSkipToTerminado,
    validarSoatConRunt: form.validarSoatConRunt,
    enrutamientoSMTP: form.enrutamientoSMTP,
    notificationTarget: form.notificationTarget,
    metodosRecaudo: [...form.metodosRecaudo],
    runtFailoverTimeoutMs: form.runtFailoverTimeoutMs,
    consultationProviderConfig: {
      vehicle_vin: { primary: form.consultaVin, fallback: fallbackFor("vehicle", form.consultaVin) },
      vehicle_plate: { primary: form.consultaPlaca, fallback: fallbackFor("vehicle", form.consultaPlaca) },
      conductor: { primary: form.consultaConductor, fallback: fallbackFor("conductor", form.consultaConductor) },
    },
    avaluoProviderConfig: {
      primary: form.avaluoPrimary,
      enabled: normalizeAvaluoEnabled(form.avaluoEnabled),
    },
    finesQuerySource: form.finesQuerySource,
  };
}

/** Un cambio concreto detectado en un campo, con la descripción real del cambio. */
export interface ConfigChangeItem {
  label: string;
  /** Descripción del cambio real: "Activar" / "Desactivar" o "valor anterior → valor nuevo". */
  detail: string;
  /** Tono para resaltar: activación (on), desactivación (off) o cambio de valor (neutral). */
  tone: "on" | "off" | "neutral";
}

/** Un grupo de cambios detectados, agrupados por pestaña/módulo de configuración. */
export interface ConfigChangeGroup {
  module: string;
  items: ConfigChangeItem[];
}

// Descriptor por campo: módulo, etiqueta y `describe` que produce el cambio REAL (activar/
// desactivar o valor anterior → nuevo). El orden define el orden en el resumen. La whitelist y
// la matriz OT NO entran aquí: tienen endpoint propio y no forman parte del PUT settings.
interface FieldDescriptor {
  key: keyof SettingsForm;
  module: string;
  label: string;
  describe: (initial: SettingsForm, current: SettingsForm) => Omit<ConfigChangeItem, "label">;
}

const onOff = (value: boolean): Omit<ConfigChangeItem, "label"> =>
  value ? { detail: "Activar", tone: "on" } : { detail: "Desactivar", tone: "off" };

const FIELD_DESCRIPTORS: FieldDescriptor[] = [
  {
    key: "blockProcedureFamilyMatriculas",
    module: "Matrículas",
    label: "No permitir trámites de matrículas",
    describe: (_i, c) => onOff(c.blockProcedureFamilyMatriculas),
  },
  {
    key: "allowMiscNewVehicles",
    module: "Matrículas",
    label: "Permitir vehículos de categorías misceláneas",
    describe: (_i, c) => onOff(c.allowMiscNewVehicles),
  },
  {
    key: "onlyOwnVehiclesMatriculas",
    module: "Matrículas",
    label: "Solo vehículos propios",
    describe: (_i, c) => onOff(c.onlyOwnVehiclesMatriculas),
  },
  {
    key: "blockProcedureFamilyTraspaso",
    module: "Traspaso",
    label: "No permitir trámites de traspaso",
    describe: (_i, c) => onOff(c.blockProcedureFamilyTraspaso),
  },
  {
    key: "onlyOwnVehiclesTraspaso",
    module: "Traspaso",
    label: "Solo vehículos propios",
    describe: (_i, c) => onOff(c.onlyOwnVehiclesTraspaso),
  },
  {
    key: "blockProcedureFamilyOtros",
    module: "Otros trámites",
    label: "No permitir otros trámites",
    describe: (_i, c) => onOff(c.blockProcedureFamilyOtros),
  },
  {
    key: "onlyOwnVehiclesOtros",
    module: "Otros trámites",
    label: "Solo vehículos propios",
    describe: (_i, c) => onOff(c.onlyOwnVehiclesOtros),
  },
  {
    key: "baulFirmasActivo",
    module: "Configuración Empresa",
    label: "Firma precargada (baúl)",
    describe: (_i, c) => onOff(c.baulFirmasActivo),
  },
  {
    key: "preasignacionPlacaActiva",
    module: "Configuración Empresa",
    label: "Preasignación de placa activa",
    describe: (_i, c) => onOff(c.preasignacionPlacaActiva),
  },
  {
    key: "plateFlowSkipToTerminado",
    module: "Configuración Empresa",
    label: "Omitir proceso del gestor (placa → Terminado)",
    describe: (_i, c) => onOff(c.plateFlowSkipToTerminado),
  },
  {
    key: "validarSoatConRunt",
    module: "Configuración Empresa",
    label: "Validar SOAT ante el RUNT al procesar",
    describe: (_i, c) => onOff(c.validarSoatConRunt),
  },
  {
    key: "enrutamientoSMTP",
    module: "Configuración Empresa",
    label: "Enrutamiento de notificaciones",
    describe: (i, c) => ({
      detail: `${SMTP_LABELS[i.enrutamientoSMTP]} → ${SMTP_LABELS[c.enrutamientoSMTP]}`,
      tone: "neutral",
    }),
  },
  {
    key: "notificationTarget",
    module: "Configuración Empresa",
    label: "Destinatario de notificaciones",
    describe: (i, c) => ({
      detail: `${NOTIFICATION_TARGET_LABELS[i.notificationTarget]} → ${NOTIFICATION_TARGET_LABELS[c.notificationTarget]}`,
      tone: "neutral",
    }),
  },
  {
    key: "finesQuerySource",
    module: "Configuración Empresa",
    label: "Fuente de comparendos",
    describe: (i, c) => ({
      detail: `${FINES_QUERY_SOURCE_LABELS[i.finesQuerySource]} → ${FINES_QUERY_SOURCE_LABELS[c.finesQuerySource]}`,
      tone: "neutral",
    }),
  },
  {
    key: "metodosRecaudo",
    module: "Configuración Empresa",
    label: "Métodos de recaudo",
    describe: (i, c) => {
      const added = c.metodosRecaudo.filter((m) => !i.metodosRecaudo.includes(m));
      const removed = i.metodosRecaudo.filter((m) => !c.metodosRecaudo.includes(m));
      const parts = [...added.map((m) => `+ ${m}`), ...removed.map((m) => `− ${m}`)];
      return { detail: parts.join(", "), tone: "neutral" };
    },
  },
  {
    key: "consultaVin",
    module: CONSULTA_MODULE,
    label: "Proveedor consulta por VIN",
    describe: (i, c) => providerChange(i.consultaVin, c.consultaVin),
  },
  {
    key: "consultaPlaca",
    module: CONSULTA_MODULE,
    label: "Proveedor consulta por placa",
    describe: (i, c) => providerChange(i.consultaPlaca, c.consultaPlaca),
  },
  {
    key: "consultaConductor",
    module: CONSULTA_MODULE,
    label: "Proveedor consulta de conductor",
    describe: (i, c) => providerChange(i.consultaConductor, c.consultaConductor),
  },
  {
    key: "runtFailoverTimeoutMs",
    module: CONSULTA_MODULE,
    label: "Timeout de failover (ms)",
    describe: (i, c) => ({
      detail: `${i.runtFailoverTimeoutMs} → ${c.runtFailoverTimeoutMs} ms`,
      tone: "neutral",
    }),
  },
  {
    key: "avaluoEnabled",
    module: AVALUO_MODULE,
    label: "Proveedores de avalúo habilitados",
    describe: (i, c) => {
      const added = c.avaluoEnabled.filter((m) => !i.avaluoEnabled.includes(m));
      const removed = i.avaluoEnabled.filter((m) => !c.avaluoEnabled.includes(m));
      const parts = [
        ...added.map((m) => `+ ${avaluoLabel(m)}`),
        ...removed.map((m) => `− ${avaluoLabel(m)}`),
      ];
      return { detail: parts.join(", "), tone: "neutral" };
    },
  },
  {
    key: "avaluoPrimary",
    module: AVALUO_MODULE,
    label: "Proveedor de avalúo sugerido",
    describe: (i, c) => ({
      detail: `${avaluoLabel(i.avaluoPrimary)} → ${avaluoLabel(c.avaluoPrimary)}`,
      tone: "neutral",
    }),
  },
];

const avaluoLabel = (key: string) => AVALUO_PROVIDER_LABELS[key] ?? key;

const providerLabel = (key: string) => CONSULTATION_PROVIDER_LABELS[key] ?? key;

const providerChange = (
  previous: string,
  current: string,
): Omit<ConfigChangeItem, "label"> => ({
  detail: `${providerLabel(previous)} → ${providerLabel(current)}`,
  tone: "neutral",
});

const MODULE_ORDER = [
  "Matrículas",
  "Traspaso",
  "Otros trámites",
  "Configuración Empresa",
  CONSULTA_MODULE,
  AVALUO_MODULE,
];

function fieldEquals(a: unknown, b: unknown): boolean {
  if (Array.isArray(a) && Array.isArray(b)) {
    if (a.length !== b.length) return false;
    const sa = [...a].sort();
    const sb = [...b].sort();
    return sa.every((v, i) => v === sb[i]);
  }
  return a === b;
}

/**
 * Compara el formulario contra su línea base (última configuración guardada) y devuelve los
 * cambios agrupados por módulo, cada uno con la descripción del cambio REAL (activar/desactivar
 * o valor anterior → nuevo). Vacío = sin cambios. Usado por la confirmación de "Guardar todo".
 */
export function diffSettings(initial: SettingsForm, current: SettingsForm): ConfigChangeGroup[] {
  const changed = FIELD_DESCRIPTORS.filter((d) => !fieldEquals(initial[d.key], current[d.key]));
  return MODULE_ORDER.map((module) => ({
    module,
    items: changed
      .filter((d) => d.module === module)
      .map((d) => ({ label: d.label, ...d.describe(initial, current) })),
  })).filter((g) => g.items.length > 0);
}

/** Métodos de recaudo soportados (RF10). Valores libres en backend. */
export const METODOS_RECAUDO = ["Pasarela FLIT", "Operación Tránsito (OT)", "Otros"] as const;

export const SMTP_LABELS: Record<EnrutamientoSMTP, string> = {
  FLIT_SMTP: "Colas FLIT",
  TENANT_API: "API Renting cliente",
};

export const NOTIFICATION_TARGETS: NotificationTarget[] = ["COMPRADOR", "RADICADOR", "NINGUNO"];

/** Etiquetas legibles para el destinatario de notificaciones (el valor enviado sigue siendo el enum). */
export const NOTIFICATION_TARGET_LABELS: Record<NotificationTarget, string> = {
  COMPRADOR: "Comprador del vehículo",
  RADICADOR: "Radicador del trámite",
  NINGUNO: "Sin notificaciones",
};

/** Opciones de fuente de comparendos (FEATURE 02). El valor enviado es el enum (internal|external). */
export const FINES_QUERY_SOURCES: FinesQuerySource[] = ["internal", "external"];

/** Etiquetas legibles para la fuente de comparendos. */
export const FINES_QUERY_SOURCE_LABELS: Record<FinesQuerySource, string> = {
  internal: "Interna (módulo de comparendos)",
  external: "Externa (SIMIT en línea)",
};
