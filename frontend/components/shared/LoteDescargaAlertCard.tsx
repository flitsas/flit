'use client';

import { useId, type Ref } from 'react';
import {
  AlertTriangle,
  CheckCircle2,
  ChevronsRight,
  Clock,
  Download,
  FileArchive,
  Loader2,
  Network,
  XCircle,
} from 'lucide-react';
import type { StatusTone } from '@/components/atom/StatusBadge';
import { MENSAJE_REINTENTAR_DESCARGA } from '@/lib/api/consolidado-lotes-client';
import {
  ETIQUETA_ESTADO_LOTE,
  type LoteConsolidados,
} from '@/lib/api/types-consolidado-lotes';

/**
 * HU #13382 (épica #13216) — aviso global del lote de descarga masiva de consolidados.
 *
 * <p>Patrón base (`flit-design-guardian`): «tarjeta de alertas» FLIT, misma familia que
 * `AvisoFalloRegeneracion` — radio 12px, borde y fondo del tono semántico (`--badge-{tone}-*` de los
 * tokens), icono lucide 16px y CTA en pastilla. Ningún color en hex: todo sale del tono (AC2).</p>
 *
 * <p>Tonos (CF-19 / AC2): activo sin omitidos → info; con omitidos (activo o completado) → warning;
 * completado → success; fallido → danger (error); expirado y cancelado → neutral.</p>
 *
 * <p>Accesibilidad (AC6, WCAG 2.1 AA): el estado se anuncia en una región `role="status"`
 * `aria-live="polite"` que solo cambia de texto con el estado (el contador no se anuncia cada 4 s);
 * la barra es un `progressbar` con valores; cada parte tiene un `<button>` nativo («Descargar»,
 * nombre accesible «Descargar parte k de n») operable con Tab/Enter/Espacio; el estado no depende
 * solo del color (icono + texto).</p>
 *
 * <p>HU #13388 — en un lote en curso, botón «Cancelar» (nombre accesible «Cancelar la descarga»),
 * un clic, sin confirmación; deshabilitado mientras `cancelando`.
 * El lote cancelado se pinta neutro con «Descarga cancelada», sin partes ni botones; un 404/403 al
 * cancelar muestra «No se pudo cancelar la descarga» en tono error (`--badge-danger-*`).</p>
 *
 * <p>HU #13419 AC6 — lote de la vista de red: rótulo «Red» (toda la red) o «Red · {hija}» (acotado).
 * El contrato trae `alcanceHijaId`; el nombre lo resuelve el seguimiento (`useNombreHijaLote`, contra
 * `/network/children`) y llega en `nombreHija`. Si no se puede resolver (hija fuera de la red, lista
 * aún sin cargar o fallida) dice «Red · compañía de la red»: nunca «Red» a secas, que afirmaría que el
 * lote es de toda la red.</p>
 *
 * <p>Compacto y minimizable (registro diferido, 2026-10-13): cabecera en una línea (icono + estado +
 * % + minimizar); el título «Descarga masiva de consolidados» queda como nombre accesible (h2 sr-only
 * y `aria-label`) y tooltip; barra `h-1`; contador y «Cancelar» (texto en `--badge-danger-fg`) en
 * una fila; partes en filas con divisor y alto máximo (~3 filas) con scroll. Con `minimizado` la
 * tarjeta se desliza a la derecha (200 ms, `motion-safe`) y todo salvo la región viva queda inerte.</p>
 *
 * Uso de ejemplo:
 *   <LoteDescargaAlertCard lote={lote} expirado={expirado} onDescargarParte={(n) => descargar(n)}
 *     errorConsulta={errorConsulta} descargandoParte={null} onMinimizar={() => setMin(true)} />
 */

/** AC2/AC4 — texto aprobado del lote expirado. */
export const TEXTO_DESCARGA_EXPIRADA = 'Descarga expirada';
/** HU #13388 AC3 — texto aprobado del lote cancelado. */
export const TEXTO_DESCARGA_CANCELADA = 'Descarga cancelada';

const formatoMiles = (n: number) => n.toLocaleString('es-CO');

/** HU #13419 AC6 — rótulo del lote acotado a una hija cuyo nombre no se pudo resolver. */
export const NOMBRE_HIJA_DESCONOCIDA = 'compañía de la red';

/**
 * HU #13419 AC6 — rótulo del alcance de red del lote: `null` en el lote propio, «Red» en el de toda
 * la red y «Red · {nombreHija}» en el acotado (o «Red · compañía de la red» si el nombre no se conoce).
 */
export function etiquetaAlcanceLote(lote: LoteConsolidados, nombreHija?: string | null): string | null {
  if (lote.alcanceRed === 'red') return 'Red';
  if (lote.alcanceRed !== 'hija') return null;
  return `Red · ${nombreHija?.trim() || NOMBRE_HIJA_DESCONOCIDA}`;
}

/** AC2 — tono semántico (de la escala de `statusTones` / `StatusBadge`) del lote. */
export function toneLoteConsolidados(lote: LoteConsolidados, expirado: boolean): StatusTone {
  if (expirado || lote.estado === 'expirado' || lote.estado === 'cancelado') return 'neutral';
  if (lote.estado === 'fallido') return 'danger';
  if (lote.omitidos > 0) return 'warning';
  if (lote.estado === 'completado_con_omitidos') return 'warning';
  if (lote.estado === 'completado') return 'success';
  return 'info';
}

/** Frase del estado que se anuncia en la región viva (también nombre accesible de la pestaña). */
export function textoEstadoLote(lote: LoteConsolidados, expirado: boolean): string {
  if (lote.estado === 'cancelado') return TEXTO_DESCARGA_CANCELADA;
  if (expirado || lote.estado === 'expirado') return TEXTO_DESCARGA_EXPIRADA;
  switch (lote.estado) {
    case 'en_cola':
      return 'En cola';
    case 'en_proceso':
      return 'Descarga en proceso';
    case 'empaquetando':
      return 'Preparando los archivos ZIP';
    case 'completado':
      return 'Descarga lista';
    case 'completado_con_omitidos':
      return 'Descarga lista, con trámites omitidos';
    case 'fallido':
      return 'La descarga falló';
    default: {
      const etiqueta = ETIQUETA_ESTADO_LOTE[lote.estado as keyof typeof ETIQUETA_ESTADO_LOTE] ?? '';
      return etiqueta ? etiqueta.charAt(0).toUpperCase() + etiqueta.slice(1) : 'Descarga';
    }
  }
}

const ICONO: Record<StatusTone, typeof Clock> = {
  info: Clock,
  warning: AlertTriangle,
  success: CheckCircle2,
  danger: XCircle,
  neutral: FileArchive,
};

/** Icono del tono (check success / alerta warning / X danger / reloj info / archivo neutral), decorativo. */
export function IconoLote({ tone, className }: { tone: StatusTone; className?: string }) {
  const Icono = ICONO[tone];
  return <Icono className={className} aria-hidden="true" />;
}

/** El lote sigue en curso (cola, proceso o empaquetado) y no expiró. */
export function loteActivo(lote: LoteConsolidados, expirado: boolean): boolean {
  return !expirado && ['en_cola', 'en_proceso', 'empaquetando'].includes(lote.estado);
}

/**
 * Avance entero (truncado: no muestra 100 % antes de terminar) del lote en curso; `null` si el lote
 * ya no está activo o no tiene total (en ese caso no se pinta ni se anuncia %).
 */
export function porcentajeLote(lote: LoteConsolidados, expirado: boolean): number | null {
  if (!loteActivo(lote, expirado) || lote.total <= 0) return null;
  return Math.floor(Math.min(100, (lote.procesados / lote.total) * 100));
}

/** Nombre accesible y tooltip del aviso (el título ya no es texto visible). */
export const TITULO_DESCARGA_MASIVA = 'Descarga masiva de consolidados';

export interface LoteDescargaAlertCardProps {
  lote: LoteConsolidados;
  /** AC4 — expirado (por estado o pasadas 24 h desde el fin). */
  expirado: boolean;
  onDescargarParte: (numero: number) => void;
  /** AC5 — la última consulta falló: se muestran los datos anteriores y se avisa del reintento. */
  errorConsulta?: boolean;
  descargandoParte?: number | null;
  errorDescarga?: string | null;
  /** HU #13388 — cancela el lote en curso; omitido ⇒ no se pinta «Cancelar». */
  onCancelar?: () => void;
  /** HU #13388 AC2 — la cancelación está en curso: botón deshabilitado. */
  cancelando?: boolean;
  /** HU #13388 AC6 — error de la cancelación (404/403). */
  errorCancelacion?: string | null;
  /** HU #13419 AC6 — nombre de la hija de un lote de red acotado, si se resolvió. */
  nombreHija?: string | null;
  /** Minimizable — omitido ⇒ no se pinta el botón «Minimizar la descarga masiva». */
  onMinimizar?: () => void;
  /**
   * Minimizable — la tarjeta sale por la derecha y queda inerte (fuera del orden de tabulación y del
   * árbol accesible) salvo la región viva del estado, que sigue montada y anunciando (AC6).
   */
  minimizado?: boolean;
  /** Minimizable — recibe el botón «Minimizar» para devolverle el foco al restaurar (AC3). */
  botonMinimizarRef?: Ref<HTMLButtonElement>;
  className?: string;
}

export function LoteDescargaAlertCard({
  lote,
  expirado,
  onDescargarParte,
  errorConsulta = false,
  descargandoParte = null,
  errorDescarga = null,
  onCancelar,
  cancelando = false,
  errorCancelacion = null,
  nombreHija = null,
  onMinimizar,
  minimizado = false,
  botonMinimizarRef,
  className,
}: LoteDescargaAlertCardProps) {
  const cardId = useId();
  const alcance = etiquetaAlcanceLote(lote, nombreHija);
  const tone = toneLoteConsolidados(lote, expirado);
  const Icono = ICONO[tone];
  const terminado = lote.estado === 'completado' || lote.estado === 'completado_con_omitidos';
  const conPartes = terminado && !expirado && lote.partes.length > 0;
  const activo = loteActivo(lote, expirado);
  const porcentaje = porcentajeLote(lote, expirado);
  const totalPartes = lote.partes.length;
  // Minimizada: todo menos la región viva queda inerte y fuera del árbol accesible.
  const oculto = minimizado ? { inert: true, 'aria-hidden': true as const } : {};

  return (
    <section
      id={cardId}
      aria-label={TITULO_DESCARGA_MASIVA}
      title={TITULO_DESCARGA_MASIVA}
      data-testid="lote-descarga-card"
      data-tone={tone}
      data-minimizado={minimizado ? 'true' : 'false'}
      // Fondo opaco de tarjeta (`bg-card`, theme-aware) con el tinte del tono encima: el aviso flota
      // sobre el contenido y el tinte de `--badge-*-bg` es translúcido. Sombra = token `shadow.card`
      // de flit_design_tokens.json. Al minimizar se desliza a la derecha en ~200 ms solo si el
      // sistema permite movimiento (prefers-reduced-motion).
      className={`rounded-xl border bg-card px-3 py-2 text-xs shadow-[0_8px_24px_rgba(22,39,68,0.08)] motion-safe:transition-[transform,opacity] motion-safe:duration-200 motion-safe:ease-out motion-reduce:transition-none ${
        minimizado ? 'pointer-events-none translate-x-[calc(100%+1.5rem)] opacity-0' : 'translate-x-0 opacity-100'
      } ${className ?? ''}`}
      style={{
        backgroundImage: `linear-gradient(var(--badge-${tone}-bg), var(--badge-${tone}-bg))`,
        borderColor: `var(--badge-${tone}-border)`,
      }}
    >
      <h2 className="sr-only" {...oculto}>
        {TITULO_DESCARGA_MASIVA}
      </h2>
      <div data-testid="lote-descarga-cabecera" className="flex items-center gap-2">
        <Icono className="h-4 w-4 shrink-0" style={{ color: `var(--badge-${tone}-fg)` }} aria-hidden="true" />
        <p
          role="status"
          aria-live="polite"
          className="min-w-0 flex-1 font-semibold leading-tight"
          style={{ color: `var(--badge-${tone}-fg)` }}
        >
          {textoEstadoLote(lote, expirado)}
        </p>
        {porcentaje !== null ? (
          <span {...oculto} className="shrink-0 font-semibold tabular-nums" style={{ color: `var(--badge-${tone}-fg)` }}>
            {porcentaje}%
          </span>
        ) : null}
        {onMinimizar ? (
          <button
            {...oculto}
            ref={botonMinimizarRef}
            type="button"
            onClick={onMinimizar}
            aria-label="Minimizar la descarga masiva"
            aria-expanded={!minimizado}
            aria-controls={cardId}
            className="-my-1 -mr-1 inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-full text-flit-primary/70 transition hover:bg-flit-brand/10 hover:text-flit-brand focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand dark:text-white/70"
          >
            <ChevronsRight className="h-4 w-4" aria-hidden="true" />
          </button>
        ) : null}
      </div>

      <div {...oculto}>
        {alcance ? (
          <p
            data-testid="lote-alcance-red"
            className="mt-1 inline-flex max-w-full items-center gap-1 rounded-full border border-flit-brand/40 px-2 py-0.5 text-xs font-semibold text-flit-brand"
          >
            <Network className="h-3 w-3 shrink-0" aria-hidden="true" />
            <span className="sr-only">Alcance: </span>
            <span className="truncate" title={alcance}>
              {alcance}
            </span>
          </p>
        ) : null}

        {!expirado && lote.estado !== 'cancelado' ? (
          <div className="mt-1.5">
            {activo ? (
              <div
                role="progressbar"
                aria-label="Avance de la descarga"
                aria-valuemin={0}
                aria-valuemax={lote.total}
                aria-valuenow={lote.procesados}
                className="h-1 w-full overflow-hidden rounded-full bg-flit-gray dark:bg-white/10"
              >
                <div
                  className="h-full rounded-full transition-[width]"
                  style={{
                    width: `${lote.total > 0 ? Math.min(100, (lote.procesados / lote.total) * 100) : 0}%`,
                    background: `var(--badge-${tone}-fg)`,
                  }}
                />
              </div>
            ) : null}
            <div
              data-testid="lote-descarga-pie"
              className={`flex items-center justify-between gap-2 ${activo ? 'mt-1' : ''}`}
            >
              <p className="min-w-0 text-flit-primary/80 dark:text-white/80">
                <span className="font-semibold tabular-nums">
                  {formatoMiles(lote.procesados)} / {formatoMiles(lote.total)}
                </span>{' '}
                trámites procesados
                {lote.omitidos > 0 ? ` · ${formatoMiles(lote.omitidos)} omitido${lote.omitidos === 1 ? '' : 's'}` : ''}
              </p>
              {activo && onCancelar ? (
                <button
                  type="button"
                  onClick={onCancelar}
                  disabled={cancelando}
                  aria-busy={cancelando}
                  aria-label="Cancelar la descarga"
                  className="-mr-1 inline-flex shrink-0 items-center gap-1 rounded-full px-1.5 py-0.5 font-semibold transition hover:bg-[var(--badge-danger-bg)] focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand disabled:cursor-wait disabled:opacity-60"
                  style={{ color: 'var(--badge-danger-fg)' }}
                >
                  {cancelando ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> : null}
                  Cancelar
                </button>
              ) : null}
            </div>
          </div>
        ) : null}

        {lote.estado === 'fallido' && !expirado ? (
          <p className="mt-1.5 text-flit-primary/80 dark:text-white/80">{MENSAJE_REINTENTAR_DESCARGA}</p>
        ) : null}

        {conPartes ? (
          <ul
            aria-label="Partes de la descarga"
            className="mt-1.5 max-h-24 divide-y divide-flit-gray overflow-y-auto border-t border-flit-gray dark:divide-white/10 dark:border-white/10"
          >
            {lote.partes.map((parte) => {
              const descargando = descargandoParte === parte.numero;
              return (
                <li key={parte.numero} className="flex items-center justify-between gap-2 py-1">
                  <span className="min-w-0 truncate text-flit-primary dark:text-white" title={parte.nombreArchivo}>
                    {totalPartes > 1 ? `Parte ${parte.numero} de ${totalPartes}` : 'Archivo ZIP'} ·{' '}
                    {formatoMiles(parte.pdfs)} PDF
                  </span>
                  <button
                    type="button"
                    onClick={() => onDescargarParte(parte.numero)}
                    disabled={descargando}
                    aria-label={`Descargar parte ${parte.numero} de ${totalPartes}`}
                    className="inline-flex shrink-0 items-center gap-1 rounded-full border border-flit-brand/40 px-2 py-0.5 font-semibold text-flit-brand transition hover:bg-flit-brand/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand disabled:cursor-wait disabled:opacity-60"
                  >
                    {descargando ? (
                      <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
                    ) : (
                      <Download className="h-3.5 w-3.5" aria-hidden="true" />
                    )}
                    Descargar
                  </button>
                </li>
              );
            })}
          </ul>
        ) : null}

        {errorDescarga ? (
          <p role="alert" className="mt-1.5 font-semibold" style={{ color: 'var(--badge-danger-fg)' }}>
            {errorDescarga}
          </p>
        ) : null}

        {errorCancelacion ? (
          <p role="alert" className="mt-1.5 font-semibold" style={{ color: 'var(--badge-danger-fg)' }}>
            {errorCancelacion}
          </p>
        ) : null}

        {errorConsulta ? (
          <p className="mt-1.5 text-flit-primary/70 dark:text-white/70">Sin conexión con el servidor; reintentando…</p>
        ) : null}
      </div>
    </section>
  );
}
