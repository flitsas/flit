"use client";

import type { ReactNode } from "react";
import { ToggleSwitch } from "../ToggleSwitch";
import { ConsultaProvidersSection } from "../ConsultaProvidersSection";
import { AvaluoProvidersSection } from "../AvaluoProvidersSection";
import {
  FINES_QUERY_SOURCE_LABELS,
  FINES_QUERY_SOURCES,
  METODOS_RECAUDO,
  SMTP_LABELS,
  type SettingsForm,
} from "../settingsForm";
import type { EnrutamientoSMTP, FinesQuerySource } from "@/lib/api/types";

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

  const extraEmailError =
    fieldErrors?.extraEmail ?? fieldErrors?.["destinatariosNotificacion.extraEmail"];

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
        id="avisosAprobacionActivos"
        label="Avisos al aprobar trámite"
        description="Cuando está activo, se envía el correo de trámite aprobado. Si se apaga, los avisos de aprobación quedan en cola y se envían al reactivar."
        checked={form.avisosAprobacionActivos}
        onChange={(v) => onChange({ avisosAprobacionActivos: v })}
      />

      <ToggleSwitch
        id="avisosRechazoActivos"
        label="Avisos al rechazar trámite"
        description="Cuando está activo, se envía el correo de trámite rechazado. Si se apaga, los avisos de rechazo quedan en cola y se envían al reactivar."
        checked={form.avisosRechazoActivos}
        onChange={(v) => onChange({ avisosRechazoActivos: v })}
      />

      <fieldset>
        <legend className="text-xs font-semibold">Destinatarios de avisos de estado</legend>
        <p className="mb-2 mt-0.5 max-w-md text-[11px] opacity-60">
          El aviso llega a los perfiles encendidos. Comprador y vendedor/propietario cubren persona
          natural, jurídica (empresa y representante legal) y locatario si existe. Si hay más de un
          correo, el comprador va como destinatario principal y el resto en copia oculta.
        </p>
        <div className="flex max-w-md flex-col gap-2">
          {(
            [
              ["destinatarioComprador", "Comprador", form.destinatarioComprador],
              [
                "destinatarioVendedorOPropietario",
                "Vendedor / propietario",
                form.destinatarioVendedorOPropietario,
              ],
              ["destinatarioRadicador", "Radicador (quien crea el trámite)", form.destinatarioRadicador],
            ] as const
          ).map(([key, label, checked]) => (
            <label
              key={key}
              className="flex cursor-pointer items-center gap-2 rounded-lg border px-3 py-2 text-xs"
            >
              <input
                id={key}
                type="checkbox"
                checked={checked}
                onChange={(e) => onChange({ [key]: e.target.checked } as Partial<SettingsForm>)}
                className="h-4 w-4 accent-[#557EFF]"
              />
              {label}
            </label>
          ))}
        </div>
        <label htmlFor="destinatarioExtraEmail" className="mb-1 mt-3 block text-xs font-semibold">
          Correo adicional
        </label>
        <input
          id="destinatarioExtraEmail"
          type="email"
          value={form.destinatarioExtraEmail}
          onChange={(e) => onChange({ destinatarioExtraEmail: e.target.value })}
          placeholder="opcional@empresa.com"
          className="w-full max-w-md rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF]"
          style={{ borderColor: extraEmailError ? "#FF4E00" : "#DFE5ED" }}
        />
        {extraEmailError && (
          <p className="mt-1 text-[11px] font-medium" style={{ color: "#FF4E00" }} role="alert">
            {extraEmailError}
          </p>
        )}
      </fieldset>

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
