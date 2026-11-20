import type { Solicitud } from "@/lib/api/cliente";
import { ApiError } from "@/lib/api/errores";

/**
 * Serializa las escrituras versionadas del borrador (autoguardado y Continuar no corren a la vez, cada una lee la versión
 * vigente al empezar) y reintenta UNA vez un 409 `conflicto_version` releyendo la versión. Si el conflicto persiste, se propaga.
 */
export function crearGuardadorSerial(
  leerVersion: () => number,
  aplicar: (s: Solicitud) => void,
  releer: () => Promise<Solicitud>,
): (escribir: (version: number) => Promise<Solicitud>) => Promise<void> {
  let cola: Promise<unknown> = Promise.resolve();
  return (escribir) => {
    const corrida = cola.then(async () => {
      try {
        aplicar(await escribir(leerVersion()));
      } catch (e) {
        if (!(e instanceof ApiError && e.status === 409 && e.code === "conflicto_version")) throw e;
        aplicar(await releer());
        aplicar(await escribir(leerVersion()));
      }
    });
    cola = corrida.catch(() => undefined);
    return corrida;
  };
}
