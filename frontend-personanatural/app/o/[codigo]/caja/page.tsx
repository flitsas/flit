import type { Metadata } from "next";
import { EstiloMarca } from "@/components/marca/EstiloMarca";
import { ProveedorMarca } from "@/components/marca/MarcaPunto";
import { PortalCaja } from "@/components/caja/PortalCaja";
import { decodificar, marcaDe } from "@/components/punto/PaginaPunto";

// Caja del organismo (ADR-0082). Protegida por middleware.ts: sin sesión del producto `caja` nunca llega aquí. La marca se
// lee en cada petición, igual que en el punto de atención.
export const dynamic = "force-dynamic";

export const metadata: Metadata = {
  title: "Caja del organismo | FLIT",
  robots: { index: false, follow: false },
};

export default async function PaginaCaja({ params }: { params: Promise<{ codigo: string }> }) {
  const codigo = decodificar((await params).codigo);
  const marca = await marcaDe(codigo);
  return (
    <>
      <EstiloMarca marca={marca} />
      <ProveedorMarca marca={marca}>
        <PortalCaja codigo={codigo} />
      </ProveedorMarca>
    </>
  );
}
