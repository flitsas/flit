"use client";

import { useEffect, useState, type MouseEvent, type ReactNode } from "react";
import { STATE_ACTIVADO as ESTADO_ACTIVADO, ToggleSwitch, type ToggleSwitchState } from "../ToggleSwitch";
import { ConsultaProvidersSection } from "../ConsultaProvidersSection";
import { AvaluoProvidersSection } from "../AvaluoProvidersSection";
import { ClampedText, ConfigCard, FIELD_CLASS, FIELD_LABEL, HELP_TEXT, OptionCard, SubBlock } from "../ConfigUi";
import {
  FINES_QUERY_SOURCE_LABELS,
  FINES_QUERY_SOURCES,
  METODOS_RECAUDO,
  SMTP_LABELS,
  type SettingsForm,
} from "../settingsForm";
import type { EnrutamientoSMTP, FinesQuerySource } from "@/lib/api/types";

// Pestaña Configuración Empresa (HU #10194, AC2/AC4 / RF09-RF10). Agrupada por intención del usuario:
// Firma y documentos · Notificaciones · Cobro y recaudo · Consultas y fuentes · Organismos de tránsito
// (tabla consolidada: grant, bloqueos y restricciones de consulta scoped por OT desde un menú de acciones —
// endpoint propio, fuera del PUT atómico). Cada bloque es una tarjeta; el índice de anclas salta a cada una.
export interface ConfiguracionEmpresaTabProps {
  form: SettingsForm;
  onChange: (patch: Partial<SettingsForm>) => void;
  /** Tabla consolidada de Organismos de Tránsito (grant + bloqueos + restricciones). */
  otSlot?: ReactNode;
  fieldErrors?: Record<string, string>;
}

/** Bloques de la pestaña, en orden de aparición: alimentan el índice de anclas. */
const BLOCKS = [
  { id: "cfg-firma", label: "Firma y documentos" },
  { id: "cfg-modulos", label: "Módulos del dashboard" },
  { id: "cfg-notificaciones", label: "Notificaciones" },
  { id: "cfg-recaudo", label: "Cobro y recaudo" },
  { id: "cfg-consultas", label: "Consultas y fuentes" },
  { id: "cfg-ot", label: "Organismos de tránsito" },
] as const;

// El valor guardado no cambia: solo el texto de estado que se lee junto al switch.
const ESTADO_SOAT: ToggleSwitchState = { on: "Permitido", off: "Bloqueado", onTone: "success", offTone: "warning" };
const ESTADO_AVISO: ToggleSwitchState = { on: "Se envía", off: "En pausa", onTone: "success", offTone: "neutral" };

const FINES_DESCRIPTIONS: Record<FinesQuerySource, string> = {
  internal: "Módulo de comparendos de FLIT con la fuente base cargada en la plataforma.",
  external: "Consulta en línea al SIMIT (regla especial del SIMIT).",
};

/** Desplaza hasta el bloque y le da el foco (el bloque lleva tabIndex -1) para que el teclado siga ahí. */
function goToBlock(e: MouseEvent<HTMLAnchorElement>, id: string, onActive: (id: string) => void) {
  e.preventDefault();
  const el = document.getElementById(id);
  if (!el) return;
  onActive(id);
  el.scrollIntoView?.({ behavior: "smooth", block: "start" });
  el.focus({ preventScroll: true });
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

  const visibleBlocks = BLOCKS.filter((b) => b.id !== "cfg-ot" || Boolean(otSlot));
  const [activeBlock, setActiveBlock] = useState<string>(visibleBlocks[0].id);

  // Marca en el índice el bloque que se está leyendo (si el navegador soporta IntersectionObserver).
  useEffect(() => {
    if (typeof IntersectionObserver === "undefined") return;
    const observer = new IntersectionObserver(
      (entries) => {
        const visible = entries
          .filter((e) => e.isIntersecting)
          .sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top)[0];
        if (visible) setActiveBlock(visible.target.id);
      },
      { rootMargin: "-10% 0px -70% 0px" },
    );
    for (const b of BLOCKS) {
      const el = document.getElementById(b.id);
      if (el) observer.observe(el);
    }
    return () => observer.disconnect();
  }, [otSlot]);

  return (
    <div className="space-y-6">
      {/* Índice de anclas: la página es larga; cada chip salta a su tarjeta. */}
      {visibleBlocks.length >= 4 && (
        <nav aria-label="Bloques de configuración de la empresa" className="flex flex-wrap items-center gap-2">
          <span className={`font-semibold ${HELP_TEXT}`}>Ir a:</span>
          {visibleBlocks.map((b) => {
            const current = activeBlock === b.id;
            return (
              <a
                key={b.id}
                href={`#${b.id}`}
                aria-label={`Ir al bloque ${b.label}`}
                aria-current={current ? "location" : undefined}
                onClick={(e) => goToBlock(e, b.id, setActiveBlock)}
                className={`inline-flex min-h-[40px] items-center rounded-full border px-4 text-xs font-semibold transition focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF] ${
                  current
                    ? "border-[#557EFF] bg-[rgba(85,126,255,0.12)] text-[#557EFF]"
                    : "border-[#DFE5ED] bg-white text-[#162744] hover:bg-[rgba(85,126,255,0.06)] dark:border-white/10 dark:bg-transparent dark:text-white"
                }`}
              >
                {b.label}
              </a>
            );
          })}
        </nav>
      )}

      <ConfigCard
        id="cfg-firma"
        title="Firma y documentos"
        description="Controla cómo se firman los documentos de cada trámite. Los cambios aplican solo a las radicaciones nuevas: las que ya están en curso conservan la configuración con la que se iniciaron."
      >
        <div className="grid gap-4 lg:grid-cols-2">
          <ToggleSwitch
            id="baulFirmasActivo"
            label="Firma precargada (baúl)"
            description="Guarda de forma segura las firmas digitales de la compañía en el baúl para reutilizarlas al firmar los documentos de cada trámite, sin tener que capturarlas en cada radicación."
            hint="Solo aplica a radicaciones nuevas."
            state={ESTADO_ACTIVADO}
            checked={form.baulFirmasActivo}
            onChange={(v) => onChange({ baulFirmasActivo: v })}
          />
        </div>
      </ConfigCard>

      <ConfigCard
        id="cfg-modulos"
        title="Módulos activos del dashboard"
        description="Decide qué servicios ve esta compañía en su dashboard. No hay regla de «al menos uno activo»: se pueden apagar los 3 sin bloquear el guardado."
      >
        <div className="grid gap-4 lg:grid-cols-3">
          <ToggleSwitch
            id="tramitesModuleEnabled"
            label="Módulo de Trámites"
            description="Habilita el módulo de Trámites en el dashboard de la compañía."
            state={ESTADO_ACTIVADO}
            checked={form.tramitesModuleEnabled}
            onChange={(v) => onChange({ tramitesModuleEnabled: v })}
          />
          <ToggleSwitch
            id="comparendosModuleEnabled"
            label="Módulo de Comparendos"
            description="Habilita el módulo de Comparendos en el dashboard de la compañía."
            state={ESTADO_ACTIVADO}
            checked={form.comparendosModuleEnabled}
            onChange={(v) => onChange({ comparendosModuleEnabled: v })}
          />
          <ToggleSwitch
            id="resolucionesModuleEnabled"
            label="Módulo de Resoluciones"
            description="Habilita el módulo de Resoluciones en el dashboard de la compañía."
            state={ESTADO_ACTIVADO}
            checked={form.resolucionesModuleEnabled}
            onChange={(v) => onChange({ resolucionesModuleEnabled: v })}
          />
        </div>
      </ConfigCard>

      {/* HU #12851 (Feature #12846) — el switch "Preasignación de placa activa" se retiró: la
          consola de rangos ya no existe (HU-A2) y la ruta de placa preasignada de la compañía se
          apaga en backend (HU-A5). El campo preasignacionPlacaActiva se sigue enviando en el PUT
          (se ignora del lado del backend) para no romper el contrato de TenantSettingsUpdate. */}
      <ConfigCard
        id="cfg-notificaciones"
        title="Notificaciones"
        description="Por dónde salen los correos de estado y quién los recibe."
      >
        <div className="max-w-sm">
          <label htmlFor="enrutamientoSMTP" className={FIELD_LABEL}>
            Enrutamiento de notificaciones
          </label>
          <select
            id="enrutamientoSMTP"
            value={form.enrutamientoSMTP}
            onChange={(e) => onChange({ enrutamientoSMTP: e.target.value as EnrutamientoSMTP })}
            className={FIELD_CLASS}
            style={{ borderColor: fieldErrors?.enrutamientoSMTP ? "#FF4E00" : "#DFE5ED" }}
          >
            {(Object.keys(SMTP_LABELS) as EnrutamientoSMTP[]).map((value) => (
              <option key={value} value={value}>
                {SMTP_LABELS[value]}
              </option>
            ))}
          </select>
          <ClampedText
            className="mt-1"
            text="Canal por el que salen los correos de notificación. «Colas FLIT» los envía con la infraestructura de correo de FLIT; «API Renting cliente» los entrega a través del sistema propio de la compañía."
          />
          {fieldErrors?.enrutamientoSMTP && (
            <p className="mt-1 text-xs font-medium" style={{ color: "#FF4E00" }} role="alert">
              {fieldErrors.enrutamientoSMTP}
            </p>
          )}
        </div>

        <div className="grid gap-4 lg:grid-cols-2">
          <ToggleSwitch
            id="avisosAprobacionActivos"
            label="Avisos al aprobar trámite"
            description="Cuando está activo, se envía el correo de trámite aprobado. Si se apaga, los avisos de aprobación quedan en cola y se envían al reactivar."
            state={ESTADO_AVISO}
            checked={form.avisosAprobacionActivos}
            onChange={(v) => onChange({ avisosAprobacionActivos: v })}
          />
          <ToggleSwitch
            id="avisosRechazoActivos"
            label="Avisos al rechazar trámite"
            description="Cuando está activo, se envía el correo de trámite rechazado. Si se apaga, los avisos de rechazo quedan en cola y se envían al reactivar."
            state={ESTADO_AVISO}
            checked={form.avisosRechazoActivos}
            onChange={(v) => onChange({ avisosRechazoActivos: v })}
          />
        </div>

        <fieldset>
          <legend className="text-sm font-semibold text-[#162744] dark:text-white">Destinatarios de avisos de estado</legend>
          <ClampedText
            className="mb-3 mt-0.5"
            text="El aviso llega a los perfiles encendidos. Comprador y vendedor/propietario cubren persona natural, jurídica (empresa y representante legal) y locatario si existe. Si hay más de un correo, el comprador va como destinatario principal y el resto en copia oculta."
          />
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
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
              <OptionCard
                key={key}
                type="checkbox"
                label={label}
                checked={checked}
                onChange={(on) => onChange({ [key]: on } as Partial<SettingsForm>)}
              />
            ))}
          </div>
          <div className="mt-4 max-w-md">
            <label htmlFor="destinatarioExtraEmail" className={FIELD_LABEL}>
              Correo adicional
            </label>
            <input
              id="destinatarioExtraEmail"
              type="email"
              value={form.destinatarioExtraEmail}
              onChange={(e) => onChange({ destinatarioExtraEmail: e.target.value })}
              placeholder="opcional@empresa.com"
              className={FIELD_CLASS}
              style={{ borderColor: extraEmailError ? "#FF4E00" : "#DFE5ED" }}
            />
            {extraEmailError && (
              <p className="mt-1 text-xs font-medium" style={{ color: "#FF4E00" }} role="alert">
                {extraEmailError}
              </p>
            )}
          </div>
        </fieldset>
      </ConfigCard>

      <ConfigCard
        id="cfg-recaudo"
        title="Cobro y recaudo"
        description="Medios habilitados para cobrar a los usuarios los costos del trámite: la pasarela de pagos de FLIT, el recaudo a través del organismo de tránsito (OT) u otros acordados con la compañía."
      >
        <div role="group" aria-label="Métodos de recaudo" className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {METODOS_RECAUDO.map((metodo) => (
            <OptionCard
              key={metodo}
              type="checkbox"
              label={metodo}
              checked={form.metodosRecaudo.includes(metodo)}
              onChange={(on) => toggleMetodo(metodo, on)}
            />
          ))}
        </div>
        {fieldErrors?.metodosRecaudo && (
          <p className="text-xs font-medium" style={{ color: "#FF4E00" }} role="alert">
            {fieldErrors.metodosRecaudo}
          </p>
        )}
      </ConfigCard>

      {/* Bug #13194 P3 — solo texto: la semántica (ON = continúa con advertencia, OFF = bloquea) no
          cambia; el rótulo anterior sugería lo contrario. */}
      <ConfigCard
        id="cfg-consultas"
        title="Consultas y fuentes"
        description="De dónde salen los datos que consulta cada trámite: RUNT, comparendos y avalúos."
      >
        <SubBlock title="SOAT">
          <ToggleSwitch
            id="validarSoatConRunt"
            label="Permitir enviar al OT sin SOAT vigente en el RUNT"
            description="Activo: se consulta el RUNT y, si no reporta SOAT vigente, el trámite continúa con una advertencia. Inactivo: sin SOAT vigente en el RUNT, el envío al OT se bloquea."
            state={ESTADO_SOAT}
            checked={form.validarSoatConRunt}
            onChange={(v) => onChange({ validarSoatConRunt: v })}
          />
        </SubBlock>

        <SubBlock title="Fuente de comparendos" separated>
          <ClampedText text="Dónde se consultan los comparendos de la compañía. «Interna» usa el módulo de comparendos de FLIT con la fuente base cargada en la plataforma; «Externa» consulta en línea al SIMIT (regla especial del SIMIT). Esta opción se aplicará al flujo de trámite en una entrega posterior." />
          <div className="grid gap-3 sm:grid-cols-2" role="radiogroup" aria-label="Fuente de comparendos">
            {FINES_QUERY_SOURCES.map((value) => (
              <OptionCard
                key={value}
                type="radio"
                name="finesQuerySource"
                label={FINES_QUERY_SOURCE_LABELS[value]}
                description={FINES_DESCRIPTIONS[value]}
                checked={form.finesQuerySource === value}
                onChange={() => onChange({ finesQuerySource: value as FinesQuerySource })}
              />
            ))}
          </div>
          {fieldErrors?.finesQuerySource && (
            <p className="text-xs font-medium" style={{ color: "#FF4E00" }} role="alert">
              {fieldErrors.finesQuerySource}
            </p>
          )}
        </SubBlock>

        <SubBlock title="Proveedores de consulta RUNT" separated>
          <ConsultaProvidersSection form={form} onChange={onChange} fieldErrors={fieldErrors} />
        </SubBlock>

        <SubBlock title="Proveedores de avalúos" separated>
          <AvaluoProvidersSection form={form} onChange={onChange} fieldErrors={fieldErrors} />
        </SubBlock>
      </ConfigCard>

      {otSlot && (
        <ConfigCard id="cfg-ot" ariaLabel="Organismos de tránsito">
          {otSlot}
        </ConfigCard>
      )}
    </div>
  );
}
