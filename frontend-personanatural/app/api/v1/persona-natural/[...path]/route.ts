import { cookies, headers } from "next/headers";
import { NextResponse } from "next/server";
import { portalConfig } from "@/lib/config.server";
import { COOKIE_SESION, reenviarAlBff } from "@/lib/server/proxy";

// /api/v1/persona-natural/* del navegador → gateway → BFF del portal. El token de la sesión del trámite vive en una
// cookie HttpOnly: aquí se guarda al abrir o reanudar un trámite y se adjunta al reenviar; el navegador nunca lo ve.
export const dynamic = "force-dynamic";

type Context = { params: Promise<{ path: string[] }> };

async function handle(request: Request, context: Context): Promise<Response> {
  const { path } = await context.params;
  const config = portalConfig();
  const jar = await cookies();
  const cabeceras = await headers();
  const ip = cabeceras.get("x-forwarded-for")?.split(",")[0]?.trim() || cabeceras.get("x-real-ip") || undefined;

  const resultado = await reenviarAlBff(request, path, {
    apiOrigin: config.apiOrigin,
    tokenSesion: jar.get(COOKIE_SESION)?.value,
    ipCliente: ip,
  });

  const respuesta = new NextResponse(resultado.cuerpo, { status: resultado.status, headers: resultado.headers });
  if (resultado.cookie?.accion === "fijar") {
    respuesta.cookies.set(COOKIE_SESION, resultado.cookie.valor, {
      httpOnly: true,
      secure: config.cookieSegura,
      sameSite: "strict",
      path: "/api/v1/persona-natural",
      expires: resultado.cookie.expiraEn,
    });
  } else if (resultado.cookie?.accion === "borrar") {
    respuesta.cookies.set(COOKIE_SESION, "", { httpOnly: true, secure: config.cookieSegura, sameSite: "strict", path: "/api/v1/persona-natural", maxAge: 0 });
  }
  return respuesta;
}

export const GET = handle;
export const POST = handle;
export const PUT = handle;
export const PATCH = handle;
export const DELETE = handle;
