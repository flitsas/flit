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
