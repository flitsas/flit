"use client";

import { Button } from "@flit/ui/Button";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { RenglonesLectura } from "@/components/backoffice/RenglonesGestion";
import { ETIQUETA_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import type { SaneamientoBackoffice } from "@/lib/backoffice/saneamientos";
import { fechaCorta } from "@/lib/tablero/detalle";
import { ENLACE_PELIGRO_CLS } from "./comun";
import { GestionEnCurso } from "./GestionEnCurso";
import type { GestionSaneamientoEstado } from "./useGestionSaneamiento";

// PS-5 (CR-5): columna derecha, la gestión según el estado (spec-ui §6.2–§6.5): SOLICITADO con «Tomar», EN_GESTION con la
// edición, ANULADO con su motivo y los cerrados en lectura con «Reabrir».

interface PropsEstado {
  g: GestionSaneamientoEstado;
  san: SaneamientoBackoffice;
}

function GestionSolicitada({ g, san }: PropsEstado) {
  return (
    <div className="flex flex-col gap-3">
      <p className="text-sm text-flit-primary">{`La compañía pidió el saneamiento el ${fechaCorta(san.solicitado_en)}.`}</p>
      <div className="flex flex-wrap items-center gap-3">
        <Button type="button" size="sm" cargando={g.tomando} onClick={() => void g.tomar()}>
          {g.tomando ? "Tomando…" : "Tomar"}
        </Button>
        <button type="button" onClick={() => g.abrir({ tipo: "anular" })} className={ENLACE_PELIGRO_CLS}>
          Anular
        </button>
      </div>
      {g.errorTomar && (
        <div role="alert" className="flex flex-wrap items-center gap-2 text-sm font-semibold text-[color:var(--di-danger)]">
          <span>{g.errorTomar.texto}</span>
          {g.errorTomar.recargar && (
            <Button type="button" variant="ghost" size="sm" onClick={g.recargar}>
              Recargar
            </Button>
          )}
        </div>
      )}
    </div>
  );
}

function GestionCerrada({ g, san }: PropsEstado) {
  const motivos = [
    ...(san.motivo_cierre?.trim() ? [{ rotulo: "Motivo del cierre", texto: san.motivo_cierre }] : []),
    ...(san.motivo_anulacion?.trim() && san.estado !== "ANULADO" ? [{ rotulo: "Motivo de la anulación", texto: san.motivo_anulacion }] : []),
  ];
  return (
    <div className="flex flex-col gap-4">
      <h3 className="text-base font-semibold text-flit-primary">Conceptos a sanear</h3>
      <RenglonesLectura renglones={san.renglones} verEvidencia={g.acciones.verEvidencia} descargas={g.descargas} />
      {san.observacion_flit?.trim() ? (
        <div className="flex flex-col gap-1">
          <p className={ETIQUETA_CLS}>Observación para la compañía</p>
          <blockquote className="whitespace-pre-line break-words border-l-2 border-[color:var(--di-border)] pl-3 text-sm text-flit-primary">{san.observacion_flit}</blockquote>
        </div>
      ) : null}
      {motivos.map((m) => (
        <div key={m.rotulo} className="flex flex-col gap-1">
          <p className={ETIQUETA_CLS}>{m.rotulo}</p>
          <p className="whitespace-pre-line break-words text-sm text-flit-primary">{m.texto}</p>
        </div>
      ))}
      <Button type="button" variant="secondary" size="sm" className="self-start" onClick={() => g.abrir({ tipo: "reabrir" })}>
        Reabrir
      </Button>
    </div>
  );
}

function CuerpoGestion({ g, san }: PropsEstado) {
  if (san.estado === "SOLICITADO") return <GestionSolicitada g={g} san={san} />;
  if (san.estado === "EN_GESTION") return <GestionEnCurso g={g} san={san} />;
  if (san.estado === "ANULADO") {
    return (
      <InlineAlert tone="info" title="Saneamiento anulado">
        <span className="whitespace-pre-line break-words">{san.motivo_anulacion?.trim() ? `Motivo: ${san.motivo_anulacion}` : "Sin motivo registrado."}</span>
      </InlineAlert>
    );
  }
  return <GestionCerrada g={g} san={san} />;
}

export function ColumnaGestion({ g, san }: PropsEstado) {
  return (
    <section aria-labelledby={`${g.id}-gestion`} className={`${TARJETA_CLS} flex min-w-0 flex-col gap-3`}>
      <h2 id={`${g.id}-gestion`} className="text-lg font-semibold text-flit-primary">
        Gestión
      </h2>
      <CuerpoGestion g={g} san={san} />
    </section>
  );
}
