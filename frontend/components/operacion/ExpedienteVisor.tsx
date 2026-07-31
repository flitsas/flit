'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import { Car, Check, Download, FileText, ShieldCheck, User } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { documentLabel } from '@/lib/tramites/document-labels';
import type {
  Actor,
  BiometricValidation,
  FieldValue,
  ProcedureAttachment,
} from '@/lib/api/types/procedure-runtime';

// Expediente digital tabulado (Vehículo · Comprador · Documentos). Adaptado del
// ExpedienteVisor de Johan a la capa de datos de FLIT. SIN pdfjs ni fotos de
// identidad (mock hasta acuerdo con proveedor): la previsualización se reduce a
// descarga de adjuntos y la identidad se muestra como estado (listBiometric).

interface Props {
  instanceId: string | null;
  fieldValues: FieldValue[];
  comprador: Actor | null;
  /** Parte saliente. Solo en traspaso; en matrícula es `null` (sin pestaña Vendedor). */
  vendedor?: Actor | null;
  vin: string;
  attachments: ProcedureAttachment[];
  biometric: BiometricValidation[];
  /**
   * HU #11014 — partes cuya identidad queda cubierta por la firma del baúl (ADR-0025 §4). Se rotulan
   * como «firmado desde el baúl»: no hay validación biométrica ni certificado que mostrar.
   */
  firmaBaulPartes?: string[];
  orgTransito: { nombre?: string; ciudad?: string; codigo?: string };
}

type MainTab = 'vehiculo' | 'vendedor' | 'comprador' | 'documentos';

const BLUE = '#557EFF';
const BORDER = '#DFE5ED';

function D({ label, value }: { label: string; value?: string | null }) {
  return (
    <div>
      <p className="text-[10px] font-semibold uppercase tracking-[0.2em] opacity-60">{label}</p>
      <p className="text-xs font-medium break-words">{value || '—'}</p>
    </div>
  );
}

/**
 * Una parte es persona jurídica cuando su documento es NIT (HU #10856): valida el representante legal.
 * HU #11014 — mismo criterio que el wizard (`isJuridical` de ActorsForm), que además admite el tipo de
 * persona explícito; aquí el detalle solo expone el documento, así que el NIT manda.
 */
function esPersonaJuridica(actor: Actor | null | undefined): boolean {
  return actor?.documentType === 'NIT';
}

function SectionTitle({ children }: { children: string }) {
  return (
    <div className="flex items-center gap-2 mb-2">
      <span className="w-1 h-4 rounded-full" style={{ background: BLUE }} aria-hidden="true" />
      <h5 className="text-xs font-bold uppercase tracking-[0.2em]">{children}</h5>
    </div>
  );
}

export default function ExpedienteVisor({
  instanceId,
  fieldValues,
  comprador,
  vendedor,
  vin,
  attachments,
  biometric,
  firmaBaulPartes = [],
  orgTransito,
}: Props) {
  const [mainTab, setMainTab] = useState<MainTab>('vehiculo');

  // Caché del certificado de identidad por validación (HU #10861): se descarga una vez por parte y
  // se conserva al cambiar de pestaña; los objectURL se liberan al desmontar el visor.
  const certCache = useRef<Map<string, string>>(new Map());
  useEffect(() => {
    const cache = certCache.current;
    return () => {
      for (const url of cache.values()) URL.revokeObjectURL(url);
      cache.clear();
    };
  }, []);

  const fv = useMemo(
    () => (key: string) => fieldValues.find((f) => f.fieldKey === key)?.valueText ?? '',
    [fieldValues],
  );

  const placa = fv('plate');

  // Validación del comprador: matrícula => parte null; traspaso => parte 'comprador'.
  const compradorBio = useMemo(
    () =>
      biometric.find((b) => b.partyRole === 'comprador') ??
      biometric.find((b) => b.partyRole === null) ??
      null,
    [biometric],
  );
  // Validación del vendedor (solo traspaso): parte 'vendedor'.
  const vendedorBio = useMemo(
    () => biometric.find((b) => b.partyRole === 'vendedor') ?? null,
    [biometric],
  );

  // Pestañas: el Vendedor solo aplica en traspaso (parte saliente). Se inserta
  // antes del Comprador (saliente → entrante), espejo del resumen.
  const tabs = useMemo<{ key: MainTab; label: string; Icon: typeof Car }[]>(
    () => [
      { key: 'vehiculo', label: 'Vehículo', Icon: Car },
      ...(vendedor ? [{ key: 'vendedor' as const, label: 'Vendedor', Icon: User }] : []),
      { key: 'comprador', label: 'Comprador', Icon: User },
      { key: 'documentos', label: 'Documentos', Icon: FileText },
    ],
    [vendedor],
  );

  const furDoc = attachments.find((a) => a.tipo === 'fur') ?? null;

  return (
    <section aria-label="Expediente digital" className="rounded-xl border" style={{ borderColor: BORDER }}>
      {/* Tabs principales */}
      <div className="flex border-b" style={{ borderColor: BORDER }} role="tablist">
        {tabs.map(({ key, label, Icon }) => {
          const active = mainTab === key;
          return (
            <button
              key={key}
              type="button"
              role="tab"
              aria-selected={active}
              onClick={() => setMainTab(key)}
              className="flex-1 flex items-center justify-center gap-1.5 px-3 py-3 text-xs font-semibold border-b-2 transition-colors"
              style={{
                borderColor: active ? BLUE : 'transparent',
                color: active ? BLUE : undefined,
                opacity: active ? 1 : 0.6,
              }}
            >
              <Icon className="h-3.5 w-3.5" aria-hidden="true" />
              {label}
            </button>
          );
        })}
      </div>

      <div className="p-4 space-y-4">
        {mainTab === 'vehiculo' && (
          <>
            {/* Card placa estilo matrícula */}
            <div className="rounded-xl border p-3 flex items-center gap-4" style={{ borderColor: BORDER }}>
              <div
                className="rounded-xl px-4 py-2 text-center shrink-0 border-2"
                style={{ borderColor: '#0B0F14' }}
              >
                <p className="text-[7px] font-semibold tracking-[0.3em] opacity-70">REPÚBLICA DE COLOMBIA</p>
                <p className="text-2xl font-bold font-mono tracking-[0.18em]">{placa || 'NUEVA'}</p>
              </div>
              <div className="min-w-0">
                <p className="text-[10px] font-semibold uppercase tracking-[0.3em] opacity-60">
                  {fv('vehicle_brand') || '—'}
                </p>
                <p className="text-base font-bold truncate">{fv('vehicle_line') || '—'}</p>
                <p className="text-xs opacity-80">
                  Modelo {fv('vehicle_year') || '—'} · {fv('vehicle_color') || '—'}
                </p>
              </div>
            </div>

            <div>
              <SectionTitle>Especificaciones técnicas</SectionTitle>
              <div
                className="grid grid-cols-2 sm:grid-cols-4 gap-3 p-3 rounded-xl border"
                style={{ borderColor: BORDER }}
              >
                <D label="Clase" value={fv('vehicle_class')} />
                <D label="Servicio" value={fv('vehicle_service')} />
                <D
                  label="Cilindraje"
                  value={fv('vehicle_engine_displacement') ? `${fv('vehicle_engine_displacement')} cc` : ''}
                />
                <D label="Combustible" value={fv('vehicle_fuel')} />
                <D label="Carrocería" value={fv('vehicle_body_type')} />
                <D label="Capacidad" value={fv('vehicle_passengers')} />
                <D label="Ejes" value={fv('vehicle_axles')} />
                <D label="Estado" value={fv('vehicle_state')} />
              </div>
            </div>

            <div>
              <SectionTitle>Identificación interna</SectionTitle>
              <div
                className="grid grid-cols-1 sm:grid-cols-2 gap-3 p-3 rounded-xl border"
                style={{ borderColor: BORDER }}
              >
                <D label="VIN" value={vin} />
                <D label="N. Motor" value={fv('vehicle_engine_number')} />
                <D label="N. Chasis" value={fv('vehicle_chassis')} />
                <D label="N. Serie" value={fv('vehicle_series')} />
              </div>
            </div>
          </>
        )}

        {mainTab === 'vendedor' && vendedor && (
          <>
            <div>
              <SectionTitle>Datos del vendedor</SectionTitle>
              <div
                className="grid grid-cols-1 sm:grid-cols-2 gap-3 p-3 rounded-xl border"
                style={{ borderColor: BORDER }}
              >
                <D label="Nombre" value={vendedor?.fullName} />
                <D label="Tipo doc" value={vendedor?.documentType || 'CC'} />
                <D label="Número" value={vendedor?.documentNumber} />
                <D label="Email" value={vendedorBio?.email || vendedor?.email || undefined} />
              </div>
            </div>

            {esPersonaJuridica(vendedor) && <RepresentanteLegalBlock bio={vendedorBio} />}

            <div>
              <SectionTitle>Validación de identidad</SectionTitle>
              <IdentidadBlock
                bio={vendedorBio}
                firmaBaul={firmaBaulPartes.includes('vendedor')}
                instanceId={instanceId}
                certCache={certCache}
              />
            </div>
          </>
        )}

        {mainTab === 'comprador' && (
          <>
            <div>
              <SectionTitle>Datos del comprador</SectionTitle>
              <div
                className="grid grid-cols-1 sm:grid-cols-2 gap-3 p-3 rounded-xl border"
                style={{ borderColor: BORDER }}
              >
                <D label="Nombre" value={comprador?.fullName} />
                <D label="Tipo doc" value={comprador?.documentType || 'CC'} />
                <D label="Número" value={comprador?.documentNumber} />
                <D label="Email" value={compradorBio?.email || comprador?.email || undefined} />
              </div>
            </div>

            {esPersonaJuridica(comprador) && <RepresentanteLegalBlock bio={compradorBio} />}

            <div>
              <SectionTitle>Validación de identidad</SectionTitle>
              <IdentidadBlock
                bio={compradorBio}
                firmaBaul={firmaBaulPartes.includes('comprador')}
                instanceId={instanceId}
                certCache={certCache}
              />
            </div>

            {(orgTransito?.nombre || orgTransito?.codigo) && (
              <div>
                <SectionTitle>Organismo de tránsito</SectionTitle>
                <div
                  className="grid grid-cols-1 sm:grid-cols-3 gap-3 p-3 rounded-xl border"
                  style={{ borderColor: BORDER }}
                >
                  <D label="Nombre" value={orgTransito.nombre} />
                  <D label="Ciudad" value={orgTransito.ciudad} />
                  <D label="Código" value={orgTransito.codigo} />
                </div>
              </div>
            )}
          </>
        )}

        {mainTab === 'documentos' && (
          <DocumentosTab instanceId={instanceId} attachments={attachments} furDoc={furDoc} />
        )}
      </div>

      <p className="px-4 pb-3 text-[10px] opacity-50">Expediente digital · FLIT Operaciones</p>
    </section>
  );
}

/**
 * Datos del representante legal de una persona jurídica (HU #10856): cuando la parte es jurídica (NIT),
 * la validación de identidad la realiza el representante legal, así que se muestran sus datos (tomados de
 * la validación biométrica) junto al certificado de validación (que aparece en el bloque de identidad).
 */
function RepresentanteLegalBlock({ bio }: { bio: BiometricValidation | null }) {
  return (
    <div>
      <SectionTitle>Representante legal</SectionTitle>
      <div
        className="grid grid-cols-1 sm:grid-cols-2 gap-3 p-3 rounded-xl border"
        style={{ borderColor: BORDER }}
      >
        <D label="Nombre" value={bio?.name} />
        <D label="Tipo doc" value={bio?.documentType} />
        <D label="Número" value={bio?.documentNumber} />
        <D label="Email" value={bio?.email} />
      </div>
      <p className="text-[10px] opacity-60 mt-1">
        La validación de identidad y su certificado corresponden al representante legal.
      </p>
    </div>
  );
}

/**
 * Bloque de estado de identidad (HU #10861): además del estado, muestra el certificado de validación
 * de identidad del proveedor en un visor embebido, cargado de forma perezosa al abrir la pestaña. Si la
 * validación no está aprobada, se muestra solo el estado (sin visor).
 */
function IdentidadBlock({
  bio,
  firmaBaul = false,
  instanceId,
  certCache,
}: {
  bio: BiometricValidation | null;
  /** HU #11014 — la identidad de la parte está cubierta por su firma del baúl (ADR-0025 §4). */
  firmaBaul?: boolean;
  instanceId: string | null;
  certCache: React.RefObject<Map<string, string>>;
}) {
  const estado = bio?.status;
  const aprobado = !firmaBaul && estado === 'aprobado';
  const rechazado = !firmaBaul && estado === 'rechazado';
  const pendiente = !firmaBaul && (estado === 'enviado' || estado === 'en_proceso');

  const { label, color } = firmaBaul
    ? { label: 'Firmado desde el baúl de firmas', color: '#5B8A1F' }
    : aprobado
    ? { label: 'Validada', color: '#5B8A1F' }
    : rechazado
      ? { label: 'Rechazada', color: '#FF4E00' }
      : pendiente
        ? { label: 'Pendiente', color: '#F9AC00' }
        : { label: 'Sin validación', color: '#9AA5B1' };

  // HU #11014 — con firma del baúl NO hay validación biométrica ni certificado: se rotula como firmada
  // desde el baúl y no se intenta descargar nada (antes hablaba de un certificado inexistente).
  const validationId = firmaBaul ? null : (bio?.id ?? null);
  const [certUrl, setCertUrl] = useState<string | null>(
    () => (validationId ? (certCache.current.get(validationId) ?? null) : null),
  );
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!aprobado || !validationId || !instanceId) return;

    // Ya en caché (lo tomó el inicializador de useState al montar): no volver a descargar.
    if (certCache.current.has(validationId)) return;

    let cancelled = false;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const { blob } = await tramitesClient.downloadBiometricCertificado(instanceId, validationId);
        if (cancelled) return;
        const objectUrl = URL.createObjectURL(blob);
        certCache.current.set(validationId, objectUrl);
        setCertUrl(objectUrl);
      } catch (e: unknown) {
        if (!cancelled) setError(e instanceof Error ? e.message : 'No se pudo cargar el certificado.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    };
    void load();

    return () => {
      cancelled = true;
    };
  }, [aprobado, validationId, instanceId, certCache]);

  return (
    <div className="p-3 rounded-xl border space-y-3" style={{ borderColor: BORDER }}>
      <div className="flex items-center gap-3">
        <span
          className="h-8 w-8 rounded-full grid place-items-center shrink-0"
          style={{ background: `color-mix(in srgb, ${color} 16%, transparent)`, color }}
          aria-hidden="true"
        >
          {aprobado || firmaBaul ? <Check className="h-4 w-4" /> : <ShieldCheck className="h-4 w-4" />}
        </span>
        <div>
          <p className="text-xs font-semibold" style={{ color }}>
            {label}
          </p>
          {aprobado && bio?.score != null && (
            <p className="text-[10px] opacity-60">Score: {bio.score}/100</p>
          )}
        </div>
      </div>

      {/* HU #11014 — con firma del baúl no hay certificado del proveedor: se explica la fuente de la firma. */}
      {firmaBaul && (
        <p className="text-[11px] opacity-70" data-testid="identidad-firma-baul">
          La identidad de esta parte se acredita con su firma registrada en el baúl de firmas; no
          requiere validación biométrica ni certificado de identidad.
        </p>
      )}

      {/* Certificado de validación de identidad (visor perezoso). Solo cuando la validación está aprobada. */}
      {aprobado ? (
        <div className="rounded-xl border overflow-hidden" style={{ borderColor: BORDER }}>
          {loading && (
            <div
              className="h-[420px] grid place-items-center text-[11px] opacity-60 animate-pulse bg-[#F4F7FC] dark:bg-white/5"
              data-testid="cert-loading"
            >
              Cargando certificado de identidad…
            </div>
          )}
          {!loading && error && (
            <div className="p-3 text-[11px]" style={{ color: '#FF4E00' }} role="alert">
              {error}
            </div>
          )}
          {!loading && !error && certUrl && (
            <iframe
              src={certUrl}
              title="Certificado de validación de identidad"
              className="w-full h-[420px]"
              data-testid="cert-iframe"
            />
          )}
        </div>
      ) : (
        <p className="text-[11px] opacity-60">
          El certificado de identidad estará disponible cuando la validación sea aprobada.
        </p>
      )}
    </div>
  );
}

/** Pestaña Documentos: chips por tipo + descarga (sin previsualización, MVP). */
function DocumentosTab({
  instanceId,
  attachments,
  furDoc,
}: {
  instanceId: string | null;
  attachments: ProcedureAttachment[];
  furDoc: ProcedureAttachment | null;
}) {
  return (
    <div className="space-y-4">
      <div>
        <SectionTitle>Documentos del expediente</SectionTitle>
        {attachments.length > 0 ? (
          <ul className="space-y-2" aria-label="Documentos del expediente (visor)">
            {attachments.map((a) => (
              <DocRow key={a.id} instanceId={instanceId} attachment={a} />
            ))}
          </ul>
        ) : (
          <p className="text-[11px] opacity-60">No se han cargado documentos.</p>
        )}
      </div>

      <div>
        <SectionTitle>FUR</SectionTitle>
        {furDoc ? (
          <ul className="space-y-2" aria-label="FUR del expediente">
            <DocRow instanceId={instanceId} attachment={furDoc} />
          </ul>
        ) : (
          <p className="text-[11px] opacity-60">
            Aún no se ha generado el FUR. Genéralo en la sección «FUR / contrato de compraventa» de
            arriba.
          </p>
        )}
      </div>
    </div>
  );
}

/** Fila de adjunto con descarga (blob → objectURL → anchor). */
function DocRow({
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
    <li className="rounded-xl border p-3 flex items-center gap-3" style={{ borderColor: BORDER }}>
      <FileText className="h-4 w-4 shrink-0" style={{ color: BLUE }} aria-hidden="true" />
      <div className="min-w-0 flex-1">
        <p className="text-xs font-semibold">
          {documentLabel(d.tipo)} <span className="opacity-50 font-normal">· {d.filename}</span>
        </p>
        <p className="text-[10px] opacity-60 truncate" title={d.sha256}>
          SHA-256: {d.sha256}
        </p>
      </div>
      <button
        type="button"
        onClick={() => void handleDownload()}
        disabled={busy || !instanceId}
        className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-[11px] font-semibold border shrink-0 disabled:opacity-50"
        style={{ borderColor: error ? '#FF4E00' : BLUE, color: error ? '#FF4E00' : BLUE }}
        aria-label={`Descargar ${d.filename}`}
      >
        <Download className="h-3 w-3" />
        {busy ? 'Descargando…' : error ? 'Reintentar' : 'Descargar'}
      </button>
    </li>
  );
}
