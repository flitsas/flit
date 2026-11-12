import { Check } from "lucide-react";
import { ActivateAccountForm } from "@/components/auth/ActivateAccountForm";
import { AuthCard } from "@/components/auth/AuthCard";
import { brandDisplayName } from "@/lib/brand/types";
import { resolveBrand } from "@/lib/brand/resolve-brand.server";
import { INGRESO_MARCA_TEXTO_CLS, INGRESO_MARCA_TITULO_CLS } from "@flit/ui/ingreso-clases";

export const metadata = { title: "Activar cuenta — FLIT" };

interface PageProps {
  searchParams: Promise<{ token?: string }>;
}

// Homologación (HU #14511): sobre el armazón del Ingreso, con sus propios textos en el panel de tinta (prop `marca`).
export default async function ActivateAccountPage({ searchParams }: PageProps) {
  const { token } = await searchParams;
  // HU #12419 AC2 — logo y nombre de la red (o FLIT sin cambios, AC7). `resolveBrand()` está
  // cacheado por request: no repite el fetch que ya hizo el layout raíz.
  const brand = await resolveBrand();
  const platformName = brandDisplayName(brand, "FLIT");

  return (
    <AuthCard
      title="Activa tu cuenta"
      subtitle="Define tu contraseña para completar el registro."
      backHref={null}
      marca={
        <>
          <h2 className={INGRESO_MARCA_TITULO_CLS}>Bienvenido a {platformName}</h2>
          <p className={INGRESO_MARCA_TEXTO_CLS}>Activa tu cuenta para comenzar<br />a gestionar trámites de movilidad.</p>
          <div className="mt-6 flex flex-col gap-3">
            {[
              "Gestión de trámites en tiempo real",
              "Validación biométrica integrada",
              "Reportes y analíticas avanzadas",
            ].map((item) => (
              <div key={item} className="flex items-center gap-3 text-sm text-[color:var(--di-ingreso-texto-2)]">
                <span className="grid h-5 w-5 shrink-0 place-items-center rounded-full bg-[color:var(--di-ingreso-acento-2)] text-[color:var(--di-ingreso-fondo)]">
                  <Check className="h-3 w-3" strokeWidth={2.5} aria-hidden="true" />
                </span>
                {item}
              </div>
            ))}
          </div>
        </>
      }
    >
      <ActivateAccountForm token={token ?? null} />
    </AuthCard>
  );
}
