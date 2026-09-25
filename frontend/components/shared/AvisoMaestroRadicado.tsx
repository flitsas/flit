'use client';

import { Landmark } from 'lucide-react';
import {
  COPY_MAESTRO_RADICADO_TITULO,
  textoMaestroRadicado,
} from '@/lib/tramites/consolidado-entrega-ot';

/**
 * HU #12787 (AC2) — aviso de que el consolidado maestro mostrado es el que se RADICÓ en Quipux, con
 * la fecha de radicación en hora Colombia, y que no se regenera.
 *
 * Banner informativo del detalle de trámite (`flit-detalle-tramite`): fondo `{azul}1F`, borde
 * `{azul}55`, icono en azul de marca. El texto va en navy (`#162744`) y no en azul: el azul de marca
 * sobre fondo claro no llega a 4.5:1 en 12px. No depende solo del color: icono + texto, y se anuncia
 * como `status`.
 */
export function AvisoMaestroRadicado({
  radicadoEn,
  className,
}: {
  radicadoEn: string;
  className?: string;
}) {
  return (
    <div
      className={`flex items-start gap-2 rounded-xl px-3 py-2 ${className ?? ''}`}
      style={{ background: '#557EFF1F', border: '1px solid #557EFF55' }}
      role="status"
      data-testid="aviso-maestro-radicado"
    >
      <Landmark className="mt-0.5 h-4 w-4 shrink-0" style={{ color: '#557EFF' }} aria-hidden="true" />
      <p className="text-xs text-[#162744] dark:text-white/80">
        <span className="font-bold">{COPY_MAESTRO_RADICADO_TITULO}.</span>{' '}
        <span>{textoMaestroRadicado(radicadoEn)}</span>
      </p>
    </div>
  );
}
