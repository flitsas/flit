'use client';

import type { ReactNode } from 'react';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import type { BiometricValidation, MecanismoFirma } from '@/lib/api/types/procedure-runtime';

const BORDER = '#DFE5ED';
const CANVAS_BG = '#EEF5FF';
const INK = '#1A2B4C';
const MUTED = '#59677D';

export interface FirmaElectronicaCardProps {
  nombre: string;
  validated: boolean;
  badgeLabel: string;
  badgeTone: StatusTone;
  detalle?: string;
  hashLine?: string | null;
  footer?: ReactNode;
  className?: string;
  stretch?: boolean;
  showTitle?: boolean;
}

export function signatureHashLabel(validation: BiometricValidation | null): string | null {
  const hash = validation?.certificateHash?.trim();
  if (hash) {
    return hash;
  }
  if (validation?.status === 'aprobado' && !validation.expired) {
    return '—';
  }
  return null;
}

export function signatureBadge(
  vaultCovered: boolean,
  mecanismoFirma: MecanismoFirma | undefined,
): { label: string; tone: StatusTone } {
  if (vaultCovered) {
    return { label: 'Firma electrónica activa', tone: 'success' };
  }
  if (mecanismoFirma === 'identidad') {
    return { label: 'Firmará con validación de identidad', tone: 'info' };
  }
  return { label: 'Sin firma registrada', tone: 'neutral' };
}

export function FirmaElectronicaCard({
  nombre,
  validated,
  badgeLabel,
  badgeTone,
  detalle,
  hashLine,
  footer,
  className = '',
  stretch = false,
  showTitle = true,
}: FirmaElectronicaCardProps) {
  return (
    <div className={`${stretch ? 'flex flex-1 flex-col' : ''} ${className}`.trim()}>
      {showTitle ? (
        <p className="mb-3 text-xs font-bold" style={{ color: INK }}>
          Método de Firma
        </p>
      ) : null}
      <div
        className={`flex items-center justify-center rounded-xl border p-5 text-center ${
          stretch ? 'flex-1' : ''
        }`}
        style={{ borderColor: BORDER, background: CANVAS_BG }}
      >
        <div>
          <p
            className="text-[11px] font-medium uppercase tracking-wide"
            style={{ color: MUTED }}
          >
            Firma electrónica
          </p>
          <p
            className="mt-2 select-none text-2xl font-semibold italic"
            style={{ color: INK, filter: validated ? undefined : 'blur(4px)' }}
          >
            {nombre}
          </p>
          <div className="mt-2">
            <StatusBadge label={badgeLabel} tone={badgeTone} />
          </div>
          {detalle ? <p className="mt-2 text-xs opacity-70">{detalle}</p> : null}
          {hashLine != null ? (
            <p className="mt-2 text-xs opacity-70">Hash: {hashLine}</p>
          ) : null}
        </div>
      </div>
      {footer ? <div className="mt-3">{footer}</div> : null}
    </div>
  );
}
