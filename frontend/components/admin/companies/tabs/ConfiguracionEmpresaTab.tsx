"use client";

import type { ReactNode } from "react";
import { ToggleSwitch } from "../ToggleSwitch";
import {
  METODOS_RECAUDO,
  NOTIFICATION_TARGET_LABELS,
  NOTIFICATION_TARGETS,
  SMTP_LABELS,
  type SettingsForm,
} from "../settingsForm";
import type { EnrutamientoSMTP, NotificationTarget } from "@/lib/api/types";

// Pestaña Configuración Empresa (HU #10194, AC2/AC4 / RF09-RF10). Baúl de firmas,
// enrutamiento SMTP, destinatario de notificaciones, métodos de recaudo + matriz
// OT (slot, endpoint propio).
export interface ConfiguracionEmpresaTabProps {
  form: SettingsForm;
  onChange: (patch: Partial<SettingsForm>) => void;
  otSlot?: ReactNode;
  fieldErrors?: Record<string, string>;
}

export function ConfiguracionEmpresaTab({
  form,
  onChange,
  otSlot,
  fieldErrors,
}: ConfiguracionEmpresaTabProps) {
  const toggleMetodo = (metodo: string, on: boolean) => {
    const next = on
      ? [...form.metodosRecaudo, metodo]
      : form.metodosRecaudo.filter((m) => m !== metodo);
    onChange({ metodosRecaudo: next });
  };

  return (
    <div className="space-y-4">
      <ToggleSwitch
        id="baulFirmasActivo"
        label="Baúl de firmas activo"
        description="Guarda de forma segura las firmas digitales de la compañía para reutilizarlas al firmar los documentos de cada trámite, sin tener que capturarlas en cada radicación."
        checked={form.baulFirmasActivo}
        onChange={(v) => onChange({ baulFirmasActivo: v })}
      />

      <div>
        <label htmlFor="enrutamientoSMTP" className="mb-1 block text-xs font-semibold">
          Enrutamiento de notificaciones
        </label>
        <select
          id="enrutamientoSMTP"
          value={form.enrutamientoSMTP}
          onChange={(e) => onChange({ enrutamientoSMTP: e.target.value as EnrutamientoSMTP })}
          className="w-full max-w-xs rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF]"
          style={{ borderColor: fieldErrors?.enrutamientoSMTP ? "#FF4E00" : "#DFE5ED" }}
        >
          {(Object.keys(SMTP_LABELS) as EnrutamientoSMTP[]).map((value) => (
            <option key={value} value={value}>
              {SMTP_LABELS[value]}
            </option>
          ))}
        </select>
        <p className="mt-1 max-w-md text-[11px] opacity-60">
          Canal por el que salen los correos de notificación. «Colas FLIT» los envía con la
          infraestructura de correo de FLIT; «API Renting cliente» los entrega a través del sistema
          propio de la compañía.
        </p>
        {fieldErrors?.enrutamientoSMTP && (
          <p className="mt-1 text-[11px] font-medium" style={{ color: "#FF4E00" }} role="alert">
            {fieldErrors.enrutamientoSMTP}
          </p>
        )}
      </div>

      <div>
        <label htmlFor="notificationTarget" className="mb-1 block text-xs font-semibold">
          Destinatario de notificaciones
        </label>
        <select
          id="notificationTarget"
          value={form.notificationTarget}
          onChange={(e) => onChange({ notificationTarget: e.target.value as NotificationTarget })}
          className="w-full max-w-xs rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF]"
          style={{ borderColor: fieldErrors?.notificationTarget ? "#FF4E00" : "#DFE5ED" }}
        >
          {NOTIFICATION_TARGETS.map((value) => (
            <option key={value} value={value}>
              {NOTIFICATION_TARGET_LABELS[value]}
            </option>
          ))}
        </select>
        <p className="mt-1 max-w-md text-[11px] opacity-60">
          Quién recibe los avisos del avance de cada trámite: el comprador del vehículo, el radicador
          que gestiona el trámite, o nadie si eliges «Sin notificaciones».
        </p>
        {fieldErrors?.notificationTarget && (
          <p className="mt-1 text-[11px] font-medium" style={{ color: "#FF4E00" }} role="alert">
            {fieldErrors.notificationTarget}
          </p>
        )}
      </div>

      <fieldset>
        <legend className="text-xs font-semibold">Métodos de recaudo</legend>
        <p className="mb-2 mt-0.5 max-w-md text-[11px] opacity-60">
          Medios habilitados para cobrar a los usuarios los costos del trámite: la pasarela de pagos
          de FLIT, el recaudo a través del organismo de tránsito (OT) u otros acordados con la
          compañía.
        </p>
        <div className="flex flex-wrap gap-3">
          {METODOS_RECAUDO.map((metodo) => {
            const checked = form.metodosRecaudo.includes(metodo);
            return (
              <label
                key={metodo}
                className="flex cursor-pointer items-center gap-2 rounded-lg border px-3 py-2 text-xs"
                style={{ borderColor: "#DFE5ED" }}
              >
                <input
                  type="checkbox"
                  checked={checked}
                  onChange={(e) => toggleMetodo(metodo, e.target.checked)}
                  className="h-4 w-4 accent-[#557EFF]"
                />
                {metodo}
              </label>
            );
          })}
        </div>
        {fieldErrors?.metodosRecaudo && (
          <p className="mt-1 text-[11px] font-medium" style={{ color: "#FF4E00" }} role="alert">
            {fieldErrors.metodosRecaudo}
          </p>
        )}
      </fieldset>

      {otSlot && (
        <div className="rounded-2xl border p-4" style={{ borderColor: "#DFE5ED" }}>
          {otSlot}
        </div>
      )}
    </div>
  );
}
