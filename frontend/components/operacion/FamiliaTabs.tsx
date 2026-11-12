'use client';

import type { ProcedureFamily } from '@/lib/api/types/procedure-parametrization';
import { FAMILIA_OPCIONES } from '@/lib/api/types/familia-labels';
import { PESTANAS_LISTA_CLS, pestanaClass } from '@flit/ui/Pestanas';

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
    <div className={PESTANAS_LISTA_CLS} role="tablist" aria-label="Tipo de trámite">
      {tabs.map((t) => {
        const active = value === t.value;
        return (
          <button
            key={t.value || 'todos'}
            type="button"
            role="tab"
            aria-selected={active}
            onClick={() => onChange(t.value)}
            // UI Kit 2026: pestaña del kit (subrayado y color por clase, también en modo oscuro).
            className={pestanaClass(active)}
          >
            {t.label}
          </button>
        );
      })}
    </div>
  );
}
