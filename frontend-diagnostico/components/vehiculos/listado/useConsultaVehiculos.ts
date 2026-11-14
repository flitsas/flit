"use client";

import { useEffect, useState, type RefObject } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import type { SaneamientoFila } from "@/lib/saneamiento/api";
import { filtrosVehiculosDesdeUrl, listarVehiculos, traducirErrorVehiculos, urlVehiculos, type ErrorVehiculos, type FiltrosVehiculos as Filtros, type ListaVehiculos } from "@/lib/vehiculos/listado";

// PS-3 (CR-5): la consulta de GET /vehiculos. Los filtros viven en la URL (`?vista=vehiculos&…`); cada cambio la reescribe
// sin agregar entradas al historial y vuelve a la página 1. Un 403 lleva a la página 403. Tras cambiar de página, la tabla
// nueva recibe el foco y se anuncia «Página p de n» (spec-ui §4.5).

export type CargaVehiculos = { tipo: "cargando" } | { tipo: "error"; error: ErrorVehiculos } | { tipo: "listo"; lista: ListaVehiculos };

export interface DependenciasConsulta {
  anunciar: (texto: string) => void;
  /** La URL cambió desde fuera: la selección se vacía (los avisos se quedan). */
  limpiarSeleccion: () => void;
  /** Cambio de filtros o recarga: selección y avisos se vacían. */
  limpiarAvisos: () => void;
  /** true tras cambiar de página: la tabla nueva toma el foco y se anuncia la página. */
  focoTablaRef: RefObject<boolean>;
}

export function useConsultaVehiculos({ anunciar, limpiarSeleccion, limpiarAvisos, focoTablaRef }: DependenciasConsulta) {
  const router = useRouter();
  const params = useSearchParams();
  const clave = params.toString();
  const [claveVista, setClaveVista] = useState(clave);
  const [filtros, setFiltros] = useState<Filtros>(() => filtrosVehiculosDesdeUrl(params));
  const [carga, setCarga] = useState<CargaVehiculos>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);

  // La URL cambió desde fuera (atrás/adelante, enlace compartido): los filtros la siguen.
  if (clave !== claveVista) {
    setClaveVista(clave);
    const deUrl = filtrosVehiculosDesdeUrl(new URLSearchParams(clave));
    if (urlVehiculos(deUrl) !== urlVehiculos(filtros)) {
      setFiltros(deUrl);
      setCarga({ tipo: "cargando" });
      limpiarSeleccion();
    }
  }

  useEffect(() => {
    const control = new AbortController();
    listarVehiculos(filtros, control.signal).then(
      (lista) => {
        if (control.signal.aborted) return;
        setCarga({ tipo: "listo", lista });
        if (focoTablaRef.current) anunciar(`Página ${lista.pagina} de ${Math.max(1, Math.ceil(lista.total / lista.tamano))}`);
      },
      (error: unknown) => {
        if (!control.signal.aborted) setCarga({ tipo: "error", error: traducirErrorVehiculos(error) });
      },
    );
    return () => control.abort();
  }, [filtros, intento, anunciar, focoTablaRef]);

  const acceso = carga.tipo === "error" && carga.error.tipo === "ACCESO" ? carga.error.code : null;
  useEffect(() => {
    if (acceso) router.replace(`/403?code=${encodeURIComponent(acceso)}`);
  }, [acceso, router]);

  /** Cambia filtros: sin página explícita vuelve a la 1; reescribe la URL sin agregar entradas al historial. */
  function aplicar(cambios: Partial<Filtros>) {
    const siguiente: Filtros = { ...filtros, ...cambios, pagina: cambios.pagina ?? 1 };
    if (urlVehiculos(siguiente) === urlVehiculos(filtros)) return;
    setFiltros(siguiente);
    setCarga({ tipo: "cargando" });
    limpiarAvisos();
    router.replace(urlVehiculos(siguiente), { scroll: false });
  }

  function recargar() {
    if (carga.tipo === "error" && carga.error.tipo === "AVISO" && carga.error.reiniciarPagina) {
      aplicar({ pagina: 1 });
      return;
    }
    setCarga({ tipo: "cargando" });
    limpiarAvisos();
    setIntento((n) => n + 1);
  }

  /** Relectura sin loader (409 de la bandera: el estado cambió en el servidor). */
  const refrescar = () => setIntento((n) => n + 1);

  const limpiar = () => aplicar({ placa: "", estado: null, veredicto: null, saneamiento: null, saneamientoEstado: null, modo: null, conTramite: null, desde: "", hasta: "" });

  /** Cambio de página desde el pie: la tabla nueva recibirá el foco. */
  function irAPagina(pagina: number) {
    focoTablaRef.current = true;
    aplicar({ pagina });
  }

  function actualizarFila(vehiculoId: string, saneamiento: SaneamientoFila) {
    setCarga((c) =>
      c.tipo === "listo" ? { ...c, lista: { ...c.lista, vehiculos: c.lista.vehiculos.map((f) => (f.vehiculo_id === vehiculoId ? { ...f, saneamiento } : f)) } } : c,
    );
  }

  return { filtros, carga, acceso, aplicar, recargar, refrescar, limpiar, irAPagina, actualizarFila };
}
