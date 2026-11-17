import { PaginaDetalleConsulta } from "@/components/tablero/PaginaDetalleConsulta";

/**
 * Detalle de una consulta del tablero en su propia página (HU #14087): el semáforo de la grilla la abre en una pestaña nueva.
 * Pide GET /diagnosticos/{id}/vehiculos/{vid}/consultas/{fuente} con la sesión de la compañía.
 */
export default async function DetalleConsulta({ params }: { params: Promise<{ id: string; vehiculoId: string; fuente: string }> }) {
  const { id, vehiculoId, fuente } = await params;
  return <PaginaDetalleConsulta loteId={id} vehiculoId={vehiculoId} fuente={fuente} />;
}
