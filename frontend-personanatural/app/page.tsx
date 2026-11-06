import { Landing } from "@/components/landing/Landing";
import { MarcaWeb } from "@/components/marca/MarcaWeb";

// La marca de la web se lee en cada petición: lo publicado o retirado se ve al recargar.
export const dynamic = "force-dynamic";

export default function Inicio() {
  return (
    <MarcaWeb>
      <Landing />
    </MarcaWeb>
  );
}
