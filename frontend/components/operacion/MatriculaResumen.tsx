'use client';

import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Check, Clock, Copy, Download, FileSignature, FileText, Star } from 'lucide-react';
import type {
  BiometricParte,
  BiometricValidation,
  FirmaBaulActorCoberturaDto,
  InstanceStatus,
  ProcedureActor,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';
import { estadoChipStyle, estadoLabel } from '@/lib/tramites/estados';
import { actorsOrderedByOrdinal, validationsForActor, isCoveredByVaultForActor } from '@/lib/tramites/ownership-share';
import { StatusBadge } from '@/components/atom/StatusBadge';
import { IdentityValidationTrackingPanel } from '@/components/atom/IdentityValidationTrackingPanel';
import { formatDateOnly } from '@/lib/format/date-only';
import { tramitesClient } from '@/lib/api/tramites-client';
import { WizardAccordion } from './WizardAccordion';
import { WizardCardHeader, WizardPair } from './wizard-atoms';
import { WIZARD_CARD, WIZARD_INPUT, WIZARD_CTA_GRADIENT } from './wizard-field-styles';
import { BiometricStep } from './BiometricStep';
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
  alto?: string;
  ancho?: string;
  largo?: string;
  llantas?: string;
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
  /**
   * ADR-0051 — partes que el TIPO somete a validación de identidad (`biometricActors`), traducidas a
   * los roles del asistente. El resumen pinta el bloque de firma SOLO de estas.
   *
   * <p>Antes se deducía: el del vendedor se condicionaba a `modalidad === 'traspaso'` y el del
   * comprador no se condicionaba a nada. En `TRASPASO_UNILATERAL` firma únicamente el propietario
   * (art. 5.3.2.2), así que el resumen le pedía al locatario —persistido como `comprador`— una
   * validación que su trámite no exige.</p>
   *
   * <p>Ausente ⇒ las dos partes, que es el criterio previo: ningún otro tipo cambia.</p>
   */
  partesBiometricas?: BiometricParte[];
  status: InstanceStatus;
  placa: string;
  vehiculo: string;
  vin: string;
  especificaciones?: ResumenEspecificaciones;
  vendedor?: ResumenActor | null;
  /**
   * Arrendatario del vehículo, en los tipos que lo declaran (`requiresLessee`: matrícula leasing y
   * cambio de locatario). El resumen solo conocía comprador y vendedor, así que el locatario —parte
   * propia del expediente desde el DDL 88— no aparecía en la pantalla donde el gestor revisa el
   * trámite antes de radicarlo. No firma: quien autoriza el leasing es el propietario.
   */
  locatario?: ResumenActor | null;
  /**
   * Cómo llama el CATÁLOGO a cada parte en este tipo. El rol persistido no siempre se llama como la
   * parte real: en `TRASPASO_UNILATERAL` el locatario del leasing se guarda con el rol `comprador`,
   * y la tarjeta lo anunciaba como «Comprador» en un trámite donde nadie compra. Sin entrada para un
   * rol, se usa su nombre de siempre.
   */
  rotulosPorRol?: Partial<Record<'comprador' | 'vendedor' | 'locatario', string>>;
  comprador: ResumenActor | null;
  /**
   * Múltiple Propietario (ADR-0053) — TODOS los actores del lado (no solo el ordinal=1 que resuelven
   * `vendedor`/`comprador` arriba), para pintar una `ResumenCard` POR COPROPIETARIO en vez de una
   * sola por parte. Extensión aditiva: ausente o con 0-1 elemento, el resumen sigue exactamente el
   * camino de siempre (la tarjeta única de `vendedor`/`comprador`) — regresión cero con el caso
   * mayoritario. Con 2+, sustituye esa tarjeta única por N, ordenadas por `ordinal`.
   */
  vendedorActores?: ProcedureActor[];
  compradorActores?: ProcedureActor[];
  /**
   * Múltiple Propietario (ADR-0053) — historial COMPLETO de validaciones (no solo la resuelta por
   * `vendedorBio`/`compradorBio`, que toma una sola por parte). Necesario para correlacionar la
   * validación de CADA copropietario vía `validationsForActor`. Ausente ⇒ `[]`: las tarjetas por
   * actor no tienen de dónde leer su estado, pero el camino de 1 solo actor no lo necesita (usa
   * `vendedorBio`/`compradorBio`, sin cambios).
   */
  biometric?: BiometricValidation[];
  /** ADR-0053 — cobertura del baúl POR ACTOR (documento del RL + ordinal), para las tarjetas por
   * copropietario. `firmaBaulPartes` (abajo) sigue siendo la fuente para el actor ordinal=1. */
  firmaBaulActores?: FirmaBaulActorCoberturaDto[];
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
  /**
   * Tarjetas sueltas de la última fila que monta `FirmaFurStep` —organismo de tránsito y placa
   * preasignada—, porque los datos son suyos. Van como celdas hermanas de transformaciones y prenda
   * en la misma rejilla de tres columnas, no envueltas: envolverlas las volvería una sola celda.
   */
  extrasSlot?: ReactNode;
}

const BORDER = '#DFE5ED';
const BLUE = '#557EFF';

// `ResumenDisclosure` desapareció: transformaciones y prenda eran las últimas que lo usaban y ahora
// son `ResumenCard`, como el resto de la fila. Un acordeón con chevrón entre tarjetas planas se leía
// como una pieza de otro sitio. El único desplegable que queda en el resumen es la trazabilidad de
// validación, dentro de cada actor.

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
export function ResumenCard({
  title,
  children,
  className = '',
}: {
  title: string;
  children: ReactNode;
  className?: string;
}) {
  return (
    <section aria-label={title} className={`${WIZARD_CARD} h-full ${className}`}>
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
          style={{ background: WIZARD_CTA_GRADIENT }}
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
          {/* Copy Flit 2.0 · S4. El puntaje se retira por decisión de producto: el número no le
              dice nada al gestor y la frase ya afirma que la validación fue exitosa. */}
          <p className="text-xs font-bold" style={{ color: 'var(--flit-success-ink)' }}>
            Identidad verificada.
          </p>
          <p className="text-xs opacity-70">
            La identidad del vendedor fue validada con éxito a través del sistema biométrico.
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
        {/* Copy Flit 2.0 · S4. */}
        <p className="text-xs font-bold" style={{ color: '#59677D' }}>
          Validación de identidad pendiente.
        </p>
        <p className="text-xs opacity-70">
          El vendedor aún no ha completado el proceso de verificación biométrica.
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
  noFirma = false,
  porcentaje,
}: {
  actor: ResumenActor;
  bio?: BiometricValidation | null;
  firmaBaul: boolean;
  certLabel: string;
  instanceId?: string | null;
  certCache: React.RefObject<Map<string, string>>;
  /**
   * Múltiple Propietario (ADR-0053) — porcentaje de propiedad de ESTE copropietario. Ausente/`null`
   * con un solo actor por lado (nunca lo pasa ese camino): la grilla de datos queda igual que
   * siempre, sin celda nueva — regresión cero.
   */
  porcentaje?: number | null;
  /**
   * Esta parte NO firma el trámite: en vez de la sección de validación de identidad, se dice que no
   * le corresponde firmar. Es el locatario del leasing y del traspaso unilateral.
   *
   * <p>No es un relleno: sin esto la tarjeta terminaba en los datos de contacto y quedaba un hueco
   * al lado de la parte que sí firma —las dos igualan altura—, y el gestor no tenía cómo saber si
   * faltaba pedirle la firma o si de verdad no le tocaba.</p>
   */
  noFirma?: boolean;
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
        {porcentaje != null ? (
          <Field label="Porcentaje de propiedad" value={`${porcentaje}%`} />
        ) : null}
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
      {noFirma ? (
        // Misma anatomía que la sección de validación —título en versalitas y su contenido debajo—
        // para que las dos tarjetas de la fila se lean como piezas del mismo tipo. Dice lo que el
        // gestor necesita saber de esta parte: que no le toca firmar, y que aun así se le notifica.
        <div className="space-y-2 border-t pt-3" style={{ borderColor: BORDER }}>
          <p className="text-xs font-semibold uppercase tracking-[0.2em] opacity-70">Firma</p>
          <p className="text-xs font-semibold" style={{ color: '#162744' }}>
            No requiere firma
          </p>
          <p className="text-xs opacity-70">
            Esta parte no firma el trámite: la validación de identidad y la firma corresponden al
            propietario del vehículo.
          </p>
          <p className="text-xs opacity-70">Recibe los avisos de estado del trámite.</p>
        </div>
      ) : !hideValidacion ? (
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
       * Trazabilidad Kyverum solo si el método es validación de identidad. Con firma del baúl no
       * hay bitácora de proveedor que mostrar (la electrónica no usa este historial).
       */}
      {bio && !firmaBaul ? (
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
        style={{ background: WIZARD_CTA_GRADIENT }}
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
  partesBiometricas,
  locatario = null,
  rotulosPorRol,
  status,
  placa,
  vehiculo,
  vin,
  especificaciones = {},
  comprador,
  vendedor,
  vendedorActores = [],
  compradorActores = [],
  biometric = [],
  firmaBaulActores = [],
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
  extrasSlot,
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
  // ADR-0051 — qué partes firman lo declara el TIPO. Ausente ⇒ el criterio previo (vendedor solo en
  // traspaso, comprador siempre), así que ningún tipo ya en operación cambia.
  const firma = (parte: BiometricParte): boolean =>
    partesBiometricas
      ? partesBiometricas.includes(parte)
      : parte === 'comprador' || modalidad === 'traspaso';
  const showBioVendedor =
    firma('vendedor') && !!instanceId && identidadPendiente(vendedorBio, vendedorFirmaBaul);
  const showBioComprador =
    firma('comprador') && !!instanceId && identidadPendiente(compradorBio, compradorFirmaBaul);
  // La parte que NO firma no lleva sección de «Validación de identidad»: ni la captura biométrica ni
  // el banner de estado con su certificado. Sus DATOS sí se muestran —el resumen es el inventario del
  // expediente y el locatario es parte del trámite—; lo que desaparece es la firma que no le toca.
  //
  // Va aparte de `showBio*`: aquel decide si se EMBEBE la captura (y solo la embebe mientras la
  // identidad está pendiente), mientras que este oculta el bloque de validación entero. Colgarlo de
  // `hideValidacion={showBio*}` hacía que, al quitarle la captura a quien no firma, apareciera en su
  // lugar el banner de validación — cambiar una cosa que sobra por otra.
  const ocultaValidacion = (parte: BiometricParte): boolean => !firma(parte);

  /**
   * ¿La pantalla muestra DOS partes? Gobierna el reparto de la rejilla: con dos, el vehículo ocupa
   * su propia fila. Traspaso las tiene por el vendedor; la matrícula leasing, por el locatario.
   */
  const dosPartes = !!vendedor || !!locatario;
  // Múltiple Propietario (ADR-0053) — ¿algún lado trae 2+ copropietarios AHORA MISMO (no memoria
  // histórica: se calcula del array actual)? Con uno, el lado sigue como una sola tarjeta y no
  // cuenta aquí — mismo criterio que ya usa `OwnershipTabsBar`/`hasPercentagePanel` en ActorsForm.
  const vendedorMultiple = vendedorActores.length >= 2;
  const compradorMultiple = compradorActores.length >= 2;
  // Generaliza `dosPartes`: el vehículo ocupa las dos columnas cuando abajo hay 2+ tarjetas, sea
  // porque hay dos partes (como siempre) o porque un lado tiene varios copropietarios (matrícula
  // con 2+). Con una sola tarjeta debajo (el caso mayoritario, `dosPartes` false y ningún lado
  // múltiple) el resultado es idéntico a `dosPartes` — regresión cero.
  const vehiculoAncho = dosPartes || vendedorMultiple || compradorMultiple;

  /** Nombre de la parte en pantalla: el del catálogo si lo hay, si no el de siempre. */
  const rotulo = (rol: 'comprador' | 'vendedor' | 'locatario', porDefecto: string): string =>
    rotulosPorRol?.[rol]?.trim() || porDefecto;

  /**
   * `ordinal` es aditivo (ver `onlyOwnerOrdinal` en `BiometricStep`): ausente, embebe TODOS los
   * actores del rol — el camino de siempre, para la tarjeta única de 1 solo actor. Presente, filtra
   * a ese copropietario concreto — lo que usa `renderCopropietarios` para que cada `ResumenCard` de
   * abajo embeba solo la biométrica de SU actor, no la de los demás del mismo lado.
   */
  const embedBiometric = (parte: BiometricParte, ordinal?: number) => {
    const step = (
      <BiometricStep
        instanceId={instanceId}
        modalidad={modalidad}
        onRefresh={onBiometricRefresh}
        hideIntro
        onlyPartes={[parte]}
        onlyOwnerOrdinal={ordinal}
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

  /**
   * Múltiple Propietario (ADR-0053) — una `ResumenCard` POR COPROPIETARIO del lado, ordenados por
   * `ordinal`, reutilizando EXACTAMENTE la misma presentación que ya tenía la parte con un solo
   * actor (`ActorBlock` + biométrica embebida, ver el bloque `vendedor`/`comprador` de abajo). Solo
   * se invoca cuando el lado tiene 2+ actores — con 1 solo actor el caller sigue el camino de
   * siempre, sin pasar por aquí (regresión cero).
   *
   * La validación de CADA actor se correlaciona por `ordinal` (`validationsForActor`), no por la
   * parte a secas: antes de esta HU, `vendedorBio`/`compradorBio` resolvían la PRIMERA validación
   * de la parte sin importar de cuál copropietario era — con 2+ actores eso mezclaba la biometría
   * de uno con los datos de otro. Se toma la última (más reciente) como "vigente", mismo criterio
   * que usa `BiometricStep` internamente.
   */
  const renderCopropietarios = (
    rol: 'vendedor' | 'comprador',
    actores: ProcedureActor[],
    rotuloBase: string,
  ) => {
    const ordenados = actorsOrderedByOrdinal(actores);
    return ordenados.map(({ item: actor, ordinal }) => {
      const matches = validationsForActor(biometric, actor, ordinal);
      const bio = matches.length > 0 ? matches[matches.length - 1] : null;
      // Mismo criterio que `BiometricStep`: el dato POR LADO (`firmaBaulPartes`/`vaultCoveredPartes`)
      // es impreciso a propósito con 2+ actores, así que solo se admite para el ordinal=1.
      const firmaBaul =
        isCoveredByVaultForActor(firmaBaulActores, rol, ordinal) ||
        (ordinal === 1 && (firmaBaulPartes.includes(rol) || vaultCoveredPartes.includes(rol)));
      const resumenActor: ResumenActor = {
        nombre: actor.nombreCompleto,
        documento: actor.numeroDocumento,
        tipoDoc: actor.tipoDocumento,
        email: actor.email,
        telefono: actor.telefono,
        direccion: actor.direccion,
        ciudad: actor.ciudad,
      };
      const titulo = `${rotuloBase} ${ordinal}`;
      const pendiente = firma(rol) && !!instanceId && identidadPendiente(bio, firmaBaul);
      return (
        <ResumenCard key={`${rol}-${ordinal}`} title={titulo}>
          <div className="space-y-4">
            <ActorBlock
              actor={resumenActor}
              bio={bio}
              firmaBaul={firmaBaul}
              certLabel={`Certificado ID · ${titulo}`}
              instanceId={instanceId}
              certCache={certCache}
              showRepresentante={resumenActor.tipoDoc === 'NIT'}
              porcentaje={actor.porcentaje ?? null}
              hideValidacion={pendiente || ocultaValidacion(rol)}
              noFirma={ocultaValidacion(rol)}
            />
            {pendiente ? embedBiometric(rol, ordinal) : null}
          </div>
        </ResumenCard>
      );
    });
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
    {
      label: 'Alto',
      value: especificaciones.alto
        ? /^\d+$/.test(especificaciones.alto.trim())
          ? `${especificaciones.alto.trim()} mm`
          : especificaciones.alto
        : undefined,
    },
    {
      label: 'Ancho',
      value: especificaciones.ancho
        ? /^\d+$/.test(especificaciones.ancho.trim())
          ? `${especificaciones.ancho.trim()} mm`
          : especificaciones.ancho
        : undefined,
    },
    {
      label: 'Largo',
      value: especificaciones.largo
        ? /^\d+$/.test(especificaciones.largo.trim())
          ? `${especificaciones.largo.trim()} mm`
          : especificaciones.largo
        : undefined,
    },
    { label: 'Llantas', value: especificaciones.llantas },
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

      {/* Vehículo + partes en la MISMA `grid lg:grid-cols-2`, en ese orden. El reparto depende de
          cuántas PARTES hay, no de cuál sea: con dos, el vehículo se lee solo a fila completa y las
          dos partes van una al lado de la otra debajo; con una sola (matrícula), vehículo y parte
          comparten la primera fila, que es lo que cabe sin dejar media rejilla vacía.

          Antes la condición era literalmente `vendedor`, así que solo el traspaso conseguía ese
          reparto. La matrícula leasing tiene también dos partes —propietario y locatario— y caía en
          la rama de una: el vehículo compartía fila con el propietario y el locatario bajaba solo,
          dejando el hueco al lado. Preguntar por el NÚMERO de partes le da a los dos el mismo trato
          y no cambia nada donde solo hay una. */}
      <div className="grid grid-cols-1 gap-3 lg:grid-cols-2 items-stretch">
        <ResumenCard title="Vehículo" className={vehiculoAncho ? 'lg:col-span-2' : ''}>
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

        {vendedorMultiple ? (
          renderCopropietarios('vendedor', vendedorActores, rotulo('vendedor', 'Vendedor'))
        ) : vendedor ? (
          <ResumenCard title={rotulo('vendedor', 'Vendedor')}>
            <div className="space-y-4">
              <ActorBlock
                actor={vendedor}
                bio={vendedorBio}
                firmaBaul={vendedorFirmaBaul}
                certLabel="Certificado ID · Vendedor"
                instanceId={instanceId}
                certCache={certCache}
                showRepresentante={vendedor.tipoDoc === 'NIT'}
                hideValidacion={showBioVendedor || ocultaValidacion('vendedor')}
                noFirma={ocultaValidacion('vendedor')}
              />
              {showBioVendedor ? embedBiometric('vendedor') : null}
            </div>
          </ResumenCard>
        ) : null}

        {/* Con otra parte en pantalla (vendedor en traspaso, locatario en leasing) esta es su pareja
            en la segunda fila, porque el vehículo ya se llevó la primera entera. Sola, acompaña al
            vehículo en la única fila. En ninguno de los dos casos necesita `col-span`. */}
        {compradorMultiple ? (
          renderCopropietarios('comprador', compradorActores, rotulo('comprador', 'Comprador'))
        ) : comprador || (!vendedor && partesTxt) ? (
          <ResumenCard title={rotulo('comprador', 'Comprador')}>
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
                  hideValidacion={showBioComprador || ocultaValidacion('comprador')}
                  noFirma={ocultaValidacion('comprador')}
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

        {/* Locatario: los tipos con leasing lo declaran como parte propia (`requiresLessee`), pero el
            resumen solo sabía de comprador y vendedor, así que el arrendatario del vehículo no salía
            en la pantalla donde el gestor revisa el trámite antes de radicarlo.

            No lleva bloque de validación ni captura biométrica: en el leasing quien firma es el
            propietario, y el DDL 88 llega a abortar el arranque si alguien mete al locatario en
            `biometricActors`. Se muestran sus datos, que es lo que el expediente necesita. */}
        {locatario ? (
          <ResumenCard title={rotulo('locatario', 'Locatario')}>
            <ActorBlock
              actor={locatario}
              bio={null}
              firmaBaul={false}
              certLabel="Certificado ID · Locatario"
              instanceId={instanceId}
              certCache={certCache}
              showRepresentante={locatario.tipoDoc === 'NIT'}
              hideValidacion
              noFirma
            />
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
      {/* Mandatario retirado del resumen (decisión del usuario). El backend lo resuelve solo cuando
          no se elige a mano, que es lo que decía el propio subtítulo de la sección. */}

      {/* Última fila del resumen, en tres columnas: transformaciones, prenda y —vía `extrasSlot`—
          el organismo de tránsito y la placa preasignada, que los monta `FirmaFurStep` porque es
          quien tiene esos datos. Antes iban apiladas a ancho completo y la prenda además usaba un
          acordeón, así que se leía como una pieza distinta de las tarjetas de al lado. */}
      {hasExtras || extrasSlot ? (
        <div className="grid grid-cols-1 gap-3 lg:grid-cols-3">
          {transformaciones.length > 0 ? (
            <ResumenCard title="Transformaciones">
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
            </ResumenCard>
          ) : null}
          {prenda ? (
            <ResumenCard title="Prenda / gravamen">
              <div
                className="grid grid-cols-1 gap-3"
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
            </ResumenCard>
          ) : null}
          {extrasSlot}
        </div>
      ) : null}
    </section>
  );
}
