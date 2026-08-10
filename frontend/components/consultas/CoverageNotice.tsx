"use client";

// Qué pasó con cada placa, VIN o radicado que el usuario pidió por nombre.
//
// Es la pieza que decide si el resultado se puede usar. Alguien pega dos placas, marca «tiene LT
// cargada» y le sale una fila: sin este aviso la lectura inmediata es «se me perdió un dato», y a
// partir de ahí la herramienta pierde la confianza que tardó semanas en ganar. Con él, la respuesta
// llega junto al resultado y sin tener que preguntarle a nadie.
//
// Cuando todo lo pedido salió, esto NO se muestra. Un panel verde que dice «12 de 12» es ruido:
// el resultado ya lo dice. El aviso solo aparece cuando hay algo que explicar.

import { useState } from "react";
import type { QueryCoverageItem, QueryField } from "@/lib/api/queries";
import { plural } from "./ui";

/**
 * Cómo se nombra el campo en el aviso. Sale del catálogo del servidor cuando está disponible: si
 * aquí hubiera una lista fija, un campo nuevo aparecería en el aviso con su identificador crudo
 * («radicado_por») justo en la pantalla que existe para dar explicaciones.
 */
function campoLabel(campo: string, fields: QueryField[]): string {
  return fields.find((f) => f.id === campo)?.label ?? FALLBACK[campo] ?? campo;
}

const FALLBACK: Record<string, string> = {
  placa: "Placa",
  vin: "VIN",
  radicado: "Radicado",
};

export function CoverageNotice({
  cobertura,
  fields = [],
  ambito,
  testIdPrefix,
}: {
  cobertura: QueryCoverageItem[];
  /** El catálogo, para nombrar los campos como se llaman en pantalla. */
  fields?: QueryField[];
  /** Dónde no está lo que no salió: «este organismo», «su empresa». */
  ambito: string;
  testIdPrefix: string;
}) {
  const [abierto, setAbierto] = useState(false);

  const faltantes = cobertura.filter((c) => c.resultado !== "encontrado");
  if (faltantes.length === 0) {
    return null;
  }

  const noExisten = faltantes.filter((c) => c.resultado === "no_existe");
  const excluidos = faltantes.filter((c) => c.resultado === "excluido");

  return (
    <div
      className="rounded-2xl border border-[#E5A50A]/40 bg-[#FFF8E6] p-3 text-xs dark:border-[#E5A50A]/30 dark:bg-[#E5A50A]/10"
      data-testid={`${testIdPrefix}-cobertura`}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="font-semibold text-[#8A6100] dark:text-[#F2C14E]">
          {plural(faltantes.length, "valor que pidió no salió", "valores que pidió no salieron")} en
          el resultado
        </p>
        <button
          type="button"
          onClick={() => setAbierto((v) => !v)}
          className="text-[11px] font-semibold text-[#8A6100] underline dark:text-[#F2C14E]"
          data-testid={`${testIdPrefix}-cobertura-detalle`}
        >
          {abierto ? "Ocultar el detalle" : "Ver por qué"}
        </button>
      </div>

      {/* El resumen distingue las dos causas antes de abrir nada, porque son dos acciones
          distintas: una se arregla aflojando un filtro y la otra no se arregla. */}
      <p className="mt-1 text-[#8A6100]/90 dark:text-[#F2C14E]/80">
        {excluidos.length > 0 && (
          <>
            {excluidos.length} {excluidos.length === 1 ? "existe" : "existen"} pero{" "}
            {excluidos.length === 1 ? "quedó" : "quedaron"} fuera por los filtros.
          </>
        )}
        {excluidos.length > 0 && noExisten.length > 0 && " "}
        {noExisten.length > 0 && (
          <>
            {noExisten.length} no {noExisten.length === 1 ? "está" : "están"} en {ambito}.
          </>
        )}
      </p>

      {abierto && (
        <ul className="mt-2 space-y-1" data-testid={`${testIdPrefix}-cobertura-lista`}>
          {faltantes.map((item) => (
            <li key={`${item.campo}-${item.valor}`} className="flex flex-wrap gap-x-2">
              <span className="font-mono font-semibold text-[#8A6100] dark:text-[#F2C14E]">
                {campoLabel(item.campo, fields)} {item.valor}
              </span>
              <span className="text-[#6B7280] dark:text-white/60">{item.motivo}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/**
 * El mismo aviso, en texto plano, para que viaje dentro del archivo exportado.
 *
 * En pantalla el aviso está al lado del resultado; en el Excel no habría nada, y el archivo es
 * justo lo que se reenvía por correo a quien no ejecutó la consulta. Sin esto, la persona que lo
 * recibe cuenta las filas y llega exactamente a la conclusión equivocada.
 */
export function coverageLines(
  cobertura: QueryCoverageItem[],
  fields: QueryField[] = [],
): string[] {
  return cobertura
    .filter((c) => c.resultado !== "encontrado")
    .map((c) => `${campoLabel(c.campo, fields)} ${c.valor}: ${c.motivo ?? "no salió"}`);
}
