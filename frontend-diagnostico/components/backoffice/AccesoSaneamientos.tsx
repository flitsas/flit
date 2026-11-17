"use client";

import { useEffect, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { VehicleLoader } from "@flit/ui/Loader";
import { useSesion } from "@/components/DiagnosticoShell";
import { AccesoBackoffice } from "@/components/backoffice/AccesoBackoffice";
import { puedeGestionarSaneamiento } from "@/lib/sesion";

/** Código de la 403 sin la capacidad gestionarSaneamiento (spec-ui §5). */
export const SIN_PERMISO_SANEAMIENTO = "SIN_PERMISO_SANEAMIENTO";

/**
 * Puerta de /backoffice/saneamientos y de la gestión (HU #14577 AC24): primero la del backoffice y, dentro, la capacidad
 * gestionarSaneamiento. Sin ella, aunque sea admin de su compañía o tenga el backoffice, va a la 403 y la pantalla no se
 * monta, así que no se pide nada. Es ayuda de interfaz: la API vuelve a exigir el permiso.
 */
export function AccesoSaneamientos({ children }: { children: ReactNode }) {
  return (
    <AccesoBackoffice>
      <ConCapacidad>{children}</ConCapacidad>
    </AccesoBackoffice>
  );
}

function ConCapacidad({ children }: { children: ReactNode }) {
  const router = useRouter();
  const { sesion } = useSesion();
  const permitido = puedeGestionarSaneamiento(sesion?.capacidades);
  const negado = sesion != null && !permitido;

  useEffect(() => {
    if (negado) router.replace(`/403?code=${SIN_PERMISO_SANEAMIENTO}`);
  }, [negado, router]);

  if (!permitido) return <VehicleLoader label="Abriendo los saneamientos…" />;
  return <>{children}</>;
}
