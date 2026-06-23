import { redirect } from "next/navigation";
import { otHubModulePath } from "@/components/admin/transit-offices/ot-nav";

interface OtHubIndexPageProps {
  params: Promise<{ id: string }>;
}

/** Redirige el hub OT al módulo Trámites por defecto (HU #10236). */
export default async function OtHubIndexPage({ params }: OtHubIndexPageProps) {
  const { id } = await params;
  redirect(otHubModulePath(id, "tramites"));
}
