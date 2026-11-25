"use client";

import { useEffect, useId, useRef, useState } from "react";
import { AYUDA_CLS, BOTON_SECUNDARIO_CLS } from "@/components/configuracion/estilos";
import { RegionDesplazable } from "@/components/comun/RegionDesplazable";
import {
  descargarLiquidacionImpuesto,
  mensajeDescargaLiquidacion,
  type VigenciaLiquidacion,
} from "@/lib/reportes/api";
import { fechaCorta, formatearPesos, type ResultadoImpuestoDetalle } from "@/lib/tablero/detalle";
import { iconoComo } from "@flit/ui/iconoComo";
import { CABECERA_TABLA_CLS, CABECERA_TABLA_ESTILO, CELDA_TABLA_CLS, FILA_TABLA_CLS, Vacio } from "./comun";

const ICONO_NUBE_BAJAR = iconoComo("nube-bajar");
const TITULO_CLS = "text-sm font-semibold text-flit-primary";

export interface LiquidacionesImpuestoProps {
  diagnosticoId: string;
  vehiculoId: string;
  /** Placa (o VIN) del vehículo: va en el nombre del archivo. */
  identificador: string;
  resultado: ResultadoImpuestoDetalle;
  /** Capacidad `descargarReportes` de la sesión de la compañía; sin ella no hay botones (la API vuelve a exigirla). */
  puedeDescargar: boolean;
}

/**
 * Botón de descarga de una liquidación con su propio estado (cargando y error). Bajo demanda: pide vehículo + año (o
 * «consolidada»), nunca el id del archivo. El error queda bajo el botón y se enlaza con `aria-describedby` solo cuando existe.
 */
function BotonLiquidacion({ diagnosticoId, vehiculoId, identificador, vigencia }: { diagnosticoId: string; vehiculoId: string; identificador: string; vigencia: VigenciaLiquidacion }) {
  const idError = useId();
  const [descargando, setDescargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const montado = useRef(true);
  useEffect(() => {
    montado.current = true;
    return () => {
      montado.current = false;
    };
  }, []);

  function descargar() {
    if (descargando) return;
    setDescargando(true);
    setError(null);
    descargarLiquidacionImpuesto(diagnosticoId, vehiculoId, vigencia, identificador)
      .catch((e: unknown) => {
        if (montado.current) setError(mensajeDescargaLiquidacion(e));
      })
      .finally(() => {
        if (montado.current) setDescargando(false);
      });
  }

  return (
    <div className="flex flex-col items-start gap-1">
      <button
        type="button"
        onClick={descargar}
        disabled={descargando}
        aria-busy={descargando || undefined}
        aria-describedby={error ? idError : undefined}
        className={BOTON_SECUNDARIO_CLS}
      >
        <ICONO_NUBE_BAJAR className="h-4 w-4" aria-hidden="true" />
        {descargando ? `Descargando liquidación ${vigencia}…` : `Descargar liquidación ${vigencia}`}
      </button>
      {error && (
        <p id={idError} role="alert" className="max-w-[16rem] text-xs font-semibold" style={{ color: "var(--badge-danger-fg)" }}>
          {error}
        </p>
      )}
    </div>
  );
}

/**
 * «Liquidaciones del impuesto» (ola impuesto-fuente-real, diseno.md §5.2): tabla «Vigencias pendientes» (año, valor,
 * vencimiento y referencia de pago) y, con el permiso de descarga, «Descargar liquidación <año>» por vigencia con archivo y
 * «Descargar liquidación consolidada» si la fuente la dejó. Vacío: sin vigencias pendientes lo dice.
 */
export function LiquidacionesImpuesto({ diagnosticoId, vehiculoId, identificador, resultado, puedeDescargar }: LiquidacionesImpuestoProps) {
  const idTitulo = useId();
  const pendientes = (resultado.vigencias ?? []).filter((v) => !v.pagada);
  const conBotones = puedeDescargar && pendientes.some((v) => v.tiene_liquidacion);
  const columnas = ["Año", "Valor", "Vencimiento", "Referencia de pago", ...(conBotones ? ["Liquidación"] : [])];
  const boton = (vigencia: VigenciaLiquidacion) => (
    <BotonLiquidacion diagnosticoId={diagnosticoId} vehiculoId={vehiculoId} identificador={identificador} vigencia={vigencia} />
  );

  return (
    <section aria-labelledby={idTitulo} className="flex flex-col gap-3">
      <h3 id={idTitulo} className={TITULO_CLS}>
        Liquidaciones del impuesto
      </h3>
      {pendientes.length === 0 ? (
        <Vacio>No hay vigencias pendientes de pago.</Vacio>
      ) : (
        <RegionDesplazable nombre="Vigencias pendientes">
          <table aria-label="Vigencias pendientes" className="w-full text-sm">
            <thead className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
              <tr>
                {columnas.map((c) => (
                  <th key={c} scope="col" className={CELDA_TABLA_CLS}>
                    {c}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {pendientes.map((v) => (
                <tr key={v.anio} className={FILA_TABLA_CLS}>
                  <td className={CELDA_TABLA_CLS}>{v.anio}</td>
                  <td className={CELDA_TABLA_CLS}>{formatearPesos(v.valor)}</td>
                  <td className={CELDA_TABLA_CLS}>{fechaCorta(v.fecha_vencimiento ?? null)}</td>
                  <td className={`${CELDA_TABLA_CLS} break-all`}>{v.referencia_pago || "—"}</td>
                  {conBotones && <td className={CELDA_TABLA_CLS}>{v.tiene_liquidacion ? boton(v.anio) : <span className={AYUDA_CLS}>Sin archivo</span>}</td>}
                </tr>
              ))}
            </tbody>
          </table>
        </RegionDesplazable>
      )}
      {puedeDescargar && resultado.tiene_liquidacion_consolidada === true && boton("consolidada")}
    </section>
  );
}
