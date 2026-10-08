import { headers } from "next/headers";
import Link from "next/link";
import { hubUrlFor } from "@flit/auth/server";
import { ShieldX } from "lucide-react";

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

export default async function ForbiddenPage({ searchParams }: { searchParams: Promise<{ code?: string }> }) {
  const { code } = await searchParams;
  const denied = code ? DENIED[code] : undefined;
  // Leída en cada petición (la página depende de la consulta): una sola imagen para los tres ambientes.
  // Con raíces alternativas (FLIT_HUB_URLS), el hub de la raíz por la que se entró: tramites.flitsas.com → app.flitsas.com.
  const primaryHub = (process.env.FLIT_HUB_URL || "").replace(/\/+$/, "");
  const hubUrls = [primaryHub, ...(process.env.FLIT_HUB_URLS ?? "").split(",").map((u) => u.trim().replace(/\/+$/, ""))].filter(Boolean);
  const host = (await headers()).get("host");
  const hubUrl = primaryHub && host ? hubUrlFor(host.split(":")[0], { hubUrl: primaryHub, hubUrls }) : primaryHub;
  const href = denied && hubUrl ? `${hubUrl}/?inicio=1` : "/";

  return (
    <main className="app-bg flex min-h-screen flex-col items-center justify-center gap-4 px-6 text-center">
      <ShieldX className="h-14 w-14" style={{ color: "#FF4E00" }} />
      <h1 className="text-2xl font-bold" style={{ color: "#162744" }}>
        {denied?.title ?? "Acceso restringido"}
      </h1>
      <p className="max-w-md text-sm opacity-70">
        {denied?.body ??
          "No tienes los permisos necesarios para acceder a esta sección. Si crees que es un error, contacta al administrador de la plataforma."}
      </p>
      <Link
        href={href}
        className="rounded-xl px-5 py-2.5 text-sm font-semibold text-white"
        style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
      >
        {denied && hubUrl ? "Ir a mis productos" : "Volver al inicio"}
      </Link>
    </main>
  );
}
