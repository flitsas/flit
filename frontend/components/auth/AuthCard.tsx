// Contenedor visual común de las pantallas de autenticación.
import Link from "next/link";

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
        <div className="flex min-h-screen items-center justify-center bg-slate-900/50 px-4 backdrop-blur-md">
          <div className="w-full max-w-md rounded-2xl border border-[#DFE5ED] bg-white p-8 text-[#162744] shadow-2xl dark:border-white/10 dark:bg-[#0B0F14] dark:text-white">
            <h1 className="mb-1 text-2xl font-bold text-[#162744] dark:text-white">{title}</h1>
            {subtitle && <p className="mb-6 text-sm text-slate-500 dark:text-white/60">{subtitle}</p>}
            {children}
            {backHref && (
              <Link
                href={backHref}
                className="mt-6 inline-block text-sm text-[#557eff] hover:underline"
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
    <main className="min-h-screen flex items-center justify-center bg-[#eef5ff] px-4">
      <div className="w-full max-w-md bg-white rounded-2xl shadow-xl p-8">
        <h1 className="text-2xl font-bold text-[#162744] mb-1">{title}</h1>
        {subtitle && <p className="text-sm text-slate-500 mb-6">{subtitle}</p>}
        {children}
        {backHref && (
          <Link
            href={backHref}
            className="mt-6 inline-block text-sm text-[#557eff] hover:underline"
          >
            ← Volver
          </Link>
        )}
      </div>
    </main>
  );
}
