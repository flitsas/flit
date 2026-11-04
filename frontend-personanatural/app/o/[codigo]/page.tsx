import type { Metadata } from "next";
import { CanalPunto } from "@/components/punto/CanalPunto";

export const metadata: Metadata = {
  title: "Punto de atención | FLIT",
  robots: { index: false, follow: false },
};

/** El código va en la ruta. Placa y documento no. */
export default async function Pagina({ params }: { params: Promise<{ codigo: string }> }) {
  const { codigo: crudo } = await params;
  let codigo = crudo;
  try {
    codigo = decodeURIComponent(crudo);
  } catch {
    codigo = crudo;
  }
  return <CanalPunto codigo={codigo} />;
}
