'use client';

import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Check, Clock, Copy, Download, FileSignature, FileText, Star } from 'lucide-react';
import type {
  BiometricParte,
  BiometricValidation,
  InstanceStatus,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';
import { estadoChipStyle, estadoLabel } from '@/lib/tramites/estados';
import { StatusBadge } from '@/components/atom/StatusBadge';
import { IdentityValidationTrackingPanel } from '@/components/atom/IdentityValidationTrackingPanel';
import { formatDateOnly } from '@/lib/format/date-only';
import { tramitesClient } from '@/lib/api/tramites-client';
import { WizardAccordion } from './WizardAccordion';
import { WizardCardHeader, WizardPair } from './wizard-atoms';
import { WIZARD_CARD, WIZARD_INPUT } from './wizard-field-styles';
import { BiometricStep } from './BiometricStep';
import { MandatarioSection } from './MandatarioSection';
import { openAttachmentInNewTab } from './ExpedienteVisor';
import { WizardReadOnlyProvider } from './WizardReadOnlyContext';

// Feature #11211 — resumen del trámite (summary-first). Especificaciones del vehículo y
// validación de identidad por actor viven aquí; el expediente debajo es solo documentos.

export type ResumenPrenda = {
  /** Etiqueta legible de la decisión (p. ej. "Registrar prenda"). */
  decisionLabel: string;
  acreedorNombre?: string | null;
  acreedorDocumento?: string | null;
  /** Etiqueta del tipo de documento de prenda exigido. */
  documentoLabel?: string | null;
  /** Adjunto de soporte para abrir en pestaña (mismo flujo que Documentos). */
  documento?: {
    id: string;
    tipo: string;
    filename: string;
    mimetype: string;
  } | null;
};

export type ResumenEspecificaciones = {
  clase?: string;
  servicio?: string;
  cilindraje?: string;
  combustible?: string;
  carroceria?: string;
  capacidad?: string;
  ejes?: string;
  estado?: string;
  motor?: string;
  chasis?: string;
  serie?: string;
  /** Casilla 19 del FUR — solo con servicio Público o Especial (matrícula y traspaso). */
  empresaVinculadoraRazonSocial?: string;
  empresaVinculadoraNit?: string;
};

export type ResumenActor = {
  nombre?: string;
  documento?: string;
  tipoDoc?: string;
  email?: string | null;
  telefono?: string | null;
  direccion?: string | null;
  ciudad?: string | null;
};

interface Props {
  modalidad: WizardModalidad;
  status: InstanceStatus;
  placa: string;
  vehiculo: string;
  vin: string;
  especificaciones?: ResumenEspecificaciones;
  vendedor?: ResumenActor | null;
  comprador: ResumenActor | null;
  archivosCount: number;
  identidadAprobada: boolean;
  firmaBaulPartes?: string[];
  soat?: { estado?: string | null; vencimiento?: string | null };
  transformaciones?: string[];
  prenda?: ResumenPrenda | null;
  /** Fecha del trámite (YYYY-MM-DD). Solo lectura en UI; siempre la del día. */
  fechaTramite?: string;
  instanceId?: string | null;
  compradorBio?: BiometricValidation | null;
  vendedorBio?: BiometricValidation | null;
  /** Refresco del wizard tras iniciar/actualizar biométrica embebida. */
  onBiometricRefresh?: () => void;
  /** Partes cubiertas por firma del baúl (no se embebe captura). */
  vaultCoveredPartes?: BiometricParte[];
  /** Borrador finalizado: fuerza biométrica editable aunque el wizard esté read-only. */
  biometricForceEditable?: boolean;
  /**
   * Texto final que se estampará en el FUR (`fur_observations`, ya compuesto por Requisitos). Solo
   * lectura aquí: la captura vive en el paso de Requisitos, no en el resumen.
   */
  observacionesFur?: string | null;
  /** HU #10536 — trámite prioritario, accionable desde la cabecera del resumen. */
  prioritario?: boolean;
  /** Ausente cuando el trámite todavía no existe (sin `instanceId`, nada que marcar). */
  onPrioritarioChange?: (value: boolean) => void;
}

const BORDER = '#DFE5ED';
const BLUE = '#557EFF';

/**
 * Secciones del resumen. Delegan en el acordeón compartido del wizard: antes traían su propio
 * chrome (radio `xl`, título en versalitas con `tracking-[0.2em]`, barrita azul y chevron gris),
 * que no era el de ningún otro panel del asistente.
 */
function ResumenDisclosure({
  title,
  defaultOpen = true,
  children,
}: {
  title: string;
  defaultOpen?: boolean;
  children: ReactNode;
}) {
  return (
    <WizardAccordion
      title={title}
      defaultOpen={defaultOpen}
      icon={<span className="h-4 w-1 shrink-0 rounded-full" style={{ background: BLUE }} aria-hidden="true" />}
      // El resumen (h3, arriba) es la tarjeta que cuelga del paso; estas secciones viven DENTRO de
      // ella, así que su título arranca un nivel por debajo (h4) y no repite el h3 del resumen.
      level="h4"
    >
      {children}
    </WizardAccordion>
  );
}

/**
 * Rótulo/valor de las grillas del resumen. Delega en `WizardPair` (el átomo del kit para grillas de
 * datos consolidados, pensado justamente para esta pantalla): la única diferencia es el fallback a
 * «—» cuando el dato no llegó, para no dejar la celda muda.
 */
function Field({ label, value }: { label: string; value?: string | null }) {
  return <WizardPair label={label} value={value || '—'} />;
}

/**
 * Tarjeta de sección siempre visible (no colapsable). Mismo tratamiento que `ResumenDisclosure`
 * (radio, borde, franja azul junto al título) pero sin plegado: Vehículo y Vendedor/Comprador son
 * lo primero que el gestor repasa antes de radicar —en la propuesta van en `grid lg:grid-cols-2`
 * siempre abiertas—, así que dejarlas detrás de un acordeón les añadía un clic que la referencia no
 * pide. Lo que sigue siendo genuinamente secundario y largo (mandatario, transformaciones, prenda)
 * se queda en `ResumenDisclosure`.
 */
function ResumenCard({
  title,
  children,
  className = '',
}: {
  title: string;
  children: ReactNode;
  className?: string;
}) {
  return (
    <section aria-label={title} className={`${WIZARD_CARD} ${className}`}>
      <div className="mb-3 flex items-center gap-2">
        <span className="h-4 w-1 shrink-0 rounded-full" style={{ background: BLUE }} aria-hidden="true" />
        <WizardCardHeader title={title} level="h4" className="" />
      </div>
      {children}
    </section>
  );
}

function PrendaDocumentoVerButton({
  instanceId,
  documento,
  label,
}: {
  instanceId: string | null | undefined;
  documento: NonNullable<ResumenPrenda['documento']>;
  label: string;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  return (
    <div>
      <p className="text-xs font-semibold uppercase tracking-[0.2em] opacity-70">{label}</p>
      <div className="mt-1.5 flex flex-col gap-1">
        <button
          type="button"
          disabled={!instanceId || busy}
          className="inline-flex w-fit max-w-full items-center gap-1.5 rounded-full px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
          style={{ background: BLUE }}
          aria-label={`Ver ${documento.filename || label}`}
          onClick={() => {
            if (!instanceId) return;
            setBusy(true);
            setError(null);
            void openAttachmentInNewTab(instanceId, documento)
              .catch((e: unknown) => {
                setError(e instanceof Error ? e.message : 'No se pudo abrir el documento.');
              })
              .finally(() => setBusy(false));
          }}
        >
          <Download className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
          {busy ? 'Abriendo…' : 'Ver'}
        </button>
        {error ? (
          <span className="text-xs" style={{ color: '#FF4E00' }} role="alert">
            {error}
          </span>
        ) : null}
      </div>
    </div>
  );
}

/**
 * Estado de identidad resuelto (misma presentación que BiometricStep):
 * baúl del representante / identidad verificada. Se muestra en cada actor del resumen
 * cuando ya no hace falta embeber la captura.
 */
function IdentidadStatusBanner({
  bio,
  firmaBaul,
}: {
  bio: BiometricValidation | null | undefined;
  firmaBaul: boolean;
}) {
  if (firmaBaul) {
    return (
      <div
        className="flex items-center gap-3 rounded-xl p-3"
        style={{ background: 'rgba(85,126,255,0.10)', border: '1px solid rgba(85,126,255,0.35)' }}
        role="status"
        aria-live="polite"
        data-testid="identidad-firma-baul"
      >
        <span
          className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full"
          style={{ background: '#557EFF', color: 'white' }}
          aria-hidden
        >
          <FileSignature className="h-5 w-5" />
        </span>
        <div className="space-y-0.5">
          <p className="text-xs font-bold" style={{ color: '#557EFF' }}>
            Firma electrónica (baúl)
          </p>
          <p className="text-xs opacity-70">
            Identidad cubierta por la firma electrónica del baúl. No requiere validación biométrica.
          </p>
        </div>
      </div>
    );
  }

  if (bio?.status === 'aprobado') {
    return (
      <div
        className="flex items-center gap-3 rounded-xl p-3"
        style={{ background: 'rgba(140,198,63,0.12)', border: '1px solid rgba(140,198,63,0.4)' }}
        role="status"
        aria-live="polite"
        data-testid="identidad-verificada"
      >
        <span
          className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full"
          // Verde de tinta del sistema (mismo token que el texto): no `#5B8A1F`, que no es paleta FLIT.
          style={{ background: 'var(--flit-success-ink)', color: 'white' }}
          aria-hidden
        >
          <Check className="h-5 w-5" />
        </span>
        <div className="space-y-0.5">
          <p className="text-xs font-bold" style={{ color: 'var(--flit-success-ink)' }}>
            {/* Nunca un puntaje inventado: si el proveedor no lo trae, no se muestra número. */}
            {bio.score != null ? `Identidad verificada — ${bio.score}/100` : 'Identidad verificada'}
          </p>
          {bio.name ? <p className="text-xs opacity-70">{bio.name}</p> : null}
        </div>
      </div>
    );
  }

  // Pendiente (sin iniciar, en proceso, rechazada o expirada): antes este estado se quedaba mudo
  // (`return null`) — en la pantalla que dice "revisa todo antes de radicar" un dato ausente no
  // puede leerse como "no hay nada pendiente".
  return (
    <div
      className="flex items-center gap-3 rounded-xl p-3"
      style={{ background: '#EEF5FF', border: '1px solid #DFE5ED' }}
      role="status"
      aria-live="polite"
      data-testid="identidad-pendiente"
    >
      <span
        className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full"
        style={{ background: '#59677D', color: 'white' }}
        aria-hidden
      >
        <Clock className="h-5 w-5" />
      </span>
      <div className="space-y-0.5">
        <p className="text-xs font-bold" style={{ color: '#59677D' }}>
          Validación de identidad pendiente
        </p>
        {bio?.status === 'rechazado' || bio?.status === 'expirado' ? (
          <p className="text-xs opacity-70">
            La última validación no se aprobó; puede reintentarse.
          </p>
        ) : null}
      </div>
    </div>
  );
}

function CertificadoIdButton({
  label,
  bio,
  firmaBaul,
  instanceId,
  certCache,
}: {
  label: string;
  bio: BiometricValidation | null | undefined;
  firmaBaul: boolean;
  instanceId: string | null | undefined;
  certCache: React.RefObject<Map<string, string>>;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const aprobado = !firmaBaul && bio?.status === 'aprobado';
  const validationId = bio?.id ?? null;

  const handleOpen = async () => {
    if (!aprobado || !validationId || !instanceId) return;
    setBusy(true);
    setError(null);
    try {
      let url = certCache.current.get(validationId) ?? null;
      if (!url) {
        const { blob } = await tramitesClient.downloadBiometricCertificado(
          instanceId,
          validationId,
        );
        url = URL.createObjectURL(blob);
        certCache.current.set(validationId, url);
      }
      window.open(url, '_blank', 'noopener,noreferrer');
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'No se pudo abrir el certificado.');
    } finally {
      setBusy(false);
    }
  };

  if (firmaBaul) return null;

  return (
    <div className="flex w-fit max-w-full flex-col gap-1">
      <button
        type="button"
        onClick={() => void handleOpen()}
        disabled={!aprobado || busy || !instanceId}
        className="inline-flex w-fit max-w-full items-center gap-2 rounded-xl border px-3 py-2 text-xs font-semibold disabled:cursor-not-allowed disabled:opacity-50"
        style={{ borderColor: BLUE, color: BLUE }}
        aria-label={label}
        title={
          aprobado
            ? 'Abrir certificado en una pestaña nueva'
            : 'Disponible cuando la validación esté aprobada'
        }
      >
        <FileText className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
        {busy ? 'Abriendo…' : label}
      </button>
      {!aprobado && (
        <span className="text-xs opacity-70">
          El certificado estará disponible cuando la validación sea aprobada.
        </span>
      )}
      {error && (
        <span className="text-xs" style={{ color: '#FF4E00' }} role="alert">
          {error}
        </span>
      )}
    </div>
  );
}

function ActorBlock({
  actor,
  bio,
  firmaBaul,
  certLabel,
  instanceId,
  certCache,
  showRepresentante,
  hideValidacion = false,
}: {
  actor: ResumenActor;
  bio?: BiometricValidation | null;
  firmaBaul: boolean;
  certLabel: string;
  instanceId?: string | null;
  certCache: React.RefObject<Map<string, string>>;
  showRepresentante: boolean;
  /** Cuando la captura biométrica va embebida debajo, no repetir el campo Validación. */
  hideValidacion?: boolean;
}) {
  return (
    <div className="space-y-3">
      {/* Datos del actor: `grid-cols-2 sm:grid-cols-3` (captura Step5, traducido a los tokens FLIT). */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
        <Field label="Nombre" value={actor.nombre} />
        <Field
          label={actor.tipoDoc === 'NIT' ? 'NIT' : 'Cédula'}
          value={actor.documento ? `${actor.tipoDoc || 'CC'} ${actor.documento}` : null}
        />
        <Field label="Email" value={bio?.email || actor.email || undefined} />
        <Field label="Teléfono" value={actor.telefono} />
        <Field label="Dirección" value={actor.direccion} />
        <Field label="Ciudad" value={actor.ciudad} />
      </div>
      {showRepresentante && bio && (
        <div>
          <p className="mb-2 text-xs font-semibold uppercase tracking-[0.2em] opacity-70">
            Representante legal
          </p>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <Field label="Nombre" value={bio.name} />
            <Field label="Tipo doc" value={bio.documentType} />
            <Field label="Número" value={bio.documentNumber} />
            <Field label="Email" value={bio.email} />
          </div>
        </div>
      )}
      {!hideValidacion ? (
        <div className="space-y-3 border-t pt-3" style={{ borderColor: BORDER }}>
          <p className="text-xs font-semibold uppercase tracking-[0.2em] opacity-70">
            Validación de identidad
          </p>
          <IdentidadStatusBanner bio={bio} firmaBaul={firmaBaul} />
          <CertificadoIdButton
            label={certLabel}
            bio={bio}
            firmaBaul={firmaBaul}
            instanceId={instanceId}
            certCache={certCache}
          />
        </div>
      ) : null}
      {/*
       * Trazabilidad + enlace de captura (antes la tarjeta compartida «Estado de validación de
       * identidad» — se eliminó por duplicar lo que ya vive aquí, en la sección del propio actor).
       * Se conserva siempre que hay una validación que auditar: el banner de arriba (o, cuando
       * `hideValidacion`, la biométrica embebida) ya dice si está pendiente o verificada, así que el
       * badge solo se repite cuando NINGUNA de las dos lo hizo.
       */}
      {bio ? (
        <IdentityTrackingBlock bio={bio} actorNombre={actor.nombre} showBadge={hideValidacion} />
      ) : null}
    </div>
  );
}

/** ¿Mostrar la captura biométrica tal cual dentro de Comprador/Vendedor? */
function identidadPendiente(
  bio: BiometricValidation | null | undefined,
  firmaBaul: boolean,
): boolean {
  if (firmaBaul) return false;
  return bio?.status !== 'aprobado';
}

/**
 * Enlace de captura, copiable, en monoespaciada — mismo patrón que `CopyLink` (participantes,
 * `FirmaFurStep`), embebido aquí en el bloque de identidad de cada actor pendiente para no crear una
 * dependencia circular entre los dos módulos del paso.
 */
function CapturaLinkCopy({ link, label }: { link: string; label: string }) {
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
        className={`${WIZARD_INPUT} font-mono`}
        style={{ borderColor: BORDER }}
      />
      <button
        type="button"
        onClick={() => void handleCopy()}
        className="flex shrink-0 items-center gap-1.5 rounded-xl px-3 py-2 text-xs font-semibold text-white"
        style={{ background: BLUE }}
        aria-label="Copiar enlace"
      >
        {copied ? <Check className="h-3 w-3" /> : <Copy className="h-3 w-3" />}
        {copied ? 'Copiado' : 'Copiar'}
      </button>
    </div>
  );
}

/**
 * Trazabilidad técnica + enlace de captura de UN actor (antes vivían en la tarjeta compartida
 * «Estado de validación de identidad», retirada por duplicar la validación que ya vive dentro de la
 * sección de cada actor — la propuesta solo contempla un actor y no la embebe aparte). Se monta
 * DENTRO de `ActorBlock`, así que nada de esto desaparece: solo cambia de contenedor.
 *
 * `showBadge` pinta el badge (`Pendiente`/`Verificada`, propuesta Step5) solo cuando la sección del
 * actor todavía no tiene un indicador de estado propio — es lo que pasa cuando la biométrica va
 * embebida (`hideValidacion`): esa vista no repite el badge de `BiometricStep`. Cuando el banner de
 * arriba ya lo dice (`!hideValidacion`), el badge se omite para no duplicarlo.
 *
 * El enlace de captura (monoespaciado, con «Copiar enlace») solo se pinta si la validación sigue
 * pendiente y el proveedor mandó `captureUrl` — aunque la biométrica embebida ya ofrezca su propio
 * QR, este enlace copiable es el mismo patrón de la captura Step5 y no se sustituye por el QR.
 *
 * La bitácora técnica por evento (antes su propia tarjeta con la tabla siempre abierta) se conserva
 * plegada: un desplegable cerrado por defecto, no una fila propia.
 */
function IdentityTrackingBlock({
  bio,
  actorNombre,
  showBadge,
}: {
  bio: BiometricValidation;
  actorNombre?: string;
  showBadge: boolean;
}) {
  const pendiente = bio.status !== 'aprobado';
  const nombre = actorNombre || bio.name;
  return (
    <div className="space-y-3 border-t pt-3" style={{ borderColor: BORDER }}>
      {showBadge ? (
        <StatusBadge label={pendiente ? 'Pendiente' : 'Verificada'} tone={pendiente ? 'warning' : 'success'} />
      ) : null}
      {pendiente && bio.captureUrl ? (
        <div>
          <p className="mb-1 text-xs font-semibold uppercase tracking-[0.2em] opacity-70">
            Enlace de captura
          </p>
          <CapturaLinkCopy link={bio.captureUrl} label={`Enlace de captura de ${nombre}`} />
        </div>
      ) : null}
      <WizardAccordion title="Ver trazabilidad de validación" level="h4" defaultOpen={false}>
        <IdentityValidationTrackingPanel validationId={bio.id} embebido />
      </WizardAccordion>
    </div>
  );
}

export default function MatriculaResumen({
  modalidad,
  status,
  placa,
  vehiculo,
  vin,
  especificaciones = {},
  comprador,
  vendedor,
  soat,
  transformaciones = [],
  prenda = null,
  fechaTramite,
  firmaBaulPartes = [],
  instanceId = null,
  compradorBio = null,
  vendedorBio = null,
  onBiometricRefresh,
  vaultCoveredPartes = [],
  biometricForceEditable = false,
  observacionesFur = null,
  prioritario,
  onPrioritarioChange,
}: Props) {
  const tone = estadoChipStyle(status).color;
  const soatEstado = (soat?.estado ?? '').toLowerCase();
  const soatLabel =
    soatEstado === 'vigente'
      ? 'Vigente'
      : soatEstado === 'vencido'
        ? 'Vencido'
        : soatEstado === 'unknown'
          ? 'No reportado'
          : '—';
  // Mismos tokens semánticos que StatusBadge (`--badge-success-fg` / `--badge-danger-fg` de
  // globals.css), no hex sueltos: quedan theme-aware en vez de fijos para modo oscuro.
  const soatColor =
    soatEstado === 'vigente'
      ? 'var(--badge-success-fg)'
      : soatEstado === 'vencido'
        ? 'var(--badge-danger-fg)'
        : undefined;
  // HU rediseño (captura Step5) — el rótulo pasa a «Consolidado del trámite». El nombre del PASO en
  // la navegación del asistente (Stepper) sigue siendo «Resumen»: es un dato distinto (wizard-copy.ts
  // / TramitesTable.tsx), fuera del alcance de este componente.
  const resumenTitulo = 'Consolidado del trámite';
  const partesTxt = [vendedor?.nombre, comprador?.nombre].filter(Boolean).join(' · ');
  const hasExtras = transformaciones.length > 0 || !!prenda;
  const showFecha = typeof fechaTramite === 'string' && fechaTramite.length > 0;
  const soatLine = `SOAT: ${soatLabel}${
    soatEstado === 'vigente' && soat?.vencimiento
      ? ` · vence ${formatDateOnly(soat.vencimiento)}`
      : ''
  }`;

  const certCache = useRef<Map<string, string>>(new Map());
  useEffect(() => {
    const cache = certCache.current;
    return () => {
      for (const url of cache.values()) URL.revokeObjectURL(url);
      cache.clear();
    };
  }, []);

  const vendedorFirmaBaul =
    firmaBaulPartes.includes('vendedor') || vaultCoveredPartes.includes('vendedor');
  const compradorFirmaBaul =
    firmaBaulPartes.includes('comprador') || vaultCoveredPartes.includes('comprador');
  const showBioVendedor =
    modalidad === 'traspaso' && !!instanceId && identidadPendiente(vendedorBio, vendedorFirmaBaul);
  const showBioComprador = !!instanceId && identidadPendiente(compradorBio, compradorFirmaBaul);

  const embedBiometric = (parte: BiometricParte) => {
    const step = (
      <BiometricStep
        instanceId={instanceId}
        modalidad={modalidad}
        onRefresh={onBiometricRefresh}
        hideIntro
        onlyPartes={[parte]}
        vaultCoveredPartes={vaultCoveredPartes}
        embedded
      />
    );
    return biometricForceEditable ? (
      <WizardReadOnlyProvider readOnly={false}>{step}</WizardReadOnlyProvider>
    ) : (
      step
    );
  };

  // Casilla 19 del FUR: empresa vinculadora + NIT, solo con servicio Público o Especial. El código
  // del catálogo llega en mayúsculas (`PUBLICO`/`ESPECIAL`); se normaliza por si acaso.
  const servicioNormalizado = (especificaciones.servicio ?? '').trim().toUpperCase();
  const requiereEmpresaVinculadora =
    servicioNormalizado === 'PUBLICO' || servicioNormalizado === 'ESPECIAL';

  const specs = [
    { label: 'Clase', value: especificaciones.clase },
    { label: 'Servicio', value: especificaciones.servicio },
    ...(requiereEmpresaVinculadora
      ? [
          { label: 'Empresa vinculadora', value: especificaciones.empresaVinculadoraRazonSocial },
          { label: 'NIT empresa vinculadora', value: especificaciones.empresaVinculadoraNit },
        ]
      : []),
    {
      label: 'Cilindraje',
      value: especificaciones.cilindraje
        ? especificaciones.cilindraje.includes('cc')
          ? especificaciones.cilindraje
          : `${especificaciones.cilindraje} cc`
        : undefined,
    },
    { label: 'Combustible', value: especificaciones.combustible },
    { label: 'Carrocería', value: especificaciones.carroceria },
    { label: 'Capacidad', value: especificaciones.capacidad },
    { label: 'Ejes', value: especificaciones.ejes },
    { label: 'Estado', value: especificaciones.estado },
    { label: 'N. Motor', value: especificaciones.motor },
    { label: 'N. Chasis', value: especificaciones.chasis },
    { label: 'N. Serie', value: especificaciones.serie },
  ].filter((s) => !!s.value);

  return (
    <section aria-label={resumenTitulo} className="space-y-3">
      <div className="flex items-center gap-2 px-0.5">
        <span className="h-5 w-1.5 shrink-0 rounded-full" style={{ background: tone }} aria-hidden="true" />
        <div className="min-w-0 flex-1">
          <WizardCardHeader
            title={resumenTitulo}
            subtitle="Revisa toda la información antes de enviar el expediente al Organismo de Tránsito."
            className=""
            action={
              <div className="flex flex-wrap items-center justify-end gap-3">
                {/* HU #10536 — el líder de diseño lo pidió explícitamente en la cabecera: esta es la
                    pantalla que dice "revisa todo antes de enviar" y hasta ahora no había forma de
                    saber si el trámite es prioritario en el punto donde se radica. Paleta de aviso
                    (`--badge-warning-*`), no los hex de la propuesta (`#FDE68A`/`#FFFBEB`/`#B45309`,
                    fuera de la paleta FLIT). */}
                {onPrioritarioChange ? (
                  <button
                    type="button"
                    onClick={() => onPrioritarioChange(!prioritario)}
                    aria-pressed={!!prioritario}
                    className="flex h-9 shrink-0 items-center gap-1.5 rounded-xl border px-3 text-xs font-semibold transition"
                    style={
                      prioritario
                        ? {
                            borderColor: 'var(--badge-warning-border)',
                            background: 'var(--badge-warning-bg)',
                            color: 'var(--badge-warning-fg)',
                          }
                        : { borderColor: BORDER, color: '#59677D' }
                    }
                  >
                    <Star className="h-3.5 w-3.5" fill={prioritario ? 'currentColor' : 'none'} aria-hidden="true" />
                    Trámite Prioritario
                  </button>
                ) : null}
                {showFecha ? (
                  <div className="text-right">
                    <label
                      htmlFor="fur-fecha-tramite"
                      className="mb-0.5 block text-xs font-semibold uppercase tracking-[0.2em] opacity-70"
                    >
                      Fecha del trámite
                    </label>
                    <input
                      id="fur-fecha-tramite"
                      type="date"
                      value={fechaTramite}
                      readOnly
                      disabled
                      aria-readonly="true"
                      className="cursor-default rounded-lg border bg-transparent px-2.5 py-1 text-xs font-medium opacity-100 disabled:opacity-100"
                      style={{ borderColor: BORDER, color: '#162744' }}
                    />
                  </div>
                ) : null}
                <StatusBadge
                  label={estadoLabel(status)}
                  bg={estadoChipStyle(status).bg}
                  color={estadoChipStyle(status).color}
                  border={estadoChipStyle(status).border}
                />
              </div>
            }
          />
        </div>
      </div>

      {/* Abre el consolidado a ancho completo, como en la propuesta: qué trámite se está radicando.
          Mismo dato (`fur_observations`) que se estampará en el FUR — de ahí el azul. Solo lectura: la
          captura de observaciones se queda en Requisitos. */}
      {observacionesFur ? (
        <div className={WIZARD_CARD}>
          <p className="mb-1 text-xs font-semibold uppercase tracking-[0.2em] opacity-70">
            Trámites solicitados
          </p>
          <p className="text-xs font-medium leading-snug" style={{ color: BLUE }}>
            {observacionesFur}
          </p>
        </div>
      ) : null}

      {/* Vehículo + Vendedor + Comprador en la MISMA `grid lg:grid-cols-2` (captura Step5), en ese
          orden. Antes Vehículo iba a ancho completo y Vendedor/Comprador vivían en una segunda
          rejilla aparte — no era la composición de la captura: en matrícula (sin vendedor) Vehículo
          ocupa la primera celda y Comprador la segunda, sin `col-span`; en traspaso Vendedor
          acompaña a Vehículo en la primera fila y Comprador cae debajo, en el flujo natural de la
          rejilla — «misma rejilla, mismo orden, mismos sitios» en las dos modalidades. */}
      <div className="grid grid-cols-1 gap-3 lg:grid-cols-2">
        <ResumenCard title="Vehículo">
          {placa ? (
            <div className="mb-3 flex flex-wrap items-center gap-3">
              <span className="font-mono text-2xl font-bold tracking-widest" style={{ color: tone }}>
                {placa}
              </span>
              <span
                className="text-xs opacity-70"
                style={soatColor ? { color: soatColor, opacity: 1 } : undefined}
              >
                {soatLine}
              </span>
            </div>
          ) : null}
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            {vehiculo ? <Field label="Modelo / clase" value={vehiculo} /> : null}
            {vin ? <Field label="VIN" value={vin} /> : null}
          </div>
          {specs.length > 0 ? (
            <div className="mt-4">
              <p className="mb-2 text-xs font-semibold uppercase tracking-[0.2em] opacity-70">
                Especificaciones técnicas
              </p>
              {/* Especificaciones del vehículo: `grid-cols-2 sm:grid-cols-4` (captura Step5). */}
              <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
                {specs.map((s) => (
                  <Field key={s.label} label={s.label} value={s.value} />
                ))}
              </div>
            </div>
          ) : null}
          {!placa && !vehiculo && !vin && specs.length === 0 ? (
            <p className="text-xs opacity-70">Sin datos de vehículo.</p>
          ) : null}
        </ResumenCard>

        {vendedor ? (
          <ResumenCard title="Vendedor">
            <div className="space-y-4">
              <ActorBlock
                actor={vendedor}
                bio={vendedorBio}
                firmaBaul={vendedorFirmaBaul}
                certLabel="Certificado ID · Vendedor"
                instanceId={instanceId}
                certCache={certCache}
                showRepresentante={vendedor.tipoDoc === 'NIT'}
                hideValidacion={showBioVendedor}
              />
              {showBioVendedor ? embedBiometric('vendedor') : null}
            </div>
          </ResumenCard>
        ) : null}

        {comprador || (!vendedor && partesTxt) ? (
          <ResumenCard title="Comprador">
            {comprador ? (
              <div className="space-y-4">
                <ActorBlock
                  actor={comprador}
                  bio={compradorBio}
                  firmaBaul={compradorFirmaBaul}
                  certLabel="Certificado ID · Comprador"
                  instanceId={instanceId}
                  certCache={certCache}
                  showRepresentante={comprador.tipoDoc === 'NIT'}
                  hideValidacion={showBioComprador}
                />
                {showBioComprador ? embedBiometric('comprador') : null}
              </div>
            ) : (
              <p className="text-xs opacity-70">
                {modalidad === 'traspaso' ? 'Partes: ' : 'Comprador: '}
                {partesTxt}
              </p>
            )}
          </ResumenCard>
        ) : null}
      </div>

      {/* Contenido nuestro sin equivalente en la captura: mandatario, transformaciones y prenda.
          Plegados por defecto (antes Transformaciones/Prenda abrían solos). El estado de validación
          de identidad de cada actor vive dentro de su propia sección (Vendedor/Comprador, arriba) —
          en FLIT no hay una tarjeta compartida aparte, a diferencia de la propuesta (un solo actor).
          El organismo de tránsito + preasignación de placa lo pinta `FirmaFurStep` en su propia fila,
          debajo de este componente. El expediente consolidado (documentos + confirmaciones) vive en
          `ExpedienteVisor`, un componente distinto que el paso monta a continuación de este. */}
      {instanceId ? (
        <MandatarioSection
          instanceId={instanceId}
          onChanged={onBiometricRefresh}
          asDisclosure
          defaultOpen={false}
        />
      ) : null}

      {hasExtras ? (
        <>
          {transformaciones.length > 0 ? (
            <ResumenDisclosure title="Transformaciones" defaultOpen={false}>
              <div
                className="grid grid-cols-1 gap-3 sm:grid-cols-2"
                aria-label="Transformaciones declaradas"
              >
                {transformaciones.map((t) => {
                  const sep = t.indexOf(':');
                  const label = sep >= 0 ? t.slice(0, sep).trim() : 'Cambio';
                  const value = sep >= 0 ? t.slice(sep + 1).trim() : t;
                  return (
                    <div
                      key={t}
                      className="rounded-xl border px-3 py-2.5"
                      style={{ borderColor: BORDER, background: 'rgba(85,126,255,0.04)' }}
                    >
                      <p
                        className="text-xs font-semibold uppercase tracking-[0.2em]"
                        style={{ color: BLUE }}
                      >
                        {label}
                      </p>
                      <p className="mt-1 text-sm font-semibold tracking-wide" style={{ color: '#162744' }}>
                        {value || '—'}
                      </p>
                    </div>
                  );
                })}
              </div>
            </ResumenDisclosure>
          ) : null}
          {prenda ? (
            <ResumenDisclosure title="Prenda / gravamen" defaultOpen={false}>
              <div
                className="grid grid-cols-1 gap-3 sm:grid-cols-2"
                aria-label="Prenda o gravamen"
              >
                <Field label="Decisión" value={prenda.decisionLabel} />
                {prenda.acreedorNombre || prenda.acreedorDocumento ? (
                  <>
                    <Field label="Acreedor (beneficiario)" value={prenda.acreedorNombre} />
                    <Field label="NIT / documento del acreedor" value={prenda.acreedorDocumento} />
                  </>
                ) : null}
                {prenda.documentoLabel ? (
                  prenda.documento && instanceId ? (
                    <PrendaDocumentoVerButton
                      instanceId={instanceId}
                      documento={prenda.documento}
                      label={prenda.documentoLabel}
                    />
                  ) : (
                    <Field label={prenda.documentoLabel} value="Sin documento cargado" />
                  )
                ) : null}
              </div>
            </ResumenDisclosure>
          ) : null}
        </>
      ) : null}
    </section>
  );
}
