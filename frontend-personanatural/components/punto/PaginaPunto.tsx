import type { Metadata } from "next";
import { headers } from "next/headers";
import { cache } from "react";
import { EstiloMarca } from "@/components/marca/EstiloMarca";
import { ProveedorMarca } from "@/components/marca/MarcaPunto";
import { CanalPunto } from "@/components/punto/CanalPunto";
import { portalConfig } from "@/lib/config.server";
import type { MarcaAplicable } from "@/lib/marca/marca";
import { obtenerMarcaPunto } from "@/lib/marca/marca.server";
import { resolverDominioPortal } from "@/lib/punto/dominio.server";
import { ipDelCliente } from "@/lib/server/ip";

// Página del punto de un organismo. La sirven `/o/{codigo}` y la raíz de un dominio activo del organismo. La marca se
// lee en CADA petición: lo recién publicado o retirado se ve al abrir o recargar. Ni `revalidate` ni caché de datos
// (`fetch` va con `no-store`); `cache` de React solo evita pedir lo mismo dos veces dentro de la misma petición
// (metadatos y página).

export const decodificar = (crudo: string): string => {
  try {
    return decodeURIComponent(crudo);
  } catch {
    return crudo;
  }
};

export const marcaDe = cache(async (codigo: string): Promise<MarcaAplicable | null> => {
  const config = portalConfig();
  const ip = ipDelCliente(await headers(), config.saltosProxyConfianza);
  return obtenerMarcaPunto(codigo, { apiOrigin: config.apiOrigin, ipCliente: ip });
});

/**
 * Código del organismo dueño del dominio con que se abrió el portal, o `null` (host propio de FLIT, dominio no activo,
 * falla del servidor): la web de persona queda como hoy. Se resuelve por petición, sin caché.
 */
export const codigoDelDominio = cache(async (): Promise<string | null> => {
  const config = portalConfig();
  const cabeceras = await headers();
  return resolverDominioPortal(cabeceras.get("host"), {
    apiOrigin: config.apiOrigin,
    ipCliente: ipDelCliente(cabeceras, config.saltosProxyConfianza),
  });
});

export async function metadataPunto(codigo: string): Promise<Metadata> {
  const marca = await marcaDe(codigo);
  return {
    title: `Punto de atención | ${marca?.nombre ?? "FLIT"}`,
    robots: { index: false, follow: false },
  };
}

/** El código va en la ruta o lo da el dominio. Placa y documento no. La marca se lee antes (`marcaDe`) para que la primera pintura ya la lleve. */
export function PaginaPunto({ codigo, marca }: { codigo: string; marca: MarcaAplicable | null }) {
  return (
    <>
      <EstiloMarca marca={marca} />
      <ProveedorMarca marca={marca}>
        <CanalPunto codigo={codigo} />
      </ProveedorMarca>
    </>
  );
}
