import { AccesoSaneamientos } from "@/components/backoffice/AccesoSaneamientos";
import { GestionSaneamiento } from "@/components/backoffice/GestionSaneamiento";
import { VolverABandeja } from "@/components/backoffice/VolverABandeja";
import { esEmpresaValida } from "@/lib/configuracion";

/**
 * PS-5, gestión de un saneamiento (HU #14577 AC29–AC37, spec-ui §6) en /backoffice/saneamientos/{sid}?empresa=. Sin una
 * compañía válida vuelve a la bandeja (patrón de backoffice/diagnosticos/[id]). Solo con `gestionarSaneamiento`.
 */
export default async function PaginaGestionSaneamiento({
  params,
  searchParams,
}: {
  params: Promise<{ sid: string }>;
  searchParams: Promise<{ empresa?: string | string[] }>;
}) {
  const { sid } = await params;
  const { empresa: valor } = await searchParams;
  const empresa = (Array.isArray(valor) ? valor[0] : valor) ?? "";
  return <AccesoSaneamientos>{esEmpresaValida(empresa) ? <GestionSaneamiento sid={sid} empresa={empresa} /> : <VolverABandeja />}</AccesoSaneamientos>;
}
