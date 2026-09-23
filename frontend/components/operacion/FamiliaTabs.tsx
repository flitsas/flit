'use client';

import type { ProcedureFamily } from '@/lib/api/types/procedure-parametrization';
import { FAMILIA_OPCIONES } from '@/lib/api/types/familia-labels';

/**
 * Pestañas de familia (Todos / Matrículas / Traspaso / Otros trámites) con subrayado alineado al
 * borde inferior. Las comparten el listado del gestor y, desde la Epic #12686, la bandeja del OT.
 */
const TABS: { value: '' | ProcedureFamily; label: string }[] = [
  { value: '', label: 'Todos' },
  ...FAMILIA_OPCIONES,
];

interface Props {
  /** Familia seleccionada; cadena vacía = todas. */
  value: '' | ProcedureFamily;
  onChange: (v: '' | ProcedureFamily) => void;
}

export function FamiliaTabs({ value, onChange }: Props) {
  return (
    <div className="flex flex-wrap items-center gap-1" role="tablist" aria-label="Tipo de trámite">
      {TABS.map((t) => {
        const active = value === t.value;
        return (
          <button
            key={t.value || 'todos'}
            type="button"
            role="tab"
            aria-selected={active}
            onClick={() => onChange(t.value)}
            className="relative rounded-t-lg px-4 py-2.5 text-xs font-semibold transition focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[#557EFF]"
            style={active ? { color: '#557EFF', opacity: 1 } : { color: '#162744', opacity: 0.65 }}
          >
            {t.label}
            {active ? (
              <span
                className="absolute inset-x-2 -bottom-2.5 h-0.5 rounded-full bg-[#557EFF]"
                aria-hidden="true"
              />
            ) : null}
          </button>
        );
      })}
    </div>
  );
}
