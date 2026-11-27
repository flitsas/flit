"use client";

import { useEffect, useMemo, useState } from "react";
import { SearchableSelect, type SearchableSelectOption } from "@flit/ui/SearchableSelect";
import { RUTAS, apiFetch } from "@/lib/api";
import { BOTON_REINTENTAR_BARRA_CLS as BOTON_REINTENTAR_CLS, QUITAR_24PX_CLS } from "@/components/comun/estilos";
import { useEmpresaSeleccionada } from "@/lib/empresaSeleccionada";

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

type Listado = { tipo: "cargando" } | { tipo: "error" } | { tipo: "listo"; empresas: EmpresaOpcion[] };

/**
 * Selector de compañía del Super Admin en la barra (plan §6.1, AC-FE-23 y AC-FE-33). «Todas las compañías» es la vista
 * global (sin ?empresa=); elegir una la guarda en ?empresa= de la URL, conserva los filtros y vuelve a la primera página.
 * Cuatro estados, todos dentro del propio control para no crecer en alto en la barra (spec-visual §4): cargando (combobox
 * deshabilitado con «Cargando compañías…» y aria-busy), error en tono warning (combobox deshabilitado con «No se
 * cargaron las compañías» y Reintentar de 32 px en la misma fila), vacío (solo la vista global; la búsqueda dice «No hay
 * compañías con Impuestos») y listo.
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
      () => {
        if (!controller.signal.aborted) setListado({ tipo: "error" });
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

  const listo = listado.tipo === "listo";
  const sinCompanias = listo && listado.empresas.length === 0;
  const selector = (
    <SearchableSelect
      id={id}
      className={`${QUITAR_24PX_CLS} min-w-0 flex-1`}
      label="Compañía"
      hideLabel
      // Sin la opción por defecto mientras no hay lista: así el input muestra el placeholder del estado.
      defaultLabel={listo ? "Todas las compañías" : undefined}
      placeholder={listado.tipo === "cargando" ? "Cargando compañías…" : listado.tipo === "error" ? "No se cargaron las compañías" : "Todas las compañías"}
      noResultsLabel={sinCompanias ? "No hay compañías con Impuestos" : "Ninguna compañía coincide"}
      options={opciones}
      value={listo ? (empresa ?? "") : ""}
      onChange={(valor) => elegir(valor || null)}
      disabled={!listo}
    />
  );

  return (
    <div className="flex w-40 flex-col sm:w-56" aria-busy={listado.tipo === "cargando" ? true : undefined}>
      {listado.tipo === "error" ? (
        <div className="flex items-center gap-1">
          {selector}
          <button type="button" onClick={reintentar} aria-label="Reintentar cargar compañías" className={BOTON_REINTENTAR_CLS}>
            Reintentar
          </button>
        </div>
      ) : (
        selector
      )}
      {/* El error se anuncia sin crecer en alto en la barra (M15-ajustes para M18): región estable, solo para el lector. */}
      <span role="status" aria-live="polite" aria-atomic="true" className="sr-only">
        {listado.tipo === "error" ? "No se cargaron las compañías" : ""}
      </span>
    </div>
  );
}
