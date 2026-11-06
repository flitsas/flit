import type { Metadata } from "next";
import { headers } from "next/headers";
import { cache } from "react";
import { EstiloMarca } from "@/components/marca/EstiloMarca";
import { ProveedorMarca } from "@/components/marca/MarcaPunto";
import { CanalPunto } from "@/components/punto/CanalPunto";
import { portalConfig } from "@/lib/config.server";
import type { MarcaAplicable } from "@/lib/marca/marca";
import { obtenerMarcaPunto } from "@/lib/marca/marca.server";
import { ipDelCliente } from "@/lib/server/ip";

// La marca del organismo se lee en CADA petición: lo recién publicado o retirado se ve al abrir o recargar. Ni `revalidate`
// ni caché de datos (`fetch` va con `no-store`); `cache` de React solo evita pedirla dos veces dentro de la misma petición
// (metadatos y página).
export const dynamic = "force-dynamic";

const decodificar = (crudo: string): string => {
  try {
    return decodeURIComponent(crudo);
  } catch {
    return crudo;
  }
};

const marcaDe = cache(async (codigo: string): Promise<MarcaAplicable | null> => {
  const config = portalConfig();
  const ip = ipDelCliente(await headers(), config.saltosProxyConfianza);
  return obtenerMarcaPunto(codigo, { apiOrigin: config.apiOrigin, ipCliente: ip });
});

export async function generateMetadata({ params }: { params: Promise<{ codigo: string }> }): Promise<Metadata> {
  const { codigo } = await params;
  const marca = await marcaDe(decodificar(codigo));
  return {
    title: `Punto de atención | ${marca?.nombre ?? "FLIT"}`,
    robots: { index: false, follow: false },
  };
}

/** El código va en la ruta. Placa y documento no. */
export default async function Pagina({ params }: { params: Promise<{ codigo: string }> }) {
  const { codigo: crudo } = await params;
  const codigo = decodificar(crudo);
  const marca = await marcaDe(codigo);
  return (
    <>
      <EstiloMarca marca={marca} />
      <ProveedorMarca marca={marca}>
        <CanalPunto codigo={codigo} />
      </ProveedorMarca>
    </>
  );
}
