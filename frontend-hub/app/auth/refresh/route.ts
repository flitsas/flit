import { hubAuthRoutes } from "@/lib/auth.server";

// @flit/auth (A-09): /auth/refresh del hub como cliente `plataforma`.
export const dynamic = "force-dynamic";

export function POST(request: Request): Promise<Response> {
  return hubAuthRoutes.refresh(request);
}
