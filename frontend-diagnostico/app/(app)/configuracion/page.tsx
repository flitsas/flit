"use client";

import { useCallback, useEffect, useState } from "react";
import { Icono } from "@flit/ui/Icono";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { useSesion } from "@/components/DiagnosticoShell";
import { ConfiguracionTabs, type Pestana } from "@/components/configuracion/ConfiguracionTabs";
import { PestanaContrato } from "@/components/configuracion/PestanaContrato";
import { PestanaOperacion } from "@/components/configuracion/PestanaOperacion";
import { BOTON_REINTENTAR_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { obtenerConfiguracion, type Configuracion } from "@/lib/configuracion";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";

type Carga = { tipo: "cargando" } | { tipo: "error"; mensaje: string } | { tipo: "listo"; configuracion: Configuracion };

/**
 * Pantalla 10 (HU #13744): configuración de Diagnóstico en dos niveles. Cada pestaña se muestra solo si la sesión trae la
 * capacidad: Contrato y Operación con verConfiguracion (Operación editable con editarConfiguracion) y el editor del
 * contrato con editarContrato (backoffice FLIT). Nada de usuarios, roles, suscripción ni retención: eso vive en otros
 * productos de la suite.
 */
export default function ConfiguracionPagina() {
  const { sesion } = useSesion();
  const capacidades = sesion?.capacidades;
  const verConfiguracion = capacidades?.verConfiguracion ?? false;
  const editarContrato = capacidades?.editarContrato ?? false;

  const [intento, setIntento] = useState(0);
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });

  useEffect(() => {
    if (!verConfiguracion) return;
    const controller = new AbortController();
    obtenerConfiguracion(controller.signal)
      .then((configuracion) => setCarga({ tipo: "listo", configuracion }))
      .catch((e: unknown) => {
        if (controller.signal.aborted) return;
        setCarga({ tipo: "error", mensaje: e instanceof Error ? e.message : "No se pudo cargar la configuración." });
      });
    return () => controller.abort();
  }, [verConfiguracion, intento]);

  const reintentar = useCallback(() => {
    setCarga({ tipo: "cargando" });
    setIntento((i) => i + 1);
  }, []);

  const alGuardar = useCallback((configuracion: Configuracion) => setCarga({ tipo: "listo", configuracion }), []);

  let contenido;
  if (!verConfiguracion && !editarContrato) {
    contenido = (
      <section aria-labelledby="configuracion-vacio" className={`${TARJETA_CLS} flex flex-col items-center gap-3 py-12 text-center`}>
        <Icono nombre="ajustes" className="h-10 w-10 text-flit-brand" />
        <h2 id="configuracion-vacio" className="text-lg font-semibold">
          No tienes acceso a la configuración
        </h2>
        <p className="max-w-md text-sm text-muted-foreground">Pídele acceso al administrador de tu compañía.</p>
      </section>
    );
  } else if (verConfiguracion && carga.tipo === "cargando") {
    contenido = <VehicleLoader label="Cargando configuración…" />;
  } else if (verConfiguracion && carga.tipo === "error") {
    contenido = (
      <InlineAlert
        tone="error"
        title="No se pudo cargar la configuración"
        action={
          <button type="button" onClick={reintentar} className={BOTON_REINTENTAR_CLS}>
            Reintentar
          </button>
        }
      >
        {carga.mensaje}
      </InlineAlert>
    );
  } else {
    const configuracion = carga.tipo === "listo" ? carga.configuracion : null;
    const pestanas: Pestana[] = [
      { id: "contrato", etiqueta: "Contrato", icono: "documento", contenido: <PestanaContrato contrato={configuracion?.contrato ?? null} puedeEditar={editarContrato} /> },
    ];
    if (configuracion)
      pestanas.push({
        id: "operacion",
        etiqueta: "Operación",
        icono: "ajustes",
        contenido: (
          <PestanaOperacion
            configuracion={configuracion}
            puedeEditar={capacidades?.editarConfiguracion ?? false}
            onGuardado={alGuardar}
            onRecargar={reintentar}
          />
        ),
      });
    contenido = <ConfiguracionTabs etiqueta="Configuración de Diagnóstico" pestanas={pestanas} />;
  }

  return (
    <>
      <CabeceraModulo
        as="section"
        aria-labelledby="configuracion-titulo"
        tituloId="configuracion-titulo"
        titulo="Configuración"
        descripcion={sesion?.empresa.nombre}
        icono="configuracion"
      />
      {contenido}
    </>
  );
}
