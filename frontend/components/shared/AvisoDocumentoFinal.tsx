'use client';

import { BadgeCheck } from 'lucide-react';
import {
  COPY_DOCUMENTO_FINAL_DETALLE,
  COPY_DOCUMENTO_FINAL_TITULO,
} from '@/lib/tramites/consolidado-entrega';

/**
 * HU #12786 (AC4) / HU #12787 (AC3) — aviso de que el consolidado mostrado es el documento FINAL
 * del trámite (estado aprobado/anulado/revocado) y no se regenera.
 *
 * Mismo tratamiento de «estado válido» que la tarjeta de identidad verificada (`MatriculaResumen`):
 * fondo verde suave, borde verde y tinta `--flit-success-ink`. No depende solo del color: lleva
 * icono y texto, y se anuncia como `status`.
 */
export function AvisoDocumentoFinal({ className }: { className?: string }) {
  return (
    <div
      className={`flex items-start gap-2 rounded-xl px-3 py-2 ${className ?? ''}`}
      style={{ background: 'rgba(140,198,63,0.12)', border: '1px solid rgba(140,198,63,0.4)' }}
      role="status"
      data-testid="aviso-documento-final"
    >
      <BadgeCheck
        className="mt-0.5 h-4 w-4 shrink-0"
        style={{ color: 'var(--flit-success-ink)' }}
        aria-hidden="true"
      />
      <p className="text-xs">
        <span className="font-bold" style={{ color: 'var(--flit-success-ink)' }}>
          {COPY_DOCUMENTO_FINAL_TITULO}.
        </span>{' '}
        <span className="text-[#162744] dark:text-white/80">{COPY_DOCUMENTO_FINAL_DETALLE}</span>
      </p>
    </div>
  );
}
