'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { ChevronDown, Info } from 'lucide-react';
import type { ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';
import { useTiposHabilitados } from '@/hooks/useTiposHabilitados';
import {
  TIPOS_UI_MOCKUP,
  infoTextNuevoTramite,
  resolveNuevoTramiteCode,
  type FamiliasBloqueadasResolver,
  type ModalidadTraspasoUi,
  type NuevoTramiteTipoUi,
} from '@/lib/tramites/nuevo-tramite-resolver';
import { WIZARD_CTA_GRADIENT } from './wizard-field-styles';

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

/** Una opción del desplegable de una tarjeta: lo que se ve y el valor que fija. */
interface OpcionTarjeta {
  value: string;
  label: string;
}

/**
 * Desplegable de la tarjeta. No es un `<select>` nativo: el diseño lo dibuja como un botón con su
 * panel y un chevron que gira, y eso un `<select>` no lo permite.
 *
 * El panel se pinta en un PORTAL a `document.body`, con posición fija calculada desde el botón. No
 * es un capricho: el cuerpo del `Modal` es `overflow-y-auto`, y eso recorta cualquier descendiente
 * absoluto — la lista de "Otros trámites" (quince tipos) se cortaba contra el borde del diálogo y
 * solo se veían las cuatro primeras opciones. Sacarla del contenedor con scroll es lo único que
 * evita el recorte sin renunciar al scroll del modal.
 *
 * Al ir en un portal, el panel deja de ser hijo del botón en el DOM: por eso el cierre no puede
 * apoyarse en `onBlur` y se resuelve con un `mousedown` de documento que mira los dos nodos.
 */
function TarjetaSelect({
  id,
  value,
  placeholder,
  options,
  onChange,
  disabled,
  ariaLabel,
}: {
  id: string;
  value: string;
  placeholder: string;
  options: OpcionTarjeta[];
  onChange: (v: string) => void;
  disabled?: boolean;
  ariaLabel: string;
}) {
  const [open, setOpen] = useState(false);
  const [rect, setRect] = useState<{ left: number; top: number; width: number } | null>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const elegida = options.find((o) => o.value === value);

  /** Alto máximo del panel; si no cabe debajo del botón, se abre hacia arriba. */
  const PANEL_MAX_H = 224;

  const recalcular = useCallback(() => {
    const el = triggerRef.current;
    if (!el) return;
    const r = el.getBoundingClientRect();
    const espacioAbajo = window.innerHeight - r.bottom;
    const arriba = espacioAbajo < PANEL_MAX_H && r.top > espacioAbajo;
    setRect({
      left: r.left,
      top: arriba ? Math.max(8, r.top - Math.min(PANEL_MAX_H, r.top - 8) - 4) : r.bottom + 4,
      width: r.width,
    });
  }, []);

  useEffect(() => {
    if (!open) return;
    recalcular();

    // `capture` para enterarse también del scroll del cuerpo del modal, que no burbujea.
    const onScrollOrResize = () => recalcular();
    window.addEventListener('scroll', onScrollOrResize, true);
    window.addEventListener('resize', onScrollOrResize);

    const onPointer = (e: MouseEvent) => {
      const target = e.target as Node;
      if (triggerRef.current?.contains(target) || panelRef.current?.contains(target)) return;
      setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setOpen(false);
        triggerRef.current?.focus();
      }
    };
    document.addEventListener('mousedown', onPointer);
    document.addEventListener('keydown', onKey);

    return () => {
      window.removeEventListener('scroll', onScrollOrResize, true);
      window.removeEventListener('resize', onScrollOrResize);
      document.removeEventListener('mousedown', onPointer);
      document.removeEventListener('keydown', onKey);
    };
  }, [open, recalcular]);

  return (
    <div className="mt-4">
      <button
        ref={triggerRef}
        type="button"
        id={id}
        disabled={disabled}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-label={ariaLabel}
        onClick={() => setOpen((o) => !o)}
        className="flex w-full items-center justify-between rounded-xl border bg-white px-3 py-2.5 text-left text-[13px] outline-none transition focus:border-[#557EFF] focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50 dark:bg-[#0B0F14]"
        style={{ borderColor: BORDER, color: elegida ? '#162744' : '#94A3B8' }}
      >
        <span className="truncate pr-2">{elegida?.label ?? placeholder}</span>
        <ChevronDown
          className={`h-4 w-4 shrink-0 text-slate-400 transition ${open ? 'rotate-180' : ''}`}
          aria-hidden="true"
        />
      </button>

      {open && typeof document !== 'undefined'
        ? createPortal(
            <div
              ref={panelRef}
              role="listbox"
              aria-label={ariaLabel}
              // z por encima del overlay del Modal (z-[100]): si no, el panel queda detrás.
              className="fixed z-[1200] overflow-y-auto rounded-xl border bg-white shadow-lg dark:bg-[#162744]"
              style={{
                borderColor: BORDER,
                left: rect?.left ?? -9999,
                top: rect?.top ?? -9999,
                width: rect?.width,
                maxHeight: PANEL_MAX_H,
              }}
            >
              {options.map((o) => {
                const on = o.value === value;
                return (
                  <button
                    key={o.value}
                    type="button"
                    role="option"
                    aria-selected={on}
                    onClick={() => {
                      onChange(o.value);
                      setOpen(false);
                    }}
                    className={`block w-full px-3 py-2 text-left text-[13px] transition ${
                      on
                        ? 'bg-[#EEF5FF] font-semibold text-[#162744]'
                        : 'text-[#475569] hover:bg-[#EEF5FF] hover:text-[#162744] dark:text-white/80'
                    }`}
                  >
                    {o.label}
                  </button>
                );
              })}
            </div>,
            document.body,
          )
        : null}
    </div>
  );
}

/**
 * Selector «Nuevo trámite» (repo de diseño flit-2.0, bloque `selector` de `Tramites.tsx`): tres
 * tarjetas con ilustración, la configuración DENTRO de cada una, franja informativa y footer
 * Cancelar + Iniciar. Resuelve el `procedureTypeCode` contra el catálogo (ADR-0050) sin tocar BE.
 *
 * La configuración vive dentro de la tarjeta y no debajo: elegir familia y configurarla son el
 * mismo gesto, y así el alto del modal no cambia al seleccionar — antes aparecía un bloque debajo y
 * el diálogo daba un salto.
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

  /**
   * Elegir en el desplegable de una tarjeta SELECCIONA esa familia y fija su configuración de una
   * vez, y limpia la de las otras dos: si no, quedaba un leasing marcado en una matrícula que ya no
   * se está creando y viajaba al resolver.
   */
  const elegirEnTarjeta = (id: NuevoTramiteTipoUi, value: string) => {
    if (estaBloqueada(id) || !tieneTipos(id)) return;
    setTipo(id);
    setResolveError(null);
    setLeasing(id === 'MATRICULAS' ? value === 'leasing' : false);
    setModalidad(id === 'TRASPASO' ? (value as ModalidadTraspasoUi) : 'bilateral');
    setSubtipoOtros(id === 'OTROS' ? value : '');
  };

  /** Valor mostrado en el desplegable de cada tarjeta; vacío si esa familia no es la elegida. */
  const valorDe = (id: NuevoTramiteTipoUi): string => {
    if (tipo !== id) return '';
    if (id === 'MATRICULAS') return leasing ? 'leasing' : 'tradicional';
    if (id === 'TRASPASO') return modalidad;
    return subtipoOtros;
  };

  const opcionesDe = (id: NuevoTramiteTipoUi): OpcionTarjeta[] => {
    if (id === 'MATRICULAS') {
      return [
        { value: 'tradicional', label: 'Matrícula Tradicional' },
        { value: 'leasing', label: 'Matrícula Leasing' },
      ];
    }
    if (id === 'TRASPASO') {
      return [
        { value: 'bilateral', label: 'Traspaso Bilateral' },
        { value: 'unilateral', label: 'Traspaso Unilateral' },
      ];
    }
    return tiposOtros.map((t) => ({ value: t.code, label: t.name }));
  };

  const infoText = infoTextNuevoTramite(tipo, {
    leasing,
    modalidadTraspaso: modalidad,
  });

  const puedeIniciar =
    tipo !== null && !estaBloqueada(tipo) && (tipo !== 'OTROS' || subtipoOtros.length > 0);

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
    <div className="flex min-w-0 flex-col">
      {tituloEnContenedor ? null : (
        <header className="mb-5">
          <h2 className="text-[22px] font-bold leading-tight" style={{ color: BLUE }}>
            Nuevo trámite
          </h2>
          <p className="mt-1.5 text-[13.5px] text-[#59677D]">
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
          const inhabilitada = blocked || noFamilia;

          return (
            <div
              key={t.id}
              className={`rounded-2xl border-2 p-5 transition ${on ? 'bg-blue-50/50' : 'bg-white dark:bg-[#162744]'}`}
              style={{
                borderColor: on ? BLUE : BORDER,
                opacity: inhabilitada ? 0.55 : 1,
              }}
            >
              {/* Aro claro detrás del icono: el círculo azul macizo ya viene dentro del SVG. */}
              <div className="mx-auto grid h-[120px] w-[120px] place-items-center rounded-full bg-[#EFF6FF]">
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img src={t.icon} alt="" aria-hidden="true" className="h-[72px] w-[72px]" />
              </div>

              <h3 className="mt-4 text-center text-[15px] font-bold text-[#1E3A8A] dark:text-white">
                {t.title}
              </h3>

              <TarjetaSelect
                id={`nuevo-tramite-${t.id.toLowerCase()}`}
                value={valorDe(t.id)}
                placeholder={t.placeholder}
                options={opcionesDe(t.id)}
                onChange={(v) => elegirEnTarjeta(t.id, v)}
                disabled={inhabilitada}
                ariaLabel={`${t.title}: ${t.placeholder}`}
              />

              {inhabilitada ? (
                <p className="mt-2 text-center text-[11px] text-[#59677D]">
                  {blocked ? 'No habilitado para tu compañía' : 'Sin tipos habilitados'}
                </p>
              ) : null}
            </div>
          );
        })}
      </div>

      {/*
        La franja se reserva SIEMPRE, con `invisible` cuando no hay nada que decir: si se montara y
        desmontara, el modal daría un salto de 56px justo al elegir una tarjeta — el momento en que
        el gestor está mirando.
      */}
      <div
        className={`mt-4 flex min-h-[56px] w-full items-start gap-2.5 rounded-lg bg-[#F4F7FF] p-3.5 dark:bg-white/5 ${
          infoText ? '' : 'invisible'
        }`}
        role="status"
        aria-live="polite"
      >
        <Info className="mt-0.5 h-5 w-5 shrink-0" style={{ color: BLUE }} aria-hidden="true" />
        <p className="text-[13.5px] leading-snug text-[#59677D] dark:text-white/70">{infoText}</p>
      </div>

      {resolveError ? (
        <p className="mt-3 text-xs font-medium" style={{ color: '#C2410C' }} role="alert">
          {resolveError}
        </p>
      ) : null}

      <div className="mt-6 flex flex-wrap items-center justify-center gap-4">
        {onCancelar ? (
          <button
            type="button"
            onClick={onCancelar}
            className="min-w-[160px] rounded-xl px-6 py-2.5 text-[13px] font-semibold text-white transition hover:opacity-90 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#FF4E00] focus-visible:ring-offset-2"
            style={{ background: ALERT }}
          >
            Cancelar
          </button>
        ) : null}
        <button
          type="button"
          onClick={iniciar}
          disabled={!puedeIniciar}
          className="min-w-[160px] rounded-xl px-6 py-2.5 text-[13px] font-semibold text-white shadow-md transition hover:opacity-90 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-40"
          style={{ background: WIZARD_CTA_GRADIENT }}
        >
          Iniciar trámite
        </button>
      </div>
    </div>
  );
}
