'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { formatFecha } from '@/lib/format/date';
import { useRouter } from 'next/navigation';
import {
  AlertCircle,
  ArrowDown,
  ArrowUp,
  ArrowUpDown,
  CheckCircle2,
  Eye,
  FileCheck,
  FileStack,
  FileText,
  Pause,
  Play,
  Star,
  X,
} from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { getToken } from '@/lib/api/client';
import { decodeJwtPayload, isSuperAdmin } from '@/lib/auth/jwt';
import { TramitesListToolbar } from './TramitesListToolbar';
import { WIZARD_CTA_GRADIENT } from './wizard-field-styles';
import { CarLoaderModal } from '@/components/atom/CarLoader';
import {
  TramitesFiltrosBar,
  TramitesFiltrosChips,
  rangoDePeriodo,
  type FiltroEspecificoKey,
  type RangoSobre,
} from './TramitesFiltrosBar';
import { estadoChipStyle, estadoLabel, type EstadoTramite } from '@/lib/tramites/estados';
import {
  TRAMITES_COLUMNS,
  TRAMITES_COLUMN_KEYS,
  TRAMITES_COLUMNS_ADDED_SINCE_LEGACY,
  DEFAULT_TRAMITES_VISIBLE_COLUMNS,
  buildTramitesGridLayout,
  buildTramitesColWidths,
  tramitesColumnToSortBy,
  type TramitesColumnDef,
  type TramitesGridLayout,
} from '@/lib/tramites/tramites-table-columns';
import { useUiPreferences } from '@/hooks/useUiPreferences';
import { StatusBadge } from '@/components/atom/StatusBadge';
import { PageNav } from '@/components/atom/PageNav';
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
import { TramiteDetalleModal } from './TramiteDetalleModal';
import { TramiteTrackingModal } from './TramiteTrackingModal';
import type {
  BiometricParte,
  FirmaParteEstado,
  InstanceStatus,
  InstanceSummary,
  ListInstancesParams,
  TramiteFuente,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';
import type { ProcedureFamily } from '@/lib/api/types/procedure-parametrization';
import { IdentidadParteTrackingModal } from './IdentidadParteTrackingModal';

/** Tope del camino filtrado del backend (mismo MaxItems del API). */
const SERVER_LIST_TAKE = 200;

/**
 * Texto corto y discreto del sub-estado de placa (debajo del chip de estado). Exportada porque
 * `TramiteDetalleModal` reutiliza el mismo texto en su banner contextual — no se duplica.
 */
export function plateFlowHint(status: string | null | undefined): string | null {
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
 * HU #11668 — ayuda del chip de identidad del listado.
 *
 * <p><b>Qué acredita.</b> Desde la HU #11667 la ruta de LOTE que alimenta estos chips acredita
 * también por firma del baúl, no solo por validación biométrica. El gestor que veía «Identidad
 * validada» iba a buscar un certificado de validación que, cuando la acreditación viene del baúl,
 * no existe.</p>
 *
 * <p><b>Qué NO dice el resumen del listado.</b> El origen concreto de cada parte no viaja en
 * <c>InstanceSummary</c>: la fila trae el estado agregado (<c>identityValidationStatus</c>) y la
 * acreditación por parte (<c>firmaVendedorEstado</c> / <c>firmaCompradorEstado</c>), y ninguno de
 * los dos distingue biométrica de baúl — los dos caminos producen exactamente el mismo valor. Por
 * eso la ayuda nombra las DOS vías posibles y remite al trámite, en vez de afirmar una que la fila
 * no puede saber. Inventar aquí el origen sería peor que no decirlo.</p>
 */
const AYUDA_ORIGEN =
  'La identidad puede quedar acreditada por validación biométrica o por la firma del baúl de la parte; cuando la acredita el baúl no hay certificado de validación que descargar. Abre el trámite para ver el origen de cada parte.';

/**
 * HU #11668 — alcance de los estados no terminales. La acreditación aprobada se resuelve por
 * persona (identidad vigente de cualquier trámite del tenant, o firma del baúl), pero «en proceso»
 * y «rechazado» salen únicamente de las validaciones de ESTE trámite: las claves en lote solo
 * traen identidades aprobadas y vigentes.
 */
const AYUDA_NO_TERMINALES =
  'Los estados en curso y rechazado se calculan solo con las validaciones propias de este trámite.';

/**
 * HU #10350 (AC3) — chip de estado async para borradores FINALIZADOS (draft + draftFinalizedAt). El
 * trámite cerró la captura y espera la validación de identidad del cliente; la firma se procesa sola
 * al aprobarse. Precedencia: rechazo → aprobado (firma pendiente / listo para radicar) → pendiente de
 * validación. Devuelve además `ready` cuando ya se puede radicar (identidad aprobada + gates), para que
 * la acción de la fila pase de "Continuar" a "Radicar". Null si no es un borrador finalizado (chip base).
 */
function asyncStatus(item: InstanceSummary): { chip: Chip; ready: boolean; ayuda: string } | null {
  if (item.estado !== 'borrador' || !item.draftFinalizedAt) return null;
  const idv = item.identityValidationStatus;

  if (idv === 'rechazado') {
    return {
      chip: { label: 'Validación rechazada', bg: 'rgba(255,78,0,0.10)', color: '#c2410c', border: 'rgba(255,78,0,0.3)' },
      ready: false,
      ayuda: AYUDA_NO_TERMINALES,
    };
  }

  if (idv === 'aprobado') {
    // HU #11668 — los tres chips de esta rama nacen de una identidad ACREDITADA, que desde la
    // HU #11667 puede venir de la biométrica o del baúl: los tres llevan la ayuda del origen.
    const ayuda = `${AYUDA_ORIGEN} ${AYUDA_NO_TERMINALES}`;
    if (item.signaturePending) {
      return {
        chip: { label: 'Pendiente firma', bg: 'rgba(99,102,241,0.12)', color: '#4f46e5', border: 'rgba(99,102,241,0.3)' },
        ready: false,
        ayuda,
      };
    }
    if (item.canSubmit) {
      return {
        chip: { label: 'Listo para radicar', bg: 'rgba(140,198,63,0.15)', color: 'var(--flit-success-ink)', border: 'rgba(140,198,63,0.4)' },
        ready: true,
        ayuda,
      };
    }
    return {
      chip: { label: 'Identidad validada', bg: 'rgba(140,198,63,0.12)', color: 'var(--flit-success-ink)', border: 'rgba(140,198,63,0.35)' },
      ready: false,
      ayuda,
    };
  }

  // en_proceso | enviado | null (sin iniciar) → esperando la validación del cliente.
  return {
    chip: { label: 'Pendiente validación', bg: 'rgba(245,158,11,0.14)', color: '#b45309', border: 'rgba(245,158,11,0.35)' },
    ready: false,
    ayuda: AYUDA_NO_TERMINALES,
  };
}

/**
 * HU #11668 — chip de identidad con su ayuda. El chip en sí es el mismo `StatusBadge` de siempre
 * (mismo texto, mismo nombre accesible): lo que se agrega es un envoltorio ALCANZABLE POR TECLADO
 * que expone la ayuda como descripción (`aria-describedby`) y la muestra al enfocar o al pasar el
 * puntero — el mismo patrón del indicador de OCR del checklist de documentos.
 *
 * El envoltorio no es un botón: no hay acción que ejecutar, solo información. Por eso tampoco
 * intercepta el clic de la fila, que sigue abriendo el trámite.
 */
function IdentidadChip({ chip, ayuda, tipId }: { chip: Chip; ayuda: string; tipId: string }) {
  const [tipOpen, setTipOpen] = useState(false);
  return (
    <span className="relative inline-flex">
      <span
        tabIndex={0}
        aria-describedby={tipOpen ? tipId : undefined}
        className="inline-flex rounded-full focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-1"
        onMouseEnter={() => setTipOpen(true)}
        onMouseLeave={() => setTipOpen(false)}
        onFocus={() => setTipOpen(true)}
        onBlur={() => setTipOpen(false)}
      >
        <StatusBadge label={chip.label} bg={chip.bg} color={chip.color} border={chip.border} />
      </span>
      {tipOpen ? (
        <span
          id={tipId}
          role="tooltip"
          className="absolute left-0 top-full z-30 mt-1.5 w-64 rounded-xl border bg-white p-2.5 text-left text-xs leading-snug text-[#162744] shadow-lg dark:bg-[#162744] dark:text-white/90"
          style={{ borderColor: '#DFE5ED' }}
        >
          {ayuda}
        </span>
      ) : null}
    </span>
  );
}

// HU #11018 — formato de negocio unico: AÑO/MES/DIA, sin hora.
function shortDate(iso: string): string {
  return formatFecha(iso);
}

function vehiculo(item: InstanceSummary): string {
  const text = `${item.vehiculoMarca ?? ''} ${item.vehiculoLinea ?? ''}`.trim();
  return text || '—';
}

const MODALIDAD_SHORT: Record<ProcedureFamily, string> = {
  OTROS: 'Otros',
  MATRICULAS: 'Matrícula',
  TRASPASO: 'Traspaso',
};

/**
 * Qué rotula la fila del listado.
 *
 * En MATRICULAS y TRASPASO la familia identifica bien el trámite. En OTROS no: agrupa quince tipos
 * —blindaje, cambio de color, levantamiento de prenda, duplicado de tarjeta…— que se veían los tres
 * igual, «Otros», sin forma de distinguirlos sin abrirlos. Ahí manda el nombre del tipo.
 *
 * Respaldo a la familia si el expediente viene de un backend anterior al campo, para que la celda
 * nunca quede vacía.
 */
function tramiteLabel(item: InstanceSummary): string {
  const familia = MODALIDAD_SHORT[item.modalidad];
  if (item.modalidad !== 'OTROS') return familia;
  return item.tipoNombre?.trim() || familia;
}

/**
 * Nombres de paso por familia — RESPALDO para expedientes servidos por un backend anterior a
 * `pasoNombre`. No se amplía: desde ADR-0050 el recorrido lo define el TIPO, no la familia, así que
 * una lista por familia no puede acertar en OTROS —quince tipos con recorridos distintos— y de hecho
 * estaba VACÍA, que es por lo que esas filas mostraban «—».
 */
const STEP_LABELS_FALLBACK: Record<ProcedureFamily, string[]> = {
  OTROS: [],
  MATRICULAS: [
    'Consulta VIN',
    'Datos y Documentos del Trámite',
    'Comprador',
    'Identidad',
    'Resumen del trámite',
  ],
  TRASPASO: [
    'Consulta del vehículo',
    'Datos y Documentos del Trámite',
    'Vendedor',
    'Comprador',
    'Datos comerciales',
    'Resumen del trámite',
  ],
};

/** Rótulo del paso en curso: manda el que arma el recorrido del tipo en el servidor. */
function stepLabel(item: InstanceSummary): string {
  return (
    item.pasoNombre?.trim() ||
    STEP_LABELS_FALLBACK[item.modalidad]?.[item.pasoActual - 1] ||
    '—'
  );
}

/**
 * Acreditación de una parte (identidad validada o firma del baúl) en la columna "Firmas".
 *
 * El diseño la dibuja como TEXTO PLANO de color, no como píldora, así que aquí solo se necesita
 * etiqueta + color, y se usan los tonos EXACTOS de la propuesta por decisión expresa del usuario.
 *
 * DEUDA DE CONTRASTE CONOCIDA: sobre blanco, `#F9AC00` da 1.9:1 y `#16A34A` 3.3:1, por debajo del
 * 4.5:1 que pide AA para texto. Se asume a sabiendas: el estado nunca depende solo del color —la
 * etiqueta lo dice— pero la legibilidad sigue siendo peor de lo que exige la norma. Se documentó
 * junto a las otras dos deudas de contraste abiertas (blanco sobre `#8CC63F` y sobre `#FF4E00`).
 */
const FIRMA_TEXTO: Record<FirmaParteEstado, { label: string; color: string }> = {
  firmado: { label: 'Firmado', color: '#16A34A' },
  pendiente: { label: 'Sin firma', color: '#F9AC00' },
  // La propuesta no dibuja una firma rechazada, así que no hay "tono exacto" que copiar: se queda
  // el naranja de marca en su variante para texto, que sí cumple contraste.
  rechazado: { label: 'Rechazado', color: '#C2410C' },
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

interface TramitesTableProps {
  /** Cambia (incrementa) para forzar un refetch — p. ej. al volver del wizard. */
  refreshKey?: number;
  /**
   * Entra al asistente de un trámite nuevo, SIN modalidad: el tipo se elige dentro del paso 1,
   * como en el diseño. Antes esta vista decidía la modalidad en un diálogo previo.
   */
  onNewTramite?: () => void;
}

export function TramitesTable({ refreshKey = 0, onNewTramite }: TramitesTableProps) {
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
  // Selector de modalidad del botón general "Nuevo trámite". La modalidad elegida se guarda
  // aparte del filtro `modalidad` del listado: son cosas distintas (crear vs filtrar).
  // ADR-0050 — el campo `modalidad` de la fila transporta ya la FAMILIA del tipo.
  const [modalidad, setModalidad] = useState<'' | ProcedureFamily>('');
  const [estado, setEstado] = useState<'' | InstanceStatus>('');
  // #1 — Filtro por compañía, solo relevante para el SuperAdmin (ve todas las empresas).
  const [compania, setCompania] = useState('');
  // HU #10536 — filtro "solo prioritarios".
  const [soloPrioritarios, setSoloPrioritarios] = useState(false);

  // Filtros server-side (mismo contrato que GET /instances). Borrador del form → se aplican
  // solo con "Aplicar filtros"; el sort sí recarga de inmediato al clic en cabecera.
  // Qué filtros específicos están AÑADIDOS (visibles) en la tarjeta — controla tanto el campo
  // real que se pinta como el chip de la fila inferior. Es independiente de si ya se aplicaron.
  const [filtrosEspecificos, setFiltrosEspecificos] = useState<Set<FiltroEspecificoKey>>(
    () => new Set(),
  );
  const [placaFilter, setPlacaFilter] = useState('');
  const [vendedorFilter, setVendedorFilter] = useState('');
  const [compradorFilter, setCompradorFilter] = useState('');
  const [gestorFilter, setGestorFilter] = useState('');
  const [firmadoFilter, setFirmadoFilter] = useState<'' | 'true' | 'false'>('');
  // "Rango sobre" + "Periodo" reemplazan a los 4 inputs de fecha sueltos: el usuario elige a qué
  // campo apunta el rango (creación o actualización) y un periodo predefinido — o "Rango propio"
  // con fechas propias. `rangoDePeriodo` (TramitesFiltrosBar) hace la conversión a
  // createdFrom/createdTo o updatedFrom/updatedTo al pulsar "Aplicar filtros".
  const [rangoSobre, setRangoSobre] = useState<RangoSobre>('created');
  const [periodo, setPeriodo] = useState('Sin periodo');
  const [rangoPropioDesde, setRangoPropioDesde] = useState('');
  const [rangoPropioHasta, setRangoPropioHasta] = useState('');
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

  // Selector de columnas: persiste qué columnas ve el usuario (scope "tramites.columns"). Degrada
  // con elegancia — si la preferencia no carga o falla al guardar, la tabla sigue con el default
  // compacto (el hook nunca deja la tabla en blanco ni bloquea su render).
  const {
    visible: visibleColumns,
    saving: savingColumns,
    setVisible: setVisibleColumns,
  } = useUiPreferences('tramites.columns', DEFAULT_TRAMITES_VISIBLE_COLUMNS, {
    catalog: TRAMITES_COLUMN_KEYS,
    addedSinceLegacy: TRAMITES_COLUMNS_ADDED_SINCE_LEGACY,
  });
  // Solo reservar la pista del checkbox cuando haya borradores ICT seleccionables; si no, ese
  // hueco vacío se veía como “espacio muerto” al inicio de Radicado.
  const includeSelectColumn = useMemo(
    () => items.some((it) => it.origin === 'ict' && it.estado === 'borrador'),
    [items],
  );
  /**
   * Columnas realmente pintadas = preferencia del usuario menos las que no aplican al tipo de
   * trámite filtrado. Hoy solo aplica a "Propietario / vendedor": la matrícula inicial no tiene
   * vendedor, así que en esa pestaña la columna sobra. En "Todos" SÍ se muestra, porque la lista
   * mezcla ambas modalidades y el dato existe para una parte de las filas.
   *
   * Es un cálculo derivado: NO se toca la preferencia guardada, así que al volver a "Todos" o a
   * "Traspaso" la columna reaparece sin que el usuario tenga que reactivarla.
   */
  const effectiveColumns = useMemo(
    () =>
      // En matrículas el titular es el comprador y la columna 'propietario' sale vacía.
      modalidad === 'MATRICULAS'
        ? visibleColumns.filter((k) => k !== 'propietario')
        : visibleColumns,
    [visibleColumns, modalidad],
  );
  const gridLayout = useMemo(
    () => buildTramitesGridLayout(effectiveColumns, { includeSelectColumn }),
    [effectiveColumns, includeSelectColumn],
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
  // Frente C, etapa 1 — modal de detalle para trámites YA RADICADOS (estado ≠ 'borrador'). El
  // borrador sigue navegando al asistente; ver `TramiteRow.handleOpen`.
  const [detalleTramite, setDetalleTramite] = useState<InstanceSummary | null>(null);
  /** Click en badge Estado → modal de línea de tiempo del trámite (todas las modalidades). */
  const [trackingTramite, setTrackingTramite] = useState<InstanceSummary | null>(null);
  /** Click en línea Firmas → modal de tracking de identidad de esa parte. */
  const [identidadTracking, setIdentidadTracking] = useState<{
    item: InstanceSummary;
    parte: BiometricParte;
    rotulo: string;
  } | null>(null);

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

  // HU #10536 — sin orden explicito por columna, el backend devuelve los prioritarios primero. Al
  // marcar uno desde la tabla se replica ESE mismo criterio en cliente, para que suba a la primera
  // fila en el acto en vez de quedarse en su sitio hasta el siguiente refetch o hasta recargar la
  // pagina. `sort` es estable, asi que dentro de cada grupo se conserva el orden que vino del
  // backend. Con un orden explicito por cabecera (`sortBy`) NO se reordena: ahi manda lo que pidio
  // el usuario, y colar los prioritarios arriba contradiria la columna que acaba de elegir.
  const ordenados = useMemo(() => {
    if (sortBy) return filtered;
    return [...filtered].sort(
      (a, b) => Number(b.prioritario ?? false) - Number(a.prioritario ?? false),
    );
  }, [filtered, sortBy]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  // Página segura: si los filtros/refetch reducen los resultados por debajo de
  // la página actual, se clampa al último rango válido.
  const safePage = Math.min(page, totalPages);
  const paginated = useMemo(() => {
    const start = (safePage - 1) * PAGE_SIZE;
    return ordenados.slice(start, start + PAGE_SIZE);
  }, [ordenados, safePage]);

  // Al cambiar cualquier filtro se vuelve a la primera página: la combinación
  // de criterios redefine el conjunto, así que arrancar desde el inicio es lo
  // esperado. Se hace en los handlers (no en un effect) para no reaccionar a
  // cambios derivados.
  const handleSearchChange = (v: string) => {
    setSearch(v);
    setPage(1);
  };
  const handleModalidadChange = (v: '' | ProcedureFamily) => {
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
  // No cambia el estado del ciclo de vida, solo el flag de ordenamiento. La fila SUBE en el acto:
  // `ordenados` reordena en cliente con el mismo criterio del backend, asi que no hay que esperar
  // al siguiente refetch ni recargar la pagina para ver el efecto de haber marcado la prioridad.
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

  /**
   * Lo que el usuario ya tocó en la tarjeta de filtros pero TODAVÍA no aplicó: filtros específicos
   * añadidos, un periodo elegido o fechas propias escritas. "Empezar de cero" tiene que poder
   * borrarlo también — si solo mirara lo aplicado, quedaban chips a la vista con el botón apagado.
   */
  const hasDraftFilters =
    filtrosEspecificos.size > 0 ||
    periodo !== 'Sin periodo' ||
    rangoPropioDesde !== '' ||
    rangoPropioHasta !== '';

  const applyServerFilters = () => {
    setAppliedPlaca(placaFilter);
    setAppliedVendedor(vendedorFilter);
    setAppliedComprador(compradorFilter);
    setAppliedGestor(gestorFilter);
    setAppliedFirmado(firmadoFilter);

    // "Periodo" → fechas: "Rango propio" usa lo que el usuario escribió en el popover; cualquier
    // otro periodo predefinido se calcula con rangoDePeriodo. "Sin periodo" no filtra (null).
    const rango =
      periodo === 'Rango propio'
        ? rangoPropioDesde || rangoPropioHasta
          ? { desde: rangoPropioDesde, hasta: rangoPropioHasta }
          : null
        : rangoDePeriodo(periodo, new Date());
    if (rangoSobre === 'created') {
      setAppliedCreatedFrom(rango?.desde ?? '');
      setAppliedCreatedTo(rango?.hasta ?? '');
      setAppliedUpdatedFrom('');
      setAppliedUpdatedTo('');
    } else {
      setAppliedUpdatedFrom(rango?.desde ?? '');
      setAppliedUpdatedTo(rango?.hasta ?? '');
      setAppliedCreatedFrom('');
      setAppliedCreatedTo('');
    }
    setPage(1);
  };

  const handleSortChange = (nextSortBy: string, nextSortDir: 'asc' | 'desc') => {
    setSortBy(nextSortBy);
    setSortDir(nextSortDir);
    setPage(1);
  };

  // Al desmarcar un filtro específico se limpia su valor (draft Y aplicado): no puede quedar un
  // filtro activo en el backend con el campo escondido (invisible para el usuario).
  const handleToggleFiltroEspecifico = (key: FiltroEspecificoKey) => {
    setFiltrosEspecificos((prev) => {
      const next = new Set(prev);
      if (next.has(key)) {
        next.delete(key);
        switch (key) {
          case 'placa':
            setPlacaFilter('');
            setAppliedPlaca('');
            break;
          case 'vendedor':
            setVendedorFilter('');
            setAppliedVendedor('');
            break;
          case 'comprador':
            setCompradorFilter('');
            setAppliedComprador('');
            break;
          case 'gestor':
            setGestorFilter('');
            setAppliedGestor('');
            break;
          case 'firmado':
            setFirmadoFilter('');
            setAppliedFirmado('');
            break;
        }
      } else {
        next.add(key);
      }
      return next;
    });
    setPage(1);
  };

  const clearFilters = () => {
    setSearch('');
    setModalidad('');
    setEstado('');
    setCompania('');
    setSoloPrioritarios(false);
    setFiltrosEspecificos(new Set());
    setPlacaFilter('');
    setVendedorFilter('');
    setCompradorFilter('');
    setGestorFilter('');
    setFirmadoFilter('');
    setRangoSobre('created');
    setPeriodo('Sin periodo');
    setRangoPropioDesde('');
    setRangoPropioHasta('');
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

  // Quita el chip de periodo: limpia borrador (periodo + rango propio) Y lo ya aplicado al
  // backend (createdFrom/To o updatedFrom/To, según a qué apuntaba) — mismo criterio que
  // `handleToggleFiltroEspecifico` para los filtros específicos.
  const handleQuitarPeriodo = () => {
    setPeriodo('Sin periodo');
    setRangoPropioDesde('');
    setRangoPropioHasta('');
    setAppliedCreatedFrom('');
    setAppliedCreatedTo('');
    setAppliedUpdatedFrom('');
    setAppliedUpdatedTo('');
    setPage(1);
  };

  return (
    // Sin tarjeta blanca envolvente: en el diseño la pantalla es una pila de bloques sobre el
    // fondo azul claro (título en tarjeta, KPIs en tarjeta, tabs desnudos, filas como tarjetas).
    // Meter todo dentro de un contenedor blanco aplanaba esa jerarquía.
    <section className="flex min-w-0 flex-col gap-4">
      {/* Título del módulo en tarjeta blanca (PageHeaderCard). */}
      <div className="rounded-2xl border border-[#DFE5ED] bg-white px-5 py-3 dark:border-white/10 dark:bg-[#162744]">
        <h1 className="text-2xl font-bold leading-tight" style={{ color: '#557EFF' }}>
          Gestión integral de trámites
        </h1>
        <p className="mt-1 text-sm leading-snug text-[#162744]/70 dark:text-white/60">
          Administra, monitorea y radica tus trámites ante organismos de tránsito en tiempo real.
        </p>
      </div>

      <div className="flex min-w-0 flex-col gap-4">
        {/* Tira de KPIs por estado + botón general "Nuevo trámite" a su derecha, como en el
            diseño. Sustituye a la fila de botones por modalidad (Matrícula inicial / Traspaso
            estándar) y a la píldora "Buscar": la modalidad se elige DENTRO del botón general y
            la búsqueda vive en el panel de filtros. */}
        {/* El botón se renderiza SIEMPRE, también con la lista vacía: es la única vía para crear
            el primer trámite. La tira de KPIs sí es condicional (sin datos no hay nada que contar). */}
        <div className="flex items-stretch justify-end gap-4">
          {!loading && !error && items.length > 0 ? (
            <div className="min-w-0 flex-1">
              <EstadoFunnel
                counts={estadoCounts}
                active={estado}
                onSelect={handleEstadoChange}
              />
            </div>
          ) : null}
          {/* Flujo del diseño: entra DIRECTO al asistente; el tipo de trámite se elige dentro del
              paso 1, no en un diálogo previo. */}
          <button
            type="button"
            onClick={() => onNewTramite?.()}
            disabled={blockNew.matricula && blockNew.traspaso}
            title={
              blockNew.matricula && blockNew.traspaso
                ? 'La compañía tiene bloqueada la creación de trámites.'
                : undefined
            }
            // Sin icono: en la propuesta el botón es solo el rótulo en dos líneas. El "+" no añadía
            // nada que el texto no dijera y competía con él por el centro del botón.
            className="flex min-h-[88px] w-28 shrink-0 flex-col items-center justify-center rounded-2xl text-sm font-semibold leading-tight text-white transition hover:opacity-95 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-45"
            style={{ background: WIZARD_CTA_GRADIENT }}
          >
            <span>
              Nuevo
              <br />
              trámite
            </span>
          </button>
        </div>

        {/* Tabs de modalidad + fila de acciones compactas (búsqueda, Periodo, + Filtro, Columnas)
            + estrella de prioritarios + actualizar. Reemplaza a la tarjeta blanca de filtros
            SIEMPRE visible (~185px de alto, casi muda en reposo): la tabla queda como foco. */}
        <TramitesListToolbar
          modalidad={modalidad}
          onModalidadChange={handleModalidadChange}
          onRefresh={() => void load()}
          loading={loading}
          hasActiveFilters={hasActiveFilters}
          soloPrioritarios={soloPrioritarios}
          onPrioritariosChange={handlePrioritariosChange}
          actions={
            <TramitesFiltrosBar
              rangoSobre={rangoSobre}
              onRangoSobreChange={setRangoSobre}
              periodo={periodo}
              onPeriodoChange={setPeriodo}
              rangoPropioDesde={rangoPropioDesde}
              rangoPropioHasta={rangoPropioHasta}
              onRangoPropioDesdeChange={setRangoPropioDesde}
              onRangoPropioHastaChange={setRangoPropioHasta}
              filtrosEspecificos={filtrosEspecificos}
              onToggleFiltroEspecifico={handleToggleFiltroEspecifico}
              placa={placaFilter}
              onPlacaChange={setPlacaFilter}
              vendedor={vendedorFilter}
              onVendedorChange={setVendedorFilter}
              comprador={compradorFilter}
              onCompradorChange={setCompradorFilter}
              gestor={gestorFilter}
              onGestorChange={setGestorFilter}
              firmado={firmadoFilter}
              onFirmadoChange={setFirmadoFilter}
              search={search}
              onSearchChange={handleSearchChange}
              onAplicar={applyServerFilters}
              onEmpezarDeCero={clearFilters}
              empezarDeCeroDisabled={!hasActiveFilters && !hasDraftFilters}
              columnSelector={
                <ColumnSelector
                  columns={TRAMITES_COLUMNS}
                  visible={visibleColumns}
                  onChange={setVisibleColumns}
                  label="Columnas"
                  disabled={savingColumns}
                />
              }
              isAdmin={isAdmin}
              companias={companias}
              compania={compania}
              onCompaniaChange={handleCompaniaChange}
            />
          }
        />

        {/* Tira de chips: SOLO existe si hay periodo o algún filtro específico activo — sin
            tarjeta ni borde, `mt-2`. */}
        <TramitesFiltrosChips
          periodo={periodo}
          filtrosEspecificos={filtrosEspecificos}
          onToggleFiltroEspecifico={handleToggleFiltroEspecifico}
          onQuitarPeriodo={handleQuitarPeriodo}
          appliedPlaca={appliedPlaca}
          appliedVendedor={appliedVendedor}
          appliedComprador={appliedComprador}
          appliedGestor={appliedGestor}
          appliedFirmado={appliedFirmado}
        />

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
          visibleColumns={effectiveColumns}
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
          onOpenDetalle={setDetalleTramite}
          onOpenTrackingTramite={setTrackingTramite}
          onOpenIdentidadTracking={setIdentidadTracking}
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

      {/* Frente C, etapa 1 — modal de detalle para trámites ya radicados (estado ≠ 'borrador'). */}
      <TramiteDetalleModal
        open={detalleTramite !== null}
        onClose={() => setDetalleTramite(null)}
        instanceId={detalleTramite?.id ?? null}
        tenantId={isAdmin ? detalleTramite?.tenantId : undefined}
        item={detalleTramite}
      />

      <TramiteTrackingModal
        open={trackingTramite !== null}
        onClose={() => setTrackingTramite(null)}
        instanceId={trackingTramite?.id ?? null}
        tenantId={isAdmin ? trackingTramite?.tenantId : undefined}
        titleHint={
          trackingTramite
            ? [trackingTramite.referenceNumber, trackingTramite.placa].filter(Boolean).join(' · ')
            : null
        }
      />

      <IdentidadParteTrackingModal
        open={identidadTracking !== null}
        onClose={() => setIdentidadTracking(null)}
        instanceId={identidadTracking?.item.id ?? null}
        tenantId={isAdmin ? identidadTracking?.item.tenantId : undefined}
        parte={identidadTracking?.parte ?? 'comprador'}
        rotulo={identidadTracking?.rotulo ?? 'Comprador'}
      />

      {processTarget && (
        <div
          // Overlay FLIT (component.modal): rgba(22,39,68,0.45) + blur 6px. Antes era
          // `bg-slate-900/40`, y la escala slate de Tailwind no es paleta de este producto.
          className="fixed inset-0 z-[90] flex items-center justify-center px-4 backdrop-blur-[6px]"
          style={{ background: 'rgba(22,39,68,0.45)' }}
          role="dialog"
          aria-modal="true"
          aria-labelledby="procesar-plate-title"
        >
          <div
            className="w-full max-w-md max-h-[90dvh] overflow-y-auto rounded-2xl bg-white p-6 shadow-2xl dark:bg-[#162744]"
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
                  style={{ background: WIZARD_CTA_GRADIENT }}
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
                    style={{ background: WIZARD_CTA_GRADIENT }}
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

/**
 * Cabecera ordenable — mismo patrón que OT ClientProceduresTable. Se renderiza DENTRO del `<th>`
 * (que ya aporta el contexto de bloque): sin envolver en un `<div>` de más, para no duplicar
 * semántica sobre la propia celda de cabecera.
 */
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
    return <>{column.label}</>;
  }
  const apiKey = tramitesColumnToSortBy(column.key);
  const active = sortBy === apiKey;
  const nextDir: 'asc' | 'desc' = active && sortDir === 'asc' ? 'desc' : 'asc';
  const Icon = !active ? ArrowUpDown : sortDir === 'asc' ? ArrowUp : ArrowDown;
  return (
    <button
      type="button"
      className="inline-flex items-center gap-1 uppercase hover:opacity-80"
      aria-label={`Ordenar por ${column.label}${active ? ` (${sortDir === 'asc' ? 'ascendente' : 'descendente'})` : ''}`}
      onClick={() => onSortChange(apiKey, nextDir)}
    >
      {column.label}
      <Icon className="h-3 w-3 opacity-60" aria-hidden="true" />
    </button>
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
  onOpenDetalle,
  onOpenTrackingTramite,
  onOpenIdentidadTracking,
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
  /** Frente C, etapa 1 — abre el modal de detalle (trámites YA RADICADOS, estado ≠ 'borrador'). */
  onOpenDetalle: (item: InstanceSummary) => void;
  /** Click en badge Estado → modal de línea de tiempo del trámite. */
  onOpenTrackingTramite: (item: InstanceSummary) => void;
  onOpenIdentidadTracking: (target: {
    item: InstanceSummary;
    parte: BiometricParte;
    rotulo: string;
  }) => void;
}) {
  if (loading) {
    // Carga de la pantalla principal del módulo: va con el loader de marca y no con barras de
    // esqueleto. Las barras siguen siendo el patrón correcto DENTRO del detalle —allí cada bloque
    // carga por separado y el esqueleto conserva la silueta de la sección—, pero aquí se está
    // esperando la pantalla entera, y esa espera es la que el loader del módulo tiene que nombrar.
    return <CarLoaderModal label="Cargando trámites…" />;
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
  // `<colgroup>` no admite `fr` (lo que usa `gridLayout`): los anchos en % se calculan aparte,
  // con el MISMO `visibleColumns` — así quedan alineados con `visibleDefs` por construcción.
  const colWidths = buildTramitesColWidths(visibleColumns, {
    includeSelectColumn: gridLayout.includeSelectColumn,
  });

  return (
    // Scroll normal de página: la tabla crece con su contenido y solo scrollea en horizontal
    // cuando las columnas visibles no caben a lo ancho. La píldora de conteo que iba aquí se
    // fundió con "Mostrando X de Y" de `PageNav` — un solo lugar para el recuento.
    <div className="flex flex-col">
      <div className="overflow-x-auto">
        <table
          aria-label="Trámites en curso"
          style={{
            minWidth: `${gridLayout.minWidthPx}px`,
            borderCollapse: 'separate',
            borderSpacing: '0 8px',
            tableLayout: 'fixed',
          }}
        >
          <colgroup>
            {/* Columna de selección solo cuando hay borradores ICT (si no, hueco vacío al inicio). */}
            {gridLayout.includeSelectColumn ? <col style={{ width: colWidths[0] }} /> : null}
            {visibleDefs.map((col, index) => (
              <col
                key={col.key}
                style={{
                  width: colWidths[gridLayout.includeSelectColumn ? index + 1 : index],
                }}
              />
            ))}
            <col style={{ width: colWidths[colWidths.length - 1] }} />
          </colgroup>
          <thead>
            <tr>
              {gridLayout.includeSelectColumn ? (
                <th
                  scope="col"
                  className="rounded-l-xl px-2 py-2.5 text-left text-xs font-semibold uppercase tracking-wider"
                  style={{ background: '#DFE5ED', color: '#162744' }}
                >
                  {/* Nombre real, no `aria-hidden`: ocultar una cabecera desalinea el recuento de
                      columnas del lector de pantalla respecto a las celdas de cada fila. */}
                  <span className="sr-only">Selección</span>
                </th>
              ) : null}
              {visibleDefs.map((col, index) => (
                <th
                  key={col.key}
                  scope="col"
                  className={`px-2 py-2.5 text-left text-xs font-semibold uppercase tracking-wider ${
                    !gridLayout.includeSelectColumn && index === 0 ? 'rounded-l-xl' : ''
                  }`}
                  style={{ background: '#DFE5ED', color: '#162744' }}
                >
                  <SortableHeaderCell
                    column={col}
                    sortBy={sortBy}
                    sortDir={sortDir}
                    onSortChange={onSortChange}
                  />
                </th>
              ))}
              <th
                scope="col"
                className="rounded-r-xl px-2 py-2.5 text-right text-xs font-semibold uppercase tracking-wider"
                style={{ background: '#DFE5ED', color: '#162744' }}
              >
                Acciones
              </th>
            </tr>
          </thead>
          <tbody>
            {paginated.map((item) => (
              <TramiteRow
                key={item.id}
                item={item}
                visibleColumns={visibleKeysOrdered}
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
                onOpenDetalle={onOpenDetalle}
                onOpenTrackingTramite={onOpenTrackingTramite}
                onOpenIdentidadTracking={onOpenIdentidadTracking}
              />
            ))}
          </tbody>
        </table>
      </div>

      {/* Fuera del contenedor con scroll horizontal: la paginación no se desplaza con la tabla. */}
      <PageNav
        page={page}
        totalPages={totalPages}
        resumen={`Mostrando ${paginated.length} de ${filtered.length}`}
        ariaLabel="Paginación de trámites"
        onPageChange={onPageChange}
      />
    </div>
  );
}

/**
 * Celda de actor: solo el nombre. La acreditación de esa parte ya NO vive aquí — se consolidó en
 * la columna única "Firmas" (ver `FirmaParteLinea`), que es lo que dibuja el diseño. Tenerla en
 * los dos sitios repetía el mismo chip dos veces por fila.
 */
function ActorCell({ nombre }: { nombre: string | null | undefined }) {
  const texto = nombre?.trim();
  return (
    <span
      className="block w-full truncate text-[#162744] dark:text-white/90"
      title={texto || undefined}
    >
      {texto || '—'}
    </span>
  );
}

/**
 * Una parte dentro de la columna "Firmas": rótulo + chip de acreditación (identidad validada o
 * firma del baúl). El rótulo NO es decorativo — con dos chips apilados es lo único que dice de
 * quién es cada firma.
 *
 * `estado` null significa que la parte existe pero aún no tiene acreditación registrada; se
 * muestra como "Sin registrar" en vez de un guion mudo, que se confundía con "no aplica".
 */
function FirmaParteLinea({
  rotulo,
  estado,
  onOpenTracking,
}: {
  rotulo: string;
  estado?: FirmaParteEstado | null;
  /** Click en el indicador → modal de tracking de identidad de esta parte. */
  onOpenTracking?: () => void;
}) {
  // Fragmento de DOS celdas, no una línea cerrada: la rejilla vive en el contenedor (ver la celda
  // `firmado`), y así el valor de vendedor y el de comprador quedan alineados en la misma columna.
  // Con la línea corrida anterior ("Vendedor: …" / "Comprador: …") los valores bailaban, porque
  // los dos rótulos no miden lo mismo, y la columna no se podía barrer en vertical.
  const valor = estado ? (
    <span
      className="whitespace-nowrap text-xs font-semibold"
      style={{ color: FIRMA_TEXTO[estado].color }}
    >
      {FIRMA_TEXTO[estado].label}
    </span>
  ) : (
    <span className="whitespace-nowrap text-xs text-[#162744]/70 dark:text-white/70">
      Sin registrar
    </span>
  );

  return (
    <>
      <span className="whitespace-nowrap text-xs text-[#162744]/70 dark:text-white/70">
        {rotulo}
      </span>
      {onOpenTracking ? (
        <button
          type="button"
          onClick={(e) => {
            e.stopPropagation();
            onOpenTracking();
          }}
          aria-label={`Ver tracking de identidad de ${rotulo}`}
          title={`Ver tracking de identidad · ${rotulo}`}
          className="rounded focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-1"
        >
          {valor}
        </button>
      ) : (
        valor
      )}
    </>
  );
}

/** Fila de trámite: clickable (abre el wizard) + acciones explícitas (documentos, consolidado, Continuar/Ver). */
function TramiteRow({
  item,
  visibleColumns,
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
  onOpenDetalle,
  onOpenTrackingTramite,
  onOpenIdentidadTracking,
}: {
  item: InstanceSummary;
  /** Claves visibles (selector de columnas) — misma lista/orden que usa la cabecera. */
  visibleColumns: readonly string[];
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
  /** Frente C, etapa 1 — abre el modal de detalle (trámites YA RADICADOS, estado ≠ 'borrador'). */
  onOpenDetalle: (item: InstanceSummary) => void;
  onOpenTrackingTramite: (item: InstanceSummary) => void;
  onOpenIdentidadTracking: (target: {
    item: InstanceSummary;
    parte: BiometricParte;
    rotulo: string;
  }) => void;
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
  // HU #11668 — solo los chips derivados de la identidad llevan ayuda; el chip base de estado
  // (radicado, entregado…) no habla de acreditación y no debe crecerle un tooltip.
  const ayudaIdentidad = async?.ayuda ?? null;
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
  // Frente C, etapa 1 (Tramites.tsx:222 de la propuesta) — borrador → asistente; radicado → modal
  // de detalle, sin navegar. `isPaused` solo aplica a borradores ICT, así que el chequeo de pausa
  // queda intacto dentro de esa rama.
  const handleOpen = () => {
    if (item.estado !== 'borrador') {
      onOpenDetalle(item);
      return;
    }
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
  // Celdas COMPUESTAS — `radicado`, `placa` y `tramite` apilan el dato de otra columna SOLO si esa
  // columna está oculta. Al activarla desde el selector, el dato se muda a su propia columna en vez
  // de aparecer dos veces. Es lo que permite adoptar el layout del diseño sin romper las
  // preferencias de columnas ya guardadas por cada usuario.
  const shows = (key: string) => visibleColumns.includes(key);

  const cellsByKey: Record<string, React.ReactNode> = {
    radicado: (
      <span className="flex min-w-0 flex-col gap-0.5">
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
          {/* Acceso por teclado/lector de pantalla a la fila: el `<tr>` ya no es focuseable (una
              tabla semántica no puede tener `role="button"` en la fila), así que el radicado
              lleva el mismo `handleOpen`/aria-label de antes en un botón real. El aspecto en
              reposo no cambia: mismo font-mono font-semibold, subrayado solo al interactuar. */}
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation();
              handleOpen();
            }}
            aria-label={`Abrir trámite ${item.referenceNumber}`}
            className="min-w-0 truncate font-mono font-semibold text-[#162744] hover:underline focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 dark:text-white"
          >
            {item.referenceNumber}
          </button>
        </span>
        {!shows('fechaCreacion') ? (
          <span className="block truncate text-xs text-[#162744]/60 dark:text-white/50">
            Creación: {shortDate(item.createdAt)}
          </span>
        ) : null}
        {!shows('fechaActualizacion') && item.updatedAt ? (
          <span className="block truncate text-xs text-[#162744]/60 dark:text-white/50">
            Actualización: {shortDate(item.updatedAt)}
          </span>
        ) : null}
      </span>
    ),
    vin: (
      <span className="block truncate font-mono text-xs text-[#162744]/80 dark:text-white/70">
        {item.vin ?? '—'}
      </span>
    ),
    placa: (
      <span className="block min-w-0">
        <span className="block truncate font-mono font-semibold tracking-wider text-[#162744] dark:text-white">
          {item.placa ?? '—'}
        </span>
        {!shows('vehiculo') ? (
          <span
            className="block truncate text-xs text-[#162744]/60 dark:text-white/50"
            title={vehiculo(item)}
          >
            {vehiculo(item)}
          </span>
        ) : null}
      </span>
    ),
    // UNA columna para las firmas de AMBAS partes, como en el diseño. La acreditación (identidad
    // validada o firma del baúl) es POR PARTE, así que la celda lleva una línea por cada una con
    // su rótulo: dos chips sueltos no dirían de quién es cada firma.
    //
    // Qué partes aparecen depende del tipo de trámite: el traspaso tiene vendedor y comprador; la
    // matrícula inicial no tiene vendedor, así que se muestra solo el comprador en lugar de gastar
    // una línea en un "No aplica" repetido en todas las filas.
    firmado: (
      <span className="grid min-w-0 grid-cols-[auto_auto] justify-start items-center gap-x-2 gap-y-1">
        {item.modalidad === 'TRASPASO' ? (
          <FirmaParteLinea
            rotulo="Vendedor"
            estado={item.firmaVendedorEstado}
            onOpenTracking={() =>
              onOpenIdentidadTracking({ item, parte: 'vendedor', rotulo: 'Vendedor' })
            }
          />
        ) : null}
        <FirmaParteLinea
          rotulo="Comprador"
          estado={item.firmaCompradorEstado}
          onOpenTracking={() =>
            onOpenIdentidadTracking({ item, parte: 'comprador', rotulo: 'Comprador' })
          }
        />
      </span>
    ),
    // El chip de estado se inyecta más abajo (solo si la columna `estado` está oculta), para
    // reutilizar EXACTAMENTE la misma celda —popover de rechazo incluido— en vez de duplicarla.
    tramite: (
      <span className="flex min-w-0 flex-col items-start gap-1">
        <span
          className="block truncate text-xs font-semibold text-[#162744] dark:text-white"
          // Los nombres de OTROS son largos («Levantamiento de prenda») y la celda es angosta: el
          // truncado necesita que el nombre completo siga estando disponible al pasar por encima.
          title={tramiteLabel(item)}
        >
          {tramiteLabel(item)}
        </span>
        {!shows('paso') ? (
          <span className="flex min-w-0 items-center gap-1 text-xs text-[#162744]/60 dark:text-white/50">
            <span className="shrink-0 font-mono tabular-nums">
              {item.pasoActual}/{item.totalPasos}
            </span>
            <span className="truncate">{stepLabel(item)}</span>
          </span>
        ) : null}
      </span>
    ),
    vehiculo: (
      <span className="block truncate text-[#162744]/90 dark:text-white/80" title={vehiculo(item)}>
        {vehiculo(item)}
      </span>
    ),
    propietario: (
      <ActorCell nombre={item.vendedorNombre} />
    ),
    comprador: (
      <ActorCell nombre={item.compradorNombre} />
    ),
    paso: (
      <span className="block min-w-0">
        <span className="block font-mono text-xs text-[#162744]/70 dark:text-white/60">
          {item.pasoActual}/{item.totalPasos}
        </span>
        <span className="block truncate text-xs text-[#162744]/60 dark:text-white/50">
          {stepLabel(item)}
        </span>
      </span>
    ),
    estado: (
      <span className="relative flex min-w-0 flex-col items-start gap-1">
        <span className="flex min-w-0 flex-wrap items-center gap-1.5">
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation();
              onOpenTrackingTramite(item);
            }}
            aria-label={`Ver trazabilidad del trámite ${item.referenceNumber}`}
            title="Ver línea de tiempo del trámite"
            className="rounded-full focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-1"
          >
            {ayudaIdentidad ? (
              <IdentidadChip
                chip={chip}
                ayuda={ayudaIdentidad}
                tipId={`identidad-ayuda-${item.id}`}
              />
            ) : (
              <StatusBadge label={chip.label} bg={chip.bg} color={chip.color} border={chip.border} />
            )}
          </button>
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
                    <p className="text-xs font-semibold uppercase tracking-wide text-[#c2410c]">
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
                        className="text-xs font-semibold px-2 py-0.5 rounded-full border whitespace-nowrap"
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
                        className="text-xs font-semibold px-2 py-0.5 rounded-full border whitespace-nowrap"
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
            className="inline-flex shrink-0 items-center whitespace-nowrap rounded-full border border-[#162744]/20 bg-[#162744]/[0.06] px-2 py-0.5 text-xs font-semibold text-[#162744]/70 dark:border-white/20 dark:bg-white/10 dark:text-white/70"
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
            className="text-xs leading-tight text-[#162744]/45 dark:text-white/40 truncate"
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
    // Sin `truncate`: el nombre del organismo se lee entero o no sirve de nada — cortado a
    // "SECRETARIA DISTRITAL DE…" no distingue una secretaría de otra, que es justo para lo que
    // está la columna. Envuelve en varias líneas; la fila crece lo que haga falta.
    secretaria: (
      <span className="block text-xs leading-snug text-balance break-words text-[#162744]/90 dark:text-white/80">
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
          className="block truncate text-xs text-[#162744]/60 dark:text-white/50"
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

  // Composición del layout del diseño: con la columna `estado` oculta, su celda —chip, popover de
  // rechazo/subsanación, "Pausado" y sub-estado de placa— se apila dentro de "Trámite / Estado".
  // Se REUTILIZA la celda ya construida en vez de escribir una segunda versión, para que ambas
  // rutas no puedan divergir.
  if (!shows('estado')) {
    cellsByKey.tramite = (
      <span className="flex min-w-0 flex-col items-start gap-1">
        {cellsByKey.tramite}
        {cellsByKey.estado}
      </span>
    );
  }

  // `<tr>` conserva el onClick (comodidad de ratón) pero PIERDE role/tabIndex/onKeyDown/aria-label:
  // un <tr role="button"> rompería la semántica de tabla. El acceso por teclado/lector de pantalla
  // vive en el botón del radicado (ver cellsByKey.radicado), con el mismo aria-label de antes.
  return (
    <tr onClick={handleOpen} className="group cursor-pointer bg-white text-xs transition dark:bg-[#162744]">
      {/* ICT — checkbox de selección solo si la tabla reservó la pista (hay borradores ICT). Como
          <tr> no admite border-radius, el borde/radio de "tarjeta" vive en cada <td>. */}
      {includeSelectColumn ? (
        <td
          className="rounded-l-xl border-y border-l border-[#DFE5ED] px-4 py-3 align-middle group-hover:border-[#557EFF]/40 dark:border-white/10"
          onClick={(e) => e.stopPropagation()}
        >
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
        </td>
      ) : null}
      {/* Selector de columnas: solo se renderizan las visibles, en el orden canónico de
          TRAMITES_COLUMNS — la misma lista/orden que usa la cabecera (TableBody). */}
      {visibleColumns.map((key, index) => (
        <td
          key={key}
          className={`border-y border-[#DFE5ED] px-4 py-3 align-middle text-xs group-hover:border-[#557EFF]/40 dark:border-white/10 ${
            !includeSelectColumn && index === 0 ? 'border-l rounded-l-xl' : ''
          }`}
        >
          {cellsByKey[key]}
        </td>
      ))}
      {/* La fila entera navega al wizard, así que las acciones detienen la propagación en un
          envoltorio: el menú no recibe el evento y no hay que filtrarlo acción por acción.
          Se conserva el `ActionsMenu` que introdujo el subflujo de placa (HU #11037): las acciones
          de documentos y consolidado entran como ítems suyos, en vez de montar un segundo grupo de
          botones en la misma celda. */}
      <td
        className="rounded-r-xl border-y border-r border-[#DFE5ED] px-4 py-3 text-right align-middle group-hover:border-[#557EFF]/40 dark:border-white/10"
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
      </td>
      {/* ICT — confirmación FLIT (Modal con blur/overlay/CTA degradado) al continuar un trámite
          pausado. `Modal` se porta vía `createPortal` a `document.body`: no llega a insertarse
          como hijo real del `<tr>` en el DOM, así que no rompe la validez de la tabla. */}
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
    </tr>
  );
}
