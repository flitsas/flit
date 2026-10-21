import { redirect } from "next/navigation";
import { getSession } from "@flit/auth/server";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import { DIAGNOSTICO_PRODUCT } from "@/lib/auth.server";
import { diagnosticoConfig } from "@/lib/config.server";

// Todas las pantallas del producto: con sesión de Diagnóstico, dentro de la barra y el dock de la suite.
export const dynamic = "force-dynamic";

export default async function AppLayout({ children }: { children: React.ReactNode }) {
  const user = await getSession(DIAGNOSTICO_PRODUCT);
  if (!user) redirect("/auth/login?returnTo=/");

  const { tramitesUrl } = diagnosticoConfig();
  return (
    <DiagnosticoShell
      tramitesUrl={tramitesUrl}
      user={{
        email: user.email,
        tenantName: user.tenant.name,
        permissions: user.permissions.map((p) => p.toLowerCase()),
        roles: user.roles.map((r) => r.code),
        isSuperAdmin: user.isSuperAdmin,
      }}
    >
      {children}
    </DiagnosticoShell>
  );
}
