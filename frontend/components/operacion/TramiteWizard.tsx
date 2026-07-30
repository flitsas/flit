'use client';

import { useCallback, useEffect, useRef, useState, type RefObject } from 'react';
import { useRouter } from 'next/navigation';
import {
  Briefcase,
  Building2,
  Calendar,
  Car,
  Check,
  ChevronLeft,
  ChevronRight,
  Eye,
  Fuel,
  Gauge,
  Hash,
  Layers,
  Lock,
  Palette,
  RefreshCw,
  Search,
  Shield,
  Tag,
  Users,
  Wrench,
} from 'lucide-react';
import { useProcedureInstance } from '@/hooks/useProcedureInstance';
import { useWizard } from '@/hooks/useWizard';
import { useWizardTelemetry } from '@/hooks/useWizardTelemetry'; // Reportes2 HU-A
import { PreflightPanel } from './PreflightPanel';
import { ActiveDeedsCollapse } from './ActiveDeedsCollapse';
import { ActorsForm } from './ActorsForm';
import { DocumentChecklist } from './DocumentChecklist';
import { CommercialForm } from './CommercialForm';
import { PrendaForm } from './PrendaForm';
import { SubsanacionPanel } from './SubsanacionPanel';
import type { WizardStepFormHandle } from './wizard-step-form';
import { BiometricStep } from './BiometricStep';
import { FirmaFurStep } from './FirmaFurStep';
import { reasonCopy, blockerCopy } from './wizard-copy';
import { canNavigateToStep, frontierIndex } from './wizard-navigation';
import { WizardReadOnlyProvider, useWizardReadOnly } from './WizardReadOnlyContext';
import { VehicleTransformationsCard } from './VehicleTransformationsCard';
import { useToast } from '@/components/admin/Toast';
import {
  tramitesClient,
  getDuplicateActiveProcedureId,
  getVehicleStateBlock,
  type VehicleStateBlockInfo,
} from '@/lib/api/tramites-client';
import { getToken } from '@/lib/api/client';
import { decodeJwtPayload } from '@/lib/auth/jwt';
import {
  isTenantOwnDocument,
  normalizeNitDigits,
  OWNER_NOT_TENANT_MESSAGE,
} from '@/lib/tramites/vehicleOwnership';
import {
  sanitizeVin,
  validateVin,
  sanitizePlate,
  validatePlate,
  sanitizeDocNumber,
  validateDocNumber,
} from '@/lib/validation/fieldRules';
import type {
  ActorDocumentType,
  BiometricParte,
  FieldValue,
  FieldValueInput,
  InstanceStatus,
  PreflightSnapshot,
  ProcedureConfiguration,
  ProcedureInstanceSummary,
  StatusHistory,
  WizardModalidad,
  WizardStep,
  WizardStepStatus,
} from '@/lib/api/types/procedure-runtime';

/**
 * El wizard es server-driven: una vez creada la instancia, GET /wizard decide
 * modalidad/pasos/status. Por eso solo necesita saber CÓMO obtener la instancia.
 *
 * - Instancia existente (Track B): `existingInstanceId` — el wizard opera sobre
 *   un draft ya creado (ruta /tramites/[instanceId]). NO crea nada.
 * - Entrada por modalidad (M0 + CF-02): `modalidad` + `title` — NO crea nada al
 *   montar. El paso 1 (consulta del vehículo) opera sin instancia y el trámite
 *   se crea al avanzar al paso 2, avisando por `onCreated`.
 * - Entrada legacy por tipo publicado: `configuration` + `procedureTypeId` —
 *   conserva el auto-create al montar (selector de tipos publicados).
 *
 * Exactamente una de las tres vías debe estar presente.
 */
type Props = {
  onExit: () => void;
} & (
  | { existingInstanceId: string; modalidad?: undefined; title?: undefined; configuration?: undefined; procedureTypeId?: undefined; onCreated?: undefined; seedVin?: undefined; seedPlaca?: undefined }
  | {
      modalidad: WizardModalidad;
      title: string;
      /** CF-02 — el trámite acaba de crearse al avanzar al paso 2; la página navega a su ruta. */
      onCreated?: (summary: ProcedureInstanceSummary) => void;
      /** R3 (HU #10539) — vehículo sembrado desde el CTA "Iniciar traspaso": solo prellena el paso 1. */
      seedVin?: string;
      seedPlaca?: string;
      existingInstanceId?: undefined;
      configuration?: undefined;
      procedureTypeId?: undefined;
    }
  | { configuration: ProcedureConfiguration; procedureTypeId: string; existingInstanceId?: undefined; modalidad?: undefined; title?: undefined; onCreated?: undefined; seedVin?: undefined; seedPlaca?: undefined }
);

/**
 * CF-02 — consulta del paso 1 resuelta SIN trámite creado: lo que hace falta para dar de alta el
 * registro al avanzar al paso 2. `previewToken` evita repetir la consulta al RUNT en la creación.
 */
type PendingConsulta = {
  previewToken: string;
  vin?: string;
  plate?: string;
  ownerDocumentType?: string;
  ownerDocumentNumber?: string;
};

const STATUS_BADGE: Record<
  WizardStepStatus,
  { bg: string; color: string }
> = {
  complete: { bg: '#8CC63F', color: '#fff' },
  incomplete: { bg: '#DFE5ED', color: '#162744' },
  locked: { bg: '#EEF1F5', color: '#9AA5B1' },
};

/**
 * Subtítulo descriptivo por paso, mostrado UNA sola vez bajo el `h2` (título
 * canónico del paso = `activeStep.label`). Centraliza aquí el copy de ayuda que
 * antes vivía duplicado en los `h4` internos de cada hijo; al subirlo evitamos
 * el doble título (uno en la shell, otro en el box). Keys sin entrada no pintan
 * subtítulo. El paso `fur` conserva su intro propia (actúa como subsección).
 */
const STEP_SUBTITLE: Record<string, string> = {
  consulta_vin: 'Ingresa el VIN para consultar los datos del vehículo en el RUNT.',
  consulta: 'Ingresa la placa y el propietario para consultar los datos del RUNT.',
  documentos: 'Adjunta los documentos que exige el trámite (PDF, JPG, PNG o WEBP, máx 20 MB).',
  comercial: 'Valor de la venta, causal e impuestos del traspaso.',
  fur: 'Fecha y observaciones del FUR. El expediente (FUR, certificados y consolidado) se genera al Preparar.',
  identidad:
    'Validación de identidad de cada parte. La biométrica real llegará en una iteración futura; por ahora puedes simular la validación de cada parte.',
};

/**
 * ¿La validación de identidad está aprobada? (HU #10350) Se deriva del estado server-driven de los
 * pasos, sin recálculo en cliente: en matrícula el paso `identidad` queda `complete` cuando la
 * biométrica del comprador está aprobada; en traspaso la biométrica vive dentro del paso `fur`, que
 * lista `pendiente_biometria` mientras falte alguna parte. `locked` ⇒ aún no alcanzable ⇒ no aprobada.
 */
function isIdentityApproved(steps: WizardStep[], modalidad: WizardModalidad): boolean {
  if (modalidad === 'traspaso') {
    const fur = steps.find((s) => s.key === 'fur');
    if (!fur || fur.status === 'locked') return false;
    return !fur.reasons.includes('pendiente_biometria');
  }
  const identidad = steps.find((s) => s.key === 'identidad');
  // HU #10549 — sin paso de identidad (el OT la deshabilitó y el wizard lo ocultó) ⇒ no se exige.
  return identidad ? identidad.status === 'complete' : true;
}

/** Icono/marcador por status del paso (✓ / • / 🔒). */
function StepMarker({ status, index }: { status: WizardStepStatus; index: number }) {
  const s = STATUS_BADGE[status];
  return (
    <span
      className="h-8 w-8 rounded-full grid place-items-center text-[11px] font-bold shrink-0"
      style={{ background: s.bg, color: s.color }}
      aria-hidden="true"
    >
      {status === 'complete' ? (
        <Check className="h-4 w-4" />
      ) : status === 'locked' ? (
        <Lock className="h-3.5 w-3.5" />
      ) : (
        index + 1
      )}
    </span>
  );
}

/**
 * Shell del wizard diferenciado, server-driven por GET /wizard. El backend
 * decide modalidad, pasos, status, razones y blockers; la shell pinta el
 * sidebar y renderiza el cuerpo del paso activo según modalidad+key. Tras cada
 * acción que mueva gates (actor, documento, preflight, comercial) se llama
 * `refresh()` para re-consultar el estado autoritativo.
 */
export function TramiteWizard(props: Props) {
  const {
    configuration,
    procedureTypeId,
    modalidad: entryModalidad,
    title,
    existingInstanceId,
    onCreated,
    seedVin,
    seedPlaca,
    onExit,
  } = props;
  const { state, start } = useProcedureInstance();
  // Con instancia existente (Track B) no se crea nada: el id viene por prop.
  // En la vía legacy por tipo publicado el id lo produce `start()` → state.instanceId.
  const instanceId = existingInstanceId ?? state.instanceId;

  // CF-02 (HU #10883, AC3) — entrada por modalidad: el trámite NO existe todavía. El paso 1 corre
  // contra la consulta desacoplada y el registro se crea al avanzar al paso 2 (AC4). Solo aplica
  // mientras no haya instancia: en cuanto se crea, el wizard es el de siempre.
  const deferredCreation = !existingInstanceId && !!entryModalidad && !instanceId;
  // Consulta resuelta en el paso 1 (token + identificadores) a la espera de crear el trámite.
  const [pendingConsulta, setPendingConsulta] = useState<PendingConsulta | null>(null);
  // Condiciones marcadas en el paso 1 antes de que el trámite exista (leasing, carrocería, paz y
  // salvo, riesgo aceptado, transformaciones). Se persisten en el mismo acto de la creación, para
  // que el paso 1 ofrezca lo MISMO que antes y solo cambie el momento del guardado.
  const pendingFieldValuesRef = useRef<Map<string, string>>(new Map());
  const collectPendingFieldValues = useCallback(
    (items: { fieldKey: string; valueText: string }[]) => {
      for (const item of items) pendingFieldValuesRef.current.set(item.fieldKey, item.valueText);
    },
    [],
  );

  // Estado de la instancia existente + sello de borrador finalizado (HU #10350). Se derivan
  // de ellos los tres modos del wizard (ver más abajo). Los trámites nuevos arrancan editables.
  const [instanceStatus, setInstanceStatus] = useState<InstanceStatus | null>(null);
  const [draftFinalizedAt, setDraftFinalizedAt] = useState<string | null>(null);
  // HU #10874 (AC1) — historial de estados de la instancia: fuente única de datos del panel de
  // subsanación (motivo/checklist de la última transición a `subsanacion`). Loading/error propios
  // (no el `.catch` silencioso de arriba) porque sin ellos el panel no podría distinguir "cargando"
  // de "sin observación" (4 estados de UI).
  const [statusHistory, setStatusHistory] = useState<StatusHistory[]>([]);
  // Estado inicial derivado de `existingInstanceId` (prop estable durante el ciclo de vida del
  // wizard: la página lo remonta por `key` en cada refresh) para no llamar setState de forma
  // síncrona dentro del efecto (react-hooks/set-state-in-effect).
  const [instanceDetailLoading, setInstanceDetailLoading] = useState(!!existingInstanceId);
  const [instanceDetailError, setInstanceDetailError] = useState<string | null>(null);
  useEffect(() => {
    if (!existingInstanceId) return;
    let active = true;
    tramitesClient
      .getInstance(existingInstanceId)
      .then((d) => {
        if (!active) return;
        setInstanceStatus(d.status ?? null);
        setDraftFinalizedAt(d.draftFinalizedAt ?? null);
        setStatusHistory(d.statusHistory ?? []);
        setInstanceDetailError(null);
      })
      .catch((err) => {
        if (!active) return;
        setInstanceDetailError(
          err instanceof Error ? err.message : 'No se pudo cargar el detalle del trámite.',
        );
      })
      .finally(() => {
        if (active) setInstanceDetailLoading(false);
      });
    return () => {
      active = false;
    };
  }, [existingInstanceId]);

  // Los modos del wizard se derivan más abajo (tras useWizard): el estado de negocio autoritativo
  // llega en GET /wizard y se re-lee en cada refresh — necesario para que "Preparar" (N 03)
  // actualice el modo sin re-consultar la instancia.

  // Clave estable de creación (modalidad o procedureTypeId) para el guard.
  const startKey = entryModalidad ?? procedureTypeId ?? '';

  // Guardia anti doble-create: StrictMode re-invoca los efectos en dev, lo que
  // dispararía DOS POST /instances casi simultáneos → choque UNIQUE de
  // reference_number → 500 y wizard sin instanceId (luego 404 en silencio).
  // El ref persiste entre la doble invocación del mismo montaje y garantiza
  // que `start()` corra UNA sola vez por entrada.
  const startedForRef = useRef<string | null>(null);

  // Crea la instancia draft al montar SOLO en la vía legacy por tipo publicado. Con instancia
  // existente no hay nada que crear, y en la entrada por modalidad la creación se difiere al avance
  // al paso 2 (CF-02): entrar al wizard ya no da de alta ningún registro.
  useEffect(() => {
    if (existingInstanceId || entryModalidad) return;
    if (startedForRef.current === startKey) return;
    startedForRef.current = startKey;
    void start({ procedureTypeId: procedureTypeId! });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [startKey, existingInstanceId, entryModalidad]);

  const {
    wizard,
    steps,
    canSubmit,
    blockers,
    loading: wizardLoading,
    error: wizardError,
    refresh,
  } = useWizard(instanceId, undefined, deferredCreation ? entryModalidad : undefined);

  // N 03 — estado de negocio del trámite: manda el del wizard (se refresca tras cada acción);
  // fallback al fetch inicial de la instancia existente mientras el wizard carga.
  const estadoTramite = (wizard?.status ?? instanceStatus) as InstanceStatus | null;

  // Modos del wizard (HU #10350 + N 03 radicación en dos pasos):
  //  • Editable: borrador SIN finalizar (o trámite nuevo) → captura completa de datos.
  //  • Borrador finalizado (`borrador` + draftFinalizedAt): datos en solo lectura, pero el paso de
  //    Identidad sigue operable (el cliente valida async). "Preparar" solo cuando el wizard
  //    reporte canSubmit + identidad aprobada.
  //  • Preparado: solo lectura, con la acción "Radicar a tránsito" (preparado→entregado) en el
  //    paso de decisión.
  //  • Solo visualización (Track C): estados posteriores (entregado, aprobado, rechazado, anulado).
  // Subsanación: flag sobre rechazado (o legado status `subsanacion`) reabre la edición COMPLETA.
  const inSubsanacion =
    estadoTramite === 'subsanacion' ||
    (estadoTramite === 'rechazado' && !!wizard?.subsanacionActiva);
  const fullReadOnly =
    !!estadoTramite &&
    estadoTramite !== 'borrador' &&
    !inSubsanacion;
  const draftFinalized = estadoTramite === 'borrador' && !!draftFinalizedAt;
  // Captura de datos deshabilitada en todos los modos no-editables (provider de solo lectura).
  const editLocked = fullReadOnly || draftFinalized;
  // Navegación: en visualización pura solo se recorren los pasos completos; en borrador finalizado
  // se respeta la regla de frontera (para poder llegar al paso de Identidad, que es la frontera).
  const navViewOnly = fullReadOnly;
  // N 03 — "Radicar a tránsito" disponible solo en `preparado` y si la máquina lo permite
  // (el backend manda vía allowedTransitions).
  const canEntregar =
    estadoTramite === 'preparado' &&
    (wizard?.allowedTransitions?.includes('entregado') ?? false);

  const [activeIndex, setActiveIndex] = useState(0);
  // Reanudar (Track B): al abrir una instancia existente queremos caer en el
  // paso donde quedó el usuario (la frontera), no en el paso 1. Este ref marca
  // que ya inicializamos el paso desde el primer `steps` del server, para NO
  // re-saltar en cada refresh posterior (p.ej. tras "Guardar y continuar").
  const stepInitializedRef = useRef(false);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  // Feature #11066 — estado informativo del paquete (FUR/certs/impronta). No bloquea Preparar.
  const [paqueteDocsStatus, setPaqueteDocsStatus] = useState<
    'idle' | 'loading' | 'ready' | 'error'
  >('idle');
  // HU #10646 — partes (NIT/jurídicas) cuya identidad quedó cubierta por la firma electrónica del baúl.
  // El backend no expone un flag "cubierto por baúl" por parte en el estado biométrico, así que la señal
  // se captura del outcome `firma_baul` que devuelve ensureIdentity al guardar la parte, y desde aquí se
  // propaga a BiometricStep para pintar el estado "cubierto por el baúl" (sin botones de biométrica).
  const [vaultCoveredPartes, setVaultCoveredPartes] = useState<BiometricParte[]>([]);
  const { show } = useToast();
  // Guardar+continuar de los pasos con form embebido (actores y comercial): la
  // shell dispara save() vía ref desde el footer "Guardar y continuar".
  const stepFormRef = useRef<WizardStepFormHandle>(null);
  const [continuing, setContinuing] = useState(false);
  /** Feature #11066 — cambios locales pendientes de Guardar (docs/forms). */
  const [hasUnsavedChanges, setHasUnsavedChanges] = useState(false);
  /**
   * Subsanación: se pone en true tras un Guardar y continuar exitoso (tras haber editado).
   * Habilita Re-radicar solo cuando además no hay dirty pendiente.
   */
  const [subsanacionSavedEdits, setSubsanacionSavedEdits] = useState(false);

  // Preflight local (semáforo) para los pasos consulta/validación.
  const [preflight, setPreflight] = useState<PreflightSnapshot | null>(null);
  const [preflightLoading, setPreflightLoading] = useState(false);

  const modalidad: WizardModalidad = wizard?.modalidad ?? entryModalidad ?? 'matricula_inicial';
  const activeStep: WizardStep | undefined = steps[activeIndex];

  // Reportes2 HU-A — telemetría de uso del wizard (fire-and-forget; emite
  // wizard_step_view al cambiar activeStep?.key y expone los demás eventos).
  const telemetry = useWizardTelemetry(instanceId, activeStep?.key);

  // Identidad aprobada (deriva del estado server-driven del paso): matrícula → paso 'identidad'
  // complete; traspaso → el paso 'fur' (que envuelve la biométrica) ya no reporta pendiente_biometria.
  // canRadicar gobierna el botón "Preparar" (N 03: borrador→preparado): el gate RF03 exige identidad,
  // mientras que canSubmit (matrícula) trata la identidad como diferida → no basta canSubmit.
  const identityApproved = isIdentityApproved(steps, modalidad);
  const canRadicar = canSubmit && identityApproved;

  // Header: por modalidad usamos `title`; legacy usa configuration.name; con
  // instancia existente derivamos la etiqueta de la modalidad server-driven.
  const headerTitle =
    title ??
    configuration?.name ??
    (modalidad === 'traspaso' ? 'Traspaso estándar' : 'Matrícula inicial');

  // AC1 (HU #10883) — autosave del paso: al AVANZAR (no al retroceder) persiste la `key` del paso
  // destino vía PATCH /instances/{id}/current-step (HU #10879), para retomar ahí al reabrir el
  // borrador (AC2). Fire-and-forget: el backend valida internamente (borrador + vehículo consultado,
  // HU #10879) y un 409/400 no debe bloquear la navegación del wizard — el autosave es best-effort.
  const persistCurrentStep = useCallback(
    (stepKey: string) => {
      if (!instanceId) return;
      void tramitesClient.setCurrentStep(instanceId, stepKey).catch(() => {
        // Silencioso a propósito: p.ej. aún no se consultó el vehículo (409) o el trámite ya no es
        // borrador. No es una acción explícita del usuario, así que no se interrumpe ni se avisa.
      });
    },
    [instanceId],
  );

  // Navegación en cascada: solo a pasos completos o a la frontera (primer
  // incompleto). No basta con que el paso no esté 'locked'.
  const goToStep = (index: number) => {
    if (!canNavigateToStep(steps, index, navViewOnly)) return;
    // Reportes2 HU-A — retroceso o salto de paso = wizard_step_exit con duración
    // de permanencia (el avance +1 con éxito lo reporta handleContinue como complete).
    if (index < activeIndex || index > activeIndex + 1) telemetry.trackStepExit();
    // AC1 (HU #10883) — solo se persiste al avanzar (index > activeIndex), no al retroceder ni al
    // reabrir un paso ya visitado: retroceder a revisar un paso no debe mover el punto de retoma.
    if (index > activeIndex && steps[index]) persistCurrentStep(steps[index].key);
    setActiveIndex(index);
  };

  // Reanudar en el paso donde quedó el usuario: cuando los pasos llegan del
  // server por primera vez para una instancia existente, posiciona el paso
  // activo en la frontera (primer incompleto; o el último si todo está
  // completo). Solo una vez (ref): en creación nueva la frontera es el paso 1,
  // y en refreshes posteriores no debe re-saltar.
  useEffect(() => {
    if (stepInitializedRef.current) return;
    if (steps.length === 0) return;
    stepInitializedRef.current = true;
    if (existingInstanceId) {
      // AC2 (HU #10883) — el paso persistido (autosave, HU #10879) PRIMA como punto de retoma: si el
      // wizard trae `persistedCurrentStep`, corresponde a un paso visible Y sigue siendo alcanzable por
      // la cascada (`canNavigateToStep`: por construcción el paso solo se persistió al avanzar, cuando
      // SÍ era navegable — ver `persistCurrentStep`/`goToStep`), arranca ahí. Se revalida la cascada aquí
      // (y no solo la existencia del paso) para no pelear con el efecto de corrección de abajo si algún
      // gate previo retrocedió entre la persistencia y la reapertura (p.ej. se eliminó un documento): en
      // ese caso cae al primer paso incompleto derivado de los gates (comportamiento previo, sin
      // regresión — el backend #10879 ya provee ese mismo fallback null-safe cuando no hay paso persistido).
      const persistedKey = wizard?.persistedCurrentStep;
      const persistedIndex = persistedKey ? steps.findIndex((s) => s.key === persistedKey) : -1;
      const persistedNavigable =
        persistedIndex !== -1 && canNavigateToStep(steps, persistedIndex, navViewOnly);
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setActiveIndex(persistedNavigable ? persistedIndex : frontierIndex(steps));
    }
  }, [steps, existingInstanceId, wizard?.persistedCurrentStep, navViewOnly]);

  // Tras refrescar el estado, si el paso activo dejó de ser navegable (p.ej. un
  // gate previo cambió y el flujo retrocedió), reubica en la frontera del flujo.
  // En solo lectura no hay edición que regrese gates, así que no reposiciona.
  useEffect(() => {
    if (navViewOnly) return;
    if (steps.length === 0) return;
    if (!canNavigateToStep(steps, activeIndex)) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setActiveIndex(frontierIndex(steps));
    }
  }, [steps, activeIndex, navViewOnly]);

  const runPreflight = async () => {
    if (!instanceId) return;
    setPreflightLoading(true);
    try {
      const snap = await tramitesClient.runPreflight(instanceId);
      setPreflight(snap);
      await refresh();
    } finally {
      setPreflightLoading(false);
    }
  };

  // Trae el último preflight al entrar a un paso que lo muestra.
  useEffect(() => {
    const key = activeStep?.key;
    if (!instanceId || !key) return;
    if (key === 'consulta' || key === 'consulta_vin') {
      tramitesClient
        .getPreflight(instanceId)
        .then((snap) => snap && setPreflight(snap))
        .catch(() => {});
    }
  }, [instanceId, activeStep?.key]);

  // Feature #11066 — genera FUR/impronta/consolidado sin bloquear la UI de negocio.
  // Reintenta ante fallos transitorios o consolidado incompleto (p.ej. FUR aún en vuelo).
  // Devuelve mensaje de error o null si el consolidado quedó completo.
  const ensureExpedienteDocs = useCallback(async (id: string): Promise<string | null> => {
    const maxAttempts = 3;
    // En tests, backoff mínimo para no alargar la suite.
    const retryMs =
      typeof process !== 'undefined' && process.env.NODE_ENV === 'test' ? 10 : 1_500;

    setPaqueteDocsStatus('loading');
    let lastError: string | null = null;

    for (let attempt = 1; attempt <= maxAttempts; attempt++) {
      try {
        try {
          await tramitesClient.generarFur(id);
        } catch {
          // Puede existir ya; el consolidado fallará si realmente falta.
        }
        try {
          await tramitesClient.generarImpronta(id);
        } catch {
          // Best-effort: sin impronta no bloquea el consolidado en backend (HU #11017).
        }
        const result = await tramitesClient.generarConsolidado(id, undefined, true);
        if (!result.incompleto) {
          setPaqueteDocsStatus('ready');
          return null;
        }
        const faltantes = (result.documentosFaltantes ?? []).filter(Boolean).join(', ');
        lastError = faltantes
          ? `Expediente incompleto: faltan ${faltantes}.`
          : 'Expediente incompleto: faltan documentos obligatorios.';
      } catch (genErr) {
        lastError =
          genErr instanceof Error
            ? genErr.message
            : 'No se pudieron generar los documentos del expediente.';
      }

      if (attempt < maxAttempts) {
        await new Promise((resolve) => setTimeout(resolve, retryMs * attempt));
      }
    }

    setPaqueteDocsStatus('error');
    return lastError ?? 'No se pudieron generar los documentos del expediente.';
  }, []);

  // N 03 (radicación en dos pasos) — Preparar: borrador→preparado vía POST /transition. El backend
  // valida el gate RF03 (identidad aprobada + documentos); solo se habilita cuando el wizard reporta
  // canSubmit Y la identidad está aprobada (ver `canRadicar`). El wizard permanece abierto: pasa a
  // solo lectura y ofrece "Radicar a tránsito".
  // Feature #11066 — la generación de FUR/paquete corre en segundo plano: NO bloquea la transición.
  const handlePreparar = async () => {
    if (!instanceId || !canRadicar) return;
    setSubmitting(true);
    setSubmitError(null);
    try {
      await tramitesClient.transitionInstance(instanceId, 'preparado');
      setInstanceStatus('preparado');
      show(
        'Trámite preparado. Generando expediente en segundo plano…',
        'success',
      );
      await refresh();

      const id = instanceId;
      void (async () => {
        const docsError = await ensureExpedienteDocs(id);
        if (docsError) {
          show(
            `Trámite preparado, pero faltó el expediente tras reintentos: ${docsError} Regenera antes de radicar.`,
            'error',
          );
        } else {
          show('Expediente listo (FUR y consolidado). Ya puedes radicar.', 'success');
        }
      })();
    } catch (err) {
      setSubmitError(
        err instanceof Error ? err.message : 'No se pudo preparar el trámite.',
      );
    } finally {
      setSubmitting(false);
    }
  };

  // N 03 (radicación en dos pasos) — Radicar a tránsito: preparado→entregado vía POST /transition
  // (los gates OT —organismo habilitado, reglas— los valida el backend en esta transición).
  // Feature #11066 — gate estricto de expediente: exige consolidado completo antes de entregar.
  // Sin pantalla intermedia: toast de éxito + volver al listado de inmediato (onExit redirige a
  // /tramites; el ToastProvider del layout no se desmonta).
  const handleRadicar = async () => {
    if (!instanceId || !canEntregar) return;
    setSubmitting(true);
    setSubmitError(null);
    try {
      const docsError = await ensureExpedienteDocs(instanceId);
      if (docsError) {
        setSubmitError(`No se puede radicar: ${docsError}`);
        setSubmitting(false);
        return;
      }

      await tramitesClient.transitionInstance(instanceId, 'entregado');
      // Reportes2 HU-A — trámite radicado desde el wizard: wizard_complete con duración total.
      telemetry.trackComplete();
      show(
        modalidad === 'traspaso'
          ? 'Traspaso enviado a tránsito correctamente.'
          : 'Matrícula inicial enviada a tránsito correctamente.',
        'success',
      );
      onExit();
    } catch (err) {
      setSubmitError(
        err instanceof Error ? err.message : 'Error al enviar el trámite',
      );
      setSubmitting(false);
    }
  };

  // Finalizar borrador (AC1): datos completos pero identidad aún pendiente. Sella draftFinalizedAt
  // SIN radicar; la firma se dispara async cuando el cliente valida su identidad. Distinto de submit.
  const handleFinalizeDraft = async () => {
    if (!instanceId || !canSubmit) return;
    setSubmitting(true);
    setSubmitError(null);
    try {
      await tramitesClient.finalizeDraft(instanceId);
      show(
        'Trámite guardado en borrador — pendiente validación del cliente.',
        'success',
      );
      onExit();
    } catch (err) {
      setSubmitError(
        err instanceof Error ? err.message : 'No se pudo finalizar el borrador.',
      );
      setSubmitting(false);
    }
  };

  // Paso de decisión terminal (HU #10350): el ÚLTIMO paso del wizard ('fur' en ambas modalidades),
  // donde el gestor finaliza el borrador o radica. El paso de Identidad ya NO es terminal: desde él se
  // "Continúa" al paso FUR (el FUR es alcanzable aunque la identidad esté pendiente — backend #10350).
  const isDecisionStep = activeStep?.key === 'fur';
  // Pasos con form embebido (actores y comercial): el footer "Continuar" guarda
  // y luego avanza, así que se habilita aunque el paso aún esté incomplete (el
  // save lo completa).
  const isSavableStep =
    activeStep?.key === 'comprador' ||
    activeStep?.key === 'vendedor' ||
    activeStep?.key === 'comercial';
  // El siguiente paso es navegable (no hay paso de datos incompleto por delante). Permite "Continuar"
  // desde un paso diferido incompleto (Identidad) hacia el FUR para finalizar/radicar.
  const nextStepNavigable = canNavigateToStep(steps, activeIndex + 1, navViewOnly);
  const continueDisabled =
    !activeStep ||
    activeIndex >= steps.length - 1 ||
    continuing ||
    // CF-02 — sin trámite creado, "Continuar" es justamente lo que lo crea: se habilita en cuanto la
    // consulta del vehículo salió bien (sin bloqueos), que es el único requisito del paso 1.
    (deferredCreation
      ? !pendingConsulta
      : !isSavableStep && activeStep.status !== 'complete' && !nextStepNavigable);

  // "Guardar y continuar" para pasos con form embebido: valida + persiste (vía
  // ref), refresca el wizard y avanza solo si el paso quedó complete. Otros
  // pasos: navegación directa al siguiente.
  const handleContinue = async () => {
    // CF-02 (HU #10883, AC4) — avance del paso 1 al paso 2 SIN trámite todavía: aquí y solo aquí
    // nace el registro, con el vehículo ya consultado. Si la creación falla no se avanza y no queda
    // nada persistido; el operador puede reintentar sobre la misma consulta.
    if (deferredCreation) {
      if (!pendingConsulta || !entryModalidad) return;
      setContinuing(true);
      setSubmitError(null);
      try {
        const created = await tramitesClient.createInstanceFromConsulta({
          modalidad: entryModalidad,
          vin: pendingConsulta.vin,
          plate: pendingConsulta.plate,
          ownerDocumentType: pendingConsulta.ownerDocumentType,
          ownerDocumentNumber: pendingConsulta.ownerDocumentNumber,
          previewToken: pendingConsulta.previewToken,
        });

        // Condiciones marcadas en el paso 1 (leasing, carrocería, paz y salvo, riesgo,
        // transformaciones): se persisten ahora, contra el trámite recién creado, para que el
        // resultado sea idéntico al del flujo anterior — donde cada casilla hacía su PATCH al
        // instante. Best-effort: un fallo aquí no debe atrapar al operador en el paso 1 (las
        // casillas siguen editables en el paso 1 del trámite ya creado).
        const extras = [...pendingFieldValuesRef.current.entries()].map(([fieldKey, valueText]) => ({
          formFieldId: null,
          fieldKey,
          valueText,
          valueJson: null,
        }));
        if (extras.length > 0) {
          try {
            await tramitesClient.patchFieldValues(created.instance.id, extras, created.instance.tenantId);
          } catch {
            // Silencioso: el trámite ya existe y el operador puede re-marcarlas en el paso 1.
          }
        }
        pendingFieldValuesRef.current.clear();

        telemetry.trackStepComplete();
        // AC1 (HU #10883) — el paso de retoma queda en el segundo paso, que es donde continúa el
        // operador. Best-effort, igual que el resto del autosave: no bloquea la navegación.
        const nextKey = steps[1]?.key;
        if (nextKey) {
          void tramitesClient
            .setCurrentStep(created.instance.id, nextKey, created.instance.tenantId)
            .catch(() => {});
        }
        onCreated?.(created.instance);
      } catch (err) {
        setSubmitError(
          err instanceof Error ? err.message : 'No se pudo crear el trámite. Reintenta.',
        );
      } finally {
        setContinuing(false);
      }
      return;
    }

    if (!isSavableStep) {
      // Reportes2 HU-A — avance con éxito desde un paso sin form embebido.
      if (canNavigateToStep(steps, activeIndex + 1, navViewOnly)) telemetry.trackStepComplete();
      goToStep(activeIndex + 1);
      // Subsanación: docs/checklist ya persisten al editar; Continuar confirma y habilita Re-radicar.
      if (inSubsanacion) {
        setHasUnsavedChanges(false);
        setSubsanacionSavedEdits(true);
        show('Cambios guardados. Ya puedes re-radicar cuando termines.', 'success');
      }
      return;
    }
    setContinuing(true);
    setSubmitError(null);
    try {
      const ok = await stepFormRef.current?.save();
      if (!ok) {
        setSubmitError('No se pudo guardar. Por favor, reintenta.');
        return;
      }

      // HU #10350 — al guardar la parte (comprador/vendedor), asegura su identidad sin esperar el clic
      // en "Validar identidad": el backend reutiliza una validación VIGENTE (≤30 días) de la persona;
      // si no hay vigente, se dispara la validación automáticamente (provider-aware: Kyverum envía el
      // enlace de captura; en mock se simula). No bloquea el avance si algo falla.
      const parteIdentidad: BiometricParte | null =
        activeStep?.key === 'comprador'
          ? 'comprador'
          : activeStep?.key === 'vendedor'
            ? 'vendedor'
            : null;
      if (parteIdentidad && instanceId) {
        try {
          const ensured = await tramitesClient.ensureIdentity(instanceId, parteIdentidad);
          // HU #10646 — actor jurídico (NIT) cubierto por la firma del baúl: NO se lanza biométrica.
          // El backend ya deja el paso de identidad completo y la parte aprobada; aquí solo registramos
          // la cobertura por baúl para que BiometricStep muestre el estado "cubierto por el baúl".
          if (ensured.outcome === 'firma_baul') {
            setVaultCoveredPartes((prev) =>
              prev.includes(parteIdentidad) ? prev : [...prev, parteIdentidad],
            );
          } else {
            // Si la parte deja de estar cubierta (p.ej. se reemplazó el NIT por una persona natural),
            // se limpia la marca para no arrastrar el estado del baúl de un guardado anterior.
            setVaultCoveredPartes((prev) => prev.filter((p) => p !== parteIdentidad));
            if (ensured.outcome === 'requiere_validacion') {
              const { provider } = await tramitesClient.getBiometricState(instanceId);
              if (provider === 'kyverum') {
                await tramitesClient.iniciarBiometric(instanceId, { parte: parteIdentidad });
              } else {
                await tramitesClient.simulateBiometric(instanceId, { parte: parteIdentidad });
              }
            }
          }
        } catch (ensureErr) {
          // No se traga en silencio (HU #10350): asegurar/iniciar la identidad falló. No bloquea el
          // avance —el gestor puede iniciarla manualmente en el paso de Identidad— pero SÍ se avisa para
          // que no continúe creyendo que la identidad quedó encaminada, y se deja traza para observabilidad.
          console.warn('[tramite-wizard] ensureIdentity falló', { instanceId, parte: parteIdentidad, error: ensureErr });
          show(
            'No se pudo iniciar automáticamente la validación de identidad. Continúa y, si es necesario, iníciala en el paso de Identidad.',
            'error',
          );
        }
      }

      const fresh = await refresh();
      if (fresh?.steps?.[activeIndex]?.status === 'complete') {
        // Reportes2 HU-A — guardado + avance con éxito = wizard_step_complete.
        telemetry.trackStepComplete();
        const nextIndex = Math.min(activeIndex + 1, steps.length - 1);
        // AC1 (HU #10883) — mismo autosave de `goToStep`, pero este avance mueve `activeIndex`
        // directamente (no pasa por `goToStep`) porque primero guarda el formulario embebido.
        if (nextIndex > activeIndex && steps[nextIndex]) persistCurrentStep(steps[nextIndex].key);
        setActiveIndex(nextIndex);
      }
      setHasUnsavedChanges(false);
      if (inSubsanacion) {
        setSubsanacionSavedEdits(true);
        show('Cambios guardados. Ya puedes re-radicar cuando termines.', 'success');
      } else {
        show('Cambios guardados en el borrador.', 'success');
      }
    } catch (err) {
      setSubmitError(
        err instanceof Error ? err.message : 'No se pudo guardar. Por favor, reintenta.',
      );
    } finally {
      setContinuing(false);
    }
  };

  return (
   <WizardReadOnlyProvider readOnly={editLocked}>
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between shrink-0">
        <div>
          <h1 className="text-xl font-bold">{headerTitle}</h1>
          {wizard && (
            <p className="text-[11px] opacity-60 mt-0.5">
              {modalidad === 'traspaso' ? 'Traspaso' : 'Matrícula inicial'} ·{' '}
              {steps.length} pasos
            </p>
          )}
        </div>
        <button
          onClick={() => {
            // Reportes2 HU-A — salida explícita sin radicar = wizard_abandon
            // (en solo visualización el trámite ya se radicó: no es abandono).
            if (!fullReadOnly) telemetry.trackAbandon();
            onExit();
          }}
          className="text-xs opacity-70 hover:opacity-100"
          aria-label={editLocked ? 'Volver al listado' : 'Cancelar y volver al selector'}
        >
          {editLocked ? '← Volver al listado' : '← Cancelar'}
        </button>
      </div>

      {fullReadOnly && (
        <div
          className="rounded-xl p-3 text-xs border shrink-0 flex items-start gap-2"
          style={{ borderColor: '#557EFF', background: 'rgba(85,126,255,0.06)', color: '#162744' }}
          role="status"
          aria-live="polite"
        >
          <Eye className="h-4 w-4 shrink-0 mt-0.5" style={{ color: '#557EFF' }} aria-hidden="true" />
          <span>
            <span className="font-semibold" style={{ color: '#557EFF' }}>
              Enviado a tránsito — solo visualización.
            </span>{' '}
            Este trámite ya no puede editarse, pero aún puedes generar o
            descargar el FUR y el expediente consolidado.
          </span>
        </div>
      )}

      {draftFinalized && (
        <div
          className="rounded-xl p-3 text-xs border shrink-0 flex items-start gap-2"
          style={{ borderColor: '#F9AC00', background: 'rgba(249,172,0,0.08)', color: '#162744' }}
          role="status"
          aria-live="polite"
        >
          <Shield className="h-4 w-4 shrink-0 mt-0.5" style={{ color: '#F9AC00' }} aria-hidden="true" />
          <span>
            <span className="font-semibold" style={{ color: '#B45309' }}>
              Borrador finalizado — esperando validación del cliente.
            </span>{' '}
            Los datos quedaron en solo lectura. Puedes iniciar o compartir la validación de identidad;
            al aprobarse podrás radicar a tránsito. La firma de compraventa es informativa y no
            bloquea la radicación (HU #10661).
          </span>
        </div>
      )}

      {/* HU #10874 (AC1/AC2) — panel de subsanación: motivo + checklist de ítems a subsanar y la
          acción "Re-radicar". El trámite sigue editable (campos/documentos) mientras se muestra. */}
      {inSubsanacion && (
        <SubsanacionPanel
          instanceId={instanceId}
          statusHistory={statusHistory}
          loading={instanceDetailLoading}
          error={instanceDetailError}
          hasUnsavedChanges={hasUnsavedChanges}
          canReradicar={subsanacionSavedEdits && !hasUnsavedChanges}
          showCancel={estadoTramite === 'rechazado' && !!wizard?.subsanacionActiva}
          onCancelSubsanacion={async () => {
            if (!instanceId) return;
            await tramitesClient.cancelSubsanacion(instanceId);
            setHasUnsavedChanges(false);
            setSubsanacionSavedEdits(false);
            show('Subsanación cancelada. El trámite sigue rechazado.', 'success');
            await refresh();
            onExit();
          }}
          onReradicado={() => {
            telemetry.trackComplete();
            show('Trámite re-radicado a tránsito correctamente.', 'success');
            onExit();
          }}
        />
      )}

      {(wizardError || submitError || state.error) && (
        <div
          className="rounded-xl p-3 text-xs border shrink-0"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          {wizardError ?? submitError ?? state.error}
        </div>
      )}

      {/* AC2 #10498: columnas niveladas (items-stretch) y scroll SOLO en la lista de
          pasos cuando excede el alto disponible; ambos contenedores quedan a la par abajo. */}
      <div className="grid grid-cols-12 gap-4 items-start md:items-stretch">
        {/* Sidebar de pasos server-driven. */}
        <aside
          className="col-span-12 md:col-span-3 rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border flex flex-col min-h-0 md:max-h-[calc(100vh-120px)]"
        >
          <p className="text-[10px] font-semibold uppercase opacity-60 mb-3 shrink-0">
            Asistente de seguimiento
          </p>
          {steps.length === 0 ? (
            <p className="text-[11px] opacity-60">
              {wizardLoading ? 'Cargando pasos…' : 'Sin pasos disponibles.'}
            </p>
          ) : (
            <ol className="space-y-3 flex-1 min-h-0 overflow-y-auto">
              {steps.map((s, i) => {
                const isActive = i === activeIndex;
                const clickable = canNavigateToStep(steps, i, navViewOnly);
                return (
                  <li key={s.key} aria-current={isActive ? 'step' : undefined}>
                    <button
                      type="button"
                      onClick={() => goToStep(i)}
                      disabled={!clickable}
                      className="w-full flex items-start gap-3 text-left disabled:cursor-not-allowed"
                      aria-label={`Paso ${i + 1}: ${s.label} (${s.status})`}
                    >
                      <StepMarker status={s.status} index={i} />
                      <span className="min-w-0 flex-1">
                        <span
                          className={`block text-xs ${isActive ? 'font-bold' : s.status === 'locked' ? 'opacity-50' : 'opacity-80'}`}
                        >
                          {s.label}
                        </span>
                        {s.status === 'incomplete' && s.reasons.length > 0 && (
                          <span className="mt-1 block space-y-0.5">
                            {s.reasons.map((r) => (
                              <span
                                key={r}
                                className="block text-[10px]"
                                style={{ color: '#F9AC00' }}
                              >
                                • {reasonCopy(r)}
                              </span>
                            ))}
                          </span>
                        )}
                      </span>
                    </button>
                  </li>
                );
              })}
            </ol>
          )}
        </aside>

        {/* Cuerpo del paso activo. */}
        <section
          className="col-span-12 md:col-span-9 rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border"
        >
          {!activeStep ? (
            <p className="text-xs opacity-60">
              {wizardLoading ? 'Cargando el asistente…' : 'Este flujo no tiene pasos.'}
            </p>
          ) : (
            <div className="space-y-6">
              <div>
                <h2 className="text-base font-bold">{activeStep.label}</h2>
                {STEP_SUBTITLE[activeStep.key] && (
                  <p className="mt-1 text-xs opacity-60">
                    {STEP_SUBTITLE[activeStep.key]}
                  </p>
                )}
              </div>
              <StepBody
                step={activeStep}
                modalidad={modalidad}
                instanceId={instanceId}
                instanceStatus={estadoTramite}
                preflight={preflight}
                preflightLoading={preflightLoading}
                onRunPreflight={runPreflight}
                onRefresh={() => void refresh()}
                stepFormRef={stepFormRef}
                identityOperable={draftFinalized}
                identityApproved={identityApproved}
                vaultCoveredPartes={vaultCoveredPartes}
                rnmcEnabled={wizard?.rnmcEnabled ?? false}
                deferredModalidad={deferredCreation ? entryModalidad : undefined}
                seedVin={seedVin}
                seedPlaca={seedPlaca}
                onPreviewDone={setPendingConsulta}
                onPendingFieldValues={collectPendingFieldValues}
                paqueteDocsStatus={paqueteDocsStatus}
                onPaqueteStatusChange={setPaqueteDocsStatus}
                onMarkDirty={() => setHasUnsavedChanges(true)}
              />
            </div>
          )}

          {/* Bloqueos de envío traducidos (en el paso de decisión). */}
          {isDecisionStep && blockers.length > 0 && (
            <div
              className="mt-6 rounded-xl p-3 border text-xs"
              style={{ borderColor: '#F9AC00', background: 'rgba(249,172,0,0.08)' }}
              role="status"
              aria-live="polite"
            >
              <p className="font-semibold mb-1" style={{ color: '#F9AC00' }}>
                Antes de enviar, resuelve:
              </p>
              <ul className="space-y-0.5" aria-label="Bloqueos de envío">
                {blockers.map((b) => (
                  <li key={b} style={{ color: '#F9AC00' }}>
                    • {blockerCopy(b)}
                  </li>
                ))}
              </ul>
            </div>
          )}

          <div
            className="flex flex-wrap gap-2 items-center justify-between mt-6 pt-4 border-t"
          >
            <button
              onClick={() => goToStep(Math.max(0, activeIndex - 1))}
              disabled={activeIndex === 0}
              className="flex items-center gap-1 px-4 py-2 rounded-xl text-xs font-medium border disabled:opacity-30"
              style={{ borderColor: '#162744', color: '#162744' }}
            >
              <ChevronLeft className="h-3 w-3" /> Anterior
            </button>
            {/* Acción derecha del footer según el modo (HU #10350 + N 03 dos pasos):
                · Preparado: "Radicar a tránsito" (preparado→entregado) en el paso de decisión.
                · Solo visualización (otros estados no editables): sin acciones, solo se recorre.
                · Paso de decisión (identidad/FUR) en borrador: "Preparar" (borrador→preparado) si la
                  identidad ya está aprobada (canRadicar); si no, "Finalizar" (finalize-draft) cuando
                  los datos están completos. En borrador ya finalizado solo se ofrece "Preparar"
                  (deshabilitado hasta que el cliente valide su identidad).
                · Pasos de datos: editable usa "Guardar y continuar" (sin Guardar aparte), igual
                  en borrador y en subsanación. */}
            {fullReadOnly ? (
              isDecisionStep && canEntregar ? (
                <button
                  onClick={() => void handleRadicar()}
                  disabled={submitting}
                  className="px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
                  style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
                  title="Entrega el trámite al organismo de tránsito"
                >
                  {submitting ? 'Radicando…' : 'Radicar a tránsito'}
                </button>
              ) : null
            ) : isDecisionStep ? (
              // HU #10874 — en subsanación NO Preparar/Finalizar: re-radicar vive en SubsanacionPanel.
              // En el último paso de subsanación: "Guardar y continuar" habilita Re-radicar.
              inSubsanacion ? (
                <button
                  type="button"
                  onClick={() => {
                    void (async () => {
                      setContinuing(true);
                      try {
                        if (stepFormRef.current?.save) {
                          const ok = await stepFormRef.current.save();
                          if (!ok) {
                            setSubmitError('No se pudo guardar. Por favor, reintenta.');
                            return;
                          }
                          await refresh();
                        }
                        setHasUnsavedChanges(false);
                        setSubsanacionSavedEdits(true);
                        show(
                          'Cambios guardados. Ya puedes re-radicar cuando termines.',
                          'success',
                        );
                      } finally {
                        setContinuing(false);
                      }
                    })();
                  }}
                  disabled={continuing}
                  className="flex items-center gap-1 px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
                  style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
                  title="Guarda los cambios de este paso y habilita Re-radicar"
                >
                  {continuing ? 'Guardando…' : 'Guardar y continuar'}
                </button>
              ) : canRadicar ? (
                <button
                  onClick={() => void handlePreparar()}
                  disabled={submitting}
                  className="px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
                  style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
                  title="Deja el trámite validado y listo para radicar (la generación de documentos no bloquea)"
                >
                  {submitting ? 'Preparando…' : 'Preparar'}
                </button>
              ) : draftFinalized ? (
                <button
                  onClick={() => void handlePreparar()}
                  disabled
                  className="px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
                  style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
                  title="Disponible cuando el cliente valide su identidad"
                >
                  Preparar
                </button>
              ) : (
                <button
                  onClick={() => void handleFinalizeDraft()}
                  disabled={!canSubmit || submitting}
                  className="px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
                  style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
                >
                  {submitting ? 'Finalizando…' : 'Finalizar'}
                </button>
              )
            ) : draftFinalized ? (
              <button
                onClick={() => goToStep(activeIndex + 1)}
                disabled={!canNavigateToStep(steps, activeIndex + 1, navViewOnly)}
                className="flex items-center gap-1 px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
                style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
              >
                Continuar
                <ChevronRight className="h-3 w-3" />
              </button>
            ) : (
              <button
                onClick={() => void handleContinue()}
                disabled={continueDisabled}
                className="flex items-center gap-1 px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
                style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
              >
                {continuing
                  ? 'Guardando…'
                  : inSubsanacion || isSavableStep
                    ? 'Guardar y continuar'
                    : 'Continuar'}
                <ChevronRight className="h-3 w-3" />
              </button>
            )}
          </div>
        </section>
      </div>
    </div>
   </WizardReadOnlyProvider>
  );
}

const DOC_TYPES: ActorDocumentType[] = ['CC', 'CE', 'NIT', 'PAS'];

/**
 * Atributos de detalle del vehículo (los que NO van en el hero placa/marca/línea/
 * modelo). Cada uno con su icono para una grilla legible. Origen RUNT, hidratados
 * en field_values por la consulta del preflight. Solo se pinta lo presente.
 */
const VEHICLE_DETAILS: { key: string; label: string; icon: typeof Car }[] = [
  { key: 'vin', label: 'VIN', icon: Hash },
  { key: 'vehicle_color', label: 'Color', icon: Palette },
  { key: 'vehicle_class', label: 'Clase', icon: Tag },
  { key: 'vehicle_service', label: 'Servicio', icon: Briefcase },
  { key: 'vehicle_fuel', label: 'Combustible', icon: Fuel },
  { key: 'vehicle_engine_displacement', label: 'Cilindraje', icon: Gauge },
  { key: 'vehicle_body_type', label: 'Carrocería', icon: Layers },
  { key: 'vehicle_engine_number', label: 'Nº Motor', icon: Wrench },
  { key: 'vehicle_chassis', label: 'Nº Chasis', icon: Hash },
  { key: 'vehicle_series', label: 'Nº Serie', icon: Hash },
  { key: 'vehicle_passengers', label: 'Pasajeros', icon: Users },
  { key: 'vehicle_registration_date', label: 'Fecha matrícula', icon: Calendar },
  { key: 'transit_office_name', label: 'Organismo de tránsito', icon: Building2 },
];

/**
 * Tarjeta "Datos del vehículo · RUNT". Lee los field_values frescos de la
 * instancia (hidratados por la consulta del preflight) y los presenta con un
 * hero (placa + marca/línea/modelo + estado) y una grilla de atributos con
 * iconos. Solo se pinta lo presente; nada de proveedor.
 */
function VehicleDataCard({ fieldValues }: { fieldValues: FieldValue[] }) {
  const byKey = (key: string) =>
    fieldValues.find((f) => f.fieldKey === key)?.valueText?.trim() ?? '';

  const plate = byKey('plate');
  const vin = byKey('vin');
  const brand = byKey('vehicle_brand');
  const line = byKey('vehicle_line');
  const year = byKey('vehicle_year');
  const estado = byKey('vehicle_state');
  const soatVencimiento = byKey('soat_vencimiento');
  const soatAseguradora = byKey('soat_aseguradora');
  const rtmVencimiento = byKey('rtm_vencimiento');

  // Antes de consultar no hay datos del vehículo → no renderiza.
  const hasAny = [plate, vin, brand, line, year].some((v) => v !== '');
  if (!hasAny) return null;

  const title = [brand, line].filter((v) => v !== '').join(' ') || 'Vehículo';
  const estadoActivo = /activo/i.test(estado);

  const details = VEHICLE_DETAILS.map((d) => ({ ...d, value: byKey(d.key) })).filter(
    (d) => d.value !== '',
  );

  const hasSoatRtm = soatVencimiento || soatAseguradora || rtmVencimiento;

  return (
    <div
      className="overflow-hidden rounded-2xl border bg-white dark:bg-[#0B0F14]"
    >
      {/* Header */}
      <div
        className="flex items-center justify-between gap-3 border-b px-4 py-3"
      >
        <div className="flex items-center gap-2">
          <span
            className="grid h-7 w-7 place-items-center rounded-lg"
            style={{ background: 'rgba(85,126,255,0.10)' }}
          >
            <Car className="h-4 w-4" style={{ color: '#557EFF' }} />
          </span>
          <h4 className="text-sm font-bold">Datos del vehículo</h4>
        </div>
        <span
          className="rounded px-1.5 py-0.5 text-[9px] font-semibold uppercase"
          style={{ background: 'rgba(85,126,255,0.10)', color: '#557EFF' }}
        >
          RUNT
        </span>
      </div>

      {/* Hero: placa + marca/línea/modelo + estado */}
      <div className="flex flex-wrap items-center gap-4 px-4 py-4">
        {plate && (
          <div
            className="rounded-xl border-2 px-4 py-2 text-center"
            style={{ borderColor: '#557EFF', background: 'rgba(85,126,255,0.06)' }}
          >
            <p className="text-[9px] font-semibold uppercase opacity-50">Placa</p>
            <p
              className="font-mono text-lg font-extrabold tracking-widest"
              style={{ color: '#557EFF' }}
            >
              {plate}
            </p>
          </div>
        )}
        <div className="min-w-0 flex-1">
          <p className="truncate text-base font-bold">{title}</p>
          <div className="mt-1 flex flex-wrap items-center gap-2">
            {year && (
              <span className="inline-flex items-center gap-1 text-[11px] opacity-70">
                <Calendar className="h-3 w-3" /> Modelo {year}
              </span>
            )}
            {estado && (
              <span
                className="rounded-full px-2 py-0.5 text-[10px] font-bold"
                style={
                  estadoActivo
                    ? { background: 'rgba(140,198,63,0.15)', color: '#8CC63F' }
                    : { background: 'rgba(154,165,177,0.15)', color: '#9AA5B1' }
                }
              >
                {estado}
              </span>
            )}
          </div>
        </div>
      </div>

      {/* Grilla de atributos */}
      {details.length > 0 && (
        <div
          className="grid gap-px border-t sm:grid-cols-2"
          style={{ background: '#DFE5ED' }}
        >
          {details.map((d) => {
            const Icon = d.icon;
            return (
              <div
                key={d.key}
                className="flex items-center gap-2.5 bg-white px-4 py-2.5 dark:bg-[#0B0F14]"
              >
                <Icon className="h-3.5 w-3.5 shrink-0 opacity-40" />
                <div className="min-w-0 flex-1">
                  <p className="text-[10px] uppercase opacity-50">{d.label}</p>
                  <p className="truncate text-xs font-semibold">{d.value}</p>
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* Sección SOAT / RTM */}
      {hasSoatRtm && (
        <div
          className="border-t px-4 py-3"
        >
          <p className="mb-2 text-[10px] font-semibold uppercase opacity-50">
            Documentos del vehículo
          </p>
          <div className="flex flex-wrap gap-3">
            {(soatVencimiento || soatAseguradora) && (
              <div
                className="flex min-w-0 items-start gap-2 rounded-xl border px-3 py-2"
              >
                <Shield className="mt-0.5 h-3.5 w-3.5 shrink-0" style={{ color: '#557EFF' }} />
                <div className="min-w-0">
                  <p className="text-[10px] font-bold uppercase" style={{ color: '#557EFF' }}>
                    SOAT
                  </p>
                  {soatVencimiento && (
                    <p className="text-[11px] font-semibold">Vence: {soatVencimiento}</p>
                  )}
                  {soatAseguradora && (
                    <p className="truncate text-[10px] opacity-60">{soatAseguradora}</p>
                  )}
                </div>
              </div>
            )}
            {rtmVencimiento && (
              <div
                className="flex min-w-0 items-start gap-2 rounded-xl border px-3 py-2"
              >
                <Wrench className="mt-0.5 h-3.5 w-3.5 shrink-0" style={{ color: '#8CC63F' }} />
                <div className="min-w-0">
                  <p className="text-[10px] font-bold uppercase" style={{ color: '#8CC63F' }}>
                    Tecno-mecánica
                  </p>
                  <p className="text-[11px] font-semibold">Vence: {rtmVencimiento}</p>
                </div>
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

// AC1/AC2 (HU #10884) — copy UX por `vehicleStatus` del bloqueo 422 VEHICLE_STATE_INVALID_FOR_TYPE
// (CF-03, HU #10877): distingue "ya matriculado" (ACTIVO en RUNT | APROBADO_FLIT en FLIT, AC1) de
// "RUNT sin dato" (DESCONOCIDO, AC2). Fallback genérico ante un vehicleStatus futuro no mapeado.
const VEHICLE_STATE_BLOCK_COPY: Record<string, string> = {
  ACTIVO:
    'El vehículo ya se encuentra matriculado según el RUNT. No es posible continuar con este trámite.',
  APROBADO_FLIT:
    'Este vehículo ya cuenta con una matrícula aprobada. No es posible continuar con este trámite.',
  DESCONOCIDO:
    'No fue posible confirmar el estado del vehículo en el RUNT. No es posible continuar hasta poder verificarlo; vuelve a intentarlo en unos minutos.',
};

function vehicleStateBlockMessage(vehicleStatus: string): string {
  return (
    VEHICLE_STATE_BLOCK_COPY[vehicleStatus] ??
    'No fue posible validar el estado del vehículo. No es posible continuar con este trámite.'
  );
}

/**
 * Paso de consulta inicial. Captura el identificador del vehículo
 * (VIN en matrícula; placa + propietario en traspaso) y, al consultar,
 * PERSISTE los field_values vía PATCH ANTES de correr el preflight, para que
 * el backend tenga el identificador al consultar RUNT (DS-4B-1). Rehidrata
 * los inputs desde la instancia si ya tiene valores guardados.
 */
function ConsultaStep({
  step,
  instanceId,
  preflight,
  preflightLoading,
  onRunPreflight,
  onRefresh,
  deferredModalidad,
  seedVin,
  seedPlaca,
  onPreviewDone,
  onPendingFieldValues,
}: {
  step: WizardStep;
  instanceId: string | null;
  preflight: PreflightSnapshot | null;
  preflightLoading: boolean;
  onRunPreflight: () => Promise<void>;
  onRefresh: () => void;
  /** CF-02 — modalidad en curso cuando el trámite aún no existe: la consulta va contra el preview. */
  deferredModalidad?: WizardModalidad;
  seedVin?: string;
  seedPlaca?: string;
  onPreviewDone?: (consulta: PendingConsulta | null) => void;
  /** CF-02 — condiciones marcadas antes de existir el trámite; el shell las guarda al crearlo. */
  onPendingFieldValues?: (items: { fieldKey: string; valueText: string }[]) => void;
}) {
  const isVin = step.key === 'consulta_vin';
  // CF-02 (HU #10883, AC3) — sin trámite creado: la consulta no persiste nada y sus resultados viven
  // en este componente hasta que "Continuar" cree el registro. El paso ofrece EXACTAMENTE los mismos
  // controles que antes; lo único que cambia es que su guardado se difiere a la creación.
  const deferred = !!deferredModalidad && !instanceId;
  const readOnly = useWizardReadOnly();
  const router = useRouter();
  // Confirmación de paz y salvo de impuesto (traspaso, paso 1): se ofrece cuando el
  // preflight no pudo verificar el impuesto vehicular (check 'impuesto' en unknown/warn).
  const [pazSalvoSaving, setPazSalvoSaving] = useState(false);
  const [riesgoSaving, setRiesgoSaving] = useState(false);
  // Banderas manuales del vehículo (leasing / cambio de carrocería / acción de prenda) que
  // alimentan el motor de reglas condicionales del checklist (RF33/37/38) vía field_values.
  const [atributosSaving, setAtributosSaving] = useState(false);

  // R3 (HU #10539) — el CTA "Iniciar traspaso" siembra el vehículo por query param. Con la creación
  // diferida ya no hay instancia donde persistirlo: simplemente prellena el input del paso 1.
  const [vin, setVin] = useState(seedVin ?? '');
  const [plate, setPlate] = useState(seedPlaca ?? '');
  const [ownerDocType, setOwnerDocType] = useState<ActorDocumentType>('CC');
  const [ownerDocNumber, setOwnerDocNumber] = useState('');
  const [persisting, setPersisting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // AC1 (HU #10882) — id del trámite existente cuando el preflight bloquea por duplicidad (409
  // DUPLICATE_ACTIVE_PROCEDURE, HU #10876). Presente ⇒ se ofrece "Retomar" (AC2) en vez del error genérico.
  const [duplicateInstanceId, setDuplicateInstanceId] = useState<string | null>(null);
  // AC1/AC2 (HU #10884) — detalle del bloqueo DURO "vehículo ya matriculado" (422
  // VEHICLE_STATE_INVALID_FOR_TYPE, CF-03 de HU #10877). Presente ⇒ banner de bloqueo no
  // subsanable (sin acción de continuar): el preflight no se persiste, así que el paso nunca
  // queda 'complete' y el avance del wizard permanece bloqueado (mismo mecanismo que AC1/#10882).
  const [vehicleStateBlock, setVehicleStateBlock] = useState<VehicleStateBlockInfo | null>(null);
  // field_values frescos de la instancia: rehidratan inputs y alimentan la
  // tarjeta "Datos del vehículo · RUNT" tras la consulta.
  const [fieldValues, setFieldValues] = useState<FieldValue[]>([]);
  // CF-02 — snapshot de la consulta desacoplada (sin instancia que lo persista todavía).
  const [previewSnapshot, setPreviewSnapshot] = useState<PreflightSnapshot | null>(null);
  // HU #10478 — proveedor de consulta por placa resuelto para el tenant. Con Kyverum RUNT NO se pide
  // el tipo de documento del propietario (lo resuelve el RUNT y lo devuelve); con Verifik sí se necesita.
  // null = aún sin resolver ⇒ se muestra el campo (default seguro para no ocultarlo con Verifik).
  const [platePrimaryProvider, setPlatePrimaryProvider] = useState<string | null>(null);
  // HU #10478 (novedad maquinaria/remolques) — con Kyverum como primario el tipo de documento del
  // propietario se oculta (Kyverum lo resuelve por placa). Pero maquinaria y remolques NO están en el RUNT
  // de Kyverum: el backend cae a Verifik, que SÍ exige el tipo real del dueño para hallar el vehículo. Si la
  // consulta devuelve "vehículo no encontrado", este flag revela el selector para corregir el tipo (p. ej. NIT).
  const [ownerDocTypeSuggested, setOwnerDocTypeSuggested] = useState(false);
  const hideOwnerDocType =
    !isVin && platePrimaryProvider === 'kyverum_runt' && !ownerDocTypeSuggested;

  // FEATURE 02 — política "solo vehículos propios" del tenant y NIT de la compañía (del JWT). Cuando la
  // política está activa, en traspaso se autorrellena el documento del propietario con el NIT del tenant
  // y, si el gestor lo edita a otro, se bloquea la consulta al RUNT con un mensaje claro.
  const [onlyOwnVehicles, setOnlyOwnVehicles] = useState(false);
  const tenantNitDigits = normalizeNitDigits(
    (decodeJwtPayload(getToken())?.company_nit as string | undefined) ?? '',
  );
  const ownershipAutofilled = useRef(false);

  // Carga (o recarga) la instancia y rehidrata inputs + field_values.
  const loadInstance = async () => {
    if (!instanceId) return;
    const detail = await tramitesClient.getInstance(instanceId);
    if (!detail?.fieldValues) return;
    setFieldValues(detail.fieldValues);
    const byKey = (key: string) =>
      detail.fieldValues.find((f) => f.fieldKey === key)?.valueText ?? '';
    setVin((v) => v || byKey('vin'));
    setPlate((v) => v || byKey('plate'));
    setOwnerDocNumber((v) => v || byKey('owner_document_number'));
    const docType = byKey('owner_document_type');
    if (docType && DOC_TYPES.includes(docType as ActorDocumentType)) {
      setOwnerDocType(docType as ActorDocumentType);
    }
  };

  // Rehidrata los inputs desde los field_values guardados de la instancia.
  useEffect(() => {
    if (!instanceId) return;
    // Rehidratación al montar: los setState ocurren tras el await (no síncronos).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadInstance().catch(() => {});
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [instanceId]);

  // HU #10478 — resuelve el proveedor de consulta por placa del tenant (solo traspaso) para decidir si
  // se pide el tipo de documento del propietario. Silencioso ante fallo: deja el campo visible.
  useEffect(() => {
    if (isVin) return;
    void tramitesClient
      .getConsultationConfig()
      .then((cfg) => {
        setPlatePrimaryProvider(cfg.vehiclePlate);
        // FEATURE 02 — flag para adaptar la captura del propietario en traspaso.
        setOnlyOwnVehicles(cfg.onlyOwnVehicles);
      })
      .catch(() => {});
  }, [isVin]);

  // FEATURE 02 — autorrelleno del documento del tenant (NIT) cuando la política está activa. Una sola
  // vez y solo si el campo está vacío: no pisa lo hidratado de la instancia ni lo que el gestor escriba.
  useEffect(() => {
    if (isVin || !onlyOwnVehicles || !tenantNitDigits || ownershipAutofilled.current) return;
    ownershipAutofilled.current = true;
    if (ownerDocNumber.trim()) return;
    // Autorrelleno del NIT del tenant: set state en efecto, misma excepción aceptada en el wizard
    // que la rehidratación de la instancia (el valor viene de una consulta async, no del render).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setOwnerDocType('NIT');
    setOwnerDocNumber(tenantNitDigits);
  }, [isVin, onlyOwnVehicles, tenantNitDigits, ownerDocNumber]);

  // HU #10478 (novedad maquinaria/remolques) — revela el selector de tipo de documento del propietario
  // cuando una consulta por placa (Kyverum primario) devuelve "vehículo no encontrado" (check 'vehiculo' en
  // fail). Probablemente es maquinaria/remolque, fuera del RUNT de Kyverum; el fallback a Verifik solo lo
  // halla con el tipo correcto del dueño (p. ej. NIT). Sticky: una vez revelado, permanece visible.
  useEffect(() => {
    if (isVin || platePrimaryProvider !== 'kyverum_runt' || ownerDocTypeSuggested) return;
    const pf = deferred ? previewSnapshot : preflight;
    const noEncontrado = (pf?.checks ?? []).some(
      (c) => c.key === 'vehiculo' && c.status === 'fail',
    );
    // Set state en efecto: misma excepción aceptada arriba (el valor deriva de una consulta
    // async — checks del preflight/preview —, no del render). Sticky por el guard de arriba.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (noEncontrado) setOwnerDocTypeSuggested(true);
  }, [isVin, platePrimaryProvider, ownerDocTypeSuggested, deferred, previewSnapshot, preflight]);

  const buildItems = (): FieldValueInput[] | null => {
    if (isVin) {
      const value = vin.trim();
      if (!value) return null;
      return [{ formFieldId: null, fieldKey: 'vin', valueText: value, valueJson: null }];
    }
    const plateValue = plate.trim();
    const docNumber = ownerDocNumber.trim();
    if (!plateValue || !docNumber) return null;
    // owner_document_type SIEMPRE viaja en el payload aunque el campo esté oculto (Kyverum): Kyverum
    // lo ignora (resuelve el tipo por la placa), pero el FALLBACK a Verifik SÍ lo exige para consultar
    // por placa (HU #10478). Por defecto 'CC'; tras un primer éxito de Kyverum, el preflight lo hidrata
    // al tipo real (tipoDocPropietario), así el fallback posterior queda correcto. Ocultarlo de la UI
    // no debe vaciar el dato o el fallback devolvería "requiere documento" (unknown) y enmascararía el
    // fallo como pre-vuelo verde.
    return [
      { formFieldId: null, fieldKey: 'plate', valueText: plateValue, valueJson: null },
      {
        formFieldId: null,
        fieldKey: 'owner_document_type',
        valueText: ownerDocType,
        valueJson: null,
      },
      {
        formFieldId: null,
        fieldKey: 'owner_document_number',
        valueText: docNumber,
        valueJson: null,
      },
    ];
  };

  const handleRun = async () => {
    if (!instanceId && !deferred) return;
    const items = buildItems();
    if (!items) {
      setError(
        isVin
          ? 'Ingresa el VIN antes de consultar.'
          : 'Ingresa la placa y el documento del propietario antes de consultar.',
      );
      return;
    }
    // FEATURE 02 — "solo vehículos propios": en traspaso, si el documento del propietario no es el NIT
    // del tenant, se bloquea ANTES de consultar el RUNT con un mensaje claro (no se gasta la consulta).
    if (!isVin && onlyOwnVehicles &&
        !isTenantOwnDocument(ownerDocType, ownerDocNumber.trim(), tenantNitDigits)) {
      setError(OWNER_NOT_TENANT_MESSAGE);
      return;
    }
    // Validación de formato antes de gastar una consulta al RUNT.
    const formatError = isVin
      ? validateVin(vin.trim())
      : (validatePlate(plate.trim()) ?? validateDocNumber(ownerDocNumber.trim(), ownerDocType));
    if (formatError) {
      setError(formatError);
      return;
    }
    setError(null);
    setDuplicateInstanceId(null);
    setVehicleStateBlock(null);
    setPersisting(true);
    try {
      // CF-02 (HU #10879, AC3) — sin trámite creado la consulta va al preview desacoplado: mismos
      // checks y mismos bloqueos, pero no se persiste NADA. El token queda a la espera de "Continuar",
      // que es lo que crea el registro reusando esta misma consulta.
      if (deferred) {
        const result = await tramitesClient.runPreflightPreview({
          modalidad: deferredModalidad!,
          vin: isVin ? vin.trim() : null,
          plate: isVin ? null : plate.trim(),
          ownerDocumentType: isVin ? null : ownerDocType,
          ownerDocumentNumber: isVin ? null : ownerDocNumber.trim(),
        });
        setPreviewSnapshot(result.preflight);
        setFieldValues(result.vehicleFields);
        onPreviewDone?.({
          previewToken: result.previewToken,
          vin: isVin ? vin.trim() : undefined,
          plate: isVin ? undefined : plate.trim(),
          ownerDocumentType: isVin ? undefined : ownerDocType,
          ownerDocumentNumber: isVin ? undefined : ownerDocNumber.trim(),
        });
        return;
      }

      if (!instanceId) return;
      // UNA sola consulta a Verifik: 1) persistir identificador → 2) preflight,
      // que con la MISMA respuesta del RUNT compone el semáforo Y hidrata los
      // atributos del vehículo en field_values → 3) recargar la instancia para
      // pintar la tarjeta "Datos del vehículo". (Antes se hacían dos consultas:
      // una dedicada de datos + el preflight; el preflight ya trae ambos.)
      await tramitesClient.patchFieldValues(instanceId, items);
      await onRunPreflight();
      await loadInstance();
    } catch (err) {
      // CF-02 — una consulta fallida (o bloqueada) invalida la anterior: sin consulta válida no se
      // puede crear el trámite, así que "Continuar" vuelve a deshabilitarse.
      onPreviewDone?.(null);
      setPreviewSnapshot(null);
      // AC1 (HU #10882) — el preflight puede bloquear por duplicidad (409 DUPLICATE_ACTIVE_PROCEDURE,
      // HU #10876): en vez del error genérico, se ofrece el aviso con "Retomar" (AC2).
      const duplicateId = getDuplicateActiveProcedureId(err);
      if (duplicateId) {
        setDuplicateInstanceId(duplicateId);
      } else {
        // AC1/AC2 (HU #10884) — bloqueo DURO "vehículo ya matriculado" (422
        // VEHICLE_STATE_INVALID_FOR_TYPE, CF-03 de HU #10877): banner específico según vehicleStatus,
        // en vez del error genérico.
        const stateBlock = getVehicleStateBlock(err);
        if (stateBlock) {
          setVehicleStateBlock(stateBlock);
        } else {
          setError(err instanceof Error ? err.message : 'No se pudo consultar.');
        }
      }
    } finally {
      setPersisting(false);
    }
  };

  // AC2 (HU #10882) — "Retomar": abre el trámite existente que reportó el bloqueo de duplicidad,
  // en su propia ruta de wizard (misma ruta que usa el listado de trámites para abrir un trámite).
  const handleRetomarDuplicado = () => {
    if (!duplicateInstanceId) return;
    router.push(`/tramites/${duplicateInstanceId}`);
  };

  const inputClass =
    'w-full px-3 py-2 rounded-xl border bg-white dark:bg-[#0B0F14] text-xs outline-none focus:border-[#557EFF]';

  const loading = preflightLoading || persisting;
  // Con creación diferida el semáforo vive en memoria (no hay snapshot persistido que releer).
  const effectivePreflight = deferred ? previewSnapshot : preflight;
  const hasResult = !!effectivePreflight?.overall;

  // CF-02 — editar el identificador invalida la consulta previa: el trámite solo puede crearse con
  // una consulta vigente para los datos que están en pantalla.
  const invalidatePreview = () => {
    if (!deferred) return;
    setPreviewSnapshot(null);
    onPreviewDone?.(null);
  };

  /**
   * CF-02 — el paso 1 ofrece EXACTAMENTE los mismos controles que antes (condiciones del trámite,
   * transformaciones, paz y salvo, aceptación de riesgo). Lo único que cambia es CUÁNDO se guardan:
   * sin trámite creado se anotan en memoria y se persisten junto con la creación, al continuar al
   * paso 2. Con trámite ya creado, cada control sigue haciendo su PATCH inmediato como siempre.
   */
  const upsertLocal = (items: { fieldKey: string; valueText: string }[]) => {
    setFieldValues((prev) => {
      const next = [...prev];
      for (const item of items) {
        const i = next.findIndex((f) => f.fieldKey === item.fieldKey);
        const value: FieldValue = {
          formFieldId: '',
          fieldKey: item.fieldKey,
          valueText: item.valueText,
          valueJson: null,
          source: 'user',
        };
        if (i >= 0) next[i] = value;
        else next.push(value);
      }
      return next;
    });
    onPendingFieldValues?.(items);
  };

  // Paz y salvo de impuesto: solo traspaso (placa-first) y solo si el preflight dejó el
  // check de impuesto sin verificar (unknown/warn). Estado leído de field_values.
  const impuestoCheck = effectivePreflight?.checks?.find((c) =>
    c.key.toLowerCase().includes('impuesto'),
  );
  const mostrarPazSalvo =
    !isVin && !!impuestoCheck &&
    (impuestoCheck.status === 'unknown' || impuestoCheck.status === 'warn');
  const pazSalvoConfirmado =
    fieldValues.find((f) => f.fieldKey === 'paz_salvo_impuesto')?.valueText === 'true';

  const handlePazSalvo = async (checked: boolean) => {
    if (deferred) {
      upsertLocal([{ fieldKey: 'paz_salvo_impuesto', valueText: checked ? 'true' : 'false' }]);
      return;
    }
    if (!instanceId) return;
    setPazSalvoSaving(true);
    try {
      await tramitesClient.patchFieldValues(instanceId, [
        { formFieldId: null, fieldKey: 'paz_salvo_impuesto', valueText: checked ? 'true' : 'false', valueJson: null },
      ]);
      await loadInstance();
      onRefresh();
    } finally {
      setPazSalvoSaving(false);
    }
  };

  // "Asumo el riesgo" ante un preflight rojo subsanable (p.ej. estado del vehículo
  // distinto de ACTIVO): persiste riesgo_aceptado en field_values y refresca el
  // wizard para que el backend desbloquee el paso 2 (documentos) sin tocar identidad.
  const riesgoAceptado =
    fieldValues.find((f) => f.fieldKey === 'riesgo_aceptado')?.valueText === 'true';

  const handleRiesgo = async (checked: boolean) => {
    if (deferred) {
      upsertLocal([{ fieldKey: 'riesgo_aceptado', valueText: checked ? 'true' : 'false' }]);
      return;
    }
    if (!instanceId) return;
    setRiesgoSaving(true);
    try {
      await tramitesClient.patchFieldValues(instanceId, [
        { formFieldId: null, fieldKey: 'riesgo_aceptado', valueText: checked ? 'true' : 'false', valueJson: null },
      ]);
      await loadInstance();
      onRefresh();
    } finally {
      setRiesgoSaving(false);
    }
  };

  // Banderas manuales que gatillan documentos condicionales (el backend las lee en
  // TramiteDocumentContextMapper). Leasing solo aplica en traspaso; carrocería en ambos. Aduana es
  // obligatorio de base en matrícula (no hay check de importado). La prenda se gestiona aparte con
  // PrendaForm (Feature #10585).
  const esLeasing = fieldValues.find((f) => f.fieldKey === 'es_leasing')?.valueText === 'true';
  const cambioCarroceria = fieldValues.find((f) => f.fieldKey === 'cambio_carroceria')?.valueText === 'true';

  const saveAtributo = async (fieldKey: string, valueText: string) => {
    if (deferred) {
      upsertLocal([{ fieldKey, valueText }]);
      return;
    }
    if (!instanceId) return;
    setAtributosSaving(true);
    try {
      await tramitesClient.patchFieldValues(instanceId, [
        { formFieldId: null, fieldKey, valueText, valueJson: null },
      ]);
      await loadInstance();
      onRefresh();
    } finally {
      setAtributosSaving(false);
    }
  };

  // A4/B4 (HU #10674) — transformaciones color/combustible: patch atómico de varias claves
  // (efectivo + flag) en una sola llamada, para que el valor declarado y su bandera queden
  // consistentes tras la re-consulta del RUNT (el backend no pisa el efectivo si el flag está activo).
  const saveTransformacion = async (items: { fieldKey: string; valueText: string }[]) => {
    if (items.length === 0) return;
    if (deferred) {
      upsertLocal(items);
      return;
    }
    if (!instanceId) return;
    setAtributosSaving(true);
    try {
      await tramitesClient.patchFieldValues(
        instanceId,
        items.map((i) => ({ formFieldId: null, fieldKey: i.fieldKey, valueText: i.valueText, valueJson: null })),
      );
      await loadInstance();
      onRefresh();
    } finally {
      setAtributosSaving(false);
    }
  };

  // R3 (HU #10539) — CTA "Iniciar traspaso": navega a la ruta de traspaso sembrando el vehículo
  // (placa/VIN) por query param; la página `nuevo/traspaso` crea la instancia y persiste el seed.
  // Solo aplica a matrícula (isVin): el check `vin_matricula` únicamente lo agrega esa rama del preflight.
  const handleIniciarTraspaso = () => {
    const byKeyFv = (key: string) =>
      fieldValues.find((f) => f.fieldKey === key)?.valueText ?? '';
    const seedVin = (vin || byKeyFv('vin')).trim();
    const seedPlaca = (plate || byKeyFv('plate')).trim();
    const params = new URLSearchParams();
    if (seedVin) params.set('seedVin', seedVin);
    if (seedPlaca) params.set('seedPlaca', seedPlaca);
    const qs = params.toString();
    router.push(`/tramites/nuevo/traspaso${qs ? `?${qs}` : ''}`);
  };

  // Botón "Consultar RUNT": mismo estilo gradiente que "Enviar a tránsito"
  // (unificación de estilos pedida). Disparo único de la consulta.
  const consultButton = (
    <button
      type="button"
      onClick={() => void handleRun()}
      disabled={loading}
      className="flex shrink-0 items-center justify-center gap-2 rounded-xl px-5 py-2.5 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
      style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
      aria-label="Consultar RUNT"
    >
      <Search className="h-3.5 w-3.5" />
      {loading ? 'Consultando…' : hasResult ? 'Actualizar' : 'Consultar RUNT'}
    </button>
  );

  return (
    <div className="space-y-4">
      {/* HU #10906 — collapse (contraído, carga perezosa) de las escrituras vigentes de la compañía.
          Tenant-scoped por el header; el NIT del tenant (tenantNitDigits) queda disponible arriba. */}
      <ActiveDeedsCollapse />
      {isVin ? (
        <div
          className="rounded-2xl border bg-white p-4 dark:bg-[#0B0F14]"
        >
          <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
            <input
              id="consulta-vin"
              type="text"
              value={vin}
              onChange={(e) => {
                setVin(sanitizeVin(e.target.value));
                invalidatePreview();
              }}
              onKeyDown={(e) => {
                if (e.key === 'Enter') void handleRun();
              }}
              disabled={readOnly}
              className={`${inputClass} sm:flex-1 disabled:opacity-60`}
              placeholder="Número VIN…"
              aria-label="Número VIN"
            />
            {!readOnly && consultButton}
          </div>
        </div>
      ) : (
        <div
          className="rounded-2xl border bg-white p-4 dark:bg-[#0B0F14]"
        >
          <div className="grid max-w-xl gap-4 sm:grid-cols-2">
            <div>
              <label htmlFor="consulta-plate" className="mb-1.5 block text-xs font-semibold">
                Placa
              </label>
              <input
                id="consulta-plate"
                type="text"
                value={plate}
                onChange={(e) => {
                  setPlate(sanitizePlate(e.target.value));
                  invalidatePreview();
                }}
                disabled={readOnly}
                className={`${inputClass} disabled:opacity-60`}
                placeholder="Ej. ABC123"
              />
            </div>
            {!hideOwnerDocType && (
              <div>
                <label
                  htmlFor="consulta-owner-doc-type"
                  className="mb-1.5 block text-xs font-semibold"
                >
                  Tipo documento propietario
                </label>
                <select
                  id="consulta-owner-doc-type"
                  value={ownerDocType}
                  onChange={(e) => {
                    const next = e.target.value as ActorDocumentType;
                    setOwnerDocType(next);
                    // Re-sanea el número al cambiar de tipo (p.ej. PAS→CC quita letras).
                    setOwnerDocNumber((n) => sanitizeDocNumber(n, next));
                    invalidatePreview();
                  }}
                  disabled={readOnly}
                  className={`${inputClass} disabled:opacity-60`}
                >
                  {DOC_TYPES.map((t) => (
                    <option key={t} value={t}>
                      {t}
                    </option>
                  ))}
                </select>
                {ownerDocTypeSuggested && (
                  <p className="mt-1 text-[11px] leading-tight opacity-70">
                    No se encontró el vehículo en RUNT. Si es maquinaria o remolque, verifica el tipo de
                    documento del propietario (p. ej. NIT) y vuelve a consultar.
                  </p>
                )}
              </div>
            )}
            <div className="sm:col-span-2">
              <label
                htmlFor="consulta-owner-doc-number"
                className="mb-1.5 block text-xs font-semibold"
              >
                Número documento propietario
              </label>
              <input
                id="consulta-owner-doc-number"
                type="text"
                value={ownerDocNumber}
                onChange={(e) => {
                  setOwnerDocNumber(sanitizeDocNumber(e.target.value, ownerDocType));
                  invalidatePreview();
                }}
                disabled={readOnly}
                className={`${inputClass} disabled:opacity-60`}
                placeholder="Ej. 1020304050"
              />
            </div>
          </div>
          {!readOnly && <div className="mt-4">{consultButton}</div>}
        </div>
      )}

      {error && (
        <p
          className="text-[11px] font-medium"
          style={{ color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          {error}
        </p>
      )}

      {/* AC1/AC2 (HU #10882) — bloqueo de duplicidad: ya hay un trámite en curso para este
          VIN/placa (409 DUPLICATE_ACTIVE_PROCEDURE del preflight, HU #10876). "Retomar" abre
          ese trámite existente en vez de continuar este borrador duplicado. */}
      {duplicateInstanceId && (
        <div
          className="flex flex-col gap-2 rounded-xl p-3 sm:flex-row sm:items-center sm:justify-between"
          style={{ background: 'rgba(255,78,0,0.08)', border: '1px solid rgba(255,78,0,0.30)' }}
          role="alert"
          aria-live="assertive"
        >
          <span className="text-xs font-medium" style={{ color: '#FF4E00' }}>
            Ya existe un trámite en curso para este vehículo.
          </span>
          <button
            type="button"
            onClick={handleRetomarDuplicado}
            className="shrink-0 rounded-xl px-4 py-2 text-xs font-semibold text-white"
            style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
            aria-label="Retomar el trámite existente"
          >
            Retomar
          </button>
        </div>
      )}

      {/* AC1/AC2 (HU #10884) — bloqueo DURO "vehículo ya matriculado": el RUNT reporta el vehículo
          ACTIVO (AC1), ya hay una matrícula APROBADA en FLIT para el mismo VIN (AC1), o el RUNT no
          respondió el estado (AC2, "RUNT sin dato"). 422 VEHICLE_STATE_INVALID_FOR_TYPE del preflight
          (CF-03, HU #10877). Sin acción de continuar: no es subsanable (mismo patrón visual que el
          aviso de duplicidad de HU #10882, sin el botón "Retomar"). */}
      {vehicleStateBlock && (
        <div
          className="flex flex-col gap-2 rounded-xl p-3 sm:flex-row sm:items-center sm:justify-between"
          style={{ background: 'rgba(255,78,0,0.08)', border: '1px solid rgba(255,78,0,0.30)' }}
          role="alert"
          aria-live="assertive"
        >
          <span className="text-xs font-medium" style={{ color: '#FF4E00' }}>
            {vehicleStateBlockMessage(vehicleStateBlock.vehicleStatus)}
          </span>
        </div>
      )}

      <VehicleDataCard fieldValues={fieldValues} />

      <VehicleTransformationsCard
        fieldValues={fieldValues}
        readOnly={readOnly}
        saving={atributosSaving}
        onPatch={saveTransformacion}
      />

      <div className="rounded-2xl border bg-white p-4 dark:bg-[#0B0F14] space-y-3">
        <p className="text-xs font-semibold opacity-80">Condiciones del trámite</p>
        <p className="text-[11px] opacity-55 -mt-1.5">
          Marca las condiciones que apliquen; el checklist de documentos se ajusta automáticamente.
        </p>

        {!isVin && (
          <label className="flex items-start gap-2.5">
            <input
              type="checkbox"
              checked={esLeasing}
              onChange={(e) => void saveAtributo('es_leasing', e.target.checked ? 'true' : 'false')}
              disabled={readOnly || atributosSaving}
              className="mt-0.5 h-4 w-4 shrink-0 accent-[#557EFF] disabled:opacity-60"
            />
            <span className="text-xs">
              <span className="font-semibold">Vehículo en leasing</span>
              <span className="mt-0.5 block opacity-55">
                Exige contrato de leasing y declaración de la arrendadora.
              </span>
            </span>
          </label>
        )}

        <label className="flex items-start gap-2.5">
          <input
            type="checkbox"
            checked={cambioCarroceria}
            onChange={(e) => void saveAtributo('cambio_carroceria', e.target.checked ? 'true' : 'false')}
            disabled={readOnly || atributosSaving}
            className="mt-0.5 h-4 w-4 shrink-0 accent-[#557EFF] disabled:opacity-60"
          />
          <span className="text-xs">
            <span className="font-semibold">Cambio de carrocería</span>
            <span className="mt-0.5 block opacity-55">Exige la factura de carrocería.</span>
          </span>
        </label>
      </div>

      {mostrarPazSalvo && (
        <label
          className="flex items-start gap-2.5 rounded-2xl border p-4 bg-white dark:bg-[#0B0F14]"
          style={{ borderColor: '#F9AC00', background: 'rgba(249,172,0,0.06)' }}
        >
          <input
            type="checkbox"
            checked={pazSalvoConfirmado}
            onChange={(e) => void handlePazSalvo(e.target.checked)}
            disabled={readOnly || pazSalvoSaving}
            className="mt-0.5 h-4 w-4 shrink-0 accent-[#557EFF] disabled:opacity-60"
          />
          <span className="text-xs">
            <span className="font-semibold">Confirmo paz y salvo de impuesto vehicular</span>
            <span className="mt-0.5 block opacity-60">
              No pudimos verificar el impuesto vehicular en línea. Confirma que el vehículo
              está al día para poder continuar.
            </span>
          </span>
        </label>
      )}

      <PreflightPanel
        snapshot={effectivePreflight}
        loading={loading}
        onRun={() => void handleRun()}
        riesgoAceptado={riesgoAceptado}
        onToggleRiesgo={(v) => void handleRiesgo(v)}
        saving={riesgoSaving}
        showRunButton={false}
        onIniciarTraspaso={isVin ? handleIniciarTraspaso : undefined}
      />
    </div>
  );
}

/**
 * Renderiza el cuerpo del paso según modalidad+key. El sidebar ya manda el
 * status; aquí solo se elige el componente de captura/consulta del paso.
 */
function StepBody({
  step,
  modalidad,
  instanceId,
  instanceStatus,
  preflight,
  preflightLoading,
  onRunPreflight,
  onRefresh,
  stepFormRef,
  identityOperable = false,
  identityApproved = false,
  vaultCoveredPartes = [],
  rnmcEnabled = false,
  deferredModalidad,
  seedVin,
  seedPlaca,
  onPreviewDone,
  onPendingFieldValues,
  paqueteDocsStatus = 'idle',
  onPaqueteStatusChange,
  onMarkDirty,
}: {
  step: WizardStep;
  modalidad: WizardModalidad;
  instanceId: string | null;
  /** Para remount del paso FUR tras Preparar y mostrar adjuntos generados. */
  instanceStatus?: InstanceStatus | null;
  /** CF-02 — modalidad en curso cuando el trámite AÚN no existe (paso 1 desacoplado). */
  deferredModalidad?: WizardModalidad;
  seedVin?: string;
  seedPlaca?: string;
  /** CF-02 — resultado de la consulta desacoplada: habilita "Continuar" (que crea el trámite). */
  onPreviewDone?: (consulta: PendingConsulta | null) => void;
  /** CF-02 — condiciones marcadas en el paso 1 antes de existir el trámite; se guardan al crearlo. */
  onPendingFieldValues?: (items: { fieldKey: string; valueText: string }[]) => void;
  preflight: PreflightSnapshot | null;
  preflightLoading: boolean;
  onRunPreflight: () => Promise<void>;
  onRefresh: () => void;
  stepFormRef: RefObject<WizardStepFormHandle | null>;
  /** FEATURE 05 — el RNMC aplica al trámite: los actores muestran la fecha de expedición. */
  rnmcEnabled?: boolean;
  /**
   * HU #10350 — borrador finalizado: aunque el wizard esté en solo lectura para los datos, el paso
   * de Identidad debe seguir operable (iniciar/compartir/refrescar Kyverum) porque la validación del
   * cliente es justamente lo que se está esperando. Reabre la captura SOLO para la biométrica.
   */
  identityOperable?: boolean;
  /** Identidad aprobada (deriva del estado server-driven). Con identidad pendiente, el paso FUR
   * informa que el FUR/firma se generarán automáticamente, en vez de empujar la generación manual. */
  identityApproved?: boolean;
  /** HU #10646 — partes (NIT) cubiertas por la firma electrónica del baúl (señal capturada del outcome
   * `firma_baul` de ensureIdentity). BiometricStep pinta el estado "cubierto por el baúl" para ellas. */
  vaultCoveredPartes?: BiometricParte[];
  /** Feature #11066 — estado de pre-generación del paquete al entrar al paso FUR. */
  paqueteDocsStatus?: 'idle' | 'loading' | 'ready' | 'error';
  onPaqueteStatusChange?: (status: 'idle' | 'loading' | 'ready' | 'error') => void;
  /** Feature #11066 — marca dirty en el shell (p.ej. checklist de docs editado). */
  onMarkDirty?: () => void;
}) {
  switch (step.key) {
    // Consulta inicial: VIN (matrícula) o placa+propietario (traspaso).
    // Persiste el identificador en field_values ANTES de correr el preflight,
    // de lo contrario el backend consulta RUNT sin datos (DS-4B-1).
    case 'consulta':
    case 'consulta_vin':
      return (
        <ConsultaStep
          step={step}
          instanceId={instanceId}
          preflight={preflight}
          preflightLoading={preflightLoading}
          onRunPreflight={onRunPreflight}
          onRefresh={onRefresh}
          deferredModalidad={deferredModalidad}
          seedVin={seedVin}
          seedPlaca={seedPlaca}
          onPreviewDone={onPreviewDone}
          onPendingFieldValues={onPendingFieldValues}
        />
      );

    // Paso 2 de ambas modalidades = Documentos (traspaso ya no usa 'validacion':
    // el semáforo del preflight vive en el paso 1). hideHeader: el h2 + subtítulo
    // ya pintan el título del paso.
    case 'documentos':
      return (
        <div className="space-y-4">
          <DocumentChecklist
            instanceId={instanceId}
            onChanged={() => {
              onMarkDirty?.();
              onRefresh?.();
            }}
            hideHeader
            modalidad={modalidad}
          />
          {/* R4 (HU #10596) — en matrícula la prenda es declarativa: se registra aquí
              (informativa, no bloquea la radicación). En traspaso el gate va en el paso
              comercial (HU #10598), no en documentos. */}
          {modalidad !== 'traspaso' && (
            <PrendaForm instanceId={instanceId} onSaved={onRefresh} />
          )}
        </div>
      );

    // key={step.key}: comprador y vendedor renderizan <ActorsForm> en la misma
    // posición del árbol; sin key, React reusa la instancia al cambiar de paso y
    // arrastra el estado (actores hidratados) del paso anterior. La key fuerza
    // el remontaje y la rehidratación limpia por paso.
    case 'comprador':
      return (
        <ActorsForm
          key={step.key}
          ref={stepFormRef}
          instanceId={instanceId}
          modalidad={modalidad === 'traspaso' ? 'traspaso' : 'matricula_inicial'}
          roles={['comprador']}
          onSaved={onRefresh}
          embeddedInWizard
          layout="split"
          rnmcEnabled={rnmcEnabled}
        />
      );

    case 'vendedor':
      return (
        <ActorsForm
          key={step.key}
          ref={stepFormRef}
          instanceId={instanceId}
          modalidad="traspaso"
          roles={['vendedor']}
          onSaved={onRefresh}
          embeddedInWizard
          layout="split"
          // El vendedor es el propietario registrado validado en el paso 1:
          // siembra su documento desde owner_document_* y consulta RUNT al llegar.
          seedDocumentoFromOwner
          autoConsultRunt
          rnmcEnabled={rnmcEnabled}
        />
      );

    case 'comercial':
      // hideHeader: el h2 + subtítulo ya cubren el título del paso. El guardado
      // lo dispara el footer "Guardar y continuar" (vía save() del ref).
      return (
        <div className="space-y-4">
          <CommercialForm
            key={step.key}
            ref={stepFormRef}
            instanceId={instanceId}
            onSaved={onRefresh}
            hideHeader
            embeddedInWizard
          />
          {/* R10 (HU #10598) — prenda como gate del traspaso: la decisión se registra en el paso
              comercial. Con gravámenes en warn, el backend bloquea la preparación/radicación sin
              decisión vigente (o sin su documento). "Omitir" es la vía "asumo el riesgo". */}
          <PrendaForm
            instanceId={instanceId}
            decisions={['solicitar', 'registrar', 'levantar', 'omitir']}
            onSaved={onRefresh}
          />
        </div>
      );

    // Matrícula paso 4 = Identidad (biométrica del comprador, parte única).
    // hideIntro: el h2 + subtítulo ya describen el paso (en `fur` la intro se
    // conserva porque ahí la biométrica es una subsección, no el título).
    case 'identidad': {
      const biometric = (
        <BiometricStep
          instanceId={instanceId}
          modalidad={modalidad}
          onRefresh={onRefresh}
          hideIntro
          vaultCoveredPartes={vaultCoveredPartes}
        />
      );
      // Borrador finalizado: reabre la captura SOLO para la biométrica (provider readOnly=false).
      // En el resto de modos hereda el contexto externo (editable o solo lectura).
      return identityOperable ? (
        <WizardReadOnlyProvider readOnly={false}>{biometric}</WizardReadOnlyProvider>
      ) : (
        biometric
      );
    }

    // FUR (matrícula 5 / traspaso 6). Biométrica de las partes (Slice 6) +
    // firma electrónica, portal de participantes y generación del FUR (Slice 7).
    // En matrícula la biométrica es del comprador (parte única) y no hay firma.
    case 'fur': {
      const biometric = (
        <BiometricStep
          instanceId={instanceId}
          modalidad={modalidad}
          onRefresh={onRefresh}
          vaultCoveredPartes={vaultCoveredPartes}
        />
      );
      return (
        <div className="space-y-6">
          {/* Feature #11066 — banner de pre-gen primero, antes de identidad y el resto del paso. */}
          {(paqueteDocsStatus === 'loading' || paqueteDocsStatus === 'error') && (
            <div
              className="rounded-xl border p-3 text-xs"
              style={
                paqueteDocsStatus === 'error'
                  ? { borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }
                  : { borderColor: '#DFE5ED', background: 'rgba(85,126,255,0.06)' }
              }
              role="status"
              aria-live="polite"
              aria-label="Estado de generación del expediente"
            >
              {paqueteDocsStatus === 'loading' ? (
                <span className="inline-flex items-center gap-2">
                  <RefreshCw className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
                  Generando documentos del expediente (FUR, certificados
                  {modalidad === 'traspaso' ? ', compraventa' : ''} e impronta)… No bloquea Preparar.
                </span>
              ) : (
                <span>
                  No se pudieron generar los documentos del expediente (tras reintentos). Puedes
                  Preparar igual y regenerarlos al Radicar.
                </span>
              )}
            </div>
          )}
          {/* HU #10350 — con la identidad pendiente, el FUR/firma se generan AUTOMÁTICAMENTE al
              aprobarse la validación del cliente (consumidor de outbox #10349). Se avisa aquí para que
              el gestor no intente generarlos a mano y entienda que solo debe "Finalizar". */}
          {!identityApproved && (
            <div
              className="rounded-xl p-3 text-xs border flex items-start gap-2"
              style={{ borderColor: '#557EFF', background: 'rgba(85,126,255,0.06)', color: '#162744' }}
              role="status"
              aria-live="polite"
            >
              <Shield className="h-4 w-4 shrink-0 mt-0.5" style={{ color: '#557EFF' }} aria-hidden="true" />
              <span>
                <span className="font-semibold" style={{ color: '#557EFF' }}>
                  El FUR y la firma se generarán automáticamente
                </span>{' '}
                cuando el cliente valide su identidad. No necesitas generarlos a mano: completa los datos
                y pulsa <span className="font-semibold">Finalizar</span> para dejar el trámite a la espera
                de la validación.
              </span>
            </div>
          )}
          {/* Borrador finalizado (traspaso): la biométrica sigue operable; la firma/FUR no (es
              automática al aprobarse la identidad), por eso hereda el contexto de solo lectura. */}
          {identityOperable ? (
            <WizardReadOnlyProvider readOnly={false}>{biometric}</WizardReadOnlyProvider>
          ) : (
            biometric
          )}
          <FirmaFurStep
            key={`${instanceId ?? 'new'}-${instanceStatus ?? 'borrador'}`}
            instanceId={instanceId}
            modalidad={modalidad}
            onRefresh={onRefresh}
            rnmcEnabled={rnmcEnabled}
            onPaqueteStatusChange={onPaqueteStatusChange}
          />
        </div>
      );
    }

    default:
      return (
        <p className="text-xs opacity-60">
          Paso «{step.key}» sin renderizador en esta fase.
        </p>
      );
  }
}
