// Contenedor visual común de las pantallas de autenticación.
import Link from "next/link";

export function AuthCard({
  title,
  subtitle,
  children,
  backHref = "/login",
}: {
  title: string;
  subtitle?: string;
  children: React.ReactNode;
  /** Destino del enlace "volver". `null` lo oculta (p. ej. en pantallas internas). */
  backHref?: string | null;
}) {
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
