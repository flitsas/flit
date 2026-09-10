import { NextResponse, type NextRequest } from "next/server";
import { exigirSuperAdmin, llamarMigracion } from "@/lib/migracion/server";
import { INSTANCIAS, TIPOS_TRAMITE, type Instancia, type TipoTramite } from "@/lib/migracion/types";

/** Cuerpo que envía la consola. Un trámite por petición, igual que el host. */
interface PeticionMigrar {
  tramite?: unknown;
  v1Id?: unknown;
  instancias?: unknown;
  dryRun?: unknown;
  batchId?: unknown;
}

/**
 * Dispara la migración de UN trámite.
 *
 * Aquí SÍ se valida —al contrario que en la consulta de estado—, y por una razón concreta: esto
 * escribe. Un `tramite` o un `v1Id` que llegan malformados deben morir antes de que exista una
 * petición al host, no depender de que el host los rechace. Y `force` no se reenvía ni existe:
 * borra el trámite y sus hijos en cascada, y sigue siendo exclusivo de la consola por SSH.
 */
export async function POST(request: NextRequest) {
  const rechazo = await exigirSuperAdmin();
  if (rechazo) {
    return NextResponse.json(rechazo.cuerpo, { status: rechazo.estado });
  }

  let cuerpo: PeticionMigrar;
  try {
    cuerpo = (await request.json()) as PeticionMigrar;
  } catch {
    return malaPeticion("migracion.cuerpo_invalido", "El cuerpo de la petición no es JSON válido.");
  }

  const tramite = cuerpo.tramite;
  if (typeof tramite !== "string" || !TIPOS_TRAMITE.includes(tramite as TipoTramite)) {
    return malaPeticion(
      "migracion.tramite_desconocido",
      `'${String(tramite)}' no es un tipo de trámite. Válidos: ${TIPOS_TRAMITE.join(", ")}.`,
    );
  }

  // Number.isSafeInteger y no un parseInt: "26350abc" pasaría un parseInt y llegaría truncado al
  // host, que migraría un trámite distinto del que se pidió sin que nadie se entere.
  const v1Id = cuerpo.v1Id;
  if (typeof v1Id !== "number" || !Number.isSafeInteger(v1Id) || v1Id <= 0) {
    return malaPeticion(
      "migracion.id_invalido",
      `'${String(v1Id)}' no es un id de V1: se espera un entero positivo.`,
    );
  }

  const instancias = cuerpo.instancias;
  let seleccion: string | null = null;
  if (Array.isArray(instancias) && instancias.length > 0) {
    const invalida = instancias.find(
      (i) => typeof i !== "string" || !INSTANCIAS.includes(i as Instancia),
    );
    if (invalida !== undefined) {
      return malaPeticion(
        "migracion.instancia_desconocida",
        `'${String(invalida)}' no es una instancia. Válidas: ${INSTANCIAS.join(", ")}.`,
      );
    }
    seleccion = (instancias as Instancia[]).join(",");
  }

  const query = new URLSearchParams();
  if (seleccion) {
    query.set("instancias", seleccion);
  }
  // Explícito y no `if (dryRun)`: el valor por omisión del host es `false`, y quien opera merece
  // ver en la respuesta el modo que realmente corrió.
  query.set("dryRun", cuerpo.dryRun === true ? "true" : "false");

  if (typeof cuerpo.batchId === "string" && cuerpo.batchId.trim() !== "") {
    query.set("batchId", cuerpo.batchId.trim());
  }

  const respuesta = await llamarMigracion(
    `/api/v1/migracion/${tramite}/${v1Id}?${query.toString()}`,
    { method: "POST" },
  );

  return NextResponse.json(respuesta.cuerpo, { status: respuesta.estado });
}

function malaPeticion(title: string, detail: string) {
  return NextResponse.json({ title, detail, status: 400 }, { status: 400 });
}
