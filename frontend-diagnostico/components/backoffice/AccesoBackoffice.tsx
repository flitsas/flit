"use client";

import { useEffect, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { VehicleLoader } from "@flit/ui/Loader";
import { useSesion } from "@/components/DiagnosticoShell";

/** Código de la 403 cuando la sesión no trae la capacidad de backoffice (la página muestra su texto por defecto). */
export const SIN_PERMISO_BACKOFFICE = "SIN_PERMISO_BACKOFFICE";

/**
 * Puerta de las rutas /backoffice/* (HU #13802, F06-C7): solo con la capacidad `backoffice` de GET /sesion (SuperAdmin de
 * cualquier compañía, o diagnostico.backoffice.ver en la compañía FLIT; ola 2026-11-11, punto 1). Sin ella (p. ej. el admin
 * de una compañía que no es SuperAdmin) lleva a la página 403 y no muestra la pantalla. El override del dictamen y el
 * llenado manual no tienen ruta propia (son modales de la grilla) y allí se ocultan sin `backofficeOperacion`. Es ayuda de interfaz: cada endpoint del backoffice vuelve a exigir su política.
 */
export function AccesoBackoffice({ children }: { children: ReactNode }) {
  const router = useRouter();
  const { sesion } = useSesion();
  const permitido = sesion?.capacidades.backoffice === true;
  const negado = sesion != null && !permitido;

  useEffect(() => {
    if (negado) router.replace(`/403?code=${SIN_PERMISO_BACKOFFICE}`);
  }, [negado, router]);

  if (!permitido) return <VehicleLoader label="Abriendo el backoffice…" />;
  return <>{children}</>;
}
