"use client";

import { GENERACION_DOCUMENTAL_TYPE_LABELS } from "./generacion-documental-nav";
import {
  STANDALONE_DOCUMENT_STATUS_OPTIONS,
  type StandaloneDocumentStatusFilter,
} from "./status-labels";

/** Estado de los filtros del historial. Cadena vacía = «sin filtro», nunca `undefined`. */
export interface HistorialFiltersValue {
  documentType: string;
  status: StandaloneDocumentStatusFilter | "";
  dateFrom: string;
  dateTo: string;
  userId: string;
  /**
   * Identificador del lote XLSX (CF-18 en I3, HU #12211). Es un filtro MÁS: convive con los de
   * tipo, fecha, usuario y estado, y todos se aplican a la vez.
   */
  batchId: string;
  /**
   * Universo de compañías, y por eso NO es un filtro más: los demás recortan lo que ya se puede
   * ver, este decide qué se puede ver. Solo lo honra el backend para un SuperAdmin.
   *
   * Cadena vacía = mi compañía, `HISTORIAL_COMPANY_ALL` = todas, un uuid = esa compañía.
   */
  company: string;
}

/**
 * Valor centinela de «todas las compañías». Es una cadena y no `null` para que el estado de los
 * filtros siga siendo homogéneo (`""` = sin filtro) y `hasHistorialFilters` no necesite un caso
 * aparte.
 */
export const HISTORIAL_COMPANY_ALL = "ALL";

export const HISTORIAL_FILTERS_EMPTY: HistorialFiltersValue = {
  documentType: "",
  status: "",
  dateFrom: "",
  dateTo: "",
  userId: "",
  batchId: "",
  // Por defecto, la propia compañía. Ver documentos de otras es un acto deliberado, incluso para
  // un SuperAdmin: el estado inicial de la pantalla nunca cruza datos entre compañías.
  company: "",
};

export function hasHistorialFilters(value: HistorialFiltersValue): boolean {
  return Object.values(value).some((v) => v !== "");
}

export interface HistorialUserOption {
  id: string;
  name: string;
}

export interface HistorialCompanyOption {
  id: string;
  name: string;
}

export interface HistorialFiltersProps {
  value: HistorialFiltersValue;
  onChange: (value: HistorialFiltersValue) => void;
  /**
   * Compañías elegibles. Solo se pasan cuando quien mira es SuperAdmin: para cualquier otro
   * usuario el control no existe —no está oculto ni deshabilitado— porque el backend ignoraría
   * el parámetro de todas formas y un selector inerte prometería algo que no va a pasar.
   */
  companies?: HistorialCompanyOption[];
  /**
   * Autores que se pueden elegir. Se construyen con los usuarios ya vistos en el historial
   * de esta sesión: pedirlos a `/api/v1/security/users` exigiría un permiso de otro módulo
   * que el usuario de generación documental no tiene por qué tener.
   */
  users: HistorialUserOption[];
  disabled?: boolean;
}

const FIELD_CLASS =
  "w-full rounded-xl border px-3 py-2 text-xs focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]";

/**
 * Filtros del historial (CF-18, HU-03).
 *
 * <p>El selector de estado ofrece <b>exactamente tres</b> opciones —«Generado», «Error» y
 * «En proceso»— porque `pending` y `processing` colapsan (CF-21). Las etiquetas no se
 * escriben aquí: vienen de `status-labels.ts`, el único archivo del módulo que las declara.
 * La opción «Todos» no es un estado, es la ausencia de filtro.</p>
 *
 * <p>Accesibilidad (CF-22): cada control tiene su `label` asociada por `htmlFor`, el foco es
 * visible y ningún filtro se comunica solo por color.</p>
 */
export function HistorialFilters({
  value,
  onChange,
  users,
  companies,
  disabled = false,
}: HistorialFiltersProps) {
  const set = <K extends keyof HistorialFiltersValue>(key: K, v: HistorialFiltersValue[K]) =>
    onChange({ ...value, [key]: v });

  return (
    <section aria-labelledby="historial-filtros-titulo" className="rounded-2xl border p-4">
      <h2 id="historial-filtros-titulo" className="mb-3 text-xs font-semibold uppercase opacity-70">
        Filtros
      </h2>

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-6">
        {/*
          Va PRIMERO y ocupa dos columnas a propósito: no recorta el listado como los demás, decide
          de qué compañías es. Leerlo después del tipo de documento invitaría a interpretarlo como
          un filtro más.
        */}
        {companies !== undefined && (
          <div className="lg:col-span-2">
            <label htmlFor="historial-filtro-compania" className="mb-1 block text-[11px] font-medium">
              Compañía
            </label>
            <select
              id="historial-filtro-compania"
              data-testid="historial-filtro-compania"
              className={FIELD_CLASS}
              value={value.company}
              disabled={disabled}
              onChange={(e) => set("company", e.target.value)}
            >
              <option value="">Mi compañía</option>
              <option value={HISTORIAL_COMPANY_ALL}>Todas las compañías</option>
              {companies.map((company) => (
                <option key={company.id} value={company.id}>
                  {company.name}
                </option>
              ))}
            </select>
          </div>
        )}

        <div>
          <label htmlFor="historial-filtro-tipo" className="mb-1 block text-[11px] font-medium">
            Tipo de documento
          </label>
          <select
            id="historial-filtro-tipo"
            className={FIELD_CLASS}
            value={value.documentType}
            disabled={disabled}
            onChange={(e) => set("documentType", e.target.value)}
          >
            <option value="">Todos</option>
            {Object.entries(GENERACION_DOCUMENTAL_TYPE_LABELS).map(([code, label]) => (
              <option key={code} value={code}>
                {label}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label htmlFor="historial-filtro-estado" className="mb-1 block text-[11px] font-medium">
            Estado
          </label>
          <select
            id="historial-filtro-estado"
            className={FIELD_CLASS}
            value={value.status}
            disabled={disabled}
            onChange={(e) => set("status", e.target.value as StandaloneDocumentStatusFilter | "")}
          >
            <option value="">Todos</option>
            {STANDALONE_DOCUMENT_STATUS_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label htmlFor="historial-filtro-desde" className="mb-1 block text-[11px] font-medium">
            Desde
          </label>
          <input
            id="historial-filtro-desde"
            type="date"
            className={FIELD_CLASS}
            value={value.dateFrom}
            disabled={disabled}
            max={value.dateTo || undefined}
            onChange={(e) => set("dateFrom", e.target.value)}
          />
        </div>

        <div>
          <label htmlFor="historial-filtro-hasta" className="mb-1 block text-[11px] font-medium">
            Hasta
          </label>
          <input
            id="historial-filtro-hasta"
            type="date"
            className={FIELD_CLASS}
            value={value.dateTo}
            disabled={disabled}
            min={value.dateFrom || undefined}
            onChange={(e) => set("dateTo", e.target.value)}
          />
        </div>

        <div>
          <label htmlFor="historial-filtro-usuario" className="mb-1 block text-[11px] font-medium">
            Usuario
          </label>
          <select
            id="historial-filtro-usuario"
            className={FIELD_CLASS}
            value={value.userId}
            disabled={disabled || users.length === 0}
            onChange={(e) => set("userId", e.target.value)}
          >
            <option value="">Todos</option>
            {users.map((user) => (
              <option key={user.id} value={user.id}>
                {user.name}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label htmlFor="historial-filtro-lote" className="mb-1 block text-[11px] font-medium">
            Lote
          </label>
          <input
            id="historial-filtro-lote"
            type="text"
            inputMode="text"
            placeholder="Identificador del lote"
            className={FIELD_CLASS}
            value={value.batchId}
            disabled={disabled}
            aria-describedby="historial-filtro-lote-ayuda"
            onChange={(e) => set("batchId", e.target.value.trim())}
          />
          <p id="historial-filtro-lote-ayuda" className="mt-1 text-[10px] opacity-60">
            Filtra las filas generadas por una carga masiva. Se combina con los demás filtros.
          </p>
        </div>
      </div>

      {hasHistorialFilters(value) && (
        <div className="mt-3 flex justify-end">
          <button
            type="button"
            onClick={() => onChange(HISTORIAL_FILTERS_EMPTY)}
            className="rounded-xl border px-3 py-1.5 text-[11px] font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          >
            Limpiar filtros
          </button>
        </div>
      )}
    </section>
  );
}
