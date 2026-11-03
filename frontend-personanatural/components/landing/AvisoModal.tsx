"use client";

import { Check, CircleHelp, X } from "lucide-react";
import { useEffect, useRef } from "react";
import { TEXTO_DOCUMENTO_NO_DISPONIBLE } from "@/lib/legal";
import { clicFueraDelDialogo } from "@/lib/ui/clic-dialogo";
import { boton } from "./estilos";

/** `alerta` = fallo/regla (rojo); `exito` = confirmación (verde); `info` = neutro (azul). Default alerta para no cambiar fallos existentes. */
export type TonoAviso = "alerta" | "exito" | "info";

export type Aviso =
  | { titulo: string; tipo: "proximamente" | "documento" }
  /** Fallas, confirmaciones y reglas de negocio (mismo diálogo del diseño; el texto va en el cuerpo). */
  | { titulo: string; tipo: "mensaje"; mensaje: string; tono?: TonoAviso };

function estiloMensaje(tono: TonoAviso): string {
  if (tono === "exito") return "border-success bg-success/10 text-success";
  if (tono === "info") return "border-primary bg-primary/10 text-primary";
  return "border-alert bg-alert/10 text-alert";
}

/** Diálogo informativo del diseño («Próximamente» / documento / mensaje de negocio). Usa <dialog> nativo: foco atrapado, Escape y retorno del foco. */
export function AvisoModal({ aviso, onCerrar }: { aviso: Aviso | null; onCerrar: () => void }) {
  const ref = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialogo = ref.current;
    if (!dialogo) return;
    if (aviso && !dialogo.open) {
      if (typeof dialogo.showModal === "function") dialogo.showModal();
      else dialogo.setAttribute("open", "");
    }
    if (!aviso && dialogo.open) {
      if (typeof dialogo.close === "function") dialogo.close();
      else dialogo.removeAttribute("open");
    }
  }, [aviso]);

  const tonoMensaje = aviso?.tipo === "mensaje" ? (aviso.tono ?? "alerta") : "info";
  const Icono = tonoMensaje === "exito" ? Check : CircleHelp;

  return (
    <dialog
      ref={ref}
      aria-labelledby="aviso-titulo"
      aria-describedby="aviso-descripcion"
      onClose={onCerrar}
      onClick={(e) => {
        if (clicFueraDelDialogo(e)) onCerrar();
      }}
      className="m-auto w-[calc(100%-2rem)] max-w-md rounded-[9px] bg-card p-6 text-foreground backdrop:bg-primary/40 backdrop:backdrop-blur-sm"
    >
      {aviso && (
        <>
          <button type="button" aria-label="Cerrar" onClick={onCerrar} className={boton("ghost", "absolute right-2 top-2 h-11 w-11 p-0", "none")}>
            <X aria-hidden />
          </button>
          <div className="flex flex-col items-center gap-2 px-8 text-center">
            <span
              aria-hidden
              className={`grid h-10 w-10 shrink-0 place-items-center rounded-full ${
                tonoMensaje === "exito" ? "bg-success/10 text-success" : "bg-primary/10 text-primary"
              }`}
            >
              <Icono className="h-5 w-5" />
            </span>
            <h2 id="aviso-titulo" className="dashboard-title">{aviso.titulo}</h2>
          </div>
          <div id="aviso-descripcion" className="mt-4 text-center">
            {aviso.tipo === "proximamente" && (
              <p>
                <span className="inline-block rounded-full bg-primary/10 px-4 py-1 text-sm font-semibold text-primary">Próximamente</span>
              </p>
            )}
            {aviso.tipo === "documento" && <p className="text-sm">{TEXTO_DOCUMENTO_NO_DISPONIBLE}</p>}
            {aviso.tipo === "mensaje" && (
              <p
                role={tonoMensaje === "alerta" ? "alert" : "status"}
                className={`rounded-[9px] border p-3 text-sm ${estiloMensaje(tonoMensaje)}`}
              >
                {aviso.mensaje}
              </p>
            )}
          </div>
          <button type="button" onClick={onCerrar} className={boton("flit", "mx-auto mt-5 flex h-11 px-10", "none")}>
            Cerrar
          </button>
        </>
      )}
    </dialog>
  );
}
