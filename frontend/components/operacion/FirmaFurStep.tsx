'use client';

import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import {
  Building2,
  Check,
  Copy,
  Download,
  FileText,
  RefreshCw,
  Search,
  X,
} from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import {
  getPlatePreassignStatus,
  listAvailablePlatesForCompany,
  type PlateDetail,
} from '@/lib/api/admin-plate-ranges';
import MatriculaResumen from './MatriculaResumen';
import ExpedienteVisor from './ExpedienteVisor';
import ExpedienteTimeline from './ExpedienteTimeline';
import { sourceLabel, checkRoleSuffix } from './PreflightPanel';
import { useWizardReadOnly } from './WizardReadOnlyContext';
import { documentLabel } from '@/lib/tramites/document-labels';
import type {
  Actor,
  BiometricValidation,
  FieldValue,
  FurDocument,
  InstanceStatus,
  Participant,
  ParticipantRol,
  PreflightCheck,
  ProcedureAttachment,
  Signature,
  SignatureParte,
  StatusHistory,
  TransitOfficeOption,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

interface Props {
  instanceId: string | null;
  modalidad: WizardModalidad;
  /** Re-consulta el estado del wizard tras una acción (server-driven). */
  onRefresh?: () => void;
  /** FEATURE 05 — el RNMC aplica al trámite: se consulta por actor y se muestra en el resumen. */
  rnmcEnabled?: boolean;
}

const PARTE_LABEL: Record<SignatureParte, string> = {
  comprador: 'Comprador',
  vendedor: 'Vendedor',
};

// FEATURE 05 — colores por estado del check RNMC (mismo semáforo del pre-vuelo: ok verde, warn ámbar).
const RNMC_STATUS_STYLE: Record<string, { dot: string; text: string }> = {
  ok: { dot: '#8CC63F', text: '#8CC63F' },
  warn: { dot: '#F9AC00', text: '#F9AC00' },
  fail: { dot: '#FF4E00', text: '#FF4E00' },
  unknown: { dot: '#9AA5B1', text: '#9AA5B1' },
  error: { dot: '#FF4E00', text: '#FF4E00' },
};

/**
 * FEATURE 05 — resultado de la consulta RNMC (medidas correctivas de la Policía) por actor. Se
 * consulta al entrar al paso final (ya con la fecha de expedición de cada actor), no en el pre-vuelo.
 * Reutiliza `sourceLabel` (verifik_rnmc→RNMC) y `checkRoleSuffix` (comprador/vendedor) del pre-vuelo.
 */
function RnmcSection({ checks, loading }: { checks: PreflightCheck[]; loading: boolean }) {
  return (
    <div className="rounded-2xl p-4 border bg-white dark:bg-[#0B0F14]">
      <div className="mb-3 flex items-center gap-2">
        <Search className="h-4 w-4 opacity-60" aria-hidden="true" />
        <h3 className="text-sm font-semibold">Consulta RNMC — Medidas correctivas</h3>
        {loading && <RefreshCw className="h-3.5 w-3.5 animate-spin opacity-60" aria-hidden="true" />}
      </div>
      {loading && checks.length === 0 ? (
        <p className="text-[11px] opacity-60">Consultando el RNMC de los actores…</p>
      ) : checks.length === 0 ? (
        <p className="text-[11px] opacity-60">Sin resultados del RNMC para los actores del trámite.</p>
      ) : (
        <ul className="space-y-1.5" aria-label="Resultados RNMC por actor">
          {checks.map((c) => {
            const s = RNMC_STATUS_STYLE[c.status] ?? RNMC_STATUS_STYLE.unknown;
            return (
              <li key={c.key} className="flex items-start gap-2.5 rounded-xl border p-2.5">
                <span
                  className="mt-1 h-2.5 w-2.5 shrink-0 rounded-full"
                  style={{ background: s.dot }}
                  aria-hidden="true"
                />
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-1.5">
                    <span className="text-xs font-semibold">
                      {c.label}
                      {checkRoleSuffix(c.key)}
                    </span>
                    <span className="text-[10px] uppercase font-bold" style={{ color: s.text }}>
                      {c.status}
                    </span>
                    <span
                      className="rounded px-1.5 py-0.5 text-[9px] font-semibold uppercase"
                      style={{ background: 'rgba(85,126,255,0.10)', color: '#557EFF' }}
                    >
                      {sourceLabel(c.source)}
                    </span>
                  </div>
                  {c.message && <p className="mt-0.5 text-[11px] opacity-70">{c.message}</p>}
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}

const ROL_OPTIONS: { value: ParticipantRol; label: string }[] = [
  { value: 'comprador', label: 'Comprador' },
  { value: 'vendedor', label: 'Vendedor' },
  { value: 'mandatario', label: 'Mandatario' },
];

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

const INPUT_BASE =
  'w-full px-3 py-2 rounded-xl border bg-white dark:bg-[#0B0F14] text-xs outline-none focus:border-[#557EFF]';

/** Badge de color por estado de la firma. */
function FirmaBadge({ estado }: { estado: string }) {
  const map: Record<string, { label: string; bg: string; color: string }> = {
    pendiente_envio: { label: 'Pendiente', bg: '#EEF1F5', color: '#9AA5B1' },
    enviada: { label: 'Enviada', bg: 'rgba(85,126,255,0.12)', color: '#557EFF' },
    firmada: { label: 'Firmada', bg: 'rgba(140,198,63,0.15)', color: '#5B8A1F' },
    rechazada: { label: 'Rechazada', bg: 'rgba(255,78,0,0.10)', color: '#FF4E00' },
  };
  const s = map[estado] ?? { label: estado, bg: '#EEF1F5', color: '#9AA5B1' };
  return (
    <span
      className="px-2.5 py-1 rounded-full text-[10px] font-bold"
      style={{ background: s.bg, color: s.color }}
    >
      {s.label}
    </span>
  );
}

/** Construye el magic-link absoluto a partir del path relativo del backend. */
function absoluteLink(path: string): string {
  if (typeof window === 'undefined') return path;
  return `${window.location.origin}${path}`;
}

/** Botón reutilizable de copiar un enlace al portapapeles. */
function CopyLink({ link, label }: { link: string; label: string }) {
  const [copied, setCopied] = useState(false);
  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(link);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Sin portapapeles: el enlace ya está visible para copiar a mano.
    }
  };
  return (
    <div className="flex items-center gap-2">
      <input
        type="text"
        readOnly
        value={link}
        aria-label={label}
        className={INPUT_BASE}
      />
      <button
        type="button"
        onClick={() => void handleCopy()}
        className="flex items-center gap-1.5 px-3 py-2 rounded-xl text-[11px] font-semibold text-white shrink-0"
        style={{ background: '#557EFF' }}
        aria-label="Copiar enlace"
      >
        {copied ? <Check className="h-3 w-3" /> : <Copy className="h-3 w-3" />}
        {copied ? 'Copiado' : 'Copiar'}
      </button>
    </div>
  );
}

/**
 * Paso de organismo + expediente + firma + FUR + envío a tránsito (matrícula
 * paso 5 / traspaso paso 6, mock). Render BAJO el BiometricStep en el paso FUR.
 * Todo el gating es server-driven: este paso refleja el estado, refresca el
 * wizard tras cada acción y delega la verificación autoritativa al backend
 * (submit hard-gate). La firma de compraventa solo aplica a traspaso.
 */
export function FirmaFurStep({ instanceId, modalidad, onRefresh, rnmcEnabled = false }: Props) {
  // Solo lectura (Track C): sin acciones (organismo, firma, participantes, FUR);
  // se conserva la visualización (resumen, expediente, timeline, descargas).
  const readOnly = useWizardReadOnly();
  // Detalle de la instancia (field_values + actors + estado) para organismo,
  // resumen, expediente y línea de tiempo.
  const [detail, setDetail] = useState<{
    fieldValues: FieldValue[];
    actors: Actor[];
    status: InstanceStatus;
    statusHistory: StatusHistory[];
  } | null>(null);
  // Adjuntos + biométrica del expediente (alimentan MatriculaResumen y ExpedienteVisor).
  const [attachments, setAttachments] = useState<ProcedureAttachment[]>([]);
  const [biometric, setBiometric] = useState<BiometricValidation[]>([]);
  // HU #11014 — partes cubiertas por la firma del baúl: el expediente las rotula como firmadas desde
  // el baúl en vez de hablar del certificado de validación de identidad.
  const [firmaBaulPartes, setFirmaBaulPartes] = useState<string[]>([]);
  // FEATURE 05 — resultado RNMC por actor (medidas correctivas). Se consulta al entrar a este paso
  // (cuando ya se capturó la fecha de expedición de cada actor), no en el pre-vuelo.
  const [rnmcChecks, setRnmcChecks] = useState<PreflightCheck[]>([]);
  const [rnmcLoading, setRnmcLoading] = useState(false);

  const loadDetail = useCallback(async () => {
    if (!instanceId) return;
    try {
      const d = await tramitesClient.getInstance(instanceId);
      setDetail({
        fieldValues: d.fieldValues ?? [],
        actors: d.actors ?? [],
        status: d.status,
        statusHistory: d.statusHistory ?? [],
      });
    } catch {
      // El detalle es secundario para el resto del paso; los subbloques
      // muestran sus propios errores. No bloquea el render.
    }
  }, [instanceId]);

  const loadExpediente = useCallback(async () => {
    if (!instanceId) return;
    try {
      // allSettled: si la biométrica falla (404 en estados tempranos) no se
      // pierde el listado de adjuntos, y viceversa. Ambos son informativos.
      const [att, bio] = await Promise.allSettled([
        tramitesClient.getAttachments(instanceId),
        tramitesClient.listBiometricExpediente(instanceId),
      ]);
      if (att.status === 'fulfilled') setAttachments(att.value);
      if (bio.status === 'fulfilled') {
        setBiometric(bio.value.validations);
        setFirmaBaulPartes(bio.value.firmaBaulPartes);
      }
    } catch {
      // El expediente es informativo; no bloquea el render del paso.
    }
  }, [instanceId]);

  // FEATURE 05 — al entrar a este paso (ya con la fecha de expedición de cada actor) se dispara la
  // consulta RNMC: POST corre la consulta por actor natural y persiste, y devuelve los checks. Si el
  // RNMC no aplica al trámite (rnmcEnabled=false) no se llama nada.
  const loadRnmc = useCallback(async () => {
    if (!instanceId || !rnmcEnabled) return;
    setRnmcLoading(true);
    try {
      const checks = await tramitesClient.runRnmc(instanceId);
      setRnmcChecks(checks);
    } catch {
      // El RNMC es informativo en este paso; su fallo no bloquea la firma/FUR.
    } finally {
      setRnmcLoading(false);
    }
  }, [instanceId, rnmcEnabled]);

  useEffect(() => {
    // Carga al montar: el setState ocurre tras el await (no es setState síncrono).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadDetail();
    void loadExpediente();
    void loadRnmc();
  }, [loadDetail, loadExpediente, loadRnmc]);

  const fv = useCallback(
    (key: string): string =>
      detail?.fieldValues.find((f) => f.fieldKey === key)?.valueText ?? '',
    [detail],
  );

  const comprador = useMemo(
    () => detail?.actors.find((a) => a.actorType === 'comprador') ?? null,
    [detail],
  );
  // Vendedor: solo existe en traspaso (parte saliente). En matrícula → null.
  const vendedor = useMemo(
    () => detail?.actors.find((a) => a.actorType === 'vendedor') ?? null,
    [detail],
  );
  // Identidad aprobada si CUALQUIER validación está en estado 'aprobado'.
  const identidadAprobada = useMemo(
    () => biometric.some((b) => b.status === 'aprobado'),
    [biometric],
  );

  const organismo = useMemo(
    () => ({
      id: fv('transit_office_id'),
      code: fv('transit_office_code'),
      name: fv('transit_office_name'),
      city: fv('transit_office_city'),
    }),
    [fv],
  );
  const organismoSelected = organismo.code.trim() !== '' || organismo.name.trim() !== '';

  // Auto-abre el modal al entrar al paso si aún no hay organismo seleccionado.
  const [organismoModalOpen, setOrganismoModalOpen] = useState(false);
  const [autoOpened, setAutoOpened] = useState(false);
  useEffect(() => {
    if (!detail || autoOpened) return;
    // Auto-abrir una sola vez al cargar el detalle; el guard `autoOpened` evita el bucle.
    // En solo lectura nunca se abre el selector de organismo. B11 (HU #10659): en traspaso el OT
    // proviene del RUNT (auto-bind en preflight) y no se selecciona/cambia → nunca se auto-abre.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (!organismoSelected && !readOnly && modalidad !== 'traspaso') setOrganismoModalOpen(true);
    setAutoOpened(true);
  }, [detail, organismoSelected, autoOpened, readOnly, modalidad]);

  const handleOrganismoConfirmed = async () => {
    setOrganismoModalOpen(false);
    await loadDetail();
    onRefresh?.();
  };

  // Tras generar la impronta o el FUR, refresca adjuntos para que el resumen y el
  // visor reflejen el nuevo documento sin remontar el paso.
  const handleDocumentGenerated = useCallback(() => {
    onRefresh?.();
    void loadExpediente();
  }, [onRefresh, loadExpediente]);

  return (
    <div className="space-y-8">
      <OrganismoSection
        organismo={organismo}
        organismoSelected={organismoSelected}
        modalidad={modalidad}
        onOpenModal={() => setOrganismoModalOpen(true)}
      />

      {/* HU #10799 — selección de placa preasignada como SECCIÓN explícita (Flujo A), solo en matrícula
          inicial y una vez elegido el OT. No aplica si el VIN ya tiene placa del RUNT (AC2). */}
      {modalidad === 'matricula_inicial' && organismoSelected && organismo.id && instanceId && (
        <PlacaPreasignadaSection
          instanceId={instanceId}
          organismoId={organismo.id}
          plateValue={fv('plate')}
          plateSource={detail?.fieldValues.find((f) => f.fieldKey === 'plate')?.source ?? ''}
          preferredDigitValue={fv('plate_preferred_last_digit')}
          readOnly={readOnly}
          onRefresh={() => {
            void loadDetail();
            onRefresh?.();
          }}
        />
      )}

      <MatriculaResumen
        modalidad={modalidad}
        status={detail?.status ?? 'borrador'}
        placa={fv('plate')}
        vehiculo={[fv('vehicle_brand'), fv('vehicle_line'), fv('vehicle_year')]
          .filter(Boolean)
          .join(' ')}
        vin={fv('vin')}
        vendedor={
          vendedor
            ? {
                nombre: vendedor.fullName,
                documento: vendedor.documentNumber,
                tipoDoc: vendedor.documentType,
              }
            : null
        }
        comprador={
          comprador
            ? {
                nombre: comprador.fullName,
                documento: comprador.documentNumber,
                tipoDoc: comprador.documentType,
              }
            : null
        }
        archivosCount={attachments.length}
        identidadAprobada={identidadAprobada}
        orgTransito={{ nombre: organismo.name, ciudad: organismo.city }}
        soat={{ estado: fv('soat_estado'), vencimiento: fv('soat_vencimiento') }}
      />

      {rnmcEnabled && <RnmcSection checks={rnmcChecks} loading={rnmcLoading} />}

      <ExpedienteVisor
        instanceId={instanceId}
        fieldValues={detail?.fieldValues ?? []}
        comprador={comprador}
        vendedor={vendedor}
        vin={fv('vin')}
        attachments={attachments}
        biometric={biometric}
        firmaBaulPartes={firmaBaulPartes}
        orgTransito={{ nombre: organismo.name, ciudad: organismo.city, codigo: organismo.code }}
      />

      {modalidad === 'traspaso' && (
        <FirmaSection instanceId={instanceId} onRefresh={onRefresh} />
      )}
      <ParticipantesSection instanceId={instanceId} />
      <ImprontaSection instanceId={instanceId} onRefresh={handleDocumentGenerated} />
      <FurSection
        instanceId={instanceId}
        modalidad={modalidad}
        onRefresh={handleDocumentGenerated}
      />

      <ExpedienteTimeline statusHistory={detail?.statusHistory ?? []} />

      {organismoModalOpen && instanceId && modalidad !== 'traspaso' && (
        <OrganismoModal
          instanceId={instanceId}
          suggestedName={fv('transit_office_name')}
          onClose={() => setOrganismoModalOpen(false)}
          onConfirmed={() => void handleOrganismoConfirmed()}
        />
      )}
    </div>
  );
}

// ── Placa preasignada (Flujo A, HU #10799) ────────────────────────────

/**
 * Sección explícita del paso FUR para elegir la placa preasignada del rango del OT (Flujo A). Reemplaza
 * la antigua fase modal. No aplica si el VIN ya tiene placa del RUNT (source 'consultation', AC2). Si no
 * hay placas disponibles, informa que el OT la asignará (Flujo B, AC3). Con buscador para rangos grandes.
 */
export function PlacaPreasignadaSection({
  instanceId,
  organismoId,
  plateValue,
  plateSource,
  preferredDigitValue = '',
  readOnly,
  onRefresh,
}: {
  instanceId: string;
  organismoId: string;
  plateValue: string;
  plateSource: string;
  /** HU #10805 — dígito de preferencia persistido (field_value `plate_preferred_last_digit`). */
  preferredDigitValue?: string;
  readOnly: boolean;
  onRefresh?: () => void;
}) {
  const [plates, setPlates] = useState<PlateDetail[]>([]);
  const [loaded, setLoaded] = useState(false);
  const [query, setQuery] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [changing, setChanging] = useState(false);
  // HU #10805 — dígito de preferencia (0-9) para radicar sin placa: guía para el OT al asignar.
  const [preferredDigit, setPreferredDigit] = useState(() => preferredDigitValue ?? '');
  const [savingDigit, setSavingDigit] = useState(false);
  // HU #10806 (AC3) — ¿la ruta de preasignación está activa para esta compañía/OT? null = cargando.
  const [preassignEnabled, setPreassignEnabled] = useState<boolean | null>(null);

  const placa = plateValue.trim();
  // AC2 — el VIN ya tiene placa del RUNT (no la eligió el usuario): no aplica la preasignación.
  const vinTienePlacaRunt = placa !== '' && plateSource === 'consultation';
  const placaElegida = placa !== '' && plateSource === 'user';
  const mostrarSelector = !readOnly && !vinTienePlacaRunt && (!placaElegida || changing);

  useEffect(() => {
    if (!mostrarSelector) return;
    let active = true;
    // HU #10806 — consulta el estado de la ruta antes de ofrecer el selector: si no está habilitada,
    // se avisa (el trámite se entregará estándar) en vez de simular que preasigna.
    // HU #10806 (Alternativa C) — persiste la decisión de ruta en borrador como field_value
    // `plate_route_active`. Es la fuente que consume el trigger de BD para fijar automáticamente
    // `plate_flow_status = 'preasignado'` al radicar sin placa, aunque el binario del API esté desfasado.
    const persistRouteActive = (enabled: boolean) => {
      void tramitesClient
        .patchFieldValues(instanceId, [
          { formFieldId: null, fieldKey: 'plate_route_active', valueText: String(enabled) },
        ])
        .catch(() => {
          /* no bloquear el wizard si la persistencia falla; el submit sigue decidiendo la ruta */
        });
    };
    getPlatePreassignStatus(organismoId)
      .then((s) => {
        if (active) {
          setPreassignEnabled(s.enabled);
          persistRouteActive(s.enabled);
        }
      })
      .catch(() => {
        // Ante un fallo de la consulta, no bloquear el flujo: se asume habilitada (default previo).
        if (active) {
          setPreassignEnabled(true);
          persistRouteActive(true);
        }
      });
    listAvailablePlatesForCompany(organismoId)
      .then((data) => {
        if (active) {
          setPlates(data);
          setLoaded(true);
        }
      })
      .catch(() => {
        if (active) setLoaded(true);
      });
    return () => {
      active = false;
    };
  }, [organismoId, mostrarSelector, instanceId]);

  const pick = async (plate: string) => {
    setSaving(true);
    setError(null);
    try {
      await tramitesClient.patchFieldValues(instanceId, [
        { formFieldId: null, fieldKey: 'plate', valueText: plate },
      ]);
      setChanging(false);
      onRefresh?.();
    } catch {
      setError('No se pudo asignar la placa. Inténtalo de nuevo.');
    } finally {
      setSaving(false);
    }
  };

  // HU #10806 (AC1) — deshacer la selección de placa: limpia el field `plate` (='') y reabre el
  // selector + el dígito de preferencia. Sin placa, DecideAsync enruta por preasignación (Flujo B),
  // así que esto es lo que permite volver a la ruta sin placa o al dígito tras haber elegido una.
  const clearPlate = async () => {
    setSaving(true);
    setError(null);
    try {
      await tramitesClient.patchFieldValues(instanceId, [
        { formFieldId: null, fieldKey: 'plate', valueText: '' },
      ]);
      setChanging(true);
      onRefresh?.();
    } catch {
      setError('No se pudo quitar la placa. Inténtalo de nuevo.');
    } finally {
      setSaving(false);
    }
  };

  // HU #10805 — persiste el dígito de preferencia (o lo limpia con ''). Solo es una guía para el OT;
  // no cambia el enrutamiento: sin placa el trámite sigue cayendo por preasignación.
  const saveDigit = async (value: string) => {
    setPreferredDigit(value);
    setSavingDigit(true);
    setError(null);
    try {
      await tramitesClient.patchFieldValues(instanceId, [
        { formFieldId: null, fieldKey: 'plate_preferred_last_digit', valueText: value },
      ]);
      onRefresh?.();
    } catch {
      setError('No se pudo guardar el dígito de preferencia. Inténtalo de nuevo.');
    } finally {
      setSavingDigit(false);
    }
  };

  const filtered = query.trim()
    ? plates.filter((p) => p.plate.toLowerCase().includes(query.trim().toLowerCase()))
    : plates;

  const shell = (children: ReactNode) => (
    <section className="rounded-2xl border border-[#DFE5ED] p-5 dark:border-white/10">
      <h3 className="text-sm font-bold">Placa preasignada</h3>
      {children}
    </section>
  );

  if (vinTienePlacaRunt) {
    return shell(
      <p className="mt-2 text-xs opacity-80">
        El vehículo ya tiene placa asignada según el RUNT (
        <span className="font-mono font-semibold">{placa}</span>
        ). No aplica la preasignación de placa.
      </p>,
    );
  }

  if (placaElegida && !changing) {
    return shell(
      <div className="mt-2 flex items-center gap-3">
        <p className="text-xs opacity-80">
          Placa seleccionada: <span className="font-mono font-semibold">{placa}</span>
        </p>
        {!readOnly && (
          <>
            <button
              type="button"
              disabled={saving}
              onClick={() => setChanging(true)}
              className="rounded-lg border px-3 py-1 text-[11px] font-semibold disabled:opacity-50"
            >
              Cambiar
            </button>
            <button
              type="button"
              disabled={saving}
              onClick={() => void clearPlate()}
              className="rounded-lg border px-3 py-1 text-[11px] font-semibold disabled:opacity-50"
            >
              Quitar placa
            </button>
          </>
        )}
      </div>,
    );
  }

  if (!mostrarSelector) {
    return null;
  }

  // HU #10806 (AC3) — la ruta de placa NO está habilitada para esta compañía/OT: avisar que el
  // trámite se entregará de forma estándar, en vez de mostrar el selector como si preasignara.
  if (preassignEnabled === false) {
    return shell(
      <p className="mt-2 text-xs opacity-80" role="status">
        La preasignación de placa no está habilitada para este organismo de tránsito o tu compañía. El
        trámite se entregará de forma estándar (sin asignación de placa por el OT).
      </p>,
    );
  }

  return shell(
    <div className="mt-2 flex flex-col gap-3">
      <p className="text-[11px] opacity-70">
        Selecciona una placa del rango asignado por el organismo de tránsito. Si no seleccionas ninguna, el
        trámite se enviará al OT para que asigne la placa.
      </p>
      {error && (
        <p className="text-[11px] font-medium" style={{ color: '#FF4E00' }} role="alert">
          {error}
        </p>
      )}
      {loaded && plates.length === 0 ? (
        <p className="text-xs opacity-80">
          No hay placas disponibles en el rango; el trámite se enviará al OT para que asigne la placa.
        </p>
      ) : (
        <>
          <div className="flex items-center gap-2 rounded-xl border px-3 py-2">
            <Search className="h-4 w-4 opacity-60" aria-hidden="true" />
            <input
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Buscar placa…"
              aria-label="Buscar placa disponible"
              className="w-full bg-transparent text-xs outline-none"
            />
          </div>
          <div className="grid max-h-64 grid-cols-3 gap-2 overflow-y-auto sm:grid-cols-4">
            {filtered.map((p) => (
              <button
                key={p.id}
                type="button"
                disabled={saving}
                onClick={() => void pick(p.plate)}
                className="rounded-xl border p-2 text-center font-mono text-xs font-semibold hover:border-[#557EFF] disabled:opacity-50"
              >
                {p.plate}
              </button>
            ))}
          </div>
        </>
      )}
      {/* HU #10805 — dígito de preferencia para radicar sin placa (guía para el OT; opcional). */}
      <label className="mt-1 flex flex-col gap-1 text-[11px] font-semibold">
        Dígito de preferencia (opcional)
        <span className="text-[10px] font-normal opacity-70">
          Si radicas sin placa, indica el número en el que prefieres que termine. El OT lo toma como
          guía: puede asignar una placa que termine en ese dígito u otra.
        </span>
        <select
          value={preferredDigit}
          disabled={savingDigit}
          onChange={(e) => void saveDigit(e.target.value)}
          aria-label="Dígito de preferencia de placa"
          className="mt-1 w-44 rounded-lg border px-2 py-1 text-xs"
        >
          <option value="">Sin preferencia</option>
          {['0', '1', '2', '3', '4', '5', '6', '7', '8', '9'].map((d) => (
            <option key={d} value={d}>
              Termina en {d}
            </option>
          ))}
        </select>
      </label>

      {changing && placa !== '' && (
        <button
          type="button"
          onClick={() => setChanging(false)}
          className="self-start rounded-lg border px-3 py-1 text-[11px] font-semibold"
        >
          Cancelar
        </button>
      )}
    </div>,
  );
}

// ── Organismo de tránsito ─────────────────────────────────────────────

function OrganismoSection({
  organismo,
  organismoSelected,
  modalidad,
  onOpenModal,
}: {
  organismo: { id: string; code: string; name: string; city: string };
  organismoSelected: boolean;
  modalidad: WizardModalidad;
  onOpenModal: () => void;
}) {
  const readOnly = useWizardReadOnly();

  // B11 (HU #10659) — en TRASPASO el organismo lo fija el RUNT (auto-bind en el preflight): solo
  // lectura, sin "Seleccionar"/"Cambiar". Si el nombre RUNT no está habilitado para la empresa
  // (hay nombre pero no id resuelto) se avisa, sin ofrecer selector.
  if (modalidad === 'traspaso') {
    const hasId = organismo.id.trim() !== '';
    const hasName = organismo.name.trim() !== '';
    return (
      <section className="space-y-3" aria-label="Organismo de tránsito">
        <div>
          <h4 className="text-sm font-bold">Organismo de tránsito</h4>
          <p className="text-xs opacity-70">
            El organismo proviene del RUNT y no puede modificarse en un traspaso.
          </p>
        </div>

        {hasId ? (
          <div
            className="rounded-xl border p-3 flex items-center gap-3"
            style={{ borderColor: '#8CC63F' }}
          >
            <Building2 className="h-4 w-4 shrink-0" style={{ color: '#5B8A1F' }} aria-hidden="true" />
            <div className="min-w-0">
              <p className="text-xs font-semibold">{organismo.name || 'Organismo del RUNT'}</p>
              <p className="text-[11px] opacity-70">
                {[organismo.city, organismo.code].filter(Boolean).join(' · ')}
              </p>
            </div>
          </div>
        ) : hasName ? (
          <div
            className="rounded-xl border p-3 text-xs"
            style={{ borderColor: '#F9AC00', background: 'rgba(249,172,0,0.08)', color: '#F9AC00' }}
            role="status"
          >
            El organismo registrado en el RUNT ({organismo.name}) no está habilitado para tu empresa.
            Contacta al administrador para habilitarlo; no es posible cambiarlo manualmente en un
            traspaso.
          </div>
        ) : (
          <div
            className="rounded-xl border p-3 text-xs"
            style={{ borderColor: '#F9AC00', background: 'rgba(249,172,0,0.08)', color: '#F9AC00' }}
            role="status"
          >
            El organismo de tránsito se tomará del RUNT al ejecutar las validaciones del trámite.
          </div>
        )}
      </section>
    );
  }

  return (
    <section className="space-y-3" aria-label="Organismo de tránsito">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h4 className="text-sm font-bold">Organismo de tránsito</h4>
          <p className="text-xs opacity-70">
            El organismo donde se radicará el trámite. Es necesario para generar
            el FUR, pero no bloquea guardar ni enviar el trámite.
          </p>
        </div>
        {!readOnly || !organismoSelected ? (
          <button
            type="button"
            onClick={onOpenModal}
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-[11px] font-semibold border shrink-0"
            style={{ borderColor: '#557EFF', color: '#557EFF' }}
          >
            <Building2 className="h-3 w-3" />
            {organismoSelected ? 'Cambiar' : 'Seleccionar'}
          </button>
        ) : null}
      </div>

      {organismoSelected ? (
        <div
          className="rounded-xl border p-3 flex items-center gap-3"
          style={{ borderColor: '#8CC63F' }}
        >
          <Building2 className="h-4 w-4 shrink-0" style={{ color: '#5B8A1F' }} aria-hidden="true" />
          <div className="min-w-0">
            <p className="text-xs font-semibold">{organismo.name || 'Organismo seleccionado'}</p>
            <p className="text-[11px] opacity-70">
              {[organismo.city, organismo.code].filter(Boolean).join(' · ')}
            </p>
          </div>
        </div>
      ) : (
        <div
          className="rounded-xl border p-3 text-xs"
          style={{ borderColor: '#F9AC00', background: 'rgba(249,172,0,0.08)', color: '#F9AC00' }}
          role="status"
        >
          Aún no has seleccionado el organismo de tránsito.
        </div>
      )}
    </section>
  );
}

function OrganismoModal({
  instanceId,
  suggestedName,
  onClose,
  onConfirmed,
}: {
  instanceId: string;
  suggestedName: string;
  onClose: () => void;
  onConfirmed: () => void;
}) {
  const [query, setQuery] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // OTs habilitados para la empresa (catálogo real filtrado por grants). El operador
  // solo puede elegir de esta lista; ya no es un catálogo estático del frontend.
  const [offices, setOffices] = useState<TransitOfficeOption[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setLoading(true);
    tramitesClient
      .listTransitOffices()
      .then((list) => {
        if (active) setOffices(list);
      })
      .catch(() => {
        if (active) setError('No se pudieron cargar los organismos habilitados.');
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, []);

  // Sugerencia desde RUNT: solo se ofrece si el organismo registrado en RUNT está
  // entre los HABILITADOS de la empresa (no se puede radicar en uno no habilitado).
  const runtSuggestion = useMemo<TransitOfficeOption | null>(() => {
    const n = suggestedName.trim().toLowerCase();
    if (!n) return null;
    return offices.find((o) => o.name.toLowerCase() === n) ?? null;
  }, [suggestedName, offices]);

  const results = useMemo(() => {
    const q = query.trim().toLowerCase();
    const list = q
      ? offices.filter(
          (o) =>
            o.name.toLowerCase().includes(q) || o.code.toLowerCase().includes(q),
        )
      : offices;
    return list.slice(0, 40);
  }, [query, offices]);

  const persist = async (org: TransitOfficeOption) => {
    setSaving(true);
    setError(null);
    try {
      await tramitesClient.patchFieldValues(instanceId, [
        { formFieldId: null, fieldKey: 'transit_office_id', valueText: org.id },
        { formFieldId: null, fieldKey: 'transit_office_code', valueText: org.code },
        { formFieldId: null, fieldKey: 'transit_office_name', valueText: org.name },
        { formFieldId: null, fieldKey: 'transit_office_city', valueText: org.cityCode },
      ]);
      // HU #10799 — la selección de placa (Flujo A) ya no vive aquí: es una SECCIÓN explícita del paso FUR
      // (PlacaPreasignadaSection). El modal solo confirma el OT.
      onConfirmed();
    } catch {
      setError('No se pudo guardar el organismo. Inténtalo de nuevo.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 grid place-items-center bg-black/40 backdrop-blur-sm px-4"
      role="dialog"
      aria-modal="true"
      aria-label="Seleccionar organismo de tránsito"
    >
      <div
        className="bg-white dark:bg-[#0B0F14] rounded-2xl p-6 w-full max-w-lg border flex flex-col max-h-[85vh]"
      >
        <div className="flex items-start justify-between mb-3">
          <div>
            <h3 className="text-sm font-bold">Organismo de tránsito</h3>
            <p className="text-[11px] opacity-70">
              Elige dónde se radicará el trámite.
            </p>
          </div>
          <button type="button" onClick={onClose} aria-label="Cerrar">
            <X className="h-5 w-5" />
          </button>
        </div>

        {runtSuggestion && (
          <button
            type="button"
            onClick={() => void persist(runtSuggestion)}
            disabled={saving}
            className="mb-3 w-full text-left rounded-xl border p-3 disabled:opacity-50"
            style={{ borderColor: '#557EFF', background: 'rgba(85,126,255,0.06)' }}
          >
            <p className="text-[10px] font-semibold uppercase" style={{ color: '#557EFF' }}>
              Usar el organismo registrado en RUNT
            </p>
            <p className="text-xs font-semibold mt-0.5">{runtSuggestion.name}</p>
            {runtSuggestion.code && (
              <p className="text-[11px] opacity-70">{runtSuggestion.code}</p>
            )}
          </button>
        )}

        <div className="relative mb-3">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-3.5 w-3.5 opacity-50" aria-hidden="true" />
          <input
            type="text"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Buscar por nombre o código…"
            aria-label="Buscar organismo de tránsito"
            className={`${INPUT_BASE} pl-9`}
            autoFocus
          />
        </div>

        {error && (
          <p className="text-[11px] font-medium mb-2" style={{ color: '#FF4E00' }} role="alert">
            {error}
          </p>
        )}

        <ul className="space-y-1.5 overflow-y-auto" aria-label="Catálogo de organismos">
          {results.map((o) => (
            <li key={o.id}>
              <button
                type="button"
                onClick={() => void persist(o)}
                disabled={saving}
                className="w-full text-left rounded-xl border p-2.5 hover:border-[#557EFF] disabled:opacity-50"
              >
                <p className="text-xs font-semibold">{o.name}</p>
                <p className="text-[11px] opacity-70">{o.code}</p>
              </button>
            </li>
          ))}
          {loading && (
            <li className="text-[11px] opacity-60 py-3 text-center">
              Cargando organismos habilitados…
            </li>
          )}
          {!loading && offices.length === 0 && (
            <li className="text-[11px] py-3 text-center" style={{ color: '#F9AC00' }}>
              Tu compañía no tiene organismos de tránsito habilitados. Contacta al
              administrador para habilitarlos.
            </li>
          )}
          {!loading && offices.length > 0 && results.length === 0 && (
            <li className="text-[11px] opacity-60 py-3 text-center">
              Sin resultados para «{query}».
            </li>
          )}
        </ul>
      </div>
    </div>
  );
}

// ── Firma (traspaso) ─────────────────────────────────────────────────

function FirmaSection({
  instanceId,
  onRefresh,
}: {
  instanceId: string | null;
  onRefresh?: () => void;
}) {
  const [signatures, setSignatures] = useState<Signature[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const readOnly = useWizardReadOnly();

  const load = useCallback(async () => {
    if (!instanceId) return;
    try {
      const list = await tramitesClient.listFirmas(instanceId);
      setSignatures(list);
      setError(() => null);
    } catch (err) {
      setError(() =>
        err instanceof Error ? err.message : 'Error al cargar las firmas.',
      );
    }
  }, [instanceId]);

  useEffect(() => {
    void load();
  }, [load]);

  const refresh = async () => {
    setLoading(true);
    try {
      await load();
    } finally {
      setLoading(false);
    }
    onRefresh?.();
  };

  // HU #11019 — saliente antes que entrante, igual que el expediente y el dashboard.
  const partes: SignatureParte[] = ['vendedor', 'comprador'];

  return (
    <section className="space-y-4" aria-label="Firma de la compraventa">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h4 className="text-sm font-bold">Firma de la compraventa</h4>
          <p className="text-xs opacity-70">
            Estado informativo de la firma electrónica por parte. La lógica
            definitiva de firmas está pendiente de definición de negocio, por lo
            que <strong>no bloquea</strong> preparar ni radicar el traspaso.
          </p>
        </div>
        {!readOnly && (
          <button
            type="button"
            onClick={() => void refresh()}
            disabled={loading || !instanceId}
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-[11px] font-semibold border shrink-0 disabled:opacity-50"
            style={{ borderColor: '#557EFF', color: '#557EFF' }}
            aria-label="Actualizar estado de firmas"
          >
            <RefreshCw className={`h-3 w-3 ${loading ? 'animate-spin' : ''}`} />
            Actualizar
          </button>
        )}
      </div>

      {error && (
        <div
          className="rounded-xl p-3 text-xs border"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          {error}
        </div>
      )}

      <div className="space-y-4">
        {partes.map((parte) => {
          const signature = (signatures ?? []).find(
            (s) => s.parte === parte,
          );
          return (
            <FirmaParteCard
              key={parte}
              parte={parte}
              instanceId={instanceId}
              signature={signature ?? null}
              onChanged={() => void refresh()}
            />
          );
        })}
      </div>
    </section>
  );
}

function FirmaParteCard({
  parte,
  instanceId,
  signature,
  onChanged,
}: {
  parte: SignatureParte;
  instanceId: string | null;
  signature: Signature | null;
  onChanged: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const readOnly = useWizardReadOnly();

  const handleSimular = async () => {
    if (!instanceId || !signature) return;
    setBusy(true);
    setError(null);
    try {
      await tramitesClient.simularFirma(instanceId, signature.id);
      onChanged();
    } catch {
      setError('No se pudo simular la firma.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <fieldset
      className="rounded-xl border p-4"
      aria-label={`Firma ${PARTE_LABEL[parte]}`}
    >
      <legend className="px-1 text-xs font-bold flex items-center gap-2">
        {PARTE_LABEL[parte]}
        {signature && <FirmaBadge estado={signature.estado} />}
      </legend>

      {signature ? (
        <div className="space-y-2 text-xs">
          {signature.signUrl && signature.estado !== 'firmada' && (
            <CopyLink
              link={signature.signUrl}
              label={`Enlace de firma ${PARTE_LABEL[parte]}`}
            />
          )}
          {signature.estado === 'firmada' && (
            <p className="flex items-center gap-1.5 font-semibold" style={{ color: '#5B8A1F' }}>
              <Check className="h-3.5 w-3.5" /> Compraventa firmada
            </p>
          )}
          {signature.estado === 'enviada' && !readOnly && (
            <button
              type="button"
              onClick={() => void handleSimular()}
              disabled={busy || !instanceId}
              className="px-4 py-1.5 rounded-xl text-[11px] font-semibold border disabled:opacity-50"
              style={{ borderColor: '#557EFF', color: '#557EFF' }}
            >
              {busy ? 'Simulando…' : 'Simular firma (DEV)'}
            </button>
          )}
        </div>
      ) : (
        // HU #11019 — se retira el botón de solicitar la firma de la compraventa. El gate ya no la exige
        // desde ADR-0028 (B12/HU #10661), así que pedirla solo añadía un paso que no desbloquea nada.
        // El estado de una firma ya solicitada se sigue mostrando arriba.
        <p className="text-[11px] opacity-60">Firma no solicitada.</p>
      )}

      {error && (
        <p className="text-[11px] font-medium mt-2" style={{ color: '#FF4E00' }} role="alert">
          {error}
        </p>
      )}
    </fieldset>
  );
}

// ── Participantes (portal) ────────────────────────────────────────────

function ParticipantesSection({ instanceId }: { instanceId: string | null }) {
  const readOnly = useWizardReadOnly();
  const [participants, setParticipants] = useState<Participant[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [lastLink, setLastLink] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!instanceId) return;
    try {
      const list = await tramitesClient.listParticipantes(instanceId);
      setParticipants(list);
      setError(() => null);
    } catch (err) {
      setError(() =>
        err instanceof Error ? err.message : 'Error al cargar los participantes.',
      );
    }
  }, [instanceId]);

  useEffect(() => {
    void load();
  }, [load]);

  const refresh = async () => {
    await load();
  };

  const [rol, setRol] = useState<ParticipantRol>('comprador');
  const [nombre, setNombre] = useState('');
  const [email, setEmail] = useState('');
  const [telefono, setTelefono] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const handleInvite = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!instanceId) return;
    if (!nombre.trim() || !email.trim()) {
      setFormError('Completa nombre y correo.');
      return;
    }
    if (!EMAIL_RE.test(email.trim())) {
      setFormError('Correo no válido.');
      return;
    }
    setFormError(null);
    setSubmitting(true);
    try {
      const result = await tramitesClient.invitarParticipante(instanceId, {
        rol,
        nombre: nombre.trim(),
        email: email.trim(),
        telefono: telefono.trim() || null,
        whatsappOptIn: false,
      });
      setLastLink(absoluteLink(result.magicLinkPath));
      setNombre('');
      setEmail('');
      setTelefono('');
      await refresh();
    } catch (err) {
      const msg = err instanceof Error ? err.message : '';
      setFormError(
        msg.startsWith('409')
          ? 'Ya existe un participante activo para este rol.'
          : 'No se pudo invitar al participante.',
      );
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <section className="space-y-4" aria-label="Participantes del portal">
      <div>
        <h4 className="text-sm font-bold">Participantes (portal)</h4>
        <p className="text-xs opacity-70">
          Invita a las partes a completar su parte vía un enlace de portal
          (consentimiento, documentos, biométrica y firma). En DEV el enlace se
          entrega manualmente (sin envío de correo).
        </p>
      </div>

      {error && (
        <div
          className="rounded-xl p-3 text-xs border"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          {error}
        </div>
      )}

      {!readOnly && (
      <form
        onSubmit={handleInvite}
        className="rounded-xl border p-4 space-y-3"
        noValidate
      >
        <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
          <div>
            <label htmlFor="part-rol" className="text-xs font-semibold mb-1.5 block">
              Rol
            </label>
            <select
              id="part-rol"
              value={rol}
              onChange={(e) => setRol(e.target.value as ParticipantRol)}
              className={INPUT_BASE}
            >
              {ROL_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label htmlFor="part-nombre" className="text-xs font-semibold mb-1.5 block">
              Nombre completo
            </label>
            <input
              id="part-nombre"
              type="text"
              value={nombre}
              onChange={(e) => setNombre(e.target.value)}
              className={INPUT_BASE}
            />
          </div>
          <div>
            <label htmlFor="part-email" className="text-xs font-semibold mb-1.5 block">
              Correo electrónico
            </label>
            <input
              id="part-email"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className={INPUT_BASE}
            />
          </div>
          <div>
            <label htmlFor="part-telefono" className="text-xs font-semibold mb-1.5 block">
              Teléfono <span className="opacity-50 font-normal">(opcional)</span>
            </label>
            <input
              id="part-telefono"
              type="tel"
              value={telefono}
              onChange={(e) => setTelefono(e.target.value)}
              className={INPUT_BASE}
            />
          </div>
        </div>

        {formError && (
          <p className="text-[11px] font-medium" style={{ color: '#FF4E00' }} role="alert">
            {formError}
          </p>
        )}

        <div className="flex justify-end">
          <button
            type="submit"
            disabled={submitting || !instanceId}
            className="px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
            style={{ background: '#557EFF' }}
          >
            {submitting ? 'Invitando…' : 'Invitar participante'}
          </button>
        </div>
      </form>
      )}

      {lastLink && (
        <div className="space-y-2">
          <p className="text-[11px] font-semibold" style={{ color: '#5B8A1F' }}>
            Enlace de portal generado (DEV: sin envío de correo).
          </p>
          <CopyLink link={lastLink} label="Enlace de portal del participante" />
        </div>
      )}

      <ul className="space-y-2" aria-label="Lista de participantes">
        {(participants ?? []).map((p) => (
          <ParticipantRow
            key={p.id}
            instanceId={instanceId}
            participant={p}
            onReinvited={(link) => setLastLink(link)}
            onChanged={() => void refresh()}
          />
        ))}
        {participants !== null && participants.length === 0 && (
          <li className="text-[11px] opacity-60">Aún no hay participantes invitados.</li>
        )}
      </ul>
    </section>
  );
}

function ParticipantRow({
  instanceId,
  participant: p,
  onReinvited,
  onChanged,
}: {
  instanceId: string | null;
  participant: Participant;
  onReinvited: (link: string) => void;
  onChanged: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const readOnly = useWizardReadOnly();

  const handleReinvite = async () => {
    if (!instanceId) return;
    setBusy(true);
    setError(null);
    try {
      const result = await tramitesClient.reinvitarParticipante(instanceId, p.id);
      onReinvited(absoluteLink(result.magicLinkPath));
      onChanged();
    } catch (err) {
      const msg = err instanceof Error ? err.message : '';
      setError(
        msg.startsWith('429')
          ? 'Espera 24h antes de reenviar el recordatorio.'
          : msg.startsWith('409')
            ? 'El participante ya finalizó su parte.'
            : 'No se pudo reinvitar.',
      );
    } finally {
      setBusy(false);
    }
  };

  return (
    <li className="rounded-xl border p-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="text-xs font-semibold capitalize">
            {p.rol} · {p.nombre}
          </p>
          <p className="text-[11px] opacity-70 truncate">{p.email}</p>
          <div className="mt-1 flex flex-wrap gap-1.5">
            <StatusChip
              ok={p.consentDado}
              okLabel="Consentimiento dado"
              pendingLabel="Sin consentimiento"
            />
            {p.completado ? (
              <StatusChip ok okLabel="Completado" pendingLabel="" />
            ) : p.expirado ? (
              <span
                className="rounded-full px-2 py-0.5 text-[9px] font-bold"
                style={{ background: '#EEF1F5', color: '#9AA5B1' }}
              >
                Expirado
              </span>
            ) : (
              <span
                className="rounded-full px-2 py-0.5 text-[9px] font-bold"
                style={{ background: 'rgba(249,172,0,0.15)', color: '#F9AC00' }}
              >
                Pendiente
              </span>
            )}
          </div>
        </div>
        {!p.completado && !readOnly && (
          <button
            type="button"
            onClick={() => void handleReinvite()}
            disabled={busy || !instanceId}
            className="rounded-xl border px-3 py-1.5 text-[11px] font-semibold shrink-0 disabled:opacity-50"
            style={{ borderColor: '#557EFF', color: '#557EFF' }}
          >
            {busy ? 'Reinvitando…' : 'Reinvitar'}
          </button>
        )}
      </div>
      {error && (
        <p className="text-[11px] font-medium mt-1.5" style={{ color: '#FF4E00' }} role="alert">
          {error}
        </p>
      )}
    </li>
  );
}

function StatusChip({
  ok,
  okLabel,
  pendingLabel,
}: {
  ok: boolean;
  okLabel: string;
  pendingLabel: string;
}) {
  if (!ok && !pendingLabel) return null;
  return (
    <span
      className="rounded-full px-2 py-0.5 text-[9px] font-bold"
      style={
        ok
          ? { background: 'rgba(140,198,63,0.15)', color: '#5B8A1F' }
          : { background: '#EEF1F5', color: '#9AA5B1' }
      }
    >
      {ok ? okLabel : pendingLabel}
    </span>
  );
}

// ── Impronta integrada al trámite ────────────────────────────────────

/**
 * Botón "Generar Impronta" del paso FUR: genera el Certificado de Improntas Digitales (Kyverum
 * RUNT) con los datos ya disponibles del trámite (placa/VIN, documento del propietario, organismo
 * de tránsito, operador) y lo adjunta al expediente (mismo flujo que una subida manual). Solo se
 * muestra si aún no existe un adjunto tipo 'impronta' (cargado a mano o generado antes) — la
 * generación es idempotente por NO-regeneración en el backend.
 */
function ImprontaSection({
  instanceId,
  onRefresh,
}: {
  instanceId: string | null;
  onRefresh?: () => void;
}) {
  const [attachment, setAttachment] = useState<ProcedureAttachment | null | undefined>(undefined);
  const [generating, setGenerating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [radicado, setRadicado] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!instanceId) return;
    try {
      const list = await tramitesClient.getAttachments(instanceId);
      setAttachment(list.find((a) => a.tipo === 'impronta') ?? null);
    } catch {
      // Informativo; no bloquea el render del paso.
    }
  }, [instanceId]);

  useEffect(() => {
    // load solo hace setState DESPUÉS del await (no es cascada síncrona).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  const handleGenerate = async () => {
    if (!instanceId) return;
    setGenerating(true);
    setError(null);
    setRadicado(null);
    try {
      const result = await tramitesClient.generarImpronta(instanceId);
      setRadicado(result.radicado);
      await load();
      onRefresh?.();

      // Feature #11066 — la impronta queda en el trámite; NO se descarga automáticamente.
      // El operador la descarga desde el listado de adjuntos si la necesita.
    } catch (err) {
      const msg = err instanceof Error ? err.message : '';
      setError(
        msg.includes('organismo de tránsito')
          ? 'Selecciona el organismo de tránsito antes de generar la impronta.'
          : msg.includes('placa o el VIN')
            ? 'Falta la placa o el VIN del vehículo para generar la impronta.'
            : msg.includes('documento del propietario')
              ? 'Falta el documento del propietario para generar la impronta.'
              : msg.includes('ya existe un documento de impronta')
                ? 'Ya existe una impronta cargada para este trámite.'
                : msg.includes('operador')
                  ? 'No se pudo resolver el operador que solicita la impronta.'
                  : msg.includes('Kyverum RUNT')
                    ? 'Kyverum RUNT no pudo generar la impronta. Intenta de nuevo en unos minutos.'
                    : 'No se pudo generar la impronta.',
      );
    } finally {
      setGenerating(false);
    }
  };

  // Aún no se sabe si existe (carga inicial): no se muestra nada para evitar parpadeo del botón.
  if (attachment === undefined) return null;
  // Ya existía un adjunto de impronta ANTES de esta sesión (manual o generado antes): la sección
  // no aparece. Si se acaba de generar en esta sesión (radicado con valor), se mantiene visible
  // para mostrar el mensaje de éxito aunque el botón ya no se necesite.
  if (attachment && radicado === null) return null;

  return (
    <section className="space-y-4" aria-label="Generación de la impronta">
      <div>
        <h4 className="text-sm font-bold">Impronta de motor y chasis</h4>
        <p className="text-xs opacity-70">
          Genera el Certificado de Improntas Digitales (Kyverum RUNT) con los datos del trámite y
          adjúntalo automáticamente al expediente. Se descargará también a tu equipo. Si ya tienes
          tu propia impronta generada, puedes subirla manualmente en su lugar.
        </p>
      </div>

      {error && (
        <div
          className="rounded-xl p-3 text-xs border"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          {error}
        </div>
      )}

      {radicado && !error && (
        <div
          className="rounded-xl p-3 text-xs border"
          style={{ borderColor: '#8CC63F', background: 'rgba(140,198,63,0.08)', color: '#5B8A1F' }}
          role="status"
          aria-live="polite"
        >
          Impronta generada (radicado {radicado}) y cargada al trámite. La descarga se inició en tu
          navegador.
        </div>
      )}

      {!attachment && (
        <button
          type="button"
          onClick={() => void handleGenerate()}
          disabled={generating || !instanceId}
          className="px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
          style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
        >
          {generating ? 'Generando…' : 'Generar Improntas'}
        </button>
      )}
    </section>
  );
}

// ── FUR / compraventa ─────────────────────────────────────────────────

/** Tipos de documento generados por el FUR. */
const FUR_TIPOS = new Set(['fur', 'compraventa', 'certificado_identidad', 'certificado_identidad_vendedor', 'certificado_rnmc']);

/**
 * HU #10987 — tope de las observaciones manuales. El recuadro OBSERVACIONES del FUR es un campo
 * `multiline` de alto fijo en el manifest: un texto más largo se desbordaría del formulario. El
 * límite es de presentación, no de negocio.
 */
const FUR_OBSERVACIONES_MAX = 300;

function FurSection({
  instanceId,
  modalidad,
  onRefresh,
}: {
  instanceId: string | null;
  modalidad: WizardModalidad;
  onRefresh?: () => void;
}) {
  const [docs, setDocs] = useState<ProcedureAttachment[] | null>(null);
  const [consolidado, setConsolidado] = useState<ProcedureAttachment | null>(null);
  const [generating, setGenerating] = useState(false);
  const [generatingConsolidado, setGeneratingConsolidado] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [consolidadoError, setConsolidadoError] = useState<string | null>(null);
  const [lastResult, setLastResult] = useState<FurDocument[] | null>(null);
  // HU #10924 — plantilla de FUR que aplica según la clasificación del vehículo (backend = fuente de verdad).
  const [furFormat, setFurFormat] = useState<string | null>(null);
  // HU #10987 / #10988 (Feature #10972) — el recuadro OBSERVACIONES y la fecha del trámite del FUR
  // leían `fur_observations` y `fur_processing_date`, dos llaves que NADIE escribía: el gestor no
  // podía aportar observaciones y la fecha era la de generación, impuesta por un fallback silencioso.
  const [observaciones, setObservaciones] = useState('');
  const [fechaTramite, setFechaTramite] = useState('');
  const [savingCampos, setSavingCampos] = useState(false);

  const load = useCallback(async () => {
    if (!instanceId) return;
    try {
      const list = await tramitesClient.getAttachments(instanceId);
      setDocs(list.filter((a) => FUR_TIPOS.has(a.tipo)));
      setConsolidado(list.find((a) => a.tipo === 'consolidado') ?? null);
      const fmt = await tramitesClient.getFurTemplateFormat(instanceId);
      setFurFormat(fmt.format);
      const detail = await tramitesClient.getInstance(instanceId);
      const valor = (key: string) =>
        detail?.fieldValues?.find((f) => f.fieldKey === key)?.valueText ?? '';
      setObservaciones(valor('fur_observations'));
      setFechaTramite(valor('fur_processing_date').slice(0, 10));
    } catch {
      // El listado de adjuntos y el formato son secundarios; el error de generar se muestra abajo.
    }
  }, [instanceId]);

  /**
   * Persiste observaciones y fecha del trámite. Se dispara al perder el foco y ANTES de generar, para
   * que el PDF salga con lo que el gestor tiene en pantalla y no con lo último guardado.
   * Best-effort: fuera de borrador/subsanación el backend responde `not_draft` y el campo queda de
   * solo lectura de hecho — no tiene sentido bloquear la generación por eso.
   */
  const guardarCampos = useCallback(async () => {
    if (!instanceId) return;
    setSavingCampos(true);
    try {
      await tramitesClient.patchFieldValues(instanceId, [
        { formFieldId: null, fieldKey: 'fur_observations', valueText: observaciones.trim() || null },
        { formFieldId: null, fieldKey: 'fur_processing_date', valueText: fechaTramite || null },
      ]);
    } catch {
      // Silencio intencionado: ver comentario de arriba.
    } finally {
      setSavingCampos(false);
    }
  }, [instanceId, observaciones, fechaTramite]);

  useEffect(() => {
    // load solo hace setState DESPUÉS del await (no es cascada síncrona).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  const handleGenerate = async () => {
    if (!instanceId) return;
    setGenerating(true);
    setError(null);
    try {
      // HU #10987/#10988 — guardar antes de generar: si el gestor escribe y pulsa el botón sin que
      // el textarea pierda el foco, el PDF saldría sin ese texto.
      await guardarCampos();
      const result = await tramitesClient.generarFur(instanceId);
      setLastResult(result.documents);
      await load();
      onRefresh?.();
    } catch (err) {
      const msg = err instanceof Error ? err.message : '';
      // HU #11017 — la identidad dejó de bloquear la generación del FUR en HU #10463 (el documento sale
      // con el sello "NO FIRMADO"), así que el backend ya no emite `biometria_gate` y ese mensaje solo
      // podía confundir. La ÚNICA restricción que queda es el organismo de tránsito, que es un dato
      // imprescindible del formulario: sin él no hay FUR que llenar.
      setError(
        msg.includes('organismo_requerido')
          ? 'Selecciona el organismo de tránsito antes de generar el FUR.'
          : msg.startsWith('409')
            ? 'No se pudo generar el FUR: selecciona el organismo de tránsito e inténtalo de nuevo.'
            : 'No se pudo generar el FUR.',
      );
    } finally {
      setGenerating(false);
    }
  };

  const handleGenerateConsolidado = async () => {
    if (!instanceId) return;
    setGeneratingConsolidado(true);
    setConsolidadoError(null);
    try {
      // HU #11017 — el consolidado se genera aunque falten documentos obligatorios: si vuelve marcado
      // como incompleto se avisa qué falta, en vez de dejar al gestor con un expediente que el
      // organismo rechazará sin explicación.
      const generado = await tramitesClient.generarConsolidado(instanceId, undefined, true);
      if (generado?.incompleto) {
        const faltantes = (generado.documentosFaltantes ?? []).map(documentLabel).join(', ');
        setConsolidadoError(
          faltantes
            ? `Consolidado generado, pero faltan documentos obligatorios: ${faltantes}.`
            : 'Consolidado generado, pero faltan documentos obligatorios.',
        );
      }
      await load();
      onRefresh?.();
    } catch (err) {
      const msg = err instanceof Error ? err.message : '';
      setConsolidadoError(
        msg.includes('fur_requerido')
          ? 'Genera el FUR antes de crear el consolidado.'
          : msg.includes('documentos_incompletos')
            ? 'Sube los documentos obligatorios antes de generar el consolidado.'
            : msg.includes('modalidad_no_soportada')
              ? 'El consolidado no está disponible para esta modalidad.'
              : 'No se pudo generar el consolidado.',
      );
    } finally {
      setGeneratingConsolidado(false);
    }
  };

  const generated = (docs ?? []).length > 0 || (lastResult ?? []).length > 0;
  const consolidadoGenerated = consolidado !== null;

  return (
    <section className="space-y-4" aria-label="Generación del FUR">
      <div>
        <div className="flex items-center gap-2">
          <h4 className="text-sm font-bold">FUR / contrato de compraventa</h4>
          {furFormat && (
            <span
              className="rounded-full px-2 py-0.5 text-[10px] font-semibold"
              style={{ background: 'rgba(85,126,255,0.12)', color: '#557EFF' }}
              title="Plantilla de FUR determinada por la clasificación del vehículo"
            >
              Plantilla: {furFormat}
            </span>
          )}
        </div>
        <p className="text-xs opacity-70">
          Genera el FUR y el certificado de identidad (y, en traspaso, el
          contrato de compraventa) con los datos del trámite. Este paso es
          opcional para guardar o enviar el trámite: puedes generar los PDF
          ahora o más adelante. Requiere biométrica aprobada y organismo
          seleccionado.
        </p>
      </div>

      {error && (
        <div
          className="rounded-xl p-3 text-xs border"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          {error}
        </div>
      )}

      {/* HU #10987 / #10988 — datos del FUR que aporta el gestor. Antes de esta HU el recuadro
          OBSERVACIONES del formulario oficial era de solo-lectura automática y la fecha era la de
          generación del PDF. */}
      <div className="rounded-xl border p-3 space-y-3" style={{ borderColor: '#DFE5ED' }}>
        <div>
          <label htmlFor="fur-fecha-tramite" className="text-xs font-semibold mb-1.5 block">
            Fecha del trámite
          </label>
          <input
            id="fur-fecha-tramite"
            type="date"
            value={fechaTramite}
            onChange={(e) => setFechaTramite(e.target.value)}
            onBlur={() => void guardarCampos()}
            disabled={!instanceId}
            className="rounded-lg border px-3 py-2 text-xs"
            style={{ borderColor: '#DFE5ED' }}
          />
          <p className="text-[10px] opacity-60 mt-1">
            Se estampa en el FUR y en el resto de documentos del trámite. Si la dejas vacía se usa la
            fecha de hoy.
          </p>
        </div>

        <div>
          <label htmlFor="fur-observaciones" className="text-xs font-semibold mb-1.5 block">
            Observaciones
          </label>
          <textarea
            id="fur-observaciones"
            value={observaciones}
            onChange={(e) => setObservaciones(e.target.value)}
            onBlur={() => void guardarCampos()}
            disabled={!instanceId}
            rows={3}
            maxLength={FUR_OBSERVACIONES_MAX}
            placeholder="Particularidades del vehículo o del negocio que el formulario no contempla."
            className="w-full rounded-lg border px-3 py-2 text-xs"
            style={{ borderColor: '#DFE5ED' }}
          />
          <p className="text-[10px] opacity-60 mt-1">
            {observaciones.length}/{FUR_OBSERVACIONES_MAX} · Se imprimen en el recuadro OBSERVACIONES
            del FUR, junto a las transformaciones declaradas y, si aplica, el gravamen.
            {savingCampos && ' · Guardando…'}
          </p>
        </div>
      </div>

      <button
        type="button"
        onClick={() => void handleGenerate()}
        disabled={generating || !instanceId}
        className="px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
        style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
      >
        {generating
          ? 'Generando…'
          : generated
            ? 'Re-generar FUR / certificado'
            : 'Generar FUR / certificado'}
      </button>

      {generated && (
        <ul className="space-y-2" aria-label="Documentos generados">
          {(docs ?? []).map((d) => (
            <li
              key={d.id}
              className="rounded-xl border p-3 flex items-center gap-3"
              style={{ borderColor: '#8CC63F' }}
            >
              <FileText className="h-4 w-4 shrink-0" style={{ color: '#5B8A1F' }} aria-hidden="true" />
              <div className="min-w-0 flex-1">
                <p className="text-xs font-semibold">
                  {documentLabel(d.tipo)} <span className="opacity-50 font-normal">· {d.filename}</span>
                </p>
                <p className="text-[10px] opacity-60 truncate" title={d.sha256}>
                  SHA-256: {d.sha256}
                </p>
              </div>
              <DownloadButton instanceId={instanceId} attachment={d} />
            </li>
          ))}
        </ul>
      )}

      {(modalidad === 'matricula_inicial' || modalidad === 'traspaso') && (
        <div className="space-y-3 pt-2 border-t">
          <div>
            <h5 className="text-xs font-bold">Expediente consolidado</h5>
            <p className="text-[11px] opacity-70">
              Un solo PDF con el FUR, el certificado de identidad y los documentos
              cargados en el trámite
              {modalidad === 'traspaso' ? ' (incluye el contrato de compraventa)' : ''}.
              Opcional: puedes generarlo cuando el FUR esté listo.
            </p>
          </div>

          {consolidadoError && (
            <div
              className="rounded-xl p-3 text-xs border"
              style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
              role="alert"
              aria-live="polite"
            >
              {consolidadoError}
            </div>
          )}

          <button
            type="button"
            onClick={() => void handleGenerateConsolidado()}
            disabled={generatingConsolidado || !instanceId}
            className="px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
            style={{ background: '#162744' }}
          >
            {generatingConsolidado
              ? 'Generando consolidado…'
              : consolidadoGenerated
                ? 'Re-generar consolidado'
                : 'Generar consolidado'}
          </button>

          {consolidadoGenerated && consolidado && (
            <div
              className="rounded-xl border p-3 flex items-center gap-3"
              style={{ borderColor: '#557EFF' }}
            >
              <FileText className="h-4 w-4 shrink-0" style={{ color: '#557EFF' }} aria-hidden="true" />
              <div className="min-w-0 flex-1">
                <p className="text-xs font-semibold">
                  consolidado · {consolidado.filename}
                </p>
                <p className="text-[10px] opacity-60 truncate" title={consolidado.sha256}>
                  SHA-256: {consolidado.sha256}
                </p>
              </div>
              <DownloadButton instanceId={instanceId} attachment={consolidado} />
            </div>
          )}
        </div>
      )}

      {/* HU #10611 (Feature #10587) — asignación de SOAT de la ruta de placa, ubicada bajo el
          Expediente consolidado (movida desde EstadoAcciones). Se auto-oculta salvo sub-estado
          de placa 'asignado' (el OT ya asignó la placa). */}
      <PlateFlowCompleteSection instanceId={instanceId} onRefresh={onRefresh} />
    </section>
  );
}

/**
 * Proceso del gestor en sub-estado Asignado: checks opcionales (SOAT / impuesto) y
 * avance a Terminado para desbloquear Aprobar/Rechazar del OT.
 */
function PlateFlowCompleteSection({
  instanceId,
  onRefresh,
}: {
  instanceId: string | null;
  onRefresh?: () => void;
}) {
  const [plateFlowStatus, setPlateFlowStatus] = useState<string | null>(null);
  const [soatPagado, setSoatPagado] = useState(false);
  const [impuestoPagado, setImpuestoPagado] = useState(false);
  const [working, setWorking] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!instanceId) return;
    let active = true;
    tramitesClient
      .getInstance(instanceId)
      .then((d) => {
        if (!active) return;
        setPlateFlowStatus(d?.plateFlowStatus ?? null);
        const fields = d?.fieldValues ?? [];
        setSoatPagado(fields.some((f) => f.fieldKey === 'soat_pagado' && f.valueText === 'true'));
        setImpuestoPagado(
          fields.some(
            (f) => f.fieldKey === 'impuesto_departamental_pagado' && f.valueText === 'true',
          ),
        );
      })
      .catch(() => {});
    return () => {
      active = false;
    };
  }, [instanceId]);

  const completar = async () => {
    if (!instanceId) return;
    setWorking(true);
    setError(null);
    setMsg(null);
    try {
      await tramitesClient.completePlateFlow(instanceId, {
        soatPagado,
        impuestoDepartamentalPagado: impuestoPagado,
      });
      setPlateFlowStatus('terminado');
      setMsg('Trámite marcado como Terminado. El OT ya puede aprobar o rechazar.');
      onRefresh?.();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo completar el proceso de placa.');
    } finally {
      setWorking(false);
    }
  };

  if (plateFlowStatus !== 'asignado') return null;

  return (
    <div className="space-y-3 pt-2 border-t">
      <div>
        <h5 className="text-xs font-bold">Procesar trámite (Asignado → Terminado)</h5>
        <p className="text-[11px] opacity-70">
          Marca los checks opcionales si aplican y procesa el trámite. Sin pasar a Terminado el OT
          no puede aprobar ni rechazar.
        </p>
      </div>

      <label className="flex cursor-pointer items-center gap-2 text-xs">
        <input
          type="checkbox"
          className="h-4 w-4 accent-[#557EFF]"
          checked={soatPagado}
          onChange={(e) => setSoatPagado(e.target.checked)}
          disabled={working}
        />
        SOAT pagado
      </label>
      <label className="flex cursor-pointer items-center gap-2 text-xs">
        <input
          type="checkbox"
          className="h-4 w-4 accent-[#557EFF]"
          checked={impuestoPagado}
          onChange={(e) => setImpuestoPagado(e.target.checked)}
          disabled={working}
        />
        Impuesto departamental pagado
      </label>

      <button
        type="button"
        disabled={working}
        onClick={() => void completar()}
        className="rounded-lg bg-[#557eff] px-3.5 py-1.5 text-xs font-semibold text-white disabled:opacity-60"
      >
        {working ? 'Procesando…' : 'Marcar como Terminado'}
      </button>

      {msg ? <p className="m-0 text-xs text-green-700">{msg}</p> : null}
      {error ? (
        <p role="alert" className="m-0 text-xs text-orange-700">
          {error}
        </p>
      ) : null}
    </div>
  );
}

/** Botón de descarga reutilizable (blob → objectURL → anchor). */
function DownloadButton({
  instanceId,
  attachment: d,
}: {
  instanceId: string | null;
  attachment: ProcedureAttachment;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(false);

  const handleDownload = async () => {
    if (!instanceId) return;
    setBusy(true);
    setError(false);
    try {
      const { blob, filename } = await tramitesClient.downloadAttachment(
        instanceId,
        d.id,
        undefined,
        d.filename,
      );
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = filename;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    } catch {
      setError(true);
    } finally {
      setBusy(false);
    }
  };

  return (
    <button
      type="button"
      onClick={() => void handleDownload()}
      disabled={busy || !instanceId}
      className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-[11px] font-semibold border shrink-0 disabled:opacity-50"
      style={{ borderColor: error ? '#FF4E00' : '#5B8A1F', color: error ? '#FF4E00' : '#5B8A1F' }}
      aria-label={`Descargar ${d.filename}`}
    >
      <Download className="h-3 w-3" />
      {busy ? 'Descargando…' : error ? 'Reintentar' : 'Descargar'}
    </button>
  );
}
