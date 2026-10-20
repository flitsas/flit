import { authRoutes } from "@/lib/auth.server";

// @flit/auth (A-09): /auth/refresh de Comparendos como cliente `comparendos`.
export const dynamic = "force-dynamic";

export function POST(request: Request): Promise<Response> {
  return authRoutes.refresh(request);
}
