"use client";

import Link from "next/link";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { Icono } from "@flit/ui/Icono";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { NotificacionPersistente } from "@flit/ui/NotificacionPersistente";
import { HistorialSaneamiento } from "@/components/backoffice/HistorialSaneamiento";
import { useSesion } from "@/components/DiagnosticoShell";
import { BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { ApiError } from "@/lib/api";
import { rotuloEmpresa, useNombresEmpresas } from "@/lib/backoffice/useNombresEmpresas";
import { CabeceraGestion } from "./gestionSaneamiento/CabeceraGestion";
import { ColumnaDeuda } from "./gestionSaneamiento/ColumnaDeuda";
import { ColumnaGestion } from "./gestionSaneamiento/ColumnaGestion";
import { ModalesGestion } from "./gestionSaneamiento/ModalesGestion";
import { useGestionSaneamiento } from "./gestionSaneamiento/useGestionSaneamiento";

export { motivoNoCerrarSaneado } from "./gestionSaneamiento/comun";

// PS-5, gestión de un saneamiento en el backoffice (spec-ui §6; plan §7.6; HU #14577 AC29–AC37). Pide en paralelo el
// detalle (historial completo) y la deuda del backoffice (SP-7), cada uno con sus estados. Izquierda: la deuda en lectura
// (SeccionesDeuda de PS-2, plegada por debajo de 1024 px); derecha: la gestión según el estado; pie: el historial. Un 409
// VERSION_DESACTUALIZADA abre la NotificacionPersistente con «Recargar», que trae los datos vigentes y descarta borradores.
// Todo valor visual sale de tokens del kit; sin hex.
// CR-5: el estado y las escrituras viven en useGestionSaneamiento; la pantalla compone el encabezado (CabeceraGestion), la
// columna de deuda (ColumnaDeuda), la de gestión y cierre (ColumnaGestion) y el modal abierto (ModalesGestion).

export interface GestionSaneamientoProps {
  sid: string;
  /** Compañía dueña del saneamiento (?empresa=). */
  empresa: string;
}

export function GestionSaneamiento({ sid, empresa }: GestionSaneamientoProps) {
  const nombres = useNombresEmpresas();
  const { sesion } = useSesion();
  const g = useGestionSaneamiento(sid, empresa);
  const { carga, san, deuda } = g;

  if (carga.tipo === "cargando" || g.detalle403) return <VehicleLoader label="Cargando saneamiento…" />;
  if (carga.tipo === "error") {
    if (carga.error instanceof ApiError && carga.error.status === 404) {
      return (
        <EstadoVacio
          icono={<Icono nombre="buscar" className="h-7 w-7" />}
          titulo="No encontramos este saneamiento"
          mensaje="Revisa la compañía o vuelve a la bandeja."
          acciones={[
            <Link key="volver" href="/backoffice/saneamientos" className={BOTON_SECUNDARIO_CLS}>
              Volver a la bandeja
            </Link>,
          ]}
        />
      );
    }
    return (
      <InlineAlert
        tone="error"
        title="No pudimos cargar el saneamiento"
        action={
          <button type="button" onClick={g.reintentar} className={BOTON_REINTENTAR_CLS}>
            Reintentar
          </button>
        }
      >
        Intenta de nuevo en un momento.
      </InlineAlert>
    );
  }
  if (!san) return null;

  const deudaLista = deuda.tipo === "listo" ? deuda.deuda : null;
  const placa = san.placa || deudaLista?.vehiculo.placa || san.vehiculo_id || "Saneamiento";
  const veredicto = san.veredicto ?? deudaLista?.dictamen?.veredicto ?? null;

  return (
    <>
      <CabeceraGestion id={g.id} san={san} empresa={empresa} placa={placa} nombreEmpresa={rotuloEmpresa(nombres, empresa)} veredicto={veredicto} />
      <div className="grid gap-4 lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)]">
        <ColumnaDeuda deuda={deuda} onReintentar={g.reintentarDeuda} />
        <ColumnaGestion g={g} san={san} />
      </div>
      <section className={TARJETA_CLS} aria-label="Pie del saneamiento">
        <HistorialSaneamiento historial={san.historial} usuarioSesionId={sesion?.usuario.id} />
      </section>

      <ModalesGestion g={g} />

      {g.conflicto && (
        <div className="fixed right-4 top-20 z-[60] max-[640px]:inset-x-2">
          <NotificacionPersistente
            titulo="Otra persona cambió este saneamiento"
            mensaje="Recarga para ver los cambios. Lo que escribiste en el concepto con error se conserva hasta que recargues."
            accion={{ label: "Recargar", onClick: g.recargar }}
            onCerrar={g.cerrarConflicto}
          />
        </div>
      )}
      <div data-region-viva>{g.region}</div>
    </>
  );
}
