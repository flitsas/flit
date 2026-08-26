'use client';

import { useEffect, useRef, useState, type ReactNode } from 'react';
import { ChevronDown } from 'lucide-react';
import type {
  BiometricEstado,
  BiometricVigenciaEstado,
} from '@/lib/api/types/procedure-runtime';
import { digitsOnly } from '@/lib/format/currency';
import { SEARCH_TEXT_MAX_LENGTH, sanitizeNoAngleBrackets } from '@/lib/validation/fieldRules';
import { SearchableSelect } from '@/components/atom/SearchableSelect';
import type { CompanyItem } from '@/lib/api/superadmin-client';

export interface ValidacionesUiFilters {
  name: string;
  documentNumber: string;
  status: '' | BiometricEstado;
  vigenciaEstado: '' | BiometricVigenciaEstado;
  createdFrom: string;
  createdTo: string;
  expiraDesde: string;
  expiraHasta: string;
  venceEnDias: string;
}

export const EMPTY_VALIDACIONES_FILTERS: ValidacionesUiFilters = {
  name: '',
  documentNumber: '',
  status: '',
  vigenciaEstado: '',
  createdFrom: '',
  createdTo: '',
  expiraDesde: '',
  expiraHasta: '',
  venceEnDias: '',
};

export function hasActiveValidacionesFilters(f: ValidacionesUiFilters): boolean {
  return (
    f.name.trim() !== '' ||
    f.documentNumber.trim() !== '' ||
    f.status !== '' ||
    f.vigenciaEstado !== '' ||
    f.createdFrom !== '' ||
    f.createdTo !== '' ||
    f.expiraDesde !== '' ||
    f.expiraHasta !== '' ||
    f.venceEnDias.trim() !== ''
  );
}

export function splitPersonaODocumentoQuery(q: string): Pick<ValidacionesUiFilters, 'name' | 'documentNumber'> {
  const t = q.trim();
  if (!t) return { name: '', documentNumber: '' };
  const compact = t.replace(/\s/g, '');
  const digits = digitsOnly(t);
  if (digits.length >= 4 && digits.length >= compact.length * 0.8) {
    return { name: '', documentNumber: digits };
  }
  return { name: sanitizeNoAngleBrackets(t).slice(0, SEARCH_TEXT_MAX_LENGTH), documentNumber: '' };
}

export function personaODocumentoDisplay(f: ValidacionesUiFilters): string {
  return f.documentNumber || f.name;
}

interface CompanyScope {
  companies: CompanyItem[];
  companyId: string;
  onCompanyChange: (id: string) => void;
  empresaVista?: CompanyItem;
}

interface Props {
  filters: ValidacionesUiFilters;
  onChange: (patch: Partial<ValidacionesUiFilters>) => void;
  onSearch: () => void;
  onClearFilters: () => void;
  onCancelConsulta: () => void;
  open: boolean;
  onToggle: () => void;
  loading?: boolean;
  resultCount: number;
  resultCountLabel?: string;
  companyScope?: CompanyScope | null;
}

const ESTADO_OPTIONS: { value: '' | BiometricEstado; label: string }[] = [
  { value: '', label: 'Todos' },
  { value: 'enviado', label: 'Enviado' },
  { value: 'en_proceso', label: 'En proceso' },
  { value: 'aprobado', label: 'Aprobado' },
  { value: 'rechazado', label: 'Rechazado' },
  { value: 'expirado', label: 'Expirado' },
  { value: 'pendiente_envio', label: 'Pendiente de envío' },
  { value: 'error_envio', label: 'Error de envío' },
];

const VIGENCIA_OPTIONS: { value: '' | BiometricVigenciaEstado; label: string }[] = [
  { value: '', label: 'Todas' },
  { value: 'vigente', label: 'Vigente' },
  { value: 'por_vencer', label: 'Por vencer (≤7 días)' },
  { value: 'vencida', label: 'Vencida' },
];

const CONTROL_CLASS =
  'mt-1.5 w-full h-[42px] rounded-xl border bg-white px-3 text-[13px] outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20 dark:bg-[#162744]';

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="flex min-w-0 flex-col">
      <span className="text-xs font-medium text-[#162744] dark:text-white">{label}</span>
      {children}
    </label>
  );
}

function FilterSelect<T extends string>({
  label,
  value,
  options,
  onSelect,
}: {
  label: string;
  value: T;
  options: { value: T; label: string }[];
  onSelect: (v: T) => void;
}) {
  return (
    <Field label={label}>
      <select value={value} onChange={(e) => onSelect(e.target.value as T)} className={CONTROL_CLASS}>
        {options.map((o) => (
          <option key={o.value || 'todos'} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
    </Field>
  );
}

function RangoFecha({
  label,
  desdeLabel,
  hastaLabel,
  desde,
  hasta,
  onDesde,
  onHasta,
}: {
  label: string;
  desdeLabel: string;
  hastaLabel: string;
  desde: string;
  hasta: string;
  onDesde: (v: string) => void;
  onHasta: (v: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const h = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', h);
    return () => document.removeEventListener('mousedown', h);
  }, [open]);

  const texto = desde || hasta ? `${desde || 'Inicio'} → ${hasta || 'Fin'}` : '';

  return (
    <div className="relative min-w-0" ref={ref}>
      <span className="text-xs font-medium text-[#162744] dark:text-white">{label}</span>
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        aria-haspopup="dialog"
        aria-label={label}
        className={`${CONTROL_CLASS} truncate text-left`}
        style={{ color: texto ? '#162744' : undefined }}
      >
        {texto || 'Seleccionar rango'}
      </button>
      {open && (
        <div
          className="absolute z-30 mt-2 w-[280px] space-y-2 rounded-xl border bg-white p-3 dark:bg-[#162744]"
          role="dialog"
          aria-label={label}
        >
          <Field label={desdeLabel}>
            <input type="date" value={desde} onChange={(e) => onDesde(e.target.value)} className={CONTROL_CLASS} />
          </Field>
          <Field label={hastaLabel}>
            <input
              type="date"
              value={hasta}
              min={desde || undefined}
              onChange={(e) => onHasta(e.target.value)}
              className={CONTROL_CLASS}
            />
          </Field>
          <div className="flex items-center justify-between pt-1">
            <button
              type="button"
              onClick={() => {
                onDesde('');
                onHasta('');
              }}
              className="text-xs font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
              style={{ color: '#FF4E00' }}
            >
              Limpiar
            </button>
            <button
              type="button"
              onClick={() => setOpen(false)}
              className="h-8 rounded-lg px-3 text-xs font-semibold text-white"
              style={{ background: '#557EFF' }}
            >
              Aplicar
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

export function ValidacionesFilterToolbar({
  filters,
  onChange,
  onSearch,
  onClearFilters,
  onCancelConsulta,
  open,
  onToggle,
  loading = false,
  resultCount,
  resultCountLabel,
  companyScope,
}: Props) {
  const hasActiveFilters = hasActiveValidacionesFilters(filters);
  const counterLabel =
    resultCountLabel ??
    (resultCount === 0 ? 'Sin resultados' : `${resultCount} persona${resultCount === 1 ? '' : 's'}`);

  return (
    <div className="shrink-0 rounded-2xl border border-[#DFE5ED] bg-white p-5 shadow-sm dark:bg-[#162744]">
      <div className="flex items-center justify-between gap-3">
        <p className="text-[13px] font-semibold text-[#162744] dark:text-white">Filtros de búsqueda</p>
        <button
          type="button"
          onClick={onToggle}
          aria-expanded={open}
          aria-controls="validaciones-filtros-panel"
          aria-label={open ? 'Colapsar panel de búsqueda' : 'Desplegar panel de búsqueda'}
          className="grid place-items-center rounded-lg p-1 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF]"
        >
          <ChevronDown
            className={`h-4 w-4 transition-transform duration-300 ${open ? 'rotate-180' : ''}`}
            aria-hidden="true"
          />
        </button>
      </div>

      <div hidden={!open} id="validaciones-filtros-panel" className="mt-4 space-y-4">
        <div className="grid grid-cols-1 gap-4 md:grid-cols-4">
          {companyScope ? (
            <div className="md:col-span-2">
              <SearchableSelect
                id="identidad-empresa"
                label="Ver las validaciones de otra empresa"
                options={companyScope.companies.map((c) => ({
                  value: c.id,
                  label: c.razonSocial,
                  hint: c.nit,
                }))}
                value={companyScope.companyId}
                onChange={companyScope.onCompanyChange}
                defaultLabel="Mi empresa"
                placeholder="Buscar empresa…"
              />
              <p className="mt-1 text-xs italic opacity-70">
                Como Administrador FLIT puedes inspeccionar las validaciones de otras organizaciones.
              </p>
              {companyScope.empresaVista ? (
                <p className="mt-1 text-xs" style={{ color: '#557EFF' }} role="status" aria-live="polite">
                  Estás viendo los datos de <strong>{companyScope.empresaVista.razonSocial}</strong>. Todo
                  lo que hagas desde esta pantalla afecta a esa empresa.
                </p>
              ) : null}
            </div>
          ) : null}
          <div className={companyScope ? 'md:col-span-2' : 'md:col-span-4'}>
            <Field label="Buscar por persona o documento">
              <input
                type="search"
                value={personaODocumentoDisplay(filters)}
                onChange={(e) =>
                  onChange(splitPersonaODocumentoQuery(e.target.value.slice(0, SEARCH_TEXT_MAX_LENGTH)))
                }
                maxLength={SEARCH_TEXT_MAX_LENGTH}
                placeholder="Nombre completo o número de cédula"
                aria-label="Buscar por persona o documento"
                className={CONTROL_CLASS}
              />
            </Field>
          </div>
        </div>

        <div className="grid grid-cols-1 items-end gap-3 md:grid-cols-5">
          <FilterSelect
            label="Estado"
            value={filters.status}
            options={ESTADO_OPTIONS}
            onSelect={(v) => onChange({ status: v })}
          />
          <FilterSelect
            label="Vigencia"
            value={filters.vigenciaEstado}
            options={VIGENCIA_OPTIONS}
            onSelect={(v) => onChange({ vigenciaEstado: v })}
          />
          <RangoFecha
            label="Rango Registro"
            desdeLabel="Registro desde"
            hastaLabel="Registro hasta"
            desde={filters.createdFrom}
            hasta={filters.createdTo}
            onDesde={(v) => onChange({ createdFrom: v })}
            onHasta={(v) => onChange({ createdTo: v })}
          />
          <RangoFecha
            label="Rango Vencimiento"
            desdeLabel="Vence desde"
            hastaLabel="Vence hasta"
            desde={filters.expiraDesde}
            hasta={filters.expiraHasta}
            onDesde={(v) => onChange({ expiraDesde: v })}
            onHasta={(v) => onChange({ expiraHasta: v })}
          />
          <Field label="Días vencimiento">
            <input
              type="text"
              inputMode="numeric"
              pattern="[0-9]*"
              autoComplete="off"
              value={filters.venceEnDias}
              onChange={(e) => onChange({ venceEnDias: digitsOnly(e.target.value) })}
              placeholder="≤ N días"
              aria-label="Vence en ≤ N días"
              className={CONTROL_CLASS}
            />
          </Field>
        </div>

        <div className="flex flex-wrap items-center justify-between gap-3">
          <p className="text-xs opacity-70" role="status" aria-live="polite">
            {counterLabel}
            {hasActiveFilters ? <span className="ml-2">· filtros activos</span> : null}
          </p>
          <div className="flex w-full flex-wrap items-center justify-end gap-2 md:w-auto md:min-w-[480px]">
            <button
              type="button"
              onClick={onCancelConsulta}
              className="h-11 flex-1 rounded-xl px-4 text-[13px] font-semibold text-white transition hover:opacity-90 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#FF4E00] md:flex-none md:px-5"
              style={{ background: '#FF4E00' }}
            >
              Cancelar consulta
            </button>
            <button
              type="button"
              onClick={onClearFilters}
              aria-label="Limpiar filtros"
              className="h-11 flex-1 rounded-xl border border-[#DFE5ED] bg-white px-4 text-[13px] font-semibold text-[#162744] transition hover:bg-[#EEF5FF] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF] md:flex-none md:px-5 dark:bg-[#162744] dark:text-white"
            >
              Limpiar filtros
            </button>
            <button
              type="button"
              onClick={onSearch}
              disabled={loading}
              className="h-11 flex-1 rounded-xl px-4 text-[13px] font-semibold text-white transition hover:opacity-90 disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF] md:flex-none md:px-5"
              style={{ background: '#557EFF' }}
            >
              Buscar
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
