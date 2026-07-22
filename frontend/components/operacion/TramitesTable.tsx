'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import { ArrowLeftRight, Car, Search, Star, X } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { getToken } from '@/lib/api/client';
import { decodeJwtPayload, isSuperAdmin } from '@/lib/auth/jwt';
import { TramitesListToolbar } from './TramitesListToolbar';
import { estadoLabel, type EstadoTramite } from '@/lib/tramites/estados';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { DataTable, type DataTableColumn } from '@/components/atom/DataTable';
import { EstadoFunnel } from './EstadoFunnel';
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

type Chip = { label: string; tone: StatusTone };

// N 03 (RF01) — chip de estado con los 6 estados de negocio; el color se resuelve por
// `tone` semántico desde la paleta unificada (StatusBadge). Mapeo del vocabulario de negocio:
const ESTADO_TONE: Record<string, StatusTone> = {
  borrador: 'warning',
  preparado: 'info',
  entregado: 'info',
  aprobado: 'success',
  rechazado: 'danger',
  anulado: 'neutral',
};

const estadoChip = (estado: InstanceStatus): Chip => ({
  label: estadoLabel(estado),
  tone: ESTADO_TONE[estado] ?? 'neutral',
});

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
    return { chip: { label: 'Validación rechazada', tone: 'danger' }, ready: false };
  }

  if (idv === 'aprobado') {
    if (item.signaturePending) {
      return { chip: { label: 'Pendiente firma', tone: 'info' }, ready: false };
    }
    if (item.canSubmit) {
      return { chip: { label: 'Listo para radicar', tone: 'success' }, ready: true };
    }
    return { chip: { label: 'Identidad validada', tone: 'success' }, ready: false };
  }

  // en_proceso | enviado | null (sin iniciar) → esperando la validación del cliente.
  return { chip: { label: 'Pendiente validación', tone: 'warning' }, ready: false };
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

/** Filas por página en el listado (paginación client-side sobre `filtered`). */
const PAGE_SIZE = 10;

// Registro de nuevo trámite por modalidad (botones estilo "Nuevo Trámite" del diseño).
const NEW_TRAMITE_ACTIONS: { id: WizardModalidad; label: string; icon: typeof Car }[] = [
  { id: 'matricula_inicial', label: 'Matrícula inicial', icon: Car },
  { id: 'traspaso', label: 'Traspaso estándar', icon: ArrowLeftRight },
];

interface TramitesTableProps {
  /** Cambia (incrementa) para forzar un refetch — p. ej. al volver del wizard. */
  refreshKey?: number;
  /** Inicia un nuevo trámite de la modalidad elegida (navega al wizard). */
  onStartTramite?: (modalidad: WizardModalidad) => void;
}

export function TramitesTable({ refreshKey = 0, onStartTramite }: TramitesTableProps) {
  const router = useRouter();
  const [items, setItems] = useState<InstanceSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filtros client-side.
  const [search, setSearch] = useState('');
  // La búsqueda por placa/VIN está oculta hasta pulsar "Buscar" (paridad con el diseño).
  const [searchOpen, setSearchOpen] = useState(false);
  const [modalidad, setModalidad] = useState<'' | WizardModalidad>('');
  const [estado, setEstado] = useState<'' | InstanceStatus>('');
  // #1 — Filtro por compañía, solo relevante para el SuperAdmin (ve todas las empresas).
  const [compania, setCompania] = useState('');
  // HU #10536 — filtro "solo prioritarios".
  const [soloPrioritarios, setSoloPrioritarios] = useState(false);

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

  // Conteo por estado de negocio (para el funnel de estados). Se calcula sobre el
  // total de trámites cargados, no sobre `filtered`, para que el embudo muestre
  // siempre el panorama completo aunque haya un estado seleccionado.
  const estadoCounts = useMemo(() => {
    const c: Record<EstadoTramite, number> = {
      borrador: 0,
      anulado: 0,
      preparado: 0,
      entregado: 0,
      aprobado: 0,
      rechazado: 0,
    };
    for (const it of items) {
      if (it.estado in c) c[it.estado as EstadoTramite] += 1;
    }
    return c;
  }, [items]);

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
      if (soloPrioritarios && !item.prioritario) return false;
      return true;
    });
  }, [items, search, modalidad, estado, compania, soloPrioritarios]);

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
  const openSearch = () => setSearchOpen(true);
  const closeSearch = () => {
    setSearchOpen(false);
    handleSearchChange('');
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
  const handlePrioritariosChange = (v: boolean) => {
    setSoloPrioritarios(v);
    setPage(1);
  };

  // HU #10536 — marca/desmarca la prioridad con actualización optimista; revierte si el backend falla.
  // No cambia el estado del ciclo de vida, solo el flag de ordenamiento (el listado ya viene ordenado
  // con los prioritarios primero; el reordenamiento visual se aplica al siguiente refetch).
  const handleTogglePriority = useCallback(
    async (id: string, next: boolean, tenantId: string) => {
      setItems((prev) =>
        prev.map((it) => (it.id === id ? { ...it, prioritario: next } : it)),
      );
      try {
        await tramitesClient.setPriority(id, next, isAdmin ? tenantId : undefined);
      } catch {
        setItems((prev) =>
          prev.map((it) => (it.id === id ? { ...it, prioritario: !next } : it)),
        );
      }
    },
    [isAdmin],
  );

  const hasActiveFilters =
    search.trim() !== '' ||
    modalidad !== '' ||
    estado !== '' ||
    compania !== '' ||
    soloPrioritarios;

  const clearFilters = () => {
    setSearch('');
    setModalidad('');
    setEstado('');
    setCompania('');
    setSoloPrioritarios(false);
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
        {/* Funnel de estados (paridad con el diseño): conteo por estado + filtro. */}
        {!loading && !error && items.length > 0 && (
          <EstadoFunnel
            counts={estadoCounts}
            active={estado}
            onSelect={handleEstadoChange}
          />
        )}

        {/* Fila de acciones (paridad con el diseño): registrar nuevo trámite por
            modalidad + búsqueda desplegable, en la misma línea y con el mismo estilo
            (píldora con gradiente). La búsqueda queda oculta hasta pulsar "Buscar". */}
        <div className="flex flex-wrap items-center gap-2" aria-label="Acciones de trámites">
          {NEW_TRAMITE_ACTIONS.map(({ id, label, icon: Icon }) => (
            <button
              key={id}
              type="button"
              onClick={() => onStartTramite?.(id)}
              className="inline-flex items-center gap-2 rounded-xl px-5 py-2.5 text-xs font-semibold text-white transition hover:opacity-95"
              style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
              aria-label={`Iniciar ${label}`}
            >
              <Icon className="h-4 w-4" aria-hidden="true" />
              {label}
            </button>
          ))}
          {searchOpen ? (
            <div className="relative min-w-0 sm:min-w-[220px] flex-1">
              <Search
                className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 opacity-40"
                aria-hidden="true"
              />
              <input
                type="search"
                autoFocus
                value={search}
                onChange={(e) => handleSearchChange(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Escape') closeSearch();
                }}
                placeholder="Buscar por placa, VIN, referencia, comprador u organismo…"
                aria-label="Buscar trámites"
                className="w-full rounded-xl border bg-white py-2.5 pl-9 pr-9 text-xs outline-none focus:border-[#557EFF] dark:bg-[#0B0F14]"
              />
              <button
                type="button"
                onClick={closeSearch}
                aria-label="Cerrar búsqueda"
                className="absolute right-2 top-1/2 grid h-6 w-6 -translate-y-1/2 place-items-center rounded-lg opacity-60 hover:opacity-100"
              >
                <X className="h-3.5 w-3.5" />
              </button>
            </div>
          ) : (
            <button
              type="button"
              onClick={openSearch}
              aria-label="Buscar por placa o VIN"
              className="inline-flex items-center gap-2 rounded-xl px-5 py-2.5 text-xs font-semibold text-white transition hover:opacity-95"
              style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
            >
              <Search className="h-4 w-4" aria-hidden="true" />
              Buscar
            </button>
          )}
        </div>

        <TramitesListToolbar
          modalidad={modalidad}
          onModalidadChange={handleModalidadChange}
          onRefresh={() => void load()}
          onClearFilters={clearFilters}
          loading={loading}
          hasActiveFilters={hasActiveFilters}
          totalCount={items.length}
          filteredCount={filtered.length}
          soloPrioritarios={soloPrioritarios}
          onPrioritariosChange={handlePrioritariosChange}
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
          onPageChange={setPage}
          hasActiveFilters={hasActiveFilters}
          showCompania={isAdmin}
          onRetry={() => void load()}
          onClearFilters={clearFilters}
          onTogglePriority={handleTogglePriority}
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
  onPageChange,
  hasActiveFilters,
  showCompania,
  onRetry,
  onClearFilters,
  onTogglePriority,
  onOpen,
}: {
  loading: boolean;
  error: string | null;
  items: InstanceSummary[];
  filtered: InstanceSummary[];
  paginated: InstanceSummary[];
  page: number;
  onPageChange: (page: number) => void;
  hasActiveFilters: boolean;
  showCompania: boolean;
  onRetry: () => void;
  onClearFilters: () => void;
  onTogglePriority: (id: string, next: boolean, tenantId: string) => void;
  onOpen: (id: string, tenantId: string) => void;
}) {
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

  const columns: DataTableColumn<InstanceSummary>[] = [
    ...(showCompania
      ? ([
          {
            key: 'compania',
            header: 'Compañía',
            render: (item) => (
              <span className="block truncate text-xs font-semibold">{item.companiaNombre ?? '—'}</span>
            ),
          },
        ] as DataTableColumn<InstanceSummary>[])
      : []),
    {
      key: 'placa',
      header: 'Placa',
      render: (item) => (
        <span className="flex min-w-0 items-center gap-2">
          {/* HU #10536 — estrella de prioridad: toggle in-line (no navega la fila). */}
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation();
              onTogglePriority(item.id, !item.prioritario, item.tenantId);
            }}
            aria-pressed={item.prioritario}
            aria-label={
              item.prioritario
                ? `Quitar prioridad al trámite ${item.referenceNumber}`
                : `Marcar como prioritario el trámite ${item.referenceNumber}`
            }
            title={item.prioritario ? 'Prioritario — clic para quitar' : 'Marcar como prioritario'}
            className="shrink-0 rounded-md p-0.5 transition hover:bg-[#557EFF]/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          >
            <Star
              className="h-4 w-4"
              style={
                item.prioritario
                  ? { color: '#F59E0B', fill: '#F59E0B' }
                  : { color: '#162744', opacity: 0.3 }
              }
              aria-hidden="true"
            />
          </button>
          <span className="min-w-0">
            <span className="block truncate font-mono font-semibold">{item.placa ?? '—'}</span>
            <span className="block truncate font-mono text-[10px] opacity-50">{item.referenceNumber}</span>
          </span>
        </span>
      ),
    },
    {
      key: 'comprador',
      header: 'Comprador',
      render: (item) => <span className="block truncate">{item.compradorNombre ?? '—'}</span>,
    },
    {
      key: 'vin',
      header: 'VIN',
      render: (item) => (
        <span className="block truncate font-mono text-xs opacity-80">{item.vin ?? '—'}</span>
      ),
    },
    {
      key: 'vehiculo',
      header: 'Vehículo',
      render: (item) => <span className="block truncate opacity-90">{vehiculo(item)}</span>,
    },
    {
      key: 'modalidad',
      header: 'Modalidad',
      render: (item) => (
        <span
          className="whitespace-nowrap rounded-full border px-2 py-0.5 text-[10px] font-semibold"
          style={{ background: 'rgba(85,126,255,0.08)', color: '#557eff', borderColor: 'rgba(85,126,255,0.25)' }}
        >
          {MODALIDAD_SHORT[item.modalidad]}
        </span>
      ),
    },
    {
      key: 'paso',
      header: 'Paso',
      render: (item) => (
        <span className="block min-w-0">
          <span className="block font-mono text-xs opacity-70">
            {item.pasoActual}/{item.totalPasos}
          </span>
          <span className="block truncate text-[10px] opacity-60">{stepLabel(item)}</span>
        </span>
      ),
    },
    {
      key: 'estado',
      header: 'Estado',
      render: (item) => {
        // Chip async para borradores finalizados; si no, el chip base del estado.
        const asyncSt = asyncStatus(item);
        const chip = asyncSt?.chip ?? estadoChip(item.estado);
        return <StatusBadge tone={chip.tone} label={chip.label} />;
      },
    },
    {
      key: 'organismo',
      header: 'Organismo',
      render: (item) => (
        <span className="block truncate text-xs opacity-90">{item.organismoTransito ?? '—'}</span>
      ),
    },
    {
      key: 'creado',
      header: 'Creado',
      render: (item) => (
        <span className="block font-mono text-xs opacity-70">{shortDate(item.createdAt)}</span>
      ),
    },
    {
      key: 'acciones',
      header: 'Acciones',
      align: 'right',
      render: (item) => {
        const asyncSt = asyncStatus(item);
        const isDraft = item.estado === 'borrador';
        const actionLabel = asyncSt?.ready ? 'Radicar' : isDraft ? 'Continuar' : 'Ver';
        return (
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation();
              onOpen(item.id, item.tenantId);
            }}
            className="whitespace-nowrap rounded-full px-3 py-1.5 text-[11px] font-semibold transition"
            style={
              isDraft
                ? { background: 'linear-gradient(135deg,#557EFF,#00DBD5)', color: '#fff' }
                : { border: '1px solid #DFE5ED', color: '#162744' }
            }
            aria-label={`${actionLabel} trámite ${item.referenceNumber}`}
          >
            {actionLabel}
          </button>
        );
      },
    },
  ];

  return (
    <DataTable
      columns={columns}
      rows={paginated}
      getRowKey={(item) => item.id}
      onRowClick={(item) => onOpen(item.id, item.tenantId)}
      minWidth={showCompania ? 1340 : 1180}
      ariaLabel="Trámites en curso"
      pagination={{ page, pageSize: PAGE_SIZE, totalCount: filtered.length, onPageChange }}
    />
  );
}

