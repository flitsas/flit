import { redirect } from "next/navigation";

/**
 * Rediseño de Comparendos (F0): la raíz será el Inicio con el tablero de la empresa (S2). Mientras tanto lleva a la
 * bandeja, que ahora vive en /bandeja, para que nada se rompa entre fases.
 */
export default function Inicio() {
  redirect("/bandeja");
}
