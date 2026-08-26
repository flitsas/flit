'use client';

import { useMemo, useState } from 'react';
import type { ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';
import { useTiposHabilitados } from '@/hooks/useTiposHabilitados';
import {
  TIPOS_UI_MOCKUP,
  TRANSFORMACIONES_MOCKUP,
  resolveNuevoTramiteCode,
  type FamiliasBloqueadasResolver,
  type ModalidadTraspasoUi,
  type NuevoTramiteTipoUi,
} from '@/lib/tramites/nuevo-tramite-resolver';
import { WIZARD_CTA_GRADIENT, WIZARD_SELECT } from './wizard-field-styles';

const BLUE = '#557EFF';
const BORDER = '#DFE5ED';
const ALERT = '#FF4E00';

export type { FamiliasBloqueadasResolver as FamiliasBloqueadas };

interface Props {
  /** Code del tipo a abrir en el asistente. */
  onElegir: (code: string) => void;
  onCancelar?: () => void;
  bloqueadas?: FamiliasBloqueadasResolver;
  /**
   * El contenedor (modal) ya pinta título/subtítulo. En página full se pinta cabecera interna.
   */
  tituloEnContenedor?: boolean;
}

/**
 * Selector «Nuevo trámite» del mockup flit-2.0: un paso con 3 tipos, config inline y footer
 * Cancelar + Iniciar. Resuelve el `procedureTypeCode` contra el catálogo (ADR-0050) sin tocar BE.
 */
export function NuevoTramiteModalContent({
  onElegir,
  onCancelar,
  bloqueadas,
  tituloEnContenedor = false,
}: Props) {
  const { familias, status, error, reload } = useTiposHabilitados();
  const [tipo, setTipo] = useState<NuevoTramiteTipoUi | null>(null);
  const [leasing, setLeasing] = useState(false);
  const [modalidad, setModalidad] = useState<ModalidadTraspasoUi>('bilateral');
  const [subtipoOtros, setSubtipoOtros] = useState('');
  const [transformaciones, setTransformaciones] = useState<string[]>([]);
  const [resolveError, setResolveError] = useState<string | null>(null);

  const tiposPlanos: ProcedureTypeSummary[] = useMemo(
    () => familias.flatMap((f) => f.tipos),
    [familias],
  );

  const tiposOtros = useMemo(
    () => familias.find((f) => f.family === 'OTROS')?.tipos ?? [],
    [familias],
  );

  const tieneTipos = (id: NuevoTramiteTipoUi) => familias.some((f) => f.family === id);

  const estaBloqueada = (id: NuevoTramiteTipoUi) => {
    if (id === 'MATRICULAS') return bloqueadas?.matriculas === true;
    if (id === 'TRASPASO') return bloqueadas?.traspaso === true;
    return bloqueadas?.otros === true;
  };

  const resetConfig = () => {
    setLeasing(false);
    setModalidad('bilateral');
    setSubtipoOtros('');
    setTransformaciones([]);
    setResolveError(null);
  };

  const elegirTipo = (id: NuevoTramiteTipoUi) => {
    if (estaBloqueada(id)) return;
    setTipo(id);
    resetConfig();
  };

  const toggleTransformacion = (id: string) => {
    setTransformaciones((prev) =>
      prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id],
    );
  };

  const puedeIniciar =
    tipo !== null &&
    !estaBloqueada(tipo) &&
    (tipo !== 'OTROS' || subtipoOtros.length > 0);

  const iniciar = () => {
    if (!tipo) return;
    const result = resolveNuevoTramiteCode(
      {
        tipo,
        leasing: tipo === 'MATRICULAS' ? leasing : undefined,
        modalidadTraspaso: tipo === 'TRASPASO' ? modalidad : undefined,
        subtipoOtrosCode: tipo === 'OTROS' ? subtipoOtros : undefined,
      },
      tiposPlanos,
      bloqueadas,
    );
    // transformaciones: UI mockup; el wizard las declara en paso 1 (handoff mínimo).
    void transformaciones;
    if (!result.ok) {
      setResolveError(result.message);
      return;
    }
    setResolveError(null);
    onElegir(result.procedureTypeCode);
  };

  if (status === 'loading') {
    return <p className="text-xs opacity-70">Cargando tipos de trámite…</p>;
  }

  if (status === 'error') {
    return (
      <div
        className="rounded-xl border p-4"
        style={{ borderColor: 'rgba(255,78,0,0.32)', background: 'rgba(255,78,0,0.12)' }}
        role="alert"
      >
        <p className="text-xs" style={{ color: '#C2410C' }}>
          {error}
        </p>
        <button
          type="button"
          onClick={() => void reload()}
          className="mt-2 text-xs font-semibold underline focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
          style={{ color: '#C2410C' }}
        >
          Reintentar
        </button>
      </div>
    );
  }

  if (familias.length === 0) {
    return (
      <p className="text-xs opacity-70">
        No hay tipos de trámite habilitados para crear. Comunícate con el administrador.
      </p>
    );
  }

  return (
    <div className="min-w-0">
      {tituloEnContenedor ? null : (
        <header className="mb-5">
          <h2 className="text-[22px] font-bold leading-tight" style={{ color: BLUE }}>
            Nuevo trámite
          </h2>
          <p className="mt-1.5 text-[13px] text-[#59677D]">
            Selecciona el trámite principal y completa su configuración. Al iniciar entrarás
            directamente al Paso 1.
          </p>
        </header>
      )}

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        {TIPOS_UI_MOCKUP.map((t) => {
          const on = tipo === t.id;
          const blocked = estaBloqueada(t.id);
          const noFamilia = !tieneTipos(t.id);

          return (
            <button
              key={t.id}
              type="button"
              disabled={blocked || noFamilia}
              aria-pressed={on}
              onClick={() => elegirTipo(t.id)}
              className={`relative w-full rounded-2xl border-2 p-4 text-left transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:cursor-not-allowed ${
                on ? 'bg-blue-50/50' : 'bg-white hover:bg-[#EEF5FF] dark:bg-[#162744]'
              }`}
              style={{
                borderColor: on ? BLUE : BORDER,
                boxShadow: on ? '0 6px 18px -6px rgba(85,126,255,.45)' : 'none',
                opacity: blocked || noFamilia ? 0.55 : 1,
              }}
            >
              <span
                className="absolute top-3 right-3 grid h-5 w-5 place-items-center rounded-full text-[11px] font-bold transition"
                style={{
                  background: on ? BLUE : BORDER,
                  color: on ? '#fff' : '#59677D',
                }}
                aria-hidden
              >
                ✓
              </span>
              <p className="pr-7 text-[14px] font-bold text-[#162744] dark:text-white">{t.title}</p>
              <p className="mt-1 text-xs text-[#59677D]">
                {blocked
                  ? `${t.subtitle} · no habilitado para tu compañía`
                  : noFamilia
                    ? `${t.subtitle} · sin tipos habilitados`
                    : t.subtitle}
              </p>
            </button>
          );
        })}
      </div>

      {tipo && !estaBloqueada(tipo) ? (
        <div className="mt-6 space-y-5">
          {tipo === 'MATRICULAS' && (
            <div
              className="flex items-center justify-between gap-4 rounded-2xl border p-4"
              style={{ borderColor: BORDER }}
            >
              <div className="min-w-0">
                <p className="text-[13px] font-bold text-[#162744] dark:text-white">
                  Matrícula tipo Leasing
                </p>
                <p className="mt-0.5 text-xs text-[#59677D]">
                  Activa esta opción si el vehículo se matricula bajo contrato de leasing.
                </p>
              </div>
              <button
                type="button"
                onClick={() => setLeasing((p) => !p)}
                aria-pressed={leasing}
                aria-label="Matrícula tipo Leasing"
                className="relative inline-block h-6 w-11 shrink-0 rounded-full transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
                style={{ background: leasing ? BLUE : '#CBD5E1' }}
              >
                <span
                  className="absolute top-0.5 h-5 w-5 rounded-full bg-white transition-all"
                  style={{ left: leasing ? 22 : 2 }}
                />
              </button>
            </div>
          )}

          {tipo === 'TRASPASO' && (
            <div>
              <p className="text-[13px] font-bold text-[#162744] dark:text-white">
                Modalidad del traspaso
              </p>
              <div className="mt-2 grid grid-cols-1 gap-3 sm:grid-cols-2">
                {(
                  [
                    { id: 'bilateral' as const, label: 'Traspaso Bilateral' },
                    { id: 'unilateral' as const, label: 'Traspaso Unilateral' },
                  ] as const
                ).map((m) => {
                  const on = modalidad === m.id;
                  return (
                    <button
                      key={m.id}
                      type="button"
                      aria-pressed={on}
                      onClick={() => {
                        setModalidad(m.id);
                        setResolveError(null);
                      }}
                      className={`rounded-xl border-2 px-4 py-3 text-left text-[13px] font-semibold transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 ${
                        on ? 'bg-blue-50/50' : 'bg-white hover:bg-[#EEF5FF] dark:bg-[#162744]'
                      }`}
                      style={{
                        borderColor: on ? BLUE : BORDER,
                        color: on ? BLUE : '#162744',
                      }}
                    >
                      {m.label}
                    </button>
                  );
                })}
              </div>
            </div>
          )}

          {tipo === 'OTROS' ? (
            <label className="block max-w-md">
              <span className="text-[13px] font-bold text-[#162744] dark:text-white">
                Trámite a realizar
              </span>
              <select
                value={subtipoOtros}
                onChange={(e) => {
                  setSubtipoOtros(e.target.value);
                  setResolveError(null);
                }}
                className={`mt-2 ${WIZARD_SELECT}`}
                style={{ borderColor: BORDER, color: '#162744' }}
              >
                <option value="">Selecciona el trámite</option>
                {tiposOtros.map((t) => (
                  <option key={t.code} value={t.code}>
                    {t.name}
                  </option>
                ))}
              </select>
              <span className="mt-1.5 block text-xs text-[#59677D]">
                Solo se puede radicar un trámite a la vez.
              </span>
            </label>
          ) : (
            <div>
              <p className="text-[13px] font-bold text-[#162744] dark:text-white">
                Transformaciones del vehículo (opcional)
              </p>
              <div className="mt-2 flex flex-wrap gap-2">
                {TRANSFORMACIONES_MOCKUP.map((s) => {
                  const on = transformaciones.includes(s.id);
                  return (
                    <button
                      key={s.id}
                      type="button"
                      onClick={() => toggleTransformacion(s.id)}
                      className="rounded-full border-2 px-3.5 py-1.5 text-xs font-semibold transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
                      style={{
                        background: on ? BLUE : '#fff',
                        color: on ? '#fff' : '#162744',
                        borderColor: on ? BLUE : BORDER,
                      }}
                    >
                      {s.label}
                    </button>
                  );
                })}
              </div>
            </div>
          )}
        </div>
      ) : null}

      {resolveError ? (
        <p className="mt-4 text-xs font-medium" style={{ color: '#C2410C' }} role="alert">
          {resolveError}
        </p>
      ) : null}

      <div className="mt-7 flex flex-wrap items-center justify-center gap-4">
        {onCancelar ? (
          <button
            type="button"
            onClick={onCancelar}
            className="rounded-xl px-5 py-2.5 text-[13px] font-semibold text-white transition hover:opacity-90 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#FF4E00] focus-visible:ring-offset-2"
            style={{ background: ALERT }}
          >
            Cancelar
          </button>
        ) : null}
        <button
          type="button"
          onClick={iniciar}
          disabled={!puedeIniciar}
          className="rounded-xl px-5 py-2.5 text-[13px] font-medium text-white shadow-md transition hover:opacity-90 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-40"
          style={{ background: WIZARD_CTA_GRADIENT }}
        >
          Iniciar trámite
        </button>
      </div>
    </div>
  );
}
