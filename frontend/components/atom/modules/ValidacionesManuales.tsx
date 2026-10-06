'use client';

import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { Eye, Search } from 'lucide-react';
import { DataTable, type DataTableColumn } from '@flit/ui/DataTable';
import { RowActions } from '@/components/atom/RowActions';
import { CarLoaderModal } from '@/components/atom/CarLoader';
import { usePaginacion } from '@/components/atom/usePaginacion';
import { WIZARD_INPUT, WIZARD_LABEL, WIZARD_SELECT } from '@/components/operacion/wizard-field-styles';
import { getManualReviewClient, type ManualReviewClient } from '@/lib/api/manual-review-client';
import type {
  ManualListItem,
  ManualOrigin,
  ManualStatus,
} from '@/lib/api/types/manual-review';
import {
  MANUAL_ORIGIN_LABEL,
  MANUAL_STATUS_META,
  formatEspera,
  manualOriginLabel,
} from '@/lib/identidad/manual-review-meta';
import { formatFechaHora } from '@/lib/format/date';
import { ManualStatusBadge } from './ManualStatusBadge';
import { ManualReviewDetailModal } from './ManualReviewDetailModal';

/**
 * Pestaña «Validaciones manuales» de Validaciones (Épica #13202, HU-C5). Solo Super Admin: quien la monta
 * (`Validaciones`) ya comprobó el rol. Tabla del modelo único de trámites: `DataTable` (cabecera
 * `table-styles`, filas-tarjeta, sin tarjeta envolvente), `RowActions`, paginación numerada con
 * «Filas por página» y `CarLoaderModal` mientras carga.
 */

const SEARCH_DEBOUNCE_MS = 300;

type Carga = 'loading' | 'ready' | 'error';

export function ValidacionesManuales({
  client,
  onChanged,
}: {
  client?: ManualReviewClient;
  /** Se aprobó o rechazó un registro desde el detalle: quien monta la pestaña refresca su contador. */
  onChanged?: () => void;
}) {
  const api = client ?? getManualReviewClient();
  const pg = usePaginacion();
  const { page, pageSize, setPage } = pg;

  const [status, setStatus] = useState<ManualStatus | ''>('');
  const [origin, setOrigin] = useState<ManualOrigin | ''>('');
  const [texto, setTexto] = useState('');
  const [q, setQ] = useState('');
  const qRef = useRef('');
  const [rows, setRows] = useState<ManualListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [carga, setCarga] = useState<Carga>('loading');
  const [reloadKey, setReloadKey] = useState(0);
  const [detalleId, setDetalleId] = useState<string | null>(null);
  const idBase = useId();

  // Búsqueda con retardo: filtra al dejar de escribir, sin botón extra (menos pasos).
  useEffect(() => {
    const t = window.setTimeout(() => {
      const limpio = texto.trim();
      if (limpio === qRef.current) return;
      qRef.current = limpio;
      setQ(limpio);
      setPage(1);
    }, SEARCH_DEBOUNCE_MS);
    return () => window.clearTimeout(t);
  }, [texto, setPage]);

  useEffect(() => {
    const ctrl = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- inicio de la carga asíncrona
    setCarga('loading');
    api
      .listManual({ page, pageSize, status, origin, q }, ctrl.signal)
      .then((res) => {
        if (ctrl.signal.aborted) return;
        setRows(res.items);
        setTotal(res.total);
        setCarga('ready');
      })
      .catch(() => {
        if (ctrl.signal.aborted) return;
        setCarga('error');
      });
    return () => ctrl.abort();
  }, [api, page, pageSize, status, origin, q, reloadKey]);

  const recargar = useCallback(() => setReloadKey((k) => k + 1), []);
  const hayFiltros = Boolean(status || origin || q);

  const columns: DataTableColumn<ManualListItem>[] = [
    {
      key: 'nombre',
      header: 'Nombre',
      render: (r) => <span className="font-semibold text-[#162744] dark:text-white">{r.fullName}</span>,
    },
    { key: 'documento', header: 'Documento', render: (r) => <span className="font-mono">{r.documentNumber}</span> },
    { key: 'compania', header: 'Compañía', render: (r) => r.tenantName },
    { key: 'origen', header: 'Origen', render: (r) => manualOriginLabel(r.origin) },
    { key: 'estado', header: 'Estado', render: (r) => <ManualStatusBadge status={r.status} /> },
    { key: 'activacion', header: 'Fecha de activación', render: (r) => formatFechaHora(r.activatedAt) },
    { key: 'espera', header: 'Tiempo en espera', render: (r) => formatEspera(r.waitingMinutes) },
    {
      key: 'acciones',
      header: 'Acciones',
      align: 'right',
      render: (r) => (
        <RowActions
          actions={[
            {
              icon: Eye,
              label: `Ver detalle de ${r.fullName}`,
              tone: 'primary',
              onClick: () => setDetalleId(r.id),
            },
          ]}
        />
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-end gap-3" role="search" aria-label="Filtros de validaciones manuales">
        <div className="min-w-[220px] flex-1">
          <label htmlFor={`${idBase}-q`} className={WIZARD_LABEL}>
            Nombre o número de documento
          </label>
          <div className="relative mt-1">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 opacity-50" aria-hidden />
            <input
              id={`${idBase}-q`}
              type="search"
              value={texto}
              onChange={(e) => setTexto(e.target.value)}
              placeholder="Buscar"
              className={`${WIZARD_INPUT} pl-9`}
              style={{ borderColor: '#DFE5ED' }}
            />
          </div>
        </div>
        <div className="w-48">
          <label htmlFor={`${idBase}-estado`} className={WIZARD_LABEL}>
            Estado
          </label>
          <select
            id={`${idBase}-estado`}
            value={status}
            onChange={(e) => {
              setStatus(e.target.value as ManualStatus | '');
              setPage(1);
            }}
            className={`${WIZARD_SELECT} mt-1`}
            style={{ borderColor: '#DFE5ED' }}
          >
            <option value="">Todos</option>
            {(Object.keys(MANUAL_STATUS_META) as ManualStatus[]).map((s) => (
              <option key={s} value={s}>
                {MANUAL_STATUS_META[s].label}
              </option>
            ))}
          </select>
        </div>
        <div className="w-48">
          <label htmlFor={`${idBase}-origen`} className={WIZARD_LABEL}>
            Origen
          </label>
          <select
            id={`${idBase}-origen`}
            value={origin}
            onChange={(e) => {
              setOrigin(e.target.value as ManualOrigin | '');
              setPage(1);
            }}
            className={`${WIZARD_SELECT} mt-1`}
            style={{ borderColor: '#DFE5ED' }}
          >
            <option value="">Todos</option>
            {(Object.keys(MANUAL_ORIGIN_LABEL) as ManualOrigin[]).map((o) => (
              <option key={o} value={o}>
                {MANUAL_ORIGIN_LABEL[o]}
              </option>
            ))}
          </select>
        </div>
      </div>

      <p className="sr-only" role="status" aria-live="polite">
        {carga === 'loading' ? 'Cargando validaciones manuales…' : `${total} validaciones manuales`}
      </p>

      {carga === 'loading' && <CarLoaderModal label="Cargando validaciones manuales…" />}

      {(carga !== 'loading' || rows.length > 0) && (
        <DataTable
          ariaLabel="Validaciones manuales"
          columns={columns}
          rows={carga === 'error' ? [] : rows}
          getRowKey={(r) => r.id}
          status={carga === 'error' ? 'error' : 'ready'}
          errorMessage="No se pudieron cargar las validaciones manuales."
          emptyMessage={
            hayFiltros
              ? 'Ninguna validación manual coincide con los filtros.'
              : 'Aún no hay validaciones manuales. Aparecen cuando activas el flujo manual en una validación.'
          }
          onRetry={recargar}
          minWidth={980}
          pagination={{
            page,
            pageSize,
            totalCount: total,
            onPageChange: setPage,
            onPageSizeChange: pg.setPageSize,
          }}
        />
      )}

      <ManualReviewDetailModal
        id={detalleId}
        client={api}
        onClose={() => setDetalleId(null)}
        onChanged={() => {
          recargar();
          onChanged?.();
        }}
      />
    </div>
  );
}
