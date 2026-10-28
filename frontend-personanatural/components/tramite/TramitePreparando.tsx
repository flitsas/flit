"use client";

import Link from "next/link";
import { MobilityLoader } from "@/components/MobilityLoader";
import { useContextoTramite } from "@/lib/tramite/contexto";

/**
 * Marcador de la ruta del wizard (/tramite/<tipo>). El wizard es del Feature F: aquí solo se confirma que la sesión abrió,
 * con el radicado a la vista. Lo que la landing dejó en memoria (contexto) se pierde al recargar la página.
 */
export function TramitePreparando() {
  const contexto = useContextoTramite();
  const subsanando = contexto?.intencion === "subsanar";

  return (
    <main className="mx-auto flex min-h-dvh max-w-xl flex-col items-center justify-center gap-4 p-6 text-center">
      <h1 className="dashboard-title">{subsanando ? "Estamos preparando la corrección de tu trámite" : "Estamos preparando tu trámite"}</h1>
      <MobilityLoader />
      {contexto?.radicado ? (
        <p className="text-sm">
          Radicado: <strong data-testid="radicado">{contexto.radicado}</strong>
        </p>
      ) : (
        <p className="text-sm">Si recargaste la página, consulta tu trámite con tu radicado y tu cédula desde el inicio.</p>
      )}
      <Link href="/" className="text-sm font-medium text-primary underline-offset-4 hover:underline">Volver al inicio</Link>
    </main>
  );
}
