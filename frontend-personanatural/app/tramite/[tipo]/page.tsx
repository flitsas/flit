import type { Metadata } from "next";
import { MarcaWeb } from "@/components/marca/MarcaWeb";
import { PaginaTramite } from "@/components/tramite/PaginaTramite";

// La marca de la web se lee en cada petición (sin caché).
export const dynamic = "force-dynamic";

export const metadata: Metadata = {
  title: "Tu trámite | FLIT Persona Natural",
  robots: { index: false, follow: false },
};

// Ruta del wizard por tipo de trámite. En la URL va solo el código del tipo: la cédula, la placa y el radicado llegan por el
// contexto en memoria (o se piden de nuevo al reanudar) y nunca se leen de la URL.
export default async function Pagina({ params }: { params: Promise<{ tipo: string }> }) {
  const { tipo } = await params;
  let codigo = tipo;
  try {
    codigo = decodeURIComponent(tipo);
  } catch {
    // Un código con % mal formado se usa tal cual: el catálogo no lo va a encontrar y la página dirá que no está disponible.
  }
  return (
    <MarcaWeb>
      <PaginaTramite tipoCodigo={codigo} />
    </MarcaWeb>
  );
}
