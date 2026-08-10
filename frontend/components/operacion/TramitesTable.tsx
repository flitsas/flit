'use client';

import { Fragment, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { formatFecha } from '@/lib/format/date';
import { useRouter } from 'next/navigation';
import {
  AlertCircle,
  ArrowDown,
  ArrowLeftRight,
  ArrowUp,
  ArrowUpDown,
  Car,
  CheckCircle2,
  ChevronDown,
  ChevronUp,
  Eye,
  FileCheck,
  FileStack,
  FileText,
  Pause,
  Play,
  Search,
  Star,
  X,
} from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { getToken } from '@/lib/api/client';
import { decodeJwtPayload, isSuperAdmin } from '@/lib/auth/jwt';
import { TramitesListToolbar } from './TramitesListToolbar';
import { estadoChipStyle, estadoLabel, type EstadoTramite } from '@/lib/tramites/estados';
import {
  TRAMITES_COLUMNS,
  DEFAULT_TRAMITES_VISIBLE_COLUMNS,
  buildTramitesGridLayout,
  tramitesColumnToSortBy,
  type TramitesColumnDef,
  type TramitesGridLayout,
} from '@/lib/tramites/tramites-table-columns';
import { useUiPreferences } from '@/hooks/useUiPreferences';
import { StatusBadge } from '@/components/atom/StatusBadge';
import { Modal } from '@/components/atom/Modal';
import { ActionsMenu, type ActionsMenuItem } from '@/components/atom/ActionsMenu';
import { ColumnSelector } from '@/components/atom/ColumnSelector';
import { InlineAlert } from '@/components/atom/InlineAlert';
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
  ListInstancesParams,
  TramiteFuente,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

const FILTER_INPUT_CLS =
  'mt-1 w-full rounded-xl border border-[#DFE5ED] bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF] dark:border-white/15';
const FILTER_FORM_CLS =
  'grid grid-cols-1 gap-3 rounded-2xl border bg-white p-4 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 dark:bg-[#0B0F14]';
/** Tope del camino filtrado del backend (mismo MaxItems del API). */
const SERVER_LIST_TAKE = 200;

/** Texto corto y discreto del sub-estado de placa (debajo del chip de estado). */
function plateFlowHint(status: string | null | undefined): string | null {
  if (status === 'asignado') return 'Placa asignada por el OT';
  if (status === 'preasignado') return 'Esperando placa del OT';
  if (status === 'terminado') return 'Listo para el OT';
  return null;
}

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
    'Datos y Documentos del Trámite',
    'Comprador',
    'Identidad',
    'Resumen del trámite',
  ],
  traspaso: [
    'Consulta del vehículo',
    'Datos y Documentos del Trámite',
    'Vendedor',
    'Comprador',
    'Datos comerciales',
    'Resumen del trámite',
  ],
};

function stepLabel(item: InstanceSummary): string {
  return STEP_LABELS[item.modalidad]?.[item.pasoActual - 1] ?? '—';
}

/**
 * HU #11057 — chip de acreditación de una parte (identidad o firma del baúl).
 * Va dentro de la misma celda del actor (propietario/vendedor o comprador).
 */
const FIRMA_CHIP: Record<FirmaParteEstado, Chip> = {
  firmado: { label: 'Firmado', bg: 'rgba(140,198,63,0.15)', color: '#5B8A1F', border: 'rgba(140,198,63,0.4)' },
  pendiente: { label: 'Pendiente', bg: 'rgba(245,158,11,0.14)', color: '#b45309', border: 'rgba(245,158,11,0.35)' },
  rechazado: { label: 'Rechazado', bg: 'rgba(255,78,0,0.10)', color: '#c2410c', border: 'rgba(255,78,0,0.3)' },
};

/** HU #11057 — etiqueta de la columna Fuente. No hay "QX": Quipux es canal de salida, no de entrada. */
const FUENTE_LABEL: Record<TramiteFuente, string> = {
  dashboard: 'Dashboard',
  integracion: 'Integración',
  migrado: 'Migrado',
};

// Selector de columnas — el ancho/orden de cada columna vive en TRAMITES_COLUMNS
// (lib/tramites/tramites-table-columns.ts); `buildTramitesGridLayout` calcula el
// `gridTemplateColumns` (Selección + visibles + Acciones) UNA sola vez a partir de las columnas
// visibles, y tanto la cabecera como cada fila lo reciben ya calculado: quedan alineadas por
// construcción sin importar cuántas columnas se oculten.

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
  /** Bloqueo de creación por modalidad (config compañía → Trámites). */
  const [blockNew, setBlockNew] = useState<{ matricula: boolean; traspaso: boolean }>({
    matricula: false,
    traspaso: false,
  });

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

  // Filtros server-side (mismo contrato que GET /instances). Borrador del form → se aplican
  // solo con "Aplicar filtros"; el sort sí recarga de inmediato al clic en cabecera.
  const [placaFilter, setPlacaFilter] = useState('');
  const [vendedorFilter, setVendedorFilter] = useState('');
  const [compradorFilter, setCompradorFilter] = useState('');
  const [gestorFilter, setGestorFilter] = useState('');
  const [firmadoFilter, setFirmadoFilter] = useState<'' | 'true' | 'false'>('');
  const [createdFromFilter, setCreatedFromFilter] = useState('');
  const [createdToFilter, setCreatedToFilter] = useState('');
  const [updatedFromFilter, setUpdatedFromFilter] = useState('');
  const [updatedToFilter, setUpdatedToFilter] = useState('');
  const [appliedPlaca, setAppliedPlaca] = useState('');
  const [appliedVendedor, setAppliedVendedor] = useState('');
  const [appliedComprador, setAppliedComprador] = useState('');
  const [appliedGestor, setAppliedGestor] = useState('');
  const [appliedFirmado, setAppliedFirmado] = useState<'' | 'true' | 'false'>('');
  const [appliedCreatedFrom, setAppliedCreatedFrom] = useState('');
  const [appliedCreatedTo, setAppliedCreatedTo] = useState('');
  const [appliedUpdatedFrom, setAppliedUpdatedFrom] = useState('');
  const [appliedUpdatedTo, setAppliedUpdatedTo] = useState('');
  const [sortBy, setSortBy] = useState('');
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('desc');
  /** Panel de filtros avanzados colapsado por defecto para no saturar la pantalla. */
  const [filtersOpen, setFiltersOpen] = useState(false);

  // Selector de columnas: persiste qué columnas ve el usuario (scope "tramites.columns"). Degrada
  // con elegancia — si la preferencia no carga o falla al guardar, la tabla sigue con el default
  // compacto (el hook nunca deja la tabla en blanco ni bloquea su render).
  const {
    visible: visibleColumns,
    saving: savingColumns,
    setVisible: setVisibleColumns,
  } = useUiPreferences('tramites.columns', DEFAULT_TRAMITES_VISIBLE_COLUMNS);
  // Solo reservar la pista del checkbox cuando haya borradores ICT seleccionables; si no, ese
  // hueco vacío se veía como “espacio muerto” al inicio de Radicado.
  const includeSelectColumn = useMemo(
    () => items.some((it) => it.origin === 'ict' && it.estado === 'borrador'),
    [items],
  );
  const gridLayout = useMemo(
    () => buildTramitesGridLayout(visibleColumns, { includeSelectColumn }),
    [visibleColumns, includeSelectColumn],
  );

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
  // ICT (paridad v1 pause-unpause-massive) — selección de trámites ICT para pausar/reanudar en lote.
  const [selectedIds, setSelectedIds] = useState<Set<string>>(() => new Set());

  // Config compañía: qué modalidades no se pueden iniciar (No permitir trámites…).
  useEffect(() => {
    let active = true;
    void tramitesClient
      .getConsultationConfig()
      .then((cfg) => {
        if (!active) return;
        const block = cfg.blockProcedureFamily;
        setBlockNew({
          matricula: block?.matriculas ?? false,
          traspaso: block?.traspaso ?? false,
        });
      })
      .catch(() => {});
    return () => {
      active = false;
    };
  }, []);

  /** Modal Procesar (Asignado → Terminado) desde la tabla. */
  const [processTarget, setProcessTarget] = useState<InstanceSummary | null>(null);
  const [soatPagado, setSoatPagado] = useState(false);
  const [impuestoPagado, setImpuestoPagado] = useState(false);
  const [processActing, setProcessActing] = useState(false);
  const [processError, setProcessError] = useState<string | null>(null);
  /**
   * Salvedad con la que el trámite avanzó (p. ej. sin SOAT vigente). No es un error: el modal se
   * queda abierto mostrándola para que el gestor sepa en qué condiciones lo envió al OT.
   */
  const [processWarning, setProcessWarning] = useState<string | null>(null);

  const openProcesar = (item: InstanceSummary) => {
    setProcessTarget(item);
    setSoatPagado(false);
    setImpuestoPagado(false);
    setProcessError(null);
    setProcessWarning(null);
  };

  const confirmProcesar = async () => {
    if (!processTarget) return;
    setProcessActing(true);
    setProcessError(null);
    setProcessWarning(null);
    try {
      const res = await tramitesClient.completePlateFlow(
        processTarget.id,
        { soatPagado, impuestoDepartamentalPagado: impuestoPagado },
        isAdmin ? processTarget.tenantId : undefined,
      );
      setItems((prev) =>
        prev.map((it) =>
          it.id === processTarget.id ? { ...it, plateFlowStatus: 'terminado' } : it,
        ),
      );
      if (res?.warningMessage) {
        setProcessWarning(res.warningMessage);
      } else {
        setProcessTarget(null);
      }
    } catch (err) {
      setProcessError(err instanceof Error ? err.message : 'No se pudo marcar como Terminado.');
    } finally {
      setProcessActing(false);
    }
  };

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const query: ListInstancesParams = {};
      if (appliedPlaca.trim()) query.placa = appliedPlaca.trim();
      if (appliedVendedor.trim()) query.vendedor = appliedVendedor.trim();
      if (appliedComprador.trim()) query.comprador = appliedComprador.trim();
      if (appliedGestor.trim()) query.gestor = appliedGestor.trim();
      if (appliedFirmado === 'true') query.firmado = true;
      if (appliedFirmado === 'false') query.firmado = false;
      if (appliedCreatedFrom.trim()) query.createdFrom = appliedCreatedFrom.trim();
      if (appliedCreatedTo.trim()) query.createdTo = appliedCreatedTo.trim();
      if (appliedUpdatedFrom.trim()) query.updatedFrom = appliedUpdatedFrom.trim();
      if (appliedUpdatedTo.trim()) query.updatedTo = appliedUpdatedTo.trim();
      if (sortBy) {
        query.sortBy = sortBy;
        query.sortDir = sortDir;
      }
      // Cualquier filtro/orden activa el camino server-side: pedir el tope del API.
      if (Object.keys(query).length > 0) {
        query.take = SERVER_LIST_TAKE;
        query.skip = 0;
      }
      const data = await tramitesClient.listInstances(
        Object.keys(query).length > 0 ? query : undefined,
      );
      setItems(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Error desconocido');
    } finally {
      setLoading(false);
    }
  }, [
    appliedPlaca,
    appliedVendedor,
    appliedComprador,
    appliedGestor,
    appliedFirmado,
    appliedCreatedFrom,
    appliedCreatedTo,
    appliedUpdatedFrom,
    appliedUpdatedTo,
    sortBy,
    sortDir,
  ]);

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

  // ICT (paridad v1) — pausar/reanudar un trámite ICT con actualización optimista; revierte si falla.
  // Solo aplica a borradores origin='ict' (el botón solo se muestra ahí). Al reanudar se limpia la nota.
  const handleTogglePause = useCallback(
    async (id: string, next: boolean, tenantId: string) => {
      setItems((prev) =>
        prev.map((it) =>
          it.id === id
            ? { ...it, isPaused: next, pausedObservation: next ? it.pausedObservation ?? null : null }
            : it,
        ),
      );
      try {
        await tramitesClient.pauseInstance(id, next, null, isAdmin ? tenantId : undefined);
      } catch {
        setItems((prev) =>
          prev.map((it) => (it.id === id ? { ...it, isPaused: !next } : it)),
        );
      }
    },
    [isAdmin],
  );

  // ICT (paridad v1 pause-unpause-massive) — selección múltiple para pausa/reanudación en lote.
  const toggleSelect = useCallback((id: string) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }, []);

  const clearSelection = useCallback(() => setSelectedIds(new Set()), []);

  const handleBulkPause = useCallback(
    async (paused: boolean) => {
      if (selectedIds.size === 0) return;
      // Optimista sobre las filas seleccionadas (solo tienen sentido las ICT en borrador).
      setItems((prev) =>
        prev.map((it) =>
          selectedIds.has(it.id) && it.origin === 'ict' && it.estado === 'borrador'
            ? { ...it, isPaused: paused, pausedObservation: paused ? it.pausedObservation ?? null : null }
            : it,
        ),
      );
      // El superadmin puede seleccionar trámites de varias compañías; el endpoint masivo se acota por
      // X-Tenant-Id, así que se agrupa por tenant y se llama una vez por compañía.
      const byTenant = new Map<string, string[]>();
      for (const it of items) {
        if (!selectedIds.has(it.id)) continue;
        const arr = byTenant.get(it.tenantId) ?? [];
        arr.push(it.id);
        byTenant.set(it.tenantId, arr);
      }
      try {
        await Promise.all(
          Array.from(byTenant.entries()).map(([tenantId, ids]) =>
            tramitesClient.pauseInstancesMassive(ids, paused, null, isAdmin ? tenantId : undefined),
          ),
        );
      } catch {
        void load(); // ante fallo parcial, refresca para reflejar el estado real del backend
      } finally {
        setSelectedIds(new Set());
      }
    },
    [selectedIds, items, isAdmin, load],
  );

  const hasServerFilters =
    appliedPlaca.trim() !== '' ||
    appliedVendedor.trim() !== '' ||
    appliedComprador.trim() !== '' ||
    appliedGestor.trim() !== '' ||
    appliedFirmado !== '' ||
    appliedCreatedFrom.trim() !== '' ||
    appliedCreatedTo.trim() !== '' ||
    appliedUpdatedFrom.trim() !== '' ||
    appliedUpdatedTo.trim() !== '';

  const hasActiveFilters =
    search.trim() !== '' ||
    modalidad !== '' ||
    estado !== '' ||
    compania !== '' ||
    soloPrioritarios ||
    hasServerFilters ||
    sortBy !== '';

  const applyServerFilters = () => {
    setAppliedPlaca(placaFilter);
    setAppliedVendedor(vendedorFilter);
    setAppliedComprador(compradorFilter);
    setAppliedGestor(gestorFilter);
    setAppliedFirmado(firmadoFilter);
    setAppliedCreatedFrom(createdFromFilter);
    setAppliedCreatedTo(createdToFilter);
    setAppliedUpdatedFrom(updatedFromFilter);
    setAppliedUpdatedTo(updatedToFilter);
    setPage(1);
  };

  const handleSortChange = (nextSortBy: string, nextSortDir: 'asc' | 'desc') => {
    setSortBy(nextSortBy);
    setSortDir(nextSortDir);
    setPage(1);
  };

  const clearFilters = () => {
    setSearch('');
    setModalidad('');
    setEstado('');
    setCompania('');
    setSoloPrioritarios(false);
    setPlacaFilter('');
    setVendedorFilter('');
    setCompradorFilter('');
    setGestorFilter('');
    setFirmadoFilter('');
    setCreatedFromFilter('');
    setCreatedToFilter('');
    setUpdatedFromFilter('');
    setUpdatedToFilter('');
    setAppliedPlaca('');
    setAppliedVendedor('');
    setAppliedComprador('');
    setAppliedGestor('');
    setAppliedFirmado('');
    setAppliedCreatedFrom('');
    setAppliedCreatedTo('');
    setAppliedUpdatedFrom('');
    setAppliedUpdatedTo('');
    setSortBy('');
    setSortDir('desc');
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
          {NEW_TRAMITE_ACTIONS.map(({ id, label, icon: Icon }) => {
            const blocked =
              id === 'matricula_inicial' ? blockNew.matricula : blockNew.traspaso;
            return (
              <button
                key={id}
                type="button"
                disabled={blocked}
                title={
                  blocked
                    ? 'La compañía tiene bloqueada la creación de este tipo de trámite.'
                    : undefined
                }
                onClick={() => {
                  if (blocked) return;
                  onStartTramite?.(id);
                }}
                className="inline-flex items-center gap-2 rounded-xl px-5 py-2.5 text-xs font-semibold text-white transition hover:opacity-95 disabled:cursor-not-allowed disabled:opacity-45"
                style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
                aria-label={blocked ? `${label} (no permitido)` : `Iniciar ${label}`}
              >
                <Icon className="h-4 w-4" aria-hidden="true" />
                {label}
              </button>
            );
          })}
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

        {/* Filtros server-side colapsables: placa, actores, gestor, firmado, rangos de fecha. */}
        <div className="rounded-2xl border bg-white dark:bg-[#0B0F14]">
          <div className="flex flex-wrap items-center gap-2 px-4 py-2.5">
            <button
              type="button"
              onClick={() => setFiltersOpen((o) => !o)}
              aria-expanded={filtersOpen}
              aria-controls="tramites-filtros-panel"
              className="inline-flex items-center gap-1.5 rounded-xl px-3 py-1.5 text-xs font-semibold text-[#162744] transition hover:bg-[#557EFF]/10 dark:text-white"
            >
              {filtersOpen ? (
                <ChevronUp className="h-3.5 w-3.5" aria-hidden="true" />
              ) : (
                <ChevronDown className="h-3.5 w-3.5" aria-hidden="true" />
              )}
              Filtros
              {hasServerFilters ? (
                <span className="ml-0.5 rounded-full bg-[#557EFF]/15 px-1.5 py-0.5 text-[10px] font-bold text-[#557EFF]">
                  activos
                </span>
              ) : null}
            </button>
            <button
              type="button"
              onClick={clearFilters}
              disabled={!hasActiveFilters}
              className="rounded-xl border px-3 py-1.5 text-xs font-semibold disabled:cursor-not-allowed disabled:opacity-40"
              style={{ borderColor: '#557EFF', color: '#557EFF' }}
              aria-label="Limpiar filtros avanzados"
            >
              Limpiar filtros
            </button>
            <ColumnSelector
              columns={TRAMITES_COLUMNS}
              visible={visibleColumns}
              onChange={setVisibleColumns}
              label="Columnas"
              disabled={savingColumns}
              className="ml-auto"
            />
          </div>
          {filtersOpen ? (
            <form
              id="tramites-filtros-panel"
              className={`${FILTER_FORM_CLS} border-0 border-t rounded-none rounded-b-2xl`}
              aria-label="Filtros de trámites"
              onSubmit={(e) => {
                e.preventDefault();
                applyServerFilters();
              }}
            >
          <label className="text-xs font-semibold text-[#162744] dark:text-white">
            Placa
            <input
              type="search"
              aria-label="Filtrar por placa"
              className={FILTER_INPUT_CLS}
              value={placaFilter}
              onChange={(e) => setPlacaFilter(e.target.value)}
              placeholder="ABC123"
            />
          </label>
          <label className="text-xs font-semibold text-[#162744] dark:text-white">
            Propietario / vendedor
            <input
              type="search"
              aria-label="Filtrar por propietario o vendedor"
              className={FILTER_INPUT_CLS}
              value={vendedorFilter}
              onChange={(e) => setVendedorFilter(e.target.value)}
              placeholder="Nombre"
            />
          </label>
          <label className="text-xs font-semibold text-[#162744] dark:text-white">
            Comprador
            <input
              type="search"
              aria-label="Filtrar por comprador"
              className={FILTER_INPUT_CLS}
              value={compradorFilter}
              onChange={(e) => setCompradorFilter(e.target.value)}
              placeholder="Nombre"
            />
          </label>
          <label className="text-xs font-semibold text-[#162744] dark:text-white">
            Gestor
            <input
              type="search"
              aria-label="Filtrar por gestor"
              className={FILTER_INPUT_CLS}
              value={gestorFilter}
              onChange={(e) => setGestorFilter(e.target.value)}
              placeholder="Nombre"
            />
          </label>
          <label className="text-xs font-semibold text-[#162744] dark:text-white">
            Firmado
            <select
              aria-label="Filtrar por firma de compraventa"
              className={FILTER_INPUT_CLS}
              value={firmadoFilter}
              onChange={(e) => setFirmadoFilter(e.target.value as '' | 'true' | 'false')}
              title="Firma electrónica de la compraventa (completa o pendiente)"
            >
              <option value="">Todos</option>
              <option value="true">Firmado</option>
              <option value="false">Pendiente</option>
            </select>
          </label>
          <label className="text-xs font-semibold text-[#162744] dark:text-white">
            Creación desde
            <input
              type="date"
              aria-label="Fecha de creación desde"
              className={FILTER_INPUT_CLS}
              value={createdFromFilter}
              onChange={(e) => setCreatedFromFilter(e.target.value)}
            />
          </label>
          <label className="text-xs font-semibold text-[#162744] dark:text-white">
            Creación hasta
            <input
              type="date"
              aria-label="Fecha de creación hasta"
              className={FILTER_INPUT_CLS}
              value={createdToFilter}
              onChange={(e) => setCreatedToFilter(e.target.value)}
            />
          </label>
          <label className="text-xs font-semibold text-[#162744] dark:text-white">
            Actualización desde
            <input
              type="date"
              aria-label="Fecha de actualización desde"
              className={FILTER_INPUT_CLS}
              value={updatedFromFilter}
              onChange={(e) => setUpdatedFromFilter(e.target.value)}
            />
          </label>
          <label className="text-xs font-semibold text-[#162744] dark:text-white">
            Actualización hasta
            <input
              type="date"
              aria-label="Fecha de actualización hasta"
              className={FILTER_INPUT_CLS}
              value={updatedToFilter}
              onChange={(e) => setUpdatedToFilter(e.target.value)}
            />
          </label>
          <div className="flex items-end gap-2 sm:col-span-2 md:col-span-3 lg:col-span-4">
            <button
              type="submit"
              className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
              style={{ background: '#557EFF' }}
            >
              Aplicar filtros
            </button>
          </div>
            </form>
          ) : null}
        </div>

        {/* ICT (paridad v1 pause-unpause-massive) — barra de acción cuando hay trámites ICT seleccionados. */}
        {selectedIds.size > 0 ? (
          <div
            role="region"
            aria-label="Acciones masivas de pausa"
            className="flex flex-wrap items-center gap-2 rounded-xl border border-[#557EFF]/30 bg-[#557EFF]/[0.06] px-3 py-2 text-xs"
          >
            <span className="font-semibold text-[#162744] dark:text-white">
              {`${selectedIds.size} seleccionado${selectedIds.size === 1 ? '' : 's'}`}
            </span>
            <button
              type="button"
              onClick={() => void handleBulkPause(true)}
              className="inline-flex items-center gap-1 rounded-lg border border-[#162744]/20 px-2.5 py-1 font-semibold text-[#162744] transition hover:bg-[#162744]/[0.06] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] dark:border-white/20 dark:text-white"
            >
              <Pause className="h-3.5 w-3.5" aria-hidden="true" /> Pausar
            </button>
            <button
              type="button"
              onClick={() => void handleBulkPause(false)}
              className="inline-flex items-center gap-1 rounded-lg border border-[#557EFF]/40 px-2.5 py-1 font-semibold text-[#557EFF] transition hover:bg-[#557EFF]/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
            >
              <Play className="h-3.5 w-3.5" aria-hidden="true" /> Reanudar
            </button>
            <button
              type="button"
              onClick={clearSelection}
              className="ml-auto inline-flex items-center gap-1 rounded-lg px-2 py-1 font-semibold text-[#162744]/60 transition hover:text-[#162744] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] dark:text-white/60 dark:hover:text-white"
            >
              <X className="h-3.5 w-3.5" aria-hidden="true" /> Limpiar
            </button>
          </div>
        ) : null}

        <TableBody
          loading={loading}
          error={error}
          items={items}
          filtered={filtered}
          paginated={paginated}
          visibleColumns={visibleColumns}
          gridLayout={gridLayout}
          page={safePage}
          totalPages={totalPages}
          onPageChange={setPage}
          hasActiveFilters={hasActiveFilters}
          sortBy={sortBy}
          sortDir={sortDir}
          onSortChange={handleSortChange}
          openPopoverId={openPopoverId}
          onTogglePopover={(id) => setOpenPopoverId((prev) => (prev === id ? null : id))}
          onClosePopover={() => setOpenPopoverId(null)}
          onRetry={() => void load()}
          onClearFilters={clearFilters}
          onTogglePriority={handleTogglePriority}
          onTogglePause={handleTogglePause}
          selectedIds={selectedIds}
          onToggleSelect={toggleSelect}
          onProcesar={openProcesar}
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

      {processTarget && (
        <div
          className="fixed inset-0 z-[90] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-labelledby="procesar-plate-title"
        >
          <div
            className="w-full max-w-md max-h-[90dvh] overflow-y-auto rounded-2xl bg-white p-6 shadow-2xl dark:bg-[#0B0F14]"
            style={{ border: '1px solid #DFE5ED' }}
            onClick={(e) => e.stopPropagation()}
          >
            <h2 id="procesar-plate-title" className="text-lg font-semibold" style={{ color: '#162744' }}>
              Procesar trámite
            </h2>
            <p className="mt-1 text-sm opacity-80">
              {processTarget.referenceNumber}
              {processTarget.placa ? ` · ${processTarget.placa}` : ''}
            </p>
            <p className="mt-2 text-xs opacity-70">
              El OT ya asignó la placa. Marca los checks opcionales si aplican y pasa a Terminado
              para que el OT pueda aprobar o rechazar.
            </p>
            <div className="mt-4 space-y-2">
              <label className="flex cursor-pointer items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  className="h-4 w-4 accent-[#557EFF]"
                  checked={soatPagado}
                  onChange={(e) => setSoatPagado(e.target.checked)}
                  disabled={processActing || !!processWarning}
                />
                SOAT pagado
              </label>
              <label className="flex cursor-pointer items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  className="h-4 w-4 accent-[#557EFF]"
                  checked={impuestoPagado}
                  onChange={(e) => setImpuestoPagado(e.target.checked)}
                  disabled={processActing || !!processWarning}
                />
                Impuesto departamental pagado
              </label>
            </div>
            {processError ? (
              <InlineAlert tone="warning" title="No se pudo procesar el trámite" className="mt-4">
                {processError}
              </InlineAlert>
            ) : null}
            {processWarning ? (
              <InlineAlert tone="warning" title="Trámite enviado al OT con advertencia" className="mt-4">
                {processWarning}
              </InlineAlert>
            ) : null}
            <div className="mt-5 flex gap-3">
              {processWarning ? (
                <button
                  type="button"
                  className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white"
                  style={{ background: '#557EFF' }}
                  onClick={() => setProcessTarget(null)}
                >
                  Entendido
                </button>
              ) : (
                <>
                  <button
                    type="button"
                    className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
                    onClick={() => setProcessTarget(null)}
                    disabled={processActing}
                  >
                    Cancelar
                  </button>
                  <button
                    type="button"
                    className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
                    style={{ background: '#557EFF' }}
                    disabled={processActing}
                    onClick={() => void confirmProcesar()}
                  >
                    {processActing ? 'Procesando…' : 'Marcar como Terminado'}
                  </button>
                </>
              )}
            </div>
          </div>
        </div>
      )}
    </section>
  );
}

/** Cabecera ordenable (grid CSS) — mismo patrón que OT ClientProceduresTable. */
function SortableHeaderCell({
  column,
  sortBy,
  sortDir,
  onSortChange,
}: {
  column: TramitesColumnDef;
  sortBy: string;
  sortDir: 'asc' | 'desc';
  onSortChange: (sortBy: string, sortDir: 'asc' | 'desc') => void;
}) {
  if (!column.sortable) {
    return <div>{column.label}</div>;
  }
  const apiKey = tramitesColumnToSortBy(column.key);
  const active = sortBy === apiKey;
  const nextDir: 'asc' | 'desc' = active && sortDir === 'asc' ? 'desc' : 'asc';
  const Icon = !active ? ArrowUpDown : sortDir === 'asc' ? ArrowUp : ArrowDown;
  return (
    <div>
      <button
        type="button"
        className="inline-flex items-center gap-1 uppercase hover:opacity-80"
        aria-label={`Ordenar por ${column.label}${active ? ` (${sortDir === 'asc' ? 'ascendente' : 'descendente'})` : ''}`}
        onClick={() => onSortChange(apiKey, nextDir)}
      >
        {column.label}
        <Icon className="h-3 w-3 opacity-60" aria-hidden="true" />
      </button>
    </div>
  );
}

/** Cuerpo de la tabla: maneja los 4 estados (cargando/error/vacío/datos). */
function TableBody({
  loading,
  error,
  items,
  filtered,
  paginated,
  visibleColumns,
  gridLayout,
  page,
  totalPages,
  onPageChange,
  hasActiveFilters,
  sortBy,
  sortDir,
  onSortChange,
  openPopoverId,
  onTogglePopover,
  onClosePopover,
  onRetry,
  onClearFilters,
  onTogglePriority,
  onTogglePause,
  selectedIds,
  onToggleSelect,
  onProcesar,
  onOpen,
  onVerDocumentos,
  onVerConsolidado,
}: {
  loading: boolean;
  error: string | null;
  items: InstanceSummary[];
  filtered: InstanceSummary[];
  paginated: InstanceSummary[];
  /** Selector de columnas: claves visibles, en el mismo orden que TRAMITES_COLUMNS. */
  visibleColumns: readonly string[];
  /** `gridTemplateColumns` + ancho mínimo, calculados UNA vez a partir de `visibleColumns` — la
   *  cabecera y cada fila lo reciben ya resuelto, así quedan alineados por construcción. */
  gridLayout: TramitesGridLayout;
  page: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  hasActiveFilters: boolean;
  sortBy: string;
  sortDir: 'asc' | 'desc';
  onSortChange: (sortBy: string, sortDir: 'asc' | 'desc') => void;
  openPopoverId: string | null;
  onTogglePopover: (id: string) => void;
  onClosePopover: () => void;
  onRetry: () => void;
  onClearFilters: () => void;
  onTogglePriority: (id: string, next: boolean, tenantId: string) => void;
  onTogglePause: (id: string, next: boolean, tenantId: string) => void;
  selectedIds: Set<string>;
  onToggleSelect: (id: string) => void;
  onProcesar: (item: InstanceSummary) => void;
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

  // Columnas visibles, RE-ORDENADAS al orden canónico de TRAMITES_COLUMNS (nunca al orden en que
  // llegó `visibleColumns`, que podría venir desordenado de una preferencia guardada). Header y
  // filas reciben esta MISMA lista `visibleKeysOrdered`, calculada una sola vez aquí: es lo que
  // garantiza la alineación por construcción, sin importar cuántas columnas se oculten.
  const visibleDefs = TRAMITES_COLUMNS.filter((c) => visibleColumns.includes(c.key));
  const visibleKeysOrdered = visibleDefs.map((c) => c.key);

  return (
    <div className="overflow-x-auto">
      <div style={{ minWidth: `${gridLayout.minWidthPx}px` }}>
        {/* Header */}
        <div
          className="grid items-center text-[11px] uppercase tracking-wider font-semibold rounded-xl px-4 py-3"
          style={{
            background: '#dfe5ed',
            color: '#162744',
            gridTemplateColumns: gridLayout.gridTemplateColumns,
          }}
          role="row"
        >
          {/* Columna de selección solo cuando hay borradores ICT (si no, hueco vacío al inicio). */}
          {gridLayout.includeSelectColumn ? <div aria-hidden="true" /> : null}
          {visibleDefs.map((col) => (
            <SortableHeaderCell
              key={col.key}
              column={col}
              sortBy={sortBy}
              sortDir={sortDir}
              onSortChange={onSortChange}
            />
          ))}
          <div className="text-right">Acciones</div>
        </div>

        {/* Rows */}
        <ul className="space-y-2 mt-2" aria-label="Trámites en curso">
          {paginated.map((item) => (
            <TramiteRow
              key={item.id}
              item={item}
              visibleColumns={visibleKeysOrdered}
              gridTemplateColumns={gridLayout.gridTemplateColumns}
              includeSelectColumn={gridLayout.includeSelectColumn}
              popoverOpen={openPopoverId === item.id}
              onTogglePopover={onTogglePopover}
              onClosePopover={onClosePopover}
              onTogglePriority={onTogglePriority}
              onTogglePause={onTogglePause}
              selected={selectedIds.has(item.id)}
              onToggleSelect={onToggleSelect}
              onProcesar={onProcesar}
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
 * HU #11057 — celda de actor + acreditación (identidad o firma del baúl) en la misma columna.
 * `estado` null = NO APLICA (p. ej. vendedor en matrícula inicial): guion, no chip, para no dar
 * una falsa alarma.
 */
function ActorConFirmaCell({
  nombre,
  estado,
  parte,
}: {
  nombre: string | null | undefined;
  estado?: FirmaParteEstado | null;
  parte: 'vendedor' | 'comprador';
}) {
  return (
    <span className="flex min-w-0 flex-col items-start gap-1">
      <span
        className="block w-full truncate text-[#162744] dark:text-white/90"
        title={nombre?.trim() || undefined}
      >
        {nombre?.trim() || '—'}
      </span>
      {estado ? (
        <StatusBadge
          label={FIRMA_CHIP[estado].label}
          bg={FIRMA_CHIP[estado].bg}
          color={FIRMA_CHIP[estado].color}
          border={FIRMA_CHIP[estado].border}
        />
      ) : (
        <span
          className="block text-[10px] text-[#162744]/40 dark:text-white/30"
          title={`No aplica: este trámite no tiene ${parte}`}
        >
          —
        </span>
      )}
    </span>
  );
}

/** Fila de trámite: clickable (abre el wizard) + acciones explícitas (documentos, consolidado, Continuar/Ver). */
function TramiteRow({
  item,
  visibleColumns,
  gridTemplateColumns,
  includeSelectColumn,
  popoverOpen,
  onTogglePopover,
  onClosePopover,
  onTogglePriority,
  onTogglePause,
  selected,
  onToggleSelect,
  onProcesar,
  onOpen,
  onVerDocumentos,
  onVerConsolidado,
}: {
  item: InstanceSummary;
  /** Claves visibles (selector de columnas) — misma lista/orden que usa la cabecera. */
  visibleColumns: readonly string[];
  /** `gridTemplateColumns` ya resuelto por TableBody a partir de `visibleColumns`. */
  gridTemplateColumns: string;
  /** Debe coincidir con `gridLayout.includeSelectColumn` (pista del checkbox ICT). */
  includeSelectColumn: boolean;
  popoverOpen: boolean;
  onTogglePopover: (id: string) => void;
  onClosePopover: () => void;
  onTogglePriority: (id: string, next: boolean, tenantId: string) => void;
  onTogglePause: (id: string, next: boolean, tenantId: string) => void;
  selected: boolean;
  onToggleSelect: (id: string) => void;
  onProcesar: (item: InstanceSummary) => void;
  onOpen: (id: string, tenantId: string) => void;
  onVerDocumentos: (item: InstanceSummary) => void;
  onVerConsolidado: (item: InstanceSummary) => void;
}) {
  // HU #11055 — la acción del consolidado solo existe si el expediente ya está generado (el resumen
  // trae el id del adjunto): el botón NUNCA dispara una generación.
  const consolidadoDisponible = !!item.consolidadoAttachmentId;
  // ICT (paridad v1) — solo los borradores originados por ICT son pausables/seleccionables.
  const isIctDraft = item.origin === 'ict' && item.estado === 'borrador';
  // HU #10350 — un borrador finalizado muestra un chip async ("Pendiente validación"/"Pendiente
  // firma"/"Listo para radicar"); el resto usa el chip base de estado. `ready` promueve la acción a
  // "Radicar" cuando la identidad ya quedó aprobada y los gates están listos.
  const async = asyncStatus(item);
  const chip = async?.chip ?? estadoChip(item.estado);
  const isDraft = item.estado === 'borrador';
  const actionLabel = async?.ready ? 'Radicar' : isDraft ? 'Continuar' : 'Ver';
  const actionIcon = async?.ready ? FileCheck : isDraft ? Play : Eye;
  const plateHint = plateFlowHint(item.plateFlowStatus);
  const puedeProcesar =
    item.estado === 'entregado' && item.plateFlowStatus === 'asignado';
  const actionItems: ActionsMenuItem[] = [
    {
      key: 'abrir',
      label: actionLabel,
      icon: actionIcon,
      // ICT — si está pausado, handleOpen abre el modal de confirmación antes de continuar.
      onSelect: () => handleOpen(),
    },
    // ICT (paridad v1) — pausar/reanudar como acción del menú (solo borradores origin='ict').
    ...(isIctDraft
      ? [
          {
            key: 'pausa',
            label: item.isPaused ? 'Reanudar' : 'Pausar',
            icon: item.isPaused ? Play : Pause,
            onSelect: () => onTogglePause(item.id, !item.isPaused, item.tenantId),
          },
        ]
      : []),
    ...(puedeProcesar
      ? [
          {
            key: 'procesar',
            label: 'Procesar',
            icon: CheckCircle2,
            attention: true,
            onSelect: () => onProcesar(item),
          },
        ]
      : []),
    // HU #11054 — documentos del expediente sin entrar al wizard.
    {
      key: 'documentos',
      label: 'Ver documentos',
      icon: FileText,
      onSelect: () => onVerDocumentos(item),
    },
    // HU #11055 — el negocio pidió la acción "sólo visible si ya se encuentra generado": se OMITE
    // cuando no hay consolidado, en vez de mostrarse deshabilitada. Así nunca dispara una generación.
    ...(consolidadoDisponible
      ? [
          {
            key: 'consolidado',
            label: 'Ver consolidado',
            icon: FileStack,
            onSelect: () => onVerConsolidado(item),
          },
        ]
      : []),
  ];
  const motivoRechazo = item.ultimoRechazoMotivo?.trim() || null;
  const subsanacionCount = item.subsanacionCount ?? 0;
  const enSubsanacion = !!item.subsanacionActiva;
  const showRejectPopover =
    !!motivoRechazo || enSubsanacion || subsanacionCount > 0;
  const popoverRef = useRef<HTMLDivElement>(null);
  const iconColor = enSubsanacion ? '#b45309' : '#c2410c';

  // ICT — abrir/continuar un trámite PAUSADO pide confirmación primero (modal FLIT, no confirm nativo):
  // recordar reanudarlo para poder radicarlo.
  const [confirmPauseOpen, setConfirmPauseOpen] = useState(false);
  const handleOpen = () => {
    if (item.isPaused) {
      setConfirmPauseOpen(true);
      return;
    }
    onOpen(item.id, item.tenantId);
  };

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

  // Contenido de cada columna de DATOS (todo menos Selección/Acciones, que son estructurales),
  // indexado por la misma clave que usa el selector de columnas. Se define aparte del JSX para
  // poder renderizar SOLO las visibles, en el orden canónico de TRAMITES_COLUMNS, con un único
  // `.map` — así la fila queda alineada con la cabecera por construcción (ambas parten de
  // `gridTemplateColumns` calculado por TableBody a partir del mismo `visibleColumns`).
  const cellsByKey: Record<string, React.ReactNode> = {
    radicado: (
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
        <span className="min-w-0 truncate block font-mono font-semibold text-[#162744] dark:text-white">
          {item.referenceNumber}
        </span>
      </span>
    ),
    vin: (
      <span className="block truncate font-mono text-xs text-[#162744]/80 dark:text-white/70">
        {item.vin ?? '—'}
      </span>
    ),
    placa: (
      <span className="block truncate font-mono font-semibold text-[#162744] dark:text-white">
        {item.placa ?? '—'}
      </span>
    ),
    tramite: (
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
    ),
    vehiculo: (
      <span className="block truncate text-[#162744]/90 dark:text-white/80" title={vehiculo(item)}>
        {vehiculo(item)}
      </span>
    ),
    propietario: (
      <ActorConFirmaCell
        nombre={item.vendedorNombre}
        estado={item.firmaVendedorEstado}
        parte="vendedor"
      />
    ),
    comprador: (
      <ActorConFirmaCell
        nombre={item.compradorNombre}
        estado={item.firmaCompradorEstado}
        parte="comprador"
      />
    ),
    paso: (
      <span className="block min-w-0">
        <span className="block font-mono text-xs text-[#162744]/70 dark:text-white/60">
          {item.pasoActual}/{item.totalPasos}
        </span>
        <span className="block truncate text-[10px] text-[#162744]/60 dark:text-white/50">
          {stepLabel(item)}
        </span>
      </span>
    ),
    estado: (
      <span className="relative flex min-w-0 flex-col items-start gap-1">
        <span className="flex min-w-0 flex-wrap items-center gap-1.5">
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
        {/* ICT — "Pausado" (solo texto, sin ícono): apilado bajo el estado; no invade Organismo. */}
        {item.isPaused ? (
          <span
            className="inline-flex shrink-0 items-center whitespace-nowrap rounded-full border border-[#162744]/20 bg-[#162744]/[0.06] px-2 py-0.5 text-[10px] font-semibold text-[#162744]/70 dark:border-white/20 dark:bg-white/10 dark:text-white/70"
            title={item.pausedObservation ?? 'Trámite pausado'}
            aria-label={
              item.pausedObservation
                ? `Trámite pausado: ${item.pausedObservation}`
                : 'Trámite pausado'
            }
          >
            Pausado
          </span>
        ) : null}
        {plateHint ? (
          <span
            className="text-[10px] leading-tight text-[#162744]/45 dark:text-white/40 truncate"
            title={plateHint}
          >
            {plateHint}
          </span>
        ) : null}
      </span>
    ),
    fechaCreacion: (
      <span className="block font-mono text-xs text-[#162744]/70 dark:text-white/60">
        {shortDate(item.createdAt)}
      </span>
    ),
    // Sin modificaciones desde que se creó ⇒ no hay fecha de actualización que mostrar.
    fechaActualizacion: (
      <span className="block font-mono text-xs text-[#162744]/70 dark:text-white/60">
        {item.updatedAt ? shortDate(item.updatedAt) : '—'}
      </span>
    ),
    secretaria: (
      <span
        className="block truncate text-xs text-[#162744]/90 dark:text-white/80"
        title={item.organismoTransito ?? undefined}
      >
        {item.organismoTransito ?? '—'}
      </span>
    ),
    // Gestor = empresa que radica + persona que la operó. Sustituye a la antigua columna
    // "Compañía" del SuperAdmin: era exactamente la misma razón social, sin la persona. El
    // filtro por compañía sigue existiendo (arriba), y ahora todos los perfiles ven el dato.
    gestor: (
      <span className="block min-w-0">
        <span
          className="block truncate text-xs font-semibold text-[#162744] dark:text-white"
          title={item.companiaNombre ?? undefined}
        >
          {item.companiaNombre ?? '—'}
        </span>
        <span
          className="block truncate text-[10px] text-[#162744]/60 dark:text-white/50"
          title={item.gestorNombre ?? undefined}
        >
          {item.gestorNombre ?? '—'}
        </span>
      </span>
    ),
    fuente: (
      <span className="block truncate text-xs text-[#162744]/90 dark:text-white/80">
        {FUENTE_LABEL[item.fuente ?? 'dashboard']}
      </span>
    ),
  };

  return (
    <li>
      <div
        role="button"
        tabIndex={0}
        onClick={handleOpen}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            handleOpen();
          }
        }}
        className="w-full grid cursor-pointer items-center bg-white dark:bg-[#162744] rounded-xl border px-4 py-3 text-xs transition hover:border-[#557EFF]/40 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
        style={{ gridTemplateColumns }}
        aria-label={`Abrir trámite ${item.referenceNumber}`}
      >
        {/* ICT — checkbox de selección solo si la grilla reservó la pista (hay borradores ICT). */}
        {includeSelectColumn ? (
          <span className="flex items-center justify-start" onClick={(e) => e.stopPropagation()}>
            {isIctDraft ? (
              <input
                type="checkbox"
                checked={selected}
                onChange={() => onToggleSelect(item.id)}
                aria-label={`Seleccionar el trámite ${item.referenceNumber} para pausar/reanudar en lote`}
                title="Seleccionar para pausar/reanudar en lote"
                className="h-3.5 w-3.5 shrink-0 cursor-pointer accent-[#557EFF]"
              />
            ) : null}
          </span>
        ) : null}
        {/* Selector de columnas: solo se renderizan las visibles, en el orden canónico de
            TRAMITES_COLUMNS — la misma lista/orden que usa la cabecera (TableBody). */}
        {visibleColumns.map((key) => (
          <Fragment key={key}>{cellsByKey[key]}</Fragment>
        ))}
        {/* La fila entera navega al wizard, así que las acciones detienen la propagación en un
            envoltorio: el menú no recibe el evento y no hay que filtrarlo acción por acción.
            Se conserva el `ActionsMenu` que introdujo el subflujo de placa (HU #11037): las acciones
            de documentos y consolidado entran como ítems suyos, en vez de montar un segundo grupo de
            botones en la misma celda. */}
        <span
          className="flex justify-end"
          onClick={(e) => e.stopPropagation()}
          onKeyDown={(e) => e.stopPropagation()}
        >
          <ActionsMenu
            ariaLabel={`Acciones del trámite ${item.referenceNumber}`}
            items={actionItems}
            className="bg-white dark:bg-[#162744]"
            attention={puedeProcesar}
            attentionHint="Pendiente por procesar: el OT ya asignó la placa"
          />
        </span>
      </div>
      {/* ICT — confirmación FLIT (Modal con blur/overlay/CTA degradado) al continuar un trámite pausado. */}
      <Modal
        open={confirmPauseOpen}
        onClose={() => setConfirmPauseOpen(false)}
        title="Trámite pausado"
        icon={Pause}
        iconBg="#162744"
        description={`Trámite ${item.referenceNumber}`}
        size="sm"
      >
        <p className="text-sm text-[#162744]/80 dark:text-white/80">
          Este trámite está <strong>pausado</strong>. Recuerda reanudarlo (despausarlo) para poder
          radicarlo. ¿Deseas continuar de todos modos?
        </p>
        <div className="mt-5 flex justify-end gap-2">
          <button
            type="button"
            onClick={() => setConfirmPauseOpen(false)}
            className="rounded-full border border-[#DFE5ED] px-4 py-1.5 text-xs font-semibold text-[#162744] transition hover:bg-[#162744]/[0.04] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] dark:border-white/20 dark:text-white"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={() => {
              setConfirmPauseOpen(false);
              onOpen(item.id, item.tenantId);
            }}
            className="rounded-full px-4 py-1.5 text-xs font-semibold text-white transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
            style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
          >
            Continuar de todos modos
          </button>
        </div>
      </Modal>
    </li>
  );
}
