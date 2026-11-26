"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { ActionsMenu, type ActionsMenuItem } from "@flit/ui";
import {
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
} from "@flit/ui/table-styles";
import { CABECERA_TABLA_CLS, CABECERA_TABLA_ESTILO, CELDA_TABLA_CLS, CONTENEDOR_TABLA_CLS, FILA_TABLA_CLS, FILA_TABLA_HOVER_CLS, FOCO_CLS } from "@/components/comun/estilos";
import { ChipEstado } from "@/components/liquidaciones/ChipEstado";
import { Monto } from "@/components/liquidaciones/TablaVigencias";
import { hrefConEmpresa } from "@/lib/empresaSeleccionada";
import { PANTALLA, chipItem, esRelanzable } from "@/lib/liquidaciones/estados";
import { SIN_DATO, formatoDocumento } from "@/lib/liquidaciones/formato";
import type { EstadoLiquidacion, Item } from "@/lib/liquidaciones/tipos";

interface Props {
  liquidacionId: string;
  items: Item[];
  empresa: string | null;
  /** impuestos.liquidaciones.crear: «Relanzar» en las placas relanzables. */
  puedeRelanzar: boolean;
  /** Estado del lote (1.3.0): con CERRADO_MANUALMENTE ninguna placa ofrece «Relanzar». */
  estadoLote?: EstadoLiquidacion | null;
  onRelanzar: (item: Item) => void;
}

const COLUMNAS = ["Placa", "Propietario", "Documento", "Departamento", "Estado", "Total a pagar", "Acciones"] as const;
const CELDA_CLS = `${CELDA_TABLA_CLS} ${TABLA_MOVIL_CELL_CLS}`;

/**
 * Placas del lote (spec-visual §3.5). Tabla por defecto con el chip de cada placa (sin role="status"), propietario y
 * documento completos (DA-7=b), el total («Sin dato» si es null) y el menú de acciones de la fila: «Ver detalle» y
 * «Relanzar» solo en las placas relanzables (`esRelanzable`: nunca NO_APLICA ni con el lote cerrado a mano) y con permiso.
 * La placa también es enlace.
 */
export function GridItems({ liquidacionId, items, empresa, puedeRelanzar, estadoLote = null, onRelanzar }: Props) {
  const router = useRouter();
  return (
    <div className={CONTENEDOR_TABLA_CLS}>
      <table aria-label="Placas del lote" className={`w-full min-w-[900px] border-collapse text-sm ${TABLA_MOVIL_TABLE_CLS}`}>
        <thead className={TABLA_MOVIL_THEAD_CLS}>
          <tr>
            {COLUMNAS.map((t) => (
              <th key={t} scope="col" className={`${CABECERA_TABLA_CLS} ${t === "Total a pagar" ? "text-right" : ""}`} style={CABECERA_TABLA_ESTILO}>
                {t}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className={TABLA_MOVIL_TBODY_CLS}>
          {items.map((it) => {
            const destino = hrefConEmpresa(`/liquidaciones/${encodeURIComponent(liquidacionId)}/items/${encodeURIComponent(it.id)}`, empresa);
            const acciones: ActionsMenuItem[] = [{ key: "ver", label: "Ver detalle", onSelect: () => router.push(destino) }];
            if (puedeRelanzar && esRelanzable(it, estadoLote)) acciones.push({ key: "relanzar", label: PANTALLA.accionRelanzar, onSelect: () => onRelanzar(it) });
            return (
              <tr key={it.id} className={`${FILA_TABLA_CLS} ${FILA_TABLA_HOVER_CLS} ${TABLA_MOVIL_ROW_CLS}`}>
                <td data-label="Placa" className={`${CELDA_CLS} ${TABLA_MOVIL_CELL_TITULO_CLS}`}>
                  <Link href={destino} className={`text-sm font-semibold uppercase tracking-[0.08em] text-[var(--flit-brand-ink)] underline-offset-4 hover:underline ${FOCO_CLS}`}>
                    {it.placa}
                  </Link>
                </td>
                <td data-label="Propietario" className={CELDA_CLS}>
                  {it.propietario?.nombre || SIN_DATO}
                </td>
                <td data-label="Documento" className={`${CELDA_CLS} whitespace-nowrap`}>
                  {formatoDocumento(it.propietario?.documentoTipo, it.propietario?.documentoNumero)}
                </td>
                <td data-label="Departamento" className={CELDA_CLS}>
                  {it.departamento?.nombre || SIN_DATO}
                </td>
                <td data-label="Estado" className={CELDA_CLS}>
                  <ChipEstado chip={chipItem(it)} />
                </td>
                <td data-label="Total a pagar" className={`${CELDA_CLS} text-right`}>
                  <Monto valor={it.totalAPagar} />
                </td>
                <td data-label="Acciones" className={CELDA_CLS}>
                  <ActionsMenu ariaLabel={`Acciones de la placa ${it.placa}`} items={acciones} />
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
