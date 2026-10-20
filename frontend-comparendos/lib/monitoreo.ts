// Textos del monitoreo de NITs y placas.

export interface ResultadoPlacas {
  agregadas: unknown[];
  yaExistian: string[];
  invalidas: string[];
  repetidas: string[];
}

const muestra = (lista: string[]) => `${lista.slice(0, 5).join(", ")}${lista.length > 5 ? "…" : ""}`;

/** «1 agregada · 1 ya estaba (TST123) · 1 repetida en lo que pegaste (TST123) · 1 no es placa válida (AB12)» (B3-1). */
export function resumenPlacas(r: ResultadoPlacas, origen: "pegado" | "archivo" = "pegado"): string {
  const n = r.agregadas.length;
  const partes = [`${n} ${n === 1 ? "agregada" : "agregadas"}`];
  if (r.yaExistian.length) partes.push(`${r.yaExistian.length} ${r.yaExistian.length === 1 ? "ya estaba" : "ya estaban"} (${muestra(r.yaExistian)})`);
  if (r.repetidas?.length) partes.push(`${r.repetidas.length} ${r.repetidas.length === 1 ? "repetida" : "repetidas"} ${origen === "archivo" ? "en el archivo" : "en lo que pegaste"} (${muestra(r.repetidas)})`);
  if (r.invalidas.length) partes.push(`${r.invalidas.length} ${r.invalidas.length === 1 ? "no es placa válida" : "no son placas válidas"} (${muestra(r.invalidas)})`);
  return partes.join(" · ");
}
