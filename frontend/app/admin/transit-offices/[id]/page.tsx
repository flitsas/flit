import { redirect } from "next/navigation";
import { otHubModulePath } from "@/components/admin/transit-offices/ot-nav";

interface OtHubIndexPageProps {
  params: Promise<{ id: string }>;
}

/**
 * Redirige el hub OT a su primer módulo visible. Era "Trámites", que se retiró de la navegación:
 * seguir apuntando ahí dejaba el hub abriendo en una pestaña que ya no se ofrece.
 */
export default async function OtHubIndexPage({ params }: OtHubIndexPageProps) {
  const { id } = await params;
  redirect(otHubModulePath(id, "client-procedures"));
}
