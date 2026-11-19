import { PaginaExcepcion } from "@flit/ui/PaginaExcepcion";
import { diagnosticoConfig } from "@/lib/config.server";

// Acceso negado: llega aquí desde @flit/auth (el hub negó el login, ?code=) o desde DiagnosticoShell (la API respondió
// 403 a GET /sesion). Está fuera del middleware, así que se muestra sin volver al login. «Ir a mis productos» vuelve
// al hub.
const MESSAGES: Record<string, string> = {
  PRODUCTO_NO_HABILITADO: "Diagnóstico no está habilitado para tu empresa o su suscripción está suspendida.",
  PRODUCT_NOT_ENABLED: "Diagnóstico no está habilitado para tu empresa.",
  SIN_PERMISOS_DIAGNOSTICO: "No tienes permisos en Diagnóstico. Pídele acceso a tu administrador.",
  PRODUCT_ROLE_REQUIRED: "No tienes un rol en Diagnóstico. Pídele acceso a tu administrador.",
  SIN_EMPRESA: "Tu usuario no tiene una empresa asociada. Pídele ayuda a tu administrador.",
  SESSION_INVALID: "Tu cuenta cambió o fue suspendida. Vuelve a iniciar sesión.",
};

export default async function Forbidden({ searchParams }: { searchParams: Promise<{ code?: string }> }) {
  const { code } = await searchParams;
  const { hubUrl } = diagnosticoConfig();
  return (
    <main className="flex min-h-full flex-col items-center justify-center">
      <PaginaExcepcion
        codigo={403}
        titulo="No tienes acceso"
        mensaje={(code && MESSAGES[code]) ?? "No tienes permiso para abrir esta página."}
        accion={{ etiqueta: "Ir a mis productos", href: `${hubUrl}/?inicio=1` }}
      />
    </main>
  );
}
