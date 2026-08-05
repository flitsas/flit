'use client';

import { useCallback, useEffect, useId, useMemo, useRef, useState, type ReactNode } from 'react';
import {
  Check,
  ChevronDown,
  Copy,
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
import { sourceLabel, checkRoleSuffix } from './PreflightPanel';
import { useWizardReadOnly } from './WizardReadOnlyContext';
import { PRENDA_DECISION_LABELS } from './PrendaForm';
import { prendaDocLabelFor, prendaDocTipoFor } from './prenda-document-tipos';
import { summarizeDeclaredTransformations } from './VehicleTransformationsCard';
import { InlineAlert } from '@/components/atom/InlineAlert';
import type {
  Actor,
  BiometricParte,
  BiometricValidation,
  FieldValue,
  InstanceStatus,
  Participant,
  ParticipantRol,
  PreflightCheck,
  ProcedureActor,
  ProcedureAttachment,
  PrendaData,
  StatusHistory,
  TransitOfficeOption,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

/** Estado de la pre-generación del paquete al entrar al paso FUR (Feature #11066). */
export type PaqueteDocsStatus = 'idle' | 'loading' | 'ready' | 'error';

interface Props {
  instanceId: string | null;
  modalidad: WizardModalidad;
  /** Re-consulta el estado del wizard tras una acción (server-driven). */
  onRefresh?: () => void;
  /** FEATURE 05 — el RNMC aplica al trámite: se consulta por actor y se muestra en el resumen. */
  rnmcEnabled?: boolean;
  /**
   * Feature #11066 — avisa al wizard si el paquete (FUR + impronta) ya se pre-generó al entrar
   * al paso. La generación NO bloquea Preparar ni Guardar; Radicar sí exige consolidado completo.
   */
  onPaqueteStatusChange?: (status: PaqueteDocsStatus) => void;
  /** HU #10646 — partes con firma del baúl (biométrica embebida en resumen). */
  vaultCoveredPartes?: BiometricParte[];
  /** Borrador finalizado: biométrica editable aunque el wizard esté read-only. */
  biometricForceEditable?: boolean;
}

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
 * paso 5 / traspaso paso 6, mock). La biométrica pendiente vive dentro del
 * resumen (Comprador/Vendedor). Todo el gating es server-driven: este paso
 * refleja el estado, refresca el wizard tras cada acción y delega la
 * verificación autoritativa al backend (submit hard-gate). La firma de
 * compraventa solo aplica a traspaso.
 */
export function FirmaFurStep({
  instanceId,
  modalidad,
  onRefresh,
  rnmcEnabled = false,
  onPaqueteStatusChange,
  vaultCoveredPartes = [],
  biometricForceEditable = false,
}: Props) {
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
  /** Contacto completo (teléfono/dirección/ciudad) desde GET actors — el detalle de instancia no lo trae. */
  const [actorsContact, setActorsContact] = useState<ProcedureActor[]>([]);
  // Adjuntos + biométrica del expediente (alimentan MatriculaResumen y ExpedienteVisor).
  const [attachments, setAttachments] = useState<ProcedureAttachment[]>([]);
  const [biometric, setBiometric] = useState<BiometricValidation[]>([]);
  // HU #11014 — partes cubiertas por la firma del baúl: el expediente las rotula como firmadas desde
  // el baúl en vez de hablar del certificado de validación de identidad.
  const [firmaBaulPartes, setFirmaBaulPartes] = useState<string[]>([]);
  // Decisión de prenda/gravamen (si el gestor ya eligió una opción en el paso previo).
  const [prenda, setPrenda] = useState<PrendaData | null>(null);
  // HU #10988 — fecha del trámite en el resumen (estampa FUR y documentos).
  const [fechaTramite, setFechaTramite] = useState(todayIsoDate);
  // FEATURE 05 — resultado RNMC por actor (medidas correctivas). Se consulta al entrar a este paso
  // (cuando ya se capturó la fecha de expedición de cada actor), no en el pre-vuelo.
  const [rnmcChecks, setRnmcChecks] = useState<PreflightCheck[]>([]);
  const [rnmcLoading, setRnmcLoading] = useState(false);
  // Feature #11066 — pre-generación del paquete al entrar al paso (antes de Preparar).
  const [paqueteStatus, setPaqueteStatus] = useState<PaqueteDocsStatus>('idle');
  const paqueteKickoffRef = useRef(false);
  const onPaqueteStatusChangeRef = useRef(onPaqueteStatusChange);
  useEffect(() => {
    onPaqueteStatusChangeRef.current = onPaqueteStatusChange;
  }, [onPaqueteStatusChange]);

  const loadDetail = useCallback(async () => {
    if (!instanceId) return;
    try {
      const [d, p, actors] = await Promise.all([
        tramitesClient.getInstance(instanceId),
        tramitesClient.getPrenda(instanceId).catch(() => null),
        tramitesClient.getActors(instanceId).catch(() => [] as ProcedureActor[]),
      ]);
      setDetail({
        fieldValues: d.fieldValues ?? [],
        actors: d.actors ?? [],
        status: d.status,
        statusHistory: d.statusHistory ?? [],
      });
      setActorsContact(actors);
      setPrenda(p);
      // Siempre la fecha del día (no editable en el resumen).
      const fecha = todayIsoDate();
      setFechaTramite(fecha);
      try {
        await tramitesClient.patchFieldValues(instanceId, [
          { formFieldId: null, fieldKey: 'fur_processing_date', valueText: fecha },
        ]);
      } catch {
        // Best-effort: el resumen ya muestra hoy.
      }
    } catch {
      // El detalle es secundario para el resto del paso; los subbloques
      // muestran sus propios errores. No bloquea el render.
    }
  }, [instanceId]);

  const guardarFechaTramite = useCallback(async () => {
    if (!instanceId) return;
    try {
      await tramitesClient.patchFieldValues(instanceId, [
        { formFieldId: null, fieldKey: 'fur_processing_date', valueText: fechaTramite || null },
      ]);
    } catch {
      // Best-effort fuera de borrador.
    }
  }, [instanceId, fechaTramite]);

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
  const compradorContact = useMemo(
    () => actorsContact.find((a) => a.rol === 'comprador') ?? null,
    [actorsContact],
  );
  const vendedorContact = useMemo(
    () => actorsContact.find((a) => a.rol === 'vendedor') ?? null,
    [actorsContact],
  );

  const toResumenActor = (a: Actor | null, contact: ProcedureActor | null) =>
    a
      ? {
          nombre: a.fullName,
          documento: a.documentNumber,
          tipoDoc: a.documentType,
          email: a.email ?? contact?.email ?? null,
          telefono: contact?.telefono ?? null,
          direccion: contact?.direccion ?? null,
          ciudad: contact?.ciudad ?? null,
        }
      : null;

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
  // HU #11199 (AC4) — marca escrita al crear el trámite con la secretaría del primer paso. Su ausencia
  // (borradores anteriores al cambio) deja el comportamiento de siempre.
  const organismoElegidoEnPasoUno = fv('transit_office_origen') === 'paso_1' && organismoSelected;

  // Auto-abre el modal al entrar al paso si aún no hay organismo seleccionado.
  const [organismoModalOpen, setOrganismoModalOpen] = useState(false);
  const [autoOpened, setAutoOpened] = useState(false);
  useEffect(() => {
    if (!detail || autoOpened) return;
    // Auto-abrir una sola vez al cargar el detalle; el guard `autoOpened` evita el bucle.
    // En solo lectura nunca se abre el selector de organismo. B11 (HU #10659): en traspaso el OT
    // proviene del RUNT (auto-bind en preflight) y no se selecciona/cambia → nunca se auto-abre.
    // HU #11199 (AC4): con la secretaría elegida en el paso 1 tampoco hay nada que preguntar (y de
    // hecho `organismoSelected` ya es true, así que este guard es redundante pero explícito).
    const faltaElegirlo =
      !organismoSelected && !readOnly && modalidad !== 'traspaso' && !organismoElegidoEnPasoUno;
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (faltaElegirlo) setOrganismoModalOpen(true);
    setAutoOpened(true);
  }, [detail, organismoSelected, autoOpened, readOnly, modalidad, organismoElegidoEnPasoUno]);

  const handleOrganismoConfirmed = async () => {
    setOrganismoModalOpen(false);
    await loadDetail();
    onRefresh?.();
  };

  // Tras generar la impronta o el FUR, refresca adjuntos para que el resumen y el
  // visor reflejen el nuevo documento sin remontar el paso.
  /**
   * Bug #11145 — <b>el aviso se decide por el expediente, no solo por la petición.</b>
   *
   * El estado solo pasaba a «listo» cuando respondía la generación del FUR, y esa petición puede
   * tardar bastante. El documento, en cambio, se materializa antes: el gestor veía el FUR en el
   * expediente mientras el aviso seguía girando. Con el FUR ya adjunto no hay nada en curso que
   * anunciar, así que el estado efectivo es «listo» pase lo que pase con la petición.
   */
  const furGenerado = useMemo(() => attachments.some((a) => a.tipo === 'fur'), [attachments]);
  const paqueteStatusEfectivo: PaqueteDocsStatus =
    paqueteStatus === 'loading' && furGenerado ? 'ready' : paqueteStatus;

  /**
   * …y para enterarse de que el documento ya está, hay que volver a mirar. Mientras la generación
   * sigue en vuelo se re-lista el expediente periódicamente: en cuanto aparece el FUR, el aviso se
   * retira y el gestor ve el documento sin esperar a que la petición cierre. El sondeo se detiene
   * solo (deja de cumplirse la condición) y nunca corre fuera de ese hueco.
   */
  useEffect(() => {
    if (paqueteStatus !== 'loading' || !instanceId || furGenerado) return;
    const intervalo = setInterval(() => {
      void loadExpediente();
    }, 3_000);
    return () => clearInterval(intervalo);
  }, [paqueteStatus, instanceId, furGenerado, loadExpediente]);

  // Feature #11066 — reporta al shell el estado del paquete (banner: no bloquea Preparar).
  useEffect(() => {
    onPaqueteStatusChangeRef.current?.(paqueteStatusEfectivo);
  }, [paqueteStatusEfectivo]);

  /**
   * Bug #11145 (endurecimiento) — la guarda de "ya no aplica" es el DESMONTAJE, no un cambio de
   * dependencias.
   *
   * Antes se usaba un `cancelled` por ejecución del efecto. Con el guard de arranque
   * (`paqueteKickoffRef`) eso abre un hueco: si una dependencia cambia mientras la generación está en
   * vuelo, React limpia y marca `cancelled`, la ejecución en curso descarta su resultado, y el efecto
   * que vuelve a entrar sale de inmediato porque el guard ya está activo. Nadie deja el estado en
   * «listo». No es el camino que reportó el negocio —ese era la espera larga, resuelta arriba— pero es
   * la misma avería esperando otra ocasión.
   */
  const montadoRef = useRef(true);
  useEffect(() => {
    montadoRef.current = true;
    return () => {
      montadoRef.current = false;
    };
  }, []);

  /** Estado del trámite como PRIMITIVO: `detail` es un objeto nuevo en cada recarga (ver arriba). */
  const estadoDetalle = detail?.status;

  // Feature #11066 — al entrar al paso FUR (con organismo y en borrador/subsanación) pre-genera
  // el paquete + impronta. Preparar/Guardar NO esperan a que termine; Radicar sí exige consolidado.
  useEffect(() => {
    if (!instanceId || readOnly || !organismoSelected) return;
    // Bug #11145 — depende del ESTADO del trámite, no del objeto `detail`: ese objeto se sustituye
    // en cada recarga del detalle y volvía a disparar la limpieza del efecto en mitad de la
    // generación.
    if (estadoDetalle !== 'borrador' && estadoDetalle !== 'subsanacion') return;
    if (paqueteKickoffRef.current) return;
    paqueteKickoffRef.current = true;

    void (async () => {
      setPaqueteStatus('loading');
      try {
        let atts: ProcedureAttachment[] = [];
        try {
          atts = await tramitesClient.getAttachments(instanceId);
          if (montadoRef.current) setAttachments(atts);
        } catch {
          // Si falla el listado, intentamos generar de todas formas.
        }

        const hasFurDoc = atts.some((a) => a.tipo === 'fur');
        const hasImpronta = atts.some((a) => a.tipo === 'impronta');

        if (!hasFurDoc) {
          await tramitesClient.generarFur(instanceId);
        }

        if (!hasImpronta) {
          try {
            await tramitesClient.generarImpronta(instanceId);
          } catch {
            // Best-effort: sin impronta no bloquea el paso ni Preparar.
          }
        }

        if (!montadoRef.current) return;
        setPaqueteStatus('ready');
        // Solo adjuntos locales — no onRefresh del wizard (evita remount / cambio de estado).
        void loadExpediente();
      } catch {
        if (!montadoRef.current) return;
        paqueteKickoffRef.current = false;
        setPaqueteStatus('error');
      }
    })();
  }, [instanceId, readOnly, organismoSelected, estadoDetalle, loadExpediente]);

  return (
    <div className="space-y-8">
      <MatriculaResumen
        modalidad={modalidad}
        status={detail?.status ?? 'borrador'}
        placa={fv('plate')}
        vehiculo={[fv('vehicle_brand'), fv('vehicle_line'), fv('vehicle_year')]
          .filter(Boolean)
          .join(' ')}
        vin={fv('vin')}
        especificaciones={{
          clase: fv('vehicle_class'),
          servicio: fv('vehicle_service'),
          cilindraje: fv('vehicle_engine_displacement'),
          combustible: fv('vehicle_fuel'),
          carroceria: fv('vehicle_body_type'),
          capacidad: fv('vehicle_passengers'),
          ejes: fv('vehicle_axles'),
          estado: fv('vehicle_state'),
          motor: fv('vehicle_engine_number'),
          chasis: fv('vehicle_chassis'),
          serie: fv('vehicle_series'),
        }}
        vendedor={toResumenActor(vendedor, vendedorContact)}
        comprador={toResumenActor(comprador, compradorContact)}
        archivosCount={attachments.length}
        identidadAprobada={identidadAprobada}
        firmaBaulPartes={firmaBaulPartes}
        soat={{ estado: fv('soat_estado'), vencimiento: fv('soat_vencimiento') }}
        transformaciones={summarizeDeclaredTransformations(detail?.fieldValues ?? [])}
        prenda={
          prenda
            ? (() => {
                const docTipo = prendaDocTipoFor(prenda.decision);
                const docAdj = docTipo
                  ? attachments.find((a) => a.tipo === docTipo)
                  : undefined;
                return {
                  decisionLabel: PRENDA_DECISION_LABELS[prenda.decision],
                  acreedorNombre: prenda.acreedorNombre,
                  acreedorDocumento: prenda.acreedorDocumento,
                  documentoLabel: docTipo ? prendaDocLabelFor(prenda.decision) : null,
                  documento: docAdj
                    ? {
                        id: docAdj.id,
                        tipo: docAdj.tipo,
                        filename: docAdj.filename,
                        mimetype: docAdj.mimetype,
                      }
                    : null,
                };
              })()
            : null
        }
        fechaTramite={fechaTramite}
        instanceId={instanceId}
        compradorBio={
          biometric.find((b) => b.partyRole === 'comprador') ??
          biometric.find((b) => b.partyRole === null) ??
          null
        }
        vendedorBio={biometric.find((b) => b.partyRole === 'vendedor') ?? null}
        onBiometricRefresh={() => {
          void loadDetail();
          void loadExpediente();
          onRefresh?.();
        }}
        vaultCoveredPartes={vaultCoveredPartes}
        biometricForceEditable={biometricForceEditable}
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

      <ExpedienteVisor
        instanceId={instanceId}
        attachments={attachments}
        modalidad={modalidad}
        status={detail?.status ?? 'borrador'}
        onBeforeGenerateConsolidado={guardarFechaTramite}
        onAttachmentsChange={() => {
          void loadExpediente();
          onRefresh?.();
        }}
      />

      {rnmcEnabled && <RnmcSection checks={rnmcChecks} loading={rnmcLoading} />}

      <PlateFlowCompleteSection instanceId={instanceId} onRefresh={onRefresh} />

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
  // Mismo patrón de desplegable que MatriculaResumen (Vehículo, Comprador, …).
  const [open, setOpen] = useState(true);
  const panelId = useId();

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
    <div
      className="overflow-hidden rounded-xl border bg-white dark:bg-[#0B0F14]"
      style={{ borderColor: '#DFE5ED' }}
    >
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        className="flex w-full items-center justify-between gap-2 px-4 py-3 text-left"
        aria-expanded={open}
        aria-controls={panelId}
      >
        <span className="flex items-center gap-2">
          <span
            className="h-4 w-1 rounded-full"
            style={{ background: '#557EFF' }}
            aria-hidden="true"
          />
          <span
            className="text-xs font-bold uppercase tracking-[0.2em]"
            style={{ color: '#557EFF' }}
          >
            Placa preasignada
          </span>
        </span>
        <ChevronDown
          className={`h-4 w-4 shrink-0 transition-transform ${open ? 'rotate-180' : ''}`}
          style={{ color: '#9AA5B1' }}
          aria-hidden
        />
      </button>
      {open ? (
        <div
          id={panelId}
          className="border-t px-4 py-3"
          style={{ borderColor: '#DFE5ED' }}
          role="region"
          aria-label="Placa preasignada"
        >
          {children}
        </div>
      ) : null}
    </div>
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

// ── Organismo de tránsito (modal de selección; no se muestra en el resumen) ─

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

// ── Participantes (portal) ────────────────────────────────────────────
// Feature #11211 — fuera del camino feliz del wizard; se exporta para reactivación futura
// sin eliminar la UI (APIs listParticipantes / invitarParticipante intactas).

export function ParticipantesSection({ instanceId }: { instanceId: string | null }) {
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

/** Feature #11066 — fecha local YYYY-MM-DD para precargar "Fecha del trámite". */
function todayIsoDate(): string {
  const d = new Date();
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
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
  /** Salvedad con la que el trámite avanzó (no bloquea, pero el gestor debe verla). */
  const [warning, setWarning] = useState<string | null>(null);

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
    setWarning(null);
    try {
      const res = await tramitesClient.completePlateFlow(instanceId, {
        soatPagado,
        impuestoDepartamentalPagado: impuestoPagado,
      });
      setPlateFlowStatus('terminado');
      setMsg('Trámite marcado como Terminado. El OT ya puede aprobar o rechazar.');
      // El trámite pudo avanzar con salvedades (p. ej. sin SOAT vigente): hay que decirlo.
      setWarning(res?.warningMessage ?? null);
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
      {warning ? (
        <InlineAlert tone="warning" title="Trámite enviado al OT con advertencia">
          {warning}
        </InlineAlert>
      ) : null}
      {error ? (
        <p role="alert" className="m-0 text-xs text-orange-700">
          {error}
        </p>
      ) : null}
    </div>
  );
}
