"use client";

import { useState } from "react";
import { editarObservacion, type SaneamientoBackoffice } from "@/lib/backoffice/saneamientos";
import { mensajeErrorSaneamiento } from "@/lib/saneamiento/api";
import { LIMITES_TEXTO, validarTextoLibre } from "@/lib/saneamiento/textoLibre";
import { RED, codigo, es403 } from "./comun";

// PS-5 (spec-ui §6.3.2): la observación para la compañía. Se guarda al salir del campo con la versión vigente; el indicador
// dice si está sin cambios, sin guardar, guardando, guardada o con error, y cada cambio de estado se anuncia.

export type EstadoObservacion = "sin_cambios" | "editado" | "guardando" | "guardado" | "error";

export interface Observacion {
  texto: string;
  guardado: string;
  estado: EstadoObservacion;
  error: string | null;
}

export interface DependenciasObservacion {
  sid: string;
  empresa: string;
  san: SaneamientoBackoffice | null;
  /** Cada lectura completa del detalle sube la generación: la observación vuelve al valor del servidor. */
  generacion: number;
  setSan: (s: SaneamientoBackoffice) => void;
  setConflicto: (conflicto: boolean) => void;
  recargar: () => void;
  ir403: () => void;
  anunciar: (texto: string) => void;
}

export function useObservacionSaneamiento({ sid, empresa, san, generacion, setSan, setConflicto, recargar, ir403, anunciar }: DependenciasObservacion) {
  const [obs, setObs] = useState<Observacion>({ texto: "", guardado: "", estado: "sin_cambios", error: null });
  const [obsGeneracion, setObsGeneracion] = useState(-1);

  // La observación toma el valor del servidor en cada lectura completa (no tras su propio guardado).
  if (san && obsGeneracion !== generacion) {
    setObsGeneracion(generacion);
    const valor = san.observacion_flit ?? "";
    setObs({ texto: valor, guardado: valor, estado: "sin_cambios", error: null });
  }

  const editar = (texto: string) => setObs((o) => ({ ...o, texto, estado: o.estado === "guardando" ? o.estado : "editado" }));

  async function guardar() {
    if (!san || obs.estado === "guardando") return;
    const texto = obs.texto;
    if (texto.trim() === obs.guardado.trim() && obs.estado !== "error") return;
    const problema = validarTextoLibre(texto, LIMITES_TEXTO.observacion);
    if (problema) {
      setObs((o) => ({ ...o, estado: "error", error: problema }));
      anunciar("Observación: No se guardó");
      return;
    }
    setObs((o) => ({ ...o, estado: "guardando", error: null }));
    anunciar("Observación: Guardando");
    try {
      const r = await editarObservacion(sid, empresa, texto, san.version);
      setSan(r);
      setObs((o) => ({ ...o, guardado: texto, estado: o.texto === texto ? "guardado" : "editado", error: null }));
      anunciar("Observación: Guardado");
    } catch (e) {
      const c = codigo(e);
      if (es403(e)) return ir403();
      if (c === "NO_EDITABLE") return recargar();
      if (c === "VERSION_DESACTUALIZADA") setConflicto(true);
      setObs((o) => ({ ...o, estado: "error", error: c === "VERSION_DESACTUALIZADA" ? mensajeErrorSaneamiento(e) : RED }));
      anunciar("Observación: No se guardó");
    }
  }

  return { obs, editar, guardar };
}
