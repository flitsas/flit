import type { Metadata } from "next";
import { decodificar, marcaDe, metadataPunto, PaginaPunto } from "@/components/punto/PaginaPunto";

// La marca del organismo se lee en CADA petición (ver PaginaPunto): sin `revalidate`.
export const dynamic = "force-dynamic";

export async function generateMetadata({
  params,
}: {
  params: Promise<{ codigo: string }>;
}): Promise<Metadata> {
  const { codigo } = await params;
  return metadataPunto(decodificar(codigo));
}

export default async function Pagina({ params }: { params: Promise<{ codigo: string }> }) {
  const { codigo } = await params;
  const codigoPunto = decodificar(codigo);
  return <PaginaPunto codigo={codigoPunto} marca={await marcaDe(codigoPunto)} />;
}
