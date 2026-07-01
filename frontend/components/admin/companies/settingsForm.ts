// Estado unificado del formulario de configuración multi-pestaña (HU #10194, AC2).
// Las 4 pestañas de config comparten este estado para un único PUT atómico.
import type {
  EnrutamientoSMTP,
  NotificationTarget,
  TenantSettings,
  TenantSettingsUpdate,
} from "@/lib/api/types";

export interface SettingsForm {
  allowInitialRegistration: boolean;
  allowMiscNewVehicles: boolean;
  onlyOwnVehicles: boolean;
  baulFirmasActivo: boolean;
  enrutamientoSMTP: EnrutamientoSMTP;
  notificationTarget: NotificationTarget;
  metodosRecaudo: string[];
}

/** Construye el estado del formulario a partir de la configuración cargada. */
export function formFromSettings(settings: TenantSettings): SettingsForm {
  return {
    allowInitialRegistration: settings.switchesMatricula.allowInitialRegistration,
    allowMiscNewVehicles: settings.switchesMatricula.allowMiscNewVehicles,
    onlyOwnVehicles: settings.switchesMatricula.onlyOwnVehicles,
    baulFirmasActivo: settings.baulFirmasActivo,
    enrutamientoSMTP: settings.enrutamientoSMTP,
    notificationTarget: settings.notificationTarget,
    metodosRecaudo: [...settings.metodosRecaudo],
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
];

const MODULE_ORDER = ["Matrícula Inicial", "Traspasos", "Configuración Empresa"];

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
