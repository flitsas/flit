'use client';

import { forwardRef, useEffect, useImperativeHandle, useState } from 'react';
import { ChevronDown } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { digitsOnly } from '@/lib/format/currency';
import { formatDateOnly } from '@/lib/format/date-only';
import { InlineAlert } from '@/components/atom/InlineAlert';
import { useWizardReadOnly } from './WizardReadOnlyContext';
import { PrendaDocumentUpload } from './PrendaDocumentUpload';
import { prendaDocTipoFor } from './prenda-document-tipos';
import type { WizardStepFormHandle } from './wizard-step-form';
import type { FieldValue, PrendaDecision, WizardModalidad } from '@/lib/api/types/procedure-runtime';

/** Handle imperativo: la shell del wizard dispara guardar+validar. */
export type PrendaFormHandle = WizardStepFormHandle;

/** Etiquetas legibles de cada decisión de prenda (contrato con el backend). */
export const PRENDA_DECISION_LABELS: Record<PrendaDecision, string> = {
  solicitar: 'Solicitar constitución de prenda',
  registrar: 'Registrar prenda',
  levantar: 'Levantar gravamen',
  omitir: 'Continuar sin gestionar (asumo el riesgo)',
  sin_prenda: 'Sin prenda',
};

/** Decisiones que exigen el documento de soporte (se adjunta en esta sección). */
const REQUIERE_DOCUMENTO: ReadonlySet<PrendaDecision> = new Set<PrendaDecision>([
  'solicitar',
  'registrar',
  'levantar',
]);

/** Decisiones que capturan datos del acreedor (beneficiario del gravamen) para el FUR. */
const CAPTURA_ACREEDOR: ReadonlySet<PrendaDecision> = new Set<PrendaDecision>([
  'solicitar',
  'registrar',
]);

/** En matrícula la prenda es declarativa: registrar o sin prenda. */
const MATRICULA_DECISIONS: PrendaDecision[] = ['registrar', 'sin_prenda'];

/** Ítem de prenda/gravamen reportado por el RUNT (cuando el proveedor trae detalle). */
export type RuntGravamenItem = {
  idPrenda?: string | null;
  acreedor?: string | null;
  documentoAcreedor?: string | null;
  tipoDocumentoAcreedor?: string | null;
  fechaInscripcion?: string | null;
  estado?: string | null;
};

/** Resumen RUNT para el desplegable junto a la alerta. */
export type RuntPrendaSummary = {
  tieneGravamenes?: string | null;
  tienePrendas?: string | null;
  prendario?: string | null;
  nombreAcreedor?: string | null;
  items: RuntGravamenItem[];
};

interface Props {
  instanceId: string | null;
  /** Decisiones ofrecidas (default: matrícula declarativa). Traspaso pasa las 4 de gestión. */
  decisions?: PrendaDecision[];
  /** Se invoca tras un guardado exitoso (la shell refresca el wizard). */
  onSaved?: () => void;
  hideHeader?: boolean;
  /**
   * Embebido en el wizard: oculta el botón de guardado dedicado. La persistencia la dispara
   * el Continuar/Guardar del paso vía `ref.save()`.
   */
  embeddedInWizard?: boolean;
  /**
   * El RUNT reportó gravámenes/prendas (check `gravamenes` en warn). Muestra una aleta
   * informativa y, si no hay decisión guardada, sugiere "registrar" precargando acreedor/NIT.
   */
  runtHasGravamen?: boolean;
  /** Mensaje opcional del check RUNT (detalle). */
  runtGravamenMessage?: string | null;
  /**
   * Modalidad del trámite (OCR / documentos). Default matrícula.
   */
  modalidad?: WizardModalidad;
  /**
   * Compañía+OT: certificado obligatorio (default) u opcional. Viene de GET /wizard
   * (`prendaDocumentRequired`).
   */
  documentRequired?: boolean;
  /**
   * Gate Continuar: `false` cuando la decisión exige certificado obligatorio y aún no hay adjunto.
   * Con certificado opcional (o sin decisión que lo exija) reporta `true`.
   */
  onDocumentGateChange?: (ready: boolean) => void;
}

const INPUT_BASE =
  'w-full px-3 py-2 rounded-xl border bg-white dark:bg-[#0B0F14] text-xs outline-none focus:border-[#557EFF] aria-[invalid=true]:border-[#FF4E00]';

function byKey(fields: FieldValue[], key: string): string {
  return fields.find((f) => f.fieldKey === key)?.valueText?.trim() ?? '';
}

function byJson(fields: FieldValue[], key: string): string | null {
  return fields.find((f) => f.fieldKey === key)?.valueJson?.trim() || null;
}

/** Parsea el JSON hidratado por la consulta RUNT (`runt_gravamenes`). */
export function parseRuntGravamenesJson(raw: string | null | undefined): RuntGravamenItem[] {
  if (!raw?.trim()) return [];
  try {
    const parsed = JSON.parse(raw) as unknown;
    if (!Array.isArray(parsed)) return [];
    return parsed.map((row) => {
      const o = (row ?? {}) as Record<string, unknown>;
      const str = (k: string) => {
        const v = o[k];
        if (v == null) return null;
        return String(v).trim() || null;
      };
      return {
        idPrenda: str('idPrenda') ?? str('IdPrenda'),
        // Intempo: nombreAcreedor · Kyverum crudo: acreedor
        acreedor:
          str('nombreAcreedor') ??
          str('NombreAcreedor') ??
          str('acreedor') ??
          str('Acreedor'),
        documentoAcreedor:
          str('numeroDocumentoAcreedor') ??
          str('NumeroDocumentoAcreedor') ??
          str('documentoAcreedor'),
        tipoDocumentoAcreedor: str('tipoDocumentoAcreedor') ?? str('TipoDocumentoAcreedor'),
        fechaInscripcion: str('fechaInscripcion') ?? str('FechaInscripcion'),
        estado: str('estadoPrenda') ?? str('EstadoPrenda') ?? str('estado'),
      };
    });
  } catch {
    return [];
  }
}

export function buildRuntPrendaSummary(fields: FieldValue[]): RuntPrendaSummary {
  return {
    tieneGravamenes: byKey(fields, 'runt_tiene_gravamenes') || null,
    tienePrendas: byKey(fields, 'runt_tiene_prendas') || null,
    prendario: byKey(fields, 'runt_prendario') || null,
    nombreAcreedor: byKey(fields, 'runt_nombre_acreedor') || null,
    items: parseRuntGravamenesJson(byJson(fields, 'runt_gravamenes')),
  };
}

function hasRuntDetail(s: RuntPrendaSummary): boolean {
  return Boolean(
    s.tieneGravamenes ||
      s.tienePrendas ||
      s.prendario ||
      s.nombreAcreedor ||
      s.items.length > 0,
  );
}

function hasRuntAcreedorDetail(s: RuntPrendaSummary): boolean {
  return Boolean(s.prendario || s.nombreAcreedor || s.items.length > 0);
}

/** Acreedor/NIT sugeridos por la consulta RUNT (primer ítem con dato, o campos resumen). */
export function pickRuntAcreedor(
  summary: RuntPrendaSummary,
): { nombre: string; documento: string } | null {
  const fromItem = summary.items.find(
    (i) => Boolean(i.acreedor?.trim() || i.documentoAcreedor?.trim()),
  );
  if (fromItem) {
    return {
      nombre: (fromItem.acreedor ?? '').trim(),
      documento: digitsOnly(fromItem.documentoAcreedor ?? ''),
    };
  }
  const nombre = (summary.nombreAcreedor || summary.prendario || '').trim();
  if (!nombre) return null;
  return { nombre, documento: '' };
}

/** Fila etiqueta/valor del detalle RUNT. */
function RuntField({ label, value }: { label: string; value: string | null | undefined }) {
  if (!value?.trim()) return null;
  return (
    <div>
      <dt className="text-[10px] font-bold uppercase opacity-55">{label}</dt>
      <dd className="font-semibold">{value}</dd>
    </div>
  );
}

/**
 * Captura de la decisión de prenda (gravamen) del trámite. En matrícula (R4) es declarativa e
 * informativa (no bloquea la radicación); en traspaso (R10) el gate lo aplica el backend.
 * Cuando la decisión exige soporte, el certificado se adjunta aquí (mismos tipos `prenda_*`
 * que consume el gate) con el mismo diseño de tarjetas del checklist de documentos.
 */
export const PrendaForm = forwardRef<PrendaFormHandle, Props>(function PrendaForm(
  {
    instanceId,
    decisions = MATRICULA_DECISIONS,
    onSaved,
    hideHeader = false,
    embeddedInWizard = false,
    runtHasGravamen = false,
    runtGravamenMessage = null,
    modalidad = 'matricula_inicial',
    documentRequired = true,
    onDocumentGateChange,
  },
  ref,
) {
  const readOnly = useWizardReadOnly();
  const [decision, setDecision] = useState<PrendaDecision | ''>('');
  const [acreedorNombre, setAcreedorNombre] = useState('');
  const [acreedorDocumento, setAcreedorDocumento] = useState('');
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [runtSummary, setRuntSummary] = useState<RuntPrendaSummary | null>(null);
  const [runtOpen, setRuntOpen] = useState(false);
  const [docSatisfied, setDocSatisfied] = useState(false);
  const offersRegistrar = decisions.includes('registrar');

  const applyRuntAcreedorIfEmpty = (
    summary: RuntPrendaSummary,
    currentNombre: string,
    currentDoc: string,
  ): { nombre: string; documento: string } => {
    if (currentNombre.trim() || currentDoc.trim()) {
      return { nombre: currentNombre, documento: currentDoc };
    }
    const pick = pickRuntAcreedor(summary);
    if (!pick) return { nombre: currentNombre, documento: currentDoc };
    return {
      nombre: pick.nombre || currentNombre,
      documento: pick.documento || currentDoc,
    };
  };

  useEffect(() => {
    if (!instanceId) return;
    let active = true;
    const load = async () => {
      setLoading(true);
      try {
        const [p, detail] = await Promise.all([
          tramitesClient.getPrenda(instanceId),
          tramitesClient.getInstance(instanceId).catch(() => null),
        ]);
        if (!active) return;
        const summary = detail?.fieldValues
          ? buildRuntPrendaSummary(detail.fieldValues)
          : { items: [] as RuntGravamenItem[] };
        setRuntSummary(summary);
        if (hasRuntAcreedorDetail(summary)) setRuntOpen(true);

        if (p) {
          setDecision(p.decision);
          const filled = applyRuntAcreedorIfEmpty(
            summary,
            p.acreedorNombre ?? '',
            digitsOnly(p.acreedorDocumento ?? ''),
          );
          setAcreedorNombre(filled.nombre);
          setAcreedorDocumento(filled.documento);
        } else if (hasRuntAcreedorDetail(summary) || runtHasGravamen) {
          // Consulta con prenda: sugerir "registrar" y precargar acreedor/NIT.
          if (offersRegistrar) {
            setDecision('registrar');
          }
          const filled = applyRuntAcreedorIfEmpty(summary, '', '');
          setAcreedorNombre(filled.nombre);
          setAcreedorDocumento(filled.documento);
        }
      } catch {
        /* sin decisión previa: el form queda vacío */
      } finally {
        if (active) setLoading(false);
      }
    };
    void load();
    return () => {
      active = false;
    };
  }, [instanceId, runtHasGravamen, offersRegistrar]);

  const capturaAcreedor = decision !== '' && CAPTURA_ACREEDOR.has(decision);
  const requiereDocumento = decision !== '' && REQUIERE_DOCUMENTO.has(decision);
  const documentGateReady = !requiereDocumento || !documentRequired || docSatisfied;

  useEffect(() => {
    onDocumentGateChange?.(documentGateReady);
  }, [documentGateReady, onDocumentGateChange]);

  const selectDecision = (d: PrendaDecision) => {
    setDecision(d);
    // La decisión nueva no pide certificado: descarta el adjunto satisfecho de la anterior
    // (se resetea aquí y no en un efecto: las otras dos rutas que fijan `decision` viven en la
    // carga inicial, donde `docSatisfied` todavía es el `false` de arranque).
    if (!REQUIERE_DOCUMENTO.has(d)) setDocSatisfied(false);
    if (CAPTURA_ACREEDOR.has(d) && runtSummary) {
      const filled = applyRuntAcreedorIfEmpty(runtSummary, acreedorNombre, acreedorDocumento);
      setAcreedorNombre(filled.nombre);
      setAcreedorDocumento(filled.documento);
    }
  };

  const submit = async (): Promise<boolean> => {
    if (!instanceId) return false;
    if (decision === '') return true;
    setSaving(true);
    setSaved(false);
    setError(null);
    try {
      await tramitesClient.putPrenda(instanceId, {
        decision,
        acreedorNombre: capturaAcreedor ? acreedorNombre.trim() || null : null,
        acreedorDocumento: capturaAcreedor ? acreedorDocumento.trim() || null : null,
      });
      setSaved(true);
      onSaved?.();
      return true;
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Error al guardar la decisión de prenda');
      return false;
    } finally {
      setSaving(false);
    }
  };

  useImperativeHandle(ref, () => ({ save: submit }));

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    await submit();
  };

  const showRuntPanel = runtHasGravamen;
  const summary = runtSummary ?? { items: [] as RuntGravamenItem[] };
  const showDetailRows = hasRuntDetail(summary);

  return (
    <form
      onSubmit={handleSubmit}
      className="rounded-2xl p-4 border bg-white dark:bg-[#0B0F14]"
      aria-label="Prenda o gravamen del trámite"
      noValidate
    >
      {!hideHeader && (
        <div className="mb-3">
          <h4 className="text-sm font-bold">Prenda / gravamen</h4>
          <p className="text-[11px] opacity-60">
            Declara si el vehículo tiene prenda. Si registras, solicitas o levantas, adjunta el
            certificado en esta sección.
          </p>
        </div>
      )}

      {showRuntPanel && (
        <div className="mb-3 space-y-2">
          <InlineAlert tone="warning" title="RUNT reporta gravamen o prenda">
            {runtGravamenMessage?.trim() ||
              'El vehículo tiene gravámenes o prendas según el RUNT. Revisa y declara la decisión correspondiente.'}
          </InlineAlert>

          <div className="rounded-xl border overflow-hidden" style={{ borderColor: 'rgba(245,158,11,0.35)' }}>
            <button
              type="button"
              onClick={() => setRuntOpen((o) => !o)}
              className="flex w-full items-center justify-between gap-2 px-3 py-2.5 text-left text-xs font-semibold"
              style={{ background: 'rgba(245,158,11,0.08)', color: '#B45309' }}
              aria-expanded={runtOpen}
              aria-controls="runt-prenda-detail"
            >
              <span>Ver información de prenda / gravamen del RUNT</span>
              <ChevronDown
                className={`h-4 w-4 shrink-0 transition-transform ${runtOpen ? 'rotate-180' : ''}`}
                aria-hidden
              />
            </button>
            {runtOpen && (
              <div
                id="runt-prenda-detail"
                className="space-y-3 border-t px-3 py-3 text-xs"
                style={{ borderColor: 'rgba(245,158,11,0.25)' }}
                role="region"
                aria-label="Detalle de prenda reportado por el RUNT"
              >
                {showDetailRows ? (
                  <>
                    <dl className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                      <RuntField label="Tiene gravámenes" value={summary.tieneGravamenes} />
                      <RuntField label="Tiene prendas" value={summary.tienePrendas} />
                      <RuntField label="Prendario" value={summary.prendario} />
                      <RuntField label="Acreedor (RUNT)" value={summary.nombreAcreedor} />
                    </dl>

                    {summary.items.length > 0 && (
                      <ul className="space-y-2" aria-label="Prendas reportadas">
                        {summary.items.map((item, idx) => (
                          <li
                            key={`${item.idPrenda ?? 'p'}-${idx}`}
                            className="rounded-lg border px-3 py-2"
                          >
                            <p className="mb-2 text-[10px] font-bold uppercase opacity-55">
                              Prenda {item.idPrenda ? `#${item.idPrenda}` : idx + 1}
                            </p>
                            <dl className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                              <RuntField label="Acreedor" value={item.acreedor} />
                              <RuntField
                                label="Documento acreedor"
                                value={
                                  item.tipoDocumentoAcreedor && item.documentoAcreedor
                                    ? `${item.tipoDocumentoAcreedor} ${item.documentoAcreedor}`
                                    : item.documentoAcreedor
                                }
                              />
                              <RuntField
                                label="Fecha de inscripción"
                                value={
                                  item.fechaInscripcion
                                    ? formatDateOnly(item.fechaInscripcion) || item.fechaInscripcion
                                    : null
                                }
                              />
                              <RuntField label="Estado" value={item.estado} />
                              <RuntField label="Id prenda" value={item.idPrenda} />
                            </dl>
                          </li>
                        ))}
                      </ul>
                    )}

                    {!hasRuntAcreedorDetail(summary) && (
                      <p className="opacity-70">
                        El RUNT marcó gravamen o prenda, pero esta consulta no trajo el detalle del
                        acreedor (puede hacer falta volver a consultar el vehículo). Declara la
                        decisión abajo con la información que tengas.
                      </p>
                    )}
                  </>
                ) : (
                  <p className="opacity-70">
                    El RUNT marcó gravamen o prenda, pero no entregó el detalle del acreedor en esta
                    consulta. Declara la decisión abajo con la información que tengas.
                  </p>
                )}
              </div>
            )}
          </div>
        </div>
      )}

      {error && (
        <div
          className="rounded-xl p-3 text-xs border mb-3 flex items-center justify-between gap-3"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          <span>{error}</span>
          <button type="button" onClick={() => setError(null)} className="font-bold" aria-label="Descartar error">
            ×
          </button>
        </div>
      )}

      <fieldset disabled={readOnly} className="contents">
        <div className="grid grid-cols-1 gap-4">
          <div>
            <p className="text-xs font-semibold mb-2">Decisión de prenda</p>
            <div
              className="flex flex-wrap gap-2"
              role="radiogroup"
              aria-label="Decisión de prenda"
            >
              {decisions.map((d) => (
                <label
                  key={d}
                  className="flex flex-1 min-w-[9rem] items-center gap-2 cursor-pointer rounded-xl border px-3 py-2.5 transition-colors hover:bg-[rgba(85,126,255,0.04)]"
                  style={decision === d ? { borderColor: '#557EFF', background: 'rgba(85,126,255,0.06)' } : undefined}
                >
                  <input
                    type="radio"
                    name="prenda-decision"
                    value={d}
                    checked={decision === d}
                    onChange={() => selectDecision(d)}
                    className="h-4 w-4 shrink-0 accent-[#557EFF] disabled:opacity-60"
                    disabled={readOnly}
                  />
                  <span className="text-xs font-medium leading-snug">{PRENDA_DECISION_LABELS[d]}</span>
                </label>
              ))}
            </div>
          </div>

          {capturaAcreedor && (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div>
                <label htmlFor="prenda-acreedor-nombre" className="text-xs font-semibold mb-1.5 block">
                  Acreedor (beneficiario)
                </label>
                <input
                  id="prenda-acreedor-nombre"
                  type="text"
                  value={acreedorNombre}
                  onChange={(e) => setAcreedorNombre(e.target.value)}
                  placeholder="Ej. Banco XYZ"
                  className={INPUT_BASE}
                />
              </div>
              <div>
                <label htmlFor="prenda-acreedor-doc" className="text-xs font-semibold mb-1.5 block">
                  NIT / documento del acreedor
                </label>
                <input
                  id="prenda-acreedor-doc"
                  type="text"
                  inputMode="numeric"
                  pattern="[0-9]*"
                  autoComplete="off"
                  value={acreedorDocumento}
                  onChange={(e) => setAcreedorDocumento(digitsOnly(e.target.value))}
                  className={INPUT_BASE}
                />
              </div>
            </div>
          )}

          {requiereDocumento && decision && prendaDocTipoFor(decision) && (
            <PrendaDocumentUpload
              instanceId={instanceId}
              decision={decision}
              docTipo={prendaDocTipoFor(decision)!}
              modalidad={modalidad}
              documentRequired={documentRequired}
              onSatisfiedChange={setDocSatisfied}
              onChanged={onSaved}
            />
          )}
        </div>
      </fieldset>

      {!readOnly && !embeddedInWizard && (
        <div className="flex items-center justify-between gap-3 mt-4">
          {saved ? (
            <span className="text-[11px] font-semibold" style={{ color: '#8CC63F' }} role="status" aria-live="polite">
              Decisión de prenda guardada ✓
            </span>
          ) : (
            <span className="text-[11px] opacity-50">{loading ? 'Cargando…' : ''}</span>
          )}
          <button
            type="submit"
            disabled={saving}
            className="px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
            style={{ background: '#557EFF' }}
          >
            {saving ? 'Guardando…' : 'Guardar decisión de prenda'}
          </button>
        </div>
      )}
    </form>
  );
});
