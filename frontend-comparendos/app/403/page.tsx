import { comparendosConfig } from "@/lib/config.server";

// Acceso negado por el servidor OIDC: @flit/auth trae aquí el código. «Ir a mis productos» vuelve al hub.
const MESSAGES: Record<string, string> = {
  PRODUCT_NOT_ENABLED: "Comparendos no está habilitado para tu empresa.",
  PRODUCT_ROLE_REQUIRED: "No tienes un rol en Comparendos. Pídele acceso a tu administrador.",
  SESSION_INVALID: "Tu cuenta cambió o fue suspendida. Vuelve a iniciar sesión.",
};

export default async function Forbidden({ searchParams }: { searchParams: Promise<{ code?: string }> }) {
  const { code } = await searchParams;
  const { hubUrl } = comparendosConfig();
  return (
    <main className="flex min-h-full flex-col items-center justify-center gap-4 px-6 py-16 text-center">
      <h1 className="text-2xl font-semibold text-flit-primary">No tienes acceso</h1>
      <p className="max-w-md text-slate-600">{(code && MESSAGES[code]) ?? "No tienes permiso para abrir esta página."}</p>
      <a href={`${hubUrl}/?inicio=1`} className="text-flit-brand underline">
        Ir a mis productos
      </a>
    </main>
  );
}
