'use client';

import { useCallback, useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react';
import { formatFecha } from '@/lib/format/date';
import { useRouter } from 'next/navigation';
import {
  AlertCircle,
  ArrowDown,
  ArrowUp,
  Download,
  ArrowUpDown,
  CheckCircle2,
  Eye,
  FileCheck,
  FileStack,
  FileText,
  History,
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
  tramitesSortOptions,
  type TramitesColumnDef,
  type TramitesGridLayout,
} from '@/lib/tramites/tramites-table-columns';
import type { QueryCondition, QueryField } from '@/lib/api/queries';
import { buildWorkbook, type DataColumn } from '@/components/consultas/columns';
import { download, EXPORT_BATCH_SIZE, exportarPorLotes } from '@/components/consultas/export';
import { XLSX_MIME } from '@/lib/xlsx';
import { nombreArchivoTramites, selloDeArchivo } from './tramites-export';
import { tramitesExportFields } from '@/lib/tramites/tramites-table-columns';
import {
  FIRMA_TEXTO,
  FUENTE_LABEL,
  marcasDe,
  stepLabel,
  tramiteLabel,
  vehiculo,
} from '@/lib/tramites/tramites-row-labels';
import { useUiPreferences } from '@/hooks/useUiPreferences';
import { useNavigableModules } from '@/hooks/useNavigableModules';
import { controlCls } from './tramites-control-styles';
import { StatusBadge } from '@/components/atom/StatusBadge';
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from '@/components/atom/table-styles';
import { PageNav } from '@/components/atom/PageNav';
import { Modal } from '@/components/atom/Modal';
import { ActionsMenu, type ActionsMenuItem } from '@/components/atom/ActionsMenu';
import { ColumnSelector } from '@/components/atom/ColumnSelector';
import { ModuleTitle } from '@/components/atom/modules/ModuleTitle';
import { InlineAlert } from '@/components/atom/InlineAlert';
import { EstadoFunnel } from './EstadoFunnel';
import {
  AttachmentPreview,
  TramiteDocumentosModal,
  useAttachmentPreview,
} from './TramiteDocumentosModal';
import { TramiteDetalleModal } from './TramiteDetalleModal';
import { TramiteTrackingModal } from './TramiteTrackingModal';
import { useAdminTramiteAcciones } from './AdminTramiteAcciones';
import type {
  BiometricParte,
  FirmaParteEstado,
  InstanceStatus,
  InstanceSummary,
  ListInstancesParams,
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

// Selector de columnas — el ancho/orden de cada columna vive en TRAMITES_COLUMNS
// (lib/tramites/tramites-table-columns.ts); `buildTramitesGridLayout` calcula el
// `gridTemplateColumns` (Selección + visibles + Acciones) UNA sola vez a partir de las columnas
// visibles, y tanto la cabecera como cada fila lo reciben ya calculado: quedan alineadas por
// construcción sin importar cuántas columnas se oculten.

/**
 * HU #12188 — tamaños de página que ofrece el selector, y el de arranque.
 *
 * <p>Hasta esta HU la tabla hacía UNA llamada de 200 filas y paginaba en memoria. Con 5.000
 * trámites llegaban 200 y punto: cambiar de página no volvía a pedir nada, y la búsqueda filtraba
 * sobre esas 200 —devolviendo «sin resultados» para trámites que sí existen— mientras los
 * contadores de estado, que sí salían del servidor, reportaban el universo completo. La pantalla
 * se contradecía sola.</p>
 *
 * <p>El tope de 100 no es una preferencia: es el techo que admite el endpoint por página razonable.
 * Pedir más no acerca a nadie a su trámite —para eso están los filtros y la búsqueda— y sí alarga
 * cada carga.</p>
 */
const TAMANOS_DE_PAGINA = [10, 25, 50, 100] as const;
const PAGE_SIZE_POR_DEFECTO = 10;

/**
 * Dónde se recuerda el tamaño de página. El requerimiento pide «durante la sesión», así que va en
 * `sessionStorage` y no en la preferencia de usuario del servidor: es una comodidad del rato, no
 * una decisión que deba seguir a la persona a otro equipo. Se lee con guarda porque en una ventana
 * privada el acceso puede lanzar.
 */
const CLAVE_PAGE_SIZE = 'tramites.pageSize';

/**
 * `useSyncExternalStore` exige una suscripción; aquí no hay ninguna a la que suscribirse —
 * `sessionStorage` solo lo cambia esta misma pantalla, y cuando lo hace ya actualiza su estado.
 */
function suscripcionInerte(): () => void {
  return () => {};
}

function leerPageSizeGuardado(): number {
  try {
    const guardado = Number(sessionStorage.getItem(CLAVE_PAGE_SIZE));
    return TAMANOS_DE_PAGINA.includes(guardado as (typeof TAMANOS_DE_PAGINA)[number])
      ? guardado
      : PAGE_SIZE_POR_DEFECTO;
  } catch {
    return PAGE_SIZE_POR_DEFECTO;
  }
}

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
  /** Conteo por estado del UNIVERSO filtrado — lo sirve el backend, no se deriva de `items`. */
  const [estadoCounts, setEstadoCounts] = useState<Record<string, number>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  /** Bloqueo de creación por modalidad (config compañía → Trámites). */
  const [blockNew, setBlockNew] = useState<{ matricula: boolean; traspaso: boolean }>({
    matricula: false,
    traspaso: false,
  });

  // Lo que el gestor está tecleando, y lo que ya se le pidió al servidor.
  const [search, setSearch] = useState('');
  /**
   * HU #12188 — el texto que de verdad viaja en la consulta, con un respiro tras la última tecla.
   *
   * <p>La búsqueda dejó de resolverse en el navegador, así que cada tecla sería una consulta: para
   * escribir «KYU631» irían seis, y las cinco primeras se descartan al llegar. El respiro no es una
   * optimización cosmética — sin él, la respuesta de una búsqueda a medias puede llegar DESPUÉS de
   * la completa y pintar un resultado que ya no corresponde a lo que se ve en el campo.</p>
   */
  const [busquedaAplicada, setBusquedaAplicada] = useState('');
  // Selector de modalidad del botón general "Nuevo trámite". La modalidad elegida se guarda
  // aparte del filtro `modalidad` del listado: son cosas distintas (crear vs filtrar).
  // ADR-0050 — el campo `modalidad` de la fila transporta ya la FAMILIA del tipo.
  const [modalidad, setModalidad] = useState<'' | ProcedureFamily>('');
  const [estado, setEstado] = useState<'' | InstanceStatus>('');
  // #1 — Filtro por compañía, solo relevante para el SuperAdmin (ve todas las empresas).
  // HU #10536 — filtro "solo prioritarios".
  const [soloPrioritarios, setSoloPrioritarios] = useState(false);

  // Filtros server-side (mismo contrato que GET /instances). Borrador del form → se aplican
  // solo con "Aplicar filtros"; el sort sí recarga de inmediato al clic en cabecera.
  /**
   * HU #12107 — condiciones de la gramática de Consultas. Sustituyen a los siete filtros sueltos
   * (placa, vendedor, comprador, gestor, firmado, organismo, tipo), cada uno con su propio estado y
   * su propio input: aquellos solo sabían preguntar «contiene» y añadir un campo obligaba a tocar
   * cuatro sitios. Se conserva la separación borrador/aplicado que ya tenía la pantalla.
   */
  const [draftCondiciones, setDraftCondiciones] = useState<QueryCondition[]>([]);
  const [appliedCondiciones, setAppliedCondiciones] = useState<QueryCondition[]>([]);
  /** Catálogo servido por el backend: un campo nuevo aparece sin desplegar frontend. */
  const [queryFields, setQueryFields] = useState<QueryField[]>([]);
  const [fieldsError, setFieldsError] = useState(false);
  const [fieldsKey, setFieldsKey] = useState(0);
  // "Rango sobre" + "Periodo" reemplazan a los 4 inputs de fecha sueltos: el usuario elige a qué
  // campo apunta el rango (creación o actualización) y un periodo predefinido — o "Rango propio"
  // con fechas propias. `rangoDePeriodo` (TramitesFiltrosBar) hace la conversión a
  // createdFrom/createdTo o updatedFrom/updatedTo al pulsar "Aplicar filtros".
  const [rangoSobre, setRangoSobre] = useState<RangoSobre>('created');
  const [periodo, setPeriodo] = useState('Sin periodo');
  const [rangoPropioDesde, setRangoPropioDesde] = useState('');
  const [rangoPropioHasta, setRangoPropioHasta] = useState('');
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
  /**
   * ¿El gestor cambió las columnas respecto al default? Es lo que marca "Columnas" en azul, con el
   * mismo criterio que "Periodo" y "+ Filtro": el azul dice "aquí hay algo aplicado". Antes el
   * botón venía azul de fábrica y el color no distinguía nada.
   */
  const columnasPersonalizadas = useMemo(() => {
    const actual = [...visibleColumns].sort();
    const porDefecto = [...DEFAULT_TRAMITES_VISIBLE_COLUMNS].sort();
    return (
      actual.length !== porDefecto.length || actual.some((k, i) => k !== porDefecto[i])
    );
  }, [visibleColumns]);

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
  /**
   * Ruta al asistente de pasos. Vive aquí y no en cada llamador porque el `?t=` del SuperAdmin
   * (trámite de OTRA compañía) tiene que viajar igual desde la fila y desde el modal de detalle.
   */
  const abrirAsistente = useCallback(
    (id: string, tenantIdFila?: string) =>
      router.push(
        isAdmin && tenantIdFila
          ? `/tramites/${id}?t=${encodeURIComponent(tenantIdFila)}`
          : `/tramites/${id}`,
      ),
    [router, isAdmin],
  );
  /**
   * HU #12196 — atajo "Ver historial de la placa" desde la fila. El gate NO es un permiso nuevo ni
   * una lectura propia del JWT: es el MISMO catálogo de módulos navegables que pinta el dock y que
   * decide si `?m=historial-placa` se abre o rebota al dashboard. Si aquí se usara otro criterio,
   * la fila podría ofrecer un atajo que el gate de la SPA rechaza acto seguido.
   */
  const { ids: navigableModuleIds } = useNavigableModules();
  const puedeVerHistorialPlaca = navigableModuleIds.includes('historial-placa');
  /**
   * Abre el módulo dedicado con la placa precargada, en vez de repintar la tabla del historial
   * dentro del listado: el PO pidió que la información sea LA MISMA que la del módulo, y dos
   * superficies que muestran lo mismo se separan en cuanto una de las dos cambie.
   *
   * SIN tenant, deliberadamente, y aunque la fila sepa el suyo: el alcance del historial lo decide
   * el servidor por ROL —el SuperAdmin ve la placa en todas las compañías— así que acotarlo al
   * tenant de la fila le escondería justo los trámites de otras empresas, que es el caso que motiva
   * el módulo. (Distinto del detalle del trámite: ese sí es un registro concreto de una compañía
   * concreta y por eso `TramiteDetalleModal` sí recibe `tenantId`.)
   */
  const verHistorialPlaca = useCallback(
    (placa: string) => {
      const normalizada = placa.trim().toUpperCase();
      if (!normalizada) return;
      router.push(`/?m=historial-placa&placa=${encodeURIComponent(normalizada)}`);
    },
    [router],
  );
  /** Click en badge Estado → modal de línea de tiempo del trámite (todas las modalidades). */
  const [trackingTramite, setTrackingTramite] = useState<InstanceSummary | null>(null);
  /** Click en línea Firmas → modal de tracking de identidad de esa parte. */
  const [identidadTracking, setIdentidadTracking] = useState<{
    item: InstanceSummary;
    parte: BiometricParte;
    rotulo: string;
  } | null>(null);

  // HU #12104 — exportación a Excel. `exportNotice` es el resultado (cuántas filas, cuántos
  // archivos): sin él, un export repartido en tres archivos parece un fallo con dos descargas de más.
  const [exporting, setExporting] = useState(false);
  const [exportNotice, setExportNotice] = useState<string | null>(null);
  const [exportError, setExportError] = useState<string | null>(null);

  // Paginación contra el SERVIDOR (1-based): `page` y `pageSize` viajan como `skip`/`take`.
  const [page, setPage] = useState(1);
  /** Total del universo filtrado, tal y como lo cuenta el servidor. */
  const [total, setTotal] = useState(0);

  /**
   * El tamaño de página: lo que el gestor eligió en ESTA pantalla, y si no, lo que dejó guardado.
   *
   * <p>El guardado se lee con `useSyncExternalStore` y no en el inicializador de un `useState`
   * porque `sessionStorage` no existe en el render del servidor: leerlo ahí daría una hidratación
   * distinta a la del cliente. Es el primitivo previsto para esto —tiene una lectura para el
   * servidor y otra para el cliente— y evita tener que arreglarlo después con un efecto.</p>
   */
  const pageSizeGuardado = useSyncExternalStore(
    suscripcionInerte,
    leerPageSizeGuardado,
    () => PAGE_SIZE_POR_DEFECTO,
  );
  const [pageSizeElegido, setPageSizeElegido] = useState<number | null>(null);
  const pageSize = pageSizeElegido ?? pageSizeGuardado;
  /** Popover de motivo OT / subsanación abierto (un solo id a la vez). */
  const [openPopoverId, setOpenPopoverId] = useState<string | null>(null);
  // ICT (paridad v1 pause-unpause-massive) — selección de trámites ICT para pausar/reanudar en lote.
  const [selectedIds, setSelectedIds] = useState<Set<string>>(() => new Set());

  // El respiro tras la última tecla. 350 ms es el rango en que una pausa se lee como «terminé de
  // escribir» sin que la tabla se sienta perezosa.
  useEffect(() => {
    // El setState va dentro del temporizador, no en el cuerpo del efecto: es diferido.
    const id = setTimeout(() => setBusquedaAplicada(search), 350);
    return () => clearTimeout(id);
  }, [search]);

  // HU #12107 — catálogo de campos filtrables. Degrada con elegancia: si no carga, la tabla se
  // pinta igual con su listado y la barra ofrece reintentar (AC7). Nunca bloquea el render.
  useEffect(() => {
    let active = true;
    void tramitesClient
      .listFilterFields()
      .then((fields) => {
        if (!active) return;
        setQueryFields(fields);
        setFieldsError(false);
      })
      .catch(() => {
        if (active) setFieldsError(true);
      });
    return () => {
      active = false;
    };
  }, [fieldsKey]);

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

  /**
   * Los criterios que resuelve el SERVIDOR, tal y como están aplicados ahora mismo.
   *
   * Vive aparte de `load` porque tiene DOS consumidores: el listado y el recorrido del export
   * (HU #12104). Con una copia en cada sitio, el día que se añada un filtro habría que acordarse de
   * ponerlo también en el export — y hasta que alguien lo notara, el Excel traería filas que la
   * pantalla no está mostrando. Así el filtro nuevo llega al archivo sin tocar el export.
   *
   * NO incluye `skip`/`take`: la paginación es cosa de cada consumidor.
   */
  const buildListQuery = useCallback((): ListInstancesParams => {
    const query: ListInstancesParams = {};
    if (appliedCondiciones.length > 0) query.condiciones = appliedCondiciones;
    // Estado y familia van al SERVIDOR, no al array ya traído: el listado devuelve como mucho
    // SERVER_LIST_TAKE filas, así que filtrarlos en cliente respondía "los borradores que cupieron
    // en la ventana" en vez de "los borradores del tenant". Con pocos trámites daba igual; con un
    // tenant grande la respuesta era incompleta y nada lo delataba.
    if (estado) query.estado = estado;
    if (modalidad) query.modalidad = modalidad;
    // HU #12187/#12188 — la búsqueda libre y el marcado prioritario también van al servidor. En el
    // cliente miraban solo las filas traídas: la búsqueda devolvía «sin resultados» para trámites
    // que sí existen, y el prioritario, con paginación real, habría pasado de mirar 200 filas a
    // mirar las diez de la página a la vista.
    if (busquedaAplicada.trim()) query.busqueda = busquedaAplicada.trim();
    if (soloPrioritarios) query.prioritario = true;
    if (appliedCreatedFrom.trim()) query.createdFrom = appliedCreatedFrom.trim();
    if (appliedCreatedTo.trim()) query.createdTo = appliedCreatedTo.trim();
    if (appliedUpdatedFrom.trim()) query.updatedFrom = appliedUpdatedFrom.trim();
    if (appliedUpdatedTo.trim()) query.updatedTo = appliedUpdatedTo.trim();
    if (sortBy) {
      query.sortBy = sortBy;
      query.sortDir = sortDir;
    }
    return query;
  }, [
    appliedCondiciones,
    appliedCreatedFrom,
    appliedCreatedTo,
    appliedUpdatedFrom,
    appliedUpdatedTo,
    busquedaAplicada,
    estado,
    modalidad,
    soloPrioritarios,
    sortBy,
    sortDir,
  ]);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      // HU #12188 — se pide LA PÁGINA, no una ventana fija. Antes iba `take: 200, skip: 0` y la
      // tabla paginaba en memoria: con más de 200 trámites, las páginas siguientes sencillamente
      // no existían.
      const query = { ...buildListQuery(), take: pageSize, skip: (page - 1) * pageSize };
      // Las dos llamadas van en paralelo: la tabla y la tira de KPIs son independientes y
      // encadenarlas solo sumaría latencia. Los conteos no pueden salir de `data` — esa es la
      // PÁGINA, y la tira habla del universo entero.
      const [page1, counts] = await Promise.all([
        tramitesClient.searchInstances(query),
        tramitesClient.searchEstadoCounts(query),
      ]);
      setItems(page1.items);
      setTotal(page1.total);
      setEstadoCounts(counts);

      // Red de seguridad: si el universo encogió por debajo de la página en la que estaba el gestor
      // —al volver del asistente, o porque otro usuario cerró trámites— la respuesta llega vacía y
      // la tabla diría «sin resultados» estando llena. Los once puntos que cambian un criterio ya
      // vuelven a la página 1; esto cubre lo que cambia SIN que nadie toque un filtro.
      const ultimaPagina = Math.max(1, Math.ceil(page1.total / pageSize));
      if (page > ultimaPagina) setPage(ultimaPagina);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Error desconocido');
    } finally {
      setLoading(false);
    }
  }, [buildListQuery, page, pageSize]);

  useEffect(() => {
    // Carga/refresca al montar y al cambiar refreshKey: los setState de `load`
    // ocurren tras el await (no es setState síncrono).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load, refreshKey]);

  /**
   * Conteo por estado para la tira KPI. Viene del servidor (`/instances/estado-counts`), NO de
   * `items`: `items` es una página de como mucho SERVER_LIST_TAKE filas, así que contarla decía
   * "69 borradores" cuando el tenant tenía muchos más.
   *
   * El backend aplica el resto de filtros y descarta el de estado, que es lo que hace que las siete
   * tarjetas sigan diciendo a dónde puede moverse el gestor después de elegir una.
   */
  const estadoCountsMostrados = useMemo(() => {
    const c: Record<EstadoTramite, number> = {
      borrador: 0,
      anulado: 0,
      preparado: 0,
      entregado: 0,
      aprobado: 0,
      rechazado: 0,
      subsanacion: 0,
    };
    for (const key of Object.keys(c) as EstadoTramite[]) c[key] = estadoCounts[key] ?? 0;
    return c;
  }, [estadoCounts]);

  // HU #12188 — YA NO QUEDA NINGÚN FILTRO EN EL CLIENTE.
  //
  // Los tres que sobrevivían aquí se fueron al servidor por la misma razón, en tres tandas: la
  // compañía (filtro del catálogo), el estado y la familia, y ahora la búsqueda libre y el
  // marcado prioritario. Un filtro aplicado sobre las filas ya traídas no responde «los borradores
  // del tenant» sino «los borradores que cupieron», que es una respuesta distinta y silenciosamente
  // incompleta — y con paginación real habría empeorado: la ventana pasa de 200 filas a las diez de
  // la página a la vista.
  //
  // La consecuencia para la exportación es que ya no tiene nada que reaplicar: lo que el servidor
  // devuelve ES lo que la pantalla muestra.

  /**
   * HU #12104 — descarga a Excel de TODO lo que cumple los filtros, no de la página a la vista.
   *
   * <p>Exportar solo las diez filas de pantalla sería una trampa: el archivo parecería completo y
   * nadie lo comprobaría. Y exportar lo que la tabla tiene en memoria no serviría tampoco — el
   * listado trae como mucho {@link SERVER_LIST_TAKE} filas, así que el archivo diría «los borradores
   * que cupieron en la ventana» en vez de «los borradores del tenant».</p>
   *
   * <p>Por eso recorre el servidor de {@link SERVER_LIST_TAKE} en {@link SERVER_LIST_TAKE} —el tope
   * DURO del endpoint, pedir más no trae más— hasta agotar el total, y reparte en archivos de
   * {@link EXPORT_BATCH_SIZE} filas en vez de truncar. Cada archivo se dispara apenas se arma,
   * dentro de la misma interacción del clic, para no depender de que el usuario pida «el resto».</p>
   *
   * <p>Se manda SIEMPRE `take`/`skip` aunque no haya ningún filtro: sin parámetros el endpoint cae
   * en su ruta histórica, que devuelve el top-N sin paginar y sin `total` — el recorrido no tendría
   * dónde parar.</p>
   */
  const handleExportExcel = useCallback(async () => {
    setExporting(true);
    setExportNotice(null);
    setExportError(null);
    try {
      const base = buildListQuery();
      const campos = tramitesExportFields(effectiveColumns);
      const columnasExcel: DataColumn<InstanceSummary>[] = campos.map((campo) => ({
        id: campo.id,
        label: campo.label,
        group: 'Trámites',
        value: campo.value,
        raw: campo.raw,
        width: campo.width,
      }));
      const idsVisibles = campos.map((c) => c.id);
      const sello = selloDeArchivo(new Date());

      // La primera página se pide aparte porque de ella sale el `total` con el que se sabe cuántas
      // quedan. Se guarda para reusarla como página 1 del recorrido en vez de volver a pedirla.
      const primeraPagina = await tramitesClient.searchInstances({
        ...base,
        skip: 0,
        take: SERVER_LIST_TAKE,
      });

      // Desde la HU #12188 no queda ningún filtro en el cliente: el `total` del servidor ES el
      // universo exportado, y no hay nada que reaplicar página a página. El aviso sigue contando
      // filas REALES —es lo honesto— pero ya no puede diferir del total por filtros escondidos.
      const { exportadas, archivos } = await exportarPorLotes<InstanceSummary>({
        total: primeraPagina.total,
        pageSize: SERVER_LIST_TAKE,
        traerPagina: async (pagina, pageSize) => {
          const filas =
            pagina === 1
              ? primeraPagina.items
              : (
                  await tramitesClient.searchInstances({
                    ...base,
                    skip: (pagina - 1) * pageSize,
                    take: pageSize,
                  })
                ).items;
          return filas;
        },
        volcar: (lote, parte) => {
          download(
            buildWorkbook('Trámites', columnasExcel, lote, idsVisibles),
            nombreArchivoTramites(sello, parte),
            XLSX_MIME,
          );
        },
      });

      if (exportadas === 0) {
        setExportNotice('Ningún trámite cumple los filtros activos: no se descargó ningún archivo.');
        return;
      }
      setExportNotice(
        archivos > 1
          ? `Se exportaron ${exportadas} trámites en ${archivos} archivos de hasta ${EXPORT_BATCH_SIZE} filas cada uno, con ${idsVisibles.length} columnas.`
          : `Se exportaron ${exportadas} trámites con ${idsVisibles.length} columnas.`,
      );
    } catch (err) {
      setExportError(
        err instanceof Error ? `No se pudo exportar: ${err.message}` : 'No se pudo exportar.',
      );
    } finally {
      setExporting(false);
    }
  }, [buildListQuery, effectiveColumns]);


  // HU #10536 — sin orden explicito por columna, el backend devuelve los prioritarios primero. Al
  // marcar uno desde la tabla se replica ESE mismo criterio en cliente, para que suba a la primera
  // fila en el acto en vez de quedarse en su sitio hasta el siguiente refetch o hasta recargar la
  // pagina. `sort` es estable, asi que dentro de cada grupo se conserva el orden que vino del
  // backend. Con un orden explicito por cabecera (`sortBy`) NO se reordena: ahi manda lo que pidio
  // el usuario, y colar los prioritarios arriba contradiria la columna que acaba de elegir.
  const ordenados = useMemo(() => {
    if (sortBy) return items;
    return [...items].sort(
      (a, b) => Number(b.prioritario ?? false) - Number(a.prioritario ?? false),
    );
  }, [items, sortBy]);

  // El total lo cuenta el SERVIDOR sobre el universo filtrado: es lo que hace que la cuenta de la
  // paginación y las tarjetas de estado digan lo mismo que la tabla puede alcanzar.
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const safePage = Math.min(page, totalPages);
  // Lo que se pinta es la página que trajo el servidor, tal cual: ya viene acotada y del tamaño
  // pedido. Recortarla aquí otra vez solo podría esconder filas que el servidor sí contó.
  const paginated = ordenados;

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
  const handlePrioritariosChange = (v: boolean) => {
    setSoloPrioritarios(v);
    setPage(1);
  };

  /**
   * HU #12188 — cambiar el tamaño de página vuelve al principio: con otro tamaño, «la página 5» no
   * describe el mismo tramo, así que quedarse en ella llevaría a un sitio que el gestor no eligió.
   */
  const handlePageSizeChange = (v: number) => {
    setPageSizeElegido(v);
    setPage(1);
    try {
      sessionStorage.setItem(CLAVE_PAGE_SIZE, String(v));
    } catch {
      // Ventana privada o almacenamiento bloqueado: el tamaño vale para esta pantalla y no se
      // recuerda. Es una comodidad, no un dato: no merece un aviso.
    }
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
    appliedCondiciones.length > 0 ||
    appliedCreatedFrom.trim() !== '' ||
    appliedCreatedTo.trim() !== '' ||
    appliedUpdatedFrom.trim() !== '' ||
    appliedUpdatedTo.trim() !== '';

  const hasActiveFilters =
    search.trim() !== '' ||
    modalidad !== '' ||
    estado !== '' ||
    soloPrioritarios ||
    hasServerFilters ||
    sortBy !== '';

  /**
   * Lo que el usuario ya tocó en la tarjeta de filtros pero TODAVÍA no aplicó: filtros específicos
   * añadidos, un periodo elegido o fechas propias escritas. "Empezar de cero" tiene que poder
   * borrarlo también — si solo mirara lo aplicado, quedaban chips a la vista con el botón apagado.
   */
  const hasDraftFilters =
    draftCondiciones.length > 0 ||
    periodo !== 'Sin periodo' ||
    rangoPropioDesde !== '' ||
    rangoPropioHasta !== '';

  const applyServerFilters = () => {
    setAppliedCondiciones(draftCondiciones);

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

  /**
   * Quitar un chip retira la condición del borrador Y de lo aplicado, y recarga.
   *
   * <p>El chip habla de lo que está filtrando AHORA: quitarlo solo del borrador dejaría la tabla
   * igual y el chip desaparecido, que se lee como que el filtro no hacía nada.</p>
   */
  const handleQuitarCondicion = (fieldId: string) => {
    setDraftCondiciones((prev) => prev.filter((c) => c.fieldId !== fieldId));
    setAppliedCondiciones((prev) => prev.filter((c) => c.fieldId !== fieldId));
    setPage(1);
  };

  const clearFilters = () => {
    setSearch('');
    setModalidad('');
    setEstado('');
    setSoloPrioritarios(false);
    setDraftCondiciones([]);
    setRangoSobre('created');
    setPeriodo('Sin periodo');
    setRangoPropioDesde('');
    setRangoPropioHasta('');
    setAppliedCondiciones([]);
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

  /** Mockup chrome: Actualizar limpia búsqueda y filtro de estado, luego recarga. */
  const handleRefresh = () => {
    setSearch('');
    setEstado('');
    setPage(1);
    void load();
  };

  return (
    // Sin tarjeta blanca envolvente: en el diseño la pantalla es una pila de bloques sobre el
    // fondo azul claro (título en tarjeta, KPIs en tarjeta, tabs desnudos, filas como tarjetas).
    // Meter todo dentro de un contenedor blanco aplanaba esa jerarquía.
    <section className="flex min-w-0 flex-col gap-4">
      <ModuleTitle
        title="Gestión Integral de trámites"
        subtitle="Administra, monitorea y radica tus trámites ante organismos de tránsito en tiempo real."
      />

      <div className="flex min-w-0 flex-col gap-4">
        {/* flit-tramites-chrome: tabs + filtros ANTES de KPIs */}
        <TramitesListToolbar
          modalidad={modalidad}
          onModalidadChange={handleModalidadChange}
          onRefresh={handleRefresh}
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
              condicionesCount={appliedCondiciones.length}
              queryFields={queryFields}
              draftCondiciones={draftCondiciones}
              onDraftCondicionesChange={setDraftCondiciones}
              filtrosTestIdPrefix="tramites"
              fieldsError={
                fieldsError ? (
                  // Un catálogo que no carga NO deja la pantalla inservible: la tabla ya se pintó
                  // con su listado y aquí solo se dice qué falta y cómo reintentarlo.
                  <div className="px-1 py-2 text-xs text-[#162744]/70 dark:text-white/60">
                    <p>No se pudieron cargar los filtros.</p>
                    <button
                      type="button"
                      onClick={() => setFieldsKey((k) => k + 1)}
                      className="mt-1 font-semibold text-[#557EFF] hover:underline"
                      data-testid="tramites-filtros-reintentar"
                    >
                      Reintentar
                    </button>
                  </div>
                ) : undefined
              }
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
                  buttonClassName={controlCls(columnasPersonalizadas)}
                />
              }
              exportAction={
                <button
                  type="button"
                  onClick={() => void handleExportExcel()}
                  disabled={exporting || loading}
                  aria-label="Exportar el listado de trámites a Excel"
                  title="Exportar a Excel"
                  className={controlCls(false)}
                  data-testid="tramites-export-xlsx"
                >
                  <Download className={`h-3.5 w-3.5 ${exporting ? 'animate-pulse' : ''}`} aria-hidden="true" />
                  {exporting ? 'Exportando…' : 'Exportar'}
                </button>
              }
            />
          }
        />

        {/* HU #12104 — resultado de la exportación. El reparto en varios archivos necesita decirse:
            tres descargas seguidas sin explicación se leen como un fallo, no como un archivo por
            lote. `role="status"` para que un lector de pantalla lo anuncie sin robar el foco. */}
        {exportError ? (
          <InlineAlert tone="warning" title="No se pudo exportar">
            {exportError}
          </InlineAlert>
        ) : null}
        {exportNotice ? (
          <p
            role="status"
            className="text-xs text-[#162744]/70 dark:text-white/60"
            data-testid="tramites-export-aviso"
          >
            {exportNotice}
          </p>
        ) : null}

        {/* KPIs por estado (filtro) + CTA Nuevo trámite */}
        <div className="flex items-stretch gap-4">
          {!loading && !error ? (
            <div className="min-w-0 flex-1">
              <EstadoFunnel
                counts={estadoCountsMostrados}
                selected={estado}
                onSelect={handleEstadoChange}
              />
            </div>
          ) : null}
          <button
            type="button"
            onClick={() => onNewTramite?.()}
            disabled={blockNew.matricula && blockNew.traspaso}
            title={
              blockNew.matricula && blockNew.traspaso
                ? 'La compañía tiene bloqueada la creación de trámites.'
                : undefined
            }
            className="flex min-h-[88px] w-28 shrink-0 flex-col items-center justify-center rounded-2xl text-sm font-semibold leading-tight text-white transition hover:opacity-90 active:scale-95 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-45"
            style={{ background: WIZARD_CTA_GRADIENT }}
          >
            <span>
              Nuevo
              <br />
              trámite
            </span>
          </button>
        </div>

        {/* Tira de chips: SOLO existe si hay periodo o alguna condición aplicada */}
        <TramitesFiltrosChips
          periodo={periodo}
          onQuitarPeriodo={handleQuitarPeriodo}
          condiciones={appliedCondiciones}
          fields={queryFields}
          onQuitarCondicion={handleQuitarCondicion}
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
          paginated={paginated}
          total={total}
          pageSize={pageSize}
          onPageSizeChange={handlePageSizeChange}
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
          onOpen={abrirAsistente}
          onVerDocumentos={setDocsTramite}
          onVerConsolidado={setConsolidadoTramite}
          onOpenDetalle={setDetalleTramite}
          onOpenTrackingTramite={setTrackingTramite}
          onOpenIdentidadTracking={setIdentidadTracking}
          puedeVerHistorialPlaca={puedeVerHistorialPlaca}
          onVerHistorialPlaca={verHistorialPlaca}
          isAdmin={isAdmin}
          onAdminActionSuccess={() => void load()}
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
        // Subsanar desde el detalle: el modal enciende el flag y delega el salto al asistente aquí,
        // que es donde vive la ruta con el `?t=` del SuperAdmin.
        onAbrirAsistente={(it) => {
          setDetalleTramite(null);
          abrirAsistente(it.id, it.tenantId);
        }}
      />

      {/* HU #12185 — la fila entera, no solo su id: la ficha del panel se arma con campos que ya
          viajan en el listado, así que abrirlo no cuesta ninguna consulta más que el historial. */}
      <TramiteTrackingModal
        open={trackingTramite !== null}
        onClose={() => setTrackingTramite(null)}
        item={trackingTramite}
        tenantId={isAdmin ? trackingTramite?.tenantId : undefined}
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
 * Cabecera ordenable (HU #12108).
 *
 * <p>Tres columnas del listado son COMPUESTAS: «Radicado» apila las dos fechas, «Vehículo» la placa
 * y el VIN, y «Trámite» el tipo y el estado. Un clic en la cabecera no puede expresar por cuál de
 * ellos se ordena, así que la cabecera abre un desplegable con sus datos y los dos sentidos.</p>
 *
 * <p>TODAS las columnas ordenables usan el mismo desplegable, también las de un solo dato. La
 * alternativa —clic que alterna cuando hay uno, menú cuando hay varios— hacía que dos cabeceras
 * vecinas idénticas respondieran distinto al mismo gesto, sin nada que lo anunciara.</p>
 *
 * <p>Cada dato es UNA fila con su nombre y un par de botones de sentido, en vez de N×2 filas
 * planas: con seis entradas seguidas que solo se diferencian por una flecha no se ve cuántos datos
 * hay realmente. El sentido se rotula según el tipo —una fecha ascendente es «Más antigua», no
 * «A-Z»—, porque si no hay que adivinar qué hace la flecha.</p>
 *
 * <p>Lo que se ofrece sale de `tramitesSortOptions`, la misma lista de subcampos que usa el Excel.
 * Si el gestor enciende la columna de desglose de un dato, éste deja de ofrecerse desde la celda
 * compuesta: si no, habría dos cabeceras distintas ordenando por lo mismo.</p>
 */
const SENTIDO_ETIQUETA: Record<'texto' | 'fecha' | 'numero', { asc: string; desc: string }> = {
  texto: { asc: 'A-Z', desc: 'Z-A' },
  fecha: { asc: 'Más antigua', desc: 'Más reciente' },
  // HU #12154 — el radicado dejó de ser TRM-2026-000123 para ser un número. «A-Z» sobre un número
  // no dice nada: el usuario no sabe si el 10 va antes o después del 9.
  numero: { asc: 'Menor a mayor', desc: 'Mayor a menor' },
};

function SortableHeaderCell({
  column,
  visibleColumns,
  sortBy,
  sortDir,
  onSortChange,
}: {
  column: TramitesColumnDef;
  visibleColumns: readonly string[];
  sortBy: string;
  sortDir: 'asc' | 'desc';
  onSortChange: (sortBy: string, sortDir: 'asc' | 'desc') => void;
}) {
  const opciones = tramitesSortOptions(column.key, visibleColumns);
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);

  // Cierra al pulsar fuera o con Escape, y devuelve el foco al disparador: sin esto, quien navega
  // con teclado se queda dentro de un menú cerrado.
  useEffect(() => {
    if (!open) return undefined;
    const onDocDown = (e: MouseEvent) => {
      const target = e.target as Node;
      if (!panelRef.current?.contains(target) && !triggerRef.current?.contains(target)) {
        setOpen(false);
      }
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setOpen(false);
        triggerRef.current?.focus();
      }
    };
    document.addEventListener('mousedown', onDocDown);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDocDown);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  if (opciones.length === 0) {
    return <>{column.label}</>;
  }

  const activa = opciones.find((o) => o.sort === sortBy);
  const Icon = !activa ? ArrowUpDown : sortDir === 'asc' ? ArrowUp : ArrowDown;

  return (
    <div className="relative inline-block">
      <button
        ref={triggerRef}
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-haspopup="menu"
        aria-expanded={open}
        // El nombre accesible dice por qué está ordenando AHORA, no solo que se puede ordenar: es
        // la única forma de saberlo sin ver el icono.
        aria-label={
          activa
            ? `Ordenar ${column.label}. Ahora: ${activa.label} ${sortDir === 'asc' ? 'ascendente' : 'descendente'}`
            : `Ordenar ${column.label}`
        }
        className={`inline-flex items-center gap-1.5 rounded-md px-1.5 py-1 uppercase transition-colors hover:bg-[#EEF5FF] dark:hover:bg-white/5 ${
          activa ? 'text-[#2C6BED] dark:text-[#7FB0FF]' : ''
        }`}
      >
        {column.label}
        <Icon className={`h-3 w-3 ${activa ? 'opacity-100' : 'opacity-45'}`} aria-hidden="true" />
      </button>

      {open ? (
        <div
          ref={panelRef}
          role="menu"
          aria-label={`Ordenar por, en ${column.label}`}
          className="absolute left-0 top-full z-50 mt-1.5 w-[18rem] overflow-hidden rounded-xl border border-[#DFE5ED] bg-white normal-case shadow-xl dark:border-white/10 dark:bg-[#162744]"
        >
          <p className="border-b border-[#EEF2F7] px-3 py-2 text-[11px] font-semibold uppercase tracking-wide text-[#7B8794] dark:border-white/10 dark:text-white/50">
            Ordenar por
          </p>
          <div className="p-1.5">
            {opciones.map((opcion) => (
              <div
                key={opcion.id}
                className="rounded-lg px-2 py-1.5 [&+&]:mt-0.5 [&+&]:border-t [&+&]:border-[#F1F5F9] [&+&]:pt-2 dark:[&+&]:border-white/5"
              >
                <p className="mb-1.5 text-xs font-medium text-[#162744] dark:text-white">
                  {opcion.label}
                </p>
                <span className="flex items-center gap-1.5">
                  {(['asc', 'desc'] as const).map((dir) => {
                    const seleccionada = sortBy === opcion.sort && sortDir === dir;
                    const Flecha = dir === 'asc' ? ArrowUp : ArrowDown;
                    return (
                      <button
                        key={dir}
                        type="button"
                        role="menuitemradio"
                        aria-checked={seleccionada}
                        aria-label={`${opcion.label}: ${SENTIDO_ETIQUETA[opcion.kind][dir]}`}
                        onClick={() => {
                          onSortChange(opcion.sort, dir);
                          setOpen(false);
                        }}
                        className={`inline-flex flex-1 items-center justify-center gap-1 rounded-md border px-2 py-1 text-[11px] font-medium transition-colors ${
                          seleccionada
                            ? 'border-[#2C6BED] bg-[#2C6BED] text-white'
                            : 'border-[#DFE5ED] text-[#5A6B7F] hover:bg-[#EEF5FF] dark:border-white/15 dark:text-white/70 dark:hover:bg-white/10'
                        }`}
                      >
                        <Flecha className="h-3 w-3" aria-hidden="true" />
                        {SENTIDO_ETIQUETA[opcion.kind][dir]}
                      </button>
                    );
                  })}
                </span>
              </div>
            ))}
          </div>
        </div>
      ) : null}
    </div>
  );
}

/** Cuerpo de la tabla: maneja los 4 estados (cargando/error/vacío/datos). */
function TableBody({
  loading,
  error,
  paginated,
  total,
  pageSize,
  onPageSizeChange,
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
  puedeVerHistorialPlaca,
  onVerHistorialPlaca,
  isAdmin,
  onAdminActionSuccess,
}: {
  loading: boolean;
  error: string | null;
  paginated: InstanceSummary[];
  /** Total del universo filtrado, contado por el SERVIDOR (no `paginated.length`). */
  total: number;
  pageSize: number;
  onPageSizeChange: (pageSize: number) => void;
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
  /** HU #12196 — el usuario tiene acceso al módulo de historial por placa (gate RBAC del dock). */
  puedeVerHistorialPlaca: boolean;
  /** HU #12196 — abre el historial de esa placa. Recibe la placa, no la fila: el historial NO se
   *  acota al trámite ni a su compañía. */
  onVerHistorialPlaca: (placa: string) => void;
  /** HU #12163 — SuperAdmin viendo trámites de otra compañía (X-Tenant-Id de la fila en las
   *  acciones administrativas avanzadas). */
  isAdmin: boolean;
  /** HU #12163 — refresca la tabla tras una acción administrativa avanzada exitosa. */
  onAdminActionSuccess: () => void;
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

  // HU #12188 — los dos vacíos se distinguen por si hay criterios activos, NO por el largo de la
  // página. Antes `items` era la ventana entera sin filtrar, así que un resultado vacío llegaba con
  // filas en memoria y bastaba mirarlas; ahora `items` ES la página, y una página vacía con un
  // filtro puesto se leía como «aún no hay trámites» — que suena a cuenta recién creada y no a un
  // filtro demasiado estrecho.
  if (total === 0 && !hasActiveFilters) {
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

  // Vacío con filtros: hay trámites, pero ninguno coincide.
  if (total === 0) {
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
            // `width: 100%` + `minWidth`: la tabla ocupa SIEMPRE el ancho del contenedor y los
            // porcentajes del `<colgroup>` reparten ese ancho entre las columnas visibles. Sin el
            // 100% la tabla era shrink-to-fit y se encogía cada vez que se ocultaba una columna,
            // dejando un hueco a la derecha. El mínimo sigue mandando cuando las columnas
            // visibles no caben: ahí entra el scroll horizontal del contenedor, no celdas
            // ilegibles.
            width: '100%',
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
                  className={`${TABLA_HEADER_CELL_CLS} rounded-l-xl`}
                  style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
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
                  className={`${TABLA_HEADER_CELL_CLS} ${
                    !gridLayout.includeSelectColumn && index === 0 ? 'rounded-l-xl' : ''
                  }`}
                  style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                >
                  <SortableHeaderCell
                    column={col}
                    visibleColumns={visibleColumns}
                    sortBy={sortBy}
                    sortDir={sortDir}
                    onSortChange={onSortChange}
                  />
                </th>
              ))}
              <th
                scope="col"
                className={`${TABLA_HEADER_CELL_CLS} rounded-r-xl text-right`}
                style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
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
                puedeVerHistorialPlaca={puedeVerHistorialPlaca}
                onVerHistorialPlaca={onVerHistorialPlaca}
                isAdmin={isAdmin}
                onAdminActionSuccess={onAdminActionSuccess}
              />
            ))}
          </tbody>
        </table>
      </div>

      {/* Fuera del contenedor con scroll horizontal: la paginación no se desplaza con la tabla. */}
      <div className="flex flex-wrap items-center justify-between gap-4">
        <label className="flex items-center gap-2 pt-3 text-xs opacity-70">
          Filas por página
          <select
            value={pageSize}
            onChange={(e) => onPageSizeChange(Number(e.target.value))}
            className={controlCls(false)}
            aria-label="Filas por página"
          >
            {TAMANOS_DE_PAGINA.map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
        </label>
        {/* «Mostrando 26–50 de 300»: con paginación de servidor el rango dice DÓNDE está el gestor
            dentro del universo, cosa que «25 de 300» no distingue de la primera página. */}
        <PageNav
          page={page}
          totalPages={totalPages}
          resumen={
            total === 0
              ? 'Sin trámites que mostrar'
              : `Mostrando ${(page - 1) * pageSize + 1}–${(page - 1) * pageSize + paginated.length} de ${total}`
          }
          ariaLabel="Paginación de trámites"
          onPageChange={onPageChange}
          className="flex-1"
        />
      </div>
    </div>
  );
}

/**
 * Celda de actor: nombre + la acreditación DE ESA PARTE (identidad validada o firma del baúl).
 *
 * Las dos cosas viven juntas porque la acreditación es por parte, no del trámite: teniéndolas en
 * columnas separadas el gestor leía "Firmado" y tenía que cruzar la vista a otra columna para
 * saber de quién. Unidas, la cabecera de la columna ya dice de quién es —"Vendedor",
 * "Comprador"—, así que la línea de firma no necesita repetir el rótulo.
 *
 * El nombre envuelve en vez de truncar, por el mismo motivo que la secretaría: "WILLYN SMITH
 * LONDOÑ…" no identifica a nadie, y el gestor tenía que pasar el puntero por encima para leer con
 * quién está tratando. Un nombre tiene espacios donde partirse, así que la segunda línea sale
 * natural; la fila crece lo que haga falta, como ya hace con el organismo de tránsito.
 */
function ActorCell({
  nombre,
  rotulo,
  estado,
  onOpenTracking,
}: {
  nombre: string | null | undefined;
  /** Parte a la que pertenece la celda; solo se usa para nombrar la acción de trazabilidad. */
  rotulo: string;
  /** `null` = la parte YA está en la fila pero aún no tiene acreditación; se dice, no se calla. */
  estado?: FirmaParteEstado | null;
  /** Click en la acreditación → modal de tracking de identidad de esta parte. */
  onOpenTracking?: () => void;
}) {
  const texto = nombre?.trim();
  // Sin actor en la celda no hay firma que reportar: "Sin registrar" debajo de un guion se lee
  // como una acreditación pendiente de alguien que ni siquiera está capturado todavía. Cubre los
  // dos casos de una: la parte que NO EXISTE en ese tipo de trámite (el vendedor en matrícula
  // inicial, en los tipos de OTROS) y la que todavía no se ha capturado en un borrador.
  const mostrarFirma = !!texto;
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
    <span className="flex min-w-0 flex-col items-start gap-0.5">
      <span
        className="block w-full break-words leading-snug text-[#162744] dark:text-white/90"
        title={texto || undefined}
      >
        {texto || '—'}
      </span>
      {mostrarFirma ? (
        onOpenTracking ? (
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
        )
      ) : null}
    </span>
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
  puedeVerHistorialPlaca,
  onVerHistorialPlaca,
  isAdmin,
  onAdminActionSuccess,
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
  /** HU #12196 — el usuario tiene acceso al módulo de historial por placa (gate RBAC del dock). */
  puedeVerHistorialPlaca: boolean;
  /** HU #12196 — abre el historial de esa placa. Recibe la placa, no la fila: el historial NO se
   *  acota al trámite ni a su compañía. */
  onVerHistorialPlaca: (placa: string) => void;
  /** HU #12163 — ver doc de `TableBody`. */
  isAdmin: boolean;
  onAdminActionSuccess: () => void;
}) {
  // HU #11055 — la acción del consolidado solo existe si el expediente ya está generado (el resumen
  // trae el id del adjunto): el botón NUNCA dispara una generación.
  const consolidadoDisponible = !!item.consolidadoAttachmentId;
  // HU #12196 — placa de la fila, normalizada. Vacía en borradores que aún no la tienen asignada.
  const placaDeLaFila = (item.placa ?? '').trim().toUpperCase();
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
  // Abre el ASISTENTE (editable) en vez del detalle de solo lectura. Además del borrador, cubre la
  // subsanación: el estado sigue siendo `rechazado`, pero el trámite se está corrigiendo, y tanto
  // "Re-radicar" como "Cancelar la subsanación" solo existen dentro del asistente.
  const abreAsistente =
    isDraft || !!item.subsanacionActiva || item.estado === 'subsanacion';
  const actionLabel = async?.ready ? 'Radicar' : abreAsistente ? 'Continuar' : 'Ver';
  const actionIcon = async?.ready ? FileCheck : abreAsistente ? Play : Eye;
  const plateHint = plateFlowHint(item.plateFlowStatus);
  const puedeProcesar =
    item.estado === 'entregado' && item.plateFlowStatus === 'asignado';
  // HU #12163 — acciones avanzadas del administrador (Cambiar estado, Anular, Consolidado,
  // Reenviar validación, Reasignar gestor), gateadas por permiso (AC1) y por estado (AC2).
  const { items: adminActionItems, modals: adminActionModals } = useAdminTramiteAcciones({
    item,
    isAdmin,
    onChanged: onAdminActionSuccess,
  });
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
    // HU #12196 — atajo al historial de ESTA placa. Se OMITE por completo si el usuario no tiene
    // el módulo (`historial-placa` fuera de sus módulos navegables): enseñar un destino al que el
    // gate va a rebotar no informa de nada, solo promete acceso.
    //
    // Con permiso pero sin placa (un borrador puede no tenerla todavía) sí aparece, DESHABILITADA
    // y con motivo: aquí el atajo no está prohibido, es que ese trámite aún no tiene por dónde
    // consultarlo. Ocultarlo dejaría al gestor buscando una acción que en otras filas sí está;
    // navegar con la placa vacía abriría el módulo a preguntar por nada.
    ...(puedeVerHistorialPlaca
      ? [
          {
            key: 'historial-placa',
            label: 'Ver historial de la placa',
            icon: History,
            disabled: !placaDeLaFila,
            disabledReason: 'Este trámite todavía no tiene placa asignada.',
            onSelect: () => onVerHistorialPlaca(placaDeLaFila),
          },
        ]
      : []),
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
    // HU #12163 — gestión avanzada del administrador, anexada al final del menú de la fila.
    ...adminActionItems,
  ];
  // Bug #12376, defecto 1 — en Anulado el motivo del último rechazo ya NO es vigente: se anuló el
  // trámite, no se resolvió el rechazo. El historial general (línea de tiempo) sí lo conserva; solo
  // se oculta aquí, donde se pintaba como si siguiera activo.
  const motivoRechazo =
    item.estado === 'anulado' ? null : item.ultimoRechazoMotivo?.trim() || null;
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
  //
  // La subsanación entra por `abreAsistente`: mandarla al detalle dejaba al gestor sin forma de
  // terminar ni de cancelar lo que él mismo activó.
  const handleOpen = () => {
    if (!abreAsistente) {
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
          <span className="block truncate text-[10px] opacity-55">
            Creación: {shortDate(item.createdAt)}
          </span>
        ) : null}
        {!shows('fechaActualizacion') && item.updatedAt ? (
          <span className="block truncate text-[10px] opacity-55">
            Actualización: {shortDate(item.updatedAt)}
          </span>
        ) : null}
      </span>
    ),
    // Vehículo: placa, VIN y marca/modelo apilados. Los tres identifican el MISMO objeto, así que
    // se leen juntos; en columnas separadas había que barrer la fila a lo ancho para reconocerlo.
    // Los dos apilados son CONDICIONALES: si el gestor enciende su columna de desglose, el dato se
    // muda allí en vez de salir dos veces.
    placa: (
      <span className="flex min-w-0 flex-col gap-0.5">
        <span className="block truncate font-mono font-semibold tracking-wider text-[#162744] dark:text-white">
          {item.placa ?? '—'}
        </span>
        {/* El VIN no envuelve: es un token de 17 caracteres sin espacios y partirlo en dos líneas
            no ayuda a compararlo de un vistazo. El piso de la columna cabe los 17 enteros. */}
        {!shows('vin') ? (
          <span className="block truncate font-mono text-xs text-[#162744]/80 dark:text-white/70">
            {item.vin ?? '—'}
          </span>
        ) : null}
        {/* La marca sí envuelve: "SUZUKI GIXXER 250" cortado a "SUZUKI GIXX…" deja de decir qué
            vehículo es. */}
        {!shows('vehiculo') ? (
          <span
            className="block break-words leading-snug text-[10px] opacity-55"
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
    // El chip de estado se inyecta más abajo (solo si la columna `estado` está oculta), para
    // reutilizar EXACTAMENTE la misma celda —popover de rechazo incluido— en vez de duplicarla.
    tramite: (
      <span className="flex min-w-0 flex-col items-start gap-1">
        <span
          className="block break-words leading-snug text-xs font-semibold text-[#162744] dark:text-white"
          // Los nombres de OTROS son largos («Levantamiento de prenda») y la celda es angosta, así
          // que envuelven en vez de cortarse: es el rótulo que distingue un trámite de otro. El
          // `title` se conserva para el caso extremo de una palabra sola más ancha que la celda.
          title={tramiteLabel(item)}
        >
          {tramiteLabel(item)}
        </span>
        {!shows('paso') ? (
          <span className="flex min-w-0 items-start gap-1 text-[10px] opacity-55">
            <span className="shrink-0 font-mono tabular-nums">
              {item.pasoActual}/{item.totalPasos}
            </span>
            <span className="break-words leading-snug">{stepLabel(item)}</span>
          </span>
        ) : null}
      </span>
    ),
    propietario: (
      <ActorCell
        nombre={item.vendedorNombre}
        rotulo="Vendedor"
        estado={item.firmaVendedorEstado}
        onOpenTracking={() =>
          onOpenIdentidadTracking({ item, parte: 'vendedor', rotulo: 'Vendedor' })
        }
      />
    ),
    comprador: (
      <ActorCell
        nombre={item.compradorNombre}
        rotulo="Comprador"
        estado={item.firmaCompradorEstado}
        onOpenTracking={() =>
          onOpenIdentidadTracking({ item, parte: 'comprador', rotulo: 'Comprador' })
        }
      />
    ),
    vin: (
      <span className="block truncate font-mono text-xs text-[#162744]/80 dark:text-white/70">
        {item.vin ?? '—'}
      </span>
    ),
    vehiculo: (
      <span
        className="block break-words leading-snug text-[#162744]/90 dark:text-white/80"
        title={vehiculo(item)}
      >
        {vehiculo(item)}
      </span>
    ),
    paso: (
      <span className="block min-w-0">
        <span className="block font-mono text-xs text-[#162744]/70 dark:text-white/60">
          {item.pasoActual}/{item.totalPasos}
        </span>
        <span className="block truncate text-[10px] opacity-55">
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
    // Envuelven las dos líneas: una razón social cortada a "Empresa Demo S.A…" y un operador a
    // "Radicador Empresa De…" no distinguen a una compañía o a una persona de otra, que es
    // justo para lo que está la columna. Mismo criterio que la secretaría y los actores.
    gestor: (
      <span className="block min-w-0">
        <span
          className="block break-words leading-snug text-xs font-semibold text-[#162744] dark:text-white"
          title={item.companiaNombre ?? undefined}
        >
          {item.companiaNombre ?? '—'}
        </span>
        <span
          className="block break-words leading-snug text-[10px] opacity-55"
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
    // HU #12183 — marcas de prenda y transformación. INFORMATIVAS: no son botones, no filtran y no
    // ordenan (la cabecera de esta columna tampoco lleva orden, ver `sort` en la definición).
    //
    // El guion no es decoración: una celda vacía se lee como un dato que falta —o como una fila que
    // no cargó bien— y no como «este trámite no tiene ninguna de las dos».
    //
    // Cada ícono lleva su rótulo en `alt` y en `title`: el color es lo único que los distingue a
    // simple vista, y el color no puede ser el único portador del significado.
    marcas: (
      <span className="flex items-center gap-1.5">
        {marcasDe(item).length === 0 ? (
          <span className="text-xs text-[#162744]/45 dark:text-white/40">—</span>
        ) : (
          marcasDe(item).map((marca) => (
            /* eslint-disable-next-line @next/next/no-img-element */
            <img
              key={marca.id}
              src={marca.src}
              alt={marca.label}
              title={marca.label}
              width={26}
              height={26}
              className="h-[26px] w-[26px] shrink-0"
            />
          ))
        )}
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
    <tr
      onClick={handleOpen}
      className={`group cursor-pointer bg-white text-xs dark:bg-[#162744] ${TABLA_ROW_HOVER_CLS}`}
    >
      {/* ICT — checkbox de selección solo si la tabla reservó la pista (hay borradores ICT). Como
          <tr> no admite border-radius, el borde/radio de "tarjeta" vive en cada <td>. */}
      {includeSelectColumn ? (
        <td
          className="rounded-l-xl border-y border-l border-[#DFE5ED] px-4 py-3 align-middle dark:border-white/10"
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
          className={`border-y border-[#DFE5ED] px-4 py-3 align-middle text-xs dark:border-white/10 ${
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
        className="rounded-r-xl border-y border-r border-[#DFE5ED] px-4 py-3 text-right align-middle dark:border-white/10"
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

      {/* HU #12163 — modales de las acciones administrativas avanzadas (Cambiar estado, Anular,
          Consolidado, Reenviar validación, Reasignar gestor). Igual que `Modal` arriba, se portan a
          `document.body` (createPortal): no rompen la validez de la tabla. */}
      {adminActionModals}
    </tr>
  );
}
