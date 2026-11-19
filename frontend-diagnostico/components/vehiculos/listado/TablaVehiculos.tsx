"use client";

import type { ReactNode, RefObject } from "react";
import { Placa } from "@flit/ui/Placa";
import { TABLA_MOVIL_CELL_CLS, TABLA_MOVIL_CELL_TITULO_CLS, TABLA_MOVIL_ROW_CLS, TABLA_MOVIL_TABLE_CLS, TABLA_MOVIL_TBODY_CLS, TABLA_MOVIL_THEAD_CLS } from "@flit/ui/table-styles";
import { FOCO_CLS } from "@/components/configuracion/estilos";
import { EtiquetaSaneamiento } from "@/components/saneamiento/EtiquetaSaneamiento";
import { fechaDelDato } from "@/components/tablero/CeldaSemaforo";
import { IconoCambioPropietario } from "@/components/tablero/IconoCambioPropietario";
import {
  CABECERA_TABLA_CLS,
  CABECERA_TABLA_ESTILO,
  CELDA_TABLA_CLS,
  CONTENEDOR_TABLA_CLS,
  Etiqueta,
  FILA_TABLA_CLS,
  FILA_TABLA_HOVER_CLS,
  type TonoEtiqueta,
} from "@/components/tablero/vistasDetalle/comun";
import { nombreModo } from "@/lib/diagnosticos/listado";
import { identificadorFila, type FilaVehiculoListado } from "@/lib/vehiculos/listado";
import { Casilla } from "./ControlesSeleccion";
import { seleccionDe, type SeleccionFila } from "./seleccion";

// PS-3 (CR-5): la tabla de placas. Por fila: placa, diagnóstico, fecha, modo, estado, dictamen, saneamiento y trámite, y
// la celda de acciones que pinta la pantalla (`accionesFila`). La fila no es clicable entera: tiene varias acciones. Por
// debajo de 1024 px cada fila es una tarjeta (TABLA_MOVIL_*).

const COLUMNAS = ["Placa", "Diagnóstico", "Fecha", "Modo", "Estado", "Dictamen", "Saneamiento", "Trámite", "Acciones"] as const;
const CELDA_CLS = `${CELDA_TABLA_CLS} ${TABLA_MOVIL_CELL_CLS}`;

/** D-104: `Placa` del kit solo para placas (hasta 7 letras y números); un VIN u otro identificador va en texto. */
const esPlaca = (texto: string) => /^[A-Z0-9]{1,7}$/.test(texto);

const ESTADO_VEHICULO: Record<FilaVehiculoListado["estado"], { texto: string; tono: TonoEtiqueta }> = {
  EN_PROCESO: { texto: "En proceso", tono: "info" },
  TERMINADO: { texto: "Terminado", tono: "success" },
};

function dictamenDe(f: FilaVehiculoListado): { texto: string; tono: TonoEtiqueta } {
  if (f.veredicto === "VIABLE") return { texto: "Viable", tono: "success" };
  if (f.veredicto === "BLOQUEADO") return { texto: "Bloqueado", tono: "danger" };
  return { texto: "Sin dictamen", tono: "neutral" };
}

export interface TablaVehiculosProps {
  vehiculos: readonly FilaVehiculoListado[];
  hayCasillas: boolean;
  puedeSanear: boolean;
  puedeDescargar: boolean;
  selecciones: ReadonlyMap<string, SeleccionFila>;
  seleccion: ReadonlySet<string>;
  /** true tras cambiar de página: la tabla nueva toma el foco al montarse. */
  focoPendienteRef: RefObject<boolean>;
  onCambiarSeleccion: (vehiculoId: string, marcada: boolean) => void;
  /** Contenido de la celda «Acciones» de la fila; `placa` es la placa, el VIN o el identificador ("" si no hay). */
  accionesFila: (fila: FilaVehiculoListado, placa: string) => ReactNode;
}

interface FilaProps extends Omit<TablaVehiculosProps, "vehiculos" | "selecciones" | "seleccion" | "focoPendienteRef"> {
  f: FilaVehiculoListado;
  seleccionFila: SeleccionFila;
  marcada: boolean;
}

function FilaVehiculo({ f, seleccionFila, marcada, hayCasillas, onCambiarSeleccion, accionesFila }: FilaProps) {
  // UA-5: la placa, o el VIN, o el identificador (como el tablero); sin ninguno, «Sin placa».
  const placa = identificadorFila(f);
  const estado = ESTADO_VEHICULO[f.estado] ?? ESTADO_VEHICULO.EN_PROCESO;
  const dictamen = dictamenDe(f);
  return (
    <tr className={`${FILA_TABLA_CLS} ${FILA_TABLA_HOVER_CLS} ${TABLA_MOVIL_ROW_CLS}`}>
      {hayCasillas && (
        <td data-label="Selección" className={CELDA_CLS}>
          <Casilla placa={placa} seleccion={seleccionFila} marcada={marcada} onCambiar={(m) => onCambiarSeleccion(f.vehiculo_id, m)} />
        </td>
      )}
      <th scope="row" data-label="Placa" className={`${CELDA_CLS} ${TABLA_MOVIL_CELL_TITULO_CLS} text-left font-semibold`}>
        <span className="inline-flex flex-wrap items-center gap-1.5">
          {esPlaca(placa) ? <Placa placa={placa} /> : placa ? <span className="break-all font-mono">{placa}</span> : <span>Sin placa</span>}
          {/* HU #14585 (spec-ui §6.5): ícono de cambio de propietario a la derecha de la placa, solo con true. */}
          <IconoCambioPropietario cambio={f.cambio_propietario} />
        </span>
      </th>
      <td data-label="Diagnóstico" className={`${CELDA_CLS} break-words`}>
        {f.diagnostico_nombre}
      </td>
      <td data-label="Fecha" className={`${CELDA_CLS} whitespace-nowrap tabular-nums`}>
        {fechaDelDato(f.diagnostico_creado_en)}
      </td>
      <td data-label="Modo" className={CELDA_CLS}>
        {nombreModo(f.modo)}
      </td>
      <td data-label="Estado" className={CELDA_CLS}>
        <Etiqueta tono={estado.tono}>{estado.texto}</Etiqueta>
      </td>
      <td data-label="Dictamen" className={CELDA_CLS}>
        <Etiqueta tono={dictamen.tono}>{dictamen.texto}</Etiqueta>
      </td>
      <td data-label="Saneamiento" className={CELDA_CLS}>
        {f.saneamiento?.estado ? (
          <EtiquetaSaneamiento estado={f.saneamiento.estado} />
        ) : (
          <span role="img" aria-label="Sin saneamiento">
            —
          </span>
        )}
      </td>
      <td data-label="Trámite" className={`${CELDA_CLS} tabular-nums`}>
        {f.tramite ? (
          `#${f.tramite.numero}`
        ) : (
          <span role="img" aria-label="Sin trámite">
            —
          </span>
        )}
      </td>
      <td data-label="Acciones" className={CELDA_CLS}>
        {accionesFila(f, placa)}
      </td>
    </tr>
  );
}

export function TablaVehiculos({ vehiculos, selecciones, seleccion, focoPendienteRef, ...fila }: TablaVehiculosProps) {
  const columnas = fila.hayCasillas ? (["Selección", ...COLUMNAS] as const) : COLUMNAS;
  return (
    <div className={`${CONTENEDOR_TABLA_CLS} max-lg:overflow-visible max-lg:border-0`}>
      <table
        ref={(el) => {
          if (el && focoPendienteRef.current) {
            focoPendienteRef.current = false;
            el.focus();
          }
        }}
        tabIndex={-1}
        aria-label="Vehículos de la compañía"
        className={`w-full min-w-[1080px] border-collapse text-sm ${FOCO_CLS} ${TABLA_MOVIL_TABLE_CLS}`}
      >
        <thead className={TABLA_MOVIL_THEAD_CLS}>
          <tr>
            {columnas.map((t) => (
              <th key={t} scope="col" className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
                {t === "Selección" ? <span className="sr-only">Selección</span> : t}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className={TABLA_MOVIL_TBODY_CLS}>
          {vehiculos.map((f) => (
            <FilaVehiculo
              key={f.vehiculo_id}
              f={f}
              seleccionFila={selecciones.get(f.vehiculo_id) ?? seleccionDe(f, fila.puedeSanear, fila.puedeDescargar)}
              marcada={seleccion.has(f.vehiculo_id)}
              {...fila}
            />
          ))}
        </tbody>
      </table>
    </div>
  );
}
