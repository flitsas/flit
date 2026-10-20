import { hubAuthRoutes } from "@/lib/auth.server";

// HU #13004: /auth/frontchannel-logout de @flit/auth. La página de cierre de sesión del hub lo abre en segundo plano y
// el hub borra su propia sesión de cliente `plataforma` al instante, también si se cerró desde otro producto.
export const dynamic = "force-dynamic";

export function GET(request: Request): Promise<Response> {
  return hubAuthRoutes.frontchannelLogout(request);
}
