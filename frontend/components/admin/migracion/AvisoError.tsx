"use client";

import { XCircle } from "lucide-react";
import { ErrorMigracion } from "@/lib/migracion/client";

/**
 * Un error del migrador, traducido a algo accionable.
 *
 * Los códigos que el host devuelve son estables (`migracion.tramite_en_curso`…), así que se
 * ramifica sobre ellos y no sobre el texto. Cada uno lleva la siguiente acción concreta: un 429 y
 * un 409 se leen igual de mal —«algo falló»— pero se resuelven distinto, y quien opera no debería
 * tener que saberse los códigos HTTP para averiguarlo.
 */
export function AvisoError({ error }: { error: Error }) {
  const codigo = error instanceof ErrorMigracion ? error.titulo : "";
  const detalle = error.message;

  return (
    <div className="flex items-start gap-2 rounded-xl border border-red-500/30 bg-red-500/5 p-3 text-sm">
      <XCircle className="mt-0.5 h-4 w-4 shrink-0 text-red-500" aria-hidden="true" />
      <div className="min-w-0">
        <p className="font-semibold text-red-600 dark:text-red-400">{titulo(codigo)}</p>
        <p className="mt-0.5 opacity-90">{detalle}</p>
        {sugerencia(codigo) && <p className="mt-1.5 text-xs opacity-70">{sugerencia(codigo)}</p>}
      </div>
    </div>
  );
}

function titulo(codigo: string): string {
  switch (codigo) {
    case "migracion.tramite_en_curso":
      return "Ese trámite ya se está migrando";
    case "migracion.ocupado":
      return "El migrador está ocupado";
    case "migracion.llave_invalida":
      return "El migrador rechazó la llave";
    case "migracion.sin_llave":
    case "migracion.host_inalcanzable":
      return "El migrador no está disponible en este ambiente";
    case "migracion.no_autorizado":
      return "No tienes acceso a la consola de migración";
    case "migracion.sesion_expirada":
      return "Tu sesión expiró";
    default:
      return "La migración no se pudo lanzar";
  }
}

function sugerencia(codigo: string): string | null {
  switch (codigo) {
    case "migracion.tramite_en_curso":
      return "Espera a que termine y vuelve a intentarlo. Reintentar no duplica nada.";
    case "migracion.ocupado":
      return "Solo se admiten dos migraciones a la vez. Reintenta en un momento.";
    case "migracion.llave_invalida":
    case "migracion.sin_llave":
      return "Es configuración del ambiente: la llave del frontend y la del migrador no coinciden. Avisa a quien administra el despliegue.";
    case "migracion.host_inalcanzable":
      return "Puede que el migrador esté apagado aquí. Se enciende por ambiente mientras dura una ola.";
    case "migracion.sesion_expirada":
      return "Vuelve a entrar y repite la operación.";
    default:
      return null;
  }
}
