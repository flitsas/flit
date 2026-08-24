"use client";

import type { ReactNode } from "react";
import { ToggleSwitch } from "../ToggleSwitch";
import { ConsultaProvidersSection } from "../ConsultaProvidersSection";
import { AvaluoProvidersSection } from "../AvaluoProvidersSection";
import {
  FINES_QUERY_SOURCE_LABELS,
  FINES_QUERY_SOURCES,
  METODOS_RECAUDO,
  NOTIFICATION_TARGET_LABELS,
  NOTIFICATION_TARGETS,
  SMTP_LABELS,
  type SettingsForm,
} from "../settingsForm";
import type {
  EnrutamientoSMTP,
  FinesQuerySource,
  NotificationTarget,
} from "@/lib/api/types";

// Pestaña Configuración Empresa (HU #10194, AC2/AC4 / RF09-RF10). Baúl de firmas,
// enrutamiento SMTP, destinatario de notificaciones, métodos de recaudo + tabla
// consolidada de Organismos de Tránsito (grant, bloqueos y restricciones de consulta
// scoped por OT desde un menú de acciones — endpoint propio, fuera del PUT atómico).
export interface ConfiguracionEmpresaTabProps {
  form: SettingsForm;
  onChange: (patch: Partial<SettingsForm>) => void;
  /** Tabla consolidada de Organismos de Tránsito (grant + bloqueos + restricciones). */
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
      <fieldset>
        <legend className="text-xs font-semibold">Parámetros de firma</legend>
        <p className="mb-2 mt-0.5 max-w-md text-[11px] opacity-60">
          Controla cómo se firman los documentos de cada trámite. Los cambios aplican solo a las
          radicaciones nuevas: las que ya están en curso conservan la configuración con la que se
          iniciaron.
        </p>
        <ToggleSwitch
          id="baulFirmasActivo"
          label="Firma precargada (baúl)"
          description="Guarda de forma segura las firmas digitales de la compañía en el baúl para reutilizarlas al firmar los documentos de cada trámite, sin tener que capturarlas en cada radicación."
          checked={form.baulFirmasActivo}
          onChange={(v) => onChange({ baulFirmasActivo: v })}
        />
      </fieldset>

      <ToggleSwitch
        id="preasignacionPlacaActiva"
        label="Preasignación de placa activa"
        description="Habilita la ruta de placa preasignada para matrícula inicial: los organismos de tránsito activos de esta compañía podrán asignarle rangos de placas, y al radicar se podrá seleccionar la placa del rango asignado."
        checked={form.preasignacionPlacaActiva}
        onChange={(v) => onChange({ preasignacionPlacaActiva: v })}
      />

      <ToggleSwitch
        id="validarSoatConRunt"
        label="Validar SOAT ante el RUNT al procesar"
        description="Al procesar un trámite en sub-estado Asignado se consulta el SOAT en el RUNT. Con la opción activa, si el RUNT no reporta un SOAT vigente el hallazgo solo se informa y el trámite continúa. Desactivada, el trámite no avanza y se muestra el error."
        checked={form.validarSoatConRunt}
        onChange={(v) => onChange({ validarSoatConRunt: v })}
      />

      <ToggleSwitch
        id="plateFlowSkipToTerminado"
        label="Omitir proceso del gestor (placa → Terminado)"
        description="Si al radicar ya hay placa completa o del rango, el trámite pasa directo a Terminado (sin paso Asignado ni checks del gestor). Si está desactivado, el gestor debe procesar Asignado → Terminado antes de que el OT apruebe."
        checked={form.plateFlowSkipToTerminado}
        onChange={(v) => onChange({ plateFlowSkipToTerminado: v })}
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

      <ToggleSwitch
        id="avisosCambioEstadoActivos"
        label="Avisos de correo al cambio de estado"
        description="Cuando está activo, se notifica por correo al ciudadano (y a la empresa / representante legal si aplica) al aprobar o rechazar un trámite. Si se apaga, los avisos quedan en cola y se envían al reactivar."
        checked={form.avisosCambioEstadoActivos}
        onChange={(v) => onChange({ avisosCambioEstadoActivos: v })}
      />

      {/*
        HU #11686 — el panel de documentos personalizados (HU #11315, Feature #11309, ADR-0042) deja de
        ser visible para SuperAdmin y AdminCompany, los dos únicos roles de esta superficie.
        La API NO se cierra: los documentos ya cargados siguen aplicándose en la generación documental.
        Consecuencia aceptada por el PO humano el 2026-08-20 y registrada en el ADR-0050.
        El componente `PersonalizedDocumentsPanel` se conserva a propósito, sin montar.
      */}

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

      <fieldset>
        <legend className="text-xs font-semibold">Fuente de comparendos</legend>
        <p className="mb-2 mt-0.5 max-w-md text-[11px] opacity-60">
          Dónde se consultan los comparendos de la compañía. «Interna» usa el módulo de
          comparendos de FLIT con la fuente base cargada en la plataforma; «Externa» consulta en
          línea al SIMIT (regla especial del SIMIT). Esta opción se aplicará al flujo de trámite en
          una entrega posterior.
        </p>
        <div className="flex flex-wrap gap-3" role="radiogroup" aria-label="Fuente de comparendos">
          {FINES_QUERY_SOURCES.map((value) => {
            const checked = form.finesQuerySource === value;
            return (
              <label
                key={value}
                className="flex cursor-pointer items-center gap-2 rounded-lg border px-3 py-2 text-xs"
                style={checked ? { borderColor: "#557EFF" } : undefined}
              >
                <input
                  type="radio"
                  name="finesQuerySource"
                  value={value}
                  checked={checked}
                  onChange={() => onChange({ finesQuerySource: value as FinesQuerySource })}
                  className="h-4 w-4 accent-[#557EFF]"
                />
                {FINES_QUERY_SOURCE_LABELS[value]}
              </label>
            );
          })}
        </div>
        {fieldErrors?.finesQuerySource && (
          <p className="mt-1 text-[11px] font-medium" style={{ color: "#FF4E00" }} role="alert">
            {fieldErrors.finesQuerySource}
          </p>
        )}
      </fieldset>

      <ConsultaProvidersSection form={form} onChange={onChange} fieldErrors={fieldErrors} />

      <AvaluoProvidersSection form={form} onChange={onChange} fieldErrors={fieldErrors} />

      {otSlot && (
        <div className="rounded-2xl border p-4">
          {otSlot}
        </div>
      )}
    </div>
  );
}
