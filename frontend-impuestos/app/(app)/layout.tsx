import { Suspense } from "react";
import { redirect } from "next/navigation";
import { getSession } from "@flit/auth/server";
import { VehicleLoader } from "@flit/ui/Loader";
import { ImpuestosShell } from "@/components/ImpuestosShell";
import { IMPUESTOS_PRODUCT } from "@/lib/auth.server";
import { impuestosConfig } from "@/lib/config.server";

// Todas las pantallas del producto: con sesión de Impuestos, dentro de la barra y el dock de la suite. El Suspense
// cubre el `useSearchParams` del selector de compañía (?empresa=).
export const dynamic = "force-dynamic";

export default async function AppLayout({ children }: { children: React.ReactNode }) {
  const user = await getSession(IMPUESTOS_PRODUCT);
  if (!user) redirect("/auth/login?returnTo=/");

  const { tramitesUrl } = impuestosConfig();
  return (
    <Suspense fallback={<VehicleLoader label="Abriendo Impuestos…" />}>
      <ImpuestosShell
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
      </ImpuestosShell>
    </Suspense>
  );
}
