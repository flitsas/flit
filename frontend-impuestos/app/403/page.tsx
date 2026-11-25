import { PaginaExcepcion } from "@flit/ui/PaginaExcepcion";
import { impuestosConfig } from "@/lib/config.server";

// Acceso negado (AC-FE-08): llega aquí desde @flit/auth (el hub negó el login, ?code=) o desde ImpuestosShell (la API
// respondió 403 a GET /sesion). Está fuera del middleware, así que se muestra sin volver al login y sin ningún módulo.
// «Ir a mis productos» vuelve al hub. Un código desconocido nunca se muestra: cae en el texto general.
const MESSAGES: Record<string, string> = {
  // Literales de x-flit-literales.errores (impuestos.v1.yaml 1.1.0); PRODUCT_NOT_ENABLED es su alias del hub.
  PRODUCTO_NO_HABILITADO: "Impuestos no está activo para tu compañía.",
  PRODUCT_NOT_ENABLED: "Impuestos no está activo para tu compañía.",
  PRODUCTO_SUSPENDIDO: "Impuestos está suspendido para tu empresa. Pídele ayuda a tu administrador.",
  SIN_PERMISOS_IMPUESTOS: "No tienes un rol en Impuestos. Pídele acceso a tu administrador.",
  PRODUCT_ROLE_REQUIRED: "No tienes un rol en Impuestos. Pídele acceso a tu administrador.",
  SIN_EMPRESA: "Tu usuario no tiene una compañía asociada.",
  SESSION_INVALID: "Tu cuenta cambió o fue suspendida. Vuelve a iniciar sesión.",
};

export default async function Forbidden({ searchParams }: { searchParams: Promise<{ code?: string }> }) {
  const { code } = await searchParams;
  const { hubUrl } = impuestosConfig();
  const mensaje = code && Object.prototype.hasOwnProperty.call(MESSAGES, code) ? MESSAGES[code] : "No tienes permiso para abrir esta página.";
  return (
    <main className="flex min-h-full flex-col items-center justify-center">
      <PaginaExcepcion codigo={403} titulo="No tienes acceso" mensaje={mensaje} accion={{ etiqueta: "Ir a mis productos", href: `${hubUrl}/?inicio=1` }} />
    </main>
  );
}
