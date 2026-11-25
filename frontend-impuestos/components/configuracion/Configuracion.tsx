"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { iconoComo } from "@flit/ui/iconoComo";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { Pestanas } from "@flit/ui/Pestanas";
import { useSesion } from "@/components/ImpuestosShell";
import { BOTON_REINTENTAR_CLS, TARJETA_CLS } from "@/components/comun/estilos";
import { useEmpresaBackoffice } from "@/lib/backoffice/api";
import { obtenerConfiguracion, type ConfiguracionImpuestos } from "@/lib/configuracion/api";
import { useEmpresaSeleccionada } from "@/lib/empresaSeleccionada";
import { mensajeErrorFijo } from "@/lib/errores";
import { PERMISOS, tienePermiso } from "@/lib/sesion";
import { PestanaContractual } from "./PestanaContractual";
import { PestanaOperativa } from "./PestanaOperativa";

type Carga = { tipo: "cargando" } | { tipo: "error"; mensaje: string } | { tipo: "listo"; configuracion: ConfiguracionImpuestos };
type IdPestana = "operativa" | "contractual";

const IconoEmpresas = iconoComo("empresas");

/**
 * /configuracion (spec-visual §3.9; D-CONFIG, plan §6.1). Pestaña «Operativa» (Administrador Impuestos y Super Admin) y
 * «Contractual» (todos con impuestos.configuracion.ver; editable solo con impuestos.configuracion.contrato.editar). El
 * Super Admin trabaja sobre la compañía de ?empresa=: sin ella no se pide nada y se le pide elegir una en la barra.
 * `?pestana=contractual` abre esa pestaña (enlace «Editar contrato» del backoffice). Sin el permiso, la 403.
 */
export function Configuracion() {
  const router = useRouter();
  const params = useSearchParams();
  const { sesion } = useSesion();
  const { empresa } = useEmpresaSeleccionada();
  const puedeVer = tienePermiso(sesion, PERMISOS.configuracionVer);
  const sinAcceso = sesion !== null && !puedeVer;
  const superAdmin = sesion?.esSuperAdmin === true;
  // Para otro usuario ?empresa= no cuenta: la API respondería 400 EMPRESA_NO_PERMITIDA.
  const empresaVista = superAdmin ? empresa : null;
  const faltaEmpresa = superAdmin && !empresaVista;
  const consultar = sesion !== null && puedeVer && !faltaEmpresa;

  const [pestana, setPestana] = useState<IdPestana>(params?.get("pestana") === "contractual" ? "contractual" : "operativa");
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);

  useEffect(() => {
    if (sinAcceso) router.replace("/403?code=SIN_PERMISO");
  }, [sinAcceso, router]);

  useEffect(() => {
    if (!consultar) return;
    const controller = new AbortController();
    obtenerConfiguracion(empresaVista, controller.signal).then(
      (configuracion) => {
        if (!controller.signal.aborted) setCarga({ tipo: "listo", configuracion });
      },
      (e: unknown) => {
        // Texto fijo; nunca el mensaje del servidor.
        if (!controller.signal.aborted) setCarga({ tipo: "error", mensaje: mensajeErrorFijo(e, "Revisa tu conexión e intenta de nuevo.") });
      },
    );
    return () => controller.abort();
  }, [consultar, empresaVista, intento]);

  const elegida = useEmpresaBackoffice(empresaVista);

  const reintentar = useCallback(() => {
    setCarga({ tipo: "cargando" });
    setIntento((n) => n + 1);
  }, []);
  const alGuardar = useCallback((configuracion: ConfiguracionImpuestos) => setCarga({ tipo: "listo", configuracion }), []);

  if (!sesion || !puedeVer) return null;

  let contenido;
  if (faltaEmpresa) {
    contenido = (
      <section className={TARJETA_CLS}>
        <EstadoVacio nivelTitulo={2} icono={<IconoEmpresas className="h-7 w-7" aria-hidden="true" strokeWidth={1.5} />} titulo="Elige una compañía en la barra para editar su contrato" />
      </section>
    );
  } else if (carga.tipo === "cargando") {
    contenido = <VehicleLoader label="Cargando la configuración…" />;
  } else if (carga.tipo === "error") {
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
    const { configuracion } = carga;
    // La clave reinicia los formularios al cambiar de compañía (otra compañía, otros valores de partida).
    const clave = empresaVista ?? "propia";
    contenido = (
      <section className={TARJETA_CLS}>
        <Pestanas<IdPestana>
          ariaLabel="Configuración de Impuestos"
          active={pestana}
          onChange={setPestana}
          tabs={[
            {
              id: "operativa",
              label: "Operativa",
              content: (
                <PestanaOperativa
                  key={clave}
                  configuracion={configuracion}
                  puedeEditar={tienePermiso(sesion, PERMISOS.configuracionEditar)}
                  empresa={empresaVista}
                  onGuardado={alGuardar}
                />
              ),
            },
            {
              id: "contractual",
              label: "Contractual",
              content: (
                <PestanaContractual
                  key={clave}
                  configuracion={configuracion}
                  puedeEditar={tienePermiso(sesion, PERMISOS.contratoEditar)}
                  tenantId={empresaVista}
                  onGuardado={alGuardar}
                />
              ),
            },
          ]}
        />
      </section>
    );
  }

  const descripcion = superAdmin ? (empresaVista ? elegida?.nombre : "Todas las compañías") : sesion.empresa.nombre || undefined;
  return (
    <>
      <CabeceraModulo titulo="Configuración" icono="configuracion" descripcion={descripcion} />
      {contenido}
    </>
  );
}
