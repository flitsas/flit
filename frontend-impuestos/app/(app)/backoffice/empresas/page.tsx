import { EmpresasAprovisionamiento } from "@/components/backoffice/EmpresasAprovisionamiento";

/** /backoffice/empresas (plan §6.1, spec-visual §3.10): solo Super Admin. */
export default function PaginaEmpresasBackoffice() {
  return <EmpresasAprovisionamiento />;
}
