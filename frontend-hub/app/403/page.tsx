import Link from "next/link";
import { buttonClass } from "@flit/ui/Button";
import { IconoModulo } from "@flit/ui/Icono";

// Acceso negado por el servidor OIDC (A-07): @flit/auth trae aquí el código (PRODUCT_NOT_ENABLED, …). «Volver» va a
// ?inicio=1: «/» sin él llevaría a quien tiene un solo producto de vuelta a ese producto (entrada directa, B-11).
const MESSAGES: Record<string, string> = {
  PRODUCT_NOT_ENABLED: "Este producto no está habilitado para tu empresa.",
  PRODUCT_ROLE_REQUIRED: "No tienes un rol asignado en este producto. Pídele acceso a tu administrador.",
  SESSION_INVALID: "Tu cuenta cambió o fue suspendida. Vuelve a iniciar sesión.",
};

export default async function Forbidden({ searchParams }: { searchParams: Promise<{ code?: string }> }) {
  const { code } = await searchParams;
  return (
    <main className="flex min-h-full flex-col items-center justify-center bg-[color:var(--di-surface)] px-4 py-16 text-center">
      {/* Homologación (HU #14511): estado vacío de la propuesta (`.vacio`), igual que la 403 de Trámites. */}
      <div className="flex w-full max-w-lg flex-col items-center gap-4 rounded-[20px] border border-border bg-card px-6 py-10 shadow-[var(--shadow-card)] sm:px-10">
        <IconoModulo clave="seguridad" disco size={96} />
        <h1 className="text-2xl font-bold text-[color:var(--di-heading)]">No tienes acceso</h1>
        <p className="max-w-md text-muted-foreground">{(code && MESSAGES[code]) ?? "No tienes permiso para abrir esta página."}</p>
        <Link href="/?inicio=1" className={buttonClass({ className: "mt-2" })}>
          Ir a mis productos
        </Link>
      </div>
    </main>
  );
}
