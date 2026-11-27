import { DetalleAprovisionamiento } from "@/components/backoffice/DetalleAprovisionamiento";

/** /backoffice/empresas/[tenantId] (plan §6.1, spec-visual §3.10): estado, credenciales, contrato e historial (solo Super Admin). */
export default async function PaginaDetalleBackoffice({ params }: { params: Promise<{ tenantId: string }> }) {
  const { tenantId } = await params;
  return <DetalleAprovisionamiento tenantId={tenantId} />;
}
