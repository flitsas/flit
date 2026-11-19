import { AccesoBackoffice } from "@/components/backoffice/AccesoBackoffice";
import { FuentesObligatoriasBackoffice } from "@/components/backoffice/FuentesObligatoriasBackoffice";

/** Fuentes obligatorias por familia o tipo (ADR-0078). Sin ?empresa=: el catálogo es de FLIT. */
export default function PaginaFuentesObligatorias() {
  return (
    <AccesoBackoffice>
      <FuentesObligatoriasBackoffice />
    </AccesoBackoffice>
  );
}
