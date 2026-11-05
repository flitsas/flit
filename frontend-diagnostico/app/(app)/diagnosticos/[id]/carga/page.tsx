import { CargaMasiva } from "@/components/carga/CargaMasiva";

export default async function CargaDiagnostico({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <CargaMasiva diagnosticoId={id} />;
}
