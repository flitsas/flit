'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { formatFecha } from '@/lib/format/date';
import { useRouter } from 'next/navigation';
import {
  AlertCircle,
  ArrowLeftRight,
  Car,
  FileStack,
  FileText,
  Search,
  Star,
  X,
} from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { getToken } from '@/lib/api/client';
import { decodeJwtPayload, isSuperAdmin } from '@/lib/auth/jwt';
import { TramitesListToolbar } from './TramitesListToolbar';
import { estadoChipStyle, estadoLabel, type EstadoTramite } from '@/lib/tramites/estados';
import { StatusBadge } from '@/components/atom/StatusBadge';
import { RowActions } from '@/components/atom/RowActions';
import { EstadoFunnel } from './EstadoFunnel';
import {
  AttachmentPreview,
  TramiteDocumentosModal,
  useAttachmentPreview,
} from './TramiteDocumentosModal';
import type {
  FirmaParteEstado,
  InstanceStatus,
  InstanceSummary,
  TramiteFuente,
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

// HU #11018 — formato de negocio unico: AÑO/MES/DIA, sin hora.
function shortDate(iso: string): string {
  return formatFecha(iso);
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

/**
 * HU #11057 — chip de firma de la compraventa POR PARTE (vendedor y comprador tienen columna propia).
 * `no_solicitada` no es lo mismo que `pendiente_envio`: la primera es "aún no se pidió", la segunda
 * "ya se pidió y falta enviarla al proveedor"; para el gestor son acciones distintas.
 */
const FIRMA_CHIP: Record<FirmaParteEstado, Chip> = {
  firmada: { label: 'Firmada', bg: 'rgba(140,198,63,0.15)', color: '#5B8A1F', border: 'rgba(140,198,63,0.4)' },
  enviada: { label: 'Enviada', bg: 'rgba(99,102,241,0.12)', color: '#4f46e5', border: 'rgba(99,102,241,0.3)' },
  pendiente_envio: { label: 'Por enviar', bg: 'rgba(245,158,11,0.14)', color: '#b45309', border: 'rgba(245,158,11,0.35)' },
  no_solicitada: { label: 'Sin solicitar', bg: 'rgba(22,39,68,0.06)', color: '#4b5563', border: 'rgba(22,39,68,0.18)' },
  rechazada: { label: 'Rechazada', bg: 'rgba(255,78,0,0.10)', color: '#c2410c', border: 'rgba(255,78,0,0.3)' },
  cancelada: { label: 'Cancelada', bg: 'rgba(22,39,68,0.06)', color: '#4b5563', border: 'rgba(22,39,68,0.18)' },
};

/** HU #11057 — etiqueta de la columna Fuente. No hay "QX": Quipux es canal de salida, no de entrada. */
const FUENTE_LABEL: Record<TramiteFuente, string> = {
  dashboard: 'Dashboard',
  integracion: 'Integración',
  migrado: 'Migrado',
};

// HU #11020 — se añade la columna Vendedor antes de Comprador (saliente → entrante), coherente con
// el expediente y el resumen del último paso.
// HU #11057 — columnas acordadas con el negocio: radicado · VIN · placa · trámite/modalidad ·
// propietario/vendedor + su firma · comprador + su firma · fecha de creación · fecha de actualización ·
// secretaría · gestor · fuente · acciones. El negocio pidió "visualizar MÍNIMO" esa información, así
// que se conservan además Vehículo, Paso y Estado (Estado y Paso son los chips que orientan la acción
// de la fila; quitarlos sería una regresión funcional). Con 17 columnas la tabla se navega con
// desplazamiento horizontal: el ancho mínimo crece en consecuencia y la cabecera comparte el mismo
// `gridTemplateColumns` que las filas para no desalinearse (ver docs/reporte-desalineamiento-tablas.md).
const GRID_COLS = [
  '1.2fr', // Radicado (+ estrella de prioridad)
  '1.1fr', // VIN
  '0.9fr', // Placa
  '0.9fr', // Trámite / Modalidad
  '1.1fr', // Vehículo
  '1.2fr', // Propietario / vendedor
  '0.9fr', // Firmado (vendedor)
  '1.2fr', // Comprador
  '0.9fr', // Firmado (comprador)
  '1fr',   // Paso
  '1.2fr', // Estado
  '0.9fr', // Fecha de creación
  '0.9fr', // Fecha de actualización
  '1.1fr', // Secretaría
  '1.3fr', // Gestor
  '0.9fr', // Fuente
  '1.2fr', // Acciones
].join(' ');

/**
 * Ancho mínimo del grid con las 17 columnas: por debajo de esto la tabla se desplaza en horizontal
 * dentro de su contenedor (`overflow-x-auto`) en vez de comprimir las celdas.
 */
const MIN_WIDTH = 'min-w-[1900px]';

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

  // HU #11054 / HU #11055 — consulta de documentos desde el listado, sin abrir el wizard. Se guarda
  // el trámite elegido (no solo su id) porque el panel se titula con el radicado y el SuperAdmin
  // necesita el tenant de la fila.
  const [docsTramite, setDocsTramite] = useState<InstanceSummary | null>(null);
  const [consolidadoTramite, setConsolidadoTramite] = useState<InstanceSummary | null>(null);

  // Paginación client-side (1-based).
  const [page, setPage] = useState(1);
  /** Popover de motivo OT / subsanación abierto (un solo id a la vez). */
  const [openPopoverId, setOpenPopoverId] = useState<string | null>(null);

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
      // HU #10874 — no tiene tarjeta propia en el funnel (FUNNEL_ORDER no la incluye), pero el
      // tipo Record<EstadoTramite, number> exige la clave; se cuenta igual por completitud.
      subsanacion: 0,
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
          item.vendedorNombre,
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

  // HU #11055 — visor del consolidado. El resumen ya trae el id del adjunto, así que no hace falta
  // consultar los adjuntos del trámite: se abre directo. El disparo va en un effect porque el hook
  // toma el instanceId del render, y en el clic el trámite elegido todavía no está en estado.
  const consolidadoPreview = useAttachmentPreview(
    consolidadoTramite?.id ?? null,
    isAdmin ? consolidadoTramite?.tenantId : undefined,
  );
  const abrirConsolidado = consolidadoPreview.open;
  useEffect(() => {
    const attachmentId = consolidadoTramite?.consolidadoAttachmentId;
    if (!consolidadoTramite || !attachmentId) return;
    void abrirConsolidado({
      id: attachmentId,
      tipo: 'consolidado',
      filename: `expediente-consolidado-${consolidadoTramite.referenceNumber}.pdf`,
      mimetype: 'application/pdf',
    });
  }, [consolidadoTramite, abrirConsolidado]);

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
      className="rounded-2xl border bg-white p-4 shrink-0 min-w-0 dark:bg-[#0B0F14]"
    >
      {heading}

      <div className="flex min-w-0 flex-col gap-3">
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
            <div className="relative min-w-[220px] flex-1">
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
          totalPages={totalPages}
          onPageChange={setPage}
          hasActiveFilters={hasActiveFilters}
          openPopoverId={openPopoverId}
          onTogglePopover={(id) => setOpenPopoverId((prev) => (prev === id ? null : id))}
          onClosePopover={() => setOpenPopoverId(null)}
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
          onVerDocumentos={setDocsTramite}
          onVerConsolidado={setConsolidadoTramite}
        />
      </div>

      {/* HU #11054 — panel de documentos del expediente sobre el propio listado. */}
      <TramiteDocumentosModal
        open={docsTramite !== null}
        onClose={() => setDocsTramite(null)}
        instanceId={docsTramite?.id ?? null}
        referenceNumber={docsTramite?.referenceNumber ?? ''}
        tenantId={isAdmin ? docsTramite?.tenantId : undefined}
      />

      {/* HU #11055 — visor del consolidado, abierto directo desde la fila. */}
      <AttachmentPreview
        preview={{
          ...consolidadoPreview,
          close: () => {
            consolidadoPreview.close();
            setConsolidadoTramite(null);
          },
        }}
      />
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
  openPopoverId,
  onTogglePopover,
  onClosePopover,
  onRetry,
  onClearFilters,
  onTogglePriority,
  onOpen,
  onVerDocumentos,
  onVerConsolidado,
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
  openPopoverId: string | null;
  onTogglePopover: (id: string) => void;
  onClosePopover: () => void;
  onRetry: () => void;
  onClearFilters: () => void;
  onTogglePriority: (id: string, next: boolean, tenantId: string) => void;
  onOpen: (id: string, tenantId: string) => void;
  onVerDocumentos: (item: InstanceSummary) => void;
  onVerConsolidado: (item: InstanceSummary) => void;
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

    return (
    <div className="overflow-x-auto">
      <div className={MIN_WIDTH}>
        {/* Header */}
        <div
          className="grid items-center text-[11px] uppercase tracking-wider font-semibold rounded-xl px-4 py-3"
          style={{
            background: '#dfe5ed',
            color: '#162744',
            gridTemplateColumns: GRID_COLS,
          }}
          role="row"
        >
          <div>Radicado</div>
          <div>VIN</div>
          <div>Placa</div>
          <div>Trámite / Modalidad</div>
          <div>Vehículo</div>
          <div>Propietario / vendedor</div>
          {/* Dos columnas "Firmado": el negocio las rotula igual, así que el sufijo va oculto
              visualmente para que cada cabecera tenga un nombre accesible propio. */}
          <div>
            Firmado<span className="sr-only"> (vendedor)</span>
          </div>
          <div>Comprador</div>
          <div>
            Firmado<span className="sr-only"> (comprador)</span>
          </div>
          <div>Paso</div>
          <div>Estado</div>
          <div>Fecha de creación</div>
          <div>Fecha de actualización</div>
          <div>Secretaría</div>
          <div>Gestor</div>
          <div>Fuente</div>
          <div className="text-right">Acciones</div>
        </div>

        {/* Rows */}
        <ul className="space-y-2 mt-2" aria-label="Trámites en curso">
          {paginated.map((item) => (
            <TramiteRow
              key={item.id}
              item={item}
              popoverOpen={openPopoverId === item.id}
              onTogglePopover={onTogglePopover}
              onClosePopover={onClosePopover}
              onTogglePriority={onTogglePriority}
              onOpen={onOpen}
              onVerDocumentos={onVerDocumentos}
              onVerConsolidado={onVerConsolidado}
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

/**
 * HU #11057 — celda de firma de una parte. `null` = NO APLICA (matrícula inicial no tiene
 * compraventa): se muestra un guion, no un chip de pendiente, para no dar una falsa alarma.
 */
function FirmaCell({
  estado,
  parte,
}: {
  estado?: FirmaParteEstado | null;
  parte: 'vendedor' | 'comprador';
}) {
  if (!estado) {
    return (
      <span
        className="block text-xs text-[#162744]/40 dark:text-white/30"
        title={`No aplica: este trámite no tiene compraventa que firme el ${parte}`}
      >
        —
      </span>
    );
  }
  const chip = FIRMA_CHIP[estado];
  return (
    <span className="block">
      <StatusBadge label={chip.label} bg={chip.bg} color={chip.color} border={chip.border} />
    </span>
  );
}

/** Fila de trámite: clickable (abre el wizard) + acciones explícitas (documentos, consolidado, Continuar/Ver). */
function TramiteRow({
  item,
  popoverOpen,
  onTogglePopover,
  onClosePopover,
  onTogglePriority,
  onOpen,
  onVerDocumentos,
  onVerConsolidado,
}: {
  item: InstanceSummary;
  popoverOpen: boolean;
  onTogglePopover: (id: string) => void;
  onClosePopover: () => void;
  onTogglePriority: (id: string, next: boolean, tenantId: string) => void;
  onOpen: (id: string, tenantId: string) => void;
  onVerDocumentos: (item: InstanceSummary) => void;
  onVerConsolidado: (item: InstanceSummary) => void;
}) {
  // HU #11055 — la acción del consolidado solo existe si el expediente ya está generado (el resumen
  // trae el id del adjunto): el botón NUNCA dispara una generación.
  const consolidadoDisponible = !!item.consolidadoAttachmentId;
  // HU #10350 — un borrador finalizado muestra un chip async ("Pendiente validación"/"Pendiente
  // firma"/"Listo para radicar"); el resto usa el chip base de estado. `ready` promueve la acción a
  // "Radicar" cuando la identidad ya quedó aprobada y los gates están listos.
  const async = asyncStatus(item);
  const chip = async?.chip ?? estadoChip(item.estado);
  const isDraft = item.estado === 'borrador';
  const actionLabel = async?.ready ? 'Radicar' : isDraft ? 'Continuar' : 'Ver';
  const motivoRechazo = item.ultimoRechazoMotivo?.trim() || null;
  const subsanacionCount = item.subsanacionCount ?? 0;
  const enSubsanacion = !!item.subsanacionActiva;
  const showRejectPopover =
    !!motivoRechazo || enSubsanacion || subsanacionCount > 0;
  const popoverRef = useRef<HTMLDivElement>(null);
  const iconColor = enSubsanacion ? '#b45309' : '#c2410c';

  useEffect(() => {
    if (!popoverOpen) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClosePopover();
    };
    const onPointer = (e: MouseEvent) => {
      if (popoverRef.current && !popoverRef.current.contains(e.target as Node)) {
        onClosePopover();
      }
    };
    document.addEventListener('keydown', onKey);
    document.addEventListener('mousedown', onPointer);
    return () => {
      document.removeEventListener('keydown', onKey);
      document.removeEventListener('mousedown', onPointer);
    };
  }, [popoverOpen, onClosePopover]);

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
        className="w-full grid cursor-pointer items-center bg-white dark:bg-[#162744] rounded-xl border px-4 py-3 text-sm transition hover:border-[#557EFF]/40 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
        style={{ gridTemplateColumns: GRID_COLS }}
        aria-label={`Abrir trámite ${item.referenceNumber}`}
      >
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
          <span className="min-w-0 font-mono font-semibold text-[#162744] dark:text-white truncate block">
            {item.referenceNumber}
          </span>
        </span>
        <span className="block font-mono text-xs text-[#162744]/80 dark:text-white/70 truncate">
          {item.vin ?? '—'}
        </span>
        <span className="block font-mono font-semibold text-[#162744] dark:text-white truncate">
          {item.placa ?? '—'}
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
        <span className="block text-[#162744]/90 dark:text-white/80 truncate">
          {vehiculo(item)}
        </span>
        <span className="block text-[#162744] dark:text-white/90 truncate">
          {item.vendedorNombre ?? '—'}
        </span>
        <FirmaCell estado={item.firmaVendedorEstado} parte="vendedor" />
        <span className="block text-[#162744] dark:text-white/90 truncate">
          {item.compradorNombre ?? '—'}
        </span>
        <FirmaCell estado={item.firmaCompradorEstado} parte="comprador" />
        <span className="block min-w-0">
          <span className="block font-mono text-xs text-[#162744]/70 dark:text-white/60">
            {item.pasoActual}/{item.totalPasos}
          </span>
          <span className="block text-[10px] text-[#162744]/60 dark:text-white/50 truncate">
            {stepLabel(item)}
          </span>
        </span>
        <span className="relative flex min-w-0 items-center gap-1.5">
          <StatusBadge label={chip.label} bg={chip.bg} color={chip.color} border={chip.border} />
          {showRejectPopover ? (
            <div ref={popoverRef} className="relative shrink-0">
              <button
                type="button"
                onClick={(e) => {
                  e.stopPropagation();
                  onTogglePopover(item.id);
                }}
                aria-expanded={popoverOpen}
                aria-haspopup="dialog"
                aria-label={`Ver detalle de rechazo / subsanación de ${item.referenceNumber}`}
                title="Ver motivo del OT y subsanación"
                className="rounded-md p-0.5 transition hover:bg-[#FF4E00]/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#FF4E00]"
              >
                <AlertCircle
                  className="h-3.5 w-3.5"
                  style={{ color: iconColor }}
                  aria-hidden="true"
                />
              </button>
              {popoverOpen ? (
                <div
                  role="dialog"
                  aria-label={`Detalle de rechazo de ${item.referenceNumber}`}
                  className="absolute left-0 top-full z-20 mt-1 w-72 rounded-xl border bg-white p-3 shadow-lg dark:bg-[#162744]"
                  style={{ borderColor: 'rgba(255,78,0,0.28)' }}
                  onClick={(e) => e.stopPropagation()}
                >
                  {motivoRechazo ? (
                    <div>
                      <p className="text-[11px] font-semibold uppercase tracking-wide text-[#c2410c]">
                        Motivo del OT
                      </p>
                      <p className="mt-1 text-sm text-[#162744] dark:text-white/90 whitespace-pre-wrap">
                        {motivoRechazo}
                      </p>
                    </div>
                  ) : null}
                  {enSubsanacion || subsanacionCount > 0 ? (
                    <div
                      className={`flex flex-wrap gap-1.5 ${motivoRechazo ? 'mt-2.5 border-t pt-2.5' : ''}`}
                      style={
                        motivoRechazo
                          ? { borderColor: 'rgba(223,229,237,0.8)' }
                          : undefined
                      }
                    >
                      {enSubsanacion ? (
                        <span
                          className="text-[10px] font-semibold px-2 py-0.5 rounded-full border whitespace-nowrap"
                          style={{
                            background: 'rgba(245,158,11,0.12)',
                            color: '#b45309',
                            borderColor: 'rgba(245,158,11,0.3)',
                          }}
                        >
                          En subsanación
                        </span>
                      ) : null}
                      {subsanacionCount > 0 ? (
                        <span
                          className="text-[10px] font-semibold px-2 py-0.5 rounded-full border whitespace-nowrap"
                          style={{
                            background: 'rgba(99,102,241,0.10)',
                            color: '#4f46e5',
                            borderColor: 'rgba(99,102,241,0.28)',
                          }}
                        >
                          Subsanado ×{subsanacionCount}
                        </span>
                      ) : null}
                    </div>
                  ) : null}
                </div>
              ) : null}
            </div>
          ) : null}
        </span>
        <span className="block font-mono text-xs text-[#162744]/70 dark:text-white/60">
          {shortDate(item.createdAt)}
        </span>
        {/* Sin modificaciones desde que se creó ⇒ no hay fecha de actualización que mostrar. */}
        <span className="block font-mono text-xs text-[#162744]/70 dark:text-white/60">
          {item.updatedAt ? shortDate(item.updatedAt) : '—'}
        </span>
        <span className="block text-xs text-[#162744]/90 dark:text-white/80 truncate">
          {item.organismoTransito ?? '—'}
        </span>
        {/* Gestor = empresa que radica + persona que la operó. Sustituye a la antigua columna
            "Compañía" del SuperAdmin: era exactamente la misma razón social, sin la persona. El
            filtro por compañía sigue existiendo (arriba), y ahora todos los perfiles ven el dato. */}
        <span className="block min-w-0">
          <span className="block truncate text-xs font-semibold text-[#162744] dark:text-white">
            {item.companiaNombre ?? '—'}
          </span>
          <span className="block truncate text-[10px] text-[#162744]/60 dark:text-white/50">
            {item.gestorNombre ?? '—'}
          </span>
        </span>
        <span className="block text-xs text-[#162744]/90 dark:text-white/80 truncate">
          {FUENTE_LABEL[item.fuente ?? 'dashboard']}
        </span>
        {/* La fila entera navega al wizard, así que las acciones detienen la propagación en un
            envoltorio: `RowActions` no recibe el evento, y así no hay que filtrarlo acción por acción. */}
        <span
          className="flex items-center justify-end gap-1"
          onClick={(e) => e.stopPropagation()}
          onKeyDown={(e) => e.stopPropagation()}
        >
          <RowActions
            actions={[
              {
                icon: FileText,
                label: `Ver documentos del trámite ${item.referenceNumber}`,
                onClick: () => onVerDocumentos(item),
              },
              // HU #11055 — el negocio pidió la acción "sólo visible si ya se encuentra generado":
              // se OMITE cuando no hay consolidado, en vez de mostrarse deshabilitada. Así el botón
              // nunca puede disparar una generación (que es justo lo que se quiso evitar).
              ...(consolidadoDisponible
                ? [
                    {
                      icon: FileStack,
                      label: `Ver expediente consolidado del trámite ${item.referenceNumber}`,
                      onClick: () => onVerConsolidado(item),
                      tone: 'primary' as const,
                    },
                  ]
                : []),
            ]}
          />
          <button
            type="button"
            onClick={() => onOpen(item.id, item.tenantId)}
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
