import type { Metadata } from "next";
import { Landing } from "@/components/landing/Landing";
import {
  codigoDelDominio,
  marcaDe,
  metadataPunto,
  PaginaPunto,
} from "@/components/punto/PaginaPunto";

// El host de la petición decide qué se pinta, así que la raíz se resuelve en cada petición (sin caché por tiempo).
export const dynamic = "force-dynamic";

/** Con el dominio activo de un organismo, la raíz es el punto de ese organismo; si no, la web de persona de siempre. */
export async function generateMetadata(): Promise<Metadata> {
  const codigo = await codigoDelDominio();
  return codigo ? metadataPunto(codigo) : {};
}

export default async function Inicio() {
  const codigo = await codigoDelDominio();
  return codigo ? <PaginaPunto codigo={codigo} marca={await marcaDe(codigo)} /> : <Landing />;
}
