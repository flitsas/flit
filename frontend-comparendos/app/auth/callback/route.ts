import { authRoutes } from "@/lib/auth.server";

// @flit/auth (A-09): /auth/callback de Comparendos como cliente `comparendos`.
export const dynamic = "force-dynamic";

export function GET(request: Request): Promise<Response> {
  return authRoutes.callback(request);
}
