// Contenedor visual común de las pantallas de autenticación.
import Link from "next/link";
import { BrandLogo } from "@/components/brand/BrandLogo";

export function AuthCard({
  title,
  subtitle,
  children,
  backHref = "/login",
  variant = "auth",
}: {
  title: string;
  subtitle?: string;
  children: React.ReactNode;
  /** Destino del enlace "volver". `null` lo oculta (p. ej. en pantallas internas). */
  backHref?: string | null;
  /**
   * `auth` (por defecto): fondo plano de las pantallas de acceso.
   * `overlay` (HU #10496, AC3): mismo tratamiento desenfocado y consistente de las
   * modales — fondo de la app tenue con `backdrop-blur` y tarjeta con panel dark-aware,
   * para páginas internas como "Cambiar contraseña".
   */
  variant?: "auth" | "overlay";
}) {
  if (variant === "overlay") {
    return (
      <main className="app-bg min-h-screen">
        <div className="flex min-h-screen items-center justify-center bg-flit-primary/50 dark:bg-background/70 px-4 backdrop-blur-md">
          <div className="w-full max-w-md rounded-2xl border border-border bg-card p-8 text-flit-primary shadow-[var(--shadow-modal)]">
            <h1 className="mb-1 text-2xl font-bold text-flit-primary">{title}</h1>
            {subtitle && <p className="mb-6 text-sm text-muted-foreground">{subtitle}</p>}
            {children}
            {backHref && (
              <Link
                href={backHref}
                className="mt-6 inline-block text-sm text-flit-brand hover:underline"
              >
                ← Volver
              </Link>
            )}
          </div>
        </div>
      </main>
    );
  }

  return (
    <main className="min-h-screen flex items-center justify-center bg-flit-bg px-4">
      <div className="w-full max-w-md bg-card text-card-foreground rounded-2xl shadow-[var(--shadow-modal)] p-8">
        {/* HU #12419 AC2 — logo de la red (o FLIT sin cambios, AC7) en acceso/recuperación. */}
        <BrandLogo variant="dark" className="h-8 w-auto mb-4" />
        <h1 className="text-2xl font-bold text-flit-primary mb-1">{title}</h1>
        {subtitle && <p className="text-sm text-muted-foreground mb-6">{subtitle}</p>}
        {children}
        {backHref && (
          <Link
            href={backHref}
            className="mt-6 inline-block text-sm text-flit-brand hover:underline"
          >
            ← Volver
          </Link>
        )}
      </div>
    </main>
  );
}
