// HU #14403 (Feature #14400) — Marca de la web de persona. Componente de servidor: lee la marca en cada petición (sin
// caché) y la deja en el HTML antes de la primera pintura. Solo lo usan las páginas de la web (raíz y trámite): el punto
// del organismo (`/o/{codigo}`) lee su propia marca y nunca pasa por aquí. Sin marca o ante una falla, no añade nada.
import { headers } from "next/headers";
import type { ReactNode } from "react";
import { portalConfig } from "@/lib/config.server";
import { obtenerMarcaWeb } from "@/lib/marca/marca.server";
import { ipDelCliente } from "@/lib/server/ip";
import { EstiloMarca } from "./EstiloMarca";
import { ProveedorMarca } from "./MarcaPunto";

export async function MarcaWeb({ children }: { children: ReactNode }) {
  const config = portalConfig();
  const marca = await obtenerMarcaWeb({
    apiOrigin: config.apiOrigin,
    ipCliente: ipDelCliente(await headers(), config.saltosProxyConfianza),
  });
  return (
    <>
      <EstiloMarca marca={marca} />
      <ProveedorMarca marca={marca}>{children}</ProveedorMarca>
    </>
  );
}
