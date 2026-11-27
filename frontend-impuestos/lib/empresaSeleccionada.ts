// Compañía elegida por el Super Admin (plan §6.1): vive en ?empresa=<tenantId> de la URL, así un enlace o un recargo
// conservan la vista, y los fetchers la propagan a la API (`apiFetch(ruta, { empresa })`). Sin `empresa` la API usa la
// compañía de la sesión o, para el Super Admin en las rutas que lo admiten, la vista global.
import { useCallback } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";

export const PARAM_EMPRESA = "empresa";

/** Parámetro de paginación que se reinicia al cambiar de compañía (otra compañía, otra lista). */
const PARAM_PAGINA = "page";

/** Agrega `empresa` a una ruta de la API (con su query, si ya la tiene). Vacía o null: la ruta tal cual. */
export function conEmpresa(ruta: string, empresa: string | null | undefined): string {
  if (!empresa) return ruta;
  const [base, query = ""] = ruta.split("?", 2);
  const params = new URLSearchParams(query);
  params.set(PARAM_EMPRESA, empresa);
  return `${base}?${params.toString()}`;
}

/** Enlace interno que conserva la compañía elegida (misma regla que `conEmpresa`). */
export const hrefConEmpresa = conEmpresa;

/** Compañía de la URL actual y cómo cambiarla (reemplaza la entrada del historial, conserva los demás filtros). */
export function useEmpresaSeleccionada(): { empresa: string | null; elegir: (empresa: string | null) => void } {
  const router = useRouter();
  const pathname = usePathname() ?? "/";
  const busqueda = useSearchParams();
  const empresa = busqueda?.get(PARAM_EMPRESA) || null;

  const elegir = useCallback(
    (nueva: string | null) => {
      const params = new URLSearchParams(busqueda?.toString() ?? "");
      params.delete(PARAM_EMPRESA);
      params.delete(PARAM_PAGINA);
      if (nueva) params.set(PARAM_EMPRESA, nueva);
      const query = params.toString();
      router.replace(query ? `${pathname}?${query}` : pathname);
    },
    [busqueda, pathname, router],
  );

  return { empresa, elegir };
}
