import { TableroLote } from "@/components/tablero/TableroLote";

/** Tablero del lote (HU #13748): destino de la confirmación de la carga masiva y del Express. */
export default async function TableroDiagnostico({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <TableroLote diagnosticoId={id} />;
}
