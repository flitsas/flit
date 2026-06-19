import Link from "next/link";
import { ShieldX } from "lucide-react";

// Destino de acceso denegado (HU #10194, AC6). El middleware redirige aquí a los
// usuarios sin rol SuperAdmin antes de renderizar cualquier dato admin.
export default function ForbiddenPage() {
  return (
    <main className="app-bg flex min-h-screen flex-col items-center justify-center gap-4 px-6 text-center">
      <ShieldX className="h-14 w-14" style={{ color: "#FF4E00" }} />
      <h1 className="text-2xl font-bold" style={{ color: "#162744" }}>
        Acceso restringido
      </h1>
      <p className="max-w-md text-sm opacity-70">
        No tienes permisos de Super Administrador para acceder a esta sección. Si crees que es un
        error, contacta al administrador de la plataforma.
      </p>
      <Link
        href="/"
        className="rounded-xl px-5 py-2.5 text-sm font-semibold text-white"
        style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
      >
        Volver al inicio
      </Link>
    </main>
  );
}
