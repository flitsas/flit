import { redirect } from "next/navigation";
import { getSession } from "@flit/auth/server";
import { ComparendosShell } from "@/components/ComparendosShell";
import { ToastProvider } from "@/components/Toast";
import { COMPARENDOS_PRODUCT } from "@/lib/auth.server";
import { comparendosConfig } from "@/lib/config.server";

// Todas las pantallas de la operación: con sesión del producto, dentro de la barra y el dock de la suite.
export const dynamic = "force-dynamic";

export default async function AppLayout({ children }: { children: React.ReactNode }) {
  const user = await getSession(COMPARENDOS_PRODUCT);
  if (!user) redirect("/auth/login?returnTo=/");

  const { tramitesUrl } = comparendosConfig();
  return (
    <ComparendosShell
      tramitesUrl={tramitesUrl}
      user={{
        email: user.email,
        tenantName: user.tenant.name,
        permissions: user.permissions.map((p) => p.toLowerCase()),
        roles: user.roles.map((r) => r.code),
        isSuperAdmin: user.isSuperAdmin,
      }}
    >
      <ToastProvider>{children}</ToastProvider>
    </ComparendosShell>
  );
}
