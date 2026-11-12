'use client';

import { CheckCircle2, Info } from 'lucide-react';
import { avisoDigitoPlacaOt } from '@/lib/tramites/plate-digit-preference';

/**
 * Registro diferido 2026-11-09 (HU 3) — aviso al gestor: la placa que asignó el organismo de
 * tránsito frente a su dígito de preferencia. Coincide → verde (válido/completo); no coincide →
 * informativo azul, nunca de error (el dígito siempre fue solo una guía). Sin preferencia o placa
 * que no asignó el OT → no pinta nada.
 *
 * Colores (homologación #14507): tonos `--badge-success-*` (cielo, el «completo» de la propuesta) e
 * `--badge-info-*`, con variante oscura. El icono acompaña al color (no depende solo de él).
 */
export function AvisoDigitoPlacaOt({
  placa,
  source,
  digito,
  clase,
  testId,
  className = '',
}: {
  placa: string | null | undefined;
  source: string | null | undefined;
  digito: string | null | undefined;
  /** `vehicle_class` del trámite: en motos se compara el primer número (HU7). */
  clase?: string | null;
  testId?: string;
  className?: string;
}) {
  const aviso = avisoDigitoPlacaOt({ placa, source, digito, clase });
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
          ? { background: 'var(--badge-success-bg)', color: 'var(--badge-success-fg)', border: '1px solid var(--badge-success-border)' }
          : { background: 'var(--badge-info-bg)', color: 'var(--badge-info-fg)', border: '1px solid var(--badge-info-border)' }
      }
    >
      <Icon className="mt-px h-3.5 w-3.5 shrink-0" aria-hidden="true" />
      <span>{aviso.texto}</span>
    </p>
  );
}
