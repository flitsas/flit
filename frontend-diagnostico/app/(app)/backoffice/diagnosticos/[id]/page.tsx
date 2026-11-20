import { AccesoBackoffice } from "@/components/backoffice/AccesoBackoffice";
import { GrillaBackoffice } from "@/components/backoffice/GrillaBackoffice";
import { ListaLotesBackoffice } from "@/components/backoffice/ListaLotesBackoffice";
import { esEmpresaValida } from "@/lib/configuracion";

/**
 * Backoffice FLIT: grilla de un lote con el origen de cada celda y el llenado manual (HU #13802). La compañía llega en
 * ?empresa=; sin una válida se vuelve al selector de la lista. Solo con la capacidad `backoffice`.
 */
export default async function GrillaLoteBackoffice({
  params,
  searchParams,
}: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ empresa?: string | string[] }>;
}) {
  const { id } = await params;
  const { empresa: valor } = await searchParams;
  const empresa = (Array.isArray(valor) ? valor[0] : valor) ?? "";
  return (
    <AccesoBackoffice>
      {esEmpresaValida(empresa) ? (
        <GrillaBackoffice diagnosticoId={id} empresa={empresa} />
      ) : (
        <ListaLotesBackoffice empresa="" estado={null} conErrores={false} pagina={1} />
      )}
    </AccesoBackoffice>
  );
}
