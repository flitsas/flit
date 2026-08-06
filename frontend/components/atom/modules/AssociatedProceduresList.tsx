'use client';

import { useState } from 'react';
import { ChevronRight, ExternalLink } from 'lucide-react';
import type { LinkedProcedureRef } from '@/lib/api/types/procedure-runtime';
import { FLIT } from '@/lib/flit-design-tokens';

const MODALIDAD_LABEL: Record<string, string> = {
  traspaso: 'Traspaso',
  matricula_inicial: 'Matrícula inicial',
};

export type AssociatedProcedureItem = {
  instanceId: string;
  referenceNumber: string;
  status?: string;
  modalidad?: string | null;
  /** True si es el trámite primario de la fila/detalle. */
  primary?: boolean;
};

/**
 * HU #11069 — lista de trámites asociados a una VID (primario + vinculados).
 * En detalle usa el mismo patrón de disclosure que el tracking («Ver trámites»).
 */
export function AssociatedProceduresList({
  procedures,
  ariaLabel = 'Trámites asociados a esta validación',
  compact = false,
  /** Detalle: colapsable como IdentityValidationTrackingPanel. */
  collapsible = false,
  defaultOpen = false,
}: {
  procedures: AssociatedProcedureItem[];
  ariaLabel?: string;
  /** Compacto para celdas de tabla; detalle usa el layout ampliado. */
  compact?: boolean;
  collapsible?: boolean;
  defaultOpen?: boolean;
}) {
  const [open, setOpen] = useState(defaultOpen);

  if (procedures.length === 0) return null;

  const list = (
    <ul className={compact ? 'space-y-0.5' : 'space-y-2'} aria-label={ariaLabel}>
      {procedures.map((p) => {
        const tipo =
          p.modalidad != null && p.modalidad !== ''
            ? (MODALIDAD_LABEL[p.modalidad] ?? p.modalidad)
            : null;
        return (
          <li key={p.instanceId}>
            <a
              href={`/tramites/${p.instanceId}`}
              className={
                compact
                  ? 'block truncate underline'
                  : 'flex flex-col gap-0.5 rounded-lg border px-3 py-2 hover:bg-[rgba(79,116,201,0.05)]'
              }
              style={compact ? { color: FLIT.brand.blue } : { borderColor: FLIT.border.soft }}
              title={`${p.referenceNumber}${tipo ? ` · ${tipo}` : ''}${p.status ? ` (${p.status})` : ''}`}
            >
              {compact ? (
                <>
                  {p.referenceNumber}
                  {tipo ? ` · ${tipo}` : ''}
                </>
              ) : (
                <>
                  <span className="flex items-center gap-1.5 text-xs font-semibold" style={{ color: FLIT.brand.blue }}>
                    <ExternalLink className="h-3 w-3 shrink-0" aria-hidden />
                    {p.referenceNumber}
                    {p.primary ? (
                      <span className="rounded-full px-1.5 py-0.5 text-[9px] font-bold opacity-70">
                        Primario
                      </span>
                    ) : null}
                  </span>
                  <span className="font-mono text-[10px] opacity-60 truncate" title={p.instanceId}>
                    ID: {p.instanceId}
                  </span>
                  {tipo && <span className="text-[10px] opacity-80">Tipo: {tipo}</span>}
                  {p.status && <span className="text-[10px] opacity-60">Estado: {p.status}</span>}
                </>
              )}
            </a>
          </li>
        );
      })}
    </ul>
  );

  if (compact) {
    return (
      <div className="text-[10px] leading-snug opacity-80 space-y-0.5" aria-label={ariaLabel}>
        <span className="font-semibold opacity-70">Trámites:</span>
        {list}
      </div>
    );
  }

  if (!collapsible) {
    return (
      <div className="space-y-2" aria-label={ariaLabel}>
        <p className="text-[11px] font-semibold text-[#162744] dark:text-white">Trámites asociados</p>
        {list}
      </div>
    );
  }

  // Mismo patrón que IdentityValidationTrackingPanel: caja + disclosure «Ver …».
  return (
    <div className="rounded-xl border p-3">
      <p className="mb-1 text-[11px] font-semibold text-[#162744] dark:text-white">Trámites asociados</p>
      <p className="mb-2 text-[10px] opacity-60">
        Trámites del tenant vinculados a esta identidad ({procedures.length}).
      </p>
      <button
        type="button"
        onClick={() => setOpen((prev) => !prev)}
        className="flex items-center gap-1.5 text-[11px] font-semibold opacity-70 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
        aria-expanded={open}
        aria-label={`Ver trámites asociados (${procedures.length})`}
      >
        <ChevronRight
          className={`h-3 w-3 transition-transform ${open ? 'rotate-90' : ''}`}
          aria-hidden
        />
        Ver trámites ({procedures.length})
      </button>
      {open && <div className="mt-2 space-y-2">{list}</div>}
    </div>
  );
}

/** Une trámite primario + linkedProcedures sin duplicar el primario. */
export function buildAssociatedProcedures(opts: {
  instanceId?: string | null;
  referenceNumber?: string | null;
  modalidad?: string | null;
  status?: string | null;
  linkedProcedures?: LinkedProcedureRef[] | null;
}): AssociatedProcedureItem[] {
  const items: AssociatedProcedureItem[] = [];
  const seen = new Set<string>();

  if (opts.instanceId && opts.referenceNumber) {
    items.push({
      instanceId: opts.instanceId,
      referenceNumber: opts.referenceNumber,
      modalidad: opts.modalidad,
      status: opts.status ?? undefined,
      primary: true,
    });
    seen.add(opts.instanceId);
  }

  for (const lp of opts.linkedProcedures ?? []) {
    if (seen.has(lp.instanceId)) continue;
    seen.add(lp.instanceId);
    items.push({
      instanceId: lp.instanceId,
      referenceNumber: lp.referenceNumber,
      status: lp.status,
      modalidad: lp.modalidad,
    });
  }

  return items;
}
