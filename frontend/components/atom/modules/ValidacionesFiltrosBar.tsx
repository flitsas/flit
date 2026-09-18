'use client';

import { useId, useRef, useState, type ReactNode } from 'react';
import { ChevronDown, Search } from 'lucide-react';
import {
  Field,
  INPUT_CLS,
  PeriodoPopover,
  POPOVER_SURFACE_CLS,
  usePopoverDismiss,
} from '@/components/operacion/TramitesFiltrosBar';
import { controlCls } from '@/components/operacion/tramites-control-styles';
import { WIZARD_CTA_GRADIENT } from '@/components/operacion/wizard-field-styles';
import {
  IDENTITY_FILTER_FIELDS,
  describeIdentityFilter,
  validateIdentityFilter,
  type IdentityFilterCondition,
  type IdentityFilterField,
} from '@/lib/identidad/validaciones-filtros';
import { SEARCH_TEXT_MAX_LENGTH } from '@/lib/validation/fieldRules';

/**
 * HU #12707 — barra de filtros de Validación de Identidad con la forma de la de Trámites, en una línea:
 * alcance (compañía del SuperAdmin o red de la cabeza, si aplica) · buscador «Nombre o número de
 * documento» · Periodo · + Filtro · Columnas. Reemplaza el panel desplegable de nueve campos.
 *
 * Reutiliza de Trámites el popover de Periodo, los estilos de control y de popover y el cierre por
 * Escape / clic fuera. «+ Filtro» sigue la misma estructura que el de Trámites (lista de campos → editor
 * con «Volver» → pie Aplicar / Empezar de cero), con editores propios porque aquí hay un rango de fechas
 * y un número de días que la gramática de Consultas no modela.
 *
 * Puramente presentacional: el borrador y lo aplicado viven en `Validaciones`.
 */

interface IdentityFiltroPopoverProps {
  /** Condiciones en BORRADOR dentro del panel; se aplican al listado con «Aplicar». */
  condiciones: IdentityFilterCondition[];
  onCondicionesChange: (next: IdentityFilterCondition[]) => void;
  /** Cuántas hay APLICADAS: numera y colorea el disparador. */
  aplicadasCount: number;
  onAplicar: () => void;
  onEmpezarDeCero: () => void;
}

function IdentityFiltroPopover({
  condiciones,
  onCondicionesChange,
  aplicadasCount,
  onAplicar,
  onEmpezarDeCero,
}: IdentityFiltroPopoverProps) {
  const [open, setOpen] = useState(false);
  const [editando, setEditando] = useState<IdentityFilterField | null>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const close = () => {
    setOpen(false);
    setEditando(null);
  };
  const panelRef = usePopoverDismiss(open, close, triggerRef);
  const panelId = useId();

  const upsert = (condicion: IdentityFilterCondition) => {
    onCondicionesChange([...condiciones.filter((c) => c.fieldId !== condicion.fieldId), condicion]);
    setEditando(null);
  };
  const quitar = (fieldId: string) => {
    onCondicionesChange(condiciones.filter((c) => c.fieldId !== fieldId));
    setEditando(null);
  };

  return (
    <div className="relative">
      <button
        ref={triggerRef}
        type="button"
        onClick={() => (open ? close() : setOpen(true))}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-controls={open ? panelId : undefined}
        className={controlCls(aplicadasCount > 0)}
      >
        {aplicadasCount > 0 ? `Filtros (${aplicadasCount})` : '+ Filtro'}
        <ChevronDown className="h-3.5 w-3.5" aria-hidden="true" />
      </button>
      {open ? (
        <div
          ref={panelRef}
          id={panelId}
          role="dialog"
          aria-label="Filtros de validaciones"
          className={`absolute right-0 top-full z-50 mt-2 w-[20rem] p-3 ${POPOVER_SURFACE_CLS}`}
        >
          {editando ? (
            <>
              <button
                type="button"
                onClick={() => setEditando(null)}
                className="mb-2 inline-flex items-center gap-1 text-xs font-semibold text-[#557EFF] hover:underline"
              >
                ← Volver a los filtros
              </button>
              <IdentityFilterEditor
                field={editando}
                value={condiciones.find((c) => c.fieldId === editando.id) ?? null}
                onApply={upsert}
                onRemove={() => quitar(editando.id)}
              />
            </>
          ) : (
            <>
              {condiciones.length > 0 ? (
                <div className="mb-2 flex flex-wrap gap-1.5 border-b border-[#DFE5ED] pb-2 dark:border-white/10">
                  {condiciones.map((c) => (
                    <span
                      key={c.fieldId}
                      className="inline-flex items-center gap-1 rounded-full border border-[#557EFF]/40 bg-[#557EFF]/10 py-1 pl-3 pr-1 text-xs font-semibold text-[#3355CC] dark:text-[#9DB5FF]"
                    >
                      <button
                        type="button"
                        onClick={() => setEditando(IDENTITY_FILTER_FIELDS.find((f) => f.id === c.fieldId) ?? null)}
                        className="max-w-[14rem] truncate"
                      >
                        {describeIdentityFilter(c)}
                      </button>
                      <button
                        type="button"
                        onClick={() => quitar(c.fieldId)}
                        aria-label={`Quitar filtro ${describeIdentityFilter(c)}`}
                        className="rounded-full px-1.5 text-sm leading-none opacity-60 hover:opacity-100"
                      >
                        ×
                      </button>
                    </span>
                  ))}
                </div>
              ) : null}
              <ul className="flex flex-col" aria-label="Campos para filtrar">
                {IDENTITY_FILTER_FIELDS.filter((f) => !condiciones.some((c) => c.fieldId === f.id)).map((f) => (
                  <li key={f.id}>
                    <button
                      type="button"
                      onClick={() => setEditando(f)}
                      className="w-full rounded-lg px-2 py-2 text-left text-xs font-semibold text-[#162744] transition hover:bg-[#EEF5FF] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] dark:text-white dark:hover:bg-white/10"
                    >
                      {f.label}
                    </button>
                  </li>
                ))}
              </ul>
            </>
          )}

          {/* Mismo criterio que Trámites: mientras se edita un campo, el pie del listado no existe (el
              editor trae su propio «Aplicar», que confirma LA CONDICIÓN). */}
          {editando ? null : (
            <div className="mt-2 flex items-center justify-end gap-2 border-t border-[#DFE5ED] pt-3 dark:border-white/10">
              <button
                type="button"
                onClick={() => {
                  onEmpezarDeCero();
                  close();
                }}
                disabled={condiciones.length === 0 && aplicadasCount === 0}
                className="rounded-xl border border-[#FF4E00] px-3 py-2 text-xs font-semibold text-[#C2410C] transition hover:bg-[#FF4E00]/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-40"
              >
                Empezar de cero
              </button>
              <button
                type="button"
                onClick={() => {
                  onAplicar();
                  close();
                }}
                className="rounded-xl px-3 py-2 text-xs font-semibold text-white transition hover:opacity-95 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
                style={{ background: WIZARD_CTA_GRADIENT }}
              >
                Aplicar
              </button>
            </div>
          )}
        </div>
      ) : null}
    </div>
  );
}

/**
 * Editor de UNA condición. Valida antes de aceptar (AC10): un valor inválido no se agrega, el control
 * muestra el motivo y, por tanto, no llega a hacerse ninguna petición.
 */
export function IdentityFilterEditor({
  field,
  value,
  onApply,
  onRemove,
}: {
  field: IdentityFilterField;
  value: IdentityFilterCondition | null;
  onApply: (condition: IdentityFilterCondition) => void;
  onRemove: () => void;
}) {
  const [values, setValues] = useState<string[]>(value?.values ?? (field.kind === 'rango-fecha' ? ['', ''] : ['']));
  const [error, setError] = useState<string | null>(null);
  const errorId = useId();

  const aplicar = () => {
    const motivo = validateIdentityFilter(field.id, values);
    if (motivo) {
      setError(motivo);
      return;
    }
    onApply({ fieldId: field.id, values: field.kind === 'numero' ? [values[0].trim()] : values });
  };

  const invalid = error !== null;
  const describedBy = invalid ? errorId : undefined;

  return (
    <div className="text-left" data-testid={`identidad-filtro-editor-${field.id}`}>
      <p className="mb-2 text-xs font-semibold text-[#0B1F33] dark:text-white">{field.label}</p>
      {field.hint ? <p className="mb-2 text-[11px] text-[#59677D] dark:text-white/60">{field.hint}</p> : null}

      {field.kind === 'opcion' ? (
        <div role="radiogroup" aria-label={field.label} className="flex flex-col gap-1">
          {field.options.map((o) => (
            <label key={o.value} className="flex items-center gap-2 rounded-lg px-1.5 py-1 text-xs hover:bg-[#EEF5FF] dark:hover:bg-white/10">
              <input
                type="radio"
                name={`identidad-filtro-${field.id}`}
                value={o.value}
                checked={values[0] === o.value}
                onChange={() => {
                  setValues([o.value]);
                  setError(null);
                }}
              />
              {o.label}
            </label>
          ))}
        </div>
      ) : null}

      {field.kind === 'numero' ? (
        <Field label="Días">
          <input
            type="text"
            inputMode="numeric"
            value={values[0]}
            onChange={(e) => {
              setValues([e.target.value]);
              setError(null);
            }}
            aria-invalid={invalid}
            aria-describedby={describedBy}
            placeholder="7"
            className={INPUT_CLS}
          />
        </Field>
      ) : null}

      {field.kind === 'rango-fecha' ? (
        <div className="flex flex-col gap-2">
          <Field label="Desde">
            <input
              type="date"
              value={values[0]}
              onChange={(e) => {
                setValues([e.target.value, values[1] ?? '']);
                setError(null);
              }}
              aria-invalid={invalid}
              aria-describedby={describedBy}
              className={INPUT_CLS}
            />
          </Field>
          <Field label="Hasta">
            <input
              type="date"
              value={values[1] ?? ''}
              onChange={(e) => {
                setValues([values[0] ?? '', e.target.value]);
                setError(null);
              }}
              aria-invalid={invalid}
              aria-describedby={describedBy}
              className={INPUT_CLS}
            />
          </Field>
        </div>
      ) : null}

      {error ? (
        <p id={errorId} role="alert" className="mt-2 text-xs font-medium text-[#C2410C]">
          {error}
        </p>
      ) : null}

      <div className="mt-3 flex items-center justify-between gap-2">
        {value ? (
          <button
            type="button"
            onClick={onRemove}
            className="rounded-xl px-3 py-2 text-xs font-semibold text-[#59677D] transition hover:bg-[#DFE5ED]/40"
          >
            Quitar
          </button>
        ) : (
          <span />
        )}
        <button
          type="button"
          onClick={aplicar}
          className="rounded-xl px-3 py-2 text-xs font-semibold text-white transition hover:opacity-95 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
          style={{ background: WIZARD_CTA_GRADIENT }}
        >
          Agregar filtro
        </button>
      </div>
    </div>
  );
}

export interface ValidacionesFiltrosBarProps {
  /** Selector de alcance ya montado (compañía del SuperAdmin o red de la cabeza); `null` si no aplica. */
  scopeSelector?: ReactNode;
  search: string;
  onSearchChange: (v: string) => void;
  periodo: string;
  onPeriodoChange: (v: string) => void;
  rangoPropioDesde: string;
  rangoPropioHasta: string;
  onRangoPropioDesdeChange: (v: string) => void;
  onRangoPropioHastaChange: (v: string) => void;
  draftCondiciones: IdentityFilterCondition[];
  onDraftCondicionesChange: (next: IdentityFilterCondition[]) => void;
  condicionesAplicadasCount: number;
  onAplicar: () => void;
  onEmpezarDeCero: () => void;
  /** `ColumnSelector` ya montado por el contenedor. */
  columnSelector: ReactNode;
}

export function ValidacionesFiltrosBar({
  scopeSelector,
  search,
  onSearchChange,
  periodo,
  onPeriodoChange,
  rangoPropioDesde,
  rangoPropioHasta,
  onRangoPropioDesdeChange,
  onRangoPropioHastaChange,
  draftCondiciones,
  onDraftCondicionesChange,
  condicionesAplicadasCount,
  onAplicar,
  onEmpezarDeCero,
  columnSelector,
}: ValidacionesFiltrosBarProps) {
  return (
    <div className="flex flex-wrap items-center gap-2" role="search" aria-label="Filtros de validaciones">
      {scopeSelector}
      <div className="relative w-64 shrink-0">
        <Search
          className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[#557EFF]"
          aria-hidden="true"
        />
        <input
          type="search"
          aria-label="Buscar validaciones por nombre o documento"
          value={search}
          onChange={(e) => onSearchChange(e.target.value)}
          placeholder="Nombre o número de documento"
          maxLength={SEARCH_TEXT_MAX_LENGTH}
          className={`${INPUT_CLS} w-full pl-9`}
        />
      </div>
      <PeriodoPopover
        periodo={periodo}
        onPeriodoChange={onPeriodoChange}
        rangoPropioDesde={rangoPropioDesde}
        rangoPropioHasta={rangoPropioHasta}
        onRangoPropioDesdeChange={onRangoPropioDesdeChange}
        onRangoPropioHastaChange={onRangoPropioHastaChange}
        onAplicar={onAplicar}
      />
      <IdentityFiltroPopover
        condiciones={draftCondiciones}
        onCondicionesChange={onDraftCondicionesChange}
        aplicadasCount={condicionesAplicadasCount}
        onAplicar={onAplicar}
        onEmpezarDeCero={onEmpezarDeCero}
      />
      {columnSelector}
    </div>
  );
}
