import type { Organismo, TipoTramite } from "@/lib/api/cliente";

/** Minúsculas y sin tildes: «NANDU» encuentra «Ñandú». */
export function normalizar(texto: string): string {
  return texto.normalize("NFD").replace(/\p{Diacritic}/gu, "").toLocaleLowerCase("es").trim();
}

/** Mismo comportamiento del buscador del diseño (contiene el texto), sin distinguir mayúsculas ni tildes. */
export function filtrarOrganismos(organismos: readonly Organismo[], consulta: string): Organismo[] {
  const buscado = normalizar(consulta);
  return organismos.filter((o) => normalizar(o.nombre).includes(buscado));
}

/** Solo los tipos que el portal puede atender hoy; los no disponibles no se ofrecen en el selector. */
export function tiposDisponibles(tipos: readonly TipoTramite[]): TipoTramite[] {
  return tipos.filter((t) => t.disponible);
}

/** Defensa ante un cuerpo inesperado del BFF: una lista que no es lista se trata como vacía. */
export function comoLista<T>(valor: unknown): T[] {
  return Array.isArray(valor) ? (valor as T[]) : [];
}
