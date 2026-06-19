'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Building2,
  Check,
  Copy,
  Download,
  FileText,
  RefreshCw,
  Search,
  Send,
  X,
} from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import {
  filterOrganismos,
  findOrganismoByName,
  type OrganismoTransito,
} from '@/lib/catalogs/organismos-transito';
import type {
  Actor,
  FieldValue,
  FurDocument,
  Participant,
  ParticipantRol,
  ProcedureAttachment,
  Signature,
  SignatureParte,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

interface Props {
  instanceId: string | null;
  modalidad: WizardModalidad;
  /** Re-consulta el estado del wizard tras una acción (server-driven). */
  onRefresh?: () => void;
  /** Notifica al shell del wizard que el trámite fue enviado a tránsito. */
  onSubmitted?: () => void;
}

const PARTE_LABEL: Record<SignatureParte, string> = {
  comprador: 'Comprador',
  vendedor: 'Vendedor',
};

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
        style={{ borderColor: '#DFE5ED' }}
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
export function FirmaFurStep({ instanceId, modalidad, onRefresh, onSubmitted }: Props) {
  // Detalle de la instancia (field_values + actors) para organismo/resumen.
  const [detail, setDetail] = useState<{
    fieldValues: FieldValue[];
    actors: Actor[];
  } | null>(null);

  const loadDetail = useCallback(async () => {
    if (!instanceId) return;
    try {
      const d = await tramitesClient.getInstance(instanceId);
      setDetail({ fieldValues: d.fieldValues ?? [], actors: d.actors ?? [] });
    } catch {
      // El detalle es secundario para el resto del paso; los subbloques
      // muestran sus propios errores. No bloquea el render.
    }
  }, [instanceId]);

  useEffect(() => {
    void loadDetail();
  }, [loadDetail]);

  const fv = useCallback(
    (key: string): string =>
      detail?.fieldValues.find((f) => f.fieldKey === key)?.valueText ?? '',
    [detail],
  );

  const organismo = useMemo(
    () => ({
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
    if (!organismoSelected) setOrganismoModalOpen(true);
    setAutoOpened(true);
  }, [detail, organismoSelected, autoOpened]);

  const handleOrganismoConfirmed = async () => {
    setOrganismoModalOpen(false);
    await loadDetail();
    onRefresh?.();
  };

  return (
    <div className="space-y-8">
      <OrganismoSection
        organismo={organismo}
        organismoSelected={organismoSelected}
        onOpenModal={() => setOrganismoModalOpen(true)}
      />

      <ExpedienteSection
        instanceId={instanceId}
        modalidad={modalidad}
        fieldValues={detail?.fieldValues ?? []}
        actors={detail?.actors ?? []}
        organismo={organismo}
      />

      {modalidad === 'traspaso' && (
        <FirmaSection instanceId={instanceId} onRefresh={onRefresh} />
      )}
      <ParticipantesSection instanceId={instanceId} />
      <FurSection instanceId={instanceId} onRefresh={onRefresh} />

      <EnviarSection
        instanceId={instanceId}
        onRefresh={onRefresh}
        onSubmitted={onSubmitted}
      />

      {organismoModalOpen && instanceId && (
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

// ── Organismo de tránsito ─────────────────────────────────────────────

function OrganismoSection({
  organismo,
  organismoSelected,
  onOpenModal,
}: {
  organismo: { code: string; name: string; city: string };
  organismoSelected: boolean;
  onOpenModal: () => void;
}) {
  return (
    <section className="space-y-3" aria-label="Organismo de tránsito">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h4 className="text-sm font-bold">Organismo de tránsito</h4>
          <p className="text-xs opacity-70">
            El organismo donde se radicará el trámite. Es obligatorio para
            generar el FUR y enviar a tránsito.
          </p>
        </div>
        <button
          type="button"
          onClick={onOpenModal}
          className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-[11px] font-semibold border shrink-0"
          style={{ borderColor: '#557EFF', color: '#557EFF' }}
        >
          <Building2 className="h-3 w-3" />
          {organismoSelected ? 'Cambiar' : 'Seleccionar'}
        </button>
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

  // Sugerencia desde RUNT: si el nombre del organismo viaja en field_values y
  // coincide con un organismo del catálogo, se ofrece como atajo de un click.
  const runtSuggestion = useMemo<OrganismoTransito | null>(() => {
    if (!suggestedName.trim()) return null;
    return (
      findOrganismoByName(suggestedName) ?? {
        // Sin código en catálogo: igual se respeta el nombre del RUNT.
        code: '',
        name: suggestedName.trim(),
        city: '',
      }
    );
  }, [suggestedName]);

  const results = useMemo(() => filterOrganismos(query).slice(0, 40), [query]);

  const persist = async (org: OrganismoTransito) => {
    setSaving(true);
    setError(null);
    try {
      await tramitesClient.patchFieldValues(instanceId, [
        { formFieldId: null, fieldKey: 'transit_office_code', valueText: org.code },
        { formFieldId: null, fieldKey: 'transit_office_name', valueText: org.name },
        { formFieldId: null, fieldKey: 'transit_office_city', valueText: org.city },
      ]);
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
        style={{ borderColor: '#DFE5ED' }}
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
            {(runtSuggestion.city || runtSuggestion.code) && (
              <p className="text-[11px] opacity-70">
                {[runtSuggestion.city, runtSuggestion.code].filter(Boolean).join(' · ')}
              </p>
            )}
          </button>
        )}

        <div className="relative mb-3">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-3.5 w-3.5 opacity-50" aria-hidden="true" />
          <input
            type="text"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Buscar por ciudad, nombre o código…"
            aria-label="Buscar organismo de tránsito"
            className={`${INPUT_BASE} pl-9`}
            style={{ borderColor: '#DFE5ED' }}
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
            <li key={o.code}>
              <button
                type="button"
                onClick={() => void persist(o)}
                disabled={saving}
                className="w-full text-left rounded-xl border p-2.5 hover:border-[#557EFF] disabled:opacity-50"
                style={{ borderColor: '#DFE5ED' }}
              >
                <p className="text-xs font-semibold">{o.name}</p>
                <p className="text-[11px] opacity-70">{o.city} · {o.code}</p>
              </button>
            </li>
          ))}
          {results.length === 0 && (
            <li className="text-[11px] opacity-60 py-3 text-center">
              Sin resultados para «{query}».
            </li>
          )}
        </ul>
      </div>
    </div>
  );
}

// ── Expediente / resumen ──────────────────────────────────────────────

const VEHICLE_RESUMEN: { key: string; label: string }[] = [
  { key: 'plate', label: 'Placa' },
  { key: 'vin', label: 'VIN' },
  { key: 'vehicle_brand', label: 'Marca' },
  { key: 'vehicle_line', label: 'Línea' },
  { key: 'vehicle_year', label: 'Modelo' },
  { key: 'vehicle_color', label: 'Color' },
  { key: 'vehicle_class', label: 'Clase' },
  { key: 'vehicle_fuel', label: 'Combustible' },
  { key: 'vehicle_engine_displacement', label: 'Cilindraje' },
  { key: 'vehicle_state', label: 'Estado del vehículo' },
];

function ResumenRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between gap-3 py-0.5">
      <dt className="text-[11px] opacity-60">{label}</dt>
      <dd className="text-xs font-medium text-right">{value}</dd>
    </div>
  );
}

function ExpedienteSection({
  instanceId,
  modalidad,
  fieldValues,
  actors,
  organismo,
}: {
  instanceId: string | null;
  modalidad: WizardModalidad;
  fieldValues: FieldValue[];
  actors: Actor[];
  organismo: { code: string; name: string; city: string };
}) {
  const byKey = (key: string) =>
    fieldValues.find((f) => f.fieldKey === key)?.valueText ?? '';

  const vehicleRows = VEHICLE_RESUMEN.map((f) => ({
    label: f.label,
    value: byKey(f.key),
  })).filter((r) => r.value.trim() !== '');

  const comprador = actors.find((a) => a.actorType === 'comprador') ?? null;
  const vendedor = actors.find((a) => a.actorType === 'vendedor') ?? null;

  const [docs, setDocs] = useState<ProcedureAttachment[]>([]);
  const loadDocs = useCallback(async () => {
    if (!instanceId) return;
    try {
      setDocs(await tramitesClient.getAttachments(instanceId));
    } catch {
      // El listado del expediente es informativo; no bloquea el paso.
    }
  }, [instanceId]);
  useEffect(() => {
    void loadDocs();
  }, [loadDocs]);

  return (
    <section className="space-y-4" aria-label="Resumen del expediente">
      <div>
        <h4 className="text-sm font-bold">Resumen del expediente</h4>
        <p className="text-xs opacity-70">
          Revisa los datos antes de generar el FUR y enviar a tránsito.
        </p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
        {vehicleRows.length > 0 && (
          <div className="rounded-xl border p-3" style={{ borderColor: '#DFE5ED' }}>
            <p className="text-[10px] font-semibold uppercase opacity-60 mb-1.5">Vehículo</p>
            <dl>
              {vehicleRows.map((r) => (
                <ResumenRow key={r.label} label={r.label} value={r.value} />
              ))}
            </dl>
          </div>
        )}

        <div className="rounded-xl border p-3" style={{ borderColor: '#DFE5ED' }}>
          <p className="text-[10px] font-semibold uppercase opacity-60 mb-1.5">
            {modalidad === 'traspaso' ? 'Partes' : 'Comprador'}
          </p>
          <dl>
            {comprador ? (
              <>
                <ResumenRow label="Comprador" value={comprador.fullName} />
                <ResumenRow
                  label="Documento"
                  value={`${comprador.documentType} ${comprador.documentNumber}`}
                />
              </>
            ) : (
              <p className="text-[11px] opacity-60">Sin comprador registrado.</p>
            )}
            {modalidad === 'traspaso' && vendedor && (
              <>
                <ResumenRow label="Vendedor" value={vendedor.fullName} />
                <ResumenRow
                  label="Documento"
                  value={`${vendedor.documentType} ${vendedor.documentNumber}`}
                />
              </>
            )}
          </dl>
        </div>

        <div className="rounded-xl border p-3 md:col-span-2" style={{ borderColor: '#DFE5ED' }}>
          <p className="text-[10px] font-semibold uppercase opacity-60 mb-1.5">Organismo de tránsito</p>
          {organismo.name || organismo.code ? (
            <dl>
              <ResumenRow label="Organismo" value={organismo.name || '—'} />
              {organismo.city && <ResumenRow label="Ciudad" value={organismo.city} />}
              {organismo.code && <ResumenRow label="Código" value={organismo.code} />}
            </dl>
          ) : (
            <p className="text-[11px] opacity-60">Sin organismo seleccionado.</p>
          )}
        </div>
      </div>

      <div>
        <p className="text-[10px] font-semibold uppercase opacity-60 mb-2">Documentos</p>
        <ul className="space-y-2" aria-label="Documentos del expediente">
          {docs.map((d) => (
            <AttachmentRow
              key={d.id}
              instanceId={instanceId}
              attachment={d}
            />
          ))}
          {docs.length === 0 && (
            <li className="text-[11px] opacity-60">
              Aún no hay documentos en el expediente.
            </li>
          )}
        </ul>
      </div>
    </section>
  );
}

/** Fila de un adjunto con acción de descarga (blob → objectURL → anchor). */
function AttachmentRow({
  instanceId,
  attachment: d,
}: {
  instanceId: string | null;
  attachment: ProcedureAttachment;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleDownload = async () => {
    if (!instanceId) return;
    setBusy(true);
    setError(null);
    try {
      const { blob, filename } = await tramitesClient.downloadAttachment(
        instanceId,
        d.id,
      );
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = filename || d.filename;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    } catch {
      setError('No se pudo descargar el documento.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <li
      className="rounded-xl border p-3 flex items-center gap-3"
      style={{ borderColor: '#DFE5ED' }}
    >
      <FileText className="h-4 w-4 shrink-0" style={{ color: '#557EFF' }} aria-hidden="true" />
      <div className="min-w-0 flex-1">
        <p className="text-xs font-semibold capitalize">
          {d.tipo} · {d.filename}
        </p>
        <p className="text-[10px] opacity-60 truncate" title={d.sha256}>
          SHA-256: {d.sha256}
        </p>
        {error && (
          <p className="text-[11px] font-medium mt-0.5" style={{ color: '#FF4E00' }} role="alert">
            {error}
          </p>
        )}
      </div>
      <button
        type="button"
        onClick={() => void handleDownload()}
        disabled={busy || !instanceId}
        className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-[11px] font-semibold border shrink-0 disabled:opacity-50"
        style={{ borderColor: '#557EFF', color: '#557EFF' }}
        aria-label={`Descargar ${d.filename}`}
      >
        <Download className="h-3 w-3" />
        {busy ? 'Descargando…' : 'Descargar'}
      </button>
    </li>
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

  const partes: SignatureParte[] = ['comprador', 'vendedor'];

  return (
    <section className="space-y-4" aria-label="Firma de la compraventa">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h4 className="text-sm font-bold">Firma de la compraventa</h4>
          <p className="text-xs opacity-70">
            Solicita la firma electrónica de cada parte. El proveedor genera un
            enlace de firma; en DEV puedes simular la firma para avanzar.
          </p>
        </div>
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

  const handleSolicitar = async () => {
    if (!instanceId) return;
    setBusy(true);
    setError(null);
    try {
      await tramitesClient.solicitarFirma(instanceId, { parte });
      onChanged();
    } catch (err) {
      const msg = err instanceof Error ? err.message : '';
      setError(
        msg.startsWith('409')
          ? 'La firma de la compraventa solo aplica a traspaso.'
          : 'No se pudo solicitar la firma.',
      );
    } finally {
      setBusy(false);
    }
  };

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
      style={{ borderColor: '#DFE5ED' }}
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
          {signature.estado === 'enviada' && (
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
        <button
          type="button"
          onClick={() => void handleSolicitar()}
          disabled={busy || !instanceId}
          className="px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
          style={{ background: '#557EFF' }}
        >
          {busy ? 'Solicitando…' : 'Solicitar firma'}
        </button>
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

      <form
        onSubmit={handleInvite}
        className="rounded-xl border p-4 space-y-3"
        style={{ borderColor: '#DFE5ED' }}
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
              style={{ borderColor: '#DFE5ED' }}
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
              style={{ borderColor: '#DFE5ED' }}
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
              style={{ borderColor: '#DFE5ED' }}
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
              style={{ borderColor: '#DFE5ED' }}
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
    <li className="rounded-xl border p-3" style={{ borderColor: '#DFE5ED' }}>
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
        {!p.completado && (
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

// ── FUR / compraventa ─────────────────────────────────────────────────

/** Tipos de documento generados por el FUR. */
const FUR_TIPOS = new Set(['fur', 'compraventa', 'certificado_identidad']);

function FurSection({
  instanceId,
  onRefresh,
}: {
  instanceId: string | null;
  onRefresh?: () => void;
}) {
  const [docs, setDocs] = useState<ProcedureAttachment[] | null>(null);
  const [generating, setGenerating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [lastResult, setLastResult] = useState<FurDocument[] | null>(null);

  const load = useCallback(async () => {
    if (!instanceId) return;
    try {
      const list = await tramitesClient.getAttachments(instanceId);
      setDocs(list.filter((a) => FUR_TIPOS.has(a.tipo)));
    } catch {
      // El listado de adjuntos es secundario; el error de generar se muestra abajo.
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
    try {
      const result = await tramitesClient.generarFur(instanceId);
      setLastResult(result.documents);
      await load();
      onRefresh?.();
    } catch (err) {
      const msg = err instanceof Error ? err.message : '';
      setError(
        msg.includes('biometria_gate')
          ? 'Falta validar identidad: primero debe aprobarse la biométrica requerida de las partes.'
          : msg.includes('organismo_requerido')
            ? 'Selecciona el organismo de tránsito antes de generar el FUR.'
            : msg.startsWith('409')
              ? 'No se pudo generar el FUR: revisa la identidad y el organismo de tránsito.'
              : 'No se pudo generar el FUR.',
      );
    } finally {
      setGenerating(false);
    }
  };

  const generated = (docs ?? []).length > 0 || (lastResult ?? []).length > 0;

  return (
    <section className="space-y-4" aria-label="Generación del FUR">
      <div>
        <h4 className="text-sm font-bold">FUR / contrato de compraventa</h4>
        <p className="text-xs opacity-70">
          Genera el FUR y el certificado de identidad (y, en traspaso, el
          contrato de compraventa) con los datos del trámite. Requiere la
          biométrica aprobada de las partes y el organismo seleccionado.
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
                <p className="text-xs font-semibold capitalize">
                  {d.tipo} · {d.filename}
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
    </section>
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
      );
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = filename || d.filename;
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

// ── Enviar a tránsito ─────────────────────────────────────────────────

const SUBMIT_409_COPY: Record<string, string> = {
  documentos_incompletos:
    'Faltan documentos obligatorios. Vuelve al paso de documentos y complétalos.',
  identidad_requerida:
    'Falta validar la identidad. Completa la biométrica requerida en este paso.',
  fur_requerido: 'Genera el FUR antes de enviar a tránsito.',
  organismo_requerido:
    'Selecciona el organismo de tránsito antes de enviar.',
};

function EnviarSection({
  instanceId,
  onRefresh,
  onSubmitted,
}: {
  instanceId: string | null;
  onRefresh?: () => void;
  onSubmitted?: () => void;
}) {
  const [submitting, setSubmitting] = useState(false);
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async () => {
    if (!instanceId) return;
    setSubmitting(true);
    setError(null);
    try {
      await tramitesClient.submitInstance(instanceId);
      setDone(true);
      onRefresh?.();
      onSubmitted?.();
    } catch (err) {
      const msg = err instanceof Error ? err.message : '';
      if (msg.startsWith('409')) {
        const code = Object.keys(SUBMIT_409_COPY).find((c) => msg.includes(c));
        setError(
          code
            ? SUBMIT_409_COPY[code]
            : 'No se puede enviar todavía: hay requisitos pendientes.',
        );
      } else {
        setError('No se pudo enviar el trámite a tránsito.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  if (done) {
    return (
      <section className="space-y-3" aria-label="Envío a tránsito">
        <div
          className="rounded-xl border p-4 flex items-center gap-3"
          style={{ borderColor: '#8CC63F', background: 'rgba(140,198,63,0.08)' }}
          role="status"
          aria-live="polite"
        >
          <Check className="h-5 w-5 shrink-0" style={{ color: '#5B8A1F' }} aria-hidden="true" />
          <div>
            <p className="text-sm font-bold" style={{ color: '#5B8A1F' }}>
              Enviado a tránsito
            </p>
            <p className="text-xs opacity-70">
              El trámite fue radicado ante el organismo de tránsito.
            </p>
          </div>
        </div>
      </section>
    );
  }

  return (
    <section className="space-y-3" aria-label="Envío a tránsito">
      <div>
        <h4 className="text-sm font-bold">Enviar a tránsito</h4>
        <p className="text-xs opacity-70">
          Radica el expediente ante el organismo de tránsito. Se valida que la
          identidad, los documentos, el FUR y el organismo estén completos.
        </p>
      </div>

      {error && (
        <div
          className="rounded-xl p-3 text-xs border"
          style={{ borderColor: '#F9AC00', background: 'rgba(249,172,0,0.08)', color: '#F9AC00' }}
          role="alert"
          aria-live="polite"
        >
          {error}
        </div>
      )}

      <button
        type="button"
        onClick={() => void handleSubmit()}
        disabled={submitting || !instanceId}
        className="flex items-center gap-2 px-5 py-2.5 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
        style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
      >
        <Send className="h-3.5 w-3.5" />
        {submitting ? 'Enviando…' : 'Enviar a tránsito'}
      </button>
    </section>
  );
}
