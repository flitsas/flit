'use client';

import { useCallback, useEffect, useRef, useState, type RefObject } from 'react';
import { useRouter } from 'next/navigation';
import {
  Calendar,
  Car,
  ChevronLeft,
  ChevronRight,
  Eye,
  RefreshCw,
  Search,
  Shield,
  Wrench,
} from 'lucide-react';
import { useProcedureInstance } from '@/hooks/useProcedureInstance';
import { useWizard } from '@/hooks/useWizard';
import { useWizardTelemetry } from '@/hooks/useWizardTelemetry'; // Reportes2 HU-A
import { PreflightPanel, preflightOverall } from './PreflightPanel';
import { TransitOfficeSearchPicker } from './TransitOfficeSearchPicker';
import { ActorsForm } from './ActorsForm';
import { DocumentChecklist } from './DocumentChecklist';
import { CommercialForm } from './CommercialForm';
import { PrendaForm, traspasoDecisions } from './PrendaForm';
import { furObservationsPreview } from './fur-auto-observations';
import { SubsanacionPanel } from './SubsanacionPanel';
import type { WizardStepFormHandle } from './wizard-step-form';
import { BiometricStep } from './BiometricStep';
import { FirmaFurStep } from './FirmaFurStep';
import { blockerCopy, identidadAutomaticaCopy, stepLabelCopy } from './wizard-copy';
import { canNavigateToStep, frontierIndex } from './wizard-navigation';
import { WizardReadOnlyProvider, useWizardReadOnly } from './WizardReadOnlyContext';
import { DeclaracionesTramite } from './DeclaracionesTramite';
import { VehicleTransformationsCard } from './VehicleTransformationsCard';
import { EstadoAcciones } from './EstadoAcciones';
import { WizardStepTracker } from './WizardStepTracker';
import { Modal } from '@/components/atom/Modal';
import { StatusBadge } from '@/components/atom/StatusBadge';
import {
  isTraspasoActorStepKey,
  nextIndexAfterUnifiedActores,
} from './wizard-actores-coalesce';
import { useToast } from '@/components/admin/Toast';
import { formatDateOnly } from '@/lib/format/date-only';
import {
  tramitesClient,
  getDuplicateActiveProcedureId,
  getVehicleStateBlock,
  isTransitOfficeUnavailable,
  type VehicleStateBlockInfo,
} from '@/lib/api/tramites-client';
// HU #10806 — ¿la ruta de preasignación de placa está activa para esta compañía en el organismo
// elegido? Es la misma consulta que hace el paso del FUR antes de ofrecer la placa preasignada.
import { getPlatePreassignStatus } from '@/lib/api/admin-plate-ranges';
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
  TransitOfficeOption,
  WizardModalidad,
  WizardStep,
} from '@/lib/api/types/procedure-runtime';
import {
  WIZARD_BTN,
  WIZARD_CARD,
  WIZARD_CTA_GRADIENT,
  WIZARD_CTA_GRADIENT_DONE,
  WIZARD_INPUT,
  WIZARD_LABEL,
} from './wizard-field-styles';
import { WizardAccordion } from './WizardAccordion';
import { WizardHelpRail } from './WizardHelpRail';
import { WizardModal } from './WizardModal';
import { estadoChipStyle, estadoLabel } from '@/lib/tramites/estados';
import { WizardCardHeader, WizardPair } from './wizard-atoms';
import { CarLoaderModal } from '@/components/atom/CarLoader';
import { InlineAlert } from '@/components/atom/InlineAlert';

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
  /** HU #11199 — secretaría elegida en el paso 1 (solo matrícula inicial). */
  transitOfficeId?: string;
  /**
   * Resultado del pre-vuelo de ESTA consulta, para que el shell pueda gatear "Continuar" sin
   * trámite creado (sin instancia no hay gates de backend que evaluar).
   * - `hardBlocked`: el vehículo no existe en el RUNT o la consulta no se pudo verificar. No es
   *   subsanable con "asumo el riesgo": sin vehículo verificado no hay trámite.
   * - `red`: bloqueos críticos subsanables (SOAT/RTM/comparendos) que sí admiten aceptar el riesgo.
   */
  hardBlocked?: boolean;
  red?: boolean;
};

/**
 * HU #11199 (AC3) — el listado solo trae los organismos ACTIVOS en FLIT y habilitados para la
 * compañía, así que hay que decir qué hacer cuando el que se busca no aparece: de otro modo el
 * gestor concluye que FLIT no lo cubre y abandona el trámite.
 */
const SECRETARIA_LISTA_AVISO =
  'Solo se muestran los organismos de tránsito activos en FLIT. Si el organismo donde vas a radicar no aparece en la lista, solicita al administrador que lo agregue y lo active.';

/**
 * HU #11200 (AC2/AC3) — el vehículo está matriculado en un organismo donde la compañía no puede
 * radicar. Se avisa en el paso 1, no al final: avanzar el trámite entero para descubrirlo al radicar
 * es trabajo perdido.
 */
const ORGANISMO_NO_DISPONIBLE =
  'No puedes radicar en este organismo de tránsito. El vehículo está matriculado en un organismo que no está activo en FLIT o no está habilitado para tu compañía. Solicita al administrador que lo active y lo habilite para tu compañía antes de continuar con el trámite.';

/**
 * Subtítulo descriptivo por paso, mostrado UNA sola vez bajo el `h2` (título
 * canónico del paso = `activeStep.label`). Centraliza aquí el copy de ayuda que
 * antes vivía duplicado en los `h4` internos de cada hijo; al subirlo evitamos
 * el doble título (uno en la shell, otro en el box). Keys sin entrada no pintan
 * subtítulo. El paso `fur` no pinta título/subtítulo aquí (el resumen tiene su propio encabezado).
 */
const STEP_SUBTITLE: Record<string, string> = {
  identidad: 'Verificación de autenticidad de las partes involucradas.',
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

/**
 * Aviso del detalle en los estados no editables (HU #11053).
 *
 * Antes se imprimía un texto fijo —«Enviado a tránsito … aún puedes generar o descargar el FUR y el
 * expediente consolidado»— para CUALQUIER estado no editable, así que un trámite aprobado, rechazado o
 * anulado anunciaba un envío a tránsito que ya no era su situación y ofrecía una generación que, desde
 * la HU #11051, el backend rechaza en estado final. Ahora el mensaje se deriva del estado real y solo
 * menciona generar donde de verdad se puede.
 */
interface ReadOnlyNoticeStyle {
  titulo: string;
  detalle: string;
  border: string;
  bg: string;
  /** Tinta legible del texto/icono cuando difiere del borde (verdes de marca — HU consolidación). */
  ink?: string;
}

const READ_ONLY_NOTICE: Record<string, ReadOnlyNoticeStyle> = {
  entregado: {
    titulo: 'Enviado a tránsito — solo visualización.',
    detalle:
      'Este trámite ya no puede editarse, pero aún puedes generar o descargar el expediente consolidado.',
    border: '#557EFF',
    bg: 'rgba(85,126,255,0.06)',
  },
  aprobado: {
    titulo: 'Trámite aprobado — solo visualización.',
    detalle:
      'El organismo de tránsito lo aprobó. Su documentación es definitiva: puedes consultarla y descargarla, pero ya no se regenera.',
    // El borde/fondo conservan el verde de marca crudo (relleno tintado); el texto/icono usan la
    // tinta legible unificada (`--flit-success-ink`, consolidación de verdes de texto).
    border: '#5B8A1F',
    bg: 'rgba(140,198,63,0.10)',
    ink: 'var(--flit-success-ink)',
  },
  rechazado: {
    titulo: 'Trámite rechazado — solo visualización.',
    detalle:
      'El organismo de tránsito lo rechazó. Revisa el motivo para saber qué corregir; mientras no se active la subsanación no puede editarse.',
    border: '#c2410c',
    bg: 'rgba(255,78,0,0.06)',
  },
  anulado: {
    titulo: 'Trámite anulado — solo visualización.',
    detalle:
      'Este trámite quedó sin efecto. Puedes consultar y descargar su documentación, pero no editarlo ni regenerarlo.',
    border: '#b91c1c',
    bg: 'rgba(185,28,28,0.06)',
  },
  preparado: {
    titulo: 'Borrador preparado — solo visualización.',
    detalle:
      'Los datos quedaron en firme. Desde el paso de decisión puedes radicar el trámite a tránsito.',
    border: '#B45309',
    bg: 'rgba(249,172,0,0.08)',
  },
};

/** Fallback para un estado no editable no contemplado (nunca debería ocurrir con los estados de N 03). */
const READ_ONLY_NOTICE_FALLBACK: ReadOnlyNoticeStyle = {
  titulo: 'Solo visualización.',
  detalle: 'Este trámite no puede editarse en su estado actual.',
  border: '#557EFF',
  bg: 'rgba(85,126,255,0.06)',
};

function ReadOnlyStateNotice({ estado }: { estado: InstanceStatus | null }) {
  const notice = (estado && READ_ONLY_NOTICE[estado]) || READ_ONLY_NOTICE_FALLBACK;
  const ink = notice.ink ?? notice.border;
  return (
    <div
      className="rounded-xl p-3 text-xs border shrink-0 flex items-start gap-2"
      style={{ borderColor: notice.border, background: notice.bg, color: '#162744' }}
      role="status"
      aria-live="polite"
    >
      <Eye className="h-4 w-4 shrink-0 mt-0.5" style={{ color: ink }} aria-hidden="true" />
      <span>
        <span className="font-semibold" style={{ color: ink }}>
          {notice.titulo}
        </span>{' '}
        {notice.detalle}
      </span>
    </div>
  );
}

/** Icono/marcador por status del paso — ver WizardStepTracker. */

/**
 * Shell del wizard diferenciado, server-driven por GET /wizard. El backend
 * decide modalidad, pasos, status, razones y blockers; la shell renderiza el
 * cuerpo del paso activo según modalidad+key. Tras cada acción que mueva gates
 * (actor, documento, preflight, comercial) se llama `refresh()` para re-consultar
 * el estado autoritativo.
 */
export function TramiteWizard(props: Props) {
  const {
    procedureTypeId,
    modalidad: entryModalidad,
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
  // El riesgo aceptado además vive en estado (no solo en el ref) porque el gate de "Continuar" del
  // paso 1 depende de él: con ref puro el botón no se re-evaluaría al marcar el checkbox.
  const [pendingRiesgoAceptado, setPendingRiesgoAceptado] = useState(false);
  const collectPendingFieldValues = useCallback(
    (items: { fieldKey: string; valueText: string }[]) => {
      for (const item of items) pendingFieldValuesRef.current.set(item.fieldKey, item.valueText);
      const riesgo = items.find((i) => i.fieldKey === 'riesgo_aceptado');
      if (riesgo) setPendingRiesgoAceptado(riesgo.valueText === 'true');
    },
    [],
  );
  // Editar el identificador invalida la consulta (onPreviewDone(null)); la aceptación de riesgo se
  // refiere a ESA consulta, así que no puede sobrevivir a la siguiente.
  const handlePreviewDone = useCallback((consulta: PendingConsulta | null) => {
    setPendingConsulta(consulta);
    if (!consulta) {
      setPendingRiesgoAceptado(false);
      pendingFieldValuesRef.current.delete('riesgo_aceptado');
    }
  }, []);

  // HU #10536 — prioridad marcada en el paso 1. No es un field_value (vive en una columna del
  // expediente, y su endpoint necesita el id), así que no puede viajar en `pendingFieldValuesRef`:
  // se recuerda aquí y se aplica con `setPriority` en cuanto la creación devuelve el trámite.
  const [pendingPrioritario, setPendingPrioritario] = useState(false);

  // Tipo de servicio (casilla 18 del FUR): completo o no, tal como lo reporta el paso de requisitos,
  // que es donde se captura desde el rediseño. Arranca en `true` — mientras el paso no se monta no
  // hay nada que gatear — y `DeclaracionesTramite` lo corrige en cuanto se abre.
  const [tipoServicioGateOk, setTipoServicioGateOk] = useState(true);

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
  const [referenceNumber, setReferenceNumber] = useState<string | null>(
    () => state.detail?.referenceNumber ?? null,
  );
  useEffect(() => {
    const fromDetail = state.detail?.referenceNumber;
    if (!fromDetail) return;
    // Sincroniza referencia cuando el detalle del reducer llega después del mount.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setReferenceNumber(fromDetail);
  }, [state.detail?.referenceNumber]);
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
        setReferenceNumber(d.referenceNumber ?? null);
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
  //  • Preparado: solo lectura, con la acción "Radicar trámite" (preparado→entregado) en el
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
  // N 03 — "Radicar trámite" disponible en `preparado` (o desde borrador encadenando) si la máquina lo permite
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
  /**
   * Trámite radicado → modal de acuse del diseño. La placa puede no existir todavía: en matrícula
   * inicial la asigna el organismo, así que el modal cae al radicado, que siempre está.
   */
  const [radicado, setRadicado] = useState<{
    placa: string | null;
    referencia: string | null;
  } | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);
  // Confirmación de "Anular trámite" (propuesta): salir pierde lo no guardado, y el botón vive
  // junto al de avance, donde un clic de más es fácil.
  const [confirmCancel, setConfirmCancel] = useState(false);
  // Confirmación de "Finalizar y enviar trámite" (propuesta, MatriculaInicial.tsx:1111-1121): la
  // acción terminal ya no radica al primer clic — abre "Confirmar radicación" y el radicado real
  // ocurre solo tras "Sí, radicar trámite".
  const [confirmRadicar, setConfirmRadicar] = useState(false);
  // Feature #11066 — estado informativo del paquete (FUR/certs/impronta). No bloquea Preparar.
  const [paqueteDocsStatus, setPaqueteDocsStatus] = useState<
    'idle' | 'loading' | 'ready' | 'error'
  >('idle');
  // Confirmaciones del expediente consolidado (propuesta, Step5): nacen desmarcadas y se suman a
  // «Requisitos pendientes antes del envío». Gatean radicar —el acto que hay que confirmar—, no la
  // vista del PDF. En la maqueta van premarcadas y no gatean nada; una casilla de consentimiento
  // premarcada no constituye autorización, así que aquí nacen vacías.
  const [confirmacionesExpediente, setConfirmacionesExpediente] = useState<string[]>([]);
  // HU #10646 — partes (NIT/jurídicas) cuya identidad quedó cubierta por la firma electrónica del baúl.
  // El backend no expone un flag "cubierto por baúl" por parte en el estado biométrico, así que la señal
  // se captura del outcome `firma_baul` que devuelve ensureIdentity al guardar la parte, y desde aquí se
  // propaga a BiometricStep para pintar el estado "cubierto por el baúl" (sin botones de biométrica).
  const [vaultCoveredPartes, setVaultCoveredPartes] = useState<BiometricParte[]>([]);
  const { show } = useToast();
  // Guardar+continuar de los pasos con form embebido (actores y comercial): la
  // shell dispara save() vía ref desde el footer "Guardar y continuar".
  const stepFormRef = useRef<WizardStepFormHandle>(null);
  /** Prenda embebida en documentos (matrícula) o comercial (traspaso): save implícito al Continuar. */
  const prendaFormRef = useRef<WizardStepFormHandle>(null);
  const [continuing, setContinuing] = useState(false);
  /**
   * Pasos comprador/vendedor: Continuar solo si la consulta RUNT/RUES del actor fue exitosa.
   * El formulario notifica el gate; al salir del paso se resetea.
   */
  const [actorsConsultationReady, setActorsConsultationReady] = useState(false);
  /**
   * Certificado de prenda: Continuar solo si no falta un adjunto obligatorio
   * (política compañía+OT + decisión que exige documento).
   */
  const [prendaDocGateOk, setPrendaDocGateOk] = useState(true);
  /** Feature #11066 — cambios locales pendientes de Guardar (docs/forms). */
  const [hasUnsavedChanges, setHasUnsavedChanges] = useState(false);
  /**
   * Subsanación: se pone en true tras un Guardar y continuar exitoso (tras haber editado).
   * Habilita Re-radicar solo cuando además no hay dirty pendiente.
   */
  const [subsanacionSavedEdits, setSubsanacionSavedEdits] = useState(false);

  /**
   * Cabecera compacta al hacer scroll: el título y su descripción se pliegan y el seguimiento de
   * pasos se queda. Es información que solo hace falta al llegar —una vez dentro, lo que el gestor
   * necesita ver es en qué paso está—, y en pantallas de portátil el bloque completo se comía un
   * tercio del alto útil del formulario.
   *
   * Se resuelve con un centinela y un IntersectionObserver en vez de escuchar el scroll: el
   * observador no corre en cada píxel movido, solo cuando el centinela cruza el borde.
   */
  const centinelaCabeceraRef = useRef<HTMLDivElement | null>(null);
  const [cabeceraCompacta, setCabeceraCompacta] = useState(false);

  useEffect(() => {
    const centinela = centinelaCabeceraRef.current;
    if (!centinela || typeof IntersectionObserver === 'undefined') return;
    // El scroll no es el de la ventana: el contenido vive dentro del contenedor del Shell.
    const contenedor = document.querySelector('[data-shell-scroll]');
    const observador = new IntersectionObserver(
      ([entrada]) => setCabeceraCompacta(!entrada.isIntersecting),
      { root: contenedor ?? null },
    );
    observador.observe(centinela);
    return () => observador.disconnect();
  }, []);

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

  // Header en la forma de la propuesta: el título ES el trámite, con una frase que dice qué hace, y
  // la identidad del expediente (referencia, tipo, identificador, estado) baja a su propia franja.
  const modalidadLabel =
    modalidad === 'traspaso' ? 'Traspaso Estándar' : 'Matrícula Inicial';
  const displayTitle = modalidadLabel;
  const displaySubtitle =
    modalidad === 'traspaso'
      ? 'Traspasa la propiedad del vehículo ante el organismo de tránsito.'
      : 'Radica el registro inicial del vehículo ante el organismo de tránsito.';
  const refLabel = referenceNumber ?? state.detail?.referenceNumber ?? null;
  // Identificador del expediente: el VIN manda en matrícula y la placa en traspaso, que es el dato
  // por el que se consultó el vehículo en cada modalidad.
  const fvOf = (key: string) =>
    state.detail?.fieldValues?.find((f) => f.fieldKey === key)?.valueText?.trim() || null;
  // Organismo real del trámite, para el texto del modal "Confirmar radicación" (propuesta:
  // MatriculaInicial.tsx:1114) — nunca un literal de maqueta.
  const organismoNombre = fvOf('transit_office_name');
  const identificador =
    modalidad === 'traspaso'
      ? (fvOf('plate') ?? fvOf('vin'))
      : (fvOf('vin') ?? fvOf('plate'));
  const identificadorLabel = modalidad === 'traspaso' ? 'PLACA' : 'VIN';

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
  const scrollWizardToTop = useCallback(() => {
    const scrollEl = (el: HTMLElement | null) => {
      if (!el) return;
      try {
        if (typeof el.scrollTo === 'function') {
          el.scrollTo({ top: 0, behavior: 'auto' });
        } else {
          el.scrollTop = 0;
        }
      } catch {
        el.scrollTop = 0;
      }
    };
    const run = () => {
      // Scroll completo en `main` ([data-shell-scroll]); el tracker queda sticky al tope.
      scrollEl(document.querySelector<HTMLElement>('[data-shell-scroll]'));
      try {
        window.scrollTo({ top: 0, behavior: 'auto' });
      } catch {
        /* jsdom stub */
      }
      const root = document.getElementById('tramite-wizard-root');
      try {
        root?.scrollIntoView({ block: 'start', behavior: 'auto' });
      } catch {
        /* jsdom stub */
      }
    };
    // Doble rAF: espera a que el cuerpo del nuevo paso pinte (layout puede crecer/encoger).
    requestAnimationFrame(() => requestAnimationFrame(run));
  }, []);

  // Al cambiar de paso, si el usuario estaba abajo, volver al tope del Shell.
  useEffect(() => {
    scrollWizardToTop();
  }, [activeIndex, scrollWizardToTop]);

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

  // N 03 — Radicar trámite (un solo click): encadena APIs existentes sin cambiar backend.
  // En borrador: borrador→preparado y luego preparado→entregado.
  // En preparado: solo preparado→entregado (misma validación de consolidado).
  const handleRadicarTramite = async () => {
    if (!instanceId) return;
    const status = (instanceStatus ?? wizard?.status) as InstanceStatus | null;
    const fromBorrador = canRadicar && status === 'borrador';
    const fromPreparado = canEntregar;
    if (!fromBorrador && !fromPreparado) return;

    setSubmitting(true);
    setSubmitError(null);
    try {
      if (fromBorrador) {
        await tramitesClient.transitionInstance(instanceId, 'preparado');
        setInstanceStatus('preparado');
      }

      const docsError = await ensureExpedienteDocs(instanceId);
      if (docsError) {
        setSubmitError(`No se puede radicar: ${docsError}`);
        setSubmitting(false);
        return;
      }

      await tramitesClient.transitionInstance(instanceId, 'entregado');
      telemetry.trackComplete();
      // Flujo del diseño: al radicar se abre el modal de trámite completado en vez de salir de
      // golpe con un toast. El gestor confirma qué quedó radicado y sale desde el CTA. La
      // transición ya ocurrió: el modal es acuse de recibo, no un paso más que haya que aprobar.
      const placaRadicada =
        state.detail?.fieldValues?.find((f) => f.fieldKey === 'placa')?.valueText?.trim() || null;
      setRadicado({
        placa: placaRadicada,
        referencia: referenceNumber ?? state.detail?.referenceNumber ?? null,
      });
      setSubmitting(false);
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
  // Pasos con form embebido (actores y Requisitos de traspaso): el footer "Continuar" guarda
  // y luego avanza, así que se habilita aunque el paso aún esté incomplete (el
  // save lo completa).
  //
  // Requisitos de traspaso ENTRA aquí desde que los datos comerciales dejaron de tener paso propio:
  // `CommercialForm` se monta ahí con `embeddedInWizard` (sin botón propio) y su único disparador de
  // guardado es este pie. Mientras la clave siguió siendo la del paso viejo, el formulario quedaba
  // sin vía de persistencia y el gate `comercial_valor` del backend no se podía satisfacer nunca:
  // el valor de venta solo se guarda al Continuar, y Continuar solo se habilitaba con el valor ya
  // guardado. `comercial` se conserva porque un borrador antiguo puede seguir apuntando ahí y
  // `StepBody` lo normaliza a Requisitos: mismo cuerpo, mismo guardado.
  const isRequisitosTraspasoStep =
    modalidad === 'traspaso' &&
    (activeStep?.key === 'documentos' || activeStep?.key === 'comercial');
  const isSavableStep =
    activeStep?.key === 'comprador' ||
    activeStep?.key === 'vendedor' ||
    isRequisitosTraspasoStep;
  const isActorStep = activeStep?.key === 'comprador' || activeStep?.key === 'vendedor';
  // La prenda vive en Requisitos en AMBAS modalidades (declarativa en matrícula, gate en traspaso),
  // así que su gate de documento aplica en el mismo paso para las dos.
  const isPrendaStep =
    activeStep?.key === 'documentos' || activeStep?.key === 'comercial';
  // El siguiente paso es navegable (no hay paso de datos incompleto por delante). Permite "Continuar"
  // desde un paso diferido incompleto (Identidad) hacia el FUR para finalizar/radicar.
  const nextStepNavigable = canNavigateToStep(steps, activeIndex + 1, navViewOnly);
  const continueDisabled =
    !activeStep ||
    activeIndex >= steps.length - 1 ||
    continuing ||
    // Sin consulta RUNT/RUES exitosa no se avanza en pasos de actores.
    (isActorStep && !actorsConsultationReady) ||
    // Certificado de prenda obligatorio sin adjuntar: no Continuar.
    (isPrendaStep && !prendaDocGateOk) ||
    // Tipo de servicio (casilla 18 del FUR, solo matrícula inicial): sin tipo elegido no se avanza
    // del paso de requisitos; si el tipo es PÚBLICO, tampoco hasta que la consulta devuelva la razón
    // social de la empresa vinculadora (casilla 19). Misma regla que antes gobernaba el paso 1, en el
    // paso donde ahora se captura — ver `DeclaracionesTramite`.
    (activeStep?.key === 'documentos' && modalidad !== 'traspaso' && !tipoServicioGateOk) ||
    // CF-02 — sin trámite creado, "Continuar" es justamente lo que lo crea: se habilita en cuanto la
    // consulta del vehículo salió bien (sin bloqueos), que es el único requisito del paso 1.
    // Sin instancia no hay gates de backend que evaluar (WizardStateQuery necesita la instancia), así
    // que el mismo bloqueo se replica aquí: de lo contrario se crearía el trámite y quedaría atascado
    // en el paso 1, que es peor que impedirlo antes de crearlo.
    (deferredCreation
      ? !pendingConsulta ||
        // Vehículo inexistente en RUNT o consulta no verificable: bloqueo DURO, sin escape.
        pendingConsulta.hardBlocked === true ||
        // Rojo subsanable (SOAT/RTM/comparendos): solo se avanza aceptando el riesgo.
        (pendingConsulta.red === true && !pendingRiesgoAceptado)
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
          // HU #11199 — la secretaría elegida en el paso 1 queda escrita con el trámite.
          transitOfficeId: pendingConsulta.transitOfficeId,
          // El tipo de servicio (casilla 18) ya no viaja aquí: se captura en el paso de requisitos,
          // contra el trámite ya creado. El backend lo sigue aceptando (opcional) por compatibilidad
          // con clientes anteriores; sin él simplemente no escribe `vehicle_service` al crear.
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

        // HU #10536 — la prioridad marcada en el paso 1, ya con el id del trámite. Va aparte del
        // patch de arriba porque no es un field_value: es una columna del expediente con su propio
        // endpoint. Solo se llama si se marcó (el default de la columna ya es `false`) y en modo
        // best-effort: es una preferencia de orden en la bandeja del OT, no un requisito para
        // avanzar, y sigue siendo alternable desde el listado.
        if (pendingPrioritario) {
          try {
            await tramitesClient.setPriority(created.instance.id, true, created.instance.tenantId);
          } catch {
            // Silencioso: el trámite ya existe y la prioridad se puede marcar desde el listado.
          }
          setPendingPrioritario(false);
        }

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
      // Matrícula: la prenda vive en el paso documentos; se persiste al Continuar (sin botón aparte).
      if (activeStep?.key === 'documentos' && modalidad !== 'traspaso') {
        setContinuing(true);
        setSubmitError(null);
        try {
          const okPrenda = await prendaFormRef.current?.save();
          if (okPrenda === false) {
            setSubmitError('No se pudo guardar la decisión de prenda. Por favor, reintenta.');
            return;
          }
          if (canNavigateToStep(steps, activeIndex + 1, navViewOnly)) telemetry.trackStepComplete();
          goToStep(activeIndex + 1);
          if (inSubsanacion) {
            setHasUnsavedChanges(false);
            setSubsanacionSavedEdits(true);
            show('Cambios guardados. Ya puedes re-radicar cuando termines.', 'success');
          }
        } finally {
          setContinuing(false);
        }
        return;
      }
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
        setSubmitError(
          isActorStep
            ? 'Consulta RUNT o RUES con éxito antes de continuar. Sin datos de la consulta no se puede avanzar.'
            : isRequisitosTraspasoStep
              // El save de los datos comerciales devuelve false por dato faltante, no por fallo de
              // red: "reintenta" mandaba a repetir un clic que iba a fallar igual. La tarjeta ya
              // marca los campos; el pie dice cuál es el bloqueo y dónde está.
              ? 'Faltan datos comerciales: ingresa el valor de venta y la causal en esta misma pantalla.'
              : 'No se pudo guardar. Por favor, reintenta.',
        );
        return;
      }

      // Traspaso: la prenda va en Requisitos, junto a los datos comerciales — mismo Continuar, sin
      // botón dedicado. Con el gravamen en warn el backend exige decisión vigente para preparar o
      // radicar, así que dejarla sin persistir aquí solo aplaza el bloqueo hasta el final del flujo.
      if (isRequisitosTraspasoStep) {
        const okPrenda = await prendaFormRef.current?.save();
        if (okPrenda === false) {
          setSubmitError('No se pudo guardar la decisión de prenda. Por favor, reintenta.');
          return;
        }
      }

      // HU #10350 — al guardar la parte (comprador/vendedor), asegura su identidad sin esperar el clic
      // en "Validar identidad". En traspaso unificado se asegura ambas partes en el mismo Continuar.
      const partesIdentidad: BiometricParte[] =
        modalidad === 'traspaso' && isTraspasoActorStepKey(activeStep?.key)
          ? ['vendedor', 'comprador']
          : activeStep?.key === 'comprador'
            ? ['comprador']
            : activeStep?.key === 'vendedor'
              ? ['vendedor']
              : [];
      if (partesIdentidad.length > 0 && instanceId) {
        for (const parteIdentidad of partesIdentidad) {
          // Qué se estaba haciendo cuando falló. El catch de abajo cubre CUATRO llamadas y hasta
          // ahora las aplastaba todas en un mismo mensaje sin código de estado: con el toast delante
          // era imposible saber si el correo de Kyverum no salió porque el actor no tiene email
          // (400), porque esa persona ya tiene un envío en vuelo (409, que es una decisión
          // deliberada y no un fallo) o porque el proveedor rechazó (502/503).
          let etapa: 'asegurar' | 'proveedor' | 'iniciar' | 'simular' = 'asegurar';
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
                etapa = 'proveedor';
                const { provider } = await tramitesClient.getBiometricState(instanceId);
                if (provider === 'kyverum') {
                  etapa = 'iniciar';
                  await tramitesClient.iniciarBiometric(instanceId, { parte: parteIdentidad });
                } else {
                  etapa = 'simular';
                  await tramitesClient.simulateBiometric(instanceId, { parte: parteIdentidad });
                }
              }
            }
          } catch (ensureErr) {
            // No se traga en silencio (HU #10350): asegurar/iniciar la identidad falló. No bloquea el
            // avance —el gestor puede iniciarla manualmente en el paso de Identidad— pero SÍ se avisa para
            // que no continúe creyendo que la identidad quedó encaminada, y se deja traza para observabilidad.
            const status = (ensureErr as { status?: number } | null)?.status;
            console.warn('[tramite-wizard] identidad automática falló', {
              instanceId,
              parte: parteIdentidad,
              etapa,
              status,
              error: ensureErr,
            });
            const { message, tone } = identidadAutomaticaCopy(etapa, status);
            show(
              tone === 'error'
                ? `${message} Continúa y, si es necesario, iníciala en el paso de Identidad.`
                : message,
              tone,
            );
          }
        }
      }

      const fresh = await refresh();
      const freshSteps = fresh?.steps ?? steps;
      const currentComplete = freshSteps[activeIndex]?.status === 'complete';
      // Traspaso unificado: también avanzar si ambos actores quedaron complete aunque
      // el índice activo sea el del vendedor (el comprador server-side se completa en el mismo save).
      const actoresBothComplete =
        modalidad === 'traspaso' &&
        isTraspasoActorStepKey(activeStep?.key) &&
        freshSteps.find((s) => s.key === 'vendedor')?.status === 'complete' &&
        freshSteps.find((s) => s.key === 'comprador')?.status === 'complete';
      if (currentComplete || actoresBothComplete) {
        // Reportes2 HU-A — guardado + avance con éxito = wizard_step_complete.
        telemetry.trackStepComplete();
        const nextIndex = nextIndexAfterUnifiedActores(freshSteps, activeIndex);
        // AC1 (HU #10883) — mismo autosave de `goToStep`, pero este avance mueve `activeIndex`
        // directamente (no pasa por `goToStep`) porque primero guarda el formulario embebido.
        if (nextIndex > activeIndex && freshSteps[nextIndex]) persistCurrentStep(freshSteps[nextIndex].key);
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
    <div id="tramite-wizard-root" className="flex w-full flex-col gap-3 pb-2">
      {/* Radicar es la espera más larga del flujo y la que más ansiedad genera: mientras dura, el
          velo impide el doble envío y el mensaje nombra al organismo de tránsito, para que la
          demora se lea como lo que es —un tercero respondiendo— y no como un cuelgue. */}
      {submitting && <CarLoaderModal mode="radicacion" />}
      {/* Centinela del colapso: mientras se ve, la cabecera va completa; al salir de cuadro el
          asistente ya está en marcha y el título deja sitio a los pasos. Va FUERA del bloque
          sticky —si estuviera dentro nunca saldría de vista— y mide un píxel: no ocupa hueco. */}
      <div ref={centinelaCabeceraRef} aria-hidden="true" className="h-px" />
      {/* Título + seguimiento fijos al scroll de main; fondo sólido app-bg (no transparente). */}
      {/* Al condensar también se aprieta el propio contenedor: la separación entre tarjeta, pasos y
          franja de identidad estaba calculada para la cabecera completa. */}
      <div
        className={`sticky top-0 z-30 -mx-1 bg-[#eef5ff] px-1 pt-1 transition-[padding] duration-200 motion-reduce:transition-none dark:bg-[#0a1428] ${
          cabeceraCompacta ? 'space-y-1.5 pb-1.5' : 'space-y-3 pb-3'
        }`}
      >
        <div
          className={`flex items-center justify-between gap-3 rounded-2xl border border-[#DFE5ED] bg-white px-5 transition-[padding] duration-200 motion-reduce:transition-none dark:border-[#1A1F2B] dark:bg-[#162744] ${
            cabeceraCompacta ? 'py-2' : 'py-4'
          }`}
          style={{ boxShadow: '0 8px 24px rgba(22, 39, 68, 0.08)' }}
        >
          {/* Colapso por `grid-template-rows`, no por `display:none`: el h1 y su descripción siguen
              en el documento y en el árbol de accesibilidad, así que el lector de pantalla puede
              seguir anunciando de qué trámite se trata aunque en pantalla no se vean. El dato
              tampoco se pierde a la vista: la franja azul de abajo repite tipo, id y estado. */}
          <div
            className={`grid min-w-0 transition-[grid-template-rows] duration-200 motion-reduce:transition-none ${
              cabeceraCompacta ? 'grid-rows-[0fr]' : 'grid-rows-[1fr]'
            }`}
          >
            <div className="min-w-0 overflow-hidden">
              <h1 className="truncate text-lg font-bold sm:text-xl" style={{ color: '#557EFF' }}>
                {displayTitle}
              </h1>
              <p className="mt-0.5 truncate text-xs" style={{ color: '#59677D' }}>
                {displaySubtitle}
              </p>
            </div>
          </div>
          <button
            onClick={() => {
              // Reportes2 HU-A — salida explícita sin radicar = wizard_abandon
              // (en solo visualización el trámite ya se radicó: no es abandono).
              if (!fullReadOnly) telemetry.trackAbandon();
              onExit();
            }}
            className="shrink-0 text-xs font-medium hover:opacity-100"
            style={{ color: '#59677D' }}
            aria-label={editLocked ? 'Volver al listado' : 'Cancelar y volver al selector'}
          >
            {editLocked ? '← Volver al listado' : '← Cancelar'}
          </button>
        </div>

        {steps.length === 0 ? (
          <p className="text-xs opacity-60">
            {wizardLoading ? 'Cargando pasos…' : 'Sin pasos disponibles.'}
          </p>
        ) : (
          <WizardStepTracker
            steps={steps}
            activeIndex={activeIndex}
            onGoToStep={goToStep}
            viewOnly={navViewOnly}
            coalesceActores={modalidad === 'traspaso'}
            compacto={cabeceraCompacta}
          />
        )}

        {/* Franja de identidad del expediente (propuesta): referencia, tipo, identificador y
            estado, centrada bajo los pasos. Solo aparece cuando hay algo que identificar — antes
            de crear el trámite no hay referencia ni vehículo consultado.
            B1 (guardián de diseño) — el relleno sólido `#557EFF` con texto blanco a 12px daba
            3.61:1 (rótulos en white/80, 2.88:1): el mismo patrón que ya se retiró de `WizardPill`
            por insuficiente. Pasa a forma tintada (fondo `rgba(85,126,255,0.14)`, texto principal
            `--badge-info-fg`, rótulos en `#59677D` — `color.text.secondary` del sistema), como el
            resto de badges. */}
        {(refLabel || identificador) && (
          <div
            className="mx-auto flex w-fit max-w-full flex-wrap items-center justify-center gap-x-6 gap-y-1 rounded-xl px-4 py-2.5"
            style={{ background: 'rgba(85,126,255,0.14)', color: 'var(--badge-info-fg)' }}
          >
            {refLabel && (
              <p className="text-xs">
                <span className="font-medium uppercase" style={{ color: '#59677D' }}>
                  ID trámite:{' '}
                </span>
                <span className="font-semibold">{refLabel}</span>
              </p>
            )}
            <p className="text-xs">
              <span className="font-medium uppercase" style={{ color: '#59677D' }}>
                Tipo:{' '}
              </span>
              <span className="font-semibold">{modalidadLabel}</span>
            </p>
            {identificador && (
              <p className="text-xs">
                <span className="font-medium uppercase" style={{ color: '#59677D' }}>
                  Identificador:{' '}
                </span>
                <span className="font-semibold">
                  {identificadorLabel}: {identificador}
                </span>
              </p>
            )}
            {estadoTramite && (
              <StatusBadge label={estadoLabel(estadoTramite)} {...estadoChipStyle(estadoTramite)} />
            )}
          </div>
        )}
      </div>

      {fullReadOnly && (
        <div>
          <ReadOnlyStateNotice estado={estadoTramite} />
        </div>
      )}

      {draftFinalized && (
        <div
          className="flex items-start gap-2 rounded-xl border p-3 text-xs"
          style={{ borderColor: '#F9AC00', background: 'rgba(249,172,0,0.08)', color: '#162744' }}
          role="status"
          aria-live="polite"
        >
          <Shield className="mt-0.5 h-4 w-4 shrink-0" style={{ color: 'var(--badge-warning-fg)' }} aria-hidden="true" />
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
        <div>
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
        </div>
      )}

      {(wizardError || submitError || state.error) && (
        <div
          className="rounded-xl border p-3 text-xs"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          {wizardError ?? submitError ?? state.error}
        </div>
      )}

      {/* Cuerpo del paso: sin tarjeta blanca envolvente; el fondo es el app-bg del layout. */}
      <section id="tramite-wizard-scroll">
        <div className="py-1">
          {!activeStep ? (
            wizardLoading ? (
              // Cuerpo del asistente vacío mientras cargan los pasos: mismo velo que las consultas
              // al RUNT, porque es la espera más larga y más visible del módulo y una línea de 12px
              // era casi indistinguible de una pantalla en blanco. El velo va SOLO aquí y no también
              // en el stepper de arriba —los dos los gobierna `wizardLoading`— porque dos velos
              // superpuestos oscurecerían el fondo dos veces.
              <CarLoaderModal label="Cargando el asistente…" />
            ) : (
              <p className="text-xs opacity-60">Este flujo no tiene pasos.</p>
            )
          ) : (
            <div className="space-y-6">
              {/* El título del paso deja de VERSE: en la propuesta el nombre vive en el asistente
                  de arriba y cada tarjeta trae su propio encabezado, así que repetirlo dejaba dos
                  rótulos seguidos diciendo lo mismo. Se conserva como encabezado accesible para no
                  dejar el cuerpo del paso sin nombre en el árbol de encabezados. */}
              <h2 className="sr-only">
                {modalidad === 'traspaso' && isTraspasoActorStepKey(activeStep.key)
                  ? stepLabelCopy('actores', 'Actores')
                  : stepLabelCopy(activeStep.key, activeStep.label)}
              </h2>
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
                prendaFormRef={prendaFormRef}
                onActorsConsultationGateChange={setActorsConsultationReady}
                identityOperable={draftFinalized}
                identityApproved={identityApproved}
                vaultCoveredPartes={vaultCoveredPartes}
                rnmcEnabled={wizard?.rnmcEnabled ?? false}
                esMigrado={wizard?.esMigrado ?? false}
                prendaDocumentRequired={wizard?.prendaDocumentRequired ?? true}
                onPrendaDocumentGateChange={setPrendaDocGateOk}
                deferredModalidad={deferredCreation ? entryModalidad : undefined}
                seedVin={seedVin}
                seedPlaca={seedPlaca}
                onPreviewDone={handlePreviewDone}
                onPendingFieldValues={collectPendingFieldValues}
                prioritario={pendingPrioritario}
                onPrioritarioChange={setPendingPrioritario}
                onTipoServicioGateChange={setTipoServicioGateOk}
                paqueteDocsStatus={paqueteDocsStatus}
                onPaqueteStatusChange={setPaqueteDocsStatus}
                onConfirmacionesExpedienteChange={setConfirmacionesExpediente}
                onMarkDirty={() => setHasUnsavedChanges(true)}
              />
            </div>
          )}

          {/* Bloqueos de envío traducidos (en el paso de decisión). */}
          {isDecisionStep && (blockers.length > 0 || confirmacionesExpediente.length > 0) && (
            <InlineAlert
              tone="error"
              title="Requisitos pendientes antes del envío"
              className="mt-6"
            >
              <ul className="space-y-0.5" aria-label="Bloqueos de envío">
                {blockers.map((b) => (
                  <li key={b}>• {blockerCopy(b)}</li>
                ))}
                {/* Las del servidor llegan como código y se traducen; estas ya son texto legible
                    porque las redacta el propio expediente. */}
                {confirmacionesExpediente.map((c) => (
                  <li key={c}>• {c}</li>
                ))}
              </ul>
            </InlineAlert>
          )}

          {/* Acción terminal del asistente (propuesta: MatriculaInicial.tsx:1100-1109) — vive en el
              CUERPO del paso de decisión, no en el pie. Es la ÚNICA pieza que se mueve: "Anterior",
              EstadoAcciones y "Anular trámite" son función existente del pie y se quedan ahí; el
              pie de FLIT tampoco desaparece en este paso como en la propuesta, porque sigue
              haciendo falta para navegar hacia atrás y para cancelar.
              Cubre las tres variantes que antes vivían en el pie con el rótulo "Radicar trámite"
              (entrega desde preparado, radicar desde borrador con identidad, y el disabled a la
              espera de identidad): las tres son la misma acción de cierre. */}
          {isDecisionStep &&
            !inSubsanacion &&
            ((fullReadOnly && canEntregar) || (!fullReadOnly && (canRadicar || draftFinalized))) && (
              <div className="mt-6 flex justify-end">
                <button
                  type="button"
                  onClick={() => setConfirmRadicar(true)}
                  disabled={
                    submitting ||
                    (!fullReadOnly && !canRadicar && draftFinalized) ||
                    confirmacionesExpediente.length > 0
                  }
                  className={`${WIZARD_BTN} text-white focus-visible:ring-[#557EFF] disabled:opacity-50`}
                  style={{ background: WIZARD_CTA_GRADIENT_DONE }}
                  title={
                    fullReadOnly
                      ? 'Entrega el trámite al organismo de tránsito'
                      : canRadicar
                        ? 'Prepara y radica el trámite en un solo paso (queda en entregado)'
                        : 'Disponible cuando el cliente valide su identidad'
                  }
                >
                  {submitting ? 'Radicando…' : 'Finalizar y enviar trámite'}
                </button>
              </div>
            )}
        </div>

          <div className="grid shrink-0 grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-3 border-t px-2 pb-3 pt-4 sm:px-5">
            <button
              onClick={() => goToStep(Math.max(0, activeIndex - 1))}
              disabled={activeIndex === 0}
              // Misma altura que las acciones de la derecha: en la propuesta el pie es una sola
              // línea de botones, y un "Anterior" más bajo la partía en dos.
              className={`${WIZARD_BTN} flex items-center gap-1 border focus-visible:ring-[#162744] disabled:opacity-30`}
              style={{ borderColor: '#162744', color: '#162744' }}
            >
              <ChevronLeft className="h-3 w-3" /> Anterior
            </button>

            <div className="flex min-w-0 justify-center">
              {instanceId ? (
                <EstadoAcciones
                  instanceId={instanceId}
                  onChanged={() => void refresh()}
                  embedded
                />
              ) : null}
            </div>

            {/* `gap-3` como el pie de la propuesta: sin él los botones quedaban pegados y se leían
                como una sola pieza, con "Anular trámite" y el avance sin aire entre medias. */}
            <div className="flex flex-wrap items-center justify-end gap-3">
            {/* Acción derecha del footer:
                · Preparado o borrador con identidad OK / en borrador finalizado: la acción terminal
                  ("Finalizar y enviar trámite") ya NO vive aquí — se movió al cuerpo del paso de
                  decisión (justo debajo del InlineAlert de requisitos pendientes), igual que en la
                  propuesta. Aquí no queda nada para esos casos.
                · Solo visualización (otros estados no editables): sin acciones, solo se recorre.
                · Paso de decisión en borrador sin identidad: "Finalizar" (finalize-draft) — esta SÍ
                  se queda en el pie: no es la acción de cierre, solo sella el borrador a la espera
                  de la validación de identidad.
                · Pasos de datos: "Guardar y continuar". */}
            {fullReadOnly ? null : isDecisionStep ? (
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
                  className={`${WIZARD_BTN} flex items-center gap-1 text-white focus-visible:ring-[#557EFF] disabled:opacity-50`}
                  style={{ background: WIZARD_CTA_GRADIENT }}
                  title="Guarda los cambios de este paso y habilita Re-radicar"
                >
                  {continuing ? 'Guardando…' : 'Guardar y continuar'}
                </button>
              ) : canRadicar || draftFinalized ? null : (
                <button
                  onClick={() => void handleFinalizeDraft()}
                  disabled={!canSubmit || submitting}
                  className={`${WIZARD_BTN} text-white focus-visible:ring-[#557EFF] disabled:opacity-50`}
                  style={{ background: WIZARD_CTA_GRADIENT }}
                >
                  {submitting ? 'Finalizando…' : 'Finalizar'}
                </button>
              )
            ) : draftFinalized ? (
              <button
                onClick={() => goToStep(activeIndex + 1)}
                disabled={!canNavigateToStep(steps, activeIndex + 1, navViewOnly)}
                className={`${WIZARD_BTN} flex items-center gap-1 text-white focus-visible:ring-[#557EFF] disabled:opacity-50`}
                style={{ background: WIZARD_CTA_GRADIENT }}
              >
                Continuar
                <ChevronRight className="h-3 w-3" />
              </button>
            ) : (
              <>
                {/* Anular trámite (propuesta): acción destructiva en naranja de alerta, junto al
                    avance y no escondida en la cabecera, con confirmación porque se pierde lo no
                    guardado. Solo mientras el trámite se está capturando: ya radicado no hay nada
                    que anular desde aquí. */}
                {!fullReadOnly && (
                  <button
                    type="button"
                    onClick={() => setConfirmCancel(true)}
                    className={`${WIZARD_BTN} text-white focus-visible:ring-[#FF4E00]`}
                    style={{ background: '#FF4E00' }}
                  >
                    Anular trámite
                  </button>
                )}
                <button
                  onClick={() => void handleContinue()}
                  disabled={continueDisabled}
                  className={`${WIZARD_BTN} text-white focus-visible:ring-[#557EFF] disabled:opacity-40`}
                  // Deshabilitado en gris pleno, no el degradado atenuado: media opacidad sobre un
                  // degradado deja un azul lavado que sigue leyéndose como acción disponible.
                  style={{ background: continueDisabled ? '#94A3B8' : WIZARD_CTA_GRADIENT }}
                >
                  {continuing ? 'Guardando…' : 'Continuar y guardar'}
                </button>
              </>
            )}
            </div>
          </div>
        </section>

      {confirmCancel && (
        <WizardModal title="Anular trámite" onClose={() => setConfirmCancel(false)}>
          <p className="text-xs leading-relaxed opacity-80">
            ¿Deseas anular el trámite en curso? Los datos no guardados se perderán.
          </p>
          <div className="mt-6 flex justify-end gap-2">
            <button
              type="button"
              onClick={() => setConfirmCancel(false)}
              className="rounded-xl border px-4 py-2 text-xs font-medium focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
            >
              Continuar editando
            </button>
            <button
              type="button"
              onClick={() => {
                // Reportes2 HU-A — salida sin radicar = wizard_abandon.
                if (!fullReadOnly) telemetry.trackAbandon();
                onExit();
              }}
              className="rounded-xl px-5 py-2 text-xs font-semibold text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-[#FF4E00] focus-visible:ring-offset-2"
              style={{ background: '#FF4E00' }}
            >
              Sí, anular
            </button>
          </div>
        </WizardModal>
      )}

      {/* Confirmación de la acción terminal (propuesta: MatriculaInicial.tsx:1111-1121). Pulsar
          "Finalizar y enviar trámite" ya NO radica al primer clic: abre este modal, con la
          modalidad y el organismo reales del trámite (nunca literales de maqueta), y solo
          "Sí, radicar trámite" dispara handleRadicarTramite. */}
      {confirmRadicar && (
        <WizardModal title="Confirmar radicación" onClose={() => setConfirmRadicar(false)}>
          <p className="text-xs leading-relaxed opacity-80">
            ¿Estás seguro de finalizar y radicar este trámite de {modalidadLabel} ante la{' '}
            {organismoNombre ?? 'entidad de tránsito seleccionada'}?
          </p>
          <div className="mt-6 flex justify-end gap-2">
            <button
              type="button"
              onClick={() => setConfirmRadicar(false)}
              className="rounded-xl border px-4 py-2 text-xs font-medium focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
            >
              Revisar datos
            </button>
            <button
              type="button"
              onClick={() => {
                setConfirmRadicar(false);
                void handleRadicarTramite();
              }}
              className="rounded-xl px-5 py-2 text-xs font-semibold text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
              style={{ background: WIZARD_CTA_GRADIENT_DONE }}
            >
              Sí, radicar trámite
            </button>
          </div>
        </WizardModal>
      )}

      {/* El "Historial de estados" y los "Avisos de correo" NO van en el asistente: colgaban sueltos
          bajo el pie, en todos los pasos, y el asistente es para capturar el trámite, no para
          auditarlo. En el repo de diseño la trazabilidad vive en la vista de detalle del trámite
          (`DetalleTramiteModal`, "Historial de auditoría"), que es donde deben reaparecer.
          `EstadoTimelinePanel` y `AvisosCorreoPanel` siguen en el repo, listos para montarse ahí. */}

      {/* Acuse de radicación — patrón "trámite completado" del diseño: título de éxito, el
          identificador en grande con tracking amplio, mensaje de confirmación y CTA degradado.
          Sustituye al toast + salida inmediata: el gestor ve QUÉ quedó radicado antes de salir. */}
      <Modal
        open={!!radicado}
        onClose={() => {
          setRadicado(null);
          onExit();
        }}
        title="¡Trámite completado!"
        titleClassName="text-lg font-bold text-[#557EFF]"
        size="sm"
      >
        <div className="text-center">
          <p className="text-xs uppercase tracking-wide opacity-55">
            {radicado?.placa ? 'Placa asociada' : 'Radicado'}
          </p>
          <p
            className="mt-1 text-3xl font-bold"
            style={{ color: '#162744', letterSpacing: radicado?.placa ? '0.35em' : '0.12em' }}
          >
            {radicado?.placa ?? radicado?.referencia ?? '—'}
          </p>
          {/* En matrícula inicial la placa la asigna el organismo: se dice, en vez de dejar un
              hueco que parezca un dato perdido. */}
          {!radicado?.placa ? (
            <p className="mt-2 text-xs opacity-60">
              La placa la asigna el organismo de tránsito.
            </p>
          ) : null}
          <p className="mt-4 text-xs opacity-70">
            El trámite fue validado y enviado correctamente al organismo de tránsito.
          </p>
          <button
            type="button"
            onClick={() => {
              setRadicado(null);
              onExit();
            }}
            className="mt-5 w-full rounded-xl py-2.5 text-xs font-semibold text-white transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
            style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
          >
            Ir al listado de trámites
          </button>
        </div>
      </Modal>
    </div>
   </WizardReadOnlyProvider>
  );
}

const DOC_TYPES: ActorDocumentType[] = ['CC', 'CE', 'NIT', 'PAS'];

/**
 * Trámites principales del selector del paso 1, en el orden y con el copy de la propuesta.
 *
 * "Otros Trámites" viaja con `disponible: false`: la propuesta lo contempla como tercera familia
 * (modificaciones y novedades) pero el asistente todavía no la sabe recorrer —no hay modalidad ni
 * pasos para ella—. Se pinta apagada en vez de omitirla porque el gestor la busca; ofrecerla
 * seleccionable sería un camino sin salida.
 */
const MODALIDAD_OPCIONES: {
  id: string;
  label: string;
  descripcion: string;
  disponible: boolean;
}[] = [
  {
    id: 'matricula_inicial',
    label: 'Matrícula Inicial',
    descripcion: 'Vehículo nuevo sin placa asignada',
    disponible: true,
  },
  {
    id: 'traspaso',
    label: 'Traspaso',
    descripcion: 'Cambio de propietario del vehículo',
    disponible: true,
  },
  {
    id: 'otros',
    label: 'Otros Trámites',
    descripcion: 'Modificaciones y novedades',
    disponible: false,
  },
];

/**
 * Atributos de detalle del vehículo (los que NO van en el hero placa/marca/línea/
 * modelo). Origen RUNT, hidratados en field_values por la consulta del preflight.
 * Solo se pinta lo presente.
 *
 * Sin icono por atributo: la propuesta presenta estos datos como una retícula de pares
 * rótulo/valor. Trece iconos distintos en una rejilla de consulta no distinguen nada —el rótulo ya
 * nombra el dato— y sí compiten con el valor, que es lo único que el gestor viene a leer.
 */
const VEHICLE_DETAILS: { key: string; label: string }[] = [
  { key: 'vin', label: 'VIN' },
  { key: 'vehicle_color', label: 'Color' },
  { key: 'vehicle_class', label: 'Clase' },
  { key: 'vehicle_service', label: 'Servicio' },
  { key: 'vehicle_fuel', label: 'Combustible' },
  { key: 'vehicle_engine_displacement', label: 'Cilindraje' },
  { key: 'vehicle_body_type', label: 'Carrocería' },
  { key: 'vehicle_engine_number', label: 'Nº Motor' },
  { key: 'vehicle_chassis', label: 'Nº Chasis' },
  { key: 'vehicle_series', label: 'Nº Serie' },
  { key: 'vehicle_passengers', label: 'Pasajeros' },
  { key: 'vehicle_registration_date', label: 'Fecha matrícula' },
  { key: 'transit_office_name', label: 'Organismo de tránsito' },
];

/**
 * Tarjeta "Datos del vehículo · RUNT". Lee los field_values frescos de la
 * instancia (hidratados por la consulta del preflight) y los presenta con un
 * hero (placa + marca/línea/modelo + estado) y una grilla de atributos con
 * iconos. Solo se pinta lo presente; nada de proveedor.
 */
function VehicleDataCard({
  fieldValues,
  bare = false,
  validadoEnRunt = false,
}: {
  fieldValues: FieldValue[];
  /** Dentro del acordeón "Datos consolidados del vehículo (RUNT)": sin marco ni cabecera propia. */
  bare?: boolean;
  /**
   * Añade el distintivo "Validado en RUNT" a la franja de identificación. Se usa donde la tarjeta
   * NO va bajo una cabecera que ya nombre la fuente —dentro de la tarjeta de consulta de
   * matrícula—, para no perder de dónde salieron los datos.
   */
  validadoEnRunt?: boolean;
}) {
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
      className={
        bare ? '' : 'overflow-hidden rounded-2xl border bg-white dark:bg-[#162744]'
      }
    >
      {/* Header — embebido lo pinta el acordeón, que ya dice de dónde vienen los datos. */}
      {!bare && (
        <div className="flex items-center gap-3 border-b px-4 py-3">
          <span
            className="grid h-7 w-7 shrink-0 place-items-center rounded-lg"
            style={{ background: 'rgba(85,126,255,0.10)' }}
          >
            <Car className="h-4 w-4" style={{ color: '#557EFF' }} />
          </span>
          <div className="min-w-0 flex-1">
            <WizardCardHeader
              title="Datos del vehículo"
              action={<StatusBadge label="RUNT" tone="info" />}
              className=""
            />
          </div>
        </div>
      )}

      {/* Hero: placa + marca/línea/modelo + estado. En bare (traspaso) el bloque SOAT/TM
          se integra a la derecha del hero para que la franja superior quede completa sin
          desplazarse, como pide el PDF (Oleada 2). Sin hasSoatRtm no se pinta nada extra. */}
      <div
        className={`flex flex-wrap items-start gap-4 ${bare ? 'pb-4' : 'px-4 py-4'}`}
      >
        {plate && (
          <div
            className="rounded-xl border-2 px-4 py-2 text-center"
            style={{ borderColor: '#557EFF', background: 'rgba(85,126,255,0.06)' }}
          >
            <p className="text-xs font-semibold uppercase opacity-50">Placa</p>
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
              <span className="inline-flex items-center gap-1 text-xs opacity-70">
                <Calendar className="h-3 w-3" /> Modelo {year}
              </span>
            )}
            {estado && (
              <StatusBadge label={estado} tone={estadoActivo ? 'success' : 'neutral'} />
            )}
            {validadoEnRunt && <StatusBadge label="Validado en RUNT" tone="success" />}
          </div>
        </div>

        {/* SOAT / TM en el hero solo en bare (traspaso): quedan a la derecha de la placa,
            en la misma franja superior, como Lovable. En !bare se pinta la sección aparte. */}
        {bare && hasSoatRtm && (
          <div className="flex shrink-0 flex-wrap gap-2">
            {(soatVencimiento || soatAseguradora) && (
              <div className="flex min-w-0 items-start gap-1.5 rounded-xl border px-2.5 py-1.5">
                <Shield className="mt-0.5 h-3.5 w-3.5 shrink-0" style={{ color: '#557EFF' }} />
                <div className="min-w-0">
                  <p className="text-xs font-bold uppercase" style={{ color: '#557EFF' }}>SOAT</p>
                  {soatVencimiento && (
                    <p className="text-xs font-semibold">Vence: {formatDateOnly(soatVencimiento)}</p>
                  )}
                  {soatAseguradora && (
                    <p className="truncate max-w-[120px] text-xs opacity-60">{soatAseguradora}</p>
                  )}
                </div>
              </div>
            )}
            {rtmVencimiento && (
              <div className="flex min-w-0 items-start gap-1.5 rounded-xl border px-2.5 py-1.5">
                <Wrench className="mt-0.5 h-3.5 w-3.5 shrink-0" style={{ color: 'var(--flit-success-ink)' }} />
                <div className="min-w-0">
                  <p className="text-xs font-bold uppercase" style={{ color: 'var(--flit-success-ink)' }}>TM</p>
                  <p className="text-xs font-semibold">Vence: {formatDateOnly(rtmVencimiento)}</p>
                </div>
              </div>
            )}
          </div>
        )}
      </div>

      {/* Retícula de pares rótulo/valor de la propuesta: 2 columnas en móvil, 3 en tablet, 5 en
          escritorio. Sin hairlines entre celdas — el aire separa mejor que la línea cuando lo que
          se compara son datos cortos. */}
      {details.length > 0 && (
        <div
          className={`grid grid-cols-2 gap-x-4 gap-y-3 sm:grid-cols-3 lg:grid-cols-5 ${
            bare ? 'border-t pt-4' : 'border-t px-4 py-4'
          }`}
        >
          {details.map((d) => (
            <WizardPair key={d.key} label={d.label} value={d.value} />
          ))}
        </div>
      )}

      {/* Sección SOAT / RTM — solo en !bare; en bare se pinta en el hero (oleada 2). */}
      {hasSoatRtm && !bare && (
        <div className="border-t px-4 py-3">
          <p className="mb-2 text-xs font-semibold uppercase opacity-50">
            Documentos del vehículo
          </p>
          <div className="flex flex-wrap gap-3">
            {(soatVencimiento || soatAseguradora) && (
              <div className="flex min-w-0 items-start gap-2 rounded-xl border px-3 py-2">
                <Shield className="mt-0.5 h-3.5 w-3.5 shrink-0" style={{ color: '#557EFF' }} />
                <div className="min-w-0">
                  <p className="text-xs font-bold uppercase" style={{ color: '#557EFF' }}>SOAT</p>
                  {soatVencimiento && (
                    <p className="text-xs font-semibold">Vence: {formatDateOnly(soatVencimiento)}</p>
                  )}
                  {soatAseguradora && (
                    <p className="truncate text-xs opacity-60">{soatAseguradora}</p>
                  )}
                </div>
              </div>
            )}
            {rtmVencimiento && (
              <div className="flex min-w-0 items-start gap-2 rounded-xl border px-3 py-2">
                <Wrench className="mt-0.5 h-3.5 w-3.5 shrink-0" style={{ color: 'var(--flit-success-ink)' }} />
                <div className="min-w-0">
                  <p className="text-xs font-bold uppercase" style={{ color: 'var(--flit-success-ink)' }}>
                    Tecno-mecánica
                  </p>
                  <p className="text-xs font-semibold">Vence: {formatDateOnly(rtmVencimiento)}</p>
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
 * Campo de observaciones del trámite en el paso de documentos (P6).
 * Lee/escribe `fur_observations` vía field_values — el mismo campo que usa FirmaFurStep.
 * Se guarda en el blur del textarea (best-effort, igual que FirmaFurStep.guardarCampos).
 */
function TramiteObservacionesField({
  instanceId,
  hideCardWrapper = false,
}: {
  instanceId: string | null;
  /**
   * Oleada 2 — cuando el campo vive dentro de un WizardAccordion que ya actúa como contenedor,
   * no repetir la tarjeta (borde + fondo + padding). El contenido se renderiza sin envoltorio.
   */
  hideCardWrapper?: boolean;
}) {
  const readOnly = useWizardReadOnly();
  const [observaciones, setObservaciones] = useState('');
  const [saving, setSaving] = useState(false);
  // Los field_values con los que se compone el texto automático (transformaciones declaradas, tipo
  // de servicio + vinculadora). Se guardan crudos, no ya compuestos, porque la vista previa se
  // recalcula en cada tecla junto con lo que el gestor escribe.
  const [fieldValues, setFieldValues] = useState<FieldValue[]>([]);
  // La vista previa se deriva del estado en cada render: así lo que se escribe aparece abajo al
  // instante, sin esperar al blur que persiste el campo.
  const preview = furObservationsPreview(observaciones, fieldValues);

  useEffect(() => {
    if (!instanceId) return;
    let active = true;
    void tramitesClient
      .getInstance(instanceId)
      .then((detail) => {
        if (active) {
          const val = detail?.fieldValues?.find((f) => f.fieldKey === 'fur_observations')?.valueText ?? '';
          setObservaciones(val);
          setFieldValues(detail?.fieldValues ?? []);
        }
      })
      .catch(() => {});
    return () => {
      active = false;
    };
  }, [instanceId]);

  const handleBlur = async () => {
    if (!instanceId || readOnly) return;
    setSaving(true);
    try {
      await tramitesClient.patchFieldValues(instanceId, [
        { formFieldId: null, fieldKey: 'fur_observations', valueText: observaciones.trim() || null },
      ]);
    } catch {
      // Best-effort: igual que FirmaFurStep.
    } finally {
      setSaving(false);
    }
  };

  const content = (
    <div className="space-y-2">
      {!hideCardWrapper && (
        <WizardCardHeader
          title="Observaciones del trámite"
          subtitle="Se incluirán en el recuadro de observaciones del FUR. Puedes editarlas también en el paso final antes de preparar el expediente."
        />
      )}
      <textarea
        id="tramite-observaciones"
        aria-label="Observaciones del trámite"
        value={observaciones}
        onChange={(e) => setObservaciones(e.target.value)}
        onBlur={() => void handleBlur()}
        disabled={readOnly || saving}
        rows={3}
        placeholder="Ingresa observaciones relevantes para el FUR…"
        className={`${WIZARD_INPUT} resize-none`}
      />
      {saving && (
        <p className="text-xs opacity-50" role="status" aria-live="polite">
          Guardando…
        </p>
      )}
      {/* Espejo de solo lectura del recuadro del FUR. */}
      {(preview.manual || preview.auto.length > 0) && (
        <div className="rounded-xl bg-[#F4F6FA] px-3 py-2 dark:bg-[#131A22]">
          <p className="text-xs font-bold uppercase opacity-55">Así quedarán en el FUR</p>
          <div className="mt-1 space-y-0.5 text-xs leading-relaxed">
            {preview.manual && (
              <p className="whitespace-pre-line break-words">{preview.manual}</p>
            )}
            {preview.auto.map((segment) => (
              <p key={segment} className="opacity-70">
                {segment}
              </p>
            ))}
          </div>
        </div>
      )}
    </div>
  );

  if (hideCardWrapper) return content;
  return (
    <div className="rounded-2xl border bg-white p-4 dark:bg-[#162744]">{content}</div>
  );
}

/**
 * Trámites Simultáneos autónomo (Oleada 2 · PDF 20/08).
 * Wrapper de VehicleTransformationsCard con su propio ciclo de carga y persistencia,
 * para poder situarlo en el paso de documentos DESPUÉS del checklist y ANTES de las
 * observaciones, sin depender del estado de DeclaracionesTramite.
 */
function TramiteSimultaneosField({
  instanceId,
  hideHeader = false,
}: {
  instanceId: string | null;
  hideHeader?: boolean;
}) {
  const readOnly = useWizardReadOnly();
  const [fieldValues, setFieldValues] = useState<FieldValue[]>([]);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!instanceId) return;
    let active = true;
    void tramitesClient
      .getInstance(instanceId)
      .then((d) => {
        if (active) setFieldValues(d?.fieldValues ?? []);
      })
      .catch(() => {});
    return () => {
      active = false;
    };
  }, [instanceId]);

  const handlePatch = async (items: { fieldKey: string; valueText: string }[]) => {
    if (!instanceId || items.length === 0) return;
    setSaving(true);
    try {
      await tramitesClient.patchFieldValues(
        instanceId,
        items.map((i) => ({
          formFieldId: null,
          fieldKey: i.fieldKey,
          valueText: i.valueText,
          valueJson: null,
        })),
      );
      const updated = await tramitesClient.getInstance(instanceId).catch(() => null);
      if (updated?.fieldValues) setFieldValues(updated.fieldValues);
    } finally {
      setSaving(false);
    }
  };

  return (
    <VehicleTransformationsCard
      fieldValues={fieldValues}
      readOnly={readOnly}
      saving={saving}
      onPatch={handlePatch}
      hideHeader={hideHeader}
    />
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
  prioritario = false,
  onPrioritarioChange,
  esMigrado = false,
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
  /** HU #10536 — marca de prioridad elegida aquí; el shell la aplica al crear el trámite. */
  prioritario?: boolean;
  onPrioritarioChange?: (value: boolean) => void;
  /** Migración V1→V2 — explica en el panel por qué el pre-vuelo llega vacío. */
  esMigrado?: boolean;
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

  // HU #11199 — la secretaría se elige aquí SOLO en matrícula inicial y SOLO mientras el trámite no
  // existe (AC5 + convivencia D8): los borradores abiertos antes del cambio siguen eligiendo el
  // organismo en el paso del FUR, donde ya lo tenían.
  const eligeSecretaria = deferred && deferredModalidad === 'matricula_inicial';
  /**
   * Si la TARJETA de radicación se pinta. Distinto de `eligeSecretaria`, que decide si el organismo
   * viaja en la creación y si el gate de "Continuar" lo exige.
   *
   * Se separaron porque compartían condición y eso escondía un defecto: la tarjeta desaparecía en
   * cuanto el trámite existía. El gestor elegía secretaría, dígito y prioridad, continuaba, volvía
   * al paso 1 — y los tres datos ya no estaban en pantalla. Estaban guardados; simplemente no había
   * dónde verlos ni cómo corregirlos sin llegar al paso del FUR.
   */
  const muestraRadicacion = isVin;
  /** Traspaso: nombre del organismo de tránsito devuelto por el RUNT (read-only en paso 1). */
  const transitOfficeNombreTraspaso = !isVin
    ? fieldValues.find((f) => f.fieldKey === 'transit_office_name')?.valueText?.trim() ?? null
    : null;
  const [secretarias, setSecretarias] = useState<TransitOfficeOption[]>([]);
  const [secretariasError, setSecretariasError] = useState<string | null>(null);
  const [transitOfficeId, setTransitOfficeId] = useState('');
  /** Prioridad del trámite YA creado (en creación diferida manda la del shell, que viaja con el id). */
  const [prioritarioInstancia, setPrioritarioInstancia] = useState(false);
  /**
   * Consulta desacoplada ya resuelta, a la espera de que estén los datos que viajan con ella a la
   * creación. Se guarda en vez de emitirse en el acto porque el organismo de tránsito se elige
   * DESPUÉS de consultar (orden del diseño) y sin él no se puede crear el trámite.
   */
  const [pendingPreview, setPendingPreview] = useState<Omit<
    PendingConsulta,
    'transitOfficeId'
  > | null>(null);

  useEffect(() => {
    if (!muestraRadicacion) return;
    let active = true;
    void tramitesClient
      .listTransitOffices()
      .then((list) => {
        if (active) setSecretarias(list);
      })
      .catch(() => {
        if (active) setSecretariasError('No se pudieron cargar los organismos de tránsito.');
      });
    return () => {
      active = false;
    };
  }, [muestraRadicacion]);

  /**
   * Dígito de preferencia de placa (HU #10805) declarado aquí, donde lo ubica el diseño. Es el MISMO
   * dato que el paso del FUR sigue ofreciendo al radicar sin placa (`plate_preferred_last_digit`):
   * una guía para que el OT elija una placa terminada en ese número al asignarla desde su rango
   * —en la consola del organismo esas placas salen marcadas y ordenadas primero—. No enruta nada:
   * el trámite cae por preasignación por NO llevar placa, no por este dígito.
   *
   * `preasignacionActiva` (HU #10806) responde si la ruta está viva para esta compañía en ESE
   * organismo; sin ella el OT no asigna desde un rango y el dígito no tendría a quién guiar.
   * `null` = todavía consultando.
   */
  const [digitoPlaca, setDigitoPlaca] = useState('');
  const [preasignacionActiva, setPreasignacionActiva] = useState<boolean | null>(null);

  /**
   * AC2 (HU #10799) — el vehículo YA tiene placa según el RUNT: la preasignación no aplica en
   * absoluto. Misma regla y misma señal que `PlacaPreasignadaSection` en el paso del FUR
   * (`vinTienePlacaRunt`): placa presente y `source === 'consultation'`, es decir traída por la
   * consulta, no elegida por el gestor. Un VIN ya matriculado no necesita que el OT le asigne nada,
   * así que no se ofrece el dígito, no se consulta el estado de la ruta y no se marca `plate_route_active`.
   */
  const placaRunt = fieldValues.find(
    (f) => f.fieldKey === 'plate' && f.source === 'consultation',
  )?.valueText?.trim() ?? '';
  const vehiculoConPlacaRunt = placaRunt !== '';


  // FEATURE 02 — política "solo vehículos propios" por familia (MATRICULAS | TRASPASO) y NIT del tenant.
  // En traspaso (placa) se autorrellena el documento del propietario con el NIT y, si se edita a otro,
  // se bloquea la consulta al RUNT. En matrícula (VIN) el flag aplica si el flujo de placa entra en juego.
  const [onlyOwnVehicles, setOnlyOwnVehicles] = useState(false);
  const [familyBlocked, setFamilyBlocked] = useState(false);
  const tenantNitDigits = normalizeNitDigits(
    (decodeJwtPayload(getToken())?.company_nit as string | undefined) ?? '',
  );
  const ownershipAutofilled = useRef(false);
  // Con la política activa el propietario NO es un dato que el gestor decida: es la compañía. El
  // campo queda de solo lectura durante todo el trámite (antes se dejaba editar y solo se rechazaba
  // al consultar, lo que invitaba a escribir un NIT ajeno para descubrir después que no se podía).
  // La regla la sigue imponiendo el backend (VehicleOwnershipGuard); esto evita el intento.
  const ownerDocLocked = !isVin && onlyOwnVehicles && !!tenantNitDigits;

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
    // Decisiones de radicación. Sin esto, al volver al paso 1 sobre un trámite YA creado la tarjeta
    // aparecía vacía: los tres datos existen en el expediente pero el paso no los releía, así que
    // el organismo elegido hace un minuto se mostraba como "Aún no has seleccionado la secretaría".
    setTransitOfficeId((v) => v || byKey('transit_office_id'));
    setDigitoPlaca((v) => v || byKey('plate_preferred_last_digit'));
    // La prioridad NO está en field_values (es una columna del expediente), por eso viaja aparte en
    // el detalle. Es la única de las tres que necesitó exponerse en el contrato.
    setPrioritarioInstancia(detail.prioritario ?? false);
  };

  // Rehidrata los inputs desde los field_values guardados de la instancia.
  useEffect(() => {
    if (!instanceId) return;
    // Rehidratación al montar: los setState ocurren tras el await (no síncronos).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadInstance().catch(() => {});
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [instanceId]);

  // HU #10478 + config de compañía (bloqueo / solo propios por familia).
  useEffect(() => {
    let active = true;
    void tramitesClient
      .getConsultationConfig()
      .then((cfg) => {
        if (!active) return;
        setPlatePrimaryProvider(cfg.vehiclePlate);
        const byFamily = cfg.onlyOwnVehiclesByFamily;
        const block = cfg.blockProcedureFamily;
        if (isVin) {
          setOnlyOwnVehicles(byFamily?.matriculas ?? false);
          setFamilyBlocked(block?.matriculas ?? false);
        } else {
          setOnlyOwnVehicles(byFamily?.traspaso ?? cfg.onlyOwnVehicles);
          setFamilyBlocked(block?.traspaso ?? false);
        }
      })
      .catch(() => {});
    return () => {
      active = false;
    };
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
    // Familia bloqueada en config de compañía: no consultar ni crear.
    if (familyBlocked) {
      setError(
        isVin
          ? 'La compañía tiene bloqueada la creación de trámites de matrículas. Contacta al administrador.'
          : 'La compañía tiene bloqueada la creación de trámites de traspaso. Contacta al administrador.',
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
          // `|| null`, no a secas: el organismo se elige DESPUÉS de consultar, así que aquí el
          // estado suele ser cadena vacía. Mandarla tal cual rompía la consulta antes de llegar al
          // handler — el binder no sabe leer "" como `Guid?` y devuelve un 400 con cuerpo vacío,
          // que en pantalla se veía como "Revisa los datos e inténtalo de nuevo".
          transitOfficeId: eligeSecretaria ? transitOfficeId || null : null,
        });
        setPreviewSnapshot(result.preflight);
        setFieldValues(result.vehicleFields);
        // AC2 (HU #10799) — el vehículo consultado ya trae placa del RUNT: lo que se hubiera
        // declarado sobre otro VIN deja de aplicar. Se borra la preferencia (también la anotada
        // para la creación) y se apaga la ruta, o viajaría un `plate_route_active` en true que el
        // trigger leería al radicar un trámite que no necesita que le asignen placa.
        if (result.vehicleFields.some(
          (f) => f.fieldKey === 'plate' && (f.valueText ?? '').trim() !== '',
        )) {
          setPreasignacionActiva(null);
          if (digitoPlaca) handleDigitoPlaca('');
          upsertLocal([{ fieldKey: 'plate_route_active', valueText: 'false' }]);
        }
        // Un 200 con el semáforo en rojo NO es una excepción: sin esto el shell solo sabría que
        // "hay consulta" y habilitaría Continuar aunque el vehículo no exista en el RUNT.
        const previewChecks = result.preflight?.checks ?? [];
        // Se guarda la consulta y se emite en un efecto: el organismo se elige DESPUÉS de consultar,
        // y "Continuar y guardar" —que es lo que crea el trámite— no puede habilitarse sin él.
        setPendingPreview({
          previewToken: result.previewToken,
          vin: isVin ? vin.trim() : undefined,
          plate: isVin ? undefined : plate.trim(),
          ownerDocumentType: isVin ? undefined : ownerDocType,
          ownerDocumentNumber: isVin ? undefined : ownerDocNumber.trim(),
          hardBlocked:
            previewChecks.some((c) => c.status === 'error') ||
            previewChecks.some((c) => c.key === 'vehiculo' && c.status === 'fail'),
          red: result.preflight?.overall === 'red',
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
      } else if (isTransitOfficeUnavailable(err)) {
        // HU #11199 (AC3) / HU #11200 (AC2/AC3) — el organismo no es utilizable. No es subsanable
        // desde el trámite: hasta que el administrador lo active y lo habilite no hay nada que hacer
        // aquí, así que se muestra el aviso en vez del error genérico y no se ofrece continuar.
        setError(ORGANISMO_NO_DISPONIBLE);
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

  // B4 (guardián de diseño) — misma clase duplicada a mano en dos sitios, ambas sin `focus:ring`.
  // Se usa `WIZARD_INPUT`, la única fuente para el anillo de foco de 2px.
  const inputClass = WIZARD_INPUT;

  const loading = preflightLoading || persisting;
  // Con creación diferida el semáforo vive en memoria (no hay snapshot persistido que releer).
  const effectivePreflight = deferred ? previewSnapshot : preflight;
  const hasResult = !!effectivePreflight?.overall;

  // CF-02 — editar el identificador invalida la consulta previa: el trámite solo puede crearse con
  // una consulta vigente para los datos que están en pantalla.
  const invalidatePreview = () => {
    if (!deferred) return;
    setPreviewSnapshot(null);
    setPendingPreview(null);
    onPreviewDone?.(null);
  };

  // La consulta solo habilita "Continuar y guardar" cuando además está el organismo de tránsito
  // (matrícula), que se elige después de consultar. Elegirlo NO invalida la consulta: sería borrar
  // lo que el gestor acaba de hacer, y el organismo viaja igualmente a la creación desde aquí.
  useEffect(() => {
    if (!deferred) return;
    if (!pendingPreview) return;
    if (eligeSecretaria && !transitOfficeId) {
      onPreviewDone?.(null);
      return;
    }
    onPreviewDone?.({
      ...pendingPreview,
      transitOfficeId: eligeSecretaria ? transitOfficeId : undefined,
    });
    // `onPreviewDone` es un callback del shell recreado en cada render: incluirlo re-emitiría en
    // bucle. Lo que gobierna la emisión es la consulta y el organismo.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [deferred, pendingPreview, eligeSecretaria, transitOfficeId]);

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

  useEffect(() => {
    // Sin organismo no hay nada que consultar. El estado vuelve a "cargando" al cambiar de
    // organismo desde el propio selector, no aquí: así el efecto solo escribe el resultado.
    if (!muestraRadicacion || !transitOfficeId || vehiculoConPlacaRunt) return;
    let active = true;
    /**
     * HU #10806 (Alternativa C) — la decisión de ruta se persiste como `plate_route_active`: es la
     * fuente que consume el trigger de BD para fijar `plate_flow_status = 'preasignado'` al radicar
     * sin placa. El paso del FUR hace exactamente esto al abrir su sección; aquí se anota con el
     * resto de lo capturado y viaja con la creación del trámite.
     */
    const persistRouteActive = (enabled: boolean) => {
      if (deferred) {
        upsertLocal([{ fieldKey: 'plate_route_active', valueText: String(enabled) }]);
        return;
      }
      if (!instanceId) return;
      void tramitesClient
        .patchFieldValues(instanceId, [
          { formFieldId: null, fieldKey: 'plate_route_active', valueText: String(enabled), valueJson: null },
        ])
        .catch(() => {
          /* no bloquear el paso si la persistencia falla; el submit sigue decidiendo la ruta */
        });
    };
    getPlatePreassignStatus(transitOfficeId)
      .then((s) => {
        if (!active) return;
        setPreasignacionActiva(s.enabled);
        persistRouteActive(s.enabled);
      })
      .catch(() => {
        // Mismo criterio que el paso del FUR ante un fallo de esta consulta: no bloquear el flujo.
        if (!active) return;
        setPreasignacionActiva(true);
        persistRouteActive(true);
      });
    return () => {
      active = false;
    };
    // `upsertLocal` se recrea en cada render; lo que gobierna la consulta es el organismo elegido.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [muestraRadicacion, transitOfficeId, vehiculoConPlacaRunt, deferred, instanceId]);

  /**
   * HU #10805 — dígito de preferencia. Sin trámite todavía se anota en memoria y viaja con la
   * creación (mismo mecanismo que paz y salvo o la aceptación de riesgo); sobre un borrador ya
   * creado hace su PATCH inmediato. La clave es la MISMA que lee el paso del FUR y que la consola
   * del OT usa para marcar las placas candidatas, así que declararlo aquí o allá es equivalente.
   */
  /**
   * Organismo elegido sobre un trámite YA creado. En creación diferida no hace nada: allí el id
   * viaja en el cuerpo de la creación y persistirlo antes sería escribir sobre un trámite que no
   * existe. Guarda también el nombre porque es lo que leen el FUR y el listado, que no resuelven
   * el id contra el catálogo.
   */
  const handleOrganismo = (id: string) => {
    if (deferred || !instanceId || !id) return;
    const nombre = secretarias.find((s) => s.id === id)?.name ?? '';
    void tramitesClient
      .patchFieldValues(instanceId, [
        { formFieldId: null, fieldKey: 'transit_office_id', valueText: id, valueJson: null },
        ...(nombre
          ? [{ formFieldId: null, fieldKey: 'transit_office_name', valueText: nombre, valueJson: null }]
          : []),
      ])
      .then(() => onRefresh())
      .catch(() => setError('No se pudo guardar el organismo de tránsito. Reintenta.'));
  };

  /**
   * Prioridad. Dos vías según el momento: antes de crear la marca viaja al shell, que la aplica con
   * `setPriority` en cuanto tiene el id; después se aplica en el acto. Optimista con reversión —es
   * una preferencia de orden en la bandeja del OT, no un requisito, y no merece bloquear la pantalla.
   */
  const prioritarioVigente = deferred ? prioritario : prioritarioInstancia;
  const handlePrioritario = (value: boolean) => {
    if (deferred) {
      onPrioritarioChange?.(value);
      return;
    }
    if (!instanceId) return;
    setPrioritarioInstancia(value);
    void tramitesClient
      .setPriority(instanceId, value)
      .then(() => onRefresh())
      .catch(() => setPrioritarioInstancia(!value));
  };

  const handleDigitoPlaca = (value: string) => {
    setDigitoPlaca(value);
    if (deferred) {
      upsertLocal([{ fieldKey: 'plate_preferred_last_digit', valueText: value }]);
      return;
    }
    if (!instanceId) return;
    void tramitesClient
      .patchFieldValues(instanceId, [
        { formFieldId: null, fieldKey: 'plate_preferred_last_digit', valueText: value, valueJson: null },
      ])
      .then(() => onRefresh())
      .catch(() => {});
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

  // Hay datos del vehículo cuando el RUNT ya respondió: permite mostrar condiciones y pre-vuelo.
  const hasVehicleData = fieldValues.some(
    (f) =>
      ['plate', 'vin', 'vehicle_brand', 'vehicle_line', 'vehicle_year'].includes(f.fieldKey) &&
      (f.valueText ?? '').trim() !== '',
  );

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

  // Trámite principal que gobierna el paso. Con creación diferida la trae la ruta; con el trámite
  // ya creado se deduce de la clave del paso, que es lo único que distingue las dos ramas aquí.
  const modalidadVigente: WizardModalidad =
    deferredModalidad ?? (isVin ? 'matricula_inicial' : 'traspaso');

  // Cambiar de tipo se confirma antes de navegar, como en la propuesta: al hacerlo cambian las
  // validaciones y los documentos exigidos, y lo poco que ya se hubiera capturado en este paso
  // (placa, documento del propietario, secretaría) se pierde al montar el otro asistente.
  const [pendingTipo, setPendingTipo] = useState<string | null>(null);

  // El identificador del vehículo está completo: el VIN en matrícula; la placa Y el documento del
  // propietario en traspaso, porque el RUNT los cruza y con uno solo la consulta no resuelve.
  const identificadorCompleto = isVin
    ? vin.trim().length > 0
    : plate.trim().length > 0 && ownerDocNumber.trim().length > 0;

  // Botón "Consultar RUNT" — azul pleno de marca, como en la propuesta.
  const consultButton = (
    <button
      type="button"
      onClick={() => void handleRun()}
      // Sin los datos que se van a consultar el botón no se habilita: pulsarlo solo devolvía un
      // error que el gestor ya podía ver por sí mismo mirando el campo vacío.
      // La secretaría ya NO gatea la consulta (antes sí, HU #11199 AC2): se elige después, sobre un
      // vehículo ya identificado. El requisito sigue vivo donde importa —la creación—, tanto en el
      // backend como en el gate de "Continuar y guardar".
      disabled={loading || familyBlocked || !identificadorCompleto}
      className={`${WIZARD_BTN} flex shrink-0 items-center justify-center gap-2 text-white focus-visible:ring-[#557EFF] disabled:cursor-not-allowed disabled:opacity-50`}
      style={{ background: WIZARD_CTA_GRADIENT }}
      aria-label={familyBlocked ? 'Consulta no permitida para esta compañía' : 'Consultar RUNT'}
    >
      <Search className="h-3.5 w-3.5" />
      {loading ? 'Consultando…' : hasResult ? 'Actualizar' : 'Consultar RUNT'}
    </button>
  );

  return (
    // `pr-16` reserva el ancho del carril de consulta anclado a la derecha (propuesta).
    <div className="space-y-3 pr-16">
      {/* La consulta al RUNT tarda segundos. Hasta ahora la única señal era el rótulo del botón, que
          se pierde en cuanto la atención se va a otra parte de la pantalla; la propuesta cubre la
          espera con la escena del vehículo y dice ante quién se está esperando.

          Cuelga de `persisting` y NO de `loading`: `loading` incluye `preflightLoading`, que también
          se enciende cuando el pre-vuelo se recarga solo al abrir el paso. Con eso el velo salía en
          cada entrada al asistente sin que nadie hubiera pulsado nada. `persisting` solo lo levanta
          el gestor al consultar. El rótulo del botón sigue usando `loading`: ahí sí interesa que se
          vea ocupado por cualquier motivo. */}
      {persisting && <CarLoaderModal mode="runt" />}
      <WizardHelpRail
        modalidad={modalidadVigente}
        transitOfficeId={transitOfficeId || undefined}
      />

      {/* 1ª tarjeta: Configuración del Trámite. La propuesta elige el tipo con tarjetas, no con un
          desplegable: son tres opciones fijas, cada una con una frase que dice qué resuelve, y así
          se leen de un vistazo en vez de tener que abrir una lista. Operable solo mientras el
          trámite no existe (creación diferida): ahí cambiar de tipo es navegar al otro, no hay nada
          creado que migrar. Con el trámite ya creado la modalidad gobierna sus pasos y documentos,
          así que queda fija y las demás tarjetas se apagan. */}
      <WizardAccordion title="Configuración del Trámite" defaultOpen>
        <p className="text-xs opacity-70 mb-3">Define el trámite principal que se radicará con este expediente.</p>
        <fieldset className="mt-4">
          <legend className="text-xs font-semibold">Tipo de Trámite Principal</legend>
          <div className="mt-2 grid grid-cols-1 gap-3 sm:grid-cols-3">
            {MODALIDAD_OPCIONES.map((o) => {
              const activa = o.id === modalidadVigente;
              // `disponible: false` (Otros Trámites) es una familia que la propuesta contempla y el
              // sistema todavía no radica. Se muestra —está en el diseño y el gestor pregunta por
              // ella— pero apagada y anunciada como tal, en vez de ofrecer un camino sin salida.
              const seleccionable = o.disponible && deferred && !readOnly && !activa;
              return (
                <button
                  key={o.id}
                  type="button"
                  onClick={() => setPendingTipo(o.id)}
                  disabled={!seleccionable}
                  aria-current={activa ? 'step' : undefined}
                  className="w-full rounded-xl border p-3.5 text-left transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:cursor-not-allowed enabled:hover:border-[#557EFF]"
                  style={
                    activa
                      ? {
                          borderColor: '#557EFF',
                          background: '#EFF6FF',
                          boxShadow: '0 0 0 3px rgba(85,126,255,0.15)',
                        }
                      : { opacity: o.disponible ? 1 : 0.55 }
                  }
                >
                  <p
                    className="text-xs font-semibold"
                    style={{ color: activa ? '#557EFF' : '#162744' }}
                  >
                    {o.label}
                  </p>
                  <p className="mt-0.5 text-xs opacity-70">
                    {o.disponible ? o.descripcion : `${o.descripcion} · aún no disponible`}
                  </p>
                </button>
              );
            })}
          </div>
        </fieldset>
      </WizardAccordion>

      {/* 2ª tarjeta: Consulta del Vehículo. En la propuesta la consulta tiene su propia tarjeta,
          con el identificador y el CTA en una línea. Los campos son los que pide cada modalidad:
          el VIN en matrícula; placa, tipo y número de documento del propietario en traspaso. */}
      <WizardAccordion title="Consulta del Vehículo" defaultOpen>
        <p className="text-xs opacity-70 mb-3">{`Validamos ${isVin ? 'el VIN' : 'la placa'} en el RUNT antes de configurar el trámite.`}</p>


        <div className="mt-4 flex flex-wrap items-end gap-4">
          {isVin ? (
            <div className="min-w-0 max-w-md flex-1">
              <label htmlFor="consulta-vin" className={WIZARD_LABEL}>
                Número VIN
              </label>
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
                className={`mt-1 ${inputClass} disabled:opacity-60`}
                placeholder="Ej. LZWCDAGA4SC802801"
              />
            </div>
          ) : (
            <>
              <div className="w-36">
                <label htmlFor="consulta-plate" className={WIZARD_LABEL}>
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
                  // Prototipo FLIT: la placa se lee como código —mayúscula y espaciada—.
                  // El placeholder vuelve a texto normal para no leerse como un valor cargado.
                  className={`mt-1 ${inputClass} font-semibold uppercase tracking-[0.14em] placeholder:font-normal placeholder:normal-case placeholder:tracking-normal disabled:opacity-60`}
                  placeholder="Ej. ABC123"
                />
              </div>
              {!hideOwnerDocType && (
                <div className="w-44">
                  <label htmlFor="consulta-owner-doc-type" className={WIZARD_LABEL}>
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
                    // Un <select> no admite readOnly: con la política activa se deshabilita,
                    // el valor (NIT) igual viaja porque vive en estado de React, no en un submit.
                    disabled={readOnly || ownerDocLocked}
                    className={`mt-1 ${inputClass} disabled:opacity-60`}
                  >
                    {DOC_TYPES.map((t) => (
                      <option key={t} value={t}>
                        {t}
                      </option>
                    ))}
                  </select>
                </div>
              )}
              <div className="min-w-56 max-w-xs flex-1">
                <label htmlFor="consulta-owner-doc-number" className={WIZARD_LABEL}>
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
                  // readOnly (no disabled): el gestor debe poder ver, enfocar y copiar el NIT, y
                  // el campo sigue en el orden de tabulación y se anuncia como de solo lectura.
                  readOnly={ownerDocLocked}
                  aria-describedby={ownerDocLocked ? 'consulta-owner-doc-locked' : undefined}
                  className={`mt-1 ${inputClass} disabled:opacity-60 ${
                    ownerDocLocked ? 'cursor-not-allowed bg-[#F4F6FA] dark:bg-[#131A22]' : ''
                  }`}
                  placeholder="Ej. 1020304050"
                />
              </div>
            </>
          )}
          {!readOnly && consultButton}
        </div>

        {/* Notas de los campos de arriba: a lo ancho de la tarjeta, no dentro de una columna,
            donde descuadraban la altura de su campo. */}
        {(ownerDocLocked ||
          (!hideOwnerDocType && ownerDocTypeSuggested) ||
          (deferred && deferredModalidad === 'traspaso')) && (
          <div className="mt-3 space-y-1.5">
            {/* El fondo gris por sí solo no comunica "no editable": se dice con texto, y este
                párrafo es además el nombre accesible del estado (aria-describedby del campo). */}
            {ownerDocLocked && (
              <p id="consulta-owner-doc-locked" className="text-xs leading-tight opacity-70">
                Tu compañía solo tramita vehículos propios, así que el propietario queda fijo en su
                NIT y no se puede editar durante el trámite.
              </p>
            )}
            {!hideOwnerDocType && ownerDocTypeSuggested && (
              <p className="text-xs leading-tight opacity-70">
                No se encontró el vehículo en RUNT. Si es maquinaria o remolque, verifica el tipo de
                documento del propietario (p. ej. NIT) y vuelve a consultar.
              </p>
            )}
          </div>
        )}

        {/* Matrícula: el resultado de la consulta vive DENTRO de esta tarjeta, como en la propuesta
            —la franja con la placa y la retícula de datos aparecen bajo el campo que los trajo—.
            En traspaso el diseño lo saca a un acordeón propio, que se pinta más abajo. */}
        {/* El resultado de la consulta vive DENTRO de la tarjeta que lo trajo, en las dos
            modalidades. En traspaso salía a un acordeón propio porque así lo hace el otro asistente
            de la propuesta; el dato es el mismo y el sitio debe ser el mismo. Además un desplegable
            sobre el resultado que el gestor viene a ver es un clic para nada. */}
        {hasVehicleData && (
          <div className="mt-4 border-t pt-4">
            <VehicleDataCard fieldValues={fieldValues} bare validadoEnRunt />
          </div>
        )}
      </WizardAccordion>

      {/* 3ª tarjeta: Organismo de Tránsito y Radicación (HU #11199 — solo matrícula y solo mientras
          el trámite no existe). La propuesta le da tarjeta propia después de la consulta: es una
          decisión de radicación, no un parámetro más del vehículo. */}
      {/* Tras la consulta: dónde se radica es una decisión sobre un vehículo YA identificado, y ese
          es el orden del diseño. `/preflight-preview` acepta consultar sin organismo; el requisito
          vive en la creación (`CreateFromConsultaCommand`) y, en pantalla, en el gate de
          "Continuar y guardar". */}
      {muestraRadicacion && hasVehicleData && (
        <WizardAccordion title="Organismo de Tránsito y Radicación" defaultOpen>
          <p className="text-xs opacity-70 mb-3">Selecciona la secretaría donde se radicará el expediente.</p>
          <div className="mt-4 grid grid-cols-1 gap-4 lg:grid-cols-3">
          <div className="min-w-0">
            <span className={`mb-1 block ${WIZARD_LABEL}`}>Secretaría de tránsito *</span>
            <TransitOfficeSearchPicker
              offices={secretarias}
              valueId={transitOfficeId}
              onChange={(id) => {
                // NO invalida la consulta: se elige después de consultar y la consulta no se corrió
                // contra ningún organismo. Borrarla sería deshacer lo que el gestor acaba de hacer;
                // el id viaja a la creación desde aquí y allí se valida.
                setTransitOfficeId(id);
                setError(null);
                // La preasignación es del organismo: al cambiarlo, el estado vuelve a "consultando"
                // y la preferencia de dígito se reinicia (también la ya anotada para la creación),
                // o viajaría una preferencia que el organismo nuevo quizá ni atiende.
                setPreasignacionActiva(null);
                if (digitoPlaca) handleDigitoPlaca('');
                handleOrganismo(id);
              }}
              disabled={readOnly}
              describedBy="consulta-secretaria-aviso"
            />
            {/* Aviso ámbar mientras falta: sin secretaría la consulta no se habilita, y el botón
                deshabilitado por sí solo no dice por qué. */}
            {!transitOfficeId && (
              <p className="mt-1.5 text-xs font-medium leading-tight" style={{ color: '#B45309' }}>
                Aún no has seleccionado la secretaría de tránsito.
              </p>
            )}
            <p id="consulta-secretaria-aviso" className="mt-1 text-xs leading-tight opacity-70">
              {SECRETARIA_LISTA_AVISO}
            </p>
            {secretariasError && (
              <p className="mt-1 text-xs leading-tight" style={{ color: '#E5484D' }}>
                {secretariasError}
              </p>
            )}
          </div>

          {/* Dígito de preasignación (HU #10805) — la misma preferencia que el paso del FUR ofrece
              al radicar sin placa (`plate_preferred_last_digit`, ver PlacaPreasignadaSection), aquí
              declarada de entrada porque es donde la pone el diseño. Solo se ofrece si el organismo
              elegido tiene la ruta de preasignación activa para la compañía (HU #10806): con ella
              apagada el OT no asigna placa desde un rango y el dígito no tendría a quién guiar. */}
          <div className="min-w-0">
            <label htmlFor="consulta-digito-placa" className={`mb-1 block ${WIZARD_LABEL}`}>
              Dígito de preasignación de placa
            </label>
            {/* AC2 (HU #10799) — con placa del RUNT no hay nada que preasignar: se dice, con la
                placa delante, en vez de dejar un selector apagado que parece un fallo. Es la misma
                salida temprana que hace el paso del FUR. */}
            {vehiculoConPlacaRunt ? (
              <p
                id="consulta-digito-placa-nota"
                className="mt-1 rounded-xl border border-dashed px-3 py-2 text-xs leading-tight opacity-80"
              >
                El vehículo ya tiene placa según el RUNT (
                <span className="font-mono font-semibold">{placaRunt}</span>
                ). No aplica la preasignación de placa.
              </p>
            ) : (
              <>
                <select
                  id="consulta-digito-placa"
                  value={digitoPlaca}
                  onChange={(e) => handleDigitoPlaca(e.target.value)}
                  disabled={readOnly || !transitOfficeId || preasignacionActiva !== true}
                  aria-describedby="consulta-digito-placa-nota"
                  className={`${inputClass} disabled:opacity-60`}
                >
                  <option value="">Sin preferencia</option>
                  {Array.from({ length: 10 }, (_, i) => (
                    <option key={i} value={String(i)}>{`Termina en ${i}`}</option>
                  ))}
                </select>
                {/* Cuatro estados más, porque un selector apagado sin explicación se lee como un
                    fallo: falta el organismo · consultando · sin preasignación · disponible. */}
                <p id="consulta-digito-placa-nota" className="mt-1 text-xs leading-tight opacity-70">
                  {!transitOfficeId
                    ? 'Elige primero la secretaría: la preasignación depende del organismo donde radiques.'
                    : preasignacionActiva === null
                      ? 'Consultando si el organismo tiene preasignación de placa…'
                      : preasignacionActiva === false
                        ? 'Este organismo (o tu compañía) no tiene preasignación de placa activa: el trámite se entregará de forma estándar.'
                        : 'Si radicas sin placa, indica el número en el que prefieres que termine. El organismo lo toma como guía; podrás cambiarlo en el paso final.'}
                </p>
              </>
            )}
          </div>

          </div>
        </WizardAccordion>
      )}

      {/* Traspaso: Organismo de Tránsito del RUNT (read-only). En matrícula el organismo se elige
          en la tarjeta de radicación de arriba; en traspaso viene del RUNT y es de solo lectura. */}
      {!isVin && hasVehicleData && transitOfficeNombreTraspaso && (
        <WizardAccordion title="Organismo de Tránsito" defaultOpen>
          <p className="text-xs opacity-70 mb-3">Organismo de tránsito registrado en el RUNT para este vehículo (solo lectura).</p>
          <div
            className="mt-2 flex items-center gap-3 rounded-xl border px-4 py-3"
            style={{ borderColor: '#DFE5ED', background: 'rgba(85,126,255,0.04)' }}
          >
            <span className="text-xs font-semibold" style={{ color: '#162744' }}>
              {transitOfficeNombreTraspaso}
            </span>
          </div>
        </WizardAccordion>
      )}

      {/* Trámite prioritario (HU #10536). Vivía DENTRO de la tarjeta de organismo, que solo existe
          en matrícula, así que en traspaso no había forma de marcarlo desde el asistente — solo
          después, desde el listado. La prioridad no depende del organismo: es una columna del
          expediente (`procedure_instances.prioritario`) con su propio endpoint. Sale a su propia
          tarjeta y existe en las dos modalidades, en el mismo sitio. */}
      {hasVehicleData && (
        <WizardAccordion title="Trámite prioritario" defaultOpen>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="min-w-0">
              <p id="consulta-prioritario-nota" className="text-xs leading-tight opacity-70">
                Prioriza la gestión de este expediente: el organismo lo revisa con primacía.{' '}
                {deferred
                  ? 'Se aplica al crear el trámite y podrás cambiarlo después desde el listado.'
                  : 'El cambio se guarda al instante; también puedes alternarlo desde el listado.'}
              </p>
            </div>
            <button
              type="button"
              onClick={() => handlePrioritario(!prioritarioVigente)}
              disabled={readOnly}
              aria-pressed={prioritarioVigente}
              aria-describedby="consulta-prioritario-nota"
              className="flex h-[38px] shrink-0 items-center justify-between gap-3 rounded-xl border bg-white px-3 text-xs font-medium transition disabled:opacity-60 dark:bg-[#162744]"
              style={prioritarioVigente ? { borderColor: '#557EFF', color: '#557EFF' } : undefined}
            >
              {prioritarioVigente ? 'Activado' : 'Desactivado'}
              <span
                aria-hidden="true"
                className="relative inline-block h-5 w-9 rounded-full transition-colors"
                style={{ background: prioritarioVigente ? '#557EFF' : '#DFE5ED' }}
              >
                <span
                  className={`absolute top-0.5 h-4 w-4 rounded-full bg-white transition-all ${
                    prioritarioVigente ? 'left-[1.125rem]' : 'left-0.5'
                  }`}
                />
              </span>
            </button>
          </div>
        </WizardAccordion>
      )}

      {error && (
        <p
          className="text-xs font-medium"
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

      {/* Datos consolidados del vehículo. Solo traspaso: es el asistente de la propuesta que los
          saca a un acordeón propio (en matrícula van dentro de la tarjeta de consulta). Abierto de
          entrada —es el resultado de la consulta, lo que el gestor viene a ver—.

          Aparece SOLO cuando hay datos. Antes se pintaba siempre, con una píldora "Pendiente" y una
          frase pidiendo consultar: una sección vacía que ocupaba sitio para repetir lo que la
          tarjeta de arriba ya dice, con su botón al lado. Es andamio, no información — y en
          matrícula, donde estos mismos datos viven dentro de la tarjeta de consulta, no aparece
          nada hasta consultar. Las dos modalidades se comportan ya igual. */}

      {mostrarPazSalvo && (
        <label
          className="flex items-start gap-2.5 rounded-2xl border p-4 bg-white dark:bg-[#162744]"
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

      {/* Semáforo de requisitos. Abierto de entrada y con el resultado global en la cabecera: es lo
          que decide si el trámite puede seguir, así que plegado tiene que seguir diciéndolo. */}
      {(hasVehicleData || !!effectivePreflight) && (
        <WizardAccordion
          title="Pre-vuelo de requisitos (RUNT · SIMIT · RNMC)"
          defaultOpen
          badge={
            preflightOverall(effectivePreflight?.overall) ? (
              <StatusBadge
                label={preflightOverall(effectivePreflight?.overall)!.label}
                tone={preflightOverall(effectivePreflight?.overall)!.tone}
              />
            ) : null
          }
        >
          <PreflightPanel
            snapshot={effectivePreflight}
            loading={loading}
            onRun={() => void handleRun()}
            riesgoAceptado={riesgoAceptado}
            onToggleRiesgo={(v) => void handleRiesgo(v)}
            saving={riesgoSaving}
            showRunButton={false}
            bare
            onIniciarTraspaso={isVin ? handleIniciarTraspaso : undefined}
            esMigrado={esMigrado}
          />
        </WizardAccordion>
      )}

      {/* Confirmación de cambio de tipo de trámite (propuesta: modal "Cambiar tipo de trámite").
          B5/B6 (guardián de diseño) — migrado a `WizardModal`: overlay opaco de marca (antes
          `bg-[#162744]/40 backdrop-blur-sm`, uno de los cuatro overlays distintos del asistente) y
          trampa de foco + retorno de foco + Escape, en vez de un `<div role="dialog">` a mano. */}
      {pendingTipo && (
        <WizardModal title="Cambiar tipo de trámite" onClose={() => setPendingTipo(null)}>
          <p className="text-xs leading-relaxed opacity-80">
            ¿Deseas cambiar el tipo de trámite? Se actualizarán las validaciones y los documentos
            requeridos, y se perderá lo capturado en este paso.
          </p>
          <div className="mt-5 flex justify-end gap-2">
            <button
              type="button"
              onClick={() => setPendingTipo(null)}
              className="rounded-xl border px-4 py-2 text-xs font-medium focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
            >
              Cancelar
            </button>
            <button
              type="button"
              onClick={() => router.push(`/tramites/nuevo/${pendingTipo}`)}
              className="rounded-xl px-5 py-2 text-xs font-semibold text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
              style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
            >
              Sí, cambiar
            </button>
          </div>
        </WizardModal>
      )}
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
  prendaFormRef,
  onActorsConsultationGateChange,
  identityOperable = false,
  identityApproved = false,
  vaultCoveredPartes = [],
  rnmcEnabled = false,
  esMigrado = false,
  prendaDocumentRequired = true,
  onPrendaDocumentGateChange,
  deferredModalidad,
  seedVin,
  seedPlaca,
  onPreviewDone,
  onPendingFieldValues,
  prioritario = false,
  onPrioritarioChange,
  onTipoServicioGateChange,
  paqueteDocsStatus = 'idle',
  onPaqueteStatusChange,
  onConfirmacionesExpedienteChange,
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
  /** HU #10536 — marca de prioridad del paso 1; se aplica al crear el trámite. */
  prioritario?: boolean;
  onPrioritarioChange?: (value: boolean) => void;
  /** Gate Continuar: tipo de servicio (+ empresa vinculadora si es PÚBLICO) completo en requisitos. */
  onTipoServicioGateChange?: (ok: boolean) => void;
  preflight: PreflightSnapshot | null;
  preflightLoading: boolean;
  onRunPreflight: () => Promise<void>;
  onRefresh: () => void;
  stepFormRef: RefObject<WizardStepFormHandle | null>;
  prendaFormRef: RefObject<WizardStepFormHandle | null>;
  /** Gate Continuar en pasos de actores (consulta RUNT/RUES exitosa). */
  onActorsConsultationGateChange?: (ready: boolean) => void;
  /** FEATURE 05 — el RNMC aplica al trámite: los actores muestran la fecha de expedición. */
  rnmcEnabled?: boolean;
  /** Migración V1→V2 — el trámite viene de V1; el paso de consulta lo explica en el pre-vuelo. */
  esMigrado?: boolean;
  /** Compañía+OT: certificado de prenda obligatorio (default) u opcional. */
  prendaDocumentRequired?: boolean;
  /** Gate Continuar: certificado de prenda listo (o no exigible). */
  onPrendaDocumentGateChange?: (ready: boolean) => void;
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
  /** Confirmaciones del expediente consolidado que siguen sin marcar (texto legible). */
  onConfirmacionesExpedienteChange?: (pendientes: string[]) => void;
  /** Feature #11066 — marca dirty en el shell (p.ej. checklist de docs editado). */
  onMarkDirty?: () => void;
}) {
  // Los datos comerciales dejaron de tener paso propio: viven en Requisitos. La clave `comercial`
  // solo puede llegar desde un borrador antiguo que quedó apuntando ahí, así que se normaliza a
  // `documentos` — el gestor encuentra lo que dejó a medias, en su nuevo sitio.
  switch (step.key === 'comercial' ? 'documentos' : step.key) {
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
          prioritario={prioritario}
          onPrioritarioChange={onPrioritarioChange}
          esMigrado={esMigrado}
        />
      );

    // Paso 2 de ambas modalidades = Datos y Documentos del Trámite.
    // Orden PDF/Lovable (Oleada 2): Tipo servicio → Comercial (traspaso) → Prenda → Gestión docs
    // → Trámites simultáneos → Observaciones.
    case 'documentos':
      return (
        <div className="space-y-3">
          {/* Tipo de servicio (casilla 18) — solo matrícula. En traspaso solo leasing.
              hideTransformaciones=true: VehicleTransformationsCard se pinta más abajo,
              después del checklist, como pide el PDF. */}
          <WizardAccordion
            title={
              modalidad === 'traspaso' ? 'Condiciones del trámite' : 'Tipo de servicio del vehículo'
            }
            defaultOpen
            level="h3"
          >
            <DeclaracionesTramite
              instanceId={instanceId}
              modalidad={modalidad}
              onChanged={() => {
                onMarkDirty?.();
                onRefresh?.();
              }}
              onTipoServicioGateChange={onTipoServicioGateChange}
              hideTransformaciones
              noCardWrapper
            />
          </WizardAccordion>

          {/* Datos comerciales del traspaso. Sin paso propio desde que se movió aquí; el guardado
              cuelga del pie ("Continuar y guardar") vía stepFormRef. */}
          {modalidad === 'traspaso' && (
            <WizardAccordion title="Datos comerciales y avalúo" defaultOpen level="h3">
              <CommercialForm
                key="comercial-en-requisitos"
                ref={stepFormRef}
                instanceId={instanceId}
                onSaved={onRefresh}
                hideHeader
                embeddedInWizard
              />
            </WizardAccordion>
          )}

          {/* Prenda: declarativa en matrícula; gate con decisiones en traspaso. */}
          <WizardAccordion title="Prenda e inscripción" defaultOpen level="h3">
            {(() => {
              const gravamen = preflight?.checks?.find((c) => c.key === 'gravamenes');
              const esTraspaso = modalidad === 'traspaso';
              return (
                <PrendaForm
                  ref={prendaFormRef}
                  instanceId={instanceId}
                  onSaved={onRefresh}
                  embeddedInWizard
                  modalidad={esTraspaso ? 'traspaso' : 'matricula_inicial'}
                  decisions={esTraspaso ? traspasoDecisions(prendaDocumentRequired) : undefined}
                  documentRequired={prendaDocumentRequired}
                  onDocumentGateChange={onPrendaDocumentGateChange}
                  runtHasGravamen={gravamen?.status === 'warn'}
                  runtGravamenMessage={gravamen?.message}
                  hideHeader
                />
              );
            })()}
          </WizardAccordion>

          {/* Gestión de documentos. */}
          <WizardAccordion title="Documentos del trámite" defaultOpen level="h3">
            <DocumentChecklist
              instanceId={instanceId}
              onChanged={() => {
                onMarkDirty?.();
                onRefresh?.();
              }}
              hideHeader
              modalidad={modalidad}
            />
          </WizardAccordion>

          {/* Trámites simultáneos (PDF: después de Docs, antes de Observaciones).
              TramiteSimultaneosField maneja su propio estado para no acoplarse a
              DeclaracionesTramite. Solo se pinta cuando el vehículo ya tiene datos. */}
          <WizardAccordion title="Trámites Simultáneos (Opcional)" level="h3">
            <TramiteSimultaneosField instanceId={instanceId} hideHeader />
          </WizardAccordion>

          {/* Observaciones — cierre del paso, después de los documentos. */}
          <WizardAccordion title="Observaciones del trámite" level="h3">
            <TramiteObservacionesField instanceId={instanceId} hideCardWrapper />
          </WizardAccordion>
        </div>
      );

    // Traspaso: vendedor y comprador se unifican en un solo formulario (2 tarjetas).
    // Matrícula sigue con comprador en layout split. key estable `actores` evita remontar
    // al pasar del índice server vendedor↔comprador.
    case 'comprador':
    case 'vendedor': {
      if (modalidad === 'traspaso') {
        return (
          <ActorsForm
            key="actores-unificados"
            ref={stepFormRef}
            instanceId={instanceId}
            modalidad="traspaso"
            roles={['vendedor', 'comprador']}
            onSaved={onRefresh}
            embeddedInWizard
            seedDocumentoFromOwner
            autoConsultRunt
            rnmcEnabled={rnmcEnabled}
            onConsultationGateChange={onActorsConsultationGateChange}
          />
        );
      }
      return (
        <ActorsForm
          key={step.key}
          ref={stepFormRef}
          instanceId={instanceId}
          modalidad="matricula_inicial"
          roles={['comprador']}
          onSaved={onRefresh}
          embeddedInWizard
          layout="split"
          rnmcEnabled={rnmcEnabled}
          onConsultationGateChange={onActorsConsultationGateChange}
        />
      );
    }

    // Los datos comerciales dejaron de tener paso propio: viven en Requisitos. Esta clave solo
    // puede llegar desde un borrador antiguo que quedó apuntando aquí, así que cae en Requisitos,
    // que es donde ahora están sus datos — el gestor encuentra lo que dejó a medias.
    // Matrícula paso 4 = Identidad (biométrica del comprador, parte única).
    // Título + subtítulo van dentro del panel blanco de BiometricStep.
    case 'identidad': {
      const biometric = (
        <BiometricStep
          instanceId={instanceId}
          modalidad={modalidad}
          onRefresh={onRefresh}
          hideIntro
          heading="Identidad"
          headingSubtitle={STEP_SUBTITLE.identidad}
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

    // Resumen del trámite (matrícula 5 / traspaso 6; key `fur`). Feature #11211: la biométrica
    // pendiente se embebe en Comprador/Vendedor del MatriculaResumen (sin bloque suelto arriba).
    case 'fur': {
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
                  Radicar igual y regenerarlos al entregar.
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
          {/* Mandatario (Persona/RL): disclosure dentro de MatriculaResumen (FirmaFurStep). */}
          <FirmaFurStep
            key={`${instanceId ?? 'new'}-${instanceStatus ?? 'borrador'}`}
            instanceId={instanceId}
            modalidad={modalidad}
            onRefresh={onRefresh}
            rnmcEnabled={rnmcEnabled}
            onPaqueteStatusChange={onPaqueteStatusChange}
            onConfirmacionesExpedienteChange={onConfirmacionesExpedienteChange}
            vaultCoveredPartes={vaultCoveredPartes}
            biometricForceEditable={identityOperable}
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
