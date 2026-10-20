import { HubLogin } from "@/components/auth/HubLogin";

// Login del hub (A-06, HU #12991). `returnUrl` lo pone el servidor OIDC al pedir el login (/connect/authorize);
// el backend vuelve a validar que sea una ruta relativa del mismo host.
export const dynamic = "force-dynamic";

export default async function LoginPage({ searchParams }: { searchParams: Promise<{ returnUrl?: string }> }) {
  const { returnUrl } = await searchParams;
  return <HubLogin returnUrl={returnUrl} />;
}
