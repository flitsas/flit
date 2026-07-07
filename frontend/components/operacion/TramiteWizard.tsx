'use client';

import { useEffect, useRef, useState, type RefObject } from 'react';
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
  Search,
  Shield,
  Tag,
  Users,
  Wrench,
} from 'lucide-react';
import { useProcedureInstance } from '@/hooks/useProcedureInstance';
import { useWizard } from '@/hooks/useWizard';
import { PreflightPanel } from './PreflightPanel';
import { ActorsForm } from './ActorsForm';
import { DocumentChecklist } from './DocumentChecklist';
import { CommercialForm } from './CommercialForm';
import { PrendaForm } from './PrendaForm';
import type { WizardStepFormHandle } from './wizard-step-form';
import { BiometricStep } from './BiometricStep';
import { FirmaFurStep } from './FirmaFurStep';
import { reasonCopy, blockerCopy } from './wizard-copy';
import { canNavigateToStep, frontierIndex } from './wizard-navigation';
import { WizardReadOnlyProvider, useWizardReadOnly } from './WizardReadOnlyContext';
import { useToast } from '@/components/admin/Toast';
import { tramitesClient } from '@/lib/api/tramites-client';
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
 * - Entrada por modalidad (M0): `modalidad` + `title` — crea la instancia draft
 *   al montar. Se conserva para los tests legacy de auto-create.
 * - Entrada legacy por tipo publicado: `configuration` + `procedureTypeId`.
 *
 * Exactamente una de las tres vías debe estar presente.
 */
type Props = {
  onExit: () => void;
} & (
  | { existingInstanceId: string; modalidad?: undefined; title?: undefined; configuration?: undefined; procedureTypeId?: undefined }
  | { modalidad: WizardModalidad; title: string; existingInstanceId?: undefined; configuration?: undefined; procedureTypeId?: undefined }
  | { configuration: ProcedureConfiguration; procedureTypeId: string; existingInstanceId?: undefined; modalidad?: undefined; title?: undefined }
);

/**
 * Etiqueta de la modalidad para el encabezado del wizard. HU #10591 — al añadir
 * `traspaso_unilateral`, estos `Record<WizardModalidad, ...>` obligan (TS) a cubrir
 * las tres modalidades, evitando que caiga al fallback binario "Matrícula inicial".
 * `TITULO` = título largo (hero); `NOMBRE` = etiqueta corta del subtítulo "· N pasos".
 */
const MODALIDAD_TITULO: Record<WizardModalidad, string> = {
  matricula_inicial: 'Matrícula inicial',
  traspaso: 'Traspaso estándar',
  traspaso_unilateral: 'Traspaso unilateral',
};

const MODALIDAD_NOMBRE: Record<WizardModalidad, string> = {
  matricula_inicial: 'Matrícula inicial',
  traspaso: 'Traspaso',
  traspaso_unilateral: 'Traspaso unilateral',
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
  fur: 'Generación opcional del FUR y expediente consolidado. Puedes enviar el trámite y generarlos después.',
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
  const { configuration, procedureTypeId, modalidad: entryModalidad, title, existingInstanceId, onExit } = props;
  const { state, start } = useProcedureInstance();
  // Con instancia existente (Track B) no se crea nada: el id viene por prop.
  // En las vías de auto-create el id lo produce `start()` → state.instanceId.
  const instanceId = existingInstanceId ?? state.instanceId;

  // Estado de la instancia existente + sello de borrador finalizado (HU #10350). Se derivan
  // de ellos los tres modos del wizard (ver más abajo). Los trámites nuevos arrancan editables.
  const [instanceStatus, setInstanceStatus] = useState<InstanceStatus | null>(null);
  const [draftFinalizedAt, setDraftFinalizedAt] = useState<string | null>(null);
  useEffect(() => {
    if (!existingInstanceId) return;
    let active = true;
    tramitesClient
      .getInstance(existingInstanceId)
      .then((d) => {
        if (active) {
          setInstanceStatus(d.status ?? null);
          setDraftFinalizedAt(d.draftFinalizedAt ?? null);
        }
      })
      .catch(() => {});
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

  // Crea la instancia draft al montar (una sola vez por entrada). Si ya existe
  // (existingInstanceId), NO crea: el wizard solo hidrata el draft vía GET /wizard.
  useEffect(() => {
    if (existingInstanceId) return;
    if (startedForRef.current === startKey) return;
    startedForRef.current = startKey;
    void start(
      entryModalidad ? { modalidad: entryModalidad } : { procedureTypeId: procedureTypeId! },
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [startKey, existingInstanceId]);

  const {
    wizard,
    steps,
    canSubmit,
    blockers,
    loading: wizardLoading,
    error: wizardError,
    refresh,
  } = useWizard(instanceId);

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
  const fullReadOnly = !!estadoTramite && estadoTramite !== 'borrador';
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
  const { show } = useToast();
  // Guardar+continuar de los pasos con form embebido (actores y comercial): la
  // shell dispara save() vía ref desde el footer "Guardar y continuar".
  const stepFormRef = useRef<WizardStepFormHandle>(null);
  const [continuing, setContinuing] = useState(false);

  // Preflight local (semáforo) para los pasos consulta/validación.
  const [preflight, setPreflight] = useState<PreflightSnapshot | null>(null);
  const [preflightLoading, setPreflightLoading] = useState(false);

  const modalidad: WizardModalidad = wizard?.modalidad ?? entryModalidad ?? 'matricula_inicial';
  const activeStep: WizardStep | undefined = steps[activeIndex];

  // Identidad aprobada (deriva del estado server-driven del paso): matrícula → paso 'identidad'
  // complete; traspaso → el paso 'fur' (que envuelve la biométrica) ya no reporta pendiente_biometria.
  // canRadicar gobierna el botón "Preparar" (N 03: borrador→preparado): el gate RF03 exige identidad,
  // mientras que canSubmit (matrícula) trata la identidad como diferida → no basta canSubmit.
  const identityApproved = isIdentityApproved(steps, modalidad);
  const canRadicar = canSubmit && identityApproved;

  // Header: por modalidad usamos `title`; legacy usa configuration.name; con
  // instancia existente derivamos la etiqueta de la modalidad server-driven.
  const headerTitle = title ?? configuration?.name ?? MODALIDAD_TITULO[modalidad];

  // Navegación en cascada: solo a pasos completos o a la frontera (primer
  // incompleto). No basta con que el paso no esté 'locked'.
  const goToStep = (index: number) => {
    if (!canNavigateToStep(steps, index, navViewOnly)) return;
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
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setActiveIndex(frontierIndex(steps));
    }
  }, [steps, existingInstanceId]);

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

  // N 03 (radicación en dos pasos) — Preparar: borrador→preparado vía POST /transition. El backend
  // valida el gate RF03 (identidad aprobada + documentos); solo se habilita cuando el wizard reporta
  // canSubmit Y la identidad está aprobada (ver `canRadicar`). El wizard permanece abierto: pasa a
  // solo lectura y ofrece "Radicar a tránsito".
  const handlePreparar = async () => {
    if (!instanceId || !canRadicar) return;
    setSubmitting(true);
    setSubmitError(null);
    try {
      await tramitesClient.transitionInstance(instanceId, 'preparado');
      setInstanceStatus('preparado');
      show('Trámite preparado: validaciones completas, listo para radicar.', 'success');
      await refresh();
    } catch (err) {
      setSubmitError(
        err instanceof Error ? err.message : 'No se pudo preparar el trámite.',
      );
    } finally {
      setSubmitting(false);
    }
  };

  // N 03 (radicación en dos pasos) — Radicar a tránsito: preparado→entregado vía POST /transition
  // (los gates OT —organismo habilitado, reglas— los valida el backend en esta transición). Sin
  // pantalla intermedia: toast de éxito + volver al listado de inmediato (onExit redirige a
  // /tramites; el ToastProvider del layout no se desmonta).
  const handleRadicar = async () => {
    if (!instanceId || !canEntregar) return;
    setSubmitting(true);
    setSubmitError(null);
    try {
      await tramitesClient.transitionInstance(instanceId, 'entregado');
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
    (!isSavableStep && activeStep.status !== 'complete' && !nextStepNavigable);

  // "Guardar y continuar" para pasos con form embebido: valida + persiste (vía
  // ref), refresca el wizard y avanza solo si el paso quedó complete. Otros
  // pasos: navegación directa al siguiente.
  const handleContinue = async () => {
    if (!isSavableStep) {
      goToStep(activeIndex + 1);
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
          if (ensured.outcome === 'requiere_validacion') {
            const { provider } = await tramitesClient.getBiometricState(instanceId);
            if (provider === 'kyverum') {
              await tramitesClient.iniciarBiometric(instanceId, { parte: parteIdentidad });
            } else {
              await tramitesClient.simulateBiometric(instanceId, { parte: parteIdentidad });
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
        setActiveIndex((i) => Math.min(i + 1, steps.length - 1));
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
              {MODALIDAD_NOMBRE[modalidad]} · {steps.length} pasos
            </p>
          )}
        </div>
        <button
          onClick={onExit}
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
            la firma se procesará automáticamente al aprobarse, y luego podrás radicar a tránsito.
          </span>
        </div>
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
                preflight={preflight}
                preflightLoading={preflightLoading}
                onRunPreflight={runPreflight}
                onRefresh={() => void refresh()}
                stepFormRef={stepFormRef}
                identityOperable={draftFinalized}
                identityApproved={identityApproved}
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
            className="flex items-center justify-between mt-6 pt-4 border-t"
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
                · Pasos de datos: en borrador finalizado solo se navega; editable usa Continuar/Guardar. */}
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
              canRadicar ? (
                <button
                  onClick={() => void handlePreparar()}
                  disabled={submitting}
                  className="px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
                  style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
                  title="Deja el trámite validado y listo para radicar"
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
                {continuing ? 'Guardando…' : isSavableStep ? 'Guardar y continuar' : 'Continuar'}
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
}: {
  step: WizardStep;
  instanceId: string | null;
  preflight: PreflightSnapshot | null;
  preflightLoading: boolean;
  onRunPreflight: () => Promise<void>;
  onRefresh: () => void;
}) {
  const isVin = step.key === 'consulta_vin';
  const readOnly = useWizardReadOnly();
  const router = useRouter();
  // Confirmación de paz y salvo de impuesto (traspaso, paso 1): se ofrece cuando el
  // preflight no pudo verificar el impuesto vehicular (check 'impuesto' en unknown/warn).
  const [pazSalvoSaving, setPazSalvoSaving] = useState(false);
  const [riesgoSaving, setRiesgoSaving] = useState(false);

  const [vin, setVin] = useState('');
  const [plate, setPlate] = useState('');
  const [ownerDocType, setOwnerDocType] = useState<ActorDocumentType>('CC');
  const [ownerDocNumber, setOwnerDocNumber] = useState('');
  const [persisting, setPersisting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // field_values frescos de la instancia: rehidratan inputs y alimentan la
  // tarjeta "Datos del vehículo · RUNT" tras la consulta.
  const [fieldValues, setFieldValues] = useState<FieldValue[]>([]);
  // HU #10478 — proveedor de consulta por placa resuelto para el tenant. Con Kyverum RUNT NO se pide
  // el tipo de documento del propietario (lo resuelve el RUNT y lo devuelve); con Verifik sí se necesita.
  // null = aún sin resolver ⇒ se muestra el campo (default seguro para no ocultarlo con Verifik).
  const [platePrimaryProvider, setPlatePrimaryProvider] = useState<string | null>(null);
  const hideOwnerDocType = !isVin && platePrimaryProvider === 'kyverum_runt';

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
      .then((cfg) => setPlatePrimaryProvider(cfg.vehiclePlate))
      .catch(() => {});
  }, [isVin]);

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
    if (!instanceId) return;
    const items = buildItems();
    if (!items) {
      setError(
        isVin
          ? 'Ingresa el VIN antes de consultar.'
          : 'Ingresa la placa y el documento del propietario antes de consultar.',
      );
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
    setPersisting(true);
    try {
      // UNA sola consulta a Verifik: 1) persistir identificador → 2) preflight,
      // que con la MISMA respuesta del RUNT compone el semáforo Y hidrata los
      // atributos del vehículo en field_values → 3) recargar la instancia para
      // pintar la tarjeta "Datos del vehículo". (Antes se hacían dos consultas:
      // una dedicada de datos + el preflight; el preflight ya trae ambos.)
      await tramitesClient.patchFieldValues(instanceId, items);
      await onRunPreflight();
      await loadInstance();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo consultar.');
    } finally {
      setPersisting(false);
    }
  };

  const inputClass =
    'w-full px-3 py-2 rounded-xl border bg-white dark:bg-[#0B0F14] text-xs outline-none focus:border-[#557EFF]';

  const loading = preflightLoading || persisting;
  const hasResult = !!preflight?.overall;

  // Paz y salvo de impuesto: solo traspaso (placa-first) y solo si el preflight dejó el
  // check de impuesto sin verificar (unknown/warn). Estado leído de field_values.
  const impuestoCheck = preflight?.checks?.find((c) =>
    c.key.toLowerCase().includes('impuesto'),
  );
  const mostrarPazSalvo =
    !isVin && !!impuestoCheck && (impuestoCheck.status === 'unknown' || impuestoCheck.status === 'warn');
  const pazSalvoConfirmado =
    fieldValues.find((f) => f.fieldKey === 'paz_salvo_impuesto')?.valueText === 'true';

  const handlePazSalvo = async (checked: boolean) => {
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
      {isVin ? (
        <div
          className="rounded-2xl border bg-white p-4 dark:bg-[#0B0F14]"
        >
          <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
            <input
              id="consulta-vin"
              type="text"
              value={vin}
              onChange={(e) => setVin(sanitizeVin(e.target.value))}
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
                onChange={(e) => setPlate(sanitizePlate(e.target.value))}
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
                onChange={(e) => setOwnerDocNumber(sanitizeDocNumber(e.target.value, ownerDocType))}
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

      <VehicleDataCard fieldValues={fieldValues} />

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
        snapshot={preflight}
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
  preflight,
  preflightLoading,
  onRunPreflight,
  onRefresh,
  stepFormRef,
  identityOperable = false,
  identityApproved = false,
}: {
  step: WizardStep;
  modalidad: WizardModalidad;
  instanceId: string | null;
  preflight: PreflightSnapshot | null;
  preflightLoading: boolean;
  onRunPreflight: () => Promise<void>;
  onRefresh: () => void;
  stepFormRef: RefObject<WizardStepFormHandle | null>;
  /**
   * HU #10350 — borrador finalizado: aunque el wizard esté en solo lectura para los datos, el paso
   * de Identidad debe seguir operable (iniciar/compartir/refrescar Kyverum) porque la validación del
   * cliente es justamente lo que se está esperando. Reabre la captura SOLO para la biométrica.
   */
  identityOperable?: boolean;
  /** Identidad aprobada (deriva del estado server-driven). Con identidad pendiente, el paso FUR
   * informa que el FUR/firma se generarán automáticamente, en vez de empujar la generación manual. */
  identityApproved?: boolean;
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
            onChanged={onRefresh}
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
          // siembra su documento (editable) desde owner_document_* de la consulta.
          seedDocumentoFromOwner
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
        />
      );
      return (
        <div className="space-y-6">
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
            instanceId={instanceId}
            modalidad={modalidad}
            onRefresh={onRefresh}
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
