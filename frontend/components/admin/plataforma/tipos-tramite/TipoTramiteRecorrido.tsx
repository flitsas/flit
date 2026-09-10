'use client';

import { useState } from 'react';
import { ArrowDown, ArrowUp, Loader2, Trash2 } from 'lucide-react';
import { superadminClient } from '@/lib/api/superadmin-client';
import type { ProcedureStep, ProcedureStepInput } from '@/lib/api/types/procedure-parametrization';
import type { WizardSectionType } from '@/lib/api/types/procedure-runtime';

/**
 * Recorrido del tipo: los pasos del asistente y qué pinta cada uno.
 *
 * El `section_type` de la sección es lo que decide el cuerpo del paso en el asistente
 * (`SectionRendererRegistry`, CFD-09). No es una etiqueta: un paso con el tipo equivocado captura
 * datos que no corresponden, y uno sin tipo válido cae en el cuerpo genérico y no captura nada.
 *
 * El upsert conserva las secciones existentes por código, así que reordenar o renombrar no destruye
 * los campos ya configurados.
 */
const TIPOS_SECCION: { valor: WizardSectionType; etiqueta: string }[] = [
  { valor: 'vehicle_query', etiqueta: 'Consulta del vehículo' },
  { valor: 'actor_form', etiqueta: 'Captura de actores' },
  { valor: 'document_checklist', etiqueta: 'Checklist de documentos' },
  { valor: 'commercial', etiqueta: 'Datos comerciales' },
  { valor: 'prenda_decision', etiqueta: 'Decisión de prenda' },
  { valor: 'biometric', etiqueta: 'Validación de identidad' },
  { valor: 'signature_fur', etiqueta: 'Firma y FUR' },
  { valor: 'plate_request', etiqueta: 'Solicitud de placa' },
  { valor: 'generic_form', etiqueta: 'Formulario genérico' },
];

const CAMPO =
  'rounded-lg border px-2 py-1.5 text-xs border-[#DFE5ED] bg-white text-[#162744] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]';

export function TipoTramiteRecorrido({
  procedureTypeId,
  pasos,
  onGuardado,
}: {
  procedureTypeId: string;
  pasos: ProcedureStep[];
  onGuardado: () => void;
}) {
  const [borrador, setBorrador] = useState<ProcedureStep[]>(pasos);
  const [guardando, setGuardando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [ok, setOk] = useState(false);

  const tocar = (siguiente: ProcedureStep[]) => {
    setBorrador(siguiente);
    setOk(false);
  };

  const mover = (i: number, delta: number) => {
    const j = i + delta;
    if (j < 0 || j >= borrador.length) return;
    const copia = [...borrador];
    [copia[i], copia[j]] = [copia[j]!, copia[i]!];
    tocar(copia);
  };

  const eliminar = (i: number) => tocar(borrador.filter((_, k) => k !== i));

  const editarPaso = (i: number, parcial: Partial<ProcedureStep>) =>
    tocar(borrador.map((p, k) => (k === i ? { ...p, ...parcial } : p)));

  const editarTipoSeccion = (i: number, sectionType: WizardSectionType) =>
    tocar(
      borrador.map((p, k) =>
        k === i
          ? {
              ...p,
              sections:
                p.sections.length > 0
                  ? p.sections.map((s, si) => (si === 0 ? { ...s, sectionType } : s))
                  : [{ code: p.code, title: p.title, sortOrder: 1, sectionType, formFields: [] }],
            }
          : p,
      ),
    );

  const guardar = async () => {
    setGuardando(true);
    setError(null);
    setOk(false);
    try {
      const payload: ProcedureStepInput[] = borrador.map((p, i) => ({
        code: p.code.trim(),
        title: p.title.trim(),
        sortOrder: i + 1,
        isActive: p.isActive ?? true,
        sections: p.sections.map((s, si) => ({
          code: s.code,
          title: s.title,
          sortOrder: si + 1,
          layout: s.layout,
          sectionType: s.sectionType,
          formFields: [],
        })),
      }));
      await superadminClient.updateSteps(procedureTypeId, payload);
      onGuardado();
      setOk(true);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'No se pudo guardar el recorrido.');
    } finally {
      setGuardando(false);
    }
  };

  if (borrador.length === 0) {
    return (
      <div className="flex flex-col gap-3">
        <p className="text-xs opacity-70">
          Este tipo no tiene pasos parametrizados. Sin recorrido no puede habilitarse: el asistente
          mostraría un estado vacío y bloqueado.
        </p>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-3">
      <ol className="flex flex-col gap-2">
        {borrador.map((p, i) => (
          <li
            key={`${p.id ?? p.code}-${i}`}
            className="grid items-center gap-2 rounded-xl border px-3 py-2.5 border-[#DFE5ED] dark:border-white/10 sm:grid-cols-[2rem_1fr_1fr_1.4fr_auto]"
          >
            <span className="text-xs font-semibold tabular-nums opacity-60">{i + 1}</span>

            <input
              className={CAMPO}
              value={p.title}
              aria-label={`Título del paso ${i + 1}`}
              onChange={(e) => editarPaso(i, { title: e.target.value })}
            />

            <input
              className={`${CAMPO} font-mono`}
              value={p.code}
              aria-label={`Código del paso ${i + 1}`}
              onChange={(e) => editarPaso(i, { code: e.target.value })}
            />

            <select
              className={CAMPO}
              aria-label={`Qué pinta el paso ${i + 1}`}
              value={p.sections[0]?.sectionType ?? 'generic_form'}
              onChange={(e) => editarTipoSeccion(i, e.target.value as WizardSectionType)}
            >
              {TIPOS_SECCION.map((t) => (
                <option key={t.valor} value={t.valor}>
                  {t.etiqueta}
                </option>
              ))}
            </select>

            <span className="flex items-center gap-1">
              <IconoBoton label={`Subir el paso ${i + 1}`} onClick={() => mover(i, -1)} disabled={i === 0}>
                <ArrowUp className="h-3.5 w-3.5" aria-hidden="true" />
              </IconoBoton>
              <IconoBoton
                label={`Bajar el paso ${i + 1}`}
                onClick={() => mover(i, 1)}
                disabled={i === borrador.length - 1}
              >
                <ArrowDown className="h-3.5 w-3.5" aria-hidden="true" />
              </IconoBoton>
              <IconoBoton label={`Quitar el paso ${i + 1}`} onClick={() => eliminar(i)}>
                <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
              </IconoBoton>
            </span>
          </li>
        ))}
      </ol>

      <div className="flex items-center gap-3">
        <button
          type="button"
          onClick={() => void guardar()}
          disabled={guardando}
          className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-40 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
          style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
        >
          {guardando ? (
            <span className="inline-flex items-center gap-1.5">
              <Loader2 className="h-3 w-3 animate-spin" aria-hidden="true" />
              Guardando…
            </span>
          ) : (
            'Guardar recorrido'
          )}
        </button>
        {ok && (
          <span className="text-xs font-medium" style={{ color: '#0E9F6E' }} role="status">
            Guardado
          </span>
        )}
      </div>

      {error && (
        <p className="text-xs" role="alert" style={{ color: '#C2410C' }}>
          {error}
        </p>
      )}
    </div>
  );
}

function IconoBoton({
  label,
  onClick,
  disabled = false,
  children,
}: {
  label: string;
  onClick: () => void;
  disabled?: boolean;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      onClick={onClick}
      disabled={disabled}
      className="rounded-lg border p-1.5 border-[#DFE5ED] transition hover:bg-[#557EFF]/10 disabled:opacity-30 dark:border-white/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
      style={{ color: '#557EFF' }}
    >
      {children}
    </button>
  );
}
