'use client';

import { CheckCircle2, Info } from 'lucide-react';
import { avisoDigitoPlacaOt } from '@/lib/tramites/plate-digit-preference';

/**
 * Registro diferido 2026-11-09 (HU 3) — aviso al gestor: la placa que asignó el organismo de
 * tránsito frente a su dígito de preferencia. Coincide → verde (válido/completo); no coincide →
 * informativo azul, nunca de error (el dígito siempre fue solo una guía). Sin preferencia o placa
 * que no asignó el OT → no pinta nada.
 *
 * Colores: verde lima del detalle (`#8CC63F`) como borde/fondo suave con texto `#15803D` para
 * contraste AA sobre fondo claro; informativo con la misma paleta que la guía del dígito del modal
 * del OT (`#EEF3FF` / `#C7D7FE` / `#1E3A8A`). El icono acompaña al color (no depende solo de él).
 */
export function AvisoDigitoPlacaOt({
  placa,
  source,
  digito,
  testId,
  className = '',
}: {
  placa: string | null | undefined;
  source: string | null | undefined;
  digito: string | null | undefined;
  testId?: string;
  className?: string;
}) {
  const aviso = avisoDigitoPlacaOt({ placa, source, digito });
  if (!aviso) return null;
  const exito = aviso.tono === 'exito';
  const Icon = exito ? CheckCircle2 : Info;
  return (
    <p
      role="status"
      data-testid={testId}
      data-tono={aviso.tono}
      className={`flex items-start gap-2 rounded-lg px-3 py-2 text-left text-xs font-medium ${className}`}
      style={
        exito
          ? { background: 'rgba(140, 198, 63, 0.12)', color: '#15803D', border: '1px solid #8CC63F' }
          : { background: '#EEF3FF', color: '#1E3A8A', border: '1px solid #C7D7FE' }
      }
    >
      <Icon className="mt-px h-3.5 w-3.5 shrink-0" aria-hidden="true" />
      <span>{aviso.texto}</span>
    </p>
  );
}
