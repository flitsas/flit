import { AccesoBackoffice } from "@/components/backoffice/AccesoBackoffice";
import { DetalleConsultaBackoffice } from "@/components/backoffice/DetalleConsultaBackoffice";
import { ListaLotesBackoffice } from "@/components/backoffice/ListaLotesBackoffice";
import { esEmpresaValida } from "@/lib/configuracion";

/**
 * Backoffice FLIT: detalle de una consulta de un lote en su propia página (HU #14087), que el semáforo de la grilla del
 * backoffice abre en una pestaña nueva. La compañía llega en ?empresa=; sin una válida se vuelve al selector de la lista.
 * Solo con la capacidad `backoffice`.
 */
export default async function DetalleConsultaBackofficePagina({
  params,
  searchParams,
}: {
  params: Promise<{ id: string; vehiculoId: string; fuente: string }>;
  searchParams: Promise<{ empresa?: string | string[] }>;
}) {
  const { id, vehiculoId, fuente } = await params;
  const { empresa: valor } = await searchParams;
  const empresa = (Array.isArray(valor) ? valor[0] : valor) ?? "";
  return (
    <AccesoBackoffice>
      {esEmpresaValida(empresa) ? (
        <DetalleConsultaBackoffice diagnosticoId={id} vehiculoId={vehiculoId} fuente={fuente} empresa={empresa} />
      ) : (
        <ListaLotesBackoffice empresa="" estado={null} conErrores={false} pagina={1} />
      )}
    </AccesoBackoffice>
  );
}
