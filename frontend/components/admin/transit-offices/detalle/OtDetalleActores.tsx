"use client";

import { OtCampo, OtListaCampos, OtTarjeta, OtVacio } from "./OtDetallePrimitivos";
import type { OtClientProcedure, OtClientProcedureActor } from "@/lib/api/types-ot";

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
  const presentes = new Set(
    (procedure.actors ?? []).map((a) => a.actorType.toLowerCase()),
  );
  return actoresEsperados(procedure.procedureTypeName).filter((t) => !presentes.has(t));
}

function ActorCard({ actor }: { actor: OtClientProcedureActor }) {
  const documento = [actor.documentType, actor.documentNumber].filter(Boolean).join(" ");

  return (
    <OtTarjeta titulo={actorTypeLabel(actor.actorType)}>
      <OtListaCampos>
        <OtCampo campo="Nombre" valor={actor.fullName} />
        <OtCampo campo="Documento" valor={documento} />
        <OtCampo campo="Persona" valor={personTypeLabel(actor.personType)} />
        <OtCampo campo="Correo" valor={actor.email} />
        <OtCampo campo="Teléfono" valor={actor.phone} />
      </OtListaCampos>
    </OtTarjeta>
  );
}

/**
 * Sección «Actores» del modal de detalle del OT. Pinta lo que ya trae el detalle: el OT no accede a
 * `GET .../actors` del runtime de trámites —ese endpoint está acotado al tenant de la empresa— así
 * que dirección, ciudad y representante legal embebido no forman parte de esta vista.
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

  return (
    <div className="flex flex-col gap-3">
      <div className="grid gap-4 md:grid-cols-2">
        {actors.map((actor, index) => (
          <ActorCard
            key={`${actor.actorType}-${actor.documentNumber}-${index}`}
            actor={actor}
          />
        ))}
      </div>
      {faltantes.length > 0 ? (
        <p className="text-xs font-medium" style={{ color: "#B45309" }} role="alert">
          Faltan actores esperados para este tipo de trámite:{" "}
          {faltantes.map(actorTypeLabel).join(", ")}.
        </p>
      ) : null}
    </div>
  );
}
