"use client";

import type { OtClientProcedure, OtClientProcedureActor } from "@/lib/api/types-ot";
import { formatDocumentWithType } from "@/lib/display/document-number";
import { OtRejilla, OtVacio } from "./OtDetallePrimitivos";

const ACTOR_TYPE_LABELS: Record<string, string> = {
  comprador: "Comprador",
  vendedor: "Vendedor",
  propietario: "Propietario",
  mandatario: "Mandatario",
  representante_legal: "Representante legal",
  locatario: "Locatario",
};

function actorTypeLabel(type: string): string {
  return ACTOR_TYPE_LABELS[type] ?? type;
}

function personTypeLabel(type: string | null | undefined): string {
  if (type === "natural") return "Natural";
  if (type === "juridical") return "Jurídica";
  return "";
}

/** Un traspaso necesita las dos puntas; el resto de trámites solo al comprador. */
function actoresEsperados(procedureTypeName: string | null | undefined): string[] {
  const esTraspaso = (procedureTypeName ?? "").toLowerCase().includes("traspaso");
  return esTraspaso ? ["comprador", "vendedor"] : ["comprador"];
}

function actoresFaltantes(procedure: OtClientProcedure): string[] {
  const presentes = new Set((procedure.actors ?? []).map((a) => a.actorType.toLowerCase()));
  return actoresEsperados(procedure.procedureTypeName).filter((t) => !presentes.has(t));
}

/**
 * Cuando el mismo papel se repite —dos compradores, dos propietarios— se numera.
 *
 * Sin esto, dos filas rotuladas «Comprador» a secas obligan a mirar el documento para saber cuál es
 * cuál, que es justo lo que una tabla debería ahorrar. Es la misma numeración del prototipo, pero
 * generalizada a cualquier papel en vez de solo a comprador y vendedor.
 */
function conRolNumerado(actors: OtClientProcedureActor[]): { actor: OtClientProcedureActor; rol: string }[] {
  const totales = new Map<string, number>();
  for (const a of actors) {
    totales.set(a.actorType, (totales.get(a.actorType) ?? 0) + 1);
  }

  const vistos = new Map<string, number>();
  return actors.map((actor) => {
    const base = actorTypeLabel(actor.actorType);
    const total = totales.get(actor.actorType) ?? 1;
    if (total === 1) return { actor, rol: base };
    const n = (vistos.get(actor.actorType) ?? 0) + 1;
    vistos.set(actor.actorType, n);
    return { actor, rol: `${base} ${n}` };
  });
}

/** Correo y teléfono cuelgan del nombre, atenuados: identifican a la persona, no la clasifican. */
function Contacto({ actor }: { actor: OtClientProcedureActor }) {
  const lineas = [actor.email?.trim(), actor.phone?.trim()].filter(Boolean);
  if (lineas.length === 0) return null;

  return (
    <span className="mt-0.5 block text-[10px] font-normal opacity-60">{lineas.join(" · ")}</span>
  );
}

/**
 * Acordeón «Actores del Trámite» (HU #12061): una tabla de tres columnas —documento, nombre y papel
 * en el trámite— en vez de una tarjeta por persona.
 *
 * El prototipo lleva dos columnas más, «Firma de la persona» y «Validación», que aquí NO se pintan:
 * son literales inventados del mockup y no existen en el contrato del OT. El organismo lee por su
 * propia puerta (`/admin/ot/client-procedures`) y no alcanza `GET .../actors` del runtime de
 * trámites —acotado al tenant de la empresa—, así que dirección, ciudad y representante legal
 * embebido tampoco forman parte de esta vista.
 */
export function OtDetalleActores({ procedure }: { procedure: OtClientProcedure }) {
  const actors = procedure.actors ?? [];
  const faltantes = actoresFaltantes(procedure);

  if (actors.length === 0) {
    return (
      <OtVacio
        mensaje={
          faltantes.length > 0
            ? `Este trámite no tiene actores registrados. Se esperan: ${faltantes.map(actorTypeLabel).join(", ")}.`
            : "Este trámite no tiene actores registrados."
        }
      />
    );
  }

  const filas = conRolNumerado(actors).map(({ actor, rol }) => [
    formatDocumentWithType(actor.documentType, actor.documentNumber) || "—",
    <span key="nombre" className="block min-w-0">
      {actor.fullName}
      <Contacto actor={actor} />
    </span>,
    [rol, personTypeLabel(actor.personType)].filter(Boolean).join(" · "),
  ]);

  return (
    <div className="flex flex-col gap-3">
      <OtRejilla
        etiqueta="Actores del trámite"
        columnas={["Documento", "Nombre completo", "Tipo de actor"]}
        plantilla="minmax(0,1fr) minmax(0,1.6fr) minmax(0,1fr)"
        filas={filas}
      />
      {faltantes.length > 0 ? (
        <p className="text-xs font-medium" style={{ color: "#B45309" }} role="alert">
          Faltan actores esperados para este tipo de trámite:{" "}
          {faltantes.map(actorTypeLabel).join(", ")}.
        </p>
      ) : null}
    </div>
  );
}
