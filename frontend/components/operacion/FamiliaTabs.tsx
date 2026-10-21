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
  /** HU #13665 — familias visibles (permiso Ver); sin la prop, todas. «Todos» se oculta si no hay ninguna. */
  familias?: readonly ProcedureFamily[];
}

export function FamiliaTabs({ value, onChange, familias }: Props) {
  const tabs = familias
    ? TABS.filter((t) => (t.value === '' ? familias.length > 0 : familias.includes(t.value)))
    : TABS;
  return (
    <div className="flex flex-wrap items-center gap-1" role="tablist" aria-label="Tipo de trámite">
      {tabs.map((t) => {
        const active = value === t.value;
        return (
          <button
            key={t.value || 'todos'}
            type="button"
            role="tab"
            aria-selected={active}
            onClick={() => onChange(t.value)}
            // Clases y no `style`: el color fijo var(--color-flit-primary) dejaba las pestañas inactivas invisibles en
            // modo oscuro.
            className={`relative rounded-t-lg px-4 py-2.5 text-xs font-semibold transition focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-flit-brand ${
              active ? 'text-flit-brand' : 'text-flit-primary/65 dark:text-white/65'
            }`}
          >
            {t.label}
            {active ? (
              <span
                className="absolute inset-x-2 -bottom-2.5 h-0.5 rounded-full bg-flit-brand"
                aria-hidden="true"
              />
            ) : null}
          </button>
        );
      })}
    </div>
  );
}
