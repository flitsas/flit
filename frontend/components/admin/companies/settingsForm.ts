// Estado unificado del formulario de configuración multi-pestaña (HU #10194, AC2).
// Las 4 pestañas de config comparten este estado para un único PUT atómico.
import type {
  EnrutamientoSMTP,
  NotificationTarget,
  TenantSettings,
  TenantSettingsUpdate,
} from "@/lib/api/types";

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
  onlyOwnVehicles: boolean;
  baulFirmasActivo: boolean;
  enrutamientoSMTP: EnrutamientoSMTP;
  notificationTarget: NotificationTarget;
  metodosRecaudo: string[];
  // HU #10478 — proveedor PRIMARIO por tipo de consulta RUNT (el fallback se deriva).
  consultaVin: string;
  consultaPlaca: string;
  consultaConductor: string;
  runtFailoverTimeoutMs: number;
}

/** Construye el estado del formulario a partir de la configuración cargada. */
export function formFromSettings(settings: TenantSettings): SettingsForm {
  const cfg = settings.consultationProviderConfig ?? {};
  return {
    allowInitialRegistration: settings.switchesMatricula.allowInitialRegistration,
    allowMiscNewVehicles: settings.switchesMatricula.allowMiscNewVehicles,
    onlyOwnVehicles: settings.switchesMatricula.onlyOwnVehicles,
    baulFirmasActivo: settings.baulFirmasActivo,
    enrutamientoSMTP: settings.enrutamientoSMTP,
    notificationTarget: settings.notificationTarget,
    metodosRecaudo: [...settings.metodosRecaudo],
    consultaVin: cfg.vehicle_vin?.primary ?? DEFAULT_VEHICLE_PROVIDER,
    consultaPlaca: cfg.vehicle_plate?.primary ?? DEFAULT_VEHICLE_PROVIDER,
    consultaConductor: cfg.conductor?.primary ?? DEFAULT_CONDUCTOR_PROVIDER,
    runtFailoverTimeoutMs: settings.runtFailoverTimeoutMs ?? DEFAULT_FAILOVER_MS,
  };
}

/** Serializa el formulario al payload del PUT settings. */
export function formToUpdate(form: SettingsForm): TenantSettingsUpdate {
  return {
    switchesMatricula: {
      allowInitialRegistration: form.allowInitialRegistration,
      allowMiscNewVehicles: form.allowMiscNewVehicles,
      onlyOwnVehicles: form.onlyOwnVehicles,
    },
    baulFirmasActivo: form.baulFirmasActivo,
    enrutamientoSMTP: form.enrutamientoSMTP,
    notificationTarget: form.notificationTarget,
    metodosRecaudo: [...form.metodosRecaudo],
    runtFailoverTimeoutMs: form.runtFailoverTimeoutMs,
    consultationProviderConfig: {
      vehicle_vin: { primary: form.consultaVin, fallback: fallbackFor("vehicle", form.consultaVin) },
      vehicle_plate: { primary: form.consultaPlaca, fallback: fallbackFor("vehicle", form.consultaPlaca) },
      conductor: { primary: form.consultaConductor, fallback: fallbackFor("conductor", form.consultaConductor) },
    },
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
    key: "allowInitialRegistration",
    module: "Matrícula Inicial",
    label: "Permitir matrícula inicial",
    describe: (_i, c) => onOff(c.allowInitialRegistration),
  },
  {
    key: "allowMiscNewVehicles",
    module: "Matrícula Inicial",
    label: "Permitir vehículos de categorías misceláneas",
    describe: (_i, c) => onOff(c.allowMiscNewVehicles),
  },
  {
    key: "onlyOwnVehicles",
    module: "Traspasos",
    label: "Solo vehículos propios",
    describe: (_i, c) => onOff(c.onlyOwnVehicles),
  },
  {
    key: "baulFirmasActivo",
    module: "Configuración Empresa",
    label: "Baúl de firmas activo",
    describe: (_i, c) => onOff(c.baulFirmasActivo),
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
];

const providerLabel = (key: string) => CONSULTATION_PROVIDER_LABELS[key] ?? key;

const providerChange = (
  previous: string,
  current: string,
): Omit<ConfigChangeItem, "label"> => ({
  detail: `${providerLabel(previous)} → ${providerLabel(current)}`,
  tone: "neutral",
});

const MODULE_ORDER = [
  "Matrícula Inicial",
  "Traspasos",
  "Configuración Empresa",
  CONSULTA_MODULE,
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
