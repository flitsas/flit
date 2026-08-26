'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import {
  AlertCircle,
  AlertTriangle,
  Bell,
  CheckCircle2,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  Clock,
  Copy,
  ExternalLink,
  ListTree,
  Pencil,
  RotateCcw,
  ScanFace,
  Send,
  ShieldCheck,
  X,
  XCircle,
} from 'lucide-react';
import { ActionsMenu, type ActionsMenuItem } from '@/components/atom/ActionsMenu';
import { ModuleTitle } from './ModuleTitle';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import {
  ValidacionesFilterToolbar,
  EMPTY_VALIDACIONES_FILTERS,
  hasActiveValidacionesFilters,
  splitPersonaODocumentoQuery,
  type ValidacionesUiFilters,
} from './ValidacionesFilterToolbar';
import { PersonIdentityDetailDrawer } from './PersonIdentityDetailDrawer';
import {
  PrevalidacionForm,
  PrevalidacionSuccessPanel,
  type PrevalidacionReuseInfo,
} from './PrevalidacionForm';
import {
  parseRateLimitDetail,
  PrevalidacionEditForm,
  PrevalidacionResendResultPanel,
  type RateLimitInfo,
} from './PrevalidacionEditForm';
import {
  setActiveTramitesTenant,
  tramitesClient,
  TramitesApiError,
} from '@/lib/api/tramites-client';
import { superadminClient, type CompanyItem } from '@/lib/api/superadmin-client';
import { getToken } from '@/lib/api/client';
import { decodeJwtPayload, isSuperAdmin } from '@/lib/auth/jwt';
import type {
  BiometricEstado,
  BiometricParte,
  BiometricValidationStats,
  EditarPrevalidacionResult,
  IniciarPrevalidacionResult,
  StuckIdentityValidation,
  StuckIdentityValidationsResponse,
  TenantBiometricPerson,
  TenantBiometricPersonFilters,
  TenantBiometricValidation,
} from '@/lib/api/types/procedure-runtime';
import { familiaLabel } from '@/lib/api/types/familia-labels';

/**
 * Módulo ÚNICO de Identidad: validaciones y prevalidaciones viven aquí (antes había una pantalla
 * aparte, /tramites/prevalidaciones, hoy retirada). Vista transversal del tenant AGRUPADA POR PERSONA
 * (GET /api/v1/tramites/biometric-validations/by-person): una fila por documento, sin repetir la misma
 * cédula, cubriendo tanto las prevalidaciones standalone como las validaciones nacidas de un trámite.
 * Provider-aware (mock | kyverum).
 *
 * El desglose POR VALIDACIÓN (cada intento, con su bitácora de envío, reenvío, aprobación o rechazo)
 * vive en el detalle de la persona — Acciones → Ver proceso — no en esta grilla.
 *
 * Crear una prevalidación se hace desde el botón "Nueva prevalidación" de esta misma pantalla. Si el
 * documento ya tiene una validación en vuelo en el tenant, NO se crea otra: se reutiliza la existente
 * (actualizando el correo si viene distinto) y se reenvía el enlace — ver PrevalidacionForm.
 *
 * AC8 — 4 estados de UI: Cargando (skeleton accesible), Error (role="alert"), Vacío (mensaje
 * explícito) y Lleno (KPIs + tabla). WCAG 2.1 AA: aria-labels por fila, foco visible, anuncios a
 * lectores de pantalla.
 *
 * Auto-refresco en vivo (fase 2): la grilla se actualiza sola cada AUTO_REFRESH_MS con los filtros
 * vigentes para reflejar los cambios que el backend persiste vía webhook/outbox de Kyverum, sin que el
 * gestor pulse "Actualizar" (que sigue disponible). Pausa cuando la pestaña no está visible.
 */

const ESTADO_META: Record<BiometricEstado, { label: string; tone: StatusTone }> = {
  enviado: { label: 'Enviado', tone: 'info' },
  en_proceso: { label: 'En proceso', tone: 'warning' },
  aprobado: { label: 'Aprobado', tone: 'success' },
  rechazado: { label: 'Rechazado', tone: 'danger' },
  expirado: { label: 'Expirado', tone: 'neutral' },
  pendiente_envio: { label: 'Pendiente de envío', tone: 'info' },
  error_envio: { label: 'Error de envío', tone: 'danger' },
};

const PROVIDER_LABEL: Record<string, string> = {
  mock: 'Simulado',
  kyverum: 'Kyverum',
};

/** Formatea una fecha ISO a texto legible (es-CO). Devuelve el ISO crudo si no parsea. */
function formatFecha(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium', timeStyle: 'short' }).format(d);
}

/** Formatea una fecha ISO solo a día (es-CO), sin hora. Para aprobación/expiración de la vigencia. */
function formatFechaCorta(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium' }).format(d);
}

/**
 * Enmascara el documento dejando visibles solo los últimos 4. CF-04 (Feature #11004, HU #11006)
 * retira el enmascarado de la TABLA principal de Validaciones (D3 — documento completo); este
 * helper se conserva solo para el banner de eventos atascados (observabilidad de dead-letter, fuera
 * del alcance de D3, que limita el documento/correo completos a "las tablas").
 */
function maskDoc(tipoDoc: string, documento: string): string {
  const tail = documento.length > 4 ? documento.slice(-4) : documento;
  const masked = documento.length > 4 ? `••••${tail}` : tail;
  return `${tipoDoc} ${masked}`.trim();
}

/**
 * Presentación de los días de vigencia restantes de una validación aprobada: color de urgencia
 * (verde holgado, ámbar por vencer, rojo vencida) + etiqueta. La vigencia es de 30 días desde la
 * aprobación; el backend ya calcula los días (0 = vencida). Null cuando la validación no está aprobada.
 */
function vigenciaBadge(dias: number | null): { label: string; color: string; bg: string } | null {
  if (dias == null) return null;
  if (dias <= 0) return { label: 'Vencida', color: '#E43D30', bg: 'rgba(228,61,48,0.12)' };
  if (dias <= 7) {
    return {
      label: `Por vencer (≤${dias} día${dias === 1 ? '' : 's'})`,
      color: '#F05A35',
      bg: 'rgba(249,172,0,0.16)',
    };
  }
  return { label: 'Vigente', color: '#70CF3A', bg: 'rgba(140,198,63,0.16)' };
}

/**
 * Filtros de persona para el endpoint agrupado (HU #11271), única grilla del módulo: solo semántica
 * de persona (los campos propios de UNA validación —referencia, modalidad, rol, proveedor, score,
 * motivo de rechazo— no aplican a un grupo y viven en el detalle). Vacíos → undefined (no se envían),
 * fechas a ISO con `createdTo`/`expiraHasta` a fin de día para incluir la fecha elegida.
 */
function buildPersonApiFilters(f: ValidacionesUiFilters): TenantBiometricPersonFilters {
  const text = (s: string) => (s.trim() === '' ? undefined : s.trim());
  const num = (s: string) => {
    if (s.trim() === '') return undefined;
    const n = Number(s);
    return Number.isNaN(n) ? undefined : n;
  };
  return {
    name: text(f.name),
    documentNumber: text(f.documentNumber),
    status: f.status || undefined,
    createdFrom: f.createdFrom ? `${f.createdFrom}T00:00:00` : undefined,
    createdTo: f.createdTo ? `${f.createdTo}T23:59:59` : undefined,
    vigenciaEstado: f.vigenciaEstado || undefined,
    expiraDesde: f.expiraDesde ? `${f.expiraDesde}T00:00:00` : undefined,
    expiraHasta: f.expiraHasta ? `${f.expiraHasta}T23:59:59` : undefined,
    venceEnDias: num(f.venceEnDias),
  };
}

/** Cadencia del auto-refresco en vivo de la grilla (fase 2). 15 s: fresco sin presionar el backend. */
const AUTO_REFRESH_MS = 15_000;

/** Opciones de filas por página (el cliente decide; de 10 en 10 hasta 50). */
const PAGE_SIZE_OPTIONS = [10, 20, 30, 40, 50];
const DEFAULT_PAGE_SIZE = 20;

/** HU #10944 (D10) — tope y cooldown de reenvíos, seguidos client-side (el contrato no los expone). */
const MAX_REENVIOS = 3;

/**
 * Estados en los que la validación sigue EN VUELO: la persona todavía no terminó el proceso, así que
 * el enlace de captura sirve. En cualquier otro estado el enlace ya no funciona aunque el backend lo
 * siga devolviendo (el endpoint agrupado no lo anula en estados terminales, a diferencia del plano).
 */
const ESTADOS_EN_VUELO: readonly BiometricEstado[] = ['enviado', 'en_proceso', 'pendiente_envio'];

/**
 * ¿Hay un enlace de captura REALMENTE utilizable? Exige las tres condiciones: que exista, que la
 * validación siga en vuelo y que el enlace no haya vencido. Gobierna tanto la celda "Enlace" como la
 * disponibilidad de "Copiar enlace" / "Abrir captura".
 */
function tieneEnlaceUtilizable(r: TenantBiometricValidation, now: number): boolean {
  if (!r.captureUrl || r.expired) return false;
  if (!ESTADOS_EN_VUELO.includes(r.status)) return false;
  if (!r.linkExpiresAt) return true;
  const vence = new Date(r.linkExpiresAt).getTime();
  return Number.isNaN(vence) || vence > now;
}

/**
 * ¿El ESTADO admite reenvío? No cuando la identidad ya está aprobada (no hay nada que capturar) ni
 * mientras la persona está EN PROCESO: el enlace vigente ya le sirve y reenviar invalidaría el que
 * está usando. Sí en el resto (enviado sin abrir, rechazado, expirado, error de envío).
 *
 * Ojo: que el estado lo admita no significa que la acción esté habilitada — el tope/cooldown de
 * reenvíos también la deshabilita. Esos casos se muestran como opción deshabilitada CON motivo, no
 * ocultándola: el gestor necesita saber por qué no puede.
 */
function estadoAdmiteReenvio(r: TenantBiometricValidation): boolean {
  return r.status !== 'aprobado' && r.status !== 'en_proceso';
}

/**
 * La validación terminó MAL y la persona se quedó sin forma de validarse: rechazada, expirada o con
 * el envío al proveedor agotado. Es el caso en el que un trámite se queda atascado si nadie vuelve a
 * lanzar la identidad, y por eso desde aquí se ofrece reintentarla (ver `onRetryClick`).
 */
function esTerminalRecuperable(r: TenantBiometricValidation): boolean {
  return (
    r.status === 'rechazado' ||
    r.status === 'expirado' ||
    r.status === 'error_envio' ||
    r.expired
  );
}

/** Estado de cooldown/tope de una fila, rastreado en memoria por la sesión de esta pantalla. */
interface ResendMeta {
  count: number;
  cooldownUntil: number | null;
}

/** Datos mínimos para pre-cargar "Nueva prevalidación" a partir de una fila `aprobado` y vencida. */
interface PrefillNueva {
  documentType?: string;
  documentNumber?: string;
  name?: string;
}

/** Resultado de un reenvío (manual, automático al editar el correo, o por documento ya existente). */
interface ResendResultState {
  email: string;
  captureUrl?: string | null;
  queued?: boolean;
  resendCount: number;
  /** Por qué se reenvió sin crear nada (documento ya existente); ausente en el reenvío manual. */
  notice?: string;
}

export function Validaciones() {
  // El admin FLIT puede mirar las validaciones de UNA empresa a la vez. El tenant elegido se fija en
  // el cliente de trámites (`setActiveTramitesTenant`), que es quien resuelve el header X-Tenant-Id de
  // TODA la pantalla — incluidos los drawers y formularios anidados, que así no necesitan recibirlo por
  // props. Para un usuario de compañía esto no cambia nada: el backend le impone su tenant desde el JWT.
  const [isFlitAdmin] = useState(() => isSuperAdmin(decodeJwtPayload(getToken())));
  const [companies, setCompanies] = useState<CompanyItem[] | null>(null);
  const [companyId, setCompanyId] = useState<string>('');
  const [showFiltros, setShowFiltros] = useState(false);
  const [incidenciasOpen, setIncidenciasOpen] = useState(false);

  const [persons, setPersons] = useState<TenantBiometricPerson[] | null>(null);
  const [stats, setStats] = useState<BiometricValidationStats | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [fetching, setFetching] = useState(false);
  const [hasLoadedOnce, setHasLoadedOnce] = useState(false);
  const [lastUpdatedAt, setLastUpdatedAt] = useState<Date | null>(null);
  // Eventos de identidad ATASCADOS (dead-letter) del tenant + ids que se están reencolando.
  // HU #11268 — cuatro estados del panel: loading / error / vacío / lleno.
  const [stuck, setStuck] = useState<StuckIdentityValidationsResponse | null>(null);
  const [stuckLoading, setStuckLoading] = useState(true);
  const [stuckError, setStuckError] = useState<string | null>(null);
  const [requeuing, setRequeuing] = useState<Set<string>>(() => new Set());
  const [requeuingAll, setRequeuingAll] = useState(false);
  // HU #11273 — historial multi-validación por persona: el desglose por validación y sus bitácoras.
  const [personDetail, setPersonDetail] = useState<{
    documentType: string;
    documentNumber: string;
  } | null>(null);

  // Gestión de prevalidaciones, absorbida de la pantalla retirada /tramites/prevalidaciones.
  const [showForm, setShowForm] = useState(false);
  const [prefillNueva, setPrefillNueva] = useState<PrefillNueva | undefined>(undefined);
  const [successResult, setSuccessResult] = useState<IniciarPrevalidacionResult | null>(null);
  const [editingRow, setEditingRow] = useState<TenantBiometricValidation | null>(null);
  // Confirmación de envío. Dos modos con endpoints distintos:
  //   'resend' — prevalidación standalone: mismo registro, token nuevo (POST .../resend).
  //   'retry'  — validación de un trámite terminada mal: lanza una validación NUEVA para esa parte
  //              (POST /instances/{id}/biometric), igual que el botón "Reintentar" del wizard. Sin
  //              esto, un rechazo o un vencimiento dejaba el trámite sin salida desde este módulo.
  const [confirmAction, setConfirmAction] = useState<{
    row: TenantBiometricValidation;
    mode: 'resend' | 'retry';
  } | null>(null);
  const [resendSubmitting, setResendSubmitting] = useState(false);
  const [resendConfirmError, setResendConfirmError] = useState<string | null>(null);
  const [resendResult, setResendResult] = useState<ResendResultState | null>(null);
  const [resendMeta, setResendMeta] = useState<Record<string, ResendMeta>>({});
  const [liveMessage, setLiveMessage] = useState('');

  // Tick ligero para refrescar la etiqueta "disponible en N min" sin depender de una acción del
  // usuario. No es una fuente de datos — solo fuerza el recálculo del cooldown en pantalla.
  const [nowTick, setNowTick] = useState(() => Date.now());
  useEffect(() => {
    const t = window.setInterval(() => setNowTick(Date.now()), 15_000);
    return () => window.clearInterval(t);
  }, []);

  // Paginación server-side (el listado ya NO se topa a 500; se navega por páginas).
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
  const [total, setTotal] = useState(0);
  const pageRef = useRef(page);
  pageRef.current = page;
  const pageSizeRef = useRef(pageSize);
  pageSizeRef.current = pageSize;

  // `filters` = controles de la UI (instantáneos); `applied` = lo que se consulta al backend. Los chips
  // y fechas aplican de inmediato; los inputs de texto aplican tras un debounce (~300 ms). El filtrado
  // se delega al backend (HU #10347) — NO se filtra client-side sobre el cap de 500 filas.
  const [filters, setFilters] = useState<ValidacionesUiFilters>(EMPTY_VALIDACIONES_FILTERS);
  const [applied, setApplied] = useState<ValidacionesUiFilters>(EMPTY_VALIDACIONES_FILTERS);
  const filtersRef = useRef(filters);
  filtersRef.current = filters;

  // Refs para el auto-refresco: el intervalo lee lo último sin re-suscribirse y se evitan carreras.
  const appliedRef = useRef(applied);
  appliedRef.current = applied;
  const personsRef = useRef(persons);
  personsRef.current = persons;
  const fetchingRef = useRef(false);
  const reqIdRef = useRef(0);

  const load = useCallback(
    async (uiFilters: ValidacionesUiFilters, opts?: { background?: boolean }) => {
      const reqId = ++reqIdRef.current; // marca de secuencia: solo se aplica el resultado más reciente
      fetchingRef.current = true;
      if (!opts?.background) setFetching(true);
      try {
        const res = await tramitesClient.listTenantBiometricPersons({
          ...buildPersonApiFilters(uiFilters),
          page: pageRef.current,
          pageSize: pageSizeRef.current,
        });
        if (reqId !== reqIdRef.current) return;
        setPersons(res.persons);
        setStats(res.stats);
        setTotal(res.total);
        setError(() => null);
        setLastUpdatedAt(new Date());
      } catch (err) {
        if (reqId !== reqIdRef.current) return;
        // En auto-refresco con datos ya en pantalla, un fallo transitorio NO machaca la vista con el error.
        if (opts?.background && personsRef.current !== null) {
          return;
        }
        setError(() =>
          err instanceof Error ? err.message : 'No se pudieron cargar las validaciones.',
        );
      } finally {
        if (reqId === reqIdRef.current) {
          fetchingRef.current = false;
          setFetching(false);
          setHasLoadedOnce(true);
        }
      }
    },
    [],
  );

  // Empresas disponibles para el admin FLIT. Un fallo aquí no rompe la pantalla: el selector
  // simplemente no aparece y se sigue viendo el tenant propio.
  useEffect(() => {
    if (!isFlitAdmin) return;
    let vivo = true;
    void superadminClient
      .listCompanies()
      .then((res) => {
        if (vivo) setCompanies(res.data ?? []);
      })
      .catch(() => {
        if (vivo) setCompanies([]);
      });
    return () => {
      vivo = false;
    };
  }, [isFlitAdmin]);

  // Al salir del módulo se devuelve el cliente a su tenant natural: el override es de ESTA pantalla,
  // no de la sesión (si no, el admin seguiría viendo la empresa elegida en Trámites o Dashboard).
  useEffect(() => () => setActiveTramitesTenant(undefined), []);

  const handleCompanyChange = (nextId: string) => {
    setCompanyId(nextId);
    // Se fija ANTES de recargar para que la petición ya salga con el tenant nuevo.
    setActiveTramitesTenant(nextId === '' ? undefined : nextId);
    setPersons(null);
    setStats(null);
    setHasLoadedOnce(false);
    setPage(1);
    pageRef.current = 1;
    void load(appliedRef.current);
    void refreshStuck();
  };

  // Eventos atascados (dead-letter): independiente de los filtros. HU #11268 — expone error/carga
  // sin romper la grilla principal (el fallo queda acotado al panel de atascadas).
  const stuckRef = useRef(stuck);
  stuckRef.current = stuck;
  const refreshStuck = useCallback(async (opts?: { background?: boolean }) => {
    if (!opts?.background) setStuckLoading(true);
    try {
      const res = await tramitesClient.listStuckIdentityValidations();
      setStuck(res);
      setStuckError(null);
    } catch (err) {
      // En auto-refresco con datos ya mostrados, un fallo transitorio no machaca el panel.
      if (opts?.background && stuckRef.current !== null) return;
      setStuckError(
        err instanceof Error ? err.message : 'No se pudieron cargar las validaciones atascadas.',
      );
    } finally {
      if (!opts?.background) setStuckLoading(false);
    }
  }, []);

  // Refetch cuando cambian los filtros aplicados O la página/tamaño (carga inicial incluida).
  useEffect(() => {
    void load(applied);
  }, [applied, page, pageSize, load]);

  // Los eventos atascados son por-tenant (independientes de filtros/página): refrescan con los filtros.
  useEffect(() => {
    void refreshStuck();
  }, [applied, refreshStuck]);

  // Auto-refresco en vivo (fase 2 — "suscripción"): tras la primera carga, refresca la grilla cada
  // AUTO_REFRESH_MS con los filtros vigentes para reflejar los cambios que el backend persiste vía
  // webhook/outbox de Kyverum (aprobado/rechazado/enviado) SIN que el gestor pulse "Actualizar". Pausa
  // cuando la pestaña no está visible (ahorra red) y refresca al volver a ella; nunca solapa peticiones.
  useEffect(() => {
    if (!hasLoadedOnce) return;
    const tick = () => {
      if (typeof document !== 'undefined' && document.visibilityState !== 'visible') return;
      if (fetchingRef.current) return;
      void load(appliedRef.current, { background: true });
      void refreshStuck({ background: true });
    };
    const intervalId = setInterval(tick, AUTO_REFRESH_MS);
    const onVisibility = () => {
      if (document.visibilityState === 'visible') tick();
    };
    document.addEventListener('visibilitychange', onVisibility);
    return () => {
      clearInterval(intervalId);
      document.removeEventListener('visibilitychange', onVisibility);
    };
  }, [hasLoadedOnce, load, refreshStuck]);

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const q = new URLSearchParams(window.location.search).get('q');
    if (!q?.trim()) return;
    const next = { ...EMPTY_VALIDACIONES_FILTERS, ...splitPersonaODocumentoQuery(q) };
    setFilters(next);
    setApplied(next);
    setShowFiltros(true);
    setPage(1);
  }, []);

  const applyChange = useCallback((patch: Partial<ValidacionesUiFilters>) => {
    setFilters({ ...filtersRef.current, ...patch });
  }, []);

  const handleSearch = useCallback(() => {
    setApplied(filtersRef.current);
    setPage(1);
  }, []);

  const handleRefresh = async () => {
    await load(appliedRef.current);
    void refreshStuck();
  };

  const handleClearFilters = useCallback(() => {
    setFilters(EMPTY_VALIDACIONES_FILTERS);
    setApplied(EMPTY_VALIDACIONES_FILTERS);
    setPage(1);
  }, []);

  const handleCancelConsulta = useCallback(() => {
    handleClearFilters();
    setShowFiltros(false);
  }, [handleClearFilters]);

  const handlePageChange = useCallback((p: number) => setPage(Math.max(1, p)), []);
  const handlePageSizeChange = useCallback((size: number) => {
    setPageSize(size);
    setPage(1); // cambiar el tamaño reinicia a la primera página
  }, []);

  // Reencolar ("desatascar") un evento: reinicia sus intentos en el backend y refresca atascados + grilla.
  const handleRequeue = useCallback(
    async (id: string) => {
      setRequeuing((s) => new Set(s).add(id));
      try {
        await tramitesClient.requeueStuckIdentityValidation(id);
        // El worker lo retomará; ya no figura como atascado. Refresca ambas vistas.
        await Promise.all([refreshStuck(), load(filtersRef.current, { background: true })]);
      } catch {
        void refreshStuck(); // refleja el estado real si el reencolado falló
      } finally {
        setRequeuing((s) => {
          const next = new Set(s);
          next.delete(id);
          return next;
        });
      }
    },
    [refreshStuck, load],
  );

  // Reencolar TODOS los atascados de una vez.
  const handleRequeueAll = useCallback(async () => {
    setRequeuingAll(true);
    try {
      await tramitesClient.requeueAllStuckIdentityValidations();
      await Promise.all([refreshStuck(), load(filtersRef.current, { background: true })]);
    } catch {
      void refreshStuck();
    } finally {
      setRequeuingAll(false);
    }
  }, [refreshStuck, load]);

  // ── Gestión de prevalidaciones (crear / editar / reenviar) ────────────────────

  const bumpResendMeta = useCallback((id: string) => {
    setResendMeta((prev) => {
      const cur = prev[id] ?? { count: 0, cooldownUntil: null };
      return { ...prev, [id]: { count: cur.count + 1, cooldownUntil: Date.now() + 5 * 60_000 } };
    });
  }, []);

  const applyRateLimit = useCallback((id: string, info: RateLimitInfo) => {
    setResendMeta((prev) => {
      const cur = prev[id] ?? { count: 0, cooldownUntil: null };
      return {
        ...prev,
        [id]: {
          count: info.maxedOut ? MAX_REENVIOS : cur.count,
          cooldownUntil: info.cooldownMinutes
            ? Date.now() + info.cooldownMinutes * 60_000
            : cur.cooldownUntil,
        },
      };
    });
  }, []);

  const handleCreated = (result: IniciarPrevalidacionResult) => {
    setShowForm(false);
    setPrefillNueva(undefined);
    setSuccessResult(result);
    void load(appliedRef.current);
  };

  /**
   * El documento ya tenía validación en vuelo: no se creó fila nueva. El formulario ya actualizó el
   * correo (si venía distinto) y reenvió el enlace; aquí solo se refleja el resultado y se refresca.
   */
  const handleReused = (info: PrevalidacionReuseInfo) => {
    setShowForm(false);
    setPrefillNueva(undefined);
    bumpResendMeta(info.validationId);
    const nextCount = (resendMeta[info.validationId]?.count ?? 0) + 1;
    const notice =
      info.kind === 'email_actualizado'
        ? `Ya existía una validación para este documento en este tenant: no se creó una nueva. Se actualizó el correo a ${info.email} y se reenvió el enlace.`
        : `Ya existía una validación para este documento en este tenant con ese mismo correo: no se creó una nueva ni se modificó nada, solo se reenvió el enlace.`;
    setResendResult({
      email: info.email,
      captureUrl: info.captureUrl,
      queued: info.queued,
      resendCount: nextCount,
      notice,
    });
    setLiveMessage(notice);
    void load(appliedRef.current);
  };

  const handleNew = () => {
    setSuccessResult(null);
    setPrefillNueva(undefined);
    setShowForm(true);
  };

  /** HU #10944 (D9/borde) — "Nueva prevalidación" para la misma persona desde un registro aprobado. */
  const handleNewFor = (row: TenantBiometricValidation) => {
    setPrefillNueva({
      documentType: row.documentType,
      documentNumber: row.documentNumber,
      name: row.name,
    });
    setShowForm(true);
  };

  const handleEditSaved = (row: TenantBiometricValidation, result: EditarPrevalidacionResult) => {
    setEditingRow(null);
    if (result.resent) {
      bumpResendMeta(row.id);
      const nextCount = (resendMeta[row.id]?.count ?? 0) + 1;
      setResendResult({
        email: result.validation.email,
        captureUrl: result.captureUrl,
        resendCount: nextCount,
      });
      setLiveMessage(`Datos actualizados. Validación reenviada a ${result.validation.email}.`);
    } else {
      setLiveMessage('Datos de la validación actualizados. No hubo cambio de correo, no se reenvió.');
    }
    void load(appliedRef.current);
  };

  /** Reenvío de una prevalidación standalone: mismo registro, enlace nuevo. */
  const confirmarReenvio = async (row: TenantBiometricValidation) => {
    const result = await tramitesClient.resendPrevalidacion(row.id);
    const nextCount = (resendMeta[row.id]?.count ?? 0) + 1;
    bumpResendMeta(row.id);
    setResendResult({
      email: result.validation.email,
      captureUrl: result.captureUrl,
      queued: result.queued,
      resendCount: nextCount,
    });
    setLiveMessage(
      result.queued
        ? `La validación quedó encolada para reenviarse a ${result.validation.email}.`
        : `Validación reenviada a ${result.validation.email}.`,
    );
  };

  /**
   * Reintento de una validación de trámite que terminó mal (rechazada, vencida o con el envío
   * agotado): lanza una validación NUEVA para esa parte por el mismo endpoint que usa el wizard, en
   * vez de reenviar la vieja (el backend prohíbe tocar por id una validación de trámite — D12).
   * El trámite debe admitir cambios (borrador o subsanación); si no, el backend responde 409 y el
   * mensaje explica que hay que abrir una subsanación.
   */
  const confirmarReintento = async (row: TenantBiometricValidation) => {
    if (!row.instanceId || !row.partyRole) throw new Error('La fila no identifica el trámite ni la parte.');
    const parte = row.partyRole as BiometricParte;
    if (row.provider === 'mock') {
      await tramitesClient.simulateBiometric(row.instanceId, { parte });
    } else {
      await tramitesClient.iniciarBiometric(row.instanceId, { parte });
    }
    setLiveMessage(
      `Se lanzó una validación de identidad nueva para ${row.name} en el trámite ${row.referenceNumber ?? ''}.`.trim(),
    );
  };

  const handleConfirmAction = async () => {
    if (!confirmAction) return;
    const { row, mode } = confirmAction;
    setResendSubmitting(true);
    setResendConfirmError(null);
    try {
      if (mode === 'retry') await confirmarReintento(row);
      else await confirmarReenvio(row);
      setConfirmAction(null);
      void load(appliedRef.current);
    } catch (err) {
      if (err instanceof TramitesApiError) {
        // 409 del trámite: el gate real es de negocio (radicado ⇒ congelado), no un fallo técnico.
        setResendConfirmError(
          mode === 'retry' && err.status === 409
            ? `${err.message} Si el trámite ya fue radicado, abre una subsanación para poder revalidar la identidad.`
            : err.message,
        );
        if (err.status === 429) applyRateLimit(row.id, parseRateLimitDetail(err.message));
      } else {
        setResendConfirmError(
          err instanceof Error ? err.message : 'No se pudo enviar la validación.',
        );
      }
    } finally {
      setResendSubmitting(false);
    }
  };

  // AC8 — estados de UI. La carga inicial (skeleton) solo aplica antes de la primera respuesta.
  const initialLoading = !hasLoadedOnce && persons === null && error === null;
  const isEmpty = persons !== null && persons.length === 0;
  // "Sin resultados" (AC2) vs "Aún no hay validaciones" se decide por los filtros EFECTIVAMENTE aplicados.
  const filtersActive = hasActiveValidacionesFilters(applied);
  // Empresa ajena que está mirando el admin FLIT (undefined = la suya).
  const empresaVista = companyId === '' ? undefined : companies?.find((c) => c.id === companyId);

  return (
    <div className="app-bg min-h-screen px-6 pt-6 pb-10 flex flex-col gap-4 text-[#162744] dark:text-white">
      {/* Anuncios para lector de pantalla del resultado de crear/editar/reenviar (WCAG 2.1 AA) */}
      <div className="sr-only" role="status" aria-live="polite">
        {liveMessage}
      </div>

      <ModuleTitle
        title="Validaciones"
        subtitle="Monitoreo, verificación biométrica y gestión de estados de identidad en tiempo real."
        right={
          <div className="flex items-center gap-2">
            {hasLoadedOnce ? <LiveIndicator at={lastUpdatedAt} /> : null}
            <button
              type="button"
              onClick={() => setIncidenciasOpen(true)}
              aria-label="Ver alertas de validación"
              aria-busy={stuckLoading}
              className="relative grid h-7 w-7 place-items-center rounded-lg border border-[#DFE5ED] bg-white transition hover:bg-[#EEF5FF] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF] dark:bg-[#162744]"
            >
              <Bell className="h-3.5 w-3.5" aria-hidden="true" />
              {!stuckLoading && stuck && stuck.total > 0 ? (
                <span
                  className="absolute -right-1.5 -top-1.5 grid h-4 min-w-4 place-items-center rounded-full px-1 text-[10px] font-bold text-white"
                  style={{ background: '#FF4E00' }}
                >
                  {stuck.total > 99 ? '99+' : stuck.total}
                </span>
              ) : null}
            </button>
          </div>
        }
      />

      {stuckLoading ? (
        <div className="sr-only" role="status" aria-label="Cargando validaciones atascadas">
          Cargando validaciones atascadas
        </div>
      ) : null}

      <div className="relative flex flex-col items-stretch gap-4 sm:flex-row">
        <div className="min-w-0 flex-1">
          <StatsCards stats={stats} totalPersonas={total} loading={initialLoading} />
        </div>
        <button
          type="button"
          onClick={handleNew}
          aria-label="Crear nueva prevalidación de identidad"
          className="flex h-[88px] w-full shrink-0 flex-col items-center justify-center rounded-2xl px-4 text-[13px] font-semibold leading-tight text-white transition hover:opacity-90 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF] sm:w-36"
          style={{ background: 'linear-gradient(90deg, #557EFF 0%, #00DBD5 100%)' }}
        >
          <span>Nueva</span>
          <span>prevalidación</span>
        </button>
      </div>

      <ValidacionesFilterToolbar
        filters={filters}
        onChange={applyChange}
        onSearch={handleSearch}
        onClearFilters={handleClearFilters}
        onCancelConsulta={handleCancelConsulta}
        open={showFiltros}
        onToggle={() => setShowFiltros((v) => !v)}
        loading={fetching}
        resultCount={persons?.length ?? 0}
        resultCountLabel={
          total === 0 ? 'Sin resultados' : `${total} persona${total === 1 ? '' : 's'}`
        }
        companyScope={
          isFlitAdmin && companies !== null && companies.length > 0
            ? {
                companies,
                companyId,
                onCompanyChange: handleCompanyChange,
                empresaVista,
              }
            : null
        }
      />

      {incidenciasOpen ? (
        <StuckIncidenciasDialog
          stuck={stuck}
          stuckLoading={stuckLoading}
          stuckError={stuckError}
          requeuing={requeuing}
          requeuingAll={requeuingAll}
          onRequeue={handleRequeue}
          onRequeueAll={() => void handleRequeueAll()}
          onRetryLoad={() => void refreshStuck()}
          onClose={() => setIncidenciasOpen(false)}
        />
      ) : null}

      {error && (
        <div
          className="rounded-2xl p-4 border text-xs flex items-start gap-3"
          style={{ borderColor: '#E43D30', background: 'rgba(228,61,48,0.06)', color: '#E43D30' }}
          role="alert"
          aria-live="polite"
        >
          <AlertCircle className="h-4 w-4 shrink-0 mt-0.5" aria-hidden="true" />
          <div className="space-y-2">
            <p className="font-semibold">No se pudieron cargar las validaciones.</p>
            <p className="opacity-80">{error}</p>
            <button
              type="button"
              onClick={() => void handleRefresh()}
              className="px-3 py-1.5 rounded-lg text-[11px] font-semibold text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
              style={{ background: '#E43D30' }}
            >
              Reintentar
            </button>
          </div>
        </div>
      )}

      {initialLoading && <ValidacionesSkeleton />}

      {isEmpty && (
        <div
          className="flex-1 min-h-0 grid place-items-center rounded-2xl border"
        >
          <div className="text-center max-w-md px-6 py-10">
            <ScanFace className="mx-auto h-10 w-10 opacity-30" aria-hidden="true" />
            {filtersActive ? (
              // AC2 — hubo respuesta vacía CON filtros activos: no es el estado inicial sin datos.
              <>
                <p className="mt-3 text-sm font-semibold">Sin resultados.</p>
                <p className="mt-1 text-xs opacity-70">
                  Ninguna validación coincide con los filtros aplicados. Ajusta o limpia los filtros
                  para ver más resultados.
                </p>
              </>
            ) : (
              <>
                <p className="mt-3 text-sm font-semibold">Aún no hay validaciones de identidad.</p>
                <p className="mt-1 text-xs opacity-70">
                  Aparecen aquí cuando inicias la identidad de una parte desde el paso de identidad de
                  un trámite, o cuando creas una prevalidación desde esta pantalla.
                </p>
                <button
                  type="button"
                  onClick={handleNew}
                  className="mt-4 mx-auto flex items-center gap-2 rounded-xl px-4 py-2 text-sm font-semibold text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF]"
                  style={{ background: 'linear-gradient(90deg, #4FD4CC 0%, #557EFF 100%)' }}
                >
                  Nueva prevalidación
                </button>
              </>
            )}
          </div>
        </div>
      )}

      {!initialLoading && !isEmpty && persons !== null && (
        <PersonasTable
          rows={persons}
          now={nowTick}
          resendMeta={resendMeta}
          onOpenPerson={(docType, docNumber) =>
            setPersonDetail({ documentType: docType, documentNumber: docNumber })
          }
          onEdit={setEditingRow}
          onResendClick={(row) => {
            setResendConfirmError(null);
            setConfirmAction({ row, mode: 'resend' });
          }}
          onRetryClick={(row) => {
            setResendConfirmError(null);
            setConfirmAction({ row, mode: 'retry' });
          }}
          onNewFor={handleNewFor}
        />
      )}

      {!initialLoading && persons !== null && persons.length > 0 && (
        <PaginationBar
          page={page}
          pageSize={pageSize}
          total={total}
          disabled={fetching}
          onPageChange={handlePageChange}
          onPageSizeChange={handlePageSizeChange}
        />
      )}

      {/* Detalle de la persona: desglose por validación + bitácoras de envío/reenvío/aprobación/rechazo. */}
      {personDetail && (
        <PersonIdentityDetailDrawer
          documentType={personDetail.documentType}
          documentNumber={personDetail.documentNumber}
          onClose={() => setPersonDetail(null)}
          onStatusChanged={() => void load(appliedRef.current, { background: true })}
        />
      )}

      {/* Modal: creación (también "Nueva prevalidación" precargada para revalidar, D9/borde) */}
      {showForm && (
        <PrevalidacionForm
          onClose={() => {
            setShowForm(false);
            setPrefillNueva(undefined);
          }}
          onSuccess={handleCreated}
          onReused={handleReused}
          initialValues={prefillNueva}
        />
      )}

      {successResult && (
        <PrevalidacionSuccessPanel
          result={successResult}
          onClose={() => setSuccessResult(null)}
          onNew={handleNew}
        />
      )}

      {/* Modal: edición (HU #10944, AC1/AC3/AC4/AC6) */}
      {editingRow && (
        <PrevalidacionEditForm
          row={editingRow}
          onClose={() => setEditingRow(null)}
          onSaved={(result) => handleEditSaved(editingRow, result)}
          onRateLimited={(info) => applyRateLimit(editingRow.id, info)}
        />
      )}

      {/* Confirmación de envío (HU #10944, AC2): reenvío standalone o reintento de trámite */}
      {confirmAction && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4"
          role="alertdialog"
          aria-modal="true"
          aria-labelledby="val-resend-confirm-title"
        >
          <div className="w-full max-w-sm rounded-2xl bg-white p-6 shadow-xl dark:bg-[#0B0F14]">
            <h2 id="val-resend-confirm-title" className="text-base font-semibold text-[#162744] dark:text-white">
              Reenviar validación
            </h2>
            <p className="mt-2 text-sm opacity-70">
              {confirmAction.mode === 'retry' ? (
                <>
                  Se lanzará una validación de identidad <strong>nueva</strong> para{' '}
                  <strong>{confirmAction.row.name}</strong> en el trámite{' '}
                  <strong>{confirmAction.row.referenceNumber ?? '—'}</strong>, con el correo registrado
                  en el trámite. La anterior queda en el historial.
                </>
              ) : (
                <>
                  ¿Reenviar el enlace de validación de <strong>{confirmAction.row.name}</strong>? El
                  enlace anterior dejará de funcionar.
                </>
              )}
            </p>
            {resendConfirmError && (
              <p role="alert" aria-live="assertive" className="mt-2 text-xs font-medium" style={{ color: '#FF4E00' }}>
                {resendConfirmError}
              </p>
            )}
            <div className="mt-4 flex justify-end gap-3">
              <button
                type="button"
                onClick={() => {
                  setConfirmAction(null);
                  setResendConfirmError(null);
                }}
                disabled={resendSubmitting}
                className="rounded-xl border px-4 py-2 text-sm font-medium text-[#162744] transition hover:bg-black/5 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF] disabled:opacity-50 dark:text-white dark:hover:bg-white/10"
              >
                Cancelar
              </button>
              <button
                type="button"
                onClick={() => void handleConfirmAction()}
                disabled={resendSubmitting}
                className="rounded-xl px-4 py-2 text-sm font-semibold text-white transition disabled:opacity-60 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF]"
                style={{ background: 'linear-gradient(90deg, #4FD4CC 0%, #557EFF 100%)' }}
              >
                {resendSubmitting ? 'Enviando…' : 'Confirmar reenvío'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Resultado del reenvío (manual, al editar el correo, o por documento ya existente) */}
      {resendResult && (
        <PrevalidacionResendResultPanel
          email={resendResult.email}
          captureUrl={resendResult.captureUrl}
          queued={resendResult.queued}
          resendCount={resendResult.resendCount}
          notice={resendResult.notice}
          onClose={() => setResendResult(null)}
        />
      )}
    </div>
  );
}

/**
 * Indicador de auto-refresco "En vivo" + hora de la última actualización. Decorativo: NO usa aria-live
 * (evita anunciar la hora cada 15 s a lectores de pantalla); los cambios de datos relevantes se anuncian
 * vía el contador role="status" del toolbar. El título da el contexto en hover/foco.
 */
function LiveIndicator({ at }: { at: Date | null }) {
  const time = at
    ? at.toLocaleTimeString('es-CO', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
    : null;
  return (
    <span
      className="inline-flex items-center gap-2 rounded-full px-3 py-1 text-xs font-semibold shrink-0"
      style={{ background: 'rgba(140,198,63,0.16)', color: '#4F7A12' }}
      title={time ? `La lista se actualiza automáticamente · ${time}` : 'La lista se actualiza automáticamente'}
    >
      <span className="relative flex h-2 w-2" aria-hidden="true">
        <span
          className="absolute inline-flex h-full w-full animate-ping rounded-full opacity-60"
          style={{ background: '#8CC63F' }}
        />
        <span className="relative inline-flex h-2 w-2 rounded-full" style={{ background: '#8CC63F' }} />
      </span>
      En vivo
    </span>
  );
}

/**
 * Clave de agrupación por persona (HU #11268). Documento normalizado (trim+upper); si no hay
 * documento ni nombre utilizable → grupo de no identificados.
 */
export function stuckPersonGroupKey(event: StuckIdentityValidation): string {
  const tipo = (event.documentType ?? '').trim().toUpperCase();
  const numero = (event.documentNumber ?? '').trim().toUpperCase();
  if (tipo || numero) return `${tipo}|${numero}`;
  return '__unidentified__';
}

export type StuckPersonGroup = {
  key: string;
  label: string;
  events: StuckIdentityValidation[];
};

/** Agrupa eventos atascados por persona; no identificados al final. */
export function groupStuckByPerson(events: StuckIdentityValidation[]): StuckPersonGroup[] {
  const map = new Map<string, StuckPersonGroup>();
  for (const event of events) {
    const key = stuckPersonGroupKey(event);
    let group = map.get(key);
    if (!group) {
      const isUnidentified = key === '__unidentified__';
      const label = isUnidentified
        ? 'No identificados'
        : (event.name?.trim() ||
          [event.documentType, event.documentNumber].filter(Boolean).join(' ') ||
          'Persona');
      group = { key, label, events: [] };
      map.set(key, group);
    }
    group.events.push(event);
  }
  const groups = [...map.values()];
  groups.sort((a, b) => {
    if (a.key === '__unidentified__') return 1;
    if (b.key === '__unidentified__') return -1;
    return b.events.length - a.events.length || a.label.localeCompare(b.label, 'es');
  });
  return groups;
}

/**
 * Modal de incidencias (campana). Reusa el agrupado por persona y Reintentar; ya no ocupa el cuerpo
 * de la pantalla.
 */
function StuckIncidenciasDialog({
  stuck,
  stuckLoading,
  stuckError,
  requeuing,
  requeuingAll,
  onRequeue,
  onRequeueAll,
  onRetryLoad,
  onClose,
}: {
  stuck: StuckIdentityValidationsResponse | null;
  stuckLoading: boolean;
  stuckError: string | null;
  requeuing: Set<string>;
  requeuingAll: boolean;
  onRequeue: (id: string) => void;
  onRequeueAll: () => void;
  onRetryLoad: () => void;
  onClose: () => void;
}) {
  const total = stuck?.total ?? 0;
  return (
    <div
      className="fixed inset-0 z-50 grid place-items-center p-6"
      style={{ background: 'rgba(22,39,68,0.45)', backdropFilter: 'blur(6px)' }}
      role="presentation"
      onClick={onClose}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="incidencias-title"
        className="relative max-h-[88vh] w-full max-w-5xl overflow-y-auto rounded-2xl border border-[#DFE5ED] bg-white p-6 dark:bg-[#162744]"
        onClick={(e) => e.stopPropagation()}
      >
        <button
          type="button"
          onClick={onClose}
          aria-label="Cerrar"
          className="absolute right-4 top-4 rounded-lg p-1 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF]"
        >
          <X className="h-4 w-4 opacity-70" aria-hidden="true" />
        </button>
        <div className="flex flex-wrap items-start justify-between gap-4 pr-8">
          <div>
            <h2 id="incidencias-title" className="text-base font-bold" style={{ color: '#557EFF' }}>
              Gestión de incidencias en validaciones
            </h2>
            <p className="mt-1 text-[13px] leading-snug opacity-70">
              {stuckError
                ? 'No se pudieron cargar las validaciones atascadas.'
                : total > 0
                  ? `Se detectaron ${total} proceso${total === 1 ? '' : 's'} atascado${total === 1 ? '' : 's'} por agotamiento de reintentos automáticos con el proveedor de identidad o el encadenamiento de firma/FUR.`
                  : 'No hay validaciones de identidad atascadas en este momento.'}
            </p>
          </div>
          {total > 1 ? (
            <button
              type="button"
              onClick={onRequeueAll}
              disabled={requeuingAll}
              className="h-11 shrink-0 rounded-xl px-6 text-[13px] font-semibold text-white disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
              style={{ background: '#FF4E00' }}
              aria-label="Reintentar todas las validaciones atascadas"
            >
              {requeuingAll ? 'Reencolando…' : 'Reintentar todos'}
            </button>
          ) : null}
        </div>

        {stuckLoading ? (
          <p className="mt-4 text-sm" role="status">
            Cargando incidencias…
          </p>
        ) : null}

        {stuckError ? (
          <div
            className="mt-4 rounded-2xl border p-4 text-sm"
            style={{ borderColor: '#F05A35', background: 'rgba(249,172,0,0.10)', color: '#F05A35' }}
            role="alert"
            aria-label="Error al cargar validaciones atascadas"
          >
            <p className="font-semibold">No se pudieron cargar las validaciones atascadas.</p>
            <p className="mt-1 opacity-80">{stuckError}</p>
            <button
              type="button"
              onClick={onRetryLoad}
              className="mt-2 rounded-lg px-3 py-1.5 text-xs font-semibold text-white"
              style={{ background: '#F05A35' }}
            >
              Reintentar
            </button>
          </div>
        ) : null}

        {!stuckLoading && !stuckError && stuck && stuck.total > 0 ? (
          <div className="mt-4">
            <StuckEventsBanner
              stuck={stuck}
              requeuing={requeuing}
              requeuingAll={requeuingAll}
              onRequeue={onRequeue}
              onRequeueAll={onRequeueAll}
              embedded
            />
          </div>
        ) : null}
      </div>
    </div>
  );
}

/**
 * Banner de validaciones de identidad ATASCADAS (dead-letter). En `embedded` se pinta dentro del
 * modal de la campana (sin banda naranja a pantalla completa).
 */
function StuckEventsBanner({
  stuck,
  requeuing,
  requeuingAll,
  onRequeue,
  onRequeueAll,
  embedded = false,
}: {
  stuck: StuckIdentityValidationsResponse;
  requeuing: Set<string>;
  requeuingAll: boolean;
  onRequeue: (id: string) => void;
  onRequeueAll: () => void;
  embedded?: boolean;
}) {
  const groups = groupStuckByPerson(stuck.stuck);
  const [openKeys, setOpenKeys] = useState<Set<string>>(() => new Set());
  const [openPanel, setOpenPanel] = useState(embedded);

  const toggleGroup = (key: string) => {
    setOpenKeys((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  };

  const list = (
          <div id="stuck-panel" className={embedded ? 'mt-1' : 'mt-2 pl-6'}>
          {!embedded ? (
          <p className="text-xs opacity-80">
            Agotaron {stuck.maxDeliveryAttempts} reintentos automáticos —el envío al proveedor de identidad o
            el encadenamiento de firma/FUR. Reencólalas para que el sistema las procese de nuevo.
          </p>
          ) : null}
          <ul className="mt-2 max-h-[40vh] space-y-3 overflow-y-auto pr-1" aria-label="Eventos atascados agrupados por persona">
            {groups.map((group) => {
              const open = openKeys.has(group.key);
              const panelId = `stuck-group-${group.key}`;
              return (
                <li key={group.key} className="rounded-2xl border bg-white p-4 dark:bg-[#0B0F14]" style={{ borderColor: '#DFE5ED' }}>
                  <button
                    type="button"
                    onClick={() => toggleGroup(group.key)}
                    className="flex w-full items-center justify-between gap-2 px-1 py-1 text-left text-[13px] font-bold focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
                    aria-expanded={open}
                    aria-controls={panelId}
                  >
                    <span className="min-w-0 truncate uppercase">
                      {group.label}
                      <span className="ml-1.5 font-medium opacity-70">
                        · {group.events.length} evento{group.events.length === 1 ? '' : 's'}
                      </span>
                    </span>
                    <span className="flex items-center gap-2">
                      <span
                        className="rounded-full px-2.5 py-1 text-xs font-semibold"
                        style={{ background: 'rgba(255,78,0,0.14)', color: '#FF4E00' }}
                      >
                        {group.events.length} evento{group.events.length === 1 ? '' : 's'} pendiente
                        {group.events.length === 1 ? '' : 's'}
                      </span>
                    <ChevronDown
                      className={`h-4 w-4 shrink-0 transition-transform ${open ? 'rotate-180' : ''}`}
                      aria-hidden="true"
                    />
                    </span>
                  </button>
                  {open && (
                    <ul id={panelId} className="space-y-2 pt-3">
                      {group.events.map((e) => (
                        <StuckRow key={e.id} event={e} busy={requeuing.has(e.id)} onRequeue={onRequeue} />
                      ))}
                    </ul>
                  )}
                </li>
              );
            })}
          </ul>
        </div>
  );

  if (embedded) {
    return (
      <section aria-label="Validaciones de identidad atascadas">
        <p className="sr-only" role="status">
          {stuck.total} validación{stuck.total === 1 ? '' : 'es'} de identidad atascada
          {stuck.total === 1 ? '' : 's'}
        </p>
        {list}
      </section>
    );
  }

  return (
    <section
      className="rounded-2xl border px-3 py-2 shrink-0"
      style={{ borderColor: '#F05A35', background: 'rgba(249,172,0,0.10)' }}
      aria-label="Validaciones de identidad atascadas"
    >
      <div className="flex items-center gap-2">
        <AlertTriangle className="h-4 w-4 shrink-0" style={{ color: '#F05A35' }} aria-hidden="true" />
        <button
          type="button"
          onClick={() => setOpenPanel((v) => !v)}
          aria-expanded={openPanel}
          aria-controls="stuck-panel"
          className="flex min-w-0 flex-1 items-center gap-1.5 text-left text-xs font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
          style={{ color: '#F05A35' }}
        >
          <span className="truncate" role="status" aria-live="polite">
            {stuck.total} validación{stuck.total === 1 ? '' : 'es'} de identidad atascada
            {stuck.total === 1 ? '' : 's'}
          </span>
          <ChevronDown
            className={`h-3.5 w-3.5 shrink-0 transition-transform ${openPanel ? 'rotate-180' : ''}`}
            aria-hidden="true"
          />
        </button>
        {stuck.total > 1 && (
          <button
            type="button"
            onClick={onRequeueAll}
            disabled={requeuingAll}
            className="flex shrink-0 items-center gap-1 rounded-lg border px-2.5 py-1 text-xs font-semibold disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
            style={{ borderColor: '#F05A35', color: '#F05A35' }}
            aria-label="Reintentar todas las validaciones atascadas"
          >
            <RotateCcw className={`h-3 w-3 ${requeuingAll ? 'animate-spin' : ''}`} aria-hidden="true" />
            {requeuingAll ? 'Reencolando…' : 'Reintentar todos'}
          </button>
        )}
      </div>
      {openPanel ? list : null}
    </section>
  );
}

function StuckRow({
  event,
  busy,
  onRequeue,
}: {
  event: StuckIdentityValidation;
  busy: boolean;
  onRequeue: (id: string) => void;
}) {
  // El envío al proveedor (Kyverum) vs. el encadenamiento async firma/FUR son etapas distintas; etiquetar
  // ayuda al gestor a entender qué se trabó. Default 'encadenamiento' si el backend no lo envía (transición).
  const esEnvio = event.kind === 'envio';
  const kindLabel = esEnvio ? 'Envío a proveedor' : 'Firma · FUR';
  return (
    <li
      className="flex items-center justify-between gap-3 rounded-xl border bg-white px-3 py-2 text-xs dark:bg-[#0B0F14]"
      style={{ borderColor: 'rgba(240,90,53,0.3)' }}
    >
      <span className="min-w-0 truncate">
        <span
          className="mr-1.5 inline-block rounded px-1.5 py-px text-[10px] font-semibold align-middle"
          style={{ background: 'rgba(240,90,53,0.14)', color: '#F05A35' }}
        >
          {kindLabel}
        </span>
        <span className="font-medium">{event.name ?? 'Persona no disponible'}</span>
        <span className="opacity-60">
          {event.documentNumber ? ` · ${maskDoc(event.documentType ?? '', event.documentNumber)}` : ''}
          {' · '}
          {event.attempts} intentos · {formatFecha(event.occurredAt)}
        </span>
      </span>
      <button
        type="button"
        onClick={() => onRequeue(event.id)}
        disabled={busy}
        className="flex shrink-0 items-center gap-1 rounded-lg px-2.5 py-1 font-semibold text-white disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
        style={{ background: '#F05A35' }}
        aria-label={`Reintentar la validación de ${event.name ?? 'persona no disponible'}`}
      >
        <RotateCcw className={`h-3 w-3 ${busy ? 'animate-spin' : ''}`} aria-hidden="true" />
        {busy ? 'Reencolando…' : 'Reintentar'}
      </button>
    </li>
  );
}

/** Barra de paginación: selector de filas por página (10–50) + navegación + "X–Y de N". */
function PaginationBar({
  page,
  pageSize,
  total,
  disabled,
  onPageChange,
  onPageSizeChange,
}: {
  page: number;
  pageSize: number;
  total: number;
  disabled: boolean;
  onPageChange: (p: number) => void;
  onPageSizeChange: (size: number) => void;
}) {
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, total);
  return (
    <div
      className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border bg-white p-3 dark:bg-[#0B0F14] shrink-0"
    >
      <div className="flex items-center gap-3 text-[11px]">
        <label className="flex items-center gap-1.5">
          <span className="opacity-60">Filas por página</span>
          <select
            value={pageSize}
            onChange={(e) => onPageSizeChange(Number(e.target.value))}
            disabled={disabled}
            aria-label="Filas por página"
            className="rounded-lg border bg-white px-2 py-1 text-xs outline-none focus:border-[#4F74C9] disabled:opacity-50 dark:bg-[#0B0F14]"
          >
            {PAGE_SIZE_OPTIONS.map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
        </label>
        <span className="opacity-60" role="status" aria-live="polite">
          {from}–{to} de {total}
        </span>
      </div>
      <div className="flex items-center gap-1.5">
        <button
          type="button"
          onClick={() => onPageChange(page - 1)}
          disabled={disabled || page <= 1}
          aria-label="Página anterior"
          className="flex h-7 w-7 items-center justify-center rounded-lg border disabled:opacity-40 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
        >
          <ChevronLeft className="h-4 w-4" aria-hidden="true" />
        </button>
        <span className="px-1 text-[11px] opacity-70">
          Página {page} de {totalPages}
        </span>
        <button
          type="button"
          onClick={() => onPageChange(page + 1)}
          disabled={disabled || page >= totalPages}
          aria-label="Página siguiente"
          className="flex h-7 w-7 items-center justify-center rounded-lg border disabled:opacity-40 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
        >
          <ChevronRight className="h-4 w-4" aria-hidden="true" />
        </button>
      </div>
    </div>
  );
}

/**
 * KPIs reales por estado. En carga muestra placeholders accesibles.
 *
 * El primer contador es de PERSONAS, no de validaciones: es el `total` del endpoint agrupado, el mismo
 * que cuenta el toolbar y el que cuadra con las filas de la grilla (una por documento). Los otros tres
 * siguen siendo conteos de validaciones — una misma persona puede tener una rechazada y otra aprobada.
 */
function StatsCards({
  stats,
  totalPersonas,
  loading,
}: {
  stats: BiometricValidationStats | null;
  totalPersonas: number;
  loading: boolean;
}) {
  const cards = [
    { l: 'Total personas', v: totalPersonas, i: ShieldCheck, c: '#4F74C9' },
    { l: 'Aprobadas', v: stats?.aprobadas, i: CheckCircle2, c: '#70CF3A' },
    { l: 'En proceso', v: stats?.enProceso, i: Clock, c: '#F05A35' },
    { l: 'Rechazadas', v: stats?.rechazadas, i: XCircle, c: '#E43D30' },
  ];
  return (
    <div
      className="grid grid-cols-2 divide-x divide-[#EEF2F7] rounded-2xl border border-[#DFE5ED] bg-white shadow-sm sm:grid-cols-4 dark:bg-[#162744] shrink-0"
    >
      {cards.map((k) => {
        const Icon = k.i;
        return (
          <div key={k.l} className="flex flex-col items-center justify-center gap-1 px-3 py-2">
            <span className="flex h-7 w-7 items-center justify-center rounded-full" style={{ background: `${k.c}1F` }}>
              <Icon className="h-3.5 w-3.5" style={{ color: k.c }} aria-hidden="true" />
            </span>
            <p className="w-full truncate text-center text-xs font-medium opacity-70">{k.l}</p>
            {loading ? (
              <div className="h-6 w-12 animate-pulse rounded bg-black/10 dark:bg-white/10" aria-hidden="true" />
            ) : (
              <p className="text-lg font-bold leading-none" style={{ color: k.c }}>
                {k.v ?? 0}
              </p>
            )}
          </div>
        );
      })}
    </div>
  );
}

/** Estado de carga (AC8): placeholder accesible mientras llega la primera respuesta. */
function ValidacionesSkeleton() {
  return (
    <div
      className="flex-1 min-h-0 space-y-2 pt-2"
      role="status"
      aria-live="polite"
      aria-busy="true"
    >
      <span className="sr-only">Cargando validaciones de identidad…</span>
      {[0, 1, 2, 3].map((i) => (
        <div
          key={i}
          className="h-12 w-full animate-pulse rounded-xl bg-black/5 dark:bg-white/5"
          aria-hidden="true"
        />
      ))}
    </div>
  );
}

/**
 * Plantilla de columnas compartida por cabecera y filas. Columnas DESACOPLADas: Registro, Aprobación y
 * Vigencia van por separado (cada dato en su propia columna y filtrable desde el toolbar). minmax(0,..)
 * permite truncar el contenido dentro de cada celda del grid.
 */
const GRID_COLS =
  'minmax(0,1.5fr) minmax(0,1.4fr) minmax(0,1.1fr) minmax(0,1.3fr) minmax(0,1.2fr) minmax(0,0.5fr) minmax(0,1.1fr) minmax(0,1fr) minmax(0,1.4fr) minmax(0,1.2fr) minmax(0,0.9fr)';

/**
 * Única grilla del módulo: una fila por PERSONA (documento), sin repetir cédula. Cada fila muestra los
 * datos de la validación MÁS RECIENTE del grupo; "Ver proceso" abre el historial multi-validación de
 * esa persona, donde vive el desglose por validación con sus bitácoras.
 */
function PersonasTable({
  rows,
  now,
  resendMeta,
  onOpenPerson,
  onEdit,
  onResendClick,
  onRetryClick,
  onNewFor,
}: {
  rows: TenantBiometricPerson[];
  now: number;
  resendMeta: Record<string, ResendMeta>;
  onOpenPerson: (documentType: string, documentNumber: string) => void;
  onEdit: (row: TenantBiometricValidation) => void;
  onResendClick: (row: TenantBiometricValidation) => void;
  onRetryClick: (row: TenantBiometricValidation) => void;
  onNewFor: (row: TenantBiometricValidation) => void;
}) {
  const byLatestId = new Map(rows.map((r) => [r.latestValidationId, r]));
  const mapped: TenantBiometricValidation[] = rows.map((p) => ({
    id: p.latestValidationId,
    instanceId: p.instanceId,
    referenceNumber: p.referenceNumber,
    modalidad: p.modalidad,
    partyRole: p.partyRole,
    name: p.name,
    documentType: p.documentType,
    documentNumber: p.documentNumber,
    status: p.status,
    score: p.score,
    provider: p.provider,
    expired: p.expired,
    createdAt: p.createdAt,
    validatedAt: p.validatedAt,
    validUntil: p.validUntil,
    daysRemaining: p.daysRemaining,
    captureUrl: p.captureUrl,
    linkExpiresAt: p.linkExpiresAt,
    // El DTO agrupado declara email como string; vacío = el backend aún no lo tiene → "—" en la celda.
    email: p.email || null,
    // HU #11505 — opcionales: el backend de esta vista aún no los envía (AC4, ver tipo en procedure-runtime.ts).
    intentos: p.intentos,
    maxIntentos: p.maxIntentos,
  }));

  const counts = new Map(rows.map((r) => [r.latestValidationId, r.validationCount]));

  return (
    <ValidacionesTable
      rows={mapped}
      now={now}
      resendMeta={resendMeta}
      validationCounts={counts}
      onViewProcess={(latestValidationId) => {
        const person = byLatestId.get(latestValidationId);
        if (person) onOpenPerson(person.documentType, person.documentNumber);
      }}
      onEdit={onEdit}
      onResendClick={onResendClick}
      onRetryClick={onRetryClick}
      onNewFor={onNewFor}
    />
  );
}

/** Tabla de validaciones. Cada fila es no navegable; el proceso se abre desde Acciones. */
function ValidacionesTable({
  rows,
  now,
  resendMeta,
  validationCounts,
  onViewProcess,
  onEdit,
  onResendClick,
  onRetryClick,
  onNewFor,
}: {
  rows: TenantBiometricValidation[];
  now: number;
  resendMeta: Record<string, ResendMeta>;
  validationCounts: Map<string, number>;
  onViewProcess: (id: string) => void;
  onEdit: (row: TenantBiometricValidation) => void;
  onResendClick: (row: TenantBiometricValidation) => void;
  onRetryClick: (row: TenantBiometricValidation) => void;
  onNewFor: (row: TenantBiometricValidation) => void;
}) {
  return (
    <div className="overflow-x-auto shrink-0">
      <div className="min-w-[1080px]">
        <div
          className="sticky top-0 z-10 grid gap-2 px-4 py-2.5 text-[10px] font-semibold uppercase rounded-t-xl"
          style={{ background: '#DDE5F0', color: '#162744', gridTemplateColumns: GRID_COLS }}
          aria-hidden="true"
        >
          <div>Trámite</div>
          <div>Persona</div>
          <div>Documento</div>
          <div>Correo</div>
          <div>Estado</div>
          <div>Score</div>
          <div>Registro</div>
          <div>Aprobación</div>
          <div>Vigencia</div>
          <div>Enlace vigente</div>
          <div>Acciones</div>
        </div>
        <ul className="space-y-2 pt-2" aria-label="Validaciones de identidad">
          {rows.map((r) => (
            <ValidacionRow
              key={r.id}
              row={r}
              now={now}
              resendMeta={resendMeta[r.id] ?? { count: 0, cooldownUntil: null }}
              validationCount={validationCounts.get(r.id) ?? 1}
              onViewProcess={() => onViewProcess(r.id)}
              onEdit={onEdit}
              onResendClick={onResendClick}
              onRetryClick={onRetryClick}
              onNewFor={onNewFor}
            />
          ))}
        </ul>
      </div>
    </div>
  );
}

function ValidacionRow({
  row: r,
  now,
  resendMeta,
  validationCount,
  onViewProcess,
  onEdit,
  onResendClick,
  onRetryClick,
  onNewFor,
}: {
  row: TenantBiometricValidation;
  now: number;
  resendMeta: ResendMeta;
  validationCount: number;
  onViewProcess: () => void;
  onEdit: (row: TenantBiometricValidation) => void;
  onResendClick: (row: TenantBiometricValidation) => void;
  onRetryClick: (row: TenantBiometricValidation) => void;
  onNewFor: (row: TenantBiometricValidation) => void;
}) {
  const [copied, setCopied] = useState(false);
  // Estado EFECTIVO: una validación no aprobada con el enlace vencido se lee "Expirado" aunque el
  // backend la conserve como enviada o en proceso (`expired` ya aplica esa regla). Los KPIs cuentan
  // por este mismo criterio: si la fila y el contador usaran estados distintos, no cuadrarían.
  const estado: BiometricEstado = r.expired && r.status !== 'aprobado' ? 'expirado' : r.status;
  const meta = ESTADO_META[estado] ?? ESTADO_META.enviado;
  // HU #11505 (AC1/AC2/AC4) — intentos consumidos, con el mismo criterio de lectura que el drawer
  // (PersonIdentityDetailDrawer: `${v.intentos} / ${v.maxIntentos}`). Ambos campos son opcionales: si
  // falta cualquiera de los dos, se omite el contador entero (nunca se pinta NaN/undefined/"0 / 0").
  const intentosInfo =
    typeof r.intentos === 'number' &&
    Number.isFinite(r.intentos) &&
    typeof r.maxIntentos === 'number' &&
    Number.isFinite(r.maxIntentos)
      ? { intentos: r.intentos, maxIntentos: r.maxIntentos }
      : null;
  // Un rechazo con intentos AGOTADOS (intentos >= maxIntentos) es legítimo; uno con intentos DISPONIBLES
  // es el rechazo prematuro del Bug #11503 (congelaba en `rechazado` al primer intento fallido). La
  // distinción tiene que leerse por TEXTO (WCAG 2.1 AA), no solo por color: cambia label + tone.
  const intentosAgotados = intentosInfo != null && intentosInfo.intentos >= intentosInfo.maxIntentos;
  const esRechazoPrematuro = estado === 'rechazado' && intentosInfo != null && !intentosAgotados;
  let badgeLabel: string = meta.label;
  let badgeTone: StatusTone = meta.tone;
  if (estado === 'rechazado' && intentosInfo != null) {
    if (intentosAgotados) {
      badgeLabel = 'Rechazado (intentos agotados)';
      badgeTone = 'danger';
    } else {
      badgeLabel = 'Rechazado (intentos disponibles)';
      badgeTone = 'warning';
    }
  }
  const modalidad = r.modalidad
    ? (familiaLabel(r.modalidad))
    : 'Prevalidación';
  const provider = PROVIDER_LABEL[r.provider] ?? r.provider;
  const parte = r.partyRole ? ` (${r.partyRole})` : '';
  const vigencia = vigenciaBadge(r.daysRemaining);
  const refLabel = r.referenceNumber ?? '—';
  const emailLabel = r.email ?? '—';
  const enlaceUtilizable = tieneEnlaceUtilizable(r, now);
  const ariaLabel =
    `Validación de ${r.name}${parte}, trámite ${refLabel} (${modalidad}), ` +
    `proveedor ${provider}, correo ${emailLabel}, estado ${badgeLabel}` +
    (intentosInfo ? `, intentos ${intentosInfo.intentos} de ${intentosInfo.maxIntentos}` : '') +
    (esRechazoPrematuro ? ', rechazo con intentos disponibles' : '') +
    (r.score != null ? `, score ${r.score}` : '') +
    (r.status === 'rechazado' && r.rejectionReason ? `, motivo: ${r.rejectionReason}` : '') +
    `, registrada ${formatFecha(r.createdAt)}` +
    (r.validatedAt ? `, aprobada ${formatFechaCorta(r.validatedAt)}` : '') +
    (r.validUntil ? `, vigente hasta ${formatFechaCorta(r.validUntil)}` : '') +
    (vigencia
      ? `, ${
          vigencia.label === 'Vencida'
            ? 'vigencia vencida'
            : r.daysRemaining != null
              ? `vigencia: ${vigencia.label}, ${r.daysRemaining} días restantes`
              : `vigencia: ${vigencia.label}`
        }`
      : '') +
    (r.instanceId ? '.' : '. Prevalidación standalone.') +
    (validationCount > 1 ? ` ${validationCount} validaciones en el historial de la persona.` : '');

  const copiarEnlace = async () => {
    if (!r.captureUrl) return;
    try {
      await navigator.clipboard.writeText(r.captureUrl);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2000);
    } catch {
      /* sin permiso de clipboard */
    }
  };

  // HU #10944 (D12/AC3) — pertenece a un trámite ⇒ solo lectura: editar/reenviar viven en el trámite.
  const isTramite = r.instanceId !== null;
  // HU #10944 (D9/AC4) — aprobada (vigente o vencida) ⇒ editar/reenviar bloqueados; se ofrece "Nueva".
  const isApproved = r.status === 'aprobado';
  const admiteReenvio = estadoAdmiteReenvio(r);

  let resendDisabledReason: string | null = null;
  if (resendMeta.count >= MAX_REENVIOS) {
    resendDisabledReason = 'Se agotó el tope de 3 reenvíos.';
  } else if (resendMeta.cooldownUntil && resendMeta.cooldownUntil > now) {
    const minsLeft = Math.max(1, Math.ceil((resendMeta.cooldownUntil - now) / 60_000));
    resendDisabledReason = `Disponible en ${minsLeft} min.`;
  }

  const actionItems: ActionsMenuItem[] = [
    {
      key: 'proceso',
      label: 'Ver proceso',
      icon: ListTree,
      onSelect: onViewProcess,
    },
  ];

  // Copiar/abrir solo cuando el enlace sirve de verdad: en estados terminales no lleva a ninguna parte.
  if (enlaceUtilizable) {
    actionItems.push({
      key: 'copiar',
      label: copied ? 'Enlace copiado' : 'Copiar enlace',
      icon: Copy,
      onSelect: () => {
        void copiarEnlace();
      },
    });
    actionItems.push({
      key: 'abrir',
      label: 'Abrir captura',
      icon: ExternalLink,
      onSelect: () => {
        window.open(r.captureUrl!, '_blank', 'noopener,noreferrer');
      },
    });
  }

  if (!isTramite && isApproved) {
    actionItems.push({
      key: 'nueva',
      label: 'Nueva prevalidación',
      icon: ScanFace,
      onSelect: () => onNewFor(r),
    });
  } else if (!isTramite) {
    actionItems.push({
      key: 'editar',
      label: 'Editar',
      icon: Pencil,
      onSelect: () => onEdit(r),
    });
  }

  if (isTramite) {
    // Validación de trámite terminada mal: sin esta salida el trámite se queda atascado — la identidad
    // no se puede reenviar por id (D12) y habría que entrar al wizard. Se lanza una validación NUEVA
    // para la parte, exactamente lo que hace "Reintentar validación" del paso de identidad.
    if (esTerminalRecuperable(r)) {
      const sinParte = !r.partyRole;
      actionItems.push({
        key: 'reintentar',
        // Mismo rótulo que en las prevalidaciones: para el gestor la acción es "reenviar", aunque
        // por dentro sean endpoints distintos (aquí nace una validación nueva para la parte).
        label: 'Reenviar',
        icon: sinParte ? Clock : Send,
        onSelect: () => onRetryClick(r),
        disabled: sinParte,
        disabledReason: sinParte
          ? 'La fila no identifica la parte del trámite: hazlo desde el trámite.'
          : undefined,
      });
    }
  } else if (admiteReenvio) {
    // Prevalidación standalone: mismo registro, enlace nuevo. Si el tope/cooldown lo impide se muestra
    // deshabilitada con el motivo, en vez de desaparecer sin explicación.
    actionItems.push({
      key: 'reenviar',
      label: 'Reenviar',
      icon: resendDisabledReason ? Clock : Send,
      onSelect: () => onResendClick(r),
      disabled: resendDisabledReason !== null,
      disabledReason: resendDisabledReason ?? undefined,
    });
  }

  const rowContent = (
    <div
      className="grid gap-2 items-center px-4 py-3 text-xs"
      style={{ gridTemplateColumns: GRID_COLS }}
    >
      <div className="min-w-0">
        {r.referenceNumber ? (
          <span className="flex items-center gap-1 font-mono font-semibold" style={{ color: '#4F74C9' }}>
            <span className="truncate">{r.referenceNumber}</span>
          </span>
        ) : (
          <span
            className="inline-block rounded-full px-2 py-0.5 text-[10px] font-semibold"
            style={{ background: 'rgba(79,116,201,0.12)', color: '#4F74C9' }}
          >
            Prevalidación
          </span>
        )}
      </div>
      <div className="min-w-0">
        <span className="block font-semibold truncate">{r.name}</span>
        {/* Subtítulo: solo lo que distingue a ESTA persona. El proveedor (siempre el mismo dentro de
            un tenant) se sacó de la fila — truncaba el rol y el contador; se consulta en el detalle. */}
        {(r.partyRole || validationCount > 1) && (
          <span className="block text-[10px] opacity-60 truncate">
            {r.partyRole ?? ''}
            {r.partyRole && validationCount > 1 ? ' · ' : ''}
            {validationCount > 1 ? `${validationCount} validaciones` : ''}
          </span>
        )}
      </div>
      <div className="min-w-0 font-mono text-[11px] opacity-80 truncate">
        {r.documentType} {r.documentNumber}
      </div>
      <div className="min-w-0 text-[11px] opacity-80 truncate" title={emailLabel}>
        {emailLabel}
      </div>
      <div className="min-w-0">
        <StatusBadge label={badgeLabel} tone={badgeTone} ariaLabel={`Estado: ${badgeLabel}`} />
        {/* HU #11505 (AC1) — contador de intentos, mismo criterio que el drawer. AC4: si falta
            `intentos` o `maxIntentos`, no se pinta nada (nunca NaN/undefined/"0 / 0"). */}
        {intentosInfo && (
          <span className="mt-0.5 block text-[10px] opacity-70">
            {intentosInfo.intentos} / {intentosInfo.maxIntentos} intentos
          </span>
        )}
        {r.status === 'rechazado' && r.rejectionReason && (
          <span className="mt-0.5 block text-[10px] opacity-70 truncate" title={r.rejectionReason}>
            {r.rejectionReason}
          </span>
        )}
      </div>
      <div className="font-semibold">{r.score ?? '—'}</div>
      <div className="min-w-0 text-[10px] leading-tight opacity-80">{formatFecha(r.createdAt)}</div>
      <div className="min-w-0 text-[10px] leading-tight opacity-80">
        {r.validatedAt ? formatFechaCorta(r.validatedAt) : '—'}
      </div>
      <div className="min-w-0 text-[10px] leading-tight">
        {vigencia ? (
          <span
            title={r.validUntil ? formatFechaCorta(r.validUntil) : undefined}
            className="font-medium"
            style={{ color: vigencia.color }}
          >
            {vigencia.label}
          </span>
        ) : (
          <span className="opacity-80">—</span>
        )}
      </div>
      <div className="min-w-0 text-[11px] leading-tight">
        {enlaceUtilizable ? (
          <span title={r.captureUrl ?? undefined}>Sí</span>
        ) : (
          <span className="opacity-70">No</span>
        )}
      </div>
      <div className="flex min-w-0 flex-col items-end gap-0.5">
        <ActionsMenu
          ariaLabel={`Acciones de validación de ${r.name}`}
          items={actionItems}
          className="bg-white dark:bg-[#0B0F14]"
        />
        {!isTramite && admiteReenvio && resendDisabledReason && (
          <span className="text-[10px] opacity-60">{resendDisabledReason}</span>
        )}
        {copied && (
          <span className="text-[10px] font-semibold" style={{ color: '#4F74C9' }} role="status" aria-live="polite">
            Enlace copiado
          </span>
        )}
      </div>
    </div>
  );

  return (
    <li
      className="relative rounded-xl bg-white dark:bg-[#0B0F14] border hover:border-[#4F74C9] transition"
      aria-label={ariaLabel}
    >
      {/* Fila no navegable: el detalle/proceso se abre solo desde Acciones → Ver proceso. */}
      {rowContent}
    </li>
  );
}
