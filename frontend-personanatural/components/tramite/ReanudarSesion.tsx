"use client";

import { useMutation } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { boton } from "@/components/landing/estilos";
import { MobilityLoader } from "@/components/MobilityLoader";
import type { ClientePortal } from "@/lib/api/cliente";
import { mensajeDe } from "@/lib/api/errores";
import { useBloqueo } from "@/lib/api/useBloqueo";
import { cedulaValida, normalizarRadicado, soloDigitos } from "@/lib/entradas";
import { fijarContexto, type ContextoTramite } from "@/lib/tramite/contexto";
import { rutaDelTramite } from "@/lib/tramite/ruta";

interface Props {
  cliente: ClientePortal;
  tipoCodigo: string;
  contexto: ContextoTramite | null;
  /** Por qué se pide reanudar: la sesión venció, o no hay una abierta en esta pestaña. */
  motivo: "vencida" | "sin_sesion";
}

const TITULO = { vencida: "Tu sesión venció", sin_sesion: "Reanuda tu trámite" } as const;
const TEXTO = {
  vencida: "Tu sesión venció. Reanuda con tu radicado y tu cédula: lo que ya guardaste se conserva.",
  sin_sesion: "No hay un trámite abierto en esta pestaña. Reanúdalo con tu radicado y tu cédula.",
} as const;

/**
 * Pantalla para volver a un trámite cuando no hay sesión (se recargó la página, venció o se llegó desde «Consultar mi
 * trámite»): radicado y cédula abren una sesión nueva (POST /sesiones/reanudar). Radicado y cédula viven solo en
 * memoria; el token de la sesión lo guarda el proxy en una cookie HttpOnly.
 */
export function ReanudarSesion({ cliente, tipoCodigo, contexto, motivo }: Props) {
  const router = useRouter();
  const [radicado, setRadicado] = useState(contexto?.radicado ?? "");
  const [cedula, setCedula] = useState(contexto?.cedula ?? "");
  const bloqueo = useBloqueo();

  const reanudar = useMutation({
    mutationFn: () => cliente.sesiones.reanudar({ radicado, cedula }),
    onSuccess: (sesion) => {
      fijarContexto({
        intencion: contexto?.intencion ?? "iniciar",
        tipoCodigo: sesion.tipoTramiteCodigo,
        solicitudId: sesion.solicitudId,
        radicado: sesion.codigoPublico,
        cedula,
        placa: contexto?.placa,
        vin: contexto?.vin,
      });
      // El radicado abre el trámite que es, aunque la ruta tuviera otro tipo.
      if (sesion.tipoTramiteCodigo !== tipoCodigo) router.replace(rutaDelTramite(sesion.tipoTramiteCodigo));
    },
    onError: (error) => bloqueo.registrar(error),
  });

  const listo = radicado.trim().length > 0 && cedulaValida(cedula);
  const puedeEnviar = listo && !reanudar.isPending && !bloqueo.bloqueado;

  return (
    <main className="mx-auto flex min-h-dvh max-w-md flex-col items-center justify-center gap-4 p-6">
      <h1 className="dashboard-title">{TITULO[motivo]}</h1>
      <p className="text-center text-sm">{TEXTO[motivo]}</p>
      <form
        className="grid w-full gap-3 rounded-[9px] border border-border bg-card p-6"
        onSubmit={(e) => {
          e.preventDefault();
          if (puedeEnviar) reanudar.mutate();
        }}
      >
        <label className="dashboard-label">
          Número de radicado
          <input required className="field uppercase" autoComplete="off" placeholder="Nº de radicado" value={radicado} onChange={(e) => setRadicado(normalizarRadicado(e.target.value))} />
        </label>
        <label className="dashboard-label">
          Cédula del solicitante
          <input required className="field" inputMode="numeric" autoComplete="off" placeholder="Ej. 1020456789" value={cedula} onChange={(e) => setCedula(soloDigitos(e.target.value))} />
        </label>
        <div aria-live="polite" className="text-center text-sm">
          {reanudar.isError && <p role="alert" className="rounded-[9px] border border-alert bg-alert/10 p-3 text-alert">{mensajeDe(reanudar.error)}</p>}
          {bloqueo.bloqueado && <p role="status" className="mt-2 text-xs text-foreground/70">Podrás intentarlo de nuevo en {bloqueo.segundosRestantes} s.</p>}
        </div>
        <div className="flex flex-wrap justify-center gap-3">
          <Link href="/" className={boton("back", "h-11 px-6", "none")}>Volver al inicio</Link>
          <button type="submit" className={boton("flit", "h-11 px-6", "none")} disabled={!puedeEnviar}>Reanudar</button>
        </div>
      </form>
      {reanudar.isPending && (
        <div className="fixed inset-0 z-50 grid place-items-center bg-background/80 backdrop-blur-sm">
          <MobilityLoader />
        </div>
      )}
    </main>
  );
}
