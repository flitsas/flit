import Link from "next/link";
import { buttonClass } from "@flit/ui/Button";
import { IconoModulo } from "@flit/ui/Icono";
import { getHubUrl } from "@flit/auth/server";

// Destino de acceso denegado (HU #10194, AC6). El middleware redirige aquí a los usuarios sin rol SuperAdmin antes de
// renderizar cualquier dato admin.
//
// Con la sesión de la suite (A-10), @flit/auth también trae aquí el código con el que el hub se negó a emitir el
// token de Trámites. En ese caso volver a «/» sería un bucle (Trámites pide sesión y el hub la vuelve a negar): se
// ofrece volver a los productos del usuario en el hub.
const DENIED: Record<string, { title: string; body: string }> = {
  PRODUCT_NOT_ENABLED: {
    title: "Tu empresa no tiene Trámites",
    body: "Pide al administrador de tu empresa que lo habilite. Mientras tanto puedes volver a tus productos.",
  },
  PRODUCT_ROLE_REQUIRED: {
    title: "No tienes un rol en Trámites",
    body: "Tu empresa tiene Trámites, pero tu usuario no tiene un rol en él. Pídele acceso al administrador de tu empresa.",
  },
  SESSION_INVALID: {
    title: "Tu sesión ya no es válida",
    body: "Tu cuenta cambió o fue suspendida. Vuelve a iniciar sesión o contacta al administrador de tu empresa.",
  },
};

/** En el dominio de una red de Marca Blanca, el hub de esa red; si no, FLIT_HUB_URL. */
async function hubUrlForRequest(): Promise<string> {
  const configured = (process.env.FLIT_HUB_URL || "").replace(/\/+$/, "");
  if (!configured) return "";
  try {
    return await getHubUrl("tramites");
  } catch {
    // Sin la configuración de la sesión de la suite (modo legacy) se queda el hub configurado.
    return configured;
  }
}

export default async function ForbiddenPage({ searchParams }: { searchParams: Promise<{ code?: string }> }) {
  const { code } = await searchParams;
  const denied = code ? DENIED[code] : undefined;
  // Leída en cada petición (la página depende de la consulta): una sola imagen para los tres ambientes.
  const hubUrl = denied ? await hubUrlForRequest() : "";
  const href = denied && hubUrl ? `${hubUrl}/?inicio=1` : "/";

  return (
    <main className="app-bg flex min-h-screen flex-col items-center justify-center px-4 py-10 text-center">
      {/* Homologación (HU #14511): estado vacío de la propuesta (`.vacio`), tarjeta con ilustración y botón del kit. */}
      <div className="flex w-full max-w-lg flex-col items-center gap-4 rounded-2xl border border-border bg-card px-6 py-10 shadow-[var(--shadow-card)] sm:px-10">
        <IconoModulo clave="seguridad" disco size={96} />
        <h1 className="text-2xl font-bold text-[color:var(--di-heading)]">
          {denied?.title ?? "Acceso restringido"}
        </h1>
        <p className="max-w-md text-sm text-muted-foreground">
          {denied?.body ??
            "No tienes los permisos necesarios para acceder a esta sección. Si crees que es un error, contacta al administrador de la plataforma."}
        </p>
        <Link href={href} className={buttonClass({ className: "mt-2" })}>
          {denied && hubUrl ? "Ir a mis productos" : "Volver al inicio"}
        </Link>
      </div>
    </main>
  );
}
