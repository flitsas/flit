"use client";

import { useEffect, useMemo, useState } from "react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { SearchableSelect, type SearchableSelectOption } from "@flit/ui/SearchableSelect";
import { RUTAS, apiFetch } from "@/lib/api";
import { useEmpresaSeleccionada } from "@/lib/empresaSeleccionada";
import { mensajeErrorFijo } from "@/lib/errores";

/** Compañía del selector (plan §5.1, lista del backoffice: `{items:[{id, nombre, nit, …}], total, page, pageSize}`). */
export interface EmpresaOpcion {
  id: string;
  nombre: string;
  nit?: string | null;
}

interface PaginaEmpresas {
  items: EmpresaOpcion[];
  total?: number;
}

/** Tope de páginas que se recorren (defensa ante un `total` inconsistente). */
const MAX_PAGINAS = 100;

/** Todas las compañías, página por página, hasta completar `total` o recibir una página vacía. */
async function cargarEmpresas(signal: AbortSignal): Promise<EmpresaOpcion[]> {
  const todas: EmpresaOpcion[] = [];
  for (let page = 1; page <= MAX_PAGINAS; page++) {
    const pagina = await apiFetch<PaginaEmpresas>(`${RUTAS.empresas}?page=${page}`, { signal });
    const items = pagina?.items ?? [];
    todas.push(...items);
    if (items.length === 0 || typeof pagina.total !== "number" || todas.length >= pagina.total) break;
  }
  return todas;
}

type Listado = { tipo: "cargando" } | { tipo: "error"; mensaje: string } | { tipo: "listo"; empresas: EmpresaOpcion[] };

const TEXTO = "text-xs text-[color:var(--di-text-secondary)]";

/**
 * Selector de compañía del Super Admin en la barra (plan §6.1, AC-FE-23 y AC-FE-33). «Todas las compañías» es la vista
 * global (sin ?empresa=); elegir una la guarda en ?empresa= de la URL, conserva los filtros y vuelve a la primera página.
 * Cuatro estados: cargando (combobox deshabilitado), error fijo con Reintentar, vacío (solo la vista global) y listo.
 * Accesibilidad la del SearchableSelect de @flit/ui: etiqueta (oculta a la vista, no al lector), teclado completo y
 * anuncio de resultados.
 */
export function SelectorEmpresa({ id = "impuestos-empresa" }: { id?: string }) {
  const { empresa, elegir } = useEmpresaSeleccionada();
  const [listado, setListado] = useState<Listado>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    cargarEmpresas(controller.signal).then(
      (empresas) => {
        if (!controller.signal.aborted) setListado({ tipo: "listo", empresas });
      },
      (e: unknown) => {
        if (!controller.signal.aborted) setListado({ tipo: "error", mensaje: mensajeErrorFijo(e, "No se pudieron cargar las compañías.") });
      },
    );
    return () => controller.abort();
  }, [intento]);

  const opciones = useMemo<SearchableSelectOption[]>(
    () => (listado.tipo === "listo" ? listado.empresas.map((e) => ({ value: e.id, label: e.nombre, ...(e.nit ? { hint: `NIT ${e.nit}` } : {}) })) : []),
    [listado],
  );

  const reintentar = () => {
    setListado({ tipo: "cargando" });
    setIntento((n) => n + 1);
  };

  return (
    <div className="flex w-56 flex-col gap-1">
      <SearchableSelect
        id={id}
        label="Compañía"
        hideLabel
        defaultLabel="Todas las compañías"
        placeholder="Todas las compañías"
        noResultsLabel="Ninguna compañía coincide"
        options={opciones}
        value={listado.tipo === "listo" ? (empresa ?? "") : ""}
        onChange={(valor) => elegir(valor || null)}
        disabled={listado.tipo !== "listo"}
      />
      {listado.tipo === "cargando" && (
        <p role="status" className={TEXTO}>
          Cargando compañías…
        </p>
      )}
      {listado.tipo === "error" && (
        <InlineAlert
          tone="error"
          compact
          action={
            <button
              type="button"
              onClick={reintentar}
              className="rounded-full border border-current px-2 py-0.5 text-xs font-semibold focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            >
              Reintentar
            </button>
          }
        >
          {listado.mensaje}
        </InlineAlert>
      )}
      {listado.tipo === "listo" && listado.empresas.length === 0 && <p className={TEXTO}>No hay compañías con Impuestos.</p>}
    </div>
  );
}
