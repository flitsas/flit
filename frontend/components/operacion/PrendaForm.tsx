'use client';

import { forwardRef, useEffect, useId, useImperativeHandle, useState } from 'react';
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
import { WIZARD_INPUT, WIZARD_CARD, WIZARD_CTA_GRADIENT } from './wizard-field-styles';
import { WizardCardHeader, WizardSegmented } from './wizard-atoms';

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

/**
 * Decisiones que muestran la sección de acreedor (PDF ajuste P0):
 * `levantar` también la muestra, pero con los campos inhabilitados.
 */
const MUESTRA_ACREEDOR: ReadonlySet<PrendaDecision> = new Set<PrendaDecision>([
  'solicitar',
  'registrar',
  'levantar',
]);

/** En matrícula la prenda es declarativa: registrar o sin prenda. */
const MATRICULA_DECISIONS: PrendaDecision[] = ['registrar', 'sin_prenda'];

/**
 * Decisiones que ofrece el traspaso (R10, HU #10598).
 *
 * CF-06 (HU #10881): `omitir` —"asumo el riesgo"— desaparece cuando el organismo exige el
 * certificado de prenda, porque ahí el riesgo no es del gestor sino una regla del OT. Ofrecerla
 * llevaba a guardar una decisión que satisfacía los dos gates sin el certificado, dejando la regla
 * del organismo evadible; el PUT de prenda la rechaza con el mismo criterio, así que esta lista
 * evita que el gestor llegue a intentarlo. Vive aquí, junto a las etiquetas, para que la regla y la
 * UI que la aplica no se separen.
 */
export function traspasoDecisions(documentRequired: boolean): PrendaDecision[] {
  return documentRequired
    ? ['solicitar', 'registrar', 'levantar']
    : ['solicitar', 'registrar', 'levantar', 'omitir'];
}

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

const INPUT_BASE = WIZARD_INPUT;

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
      <dt className="text-xs font-bold uppercase opacity-70">{label}</dt>
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
  const runtDetailId = useId();
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
  /** PDF ajuste P0: levantar muestra acreedor/doc pero inhabilitados (NO editable, no oculto). */
  const muestraAcreedor = decision !== '' && MUESTRA_ACREEDOR.has(decision);
  const acreedorReadOnly = decision === 'levantar';
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

  /**
   * `WizardSegmented`/`WizardSelectCards` trabajan con un tipo cerrado (nunca ''): el asistente
   * arranca sin decisión y ninguna opción queda marcada, así que el wrapper descarta la cadena
   * vacía antes de llegar a `selectDecision`.
   */
  const handleDecisionChange = (d: PrendaDecision | '') => {
    if (d) selectDecision(d);
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
      className={WIZARD_CARD}
      aria-label="Prenda o gravamen del trámite"
      noValidate
    >
      {!hideHeader && (
        <WizardCardHeader
          title="Asignación de Prenda / Limitación a la Propiedad"
          subtitle="Declara si el vehículo tiene prenda. Si registras, solicitas o levantas, adjunta el certificado en esta sección."
        />
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
              style={{ background: 'rgba(245,158,11,0.08)', color: 'var(--badge-warning-fg)' }}
              aria-expanded={runtOpen}
              aria-controls={runtDetailId}
            >
              <span>Ver información de prenda / gravamen del RUNT</span>
              <ChevronDown
                className={`h-4 w-4 shrink-0 transition-transform ${runtOpen ? 'rotate-180' : ''}`}
                aria-hidden
              />
            </button>
            {runtOpen && (
              <div
                id={runtDetailId}
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
                            <p className="mb-2 text-xs font-bold uppercase opacity-70">
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

      {/* Item 3 (guardián de diseño): el estado de carga solo se pintaba fuera del wizard —dentro
          los controles aparecían vacíos y el gestor podía creer que no había decisión guardada y
          pisarla con otra. Se pinta aquí, antes del fieldset, para los dos modos; fuera del
          wizard la barra inferior ya cubre el aviso y no hace falta duplicarlo. */}
      {embeddedInWizard && loading && (
        <p className="mb-3 text-xs opacity-70">Cargando la decisión de prenda guardada…</p>
      )}

      <fieldset disabled={readOnly} className="contents">
        <div className="grid grid-cols-1 gap-4">
          {/* PDF ajuste P0: traspaso → select + Acreedor + documento en la misma fila.
              P1.10: PrendaDocumentUpload visualmente acoplado en la misma grilla (span-3). */}
          {decisions.length > 2 ? (
            <div className={`grid grid-cols-1 gap-4 ${muestraAcreedor ? 'md:grid-cols-3' : ''}`}>
              <div className="min-w-0 md:self-end">
                <label htmlFor="prenda-decision-select" className="text-xs font-semibold mb-1.5 block">
                  ¿Al vehículo se le asociará una prenda?
                </label>
                <select
                  id="prenda-decision-select"
                  value={decision}
                  onChange={(e) => handleDecisionChange(e.target.value as PrendaDecision | '')}
                  disabled={readOnly}
                  className={`${INPUT_BASE} disabled:opacity-60`}
                >
                  <option value="">Seleccionar…</option>
                  {decisions.map((d) => (
                    <option key={d} value={d}>{PRENDA_DECISION_LABELS[d]}</option>
                  ))}
                </select>
              </div>
              {muestraAcreedor && (
                <>
                  <div className="min-w-0 md:self-end">
                    <label htmlFor="prenda-acreedor-nombre" className="text-xs font-semibold mb-1.5 block">
                      Acreedor (beneficiario)
                    </label>
                    <input
                      id="prenda-acreedor-nombre"
                      type="text"
                      value={acreedorNombre}
                      onChange={(e) => { if (!acreedorReadOnly) setAcreedorNombre(e.target.value); }}
                      readOnly={acreedorReadOnly}
                      disabled={acreedorReadOnly}
                      placeholder="Ej. Banco XYZ"
                      className={`${INPUT_BASE}${acreedorReadOnly ? ' opacity-70' : ''}`}
                      style={acreedorReadOnly ? { background: 'rgba(223,229,237,0.35)' } : undefined}
                    />
                  </div>
                  <div className="min-w-0 md:self-end">
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
                      onChange={(e) => { if (!acreedorReadOnly) setAcreedorDocumento(digitsOnly(e.target.value)); }}
                      readOnly={acreedorReadOnly}
                      disabled={acreedorReadOnly}
                      className={`${INPUT_BASE}${acreedorReadOnly ? ' opacity-70' : ''}`}
                      style={acreedorReadOnly ? { background: 'rgba(223,229,237,0.35)' } : undefined}
                    />
                  </div>
                </>
              )}
              {acreedorReadOnly && (
                <p className="md:col-span-3 text-xs opacity-70 -mt-2">
                  Al levantar el gravamen, Acreedor y documento quedan inhabilitados.
                </p>
              )}
              {/* P1.10: upload acoplado en la misma grilla, span completo */}
              {requiereDocumento && decision && prendaDocTipoFor(decision) && (
                <div className={muestraAcreedor ? 'md:col-span-3' : undefined}>
                  <PrendaDocumentUpload
                    instanceId={instanceId}
                    decision={decision}
                    docTipo={prendaDocTipoFor(decision)!}
                    modalidad={modalidad}
                    documentRequired={documentRequired}
                    onSatisfiedChange={setDocSatisfied}
                    onChanged={onSaved}
                  />
                </div>
              )}
            </div>
          ) : (
            <>
              <WizardSegmented<PrendaDecision | ''>
                label="¿Al vehículo se le asociará una prenda?"
                value={decision}
                onChange={handleDecisionChange}
                disabled={readOnly}
                options={decisions.map((d) => ({ value: d, label: PRENDA_DECISION_LABELS[d] }))}
              />
              {muestraAcreedor && (
                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                  <div>
                    <label htmlFor="prenda-acreedor-nombre" className="text-xs font-semibold mb-1.5 block">
                      Acreedor (beneficiario)
                    </label>
                    <input
                      id="prenda-acreedor-nombre"
                      type="text"
                      value={acreedorNombre}
                      onChange={(e) => { if (!acreedorReadOnly) setAcreedorNombre(e.target.value); }}
                      readOnly={acreedorReadOnly}
                      disabled={acreedorReadOnly}
                      placeholder="Ej. Banco XYZ"
                      className={`${INPUT_BASE}${acreedorReadOnly ? ' opacity-70' : ''}`}
                      style={acreedorReadOnly ? { background: 'rgba(223,229,237,0.35)' } : undefined}
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
                      onChange={(e) => { if (!acreedorReadOnly) setAcreedorDocumento(digitsOnly(e.target.value)); }}
                      readOnly={acreedorReadOnly}
                      disabled={acreedorReadOnly}
                      className={`${INPUT_BASE}${acreedorReadOnly ? ' opacity-70' : ''}`}
                      style={acreedorReadOnly ? { background: 'rgba(223,229,237,0.35)' } : undefined}
                    />
                  </div>
                </div>
              )}
            </>
          )}

          {/* Matrícula (2 decisiones): upload fuera del segmentado, a ancho completo */}
          {decisions.length <= 2 && requiereDocumento && decision && prendaDocTipoFor(decision) && (
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
            <span className="text-xs font-semibold" style={{ color: 'var(--flit-success-ink)' }} role="status" aria-live="polite">
              Decisión de prenda guardada ✓
            </span>
          ) : (
            <span className="text-xs opacity-70">{loading ? 'Cargando…' : ''}</span>
          )}
          <button
            type="submit"
            disabled={saving}
            className="px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
            style={{ background: WIZARD_CTA_GRADIENT }}
          >
            {saving ? 'Guardando…' : 'Guardar decisión de prenda'}
          </button>
        </div>
      )}
    </form>
  );
});
