import { AccesoSaneamientos } from "@/components/backoffice/AccesoSaneamientos";
import { BandejaSaneamientos } from "@/components/backoffice/BandejaSaneamientos";
import { filtrosBandejaDesdeUrl } from "@/lib/backoffice/saneamientos";

type Consulta = Record<string, string | string[] | undefined>;

/** Consulta de la URL a URLSearchParams, conservando los parámetros repetidos (`estado`). */
function parametros(q: Consulta): URLSearchParams {
  const p = new URLSearchParams();
  for (const [clave, valor] of Object.entries(q)) {
    if (Array.isArray(valor)) for (const v of valor) p.append(clave, v);
    else if (valor !== undefined) p.append(clave, valor);
  }
  return p;
}

/**
 * PS-4, bandeja «Saneamientos» del backoffice FLIT (HU #14577 AC24–AC28, spec-ui §5): solicitudes de todas las compañías,
 * con los filtros en la URL. Solo con las capacidades `backoffice` y `gestionarSaneamiento`.
 */
export default async function PaginaSaneamientos({ searchParams }: { searchParams: Promise<Consulta> }) {
  const filtros = filtrosBandejaDesdeUrl(parametros(await searchParams));
  return (
    <AccesoSaneamientos>
      <BandejaSaneamientos filtros={filtros} />
    </AccesoSaneamientos>
  );
}
