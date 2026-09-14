'use client';

import { useId } from 'react';
import { Network } from 'lucide-react';
import type { NetworkChildOption, NetworkChildrenStatus } from '@/hooks/useNetworkScope';
import {
  ETIQUETA_ALCANCE,
  ETIQUETA_ALCANCE_PROPIO,
  ETIQUETA_ALCANCE_RED,
  ETIQUETA_GRUPO_HIJOS,
  optionValueToScope,
  scopeToOptionValue,
  type NetworkScopePreference,
} from '@/lib/tramites/network-scope';
import { controlCls } from './tramites-control-styles';

export interface NetworkScopeSelectorProps {
  scope: NetworkScopePreference;
  onChange: (next: NetworkScopePreference) => void;
  /** Hijos de la red, ya ordenados. Vacío ⇒ solo «Mi compañía | Toda la red». */
  hijos?: readonly NetworkChildOption[];
  childrenStatus?: NetworkChildrenStatus;
  disabled?: boolean;
  /** Rótulo visible. Se conserva «Alcance» salvo que la pantalla necesite otro (analítica). */
  label?: string;
  /** Sufijo del `data-testid` del `<select>` (`network-scope-select` por defecto). */
  testId?: string;
  className?: string;
}

/**
 * HU #12363 — selector de alcance de una cabeza de red: «Mi compañía», «Toda la red» y cada
 * cliente hijo por nombre. Control COMPARTIDO: lo reutiliza #12364 en estadísticas y reportes,
 * por eso no sabe nada de la tabla ni de la preferencia — recibe el alcance y avisa del cambio.
 *
 * Es un `<select>` nativo con `<label>` visible: teclado, lector de pantalla y foco resueltos sin
 * dependencia del color. Sigue el patrón de la fila de filtros del listado (`controlCls`): neutro
 * en reposo y en azul cuando hay «algo aplicado», que aquí es cualquier alcance distinto del propio
 * — la misma regla que Periodo, + Filtro y Columnas. Quien lo monta decide si se pinta: solo existe
 * para una cabeza de grupo (AC1).
 */
export function NetworkScopeSelector({
  scope,
  onChange,
  hijos = [],
  childrenStatus = 'idle',
  disabled = false,
  label = ETIQUETA_ALCANCE,
  testId = 'network-scope-select',
  className = '',
}: NetworkScopeSelectorProps) {
  const id = useId();
  const value = scopeToOptionValue(scope);
  const activo = scope.mode === 'network';
  const cargandoHijos = childrenStatus === 'loading';

  return (
    <div className={`flex shrink-0 items-center gap-2 ${className}`}>
      <label
        htmlFor={id}
        className="inline-flex items-center gap-1.5 text-xs font-semibold text-[#162744]/80 dark:text-white/70"
      >
        <Network className="h-4 w-4 text-[#557EFF]" aria-hidden="true" />
        {label}
      </label>
      <select
        id={id}
        value={value}
        onChange={(e) => onChange(optionValueToScope(e.target.value))}
        disabled={disabled}
        aria-busy={cargandoHijos || undefined}
        className={`${controlCls(activo)} min-w-[11rem] pr-8`}
        data-testid={testId}
      >
        <option value="own">{ETIQUETA_ALCANCE_PROPIO}</option>
        <option value="network">{ETIQUETA_ALCANCE_RED}</option>
        {hijos.length > 0 ? (
          <optgroup label={ETIQUETA_GRUPO_HIJOS}>
            {hijos.map((hijo) => (
              <option key={hijo.id} value={`child:${hijo.id}`}>
                {hijo.nombre}
              </option>
            ))}
          </optgroup>
        ) : null}
      </select>
    </div>
  );
}
