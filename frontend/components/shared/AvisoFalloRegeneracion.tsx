'use client';

import { AlertTriangle, RefreshCw } from 'lucide-react';
import {
  COPY_FALLO_REGENERACION,
  textoAvisoFalloRegeneracion,
  type FalloRegeneracionConsolidado,
} from '@/lib/tramites/fallo-regeneracion-consolidado';

export interface AvisoFalloRegeneracionProps {
  fallo: FalloRegeneracionConsolidado;
  /** ISO de la generación del PDF conservado (`vigencia.generadoEn`). Sin fecha fiable se omite. */
  generadoEn?: string | null;
  /** Acción de regenerar existente (con su candado). Omitida ⇒ no se pinta «Reintentar». */
  onReintentar?: () => void;
  /** Deshabilita «Reintentar» mientras hay una generación/apertura en vuelo. */
  reintentando?: boolean;
  className?: string;
}

/**
 * HU #12799 (Épica #12760) — aviso de que la última regeneración del consolidado falló y el PDF que
 * se muestra es el anterior (con su fecha, hora Colombia). Se pinta en el bloque del consolidado
 * (gestor y consola OT) y sobre el visor del PDF.
 *
 * Diseño (`flit-design-guardian` / `flit-detalle-tramite`): banner de advertencia con los tokens
 * `--badge-warning-*` (ámbar = alerta), radio 12px, icono lucide 16px. El título va en
 * `--badge-warning-fg` (AA, theme-aware) y el cuerpo en navy. No depende del color: icono + título +
 * texto. `role="alert"`: aparece como resultado de una acción del usuario y debe anunciarse.
 *
 * Uso de ejemplo:
 *   <AvisoFalloRegeneracion fallo={fallo} generadoEn={vigencia?.generadoEn}
 *     onReintentar={() => void regenerar()} reintentando={busy} />
 */
export function AvisoFalloRegeneracion({
  fallo,
  generadoEn,
  onReintentar,
  reintentando = false,
  className,
}: AvisoFalloRegeneracionProps) {
  return (
    <div
      role="alert"
      data-testid="aviso-fallo-regeneracion"
      data-documento={fallo.documento}
      className={`flex flex-wrap items-start gap-2 rounded-xl border px-3 py-2 ${className ?? ''}`}
      style={{
        background: 'var(--badge-warning-bg)',
        borderColor: 'var(--badge-warning-border)',
      }}
    >
      <AlertTriangle
        className="mt-0.5 h-4 w-4 shrink-0"
        style={{ color: 'var(--badge-warning-fg)' }}
        aria-hidden="true"
      />
      <p className="min-w-0 flex-1 text-xs">
        <span className="font-bold" style={{ color: 'var(--badge-warning-fg)' }}>
          {COPY_FALLO_REGENERACION.titulo[fallo.documento]}.
        </span>{' '}
        <span className="text-[#162744] dark:text-white/80">
          {textoAvisoFalloRegeneracion(fallo, generadoEn)}
        </span>
      </p>
      {onReintentar ? (
        <button
          type="button"
          onClick={onReintentar}
          disabled={reintentando}
          aria-label={COPY_FALLO_REGENERACION.ariaReintentar[fallo.documento]}
          className="inline-flex shrink-0 items-center gap-1.5 rounded-full border bg-white px-3 py-1 text-xs font-semibold transition hover:opacity-90 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:opacity-50 dark:bg-transparent"
          style={{ borderColor: 'var(--badge-warning-border)', color: 'var(--badge-warning-fg)' }}
        >
          <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
          {COPY_FALLO_REGENERACION.reintentar}
        </button>
      ) : null}
    </div>
  );
}
