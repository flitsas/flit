'use client';

import { useCallback, useEffect, useState } from 'react';
import { Check, Copy, FileText, RefreshCw } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import type {
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
 * Paso de firma electrónica + generación del FUR + portal de participantes
 * (Slice 7, mock). Render BAJO el BiometricStep en el paso FUR del wizard.
 * Todo el gating es server-driven: este paso refleja el estado y refresca el
 * wizard tras cada acción; no calcula completitud en cliente.
 *
 * - Firma (solo traspaso): por rol (comprador|vendedor) — solicitar/simular.
 * - Participantes (portal): invitar + magic-link copiable + reinvitar.
 * - FUR: generar (409 biometria_gate manejado) + listado de documentos.
 */
export function FirmaFurStep({ instanceId, modalidad, onRefresh }: Props) {
  return (
    <div className="space-y-8">
      {modalidad === 'traspaso' && (
        <FirmaSection instanceId={instanceId} onRefresh={onRefresh} />
      )}
      <ParticipantesSection instanceId={instanceId} />
      <FurSection instanceId={instanceId} onRefresh={onRefresh} />
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
const FUR_TIPOS = new Set(['fur', 'compraventa']);

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
        msg.startsWith('409')
          ? 'Para generar el FUR primero debe aprobarse la biométrica requerida de las partes.'
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
          Genera el FUR (y, en traspaso, el contrato de compraventa) con los
          datos del trámite. Requiere la biométrica aprobada de las partes.
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
            ? 'Re-generar FUR / contrato'
            : 'Generar FUR / contrato'}
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
              <div className="min-w-0">
                <p className="text-xs font-semibold capitalize">
                  {d.tipo} · {d.filename}
                </p>
                <p className="text-[10px] opacity-60 truncate" title={d.sha256}>
                  SHA-256: {d.sha256}
                </p>
              </div>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
