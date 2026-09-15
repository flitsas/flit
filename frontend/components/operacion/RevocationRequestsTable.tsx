'use client';

import { Eye } from 'lucide-react';
import { StatusBadge } from '@/components/atom/StatusBadge';
import { RowActions } from '@/components/atom/RowActions';
import { PageNav } from '@/components/atom/PageNav';
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from '@/components/atom/table-styles';
import { formatFechaHora } from '@/lib/format/date';
import { revocationRequestListLabel, revocationRequestListTone } from '@/lib/tramites/estados';
import type { RevocationRequestListItem } from '@/lib/api/types/revocation-requests';

/**
 * HU #12578 (Feature #12565) — tabla de la vista dedicada "Revocatorias". Compartida por gestor y OT
 * (misma fila en las dos respuestas del backend): SIN tarjeta blanca envolvente, mismo lenguaje visual
 * que `TramitesTable`/`ClientProceduresTable`/`BannerListTable` (`@/components/atom/table-styles`).
 *
 * `onView` es opcional: el gestor lo pasa (navega al detalle del trámite en `/tramites/{id}`); el lado
 * OT no tiene hoy una vista de detalle por id fuera de la bandeja de trámites de cliente (fuera de
 * alcance de esta HU), así que lo omite y la columna "Acciones" no se pinta.
 */
export interface RevocationRequestsTableProps {
  items: RevocationRequestListItem[];
  total: number;
  skip: number;
  take: number;
  onPageChange: (skip: number) => void;
  onView?: (item: RevocationRequestListItem) => void;
}

export function RevocationRequestsTable({
  items,
  total,
  skip,
  take,
  onPageChange,
  onView,
}: RevocationRequestsTableProps) {
  const page = Math.floor(skip / take) + 1;
  const totalPages = Math.max(1, Math.ceil(total / take));
  const desde = total === 0 ? 0 : skip + 1;
  const hasta = Math.min(skip + items.length, total);

  return (
    <div className="flex flex-1 flex-col gap-3">
      <div className="overflow-x-auto">
        <table
          aria-label="Solicitudes de revocatoria"
          style={{ width: '100%', borderCollapse: 'separate', borderSpacing: '0 8px' }}
        >
          <thead>
            <tr>
              <th
                scope="col"
                className={`${TABLA_HEADER_CELL_CLS} rounded-l-xl`}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Radicado
              </th>
              <th
                scope="col"
                className={TABLA_HEADER_CELL_CLS}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Placa
              </th>
              <th
                scope="col"
                className={TABLA_HEADER_CELL_CLS}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Organismo de tránsito
              </th>
              <th
                scope="col"
                className={TABLA_HEADER_CELL_CLS}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Estado
              </th>
              <th
                scope="col"
                className={TABLA_HEADER_CELL_CLS}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Intento
              </th>
              <th
                scope="col"
                className={TABLA_HEADER_CELL_CLS}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Solicitada
              </th>
              <th
                scope="col"
                className={TABLA_HEADER_CELL_CLS}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
              >
                Decidida
              </th>
              {onView ? (
                <th
                  scope="col"
                  className={`${TABLA_HEADER_CELL_CLS} rounded-r-xl text-right`}
                  style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                >
                  Acciones
                </th>
              ) : (
                <th
                  scope="col"
                  aria-hidden="true"
                  className={`${TABLA_HEADER_CELL_CLS} rounded-r-xl`}
                  style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                />
              )}
            </tr>
          </thead>
          <tbody>
            {items.map((item) => (
              <tr
                key={item.revocationRequestId}
                className={`bg-white text-xs dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS}`}
              >
                <td
                  className="rounded-l-xl border-y border-l px-4 py-3 font-semibold"
                  style={{ borderColor: '#DFE5ED' }}
                >
                  {item.referenceNumber}
                </td>
                <td className="border-y px-4 py-3 uppercase" style={{ borderColor: '#DFE5ED' }}>
                  {item.placa ?? '—'}
                </td>
                <td className="border-y px-4 py-3" style={{ borderColor: '#DFE5ED' }}>
                  {item.transitOfficeName ?? '—'}
                </td>
                <td className="border-y px-4 py-3" style={{ borderColor: '#DFE5ED' }}>
                  <StatusBadge
                    label={revocationRequestListLabel(item.status)}
                    tone={revocationRequestListTone(item.status)}
                  />
                </td>
                <td className="border-y px-4 py-3 opacity-80" style={{ borderColor: '#DFE5ED' }}>
                  {item.attemptNumber}
                </td>
                <td className="border-y px-4 py-3 opacity-80" style={{ borderColor: '#DFE5ED' }}>
                  {formatFechaHora(item.requestedAt)}
                </td>
                <td className="border-y px-4 py-3 opacity-80" style={{ borderColor: '#DFE5ED' }}>
                  {formatFechaHora(item.decidedAt)}
                </td>
                {onView ? (
                  <td
                    className="rounded-r-xl border-y border-r px-4 py-3 text-right"
                    style={{ borderColor: '#DFE5ED' }}
                  >
                    <RowActions
                      actions={[
                        {
                          icon: Eye,
                          label: `Ver trámite ${item.referenceNumber}`,
                          onClick: () => onView(item),
                          tone: 'primary',
                        },
                      ]}
                    />
                  </td>
                ) : (
                  <td className="rounded-r-xl border-y border-r" style={{ borderColor: '#DFE5ED' }} />
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <PageNav
        page={page}
        totalPages={totalPages}
        onPageChange={(p) => onPageChange((p - 1) * take)}
        resumen={total > 0 ? `Mostrando ${desde}–${hasta} de ${total}` : 'Sin resultados'}
        ariaLabel="Paginación de solicitudes de revocatoria"
      />
    </div>
  );
}
