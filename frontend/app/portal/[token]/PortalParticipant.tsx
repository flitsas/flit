'use client';

import { useCallback, useEffect, useState } from 'react';
import { Check, FileText } from 'lucide-react';
import { portalPublicClient } from '@/lib/api/tramites-client';
import type {
  PortalDocumentoStatus,
  PortalFirmaUrl,
  PortalView,
} from '@/lib/api/types/procedure-runtime';

interface Props {
  token: string;
}

type LoadState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'ready'; view: PortalView };

const CARD = 'w-full max-w-md rounded-3xl p-7 bg-white dark:bg-[#0B0F14] border';

/**
 * Portal público del participante por magic-link (Slice 7B). Lee la vista por
 * token; si está expirada/no encontrada/completada muestra un mensaje terminal.
 * Bloquea con el consentimiento Ley 1581 hasta aceptarlo; luego muestra los
 * pasos pendientes (documentos, biométrica, firma) y un botón de finalizar.
 * Todo el estado es server-driven: tras cada acción re-consulta la vista.
 */
export function PortalParticipant({ token }: Props) {
  const [load, setLoad] = useState<LoadState>({ kind: 'loading' });
  const [finalized, setFinalized] = useState(false);

  const fetchView = useCallback(async () => {
    try {
      const view = await portalPublicClient.getByToken(token);
      setLoad(() => ({ kind: 'ready', view }));
    } catch (err) {
      const msg = err instanceof Error ? err.message : '';
      setLoad(() => ({
        kind: 'error',
        message: msg.startsWith('404')
          ? 'Enlace no válido, expirado o ya utilizado.'
          : 'No se pudo cargar el portal. Intenta de nuevo.',
      }));
    }
  }, [token]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void fetchView();
  }, [fetchView]);

  if (finalized) {
    return (
      <div className={CARD} role="status">
        <div
          className="h-14 w-14 mx-auto rounded-full grid place-items-center mb-3"
          style={{ background: 'rgba(0,219,213,0.15)' }}
        >
          <Check className="h-7 w-7" style={{ color: '#00DBD5' }} />
        </div>
        <h1 className="text-lg font-bold text-center">¡Listo!</h1>
        <p className="text-sm opacity-70 text-center mt-1">
          Completaste tu parte del trámite. Ya puedes cerrar esta página.
        </p>
      </div>
    );
  }

  if (load.kind === 'loading') {
    return (
      <div className={CARD}>
        <p className="text-sm opacity-70 text-center">Cargando portal…</p>
      </div>
    );
  }

  if (load.kind === 'error') {
    return (
      <div className={CARD} role="alert">
        <h1 className="text-lg font-bold mb-2">Portal del participante</h1>
        <p className="text-sm" style={{ color: '#FF4E00' }}>
          {load.message}
        </p>
      </div>
    );
  }

  const { view } = load;

  if (view.completado) {
    return (
      <div className={CARD} role="status">
        <h1 className="text-lg font-bold mb-2">Trámite completado</h1>
        <p className="text-sm opacity-70">
          Ya completaste tu parte de este trámite.
        </p>
      </div>
    );
  }

  if (view.expirado) {
    return (
      <div className={CARD} role="alert">
        <h1 className="text-lg font-bold mb-2">Enlace expirado</h1>
        <p className="text-sm opacity-70">
          El enlace del portal expiró. Solicita uno nuevo a tu gestor.
        </p>
      </div>
    );
  }

  return (
    <div className={CARD}>
      <h1 className="text-lg font-bold">Portal del participante</h1>
      <p className="text-sm opacity-70 mt-1">
        Hola {view.nombre} ({view.rol}). Trámite {view.instancia.referencia}
        {view.instancia.tipologiaNombre ? ` · ${view.instancia.tipologiaNombre}` : ''}.
      </p>

      {!view.consentDado ? (
        <ConsentGate token={token} view={view} onAccepted={() => void fetchView()} />
      ) : (
        <PendingTasks
          token={token}
          view={view}
          onRefresh={() => void fetchView()}
          onFinalized={() => setFinalized(true)}
        />
      )}
    </div>
  );
}

// ── Consentimiento Ley 1581 (gate) ────────────────────────────────────

function ConsentGate({
  token,
  view,
  onAccepted,
}: {
  token: string;
  view: PortalView;
  onAccepted: () => void;
}) {
  const [accepted, setAccepted] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleAccept = async () => {
    if (!accepted) return;
    setSubmitting(true);
    setError(null);
    try {
      await portalPublicClient.aceptarConsentimiento(token);
      onAccepted();
    } catch {
      setError('No se pudo registrar el consentimiento. Intenta de nuevo.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="mt-5 space-y-4">
      <div>
        <h2 className="text-sm font-bold">Autorización de datos personales</h2>
        <p className="text-[10px] opacity-50">Versión {view.consentVersion}</p>
      </div>
      <div
        className="rounded-xl border p-3 text-[11px] leading-relaxed max-h-48 overflow-y-auto opacity-80"
      >
        {view.consentText}
      </div>
      <label className="flex items-start gap-2 text-xs cursor-pointer">
        <input
          type="checkbox"
          checked={accepted}
          onChange={(e) => setAccepted(e.target.checked)}
          className="mt-0.5"
          aria-label="Acepto el tratamiento de mis datos personales"
        />
        <span>
          He leído y acepto el tratamiento de mis datos personales conforme a la
          Ley 1581 de 2012.
        </span>
      </label>

      {error && (
        <p className="text-[11px] font-medium" style={{ color: '#FF4E00' }} role="alert">
          {error}
        </p>
      )}

      <button
        type="button"
        onClick={() => void handleAccept()}
        disabled={!accepted || submitting}
        className="w-full py-2.5 rounded-xl text-sm font-semibold text-white disabled:opacity-50"
        style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
      >
        {submitting ? 'Registrando…' : 'Aceptar y continuar'}
      </button>
    </div>
  );
}

// ── Pasos pendientes (tras consentimiento) ────────────────────────────

function PendingTasks({
  token,
  view,
  onRefresh,
  onFinalized,
}: {
  token: string;
  view: PortalView;
  onRefresh: () => void;
  onFinalized: () => void;
}) {
  const { pasosPendientes: pasos } = view;

  return (
    <div className="mt-5 space-y-6">
      <DocumentosTask
        token={token}
        documentos={pasos.documentos}
        onUploaded={onRefresh}
      />

      <BiometricaTask
        existe={pasos.biometrica.existe}
        estado={pasos.biometrica.estado}
        pendiente={pasos.biometrica.pendiente}
      />

      {pasos.firma.aplica && <FirmaTask token={token} onRefresh={onRefresh} />}

      <FinalizarTask token={token} onFinalized={onFinalized} />
    </div>
  );
}

function DocumentosTask({
  token,
  documentos,
  onUploaded,
}: {
  token: string;
  documentos: PortalDocumentoStatus[];
  onUploaded: () => void;
}) {
  const [uploadingTipo, setUploadingTipo] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const handleUpload = async (tipo: string, file: File | undefined) => {
    if (!file) return;
    setUploadingTipo(tipo);
    setError(null);
    try {
      await portalPublicClient.subirDocumento(token, tipo, file);
      onUploaded();
    } catch {
      setError('No se pudo subir el documento. Verifica el formato (PDF/JPG/PNG/WEBP, máx 20 MB).');
    } finally {
      setUploadingTipo(null);
    }
  };

  if (documentos.length === 0) {
    return (
      <section aria-label="Documentos">
        <h2 className="text-sm font-bold mb-1">Documentos</h2>
        <p className="text-[11px] opacity-60">No se requieren documentos de tu parte.</p>
      </section>
    );
  }

  return (
    <section aria-label="Documentos">
      <h2 className="text-sm font-bold mb-2">Documentos</h2>
      <ul className="space-y-2">
        {documentos.map((d) => (
          <li
            key={d.tipo}
            className="rounded-xl border p-3"
            style={{ borderColor: d.subido ? '#8CC63F' : '#DFE5ED' }}
          >
            <div className="flex items-center justify-between gap-3">
              <div className="flex items-center gap-1.5 min-w-0">
                {d.subido && (
                  <span style={{ color: '#8CC63F' }} aria-hidden="true">
                    ✓
                  </span>
                )}
                <span className="text-xs font-semibold truncate">{d.label}</span>
                {d.obligatorio && (
                  <span
                    className="rounded px-1.5 py-0.5 text-[9px] font-bold uppercase shrink-0"
                    style={{ background: 'rgba(255,78,0,0.10)', color: '#FF4E00' }}
                  >
                    Obligatorio
                  </span>
                )}
              </div>
              <label
                className="rounded-xl border px-3 py-1.5 text-[11px] font-semibold shrink-0 cursor-pointer"
                style={{ borderColor: '#557EFF', color: '#557EFF' }}
              >
                {uploadingTipo === d.tipo ? 'Subiendo…' : d.subido ? 'Reemplazar' : 'Subir'}
                <input
                  type="file"
                  accept="application/pdf,image/jpeg,image/png,image/webp"
                  className="hidden"
                  aria-label={`Subir ${d.label}`}
                  onChange={(e) => {
                    const file = e.target.files?.[0];
                    e.target.value = '';
                    void handleUpload(d.tipo, file);
                  }}
                />
              </label>
            </div>
          </li>
        ))}
      </ul>
      {error && (
        <p className="text-[11px] font-medium mt-2" style={{ color: '#FF4E00' }} role="alert">
          {error}
        </p>
      )}
    </section>
  );
}

function BiometricaTask({
  existe,
  estado,
  pendiente,
}: {
  existe: boolean;
  estado: string | null;
  pendiente: boolean;
}) {
  return (
    <section aria-label="Validación biométrica">
      <h2 className="text-sm font-bold mb-1">Validación biométrica</h2>
      {!existe ? (
        <p className="text-[11px] opacity-60">
          Tu gestor aún no ha generado la validación biométrica. Estará disponible
          pronto.
        </p>
      ) : !pendiente ? (
        <p className="text-[11px] flex items-center gap-1.5 font-semibold" style={{ color: '#5B8A1F' }}>
          <Check className="h-3.5 w-3.5" /> Identidad verificada
        </p>
      ) : (
        <p className="text-[11px] opacity-70">
          Validación biométrica pendiente
          {estado ? ` (${estado})` : ''}. Sigue el enlace que te compartió tu
          gestor para completarla.
        </p>
      )}
    </section>
  );
}

function FirmaTask({ token, onRefresh }: { token: string; onRefresh: () => void }) {
  const [firma, setFirma] = useState<PortalFirmaUrl | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const f = await portalPublicClient.getFirma(token);
      setFirma(f);
    } catch {
      // El estado de firma es secundario; el resto del portal sigue funcionando.
    }
  }, [token]);

  useEffect(() => {
    // load solo hace setState DESPUÉS del await (no es cascada síncrona).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  const handleSimular = async () => {
    setBusy(true);
    setError(null);
    try {
      await portalPublicClient.simularFirma(token);
      await load();
      onRefresh();
    } catch (err) {
      const msg = err instanceof Error ? err.message : '';
      setError(
        msg.startsWith('409')
          ? 'La firma aún no ha sido solicitada por el gestor.'
          : 'No se pudo simular la firma.',
      );
    } finally {
      setBusy(false);
    }
  };

  return (
    <section aria-label="Firma de la compraventa">
      <h2 className="text-sm font-bold mb-1">Firma de la compraventa</h2>
      {firma?.firmada ? (
        <p className="text-[11px] flex items-center gap-1.5 font-semibold" style={{ color: '#5B8A1F' }}>
          <Check className="h-3.5 w-3.5" /> Firmaste la compraventa
        </p>
      ) : firma && firma.signatureId === null ? (
        <p className="text-[11px] opacity-70">
          La firma aún no ha sido solicitada por el gestor.
        </p>
      ) : (
        <div className="space-y-2">
          {firma?.signUrl && (
            <a
              href={firma.signUrl}
              target="_blank"
              rel="noreferrer"
              className="text-[11px] underline"
              style={{ color: '#557EFF' }}
            >
              Abrir enlace de firma
            </a>
          )}
          <div>
            <button
              type="button"
              onClick={() => void handleSimular()}
              disabled={busy}
              className="px-4 py-1.5 rounded-xl text-[11px] font-semibold border disabled:opacity-50"
              style={{ borderColor: '#557EFF', color: '#557EFF' }}
            >
              {busy ? 'Firmando…' : 'Simular firma (DEV)'}
            </button>
          </div>
        </div>
      )}
      {error && (
        <p className="text-[11px] font-medium mt-2" style={{ color: '#FF4E00' }} role="alert">
          {error}
        </p>
      )}
    </section>
  );
}

function FinalizarTask({
  token,
  onFinalized,
}: {
  token: string;
  onFinalized: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleFinalizar = async () => {
    setBusy(true);
    setError(null);
    try {
      await portalPublicClient.finalizar(token);
      onFinalized();
    } catch {
      setError('No se pudo finalizar. Intenta de nuevo.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <section aria-label="Finalizar" className="pt-2 border-t">
      <button
        type="button"
        onClick={() => void handleFinalizar()}
        disabled={busy}
        className="w-full mt-3 py-2.5 rounded-xl text-sm font-semibold text-white disabled:opacity-50 flex items-center justify-center gap-2"
        style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
      >
        <FileText className="h-4 w-4" aria-hidden="true" />
        {busy ? 'Finalizando…' : 'Finalizar mi parte'}
      </button>
      {error && (
        <p className="text-[11px] font-medium mt-2" style={{ color: '#FF4E00' }} role="alert">
          {error}
        </p>
      )}
    </section>
  );
}
