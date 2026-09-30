"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Search, X } from "lucide-react";
import { Pagination } from "@/components/atom/Pagination";
import { usePaginacion } from "@/components/atom/usePaginacion";
import {
  fetchCompanyAssociableCompanies,
  fetchOtAssociableCompanies,
  type AssociableCompaniesPage,
  type AssociableCompany,
  type MandateSignerOfficeCompanies,
} from "@/lib/api/admin-mandate-signers";

/** El servidor rechaza búsquedas de menos de 2 caracteres (422): no se envían. */
const MIN_BUSQUEDA = 2;
const DEBOUNCE_MS = 300;
/** Las hijas se piden de a 100 (máximo del servidor) hasta completarlas. */
const PAGINAS_HIJAS_MAX = 10;

/**
 * De dónde salen las compañías que se pueden asociar (HU #13181): el OT y el Super Admin buscan entre
 * todas las compañías activas; el Admin de Compañía solo ve sus hijas.
 */
export type FuenteAsociadas =
  | { modo: "ot"; transitOfficeId: string }
  | { modo: "hijas"; tenantId: string; networkHeadId?: string | null };

/** Compañía marcada. Sin `name` cuando viene de un mandatario guardado y aún no se vio en la lista. */
export interface AsociadaSeleccionada {
  id: string;
  name?: string;
  nit?: string;
}

/**
 * Selección inicial al editar: con nombre y NIT cuando el servidor los trae; solo con id (respaldo
 * «Compañía asociada») si no.
 */
export function precargarAsociadas(
  officeCompanies?: MandateSignerOfficeCompanies[],
): Record<string, AsociadaSeleccionada> {
  const out: Record<string, AsociadaSeleccionada> = {};
  for (const o of officeCompanies ?? []) {
    for (const c of o.associatedCompanies ?? []) {
      if (c.id) out[c.id] = { id: c.id, name: c.name || undefined, nit: c.nit || undefined };
    }
    for (const id of o.associatedCompanyTenantIds ?? []) {
      if (!out[id]) out[id] = { id };
    }
  }
  return out;
}

export function etiquetaAsociada(c: AsociadaSeleccionada): string {
  if (!c.name) return "Compañía asociada";
  return c.nit ? `${c.name} · NIT ${c.nit}` : c.name;
}

export const TEXTO_SOLO_SU_COMPANIA = "Este mandatario aplica solo a su compañía";

/**
 * HU #13181 — lista de compañías asociadas del formulario del mandatario, según el perfil. Solo
 * nombre y NIT (Habeas Data). Reemplaza el antiguo bloque de «empresas representadas» por RL.
 * `onSinRed` avisa al formulario cuando no hay lista (Admin de Compañía sin hijas).
 */
export function MandatarioCompaniasAsociadas({
  fuente,
  seleccion,
  onChange,
  excluirIds = [],
  errores = {},
  onSinRed,
}: {
  fuente: FuenteAsociadas;
  seleccion: Record<string, AsociadaSeleccionada>;
  onChange: (next: Record<string, AsociadaSeleccionada>) => void;
  /** Compañía propia del mandatario: no se ofrece (el servidor la rechazaría). */
  excluirIds?: string[];
  /** Motivo 422 por compañía (id de tenant → mensaje). */
  errores?: Record<string, string>;
  onSinRed?: (sinRed: boolean) => void;
}) {
  return fuente.modo === "ot" ? (
    <ListaBuscable
      transitOfficeId={fuente.transitOfficeId}
      seleccion={seleccion}
      onChange={onChange}
      excluirIds={excluirIds}
      errores={errores}
    />
  ) : (
    <ListaHijas
      tenantId={fuente.tenantId}
      networkHeadId={fuente.networkHeadId}
      seleccion={seleccion}
      onChange={onChange}
      excluirIds={excluirIds}
      errores={errores}
      onSinRed={onSinRed}
    />
  );
}

const TEXTO_OPCIONAL = "Opcional. Sin selección, el mandatario aplica solo a su compañía.";

function AyudaOpcional() {
  return <p className="text-[11px] leading-tight opacity-70">{TEXTO_OPCIONAL}</p>;
}

function alternar(
  seleccion: Record<string, AsociadaSeleccionada>,
  c: AssociableCompany,
): Record<string, AsociadaSeleccionada> {
  const next = { ...seleccion };
  if (next[c.id]) delete next[c.id];
  else next[c.id] = { id: c.id, name: c.name, nit: c.nit };
  return next;
}

const INPUT_CLS =
  "w-full rounded-xl border bg-white py-2 pl-8 pr-3 text-xs outline-none focus:border-[#557EFF] dark:bg-[#0B0F14]";

function ListaBuscable({
  transitOfficeId,
  seleccion,
  onChange,
  excluirIds,
  errores,
}: {
  transitOfficeId: string;
  seleccion: Record<string, AsociadaSeleccionada>;
  onChange: (next: Record<string, AsociadaSeleccionada>) => void;
  excluirIds: string[];
  errores: Record<string, string>;
}) {
  const pg = usePaginacion(10);
  const { page, pageSize, setPage } = pg;
  const [entrada, setEntrada] = useState("");
  const [termino, setTermino] = useState("");
  const [data, setData] = useState<AssociableCompaniesPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);
  const [reintento, setReintento] = useState(0);

  useEffect(() => {
    const t = entrada.trim();
    if (t.length > 0 && t.length < MIN_BUSQUEDA) return;
    const id = setTimeout(() => {
      setTermino(t);
      setPage(1);
    }, DEBOUNCE_MS);
    return () => clearTimeout(id);
  }, [entrada, setPage]);

  useEffect(() => {
    const ctrl = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga al cambiar filtro o página
    setLoading(true);
    setError(false);
    void (async () => {
      try {
        const res = await fetchOtAssociableCompanies(
          transitOfficeId,
          { search: termino || undefined, page, pageSize },
          ctrl.signal,
        );
        if (!ctrl.signal.aborted) setData(res);
      } catch {
        if (!ctrl.signal.aborted) setError(true);
      } finally {
        if (!ctrl.signal.aborted) setLoading(false);
      }
    })();
    return () => ctrl.abort();
  }, [transitOfficeId, termino, page, pageSize, reintento]);

  const items = (data?.items ?? []).filter((c) => !excluirIds.includes(c.id));
  const corta = entrada.trim().length > 0 && entrada.trim().length < MIN_BUSQUEDA;

  return (
    <div className="space-y-2" data-testid="mandatario-asociadas-ot">
      <div className="relative">
        <Search
          className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 opacity-60"
          aria-hidden="true"
        />
        <label htmlFor="mandatario-asociadas-buscar" className="sr-only">
          Buscar compañía por nombre o NIT
        </label>
        <input
          id="mandatario-asociadas-buscar"
          type="search"
          value={entrada}
          onChange={(e) => setEntrada(e.target.value)}
          placeholder="Buscar por nombre o NIT…"
          className={INPUT_CLS}
          aria-describedby={corta ? "mandatario-asociadas-ayuda" : undefined}
        />
      </div>
      {corta ? (
        <p id="mandatario-asociadas-ayuda" className="text-[11px] leading-tight opacity-70">
          Escribe al menos {MIN_BUSQUEDA} caracteres.
        </p>
      ) : null}

      <div
        className="max-h-48 space-y-1 overflow-y-auto rounded-xl border p-2"
        aria-busy={loading}
        data-testid="mandatario-asociadas-lista"
      >
        {error ? (
          <div role="alert" className="flex items-center justify-between gap-2 text-[11px]">
            <span style={{ color: "#E5484D" }}>No se pudieron cargar las compañías.</span>
            <button
              type="button"
              className="rounded-lg border px-2 py-1 font-semibold"
              onClick={() => setReintento((n) => n + 1)}
            >
              Reintentar
            </button>
          </div>
        ) : loading && !data ? (
          <p role="status" className="text-[11px] opacity-70">
            Cargando compañías…
          </p>
        ) : items.length === 0 ? (
          <p role="status" className="text-[11px] opacity-70">
            {termino ? "Sin resultados" : "No hay compañías para asociar."}
          </p>
        ) : (
          items.map((c) => (
            <FilaCompania
              key={c.id}
              company={c}
              checked={!!seleccion[c.id]}
              onToggle={() => onChange(alternar(seleccion, c))}
            />
          ))
        )}
      </div>

      {!error && (data?.total ?? 0) > 0 ? (
        <Pagination
          page={pg.page}
          pageSize={pg.pageSize}
          totalCount={data?.total ?? 0}
          onPageChange={pg.setPage}
          onPageSizeChange={pg.setPageSize}
          noun="compañías"
          ariaLabel="Paginación de compañías asociables"
        />
      ) : null}

      <Seleccionadas seleccion={seleccion} onChange={onChange} errores={errores} />
      <AyudaOpcional />
    </div>
  );
}

function ListaHijas({
  tenantId,
  networkHeadId,
  seleccion,
  onChange,
  excluirIds,
  errores,
  onSinRed,
}: {
  tenantId: string;
  networkHeadId?: string | null;
  seleccion: Record<string, AsociadaSeleccionada>;
  onChange: (next: Record<string, AsociadaSeleccionada>) => void;
  excluirIds: string[];
  errores: Record<string, string>;
  onSinRed?: (sinRed: boolean) => void;
}) {
  const [hijas, setHijas] = useState<AssociableCompany[] | null>(null);
  const [soloPropia, setSoloPropia] = useState(false);
  const [error, setError] = useState(false);
  const [reintento, setReintento] = useState(0);

  useEffect(() => {
    const ctrl = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial de las hijas
    setError(false);
    void (async () => {
      try {
        const todas: AssociableCompany[] = [];
        let propia = false;
        for (let page = 1; page <= PAGINAS_HIJAS_MAX; page++) {
          const res = await fetchCompanyAssociableCompanies(
            tenantId,
            { page, pageSize: 100 },
            ctrl.signal,
            networkHeadId,
          );
          todas.push(...res.items);
          propia = res.aplicaSoloASuCompania;
          if (todas.length >= res.total || res.items.length === 0) break;
        }
        if (ctrl.signal.aborted) return;
        setHijas(todas);
        setSoloPropia(propia || todas.length === 0);
      } catch {
        if (!ctrl.signal.aborted) setError(true);
      }
    })();
    return () => ctrl.abort();
  }, [tenantId, networkHeadId, reintento]);

  useEffect(() => {
    if (hijas) onSinRed?.(soloPropia);
  }, [hijas, soloPropia, onSinRed]);

  const visibles = useMemo(
    () => (hijas ?? []).filter((c) => !excluirIds.includes(c.id)),
    [hijas, excluirIds],
  );

  if (error) {
    return (
      <div role="alert" className="flex items-center justify-between gap-2 text-[11px]">
        <span style={{ color: "#E5484D" }}>No se pudieron cargar las compañías.</span>
        <button
          type="button"
          className="rounded-lg border px-2 py-1 font-semibold"
          onClick={() => setReintento((n) => n + 1)}
        >
          Reintentar
        </button>
      </div>
    );
  }
  if (!hijas) {
    return (
      <p role="status" className="text-[11px] opacity-70">
        Cargando compañías…
      </p>
    );
  }
  if (soloPropia) {
    return (
      <p className="text-[11px] leading-tight opacity-70" data-testid="mandatario-asociadas-sin-red">
        {TEXTO_SOLO_SU_COMPANIA}
      </p>
    );
  }
  return (
    <div className="space-y-2" data-testid="mandatario-asociadas-hijas">
      <div className="max-h-48 space-y-1 overflow-y-auto rounded-xl border p-2">
        {visibles.map((c) => (
          <FilaCompania
            key={c.id}
            company={c}
            checked={!!seleccion[c.id]}
            onToggle={() => onChange(alternar(seleccion, c))}
          />
        ))}
      </div>
      <Seleccionadas seleccion={seleccion} onChange={onChange} errores={errores} oculta />
      <AyudaOpcional />
    </div>
  );
}

function FilaCompania({
  company,
  checked,
  onToggle,
}: {
  company: AssociableCompany;
  checked: boolean;
  onToggle: () => void;
}) {
  return (
    <label className="flex items-center gap-2 text-xs">
      <input
        type="checkbox"
        checked={checked}
        onChange={onToggle}
        aria-label={`${company.name} (NIT ${company.nit})`}
      />
      <span>
        {company.name}
        <span className="opacity-70"> · NIT {company.nit}</span>
      </span>
    </label>
  );
}

/**
 * Compañías marcadas (persisten entre páginas y búsquedas) y el motivo del rechazo del servidor por
 * compañía. Con `oculta` solo muestra los motivos: la lista de hijas ya deja ver qué está marcado.
 */
function Seleccionadas({
  seleccion,
  onChange,
  errores,
  oculta = false,
}: {
  seleccion: Record<string, AsociadaSeleccionada>;
  onChange: (next: Record<string, AsociadaSeleccionada>) => void;
  errores: Record<string, string>;
  oculta?: boolean;
}) {
  const quitar = useCallback(
    (id: string) => {
      const next = { ...seleccion };
      delete next[id];
      onChange(next);
    },
    [seleccion, onChange],
  );
  const marcadas = Object.values(seleccion);
  const ids = Object.keys(errores);
  if ((oculta || marcadas.length === 0) && ids.length === 0) return null;
  return (
    <div className="space-y-1.5" data-testid="mandatario-asociadas-seleccion">
      {!oculta && marcadas.length > 0 ? (
        <ul className="flex flex-wrap gap-1.5" aria-label="Compañías seleccionadas">
          {marcadas.map((c) => (
            <li
              key={c.id}
              className="flex items-center gap-1 rounded-lg border px-2 py-1 text-[11px]"
              style={errores[c.id] ? { borderColor: "#E5484D" } : undefined}
            >
              {etiquetaAsociada(c)}
              <button
                type="button"
                onClick={() => quitar(c.id)}
                aria-label={`Quitar ${c.name ?? "compañía asociada"}`}
              >
                <X className="h-3 w-3" aria-hidden="true" />
              </button>
            </li>
          ))}
        </ul>
      ) : null}
      {ids.length > 0 ? (
        <ul className="space-y-0.5" role="alert" data-testid="mandatario-asociadas-errores">
          {ids.map((id) => (
            <li key={id} className="text-[11px] leading-tight" style={{ color: "#E5484D" }}>
              {seleccion[id]?.name ? `${seleccion[id]!.name}: ` : ""}
              {errores[id]}
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
