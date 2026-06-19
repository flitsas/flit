'use client';

import { useEffect, useRef, useState } from 'react';
import { Check, ChevronLeft, ChevronRight, Lock } from 'lucide-react';
import { useProcedureInstance } from '@/hooks/useProcedureInstance';
import { useWizard } from '@/hooks/useWizard';
import { PreflightPanel } from './PreflightPanel';
import { ActorsForm } from './ActorsForm';
import { DocumentChecklist } from './DocumentChecklist';
import { CommercialForm } from './CommercialForm';
import { BiometricStep } from './BiometricStep';
import { FirmaFurStep } from './FirmaFurStep';
import { reasonCopy, blockerCopy } from './wizard-copy';
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

  const goToStep = (index: number) => {
    const target = steps[index];
    if (!target || target.status === 'locked') return;
    setActiveIndex(index);
  };

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
                const clickable = s.status !== 'locked';
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
                style={{ background: '#557EFF' }}
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
 * Grupos de atributos del vehículo hidratados por el backend en field_values
 * tras la consulta (origen RUNT). Solo se pinta lo presente; nada de proveedor.
 */
const VEHICLE_GROUPS: { title: string; fields: { key: string; label: string }[] }[] = [
  {
    title: 'Identificación',
    fields: [
      { key: 'plate', label: 'Placa' },
      { key: 'vin', label: 'VIN' },
    ],
  },
  {
    title: 'Características',
    fields: [
      { key: 'vehicle_brand', label: 'Marca' },
      { key: 'vehicle_line', label: 'Línea' },
      { key: 'vehicle_year', label: 'Modelo' },
      { key: 'vehicle_color', label: 'Color' },
      { key: 'vehicle_class', label: 'Clase' },
      { key: 'vehicle_fuel', label: 'Combustible' },
      { key: 'vehicle_engine_displacement', label: 'Cilindraje' },
    ],
  },
  {
    title: 'Tránsito',
    fields: [
      { key: 'transit_office_name', label: 'Organismo de tránsito' },
      { key: 'vehicle_state', label: 'Estado del vehículo' },
    ],
  },
];

/**
 * Tarjeta "Datos del vehículo · RUNT". Lee los field_values frescos de la
 * instancia y muestra una fila por atributo presente, agrupado. Reusa la
 * convención de cards de operación (rounded-2xl + borde #DFE5ED).
 */
function VehicleDataCard({ fieldValues }: { fieldValues: FieldValue[] }) {
  const byKey = (key: string) =>
    fieldValues.find((f) => f.fieldKey === key)?.valueText ?? '';

  const groups = VEHICLE_GROUPS.map((g) => ({
    title: g.title,
    rows: g.fields
      .map((f) => ({ label: f.label, value: byKey(f.key) }))
      .filter((r) => r.value.trim() !== ''),
  })).filter((g) => g.rows.length > 0);

  if (groups.length === 0) return null;

  return (
    <div
      className="rounded-2xl p-4 border bg-white dark:bg-[#0B0F14]"
      style={{ borderColor: '#DFE5ED' }}
    >
      <div className="mb-3 flex items-center justify-between gap-3">
        <h4 className="text-sm font-bold">Datos del vehículo</h4>
        <span
          className="rounded px-1.5 py-0.5 text-[9px] font-semibold uppercase"
          style={{ background: 'rgba(85,126,255,0.10)', color: '#557EFF' }}
        >
          RUNT
        </span>
      </div>
      <div className="space-y-4">
        {groups.map((g) => (
          <div key={g.title}>
            <p className="text-[10px] font-semibold uppercase opacity-60 mb-1.5">
              {g.title}
            </p>
            <dl className="grid gap-x-6 gap-y-1.5 sm:grid-cols-2">
              {g.rows.map((r) => (
                <div key={r.label} className="flex items-baseline justify-between gap-3">
                  <dt className="text-[11px] opacity-60 shrink-0">{r.label}</dt>
                  <dd className="text-xs font-semibold text-right">{r.value}</dd>
                </div>
              ))}
            </dl>
          </div>
        ))}
      </div>
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
      // 1) Persistir identificador → 2) preflight (el backend hidrata los datos
      // del vehículo en field_values) → 3) recargar la instancia para pintar la
      // tarjeta "Datos del vehículo · RUNT" con los valores frescos.
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

  return (
    <div className="space-y-4">
      <p className="text-xs opacity-70">
        {isVin
          ? 'Ingresa el VIN del vehículo para consultar los datos del RUNT y correr el pre-vuelo.'
          : 'Ingresa la placa y el propietario del vehículo para consultar los datos del RUNT y correr el pre-vuelo.'}
      </p>

      {isVin ? (
        <div className="max-w-sm">
          <label htmlFor="consulta-vin" className="text-xs font-semibold mb-1.5 block">
            VIN
          </label>
          <input
            id="consulta-vin"
            type="text"
            value={vin}
            onChange={(e) => setVin(e.target.value)}
            className={inputClass}
            style={{ borderColor: '#DFE5ED' }}
            placeholder="Ej. 9BWZZZ377VT004251"
          />
        </div>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 max-w-xl">
          <div>
            <label htmlFor="consulta-plate" className="text-xs font-semibold mb-1.5 block">
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
              className="text-xs font-semibold mb-1.5 block"
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
              className="text-xs font-semibold mb-1.5 block"
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

      <PreflightPanel
        snapshot={preflight}
        loading={preflightLoading || persisting}
        onRun={() => void handleRun()}
        riesgoAceptado={false}
        onToggleRiesgo={() => {}}
      />

      <VehicleDataCard fieldValues={fieldValues} />
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
}: {
  step: WizardStep;
  modalidad: WizardModalidad;
  instanceId: string | null;
  preflight: PreflightSnapshot | null;
  preflightLoading: boolean;
  onRunPreflight: () => Promise<void>;
  onRefresh: () => void;
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
      return <DocumentChecklist instanceId={instanceId} />;

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
