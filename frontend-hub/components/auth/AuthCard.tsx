import Link from "next/link";
import { BrandLogo } from "@flit/brand/BrandLogo";

// Contenedor de las pantallas de recuperación del hub (mismo que el de Trámites, components/auth/AuthCard.tsx).
export function AuthCard({ title, subtitle, children, backHref = "/login" }: { title: string; subtitle?: string; children: React.ReactNode; backHref?: string | null }) {
  return (
    <main className="flex min-h-screen items-center justify-center bg-flit-bg px-4">
      <div className="w-full max-w-md rounded-2xl bg-white p-8 shadow-xl">
        <BrandLogo variant="dark" className="mb-4 h-8 w-auto" />
        <h1 className="mb-1 text-2xl font-bold text-flit-primary">{title}</h1>
        {subtitle && <p className="mb-6 text-sm text-slate-500">{subtitle}</p>}
        {children}
        {backHref && (
          <Link href={backHref} className="mt-6 inline-block text-sm text-flit-brand hover:underline">
            ← Volver
          </Link>
        )}
      </div>
    </main>
  );
}

export const INPUT_CLASS =
  "w-full bg-white border border-slate-200 rounded-xl px-3 py-2.5 text-sm outline-none transition focus:border-flit-brand focus:ring-2 focus:ring-flit-brand/20";

export const SUBMIT_CLASS =
  "w-full rounded-xl bg-flit-brand py-3 text-sm font-semibold text-white transition hover:opacity-90 disabled:opacity-60";
