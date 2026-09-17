'use client';

import { Network } from 'lucide-react';
import { StatusBadge } from '@/components/atom/StatusBadge';
import type { NetworkChildOption } from '@/hooks/useNetworkScope';
import {
  ETIQUETA_DISTINTIVO_RED,
  describirAlcanceRed,
  type NetworkScopePreference,
} from '@/lib/tramites/network-scope';

export interface NetworkScopeBadgeProps {
  scope: NetworkScopePreference;
  hijos?: readonly NetworkChildOption[];
  className?: string;
  /** Sufijo del `data-testid` (`network-scope-badge` por defecto). */
  testId?: string;
}

/**
 * HU #12364 — distintivo «Red» que acompaña a cada indicador o reporte calculado sobre la red
 * (AC1): texto + icono, mismo chip tintado (`StatusBadge` tone `info`) que el resto de tablas,
 * y el nombre del cliente hijo cuando el alcance es uno solo. No se pinta con alcance propio:
 * quien lo monta decide (`networkActive`).
 */
export function NetworkScopeBadge({
  scope,
  hijos = [],
  className = '',
  testId = 'network-scope-badge',
}: NetworkScopeBadgeProps) {
  if (scope.mode !== 'network') return null;
  const detalle = describirAlcanceRed(scope, hijos);
  const conHijo = Boolean(scope.childTenantId);
  return (
    <span className={`inline-flex min-w-0 items-center gap-1.5 ${className}`} data-testid={testId}>
      <StatusBadge
        tone="info"
        ariaLabel={`${ETIQUETA_DISTINTIVO_RED}: datos de ${detalle}`}
        label={
          <span className="inline-flex items-center gap-1">
            <Network className="h-3 w-3" aria-hidden="true" />
            {ETIQUETA_DISTINTIVO_RED}
          </span>
        }
      />
      {conHijo ? (
        <span className="truncate text-[11px] font-medium opacity-70" title={detalle}>
          {detalle}
        </span>
      ) : null}
    </span>
  );
}
