'use client';

import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { Check, ChevronDown, Download, FileSignature, FileText } from 'lucide-react';
import type {
  BiometricParte,
  BiometricValidation,
  InstanceStatus,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';
import { estadoChipStyle, estadoLabel } from '@/lib/tramites/estados';
import { formatDateOnly } from '@/lib/format/date-only';
import { tramitesClient } from '@/lib/api/tramites-client';
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
}

const BORDER = '#DFE5ED';
const BLUE = '#557EFF';

function ResumenDisclosure({
  title,
  defaultOpen = true,
  children,
}: {
  title: string;
  defaultOpen?: boolean;
  children: ReactNode;
}) {
  const [open, setOpen] = useState(defaultOpen);
  const panelId = useId();

  return (
    <div
      className="overflow-hidden rounded-xl border bg-white dark:bg-[#0B0F14]"
      style={{ borderColor: BORDER }}
    >
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        className="flex w-full items-center justify-between gap-2 px-4 py-3 text-left"
        aria-expanded={open}
        aria-controls={panelId}
      >
        <span className="flex items-center gap-2">
          <span className="h-4 w-1 rounded-full" style={{ background: BLUE }} aria-hidden="true" />
          <span className="text-xs font-bold uppercase tracking-[0.2em]" style={{ color: BLUE }}>
            {title}
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
          style={{ borderColor: BORDER }}
          role="region"
          aria-label={title}
        >
          {children}
        </div>
      ) : null}
    </div>
  );
}

function Field({ label, value }: { label: string; value?: string | null }) {
  return (
    <div>
      <p className="text-[10px] font-semibold uppercase tracking-[0.2em] opacity-60">{label}</p>
      <p className="break-words text-xs font-medium" style={{ color: '#162744' }}>
        {value || '—'}
      </p>
    </div>
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
      <p className="text-[10px] font-semibold uppercase tracking-[0.2em] opacity-60">{label}</p>
      <div className="mt-1.5 flex flex-col gap-1">
        <button
          type="button"
          disabled={!instanceId || busy}
          className="inline-flex w-fit max-w-full items-center gap-1.5 rounded-full px-4 py-2 text-[11px] font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
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
          <span className="text-[10px]" style={{ color: '#FF4E00' }} role="alert">
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
          <p className="text-[11px] opacity-70">
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
          style={{ background: '#5B8A1F', color: 'white' }}
          aria-hidden
        >
          <Check className="h-5 w-5" />
        </span>
        <div className="space-y-0.5">
          <p className="text-xs font-bold" style={{ color: '#5B8A1F' }}>
            Identidad verificada — {bio.score ?? 95}/100
          </p>
          {bio.name ? <p className="text-[11px] opacity-70">{bio.name}</p> : null}
        </div>
      </div>
    );
  }

  return null;
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
        className="inline-flex w-fit max-w-full items-center gap-2 rounded-xl border px-3 py-2 text-[11px] font-semibold disabled:cursor-not-allowed disabled:opacity-50"
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
        <span className="text-[10px] opacity-60">
          El certificado estará disponible cuando la validación sea aprobada.
        </span>
      )}
      {error && (
        <span className="text-[10px]" style={{ color: '#FF4E00' }} role="alert">
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
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
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
          <p className="mb-2 text-[10px] font-semibold uppercase tracking-[0.2em] opacity-50">
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
          <p className="text-[10px] font-semibold uppercase tracking-[0.2em] opacity-60">
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
  const soatColor =
    soatEstado === 'vigente' ? '#15803d' : soatEstado === 'vencido' ? '#c2410c' : undefined;
  const resumenTitulo = 'Resumen del trámite';
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
      />
    );
    return biometricForceEditable ? (
      <WizardReadOnlyProvider readOnly={false}>{step}</WizardReadOnlyProvider>
    ) : (
      step
    );
  };

  const specs = [
    { label: 'Clase', value: especificaciones.clase },
    { label: 'Servicio', value: especificaciones.servicio },
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
      <div className="flex items-center justify-between gap-3 px-0.5">
        <div className="flex items-center gap-2">
          <span className="h-5 w-1.5 rounded-full" style={{ background: tone }} aria-hidden="true" />
          <h4 className="text-xs font-bold uppercase tracking-[0.18em]" style={{ color: BLUE }}>
            {resumenTitulo}
          </h4>
        </div>
        <div className="flex flex-wrap items-center justify-end gap-3">
          {showFecha ? (
            <div className="text-right">
              <label
                htmlFor="fur-fecha-tramite"
                className="mb-0.5 block text-[10px] font-semibold uppercase tracking-[0.2em] opacity-60"
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
          <span
            className="rounded-full px-3 py-1 text-[11px] font-semibold"
            style={{ background: `color-mix(in srgb, ${tone} 14%, transparent)`, color: tone }}
          >
            {estadoLabel(status)}
          </span>
        </div>
      </div>

      <ResumenDisclosure title="Vehículo">
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
            <p className="mb-2 text-[10px] font-semibold uppercase tracking-[0.2em] opacity-50">
              Especificaciones técnicas
            </p>
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
              {specs.map((s) => (
                <Field key={s.label} label={s.label} value={s.value} />
              ))}
            </div>
          </div>
        ) : null}
        {!placa && !vehiculo && !vin && specs.length === 0 ? (
          <p className="text-xs opacity-60">Sin datos de vehículo.</p>
        ) : null}
      </ResumenDisclosure>

      {vendedor ? (
        <ResumenDisclosure title="Vendedor">
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
        </ResumenDisclosure>
      ) : null}

      {comprador || (!vendedor && partesTxt) ? (
        <ResumenDisclosure title="Comprador">
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
        </ResumenDisclosure>
      ) : null}

      {instanceId ? (
        <MandatarioSection
          instanceId={instanceId}
          onChanged={onBiometricRefresh}
          asDisclosure
        />
      ) : null}

      {hasExtras ? (
        <>
          {transformaciones.length > 0 ? (
            <ResumenDisclosure title="Transformaciones">
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
                        className="text-[10px] font-semibold uppercase tracking-[0.2em]"
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
            <ResumenDisclosure title="Prenda / gravamen">
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
