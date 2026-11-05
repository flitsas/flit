import { AccesoBackoffice } from "@/components/backoffice/AccesoBackoffice";
import { ListaLotesBackoffice } from "@/components/backoffice/ListaLotesBackoffice";
import type { EstadoDiagnostico } from "@/lib/diagnosticos/api";

const ESTADOS: ReadonlySet<string> = new Set(["CREADO", "EN_PROCESO", "COMPLETADO", "INCOMPLETO"]);

type Consulta = { empresa?: string | string[]; estado?: string | string[]; con_errores?: string | string[]; pagina?: string | string[] };
const uno = (v: string | string[] | undefined) => (Array.isArray(v) ? v[0] : v) ?? "";

/** Backoffice FLIT: lotes de una compañía elegida explícitamente (HU #13802). Solo con la capacidad `backoffice`. */
export default async function LotesBackoffice({ searchParams }: { searchParams: Promise<Consulta> }) {
  const q = await searchParams;
  const estado = uno(q.estado);
  return (
    <AccesoBackoffice>
      <ListaLotesBackoffice
        empresa={uno(q.empresa)}
        estado={ESTADOS.has(estado) ? (estado as EstadoDiagnostico) : null}
        conErrores={uno(q.con_errores) === "true"}
        pagina={Number.parseInt(uno(q.pagina), 10) || 1}
      />
    </AccesoBackoffice>
  );
}
