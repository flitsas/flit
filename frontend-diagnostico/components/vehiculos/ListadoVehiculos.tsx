"use client";

import { useRef, useState, type ReactNode } from "react";
import Link from "next/link";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { iconoComo } from "@flit/ui/iconoComo";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { Pagination } from "@flit/ui/Pagination";
import { ACCION_FILA_CLS, BOTON_PRIMARIO_CLS, BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS, ENLACE_ACCION_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { IconoDxDeuda } from "@/components/dx/iconos/IconoDxDeuda";
import { banderaPanelDeuda, filaDeEstado } from "@/components/saneamiento/BanderaPanelDeuda";
import { BotonSanear } from "@/components/saneamiento/BotonSanear";
import { PanelDeudaSaneamiento } from "@/components/saneamiento/PanelDeudaSaneamiento";
import { useRegionVivaPantalla } from "@/components/saneamiento/RegionVivaPantalla";
import type { SaneamientoFila } from "@/lib/saneamiento/api";
import { deLaPlaca } from "@/lib/saneamiento/estados";
import { hayFiltrosVehiculos, identificadorFila, type FilaVehiculoListado } from "@/lib/vehiculos/listado";
import { FiltrosVehiculos } from "./FiltrosVehiculos";
import { BarraVehiculos } from "./listado/BarraVehiculos";
import { terminado } from "./listado/seleccion";
import { TablaVehiculos } from "./listado/TablaVehiculos";
import { useConsultaVehiculos } from "./listado/useConsultaVehiculos";
import { useAccionesSeleccion, useSeleccionVehiculos } from "./listado/useSeleccionVehiculos";

// PS-3 «Vehículos» del inicio (spec-ui §4; plan §7.4 y §6.6; HU #14577 AC21–AC23). Todo valor visual sale de tokens
// (--di-*, --badge-*, recetas del kit); sin hex. Cuatro estados del patrón de Diagnóstico (D-01).
// CR-5: la consulta vive en useConsultaVehiculos, la selección y sus acciones en useSeleccionVehiculos/useAccionesSeleccion;
// la pantalla compone los filtros, los estados, la barra (BarraVehiculos), la tabla (TablaVehiculos) con las acciones de
// cada fila (AccionesFila) y el panel PS-2.

export interface ListadoVehiculosProps {
  /** Capacidad `crearDiagnosticos`: acción «Diagnosticar un vehículo» del vacío. */
  puedeCrear: boolean;
  /** Capacidad `sanear`: bandera por fila y «Sanear (N)». */
  puedeSanear: boolean;
  /** Capacidad `descargarReportes`: «Descargar deuda (N)» y las descargas del panel PS-2. */
  puedeDescargar: boolean;
}

const IconoBuscar = iconoComo("buscar");
const IconoPortapapeles = iconoComo("portapapeles");

const contarVehiculos = (n: number) => (n === 1 ? "1 vehículo" : `${n} vehículos`);

interface AccionesFilaProps {
  f: FilaVehiculoListado;
  /** La placa, el VIN o el identificador; "" si no hay ninguno. */
  placa: string;
  puedeSanear: boolean;
  onActualizarFila: (vehiculoId: string, saneamiento: SaneamientoFila) => void;
  onAbrirPanel: (fila: FilaVehiculoListado, disparador: HTMLElement | null) => void;
  onRefrescar: () => void;
  onAnunciar: (texto: string) => void;
}

/** Celda «Acciones» de la fila: la bandera (BotonSanear), «Abrir diagnóstico» (tablero del lote) y «Ver deuda» (PS-2, solo terminados). */
function AccionesFila({ f, placa, puedeSanear, onActualizarFila, onAbrirPanel, onRefrescar, onAnunciar }: AccionesFilaProps) {
  return (
    <div className="flex flex-col items-start gap-2 max-[900px]:items-end">
      <BotonSanear
        diagnosticoId={f.diagnostico_id}
        vehiculo={{ id: f.vehiculo_id, placa: f.placa, vin: f.vin, identificador: f.identificador }}
        saneamiento={f.saneamiento}
        puedeSanear={puedeSanear}
        onCambio={(_estado, saneamiento) => onActualizarFila(f.vehiculo_id, saneamiento)}
        onAbrirPanel={(disparador) => onAbrirPanel(f, disparador)}
        onRefrescar={onRefrescar}
        onAnunciar={onAnunciar}
      />
      <Link href={`/diagnosticos/${encodeURIComponent(f.diagnostico_id)}`} aria-label={`Abrir el diagnóstico ${f.diagnostico_nombre}`} className={ENLACE_ACCION_CLS}>
        Abrir diagnóstico
      </Link>
      {terminado(f) && (
        <button
          type="button"
          aria-haspopup="dialog"
          aria-label={`Ver deuda ${deLaPlaca(placa)}`}
          onClick={(e) => onAbrirPanel(f, e.currentTarget)}
          className={`${ACCION_FILA_CLS} whitespace-nowrap`}
        >
          <IconoDxDeuda tamano={16} className="h-3.5 w-3.5" />
          Ver deuda
        </button>
      )}
    </div>
  );
}

/**
 * Grilla de placas de la compañía (pestaña «Vehículos» del inicio). Filtros en la URL (`?vista=vehiculos&…`, lib/vehiculos);
 * cada cambio la reescribe, vuelve a la página 1 y limpia la selección. Tabla con placa, diagnóstico, fecha, modo, estado,
 * dictamen, saneamiento y trámite; por fila, la bandera (BotonSanear), «Abrir diagnóstico» (tablero del lote) y «Ver deuda»
 * (PS-2, solo terminados). La fila no es clicable entera: tiene varias acciones. Selección por página con «Sanear (N)»
 * (POST /saneamientos/solicitudes, como el tablero) y «Descargar deuda (N)» (Excel de 1 a 200 placas). Al cambiar de página
 * el foco va a la tabla y se anuncia «Página p de n». Por debajo de 900 px cada fila es una tarjeta (TABLA_MOVIL_*).
 */
export function ListadoVehiculos({ puedeCrear, puedeSanear, puedeDescargar }: ListadoVehiculosProps) {
  const [panel, setPanel] = useState<{ fila: FilaVehiculoListado; disparador: HTMLElement | null } | null>(null);
  const { anunciar, region } = useRegionVivaPantalla();
  // Tras cambiar de página: el foco va a la tabla nueva y se anuncia la página (spec-ui §4.5).
  const focoTablaRef = useRef(false);
  const resultadoRef = useRef<HTMLDivElement>(null);
  const sel = useSeleccionVehiculos();
  const consulta = useConsultaVehiculos({ anunciar, limpiarSeleccion: sel.limpiarSeleccion, limpiarAvisos: sel.limpiarAvisos, focoTablaRef });
  const { carga, filtros, actualizarFila } = consulta;
  const filas = carga.tipo === "listo" ? carga.lista.vehiculos : [];
  const acciones = useAccionesSeleccion(sel, { filas, puedeSanear, puedeDescargar, actualizarFila, anunciar, resultadoRef });
  const hayCasillas = puedeSanear || puedeDescargar;
  const errorCampo = carga.tipo === "error" && carga.error.tipo === "CAMPO" ? carga.error : null;

  let contenido: ReactNode;
  if (carga.tipo === "cargando" || consulta.acceso) {
    contenido = <VehicleLoader label="Cargando vehículos…" />;
  } else if (carga.tipo === "error" && carga.error.tipo === "CAMPO") {
    contenido = (
      <InlineAlert tone="warning" title="Revisa los filtros">
        Corrige el filtro marcado para ver los vehículos.
      </InlineAlert>
    );
  } else if (carga.tipo === "error") {
    const reiniciar = carga.error.tipo === "AVISO" && carga.error.reiniciarPagina;
    contenido = (
      <InlineAlert
        tone="error"
        title="No pudimos cargar los vehículos"
        action={
          <button type="button" onClick={consulta.recargar} className={BOTON_REINTENTAR_CLS}>
            Reintentar
          </button>
        }
      >
        {/* AC43 (D-107): texto fijo en español; nunca el mensaje, el código ni el estado HTTP del servidor. */}
        {reiniciar ? "La página pedida no es válida. Vuelve a la primera página." : "Revisa tu conexión e intenta de nuevo."}
      </InlineAlert>
    );
  } else if (carga.lista.vehiculos.length === 0 && hayFiltrosVehiculos(filtros)) {
    contenido = (
      <EstadoVacio
        anunciar
        nivelTitulo={3}
        icono={<IconoBuscar className="h-7 w-7" aria-hidden="true" strokeWidth={1.5} />}
        titulo="Ningún vehículo coincide"
        mensaje="Prueba con otros filtros."
        acciones={[
          <button key="limpiar" type="button" onClick={consulta.limpiar} className={BOTON_SECUNDARIO_CLS}>
            Limpiar filtros
          </button>,
        ]}
      />
    );
  } else if (carga.lista.vehiculos.length === 0) {
    contenido = (
      <EstadoVacio
        nivelTitulo={3}
        icono={<IconoPortapapeles className="h-7 w-7" aria-hidden="true" strokeWidth={1.5} />}
        titulo="Aún no hay vehículos"
        mensaje="Aquí verás todas las placas de tus diagnósticos, en proceso y terminadas."
        acciones={
          puedeCrear
            ? [
                <Link key="express" href="/diagnosticos/nuevo?modo=express" className={BOTON_PRIMARIO_CLS}>
                  Diagnosticar un vehículo
                </Link>,
              ]
            : undefined
        }
      />
    );
  } else {
    const { lista: datos } = carga;
    contenido = (
      <div className="flex flex-col gap-3">
        {hayCasillas && <BarraVehiculos sel={sel} acciones={acciones} puedeSanear={puedeSanear} puedeDescargar={puedeDescargar} resultadoRef={resultadoRef} />}
        <TablaVehiculos
          vehiculos={datos.vehiculos}
          hayCasillas={hayCasillas}
          puedeSanear={puedeSanear}
          puedeDescargar={puedeDescargar}
          selecciones={acciones.selecciones}
          seleccion={sel.seleccion}
          focoPendienteRef={focoTablaRef}
          onCambiarSeleccion={acciones.cambiarSeleccion}
          accionesFila={(f, placa) => (
            <AccionesFila
              f={f}
              placa={placa}
              puedeSanear={puedeSanear}
              onActualizarFila={actualizarFila}
              onAbrirPanel={(fila, disparador) => setPanel({ fila, disparador })}
              onRefrescar={consulta.refrescar}
              onAnunciar={anunciar}
            />
          )}
        />
        <p className="text-sm text-muted-foreground">{contarVehiculos(datos.total)}</p>
        {/* UA-2 (rechaza D-105): el pie estándar del kit, con «Filas por página» de TAMANOS_DE_PAGINA. */}
        <Pagination
          page={datos.pagina}
          pageSize={datos.tamano}
          totalCount={datos.total}
          onPageChange={consulta.irAPagina}
          onPageSizeChange={(tamano) => consulta.aplicar({ tamano, pagina: 1 })}
          ariaLabel="Paginación de vehículos"
          noun="vehículos"
        />
      </div>
    );
  }

  return (
    <section aria-labelledby="listado-vehiculos-titulo" className={`${TARJETA_CLS} flex flex-col gap-4`}>
      <h2 id="listado-vehiculos-titulo" className="text-lg font-semibold text-flit-primary">
        Vehículos
      </h2>
      <FiltrosVehiculos valor={filtros} error={errorCampo} onCambiar={consulta.aplicar} onLimpiar={consulta.limpiar} onActualizar={consulta.recargar} />
      <div>{contenido}</div>
      <div data-region-viva-vehiculos="">{region}</div>
      {panel && (
        <PanelDeudaSaneamiento
          key={panel.fila.vehiculo_id}
          diagnosticoId={panel.fila.diagnostico_id}
          vehiculoId={panel.fila.vehiculo_id}
          identificador={identificadorFila(panel.fila) || "Sin placa"}
          puedeSanear={puedeSanear}
          puedeDescargar={puedeDescargar}
          devolverFocoA={panel.disparador}
          onClose={() => setPanel(null)}
          onCambioEstado={(estado) => actualizarFila(panel.fila.vehiculo_id, filaDeEstado(estado))}
          accionBandera={banderaPanelDeuda(panel.fila.diagnostico_id, {
            id: panel.fila.vehiculo_id,
            placa: panel.fila.placa,
            vin: panel.fila.vin,
            identificador: panel.fila.identificador,
          })}
        />
      )}
    </section>
  );
}
