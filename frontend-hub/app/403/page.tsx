import Link from "next/link";

// Acceso negado por el servidor OIDC (A-07): @flit/auth trae aquí el código (PRODUCT_NOT_ENABLED, …).
const MESSAGES: Record<string, string> = {
  PRODUCT_NOT_ENABLED: "Este producto no está habilitado para tu empresa.",
  PRODUCT_ROLE_REQUIRED: "No tienes un rol asignado en este producto. Pídele acceso a tu administrador.",
};

export default async function Forbidden({ searchParams }: { searchParams: Promise<{ code?: string }> }) {
  const { code } = await searchParams;
  return (
    <main className="flex min-h-full flex-col items-center justify-center gap-4 px-6 py-16 text-center">
      <h1 className="text-2xl font-semibold text-flit-primary">No tienes acceso</h1>
      <p className="max-w-md text-slate-600">{(code && MESSAGES[code]) ?? "No tienes permiso para abrir esta página."}</p>
      <Link href="/" className="text-flit-brand underline">
        Volver al inicio
      </Link>
    </main>
  );
}
