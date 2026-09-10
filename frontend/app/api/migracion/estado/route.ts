import { NextResponse, type NextRequest } from "next/server";
import { exigirSuperAdmin, llamarMigracion } from "@/lib/migracion/server";

/**
 * Consulta de solo lectura: qué ids de la lista ya están migrados.
 *
 * Los parámetros se reenvían tal cual y la validación se deja al host —es él quien conoce los
 * tipos de trámite y el tope de ids—. Duplicarla aquí solo crearía dos verdades que se
 * desincronizan; el trabajo de este handler es la llave, no las reglas.
 */
export async function GET(request: NextRequest) {
  const rechazo = await exigirSuperAdmin();
  if (rechazo) {
    return NextResponse.json(rechazo.cuerpo, { status: rechazo.estado });
  }

  const { searchParams } = request.nextUrl;
  const tramite = searchParams.get("tramite") ?? "";
  const ids = searchParams.get("ids") ?? "";

  // `tramite` va en la RUTA del host, así que se sanea aquí aunque el host valide: sin esto, un
  // valor con barras o `..` podría apuntar a otra ruta del host. El host respondería 404, pero la
  // petición no debería llegar a formularse.
  if (!/^[a-z]+$/i.test(tramite)) {
    return NextResponse.json(
      {
        title: "migracion.tramite_desconocido",
        detail: `'${tramite}' no es un tipo de trámite. Válidos: registration, transfer.`,
        status: 400,
      },
      { status: 400 },
    );
  }

  const { cuerpo, estado } = await llamarMigracion(
    `/api/v1/migracion/estado/${tramite}?ids=${encodeURIComponent(ids)}`,
    { method: "GET" },
  );

  return NextResponse.json(cuerpo, { status: estado });
}
