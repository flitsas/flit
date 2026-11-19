import { TableroLote } from "@/components/tablero/TableroLote";

/**
 * Tablero del lote (HU #13748): destino de la confirmación de la carga masiva, del Express y del listado. ?desde= trae los
 * filtros del listado para «Volver a diagnósticos» (HU #14087). ?saneamiento=con|sin enciende el chip de saneamiento
 * (HU #14577 AC9; la campana navega con ?saneamiento=con).
 */
export default async function TableroDiagnostico({
  params,
  searchParams,
}: {
  params: Promise<{ id: string }>;
  searchParams?: Promise<{ desde?: string | string[]; saneamiento?: string | string[] }>;
}) {
  const { id } = await params;
  const consulta = await searchParams;
  const valor = consulta?.desde;
  const desde = (Array.isArray(valor) ? valor[0] : valor) ?? null;
  return <TableroLote diagnosticoId={id} desde={desde} saneamiento={consulta?.saneamiento ?? null} />;
}
