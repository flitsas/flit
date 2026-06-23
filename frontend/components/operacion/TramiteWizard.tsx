'use client';

import { useEffect, useRef, useState } from 'react';
import {
  Briefcase,
  Building2,
  Calendar,
  Car,
  Check,
  ChevronLeft,
  ChevronRight,
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
import { BiometricStep } from './BiometricStep';
import { FirmaFurStep } from './FirmaFurStep';
import { reasonCopy, blockerCopy } from './wizard-copy';
import { canNavigateToStep, frontierIndex } from './wizard-navigation';
import { tramitesClient } from '@/lib/api/tramites-client';
import type {
  ActorDocumentType,
  FieldValue,
  FieldValueInput,
  PreflightSnapshot,
  ProcedureConfiguration,
  WizardModalidad,
  WizardStep,
  WizardStepStatus,
} from '@/lib/api/types/procedure-runtime';

/**
 * El wizard es server-driven: una vez creada la instancia, GET /wizard decide
 * modalidad/pasos/status. Por eso solo necesita saber CÓMO crear la instancia.
 *
 * - Entrada por modalidad (M0): `modalidad` + `title` (etiqueta para el header).
 * - Entrada legacy por tipo publicado: `configuration` + `procedureTypeId`.
 *
 * Exactamente una de las dos vías debe estar presente.
 */
type Props = {
  onExit: () => void;
} & (
  | { modalidad: WizardModalidad; title: string; configuration?: undefined; procedureTypeId?: undefined }
  | { configuration: ProcedureConfiguration; procedureTypeId: string; modalidad?: undefined; title?: undefined }
);

const STATUS_BADGE: Record<
  WizardStepStatus,
  { bg: string; color: string }
> = {
  complete: { bg: '#8CC63F', color: '#fff' },
  incomplete: { bg: '#DFE5ED', color: '#162744' },
  locked: { bg: '#EEF1F5', color: '#9AA5B1' },
};

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
  const { configuration, procedureTypeId, modalidad: entryModalidad, title, onExit } = props;
  const { state, start } = useProcedureInstance();
  const instanceId = state.instanceId;

  // Header: por modalidad usamos `title`; legacy usa configuration.name.
  const headerTitle = title ?? configuration?.name ?? 'Trámite';

  // Clave estable de creación (modalidad o procedureTypeId) para el guard.
  const startKey = entryModalidad ?? procedureTypeId ?? '';

  // Guardia anti doble-create: StrictMode re-invoca los efectos en dev, lo que
  // dispararía DOS POST /instances casi simultáneos → choque UNIQUE de
  // reference_number → 500 y wizard sin instanceId (luego 404 en silencio).
  // El ref persiste entre la doble invocación del mismo montaje y garantiza
  // que `start()` corra UNA sola vez por entrada.
  const startedForRef = useRef<string | null>(null);

  // Crea la instancia draft al montar (una sola vez por entrada).
  useEffect(() => {
    if (startedForRef.current === startKey) return;
    startedForRef.current = startKey;
    void start(
      entryModalidad ? { modalidad: entryModalidad } : { procedureTypeId: procedureTypeId! },
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [startKey]);

  const {
    wizard,
    steps,
    canSubmit,
    blockers,
    loading: wizardLoading,
    error: wizardError,
    refresh,
  } = useWizard(instanceId);

  const [activeIndex, setActiveIndex] = useState(0);
  const [submitting, setSubmitting] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  // Preflight local (semáforo) para los pasos consulta/validación.
  const [preflight, setPreflight] = useState<PreflightSnapshot | null>(null);
  const [preflightLoading, setPreflightLoading] = useState(false);

  const modalidad: WizardModalidad = wizard?.modalidad ?? 'matricula_inicial';
  const activeStep: WizardStep | undefined = steps[activeIndex];

  // Navegación en cascada: solo a pasos completos o a la frontera (primer
  // incompleto). No basta con que el paso no esté 'locked'.
  const goToStep = (index: number) => {
    if (!canNavigateToStep(steps, index)) return;
    setActiveIndex(index);
  };

  // Tras refrescar el estado, si el paso activo dejó de ser navegable (p.ej. un
  // gate previo cambió y el flujo retrocedió), reubica en la frontera del flujo.
  useEffect(() => {
    if (steps.length === 0) return;
    if (!canNavigateToStep(steps, activeIndex)) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setActiveIndex(frontierIndex(steps));
    }
  }, [steps, activeIndex]);

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
    if (key === 'consulta' || key === 'consulta_vin' || key === 'validacion') {
      tramitesClient
        .getPreflight(instanceId)
        .then((snap) => snap && setPreflight(snap))
        .catch(() => {});
    }
  }, [instanceId, activeStep?.key]);

  const handleFinish = async () => {
    if (!instanceId || !canSubmit) return;
    setSubmitting(true);
    setSubmitError(null);
    try {
      await tramitesClient.submitInstance(instanceId);
      setSubmitted(true);
    } catch (err) {
      setSubmitError(
        err instanceof Error ? err.message : 'Error al enviar el trámite',
      );
    } finally {
      setSubmitting(false);
    }
  };

  if (submitted) {
    return (
      <div className="h-full w-full grid place-items-center px-6 pb-24">
        <div
          className="max-w-md w-full rounded-3xl p-8 bg-white dark:bg-[#0B0F14] border text-center"
          style={{ borderColor: '#DFE5ED' }}
          role="status"
          aria-live="polite"
        >
          <div
            className="h-16 w-16 mx-auto rounded-full grid place-items-center mb-4"
            style={{ background: 'rgba(0,219,213,0.15)' }}
          >
            <Check className="h-8 w-8" style={{ color: '#00DBD5' }} aria-hidden="true" />
          </div>
          <h2 className="text-xl font-bold">¡Trámite enviado!</h2>
          <p className="text-xs opacity-70 mt-2">
            El trámite {headerTitle} fue radicado correctamente.
          </p>
          <button
            onClick={onExit}
            className="w-full mt-5 py-2.5 rounded-xl text-sm font-semibold text-white"
            style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
          >
            Volver a Operación
          </button>
        </div>
      </div>
    );
  }

  const isLast = steps.length > 0 && activeIndex === steps.length - 1;
  const continueDisabled =
    !activeStep || activeStep.status !== 'complete' || activeIndex >= steps.length - 1;

  return (
    <div className="flex-1 min-h-0 flex flex-col gap-4 overflow-hidden">
      <div className="flex items-center justify-between shrink-0">
        <div>
          <h1 className="text-xl font-bold">{headerTitle}</h1>
          {wizard && (
            <p className="text-[11px] opacity-60 mt-0.5">
              {modalidad === 'traspaso' ? 'Traspaso' : 'Matrícula inicial'} ·{' '}
              {wizard.totalSteps} pasos
            </p>
          )}
        </div>
        <button
          onClick={onExit}
          className="text-xs opacity-70 hover:opacity-100"
          aria-label="Cancelar y volver al selector"
        >
          ← Cancelar
        </button>
      </div>

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

      <div className="grid grid-cols-12 gap-4 flex-1 min-h-0">
        {/* Sidebar de pasos server-driven. */}
        <aside
          className="col-span-12 md:col-span-3 rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border overflow-y-auto"
          style={{ borderColor: '#DFE5ED' }}
        >
          <p className="text-[10px] font-semibold uppercase opacity-60 mb-3">
            Asistente de seguimiento
          </p>
          {steps.length === 0 ? (
            <p className="text-[11px] opacity-60">
              {wizardLoading ? 'Cargando pasos…' : 'Sin pasos disponibles.'}
            </p>
          ) : (
            <ol className="space-y-3">
              {steps.map((s, i) => {
                const isActive = i === activeIndex;
                const clickable = canNavigateToStep(steps, i);
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
          className="col-span-12 md:col-span-9 rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border overflow-y-auto"
          style={{ borderColor: '#DFE5ED' }}
        >
          {!activeStep ? (
            <p className="text-xs opacity-60">
              {wizardLoading ? 'Cargando el asistente…' : 'Este flujo no tiene pasos.'}
            </p>
          ) : (
            <div className="space-y-6">
              <h2 className="text-base font-bold">{activeStep.label}</h2>
              <StepBody
                step={activeStep}
                modalidad={modalidad}
                instanceId={instanceId}
                preflight={preflight}
                preflightLoading={preflightLoading}
                onRunPreflight={runPreflight}
                onRefresh={() => void refresh()}
                onSubmitted={() => setSubmitted(true)}
              />
            </div>
          )}

          {/* Bloqueos de envío traducidos. */}
          {isLast && blockers.length > 0 && (
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
            style={{ borderColor: '#DFE5ED' }}
          >
            <button
              onClick={() => goToStep(Math.max(0, activeIndex - 1))}
              disabled={activeIndex === 0}
              className="flex items-center gap-1 px-4 py-2 rounded-xl text-xs font-medium border disabled:opacity-30"
              style={{ borderColor: '#162744', color: '#162744' }}
            >
              <ChevronLeft className="h-3 w-3" /> Anterior
            </button>
            {!isLast ? (
              <button
                onClick={() => goToStep(activeIndex + 1)}
                disabled={continueDisabled}
                className="flex items-center gap-1 px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
                style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
              >
                Continuar
                <ChevronRight className="h-3 w-3" />
              </button>
            ) : (
              <button
                onClick={() => void handleFinish()}
                disabled={!canSubmit || submitting}
                className="px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
                style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
              >
                {submitting ? 'Enviando…' : 'Finalizar'}
              </button>
            )}
          </div>
        </section>
      </div>
    </div>
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
      style={{ borderColor: '#DFE5ED' }}
    >
      {/* Header */}
      <div
        className="flex items-center justify-between gap-3 border-b px-4 py-3"
        style={{ borderColor: '#DFE5ED' }}
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
          style={{ borderColor: '#DFE5ED', background: '#DFE5ED' }}
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
          style={{ borderColor: '#DFE5ED' }}
        >
          <p className="mb-2 text-[10px] font-semibold uppercase opacity-50">
            Documentos del vehículo
          </p>
          <div className="flex flex-wrap gap-3">
            {(soatVencimiento || soatAseguradora) && (
              <div
                className="flex min-w-0 items-start gap-2 rounded-xl border px-3 py-2"
                style={{ borderColor: '#DFE5ED' }}
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
                style={{ borderColor: '#DFE5ED' }}
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
}: {
  step: WizardStep;
  instanceId: string | null;
  preflight: PreflightSnapshot | null;
  preflightLoading: boolean;
  onRunPreflight: () => Promise<void>;
}) {
  const isVin = step.key === 'consulta_vin';

  const [vin, setVin] = useState('');
  const [plate, setPlate] = useState('');
  const [ownerDocType, setOwnerDocType] = useState<ActorDocumentType>('CC');
  const [ownerDocNumber, setOwnerDocNumber] = useState('');
  const [persisting, setPersisting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // field_values frescos de la instancia: rehidratan inputs y alimentan la
  // tarjeta "Datos del vehículo · RUNT" tras la consulta.
  const [fieldValues, setFieldValues] = useState<FieldValue[]>([]);

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

  const buildItems = (): FieldValueInput[] | null => {
    if (isVin) {
      const value = vin.trim();
      if (!value) return null;
      return [{ formFieldId: null, fieldKey: 'vin', valueText: value, valueJson: null }];
    }
    const plateValue = plate.trim();
    const docNumber = ownerDocNumber.trim();
    if (!plateValue || !docNumber) return null;
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
          style={{ borderColor: '#DFE5ED' }}
        >
          <h4 className="text-sm font-bold">Consulta de vehículo</h4>
          <p className="mt-0.5 text-xs opacity-60">
            Ingresa el VIN para consultar los datos del vehículo en el RUNT.
          </p>
          <div className="mt-3 flex flex-col gap-2 sm:flex-row sm:items-center">
            <input
              id="consulta-vin"
              type="text"
              value={vin}
              onChange={(e) => setVin(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') void handleRun();
              }}
              className={`${inputClass} sm:flex-1`}
              style={{ borderColor: '#DFE5ED' }}
              placeholder="Número VIN…"
              aria-label="Número VIN"
            />
            {consultButton}
          </div>
        </div>
      ) : (
        <div
          className="rounded-2xl border bg-white p-4 dark:bg-[#0B0F14]"
          style={{ borderColor: '#DFE5ED' }}
        >
          <h4 className="text-sm font-bold">Consulta de vehículo</h4>
          <p className="mt-0.5 text-xs opacity-60">
            Ingresa la placa y el propietario para consultar los datos del RUNT.
          </p>
          <div className="mt-3 grid max-w-xl gap-4 sm:grid-cols-2">
            <div>
              <label htmlFor="consulta-plate" className="mb-1.5 block text-xs font-semibold">
                Placa
              </label>
              <input
                id="consulta-plate"
                type="text"
                value={plate}
                onChange={(e) => setPlate(e.target.value)}
                className={inputClass}
                style={{ borderColor: '#DFE5ED' }}
                placeholder="Ej. ABC123"
              />
            </div>
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
                onChange={(e) => setOwnerDocType(e.target.value as ActorDocumentType)}
                className={inputClass}
                style={{ borderColor: '#DFE5ED' }}
              >
                {DOC_TYPES.map((t) => (
                  <option key={t} value={t}>
                    {t}
                  </option>
                ))}
              </select>
            </div>
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
                onChange={(e) => setOwnerDocNumber(e.target.value)}
                className={inputClass}
                style={{ borderColor: '#DFE5ED' }}
                placeholder="Ej. 1020304050"
              />
            </div>
          </div>
          <div className="mt-4">{consultButton}</div>
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

      <PreflightPanel
        snapshot={preflight}
        loading={loading}
        onRun={() => void handleRun()}
        riesgoAceptado={false}
        onToggleRiesgo={() => {}}
        showRunButton={false}
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
  onSubmitted,
}: {
  step: WizardStep;
  modalidad: WizardModalidad;
  instanceId: string | null;
  preflight: PreflightSnapshot | null;
  preflightLoading: boolean;
  onRunPreflight: () => Promise<void>;
  onRefresh: () => void;
  onSubmitted: () => void;
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
        />
      );

    // Validación legal (traspaso): muestra el semáforo del preflight.
    case 'validacion':
      return (
        <div className="space-y-4">
          <p className="text-xs opacity-70">
            Resultado legal de la consulta (RUNT · SIMIT · RNMC).
          </p>
          <PreflightPanel
            snapshot={preflight}
            loading={preflightLoading}
            onRun={onRunPreflight}
            riesgoAceptado={false}
            onToggleRiesgo={() => {}}
          />
        </div>
      );

    case 'documentos':
      return <DocumentChecklist instanceId={instanceId} onChanged={onRefresh} />;

    case 'comprador':
      return (
        <ActorsForm
          instanceId={instanceId}
          modalidad={modalidad === 'traspaso' ? 'traspaso' : 'matricula_inicial'}
          roles={['comprador']}
          onSaved={onRefresh}
        />
      );

    case 'vendedor':
      return (
        <ActorsForm
          instanceId={instanceId}
          modalidad="traspaso"
          roles={['vendedor']}
          onSaved={onRefresh}
        />
      );

    case 'comercial':
      return <CommercialForm instanceId={instanceId} onSaved={onRefresh} />;

    // Matrícula paso 4 = Identidad (biométrica del comprador, parte única).
    case 'identidad':
      return (
        <BiometricStep
          instanceId={instanceId}
          modalidad={modalidad}
          onRefresh={onRefresh}
        />
      );

    // FUR (matrícula 5 / traspaso 6). Biométrica de las partes (Slice 6) +
    // firma electrónica, portal de participantes y generación del FUR (Slice 7).
    // En matrícula la biométrica es del comprador (parte única) y no hay firma.
    case 'fur':
      return (
        <div className="space-y-6">
          <BiometricStep
            instanceId={instanceId}
            modalidad={modalidad}
            onRefresh={onRefresh}
          />
          <FirmaFurStep
            instanceId={instanceId}
            modalidad={modalidad}
            onRefresh={onRefresh}
            onSubmitted={onSubmitted}
          />
        </div>
      );

    default:
      return (
        <p className="text-xs opacity-60">
          Paso «{step.key}» sin renderizador en esta fase.
        </p>
      );
  }
}
