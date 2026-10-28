import type { Metadata } from "next";
import { TramitePreparando } from "@/components/tramite/TramitePreparando";

export const metadata: Metadata = {
  title: "Tu trámite | FLIT 2.0",
  robots: { index: false, follow: false },
};

// Ruta del wizard por tipo de trámite (Feature F). Por ahora solo el marcador «Estamos preparando tu trámite»; el tipo y la
// identificación del vehículo no van en la URL ni se leen de ella: llegan por el contexto en memoria.
export default function PaginaTramite() {
  return <TramitePreparando />;
}
