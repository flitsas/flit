'use client';

import { AlertTriangle, CheckCircle2, Clock, Download, FileArchive, Loader2, X, XCircle } from 'lucide-react';
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
 * Uso de ejemplo:
 *   <LoteDescargaAlertCard lote={lote} expirado={expirado} onDescargarParte={(n) => descargar(n)}
 *     onCerrar={ocultar} errorConsulta={errorConsulta} descargandoParte={null} />
 */

/** AC2/AC4 — texto aprobado del lote expirado. */
export const TEXTO_DESCARGA_EXPIRADA = 'Descarga expirada';

const formatoMiles = (n: number) => n.toLocaleString('es-CO');

/** AC2 — tono semántico (de la escala de `statusTones` / `StatusBadge`) del lote. */
export function toneLoteConsolidados(lote: LoteConsolidados, expirado: boolean): StatusTone {
  if (expirado || lote.estado === 'expirado' || lote.estado === 'cancelado') return 'neutral';
  if (lote.estado === 'fallido') return 'danger';
  if (lote.omitidos > 0) return 'warning';
  if (lote.estado === 'completado_con_omitidos') return 'warning';
  if (lote.estado === 'completado') return 'success';
  return 'info';
}

/** Frase del estado que se anuncia en la región viva. */
function textoEstado(lote: LoteConsolidados, expirado: boolean): string {
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
    case 'cancelado':
      return 'Descarga cancelada';
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

export interface LoteDescargaAlertCardProps {
  lote: LoteConsolidados;
  /** AC4 — expirado (por estado o pasadas 24 h desde el fin). */
  expirado: boolean;
  onDescargarParte: (numero: number) => void;
  /** Cierra el aviso; omitido ⇒ no se pinta la X. */
  onCerrar?: () => void;
  /** AC5 — la última consulta falló: se muestran los datos anteriores y se avisa del reintento. */
  errorConsulta?: boolean;
  descargandoParte?: number | null;
  errorDescarga?: string | null;
  className?: string;
}

export function LoteDescargaAlertCard({
  lote,
  expirado,
  onDescargarParte,
  onCerrar,
  errorConsulta = false,
  descargandoParte = null,
  errorDescarga = null,
  className,
}: LoteDescargaAlertCardProps) {
  const tone = toneLoteConsolidados(lote, expirado);
  const Icono = ICONO[tone];
  const terminado = lote.estado === 'completado' || lote.estado === 'completado_con_omitidos';
  const conPartes = terminado && !expirado && lote.partes.length > 0;
  const activo = !expirado && ['en_cola', 'en_proceso', 'empaquetando'].includes(lote.estado);
  const totalPartes = lote.partes.length;

  return (
    <section
      aria-label="Descarga masiva de consolidados"
      data-testid="lote-descarga-card"
      data-tone={tone}
      // Fondo opaco de tarjeta (`bg-card`, theme-aware) con el tinte del tono encima: el aviso flota
      // sobre el contenido y el tinte de `--badge-*-bg` es translúcido. Sombra = token `shadow.card`.
      className={`rounded-xl border bg-card px-4 py-3 text-xs shadow-[0_8px_24px_rgba(22,39,68,0.08)] ${className ?? ''}`}
      style={{
        backgroundImage: `linear-gradient(var(--badge-${tone}-bg), var(--badge-${tone}-bg))`,
        borderColor: `var(--badge-${tone}-border)`,
      }}
    >
      <div className="flex items-start gap-2">
        <Icono className="mt-0.5 h-4 w-4 shrink-0" style={{ color: `var(--badge-${tone}-fg)` }} aria-hidden="true" />
        <div className="min-w-0 flex-1">
          <h2 className="text-sm font-semibold text-flit-primary dark:text-white">Descarga masiva de consolidados</h2>
          <p role="status" aria-live="polite" className="font-semibold" style={{ color: `var(--badge-${tone}-fg)` }}>
            {textoEstado(lote, expirado)}
          </p>
        </div>
        {onCerrar ? (
          <button
            type="button"
            onClick={onCerrar}
            aria-label="Cerrar aviso de descarga"
            className="shrink-0 rounded-lg p-1 text-flit-primary/70 transition hover:bg-flit-primary/[0.06] focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand dark:text-white/70"
          >
            <X className="h-4 w-4" aria-hidden="true" />
          </button>
        ) : null}
      </div>

      {!expirado && lote.estado !== 'cancelado' ? (
        <div className="mt-2">
          <p className="text-flit-primary/80 dark:text-white/80">
            <span className="font-semibold tabular-nums">
              {formatoMiles(lote.procesados)} / {formatoMiles(lote.total)}
            </span>{' '}
            trámites procesados
            {lote.omitidos > 0 ? ` · ${formatoMiles(lote.omitidos)} omitido${lote.omitidos === 1 ? '' : 's'}` : ''}
          </p>
          {activo ? (
            <div
              role="progressbar"
              aria-label="Avance de la descarga"
              aria-valuemin={0}
              aria-valuemax={lote.total}
              aria-valuenow={lote.procesados}
              className="mt-1.5 h-1.5 w-full overflow-hidden rounded-full bg-flit-gray dark:bg-white/10"
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
        </div>
      ) : null}

      {lote.estado === 'fallido' && !expirado ? (
        <p className="mt-2 text-flit-primary/80 dark:text-white/80">{MENSAJE_REINTENTAR_DESCARGA}</p>
      ) : null}

      {conPartes ? (
        <ul aria-label="Partes de la descarga" className="mt-2 flex flex-col gap-1.5">
          {lote.partes.map((parte) => {
            const descargando = descargandoParte === parte.numero;
            return (
              <li
                key={parte.numero}
                className="flex items-center justify-between gap-2 rounded-xl border border-flit-gray bg-white/70 px-3 py-1.5 dark:border-white/10 dark:bg-white/[0.04]"
              >
                <span className="min-w-0 truncate text-flit-primary dark:text-white" title={parte.nombreArchivo}>
                  {totalPartes > 1 ? `Parte ${parte.numero} de ${totalPartes}` : 'Archivo ZIP'} ·{' '}
                  {formatoMiles(parte.pdfs)} PDF
                </span>
                <button
                  type="button"
                  onClick={() => onDescargarParte(parte.numero)}
                  disabled={descargando}
                  aria-label={`Descargar parte ${parte.numero} de ${totalPartes}`}
                  className="inline-flex shrink-0 items-center gap-1 rounded-full border border-flit-brand/40 px-2.5 py-1 font-semibold text-flit-brand transition hover:bg-flit-brand/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand disabled:cursor-wait disabled:opacity-60"
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
        <p role="alert" className="mt-2 font-semibold" style={{ color: 'var(--badge-danger-fg)' }}>
          {errorDescarga}
        </p>
      ) : null}

      {errorConsulta ? (
        <p className="mt-2 text-flit-primary/70 dark:text-white/70">
          Sin conexión con el servidor; reintentando…
        </p>
      ) : null}
    </section>
  );
}
