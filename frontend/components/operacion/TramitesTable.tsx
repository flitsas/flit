'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import { tramitesClient } from '@/lib/api/tramites-client';
import { getToken } from '@/lib/api/client';
import { decodeJwtPayload, isSuperAdmin } from '@/lib/auth/jwt';
import { TramitesListToolbar } from './TramitesListToolbar';
import { estadoChipStyle, estadoLabel } from '@/lib/tramites/estados';
import { StatusBadge } from '@/components/atom/StatusBadge';
import type {
  InstanceStatus,
  InstanceSummary,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

/**
 * Track A — vista completa del listado de "Trámites en curso": toolbar de
 * filtros (búsqueda + modalidad + estado) + tabla. Lista las instancias del
 * tenant (GET /instances) y filtra client-side sobre el array (máx ~200 del
 * backend). Cada fila navega al wizard de la instancia; las acciones explícitas
 * (Continuar/Ver) llevan al mismo destino. Se refresca al montar, al pulsar
 * Actualizar y cada vez que cambia `refreshKey`.
 */

// N 03 (RF01) — chip de estado con los 6 estados de negocio en español; labels/colores
// desde la fuente única lib/tramites/estados.ts (fallback titlecase para valores desconocidos).
const estadoChip = (
  estado: InstanceStatus,
): { label: string; bg: string; color: string; border: string } => {
  const style = estadoChipStyle(estado);
  return { label: estadoLabel(estado), bg: style.bg, color: style.color, border: style.border };
};

type Chip = { label: string; bg: string; color: string; border: string };

/**
 * HU #10350 (AC3) — chip de estado async para borradores FINALIZADOS (draft + draftFinalizedAt). El
 * trámite cerró la captura y espera la validación de identidad del cliente; la firma se procesa sola
 * al aprobarse. Precedencia: rechazo → aprobado (firma pendiente / listo para radicar) → pendiente de
 * validación. Devuelve además `ready` cuando ya se puede radicar (identidad aprobada + gates), para que
 * la acción de la fila pase de "Continuar" a "Radicar". Null si no es un borrador finalizado (chip base).
 */
function asyncStatus(item: InstanceSummary): { chip: Chip; ready: boolean } | null {
  if (item.estado !== 'borrador' || !item.draftFinalizedAt) return null;
  const idv = item.identityValidationStatus;

  if (idv === 'rechazado') {
    return {
      chip: { label: 'Validación rechazada', bg: 'rgba(255,78,0,0.10)', color: '#c2410c', border: 'rgba(255,78,0,0.3)' },
      ready: false,
    };
  }

  if (idv === 'aprobado') {
    if (item.signaturePending) {
      return {
        chip: { label: 'Pendiente firma', bg: 'rgba(99,102,241,0.12)', color: '#4f46e5', border: 'rgba(99,102,241,0.3)' },
        ready: false,
      };
    }
    if (item.canSubmit) {
      return {
        chip: { label: 'Listo para radicar', bg: 'rgba(140,198,63,0.15)', color: '#5B8A1F', border: 'rgba(140,198,63,0.4)' },
        ready: true,
      };
    }
    return {
      chip: { label: 'Identidad validada', bg: 'rgba(140,198,63,0.12)', color: '#5B8A1F', border: 'rgba(140,198,63,0.35)' },
      ready: false,
    };
  }

  // en_proceso | enviado | null (sin iniciar) → esperando la validación del cliente.
  return {
    chip: { label: 'Pendiente validación', bg: 'rgba(245,158,11,0.14)', color: '#b45309', border: 'rgba(245,158,11,0.35)' },
    ready: false,
  };
}

function shortDate(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '—';
  return d.toLocaleDateString('es-CO', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  });
}

function vehiculo(item: InstanceSummary): string {
  const text = `${item.vehiculoMarca ?? ''} ${item.vehiculoLinea ?? ''}`.trim();
  return text || '—';
}

const MODALIDAD_SHORT: Record<WizardModalidad, string> = {
  matricula_inicial: 'Matrícula',
  traspaso: 'Traspaso',
};

/**
 * Nombres de paso por modalidad, alineados con TipologiaMatrizCatalog. Solo
 * presentación: `pasoActual`/`totalPasos` se usan tal cual los entrega el API
 * (no se corrige backend en este track). El label es STEP_LABELS[modalidad]
 * [pasoActual - 1], o '—' si el índice no existe.
 */
const STEP_LABELS: Record<WizardModalidad, string[]> = {
  matricula_inicial: [
    'Consulta VIN',
    'Documentos',
    'Comprador',
    'Identidad',
    'Generar FUR',
  ],
  traspaso: [
    'Consulta del vehículo',
    'Documentos',
    'Vendedor',
    'Comprador',
    'Datos comerciales',
    'Generar FUR',
  ],
};

function stepLabel(item: InstanceSummary): string {
  return STEP_LABELS[item.modalidad]?.[item.pasoActual - 1] ?? '—';
}

const GRID_COLS = '1fr 1.3fr 1.2fr 1.2fr 0.9fr 1.4fr 1.1fr 1.3fr 0.9fr 1fr';
// #1 — SuperAdmin: columna "Compañía" como primera columna (ve trámites de TODAS las empresas).
const GRID_COLS_ADMIN = `1.2fr ${GRID_COLS}`;

/** Filas por página en el listado (paginación client-side sobre `filtered`). */
const PAGE_SIZE = 10;

interface TramitesTableProps {
  /** Cambia (incrementa) para forzar un refetch — p. ej. al volver del wizard. */
  refreshKey?: number;
}

export function TramitesTable({ refreshKey = 0 }: TramitesTableProps) {
  const router = useRouter();
  const [items, setItems] = useState<InstanceSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filtros client-side.
  const [search, setSearch] = useState('');
  const [modalidad, setModalidad] = useState<'' | WizardModalidad>('');
  const [estado, setEstado] = useState<'' | InstanceStatus>('');
  // #1 — Filtro por compañía, solo relevante para el SuperAdmin (ve todas las empresas).
  const [compania, setCompania] = useState('');

  // #1 — ¿el caller es SuperAdmin? Determina la columna/filtro Compañía y si al abrir un trámite
  // se pasa el tenant de la fila (?t=) para poder verlo aunque sea de otra empresa. Se resuelve del
  // JWT en cliente tras montar (getToken lee la cookie), por eso vive en estado, no en el render SSR.
  const [isAdmin, setIsAdmin] = useState(false);
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setIsAdmin(isSuperAdmin(decodeJwtPayload(getToken())));
  }, []);

  // Paginación client-side (1-based).
  const [page, setPage] = useState(1);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await tramitesClient.listInstances();
      setItems(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Error desconocido');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    // Carga/refresca al montar y al cambiar refreshKey: los setState de `load`
    // ocurren tras el await (no es setState síncrono).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load, refreshKey]);

  // Compañías presentes en el listado (para el filtro del SuperAdmin), ordenadas.
  const companias = useMemo(() => {
    const set = new Set<string>();
    for (const it of items) if (it.companiaNombre) set.add(it.companiaNombre);
    return [...set].sort((a, b) => a.localeCompare(b, 'es'));
  }, [items]);

  // Filtrado en cadena: búsqueda → modalidad → estado → compañía.
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return items.filter((item) => {
      if (q) {
        const haystack = [
          item.placa,
          item.vin,
          item.referenceNumber,
          item.compradorNombre,
          item.organismoTransito,
          item.companiaNombre,
        ]
          .filter(Boolean)
          .join(' ')
          .toLowerCase();
        if (!haystack.includes(q)) return false;
      }
      if (modalidad && item.modalidad !== modalidad) return false;
      if (estado && item.estado !== estado) return false;
      if (compania && item.companiaNombre !== compania) return false;
      return true;
    });
  }, [items, search, modalidad, estado, compania]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  // Página segura: si los filtros/refetch reducen los resultados por debajo de
  // la página actual, se clampa al último rango válido.
  const safePage = Math.min(page, totalPages);
  const paginated = useMemo(() => {
    const start = (safePage - 1) * PAGE_SIZE;
    return filtered.slice(start, start + PAGE_SIZE);
  }, [filtered, safePage]);

  // Al cambiar cualquier filtro se vuelve a la primera página: la combinación
  // de criterios redefine el conjunto, así que arrancar desde el inicio es lo
  // esperado. Se hace en los handlers (no en un effect) para no reaccionar a
  // cambios derivados.
  const handleSearchChange = (v: string) => {
    setSearch(v);
    setPage(1);
  };
  const handleModalidadChange = (v: '' | WizardModalidad) => {
    setModalidad(v);
    setPage(1);
  };
  const handleEstadoChange = (v: '' | InstanceStatus) => {
    setEstado(v);
    setPage(1);
  };
  const handleCompaniaChange = (v: string) => {
    setCompania(v);
    setPage(1);
  };

  const hasActiveFilters =
    search.trim() !== '' || modalidad !== '' || estado !== '' || compania !== '';

  const clearFilters = () => {
    setSearch('');
    setModalidad('');
    setEstado('');
    setCompania('');
    setPage(1);
  };

  const heading = (
    <h2 className="mb-3 text-sm font-bold">
      Trámites en curso
      {!loading && !error && (
        <span className="opacity-60"> ({items.length})</span>
      )}
    </h2>
  );

  return (
    <section
      className="rounded-2xl border bg-white p-4 shrink-0 dark:bg-[#0B0F14]"
    >
      {heading}

      <div className="flex flex-col gap-3">
        <TramitesListToolbar
          search={search}
          onSearchChange={handleSearchChange}
          modalidad={modalidad}
          onModalidadChange={handleModalidadChange}
          estado={estado}
          onEstadoChange={handleEstadoChange}
          onRefresh={() => void load()}
          onClearFilters={clearFilters}
          loading={loading}
          totalCount={items.length}
          filteredCount={filtered.length}
        />

        {/* #1 — Filtro por compañía (solo SuperAdmin, que ve trámites de todas las empresas). */}
        {isAdmin && companias.length > 0 && (
          <div className="flex items-center gap-2">
            <label htmlFor="filtro-compania" className="text-[11px] font-semibold opacity-60">
              Compañía
            </label>
            <select
              id="filtro-compania"
              value={compania}
              onChange={(e) => handleCompaniaChange(e.target.value)}
              className="rounded-xl border px-3 py-1.5 text-xs"
              style={{ color: '#162744' }}
            >
              <option value="">Todas</option>
              {companias.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
          </div>
        )}

        <TableBody
          loading={loading}
          error={error}
          items={items}
          filtered={filtered}
          paginated={paginated}
          page={safePage}
          totalPages={totalPages}
          onPageChange={setPage}
          hasActiveFilters={hasActiveFilters}
          showCompania={isAdmin}
          onRetry={() => void load()}
          onClearFilters={clearFilters}
          onOpen={(id, tenantId) =>
            router.push(
              isAdmin && tenantId
                ? `/tramites/${id}?t=${encodeURIComponent(tenantId)}`
                : `/tramites/${id}`,
            )
          }
        />
      </div>
    </section>
  );
}

/** Cuerpo de la tabla: maneja los 4 estados (cargando/error/vacío/datos). */
function TableBody({
  loading,
  error,
  items,
  filtered,
  paginated,
  page,
  totalPages,
  onPageChange,
  hasActiveFilters,
  showCompania,
  onRetry,
  onClearFilters,
  onOpen,
}: {
  loading: boolean;
  error: string | null;
  items: InstanceSummary[];
  filtered: InstanceSummary[];
  paginated: InstanceSummary[];
  page: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  hasActiveFilters: boolean;
  showCompania: boolean;
  onRetry: () => void;
  onClearFilters: () => void;
  onOpen: (id: string, tenantId: string) => void;
}) {
  const gridCols = showCompania ? GRID_COLS_ADMIN : GRID_COLS;
  if (loading) {
    return (
      <div
        className="flex flex-col gap-2"
        aria-busy="true"
        aria-label="Cargando trámites"
      >
        {Array.from({ length: 3 }).map((_, i) => (
          <div
            key={i}
            className="h-12 rounded-xl animate-pulse"
            style={{ background: 'rgba(223,229,237,0.5)' }}
          />
        ))}
      </div>
    );
  }

  if (error) {
    return (
      <div
        className="flex flex-col items-center justify-center gap-3 py-10 text-center"
        role="alert"
      >
        <p className="text-sm font-bold">Error al cargar trámites</p>
        <p className="text-xs opacity-60 max-w-xs">{error}</p>
        <button
          onClick={onRetry}
          className="px-5 py-2.5 rounded-xl text-xs font-semibold border"
          style={{ borderColor: '#557EFF', color: '#557EFF' }}
          aria-label="Reintentar cargar trámites"
        >
          Reintentar
        </button>
      </div>
    );
  }

  // Vacío sin filtros: no hay ningún trámite todavía.
  if (items.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center gap-2 py-10 text-center">
        <p className="text-sm font-bold">Aún no hay trámites</p>
        <p className="text-xs opacity-60 max-w-xs">
          Inicia un trámite con el selector de modalidad de arriba para verlo
          aquí.
        </p>
      </div>
    );
  }

  // Vacío con filtros: hay trámites pero ninguno coincide.
  if (filtered.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center gap-3 py-10 text-center">
        <p className="text-sm font-bold">Sin resultados</p>
        <p className="text-xs opacity-60 max-w-xs">
          Ningún trámite coincide con la búsqueda o los filtros aplicados.
        </p>
        {hasActiveFilters && (
          <button
            onClick={onClearFilters}
            className="px-5 py-2.5 rounded-xl text-xs font-semibold border"
            style={{ borderColor: '#557EFF', color: '#557EFF' }}
            aria-label="Limpiar filtros"
          >
            Limpiar filtros
          </button>
        )}
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <div className={showCompania ? 'min-w-[1340px]' : 'min-w-[1180px]'}>
        {/* Header */}
        <div
          className="grid items-center text-[11px] uppercase tracking-wider font-semibold rounded-xl px-4 py-3"
          style={{
            background: '#dfe5ed',
            color: '#162744',
            gridTemplateColumns: gridCols,
          }}
          role="row"
        >
          {showCompania && <div>Compañía</div>}
          <div>Placa</div>
          <div>Comprador</div>
          <div>VIN</div>
          <div>Vehículo</div>
          <div>Modalidad</div>
          <div>Paso</div>
          <div>Estado</div>
          <div>Organismo</div>
          <div>Creado</div>
          <div className="text-right">Acciones</div>
        </div>

        {/* Rows */}
        <ul className="space-y-2 mt-2" aria-label="Trámites en curso">
          {paginated.map((item) => (
            <TramiteRow
              key={item.id}
              item={item}
              showCompania={showCompania}
              gridCols={gridCols}
              onOpen={onOpen}
            />
          ))}
        </ul>

        <Pagination
          page={page}
          totalPages={totalPages}
          total={filtered.length}
          shown={paginated.length}
          onPageChange={onPageChange}
        />
      </div>
    </div>
  );
}

/**
 * Control de paginación client-side. Se oculta cuando todo cabe en una sola
 * página (totalPages <= 1). Estilo FLIT: borde #DFE5ED, acento #557EFF.
 */
function Pagination({
  page,
  totalPages,
  total,
  shown,
  onPageChange,
}: {
  page: number;
  totalPages: number;
  total: number;
  shown: number;
  onPageChange: (page: number) => void;
}) {
  if (totalPages <= 1) return null;

  const from = (page - 1) * PAGE_SIZE + 1;
  const to = from + shown - 1;

  return (
    <nav
      className="mt-3 flex items-center justify-between gap-3 border-t pt-3"
      aria-label="Paginación de trámites"
    >
      <p className="text-[11px] opacity-60" role="status" aria-live="polite">
        {from}–{to} de {total}
      </p>
      <div className="flex items-center gap-2">
        <button
          type="button"
          onClick={() => onPageChange(page - 1)}
          disabled={page <= 1}
          className="rounded-xl border px-3 py-1.5 text-[11px] font-semibold transition disabled:opacity-40"
          style={{ color: '#162744' }}
          aria-label="Página anterior"
        >
          Anterior
        </button>
        <span className="text-[11px] font-semibold tabular-nums opacity-70">
          {page} / {totalPages}
        </span>
        <button
          type="button"
          onClick={() => onPageChange(page + 1)}
          disabled={page >= totalPages}
          className="rounded-xl border px-3 py-1.5 text-[11px] font-semibold transition disabled:opacity-40"
          style={{ borderColor: '#557EFF', color: '#557EFF' }}
          aria-label="Página siguiente"
        >
          Siguiente
        </button>
      </div>
    </nav>
  );
}

/** Fila de trámite: clickable (abre el wizard) + acción explícita Continuar/Ver. */
function TramiteRow({
  item,
  showCompania,
  gridCols,
  onOpen,
}: {
  item: InstanceSummary;
  showCompania: boolean;
  gridCols: string;
  onOpen: (id: string, tenantId: string) => void;
}) {
  // HU #10350 — un borrador finalizado muestra un chip async ("Pendiente validación"/"Pendiente
  // firma"/"Listo para radicar"); el resto usa el chip base de estado. `ready` promueve la acción a
  // "Radicar" cuando la identidad ya quedó aprobada y los gates están listos.
  const async = asyncStatus(item);
  const chip = async?.chip ?? estadoChip(item.estado);
  const isDraft = item.estado === 'borrador';
  const actionLabel = async?.ready ? 'Radicar' : isDraft ? 'Continuar' : 'Ver';

  return (
    <li>
      <div
        role="button"
        tabIndex={0}
        onClick={() => onOpen(item.id, item.tenantId)}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            onOpen(item.id, item.tenantId);
          }
        }}
        className="w-full grid cursor-pointer items-center bg-white dark:bg-[#162744] rounded-xl px-4 py-3 text-sm shadow-[0_2px_8px_rgba(22,39,68,0.05)] transition hover:shadow-[0_4px_14px_rgba(22,39,68,0.12)] hover:ring-1 hover:ring-[#557EFF]/40 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
        style={{ gridTemplateColumns: gridCols }}
        aria-label={`Abrir trámite ${item.referenceNumber}`}
      >
        {showCompania && (
          <span className="block text-xs font-semibold text-[#162744]/90 dark:text-white/80 truncate">
            {item.companiaNombre ?? '—'}
          </span>
        )}
        <span className="min-w-0">
          <span className="font-mono font-semibold text-[#162744] dark:text-white truncate block">
            {item.placa ?? '—'}
          </span>
          <span className="text-[10px] font-mono text-[#162744]/50 dark:text-white/50 truncate block">
            {item.referenceNumber}
          </span>
        </span>
        <span className="block text-[#162744] dark:text-white/90 truncate">
          {item.compradorNombre ?? '—'}
        </span>
        <span className="block font-mono text-xs text-[#162744]/80 dark:text-white/70 truncate">
          {item.vin ?? '—'}
        </span>
        <span className="block text-[#162744]/90 dark:text-white/80 truncate">
          {vehiculo(item)}
        </span>
        <span className="block">
          <span
            className="text-[10px] font-semibold px-2 py-0.5 rounded-full border whitespace-nowrap"
            style={{
              background: 'rgba(85,126,255,0.08)',
              color: '#557eff',
              borderColor: 'rgba(85,126,255,0.25)',
            }}
          >
            {MODALIDAD_SHORT[item.modalidad]}
          </span>
        </span>
        <span className="block min-w-0">
          <span className="block font-mono text-xs text-[#162744]/70 dark:text-white/60">
            {item.pasoActual}/{item.totalPasos}
          </span>
          <span className="block text-[10px] text-[#162744]/60 dark:text-white/50 truncate">
            {stepLabel(item)}
          </span>
        </span>
        <span className="block">
          <StatusBadge label={chip.label} bg={chip.bg} color={chip.color} border={chip.border} />
        </span>
        <span className="block text-xs text-[#162744]/90 dark:text-white/80 truncate">
          {item.organismoTransito ?? '—'}
        </span>
        <span className="block font-mono text-xs text-[#162744]/70 dark:text-white/60">
          {shortDate(item.createdAt)}
        </span>
        <span className="flex justify-end">
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation();
              onOpen(item.id, item.tenantId);
            }}
            className="rounded-full px-3 py-1.5 text-[11px] font-semibold whitespace-nowrap transition"
            style={
              isDraft
                ? { background: 'linear-gradient(135deg,#557EFF,#00DBD5)', color: '#fff' }
                : { border: '1px solid #DFE5ED', color: '#162744' }
            }
            aria-label={`${actionLabel} trámite ${item.referenceNumber}`}
          >
            {actionLabel}
          </button>
        </span>
      </div>
    </li>
  );
}
