"use client";

import Link from "next/link";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { FOCO_CLS } from "@/components/configuracion/estilos";
import { EtiquetaSaneamiento } from "@/components/saneamiento/EtiquetaSaneamiento";
import { EtiquetaTono } from "@/components/saneamiento/SeccionesDeuda";
import type { SaneamientoBackoffice } from "@/lib/backoffice/saneamientos";

// PS-5 (CR-5): encabezado de la gestión. Placa como título, compañía y lote como descripción, y el estado con el dictamen
// en un contenedor enfocable: tras una transición el foco vuelve ahí (spec-ui §6.3.3).

export interface CabeceraGestionProps {
  /** Id de la pantalla (useId): marca el contenedor del estado para devolverle el foco. */
  id: string;
  san: SaneamientoBackoffice;
  empresa: string;
  placa: string;
  nombreEmpresa: string;
  veredicto: "VIABLE" | "BLOQUEADO" | null;
}

export function CabeceraGestion({ id, san, empresa, placa, nombreEmpresa, veredicto }: CabeceraGestionProps) {
  return (
    <CabeceraModulo
      titulo={placa}
      tituloClassName="uppercase tracking-wider"
      icono="pago"
      miga={[{ etiqueta: "Saneamientos", href: "/backoffice/saneamientos" }, { etiqueta: placa }]}
      descripcion={
        <span className="break-words">
          {nombreEmpresa}
          {" · "}
          {san.diagnostico_id ? (
            <Link
              href={`/backoffice/diagnosticos/${encodeURIComponent(san.diagnostico_id)}?empresa=${encodeURIComponent(empresa)}`}
              className={`font-semibold text-[var(--flit-brand-ink)] underline underline-offset-2 ${FOCO_CLS}`}
            >
              {san.diagnostico_nombre || "Ver el lote"}
            </Link>
          ) : (
            (san.diagnostico_nombre ?? "Lote")
          )}
        </span>
      }
      acciones={
        <div data-estado-saneamiento={id} tabIndex={-1} className="flex flex-wrap items-center gap-2 rounded-full focus:outline-2 focus:outline-offset-2 focus:outline-[var(--di-action)]">
          <EtiquetaSaneamiento estado={san.estado} backoffice />
          {veredicto && <EtiquetaTono tono={veredicto === "BLOQUEADO" ? "danger" : "success"}>{veredicto === "BLOQUEADO" ? "Bloqueado" : "Viable"}</EtiquetaTono>}
        </div>
      }
    />
  );
}
