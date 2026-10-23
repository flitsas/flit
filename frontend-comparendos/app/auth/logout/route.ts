import { authRoutes } from "@/lib/auth.server";

// @flit/auth (A-09): /auth/logout de Comparendos como cliente `comparendos`.
export const dynamic = "force-dynamic";

export function GET(request: Request): Promise<Response> {
  return authRoutes.logout(request);
}
export const POST = GET;
