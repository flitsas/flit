// Texto buscado en la tabla de compañías de /usuarios (hallazgo O-1 de seguridad, Habeas Data). El buscador acepta nombre
// o NIT, y el NIT de una persona natural es su cédula: por eso no viaja en la URL (historial, Referer, logs). Se guarda en
// sessionStorage de la pestaña, con clave por persona de la sesión, y se restaura al volver por la miga o con
// atrás/adelante. Vaciar la caja borra la entrada; al cerrar la pestaña desaparece.
//
// Por qué sessionStorage y no window.history.state: la miga «Usuarios» es un enlace que crea una entrada nueva del
// historial sin el estado de la anterior, así que con history.state solo se restauraría con «atrás». Además, el router de
// Next reescribe history.state en cada router.replace de los filtros, y una clave propia se perdería.
import { useSyncExternalStore } from "react";
import { BUSQUEDA_MAXIMA } from "./tipos";

const PREFIJO = "flit-diagnostico:usuarios:busqueda-companias:";
const oyentes = new Set<() => void>();
/** Respaldo en memoria si sessionStorage está bloqueado (modo privado estricto): dura lo que dure la página. */
const respaldo = new Map<string, string>();

function clave(usuario: string): string {
  return `${PREFIJO}${usuario}`;
}

function suscribir(oyente: () => void): () => void {
  oyentes.add(oyente);
  return () => oyentes.delete(oyente);
}

/** Lee la búsqueda guardada de la persona; "" si no hay o si no hay sesión. */
export function leerBusquedaCompanias(usuario: string | null | undefined): string {
  if (!usuario) return "";
  try {
    return (window.sessionStorage.getItem(clave(usuario)) ?? "").slice(0, BUSQUEDA_MAXIMA);
  } catch {
    return respaldo.get(usuario) ?? "";
  }
}

/** Guarda (recortada) la búsqueda de la persona; vacía, borra la entrada. Avisa a quien la esté mostrando. */
export function guardarBusquedaCompanias(usuario: string | null | undefined, texto: string): void {
  if (!usuario) return;
  const valor = texto.trim().slice(0, BUSQUEDA_MAXIMA);
  if (valor) respaldo.set(usuario, valor);
  else respaldo.delete(usuario);
  try {
    if (valor) window.sessionStorage.setItem(clave(usuario), valor);
    else window.sessionStorage.removeItem(clave(usuario));
  } catch {
    // Almacén bloqueado: queda el respaldo en memoria.
  }
  oyentes.forEach((oyente) => oyente());
}

/** Búsqueda guardada de la persona, reactiva. En el servidor y en la hidratación vale "" (sin desajuste). */
export function useBusquedaCompanias(usuario: string | null | undefined): string {
  return useSyncExternalStore(
    suscribir,
    () => leerBusquedaCompanias(usuario),
    () => "",
  );
}
