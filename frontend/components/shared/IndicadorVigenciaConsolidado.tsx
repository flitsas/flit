'use client';

import type { ReactNode } from 'react';
import type { ConsolidadoVigencia } from '@/lib/api/types/procedure-runtime';
import {
  COPY_VIGENCIA,
  describirVigenciaConsolidado,
  type DocumentoConsolidado,
  type VistaVigenciaConsolidado,
} from '@/lib/tramites/vigencia-consolidado';

export interface IndicadorVigenciaConsolidadoProps {
  /** Vigencia tal como la expone el backend (HU #12791). `null`/`undefined` ⇒ no se pinta nada. */
  vigencia: ConsolidadoVigencia | null | undefined;
  /** Qué consolidado describe: el del wizard (default) o el maestro del OT. */
  documento?: DocumentoConsolidado;
  /** `compacta` = celda de tabla; `completa` = cabecera/barra de un visor. Default `completa`. */
  variante?: 'compacta' | 'completa';
  /** Override de la leyenda de «desactualizado» (consola OT: «se reconstruirá al abrirlo»). */
  leyendaDesactualizado?: string;
  /**
   * HU #12793 (AC3) — ISO de radicación Quipux cuando el OT consulta en read-only: el indicador
   * pasa a «Versión radicada» con esa fecha. Omitido/`null` = comportamiento de la HU #12792.
   */
  radicadoEn?: string | null;
  /** Hueco para avisos adicionales junto al indicador (p. ej. fallo de regeneración, #12799). */
  children?: ReactNode;
  className?: string;
}

/**
 * HU #12792 (Épica #12760) — indicador de vigencia del consolidado: punto de color + texto + fecha
 * de la última generación en hora Colombia. Compartido por el listado del gestor, el
 * `ExpedienteVisor` y las HUs siguientes de la épica (#12793, #12794, #12799).
 *
 * Diseño (`flit-design-guardian` / `flit-detalle-tramite`): el verde #70CF3A y el gris #59677D son
 * tokens de estado y van SOLO en el punto; el texto va en tintas con contraste AA
 * (`--flit-success-ink`, #59677D). «Definitivo» es un badge soft (`{color}22`), como los del
 * detalle. No depende del color: cada estado lleva rótulo visible y `aria-label` completo.
 *
 * Uso de ejemplo:
 *   <IndicadorVigenciaConsolidado vigencia={item.consolidadoWizard} variante="compacta" />
 *   <IndicadorVigenciaConsolidado vigencia={maestro} documento="maestro"
 *     leyendaDesactualizado="Se reconstruirá al abrirlo">{avisoFallo}</IndicadorVigenciaConsolidado>
 */
export function IndicadorVigenciaConsolidado({
  vigencia,
  documento = 'wizard',
  variante = 'completa',
  leyendaDesactualizado,
  radicadoEn,
  children,
  className,
}: IndicadorVigenciaConsolidadoProps) {
  const vista = describirVigenciaConsolidado(vigencia, {
    documento,
    leyendaDesactualizado,
    radicadoEn,
  });
  if (!vista) return null;

  return variante === 'compacta' ? (
    <IndicadorCompacto vista={vista} className={className}>
      {children}
    </IndicadorCompacto>
  ) : (
    <IndicadorCompleto vista={vista} className={className}>
      {children}
    </IndicadorCompleto>
  );
}

function Punto({ vista, size }: { vista: VistaVigenciaConsolidado; size: number }) {
  return (
    <span
      aria-hidden="true"
      data-testid="vigencia-consolidado-punto"
      className="inline-block shrink-0 rounded-full"
      style={{
        width: size,
        height: size,
        background: vista.puntoHueco ? 'transparent' : vista.colorPunto,
        border: `2px solid ${vista.colorPunto}`,
      }}
    />
  );
}

/** Vigente y radicado (HU #12793) comparten la tinta verde de éxito. */
function tintaExito(vista: VistaVigenciaConsolidado): boolean {
  return vista.estado === 'vigente' || vista.estado === 'radicado';
}

/** Tinta del rótulo: la verde es variable de tema; la neutra necesita su versión oscura. */
function claseTinta(vista: VistaVigenciaConsolidado): string {
  return tintaExito(vista) ? '' : 'text-[#59677D] dark:text-white/70';
}

function estiloTinta(vista: VistaVigenciaConsolidado) {
  return tintaExito(vista) ? { color: vista.colorTexto } : undefined;
}

function BadgeDefinitivo() {
  return (
    <span
      data-testid="vigencia-consolidado-definitivo"
      className="inline-flex items-center rounded-full px-2.5 py-0.5 text-[10px] font-semibold"
      style={{ background: '#70CF3A22', color: 'var(--flit-success-ink)' }}
    >
      {COPY_VIGENCIA.definitivo}
    </span>
  );
}

function IndicadorCompacto({
  vista,
  className,
  children,
}: {
  vista: VistaVigenciaConsolidado;
  className?: string;
  children?: ReactNode;
}) {
  // Detalle bajo el rótulo: la fecha en vigente; la leyenda («Pendiente de regenerar» / «Aún no se
  // ha generado») en los otros dos. La fecha de la última generación del desactualizado queda en el
  // `aria-label`/`title`: en la celda no cabe una tercera línea.
  const detalle = tintaExito(vista) ? vista.fecha : vista.leyenda;
  // Los avisos extra (`children`) quedan FUERA del grupo: tienen su propio rol/nombre y no deben
  // heredar el `aria-label` del indicador.
  return (
    <span
      data-testid="vigencia-consolidado"
      data-estado={vista.estado}
      data-variante="compacta"
      className={`inline-flex min-w-0 flex-col items-start gap-0.5 ${className ?? ''}`}
    >
      <span
        role="group"
        aria-label={vista.ariaLabel}
        title={vista.ariaLabel}
        className="inline-flex min-w-0 flex-col items-start gap-0.5"
      >
        <span className="inline-flex min-w-0 items-center gap-1.5">
          <Punto vista={vista} size={8} />
          <span
            className={`truncate text-[11px] font-semibold leading-none ${claseTinta(vista)}`}
            style={estiloTinta(vista)}
          >
            {vista.documento} {vista.etiqueta.toLowerCase()}
          </span>
          {vista.definitivo ? <BadgeDefinitivo /> : null}
        </span>
        {detalle ? (
          <span className="truncate pl-[14px] text-[10px] font-medium leading-none text-[#59677D] dark:text-white/70">
            {detalle}
          </span>
        ) : null}
      </span>
      {children}
    </span>
  );
}

function IndicadorCompleto({
  vista,
  className,
  children,
}: {
  vista: VistaVigenciaConsolidado;
  className?: string;
  children?: ReactNode;
}) {
  const secundario = [vista.leyenda, vista.textoFecha].filter(Boolean).join(' · ');
  return (
    <div
      data-testid="vigencia-consolidado"
      data-estado={vista.estado}
      data-variante="completa"
      className={`flex flex-col gap-2 ${className ?? ''}`}
    >
      <div
        role="status"
        aria-label={vista.ariaLabel}
        className="flex flex-wrap items-center gap-x-2 gap-y-1 rounded-xl border bg-white px-3 py-2 dark:bg-transparent"
        style={{ borderColor: '#DFE5ED' }}
      >
        <Punto vista={vista} size={10} />
        <span className={`text-xs font-bold ${claseTinta(vista)}`} style={estiloTinta(vista)}>
          {vista.documento}: {vista.etiqueta}
        </span>
        {secundario ? (
          <span className="text-xs text-[#59677D] dark:text-white/70">{secundario}</span>
        ) : null}
        {vista.definitivo ? <BadgeDefinitivo /> : null}
      </div>
      {children}
    </div>
  );
}
