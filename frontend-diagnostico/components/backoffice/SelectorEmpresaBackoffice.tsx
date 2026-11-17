"use client";

import { useEffect, useMemo, useState } from "react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { SearchableSelect, type SearchableSelectOption } from "@flit/ui/SearchableSelect";
import { ApiError } from "@/lib/api";
import { CODIGO_IDENTIDAD_NO_CONFIGURADA, etiquetaNit, listarEmpresasBackoffice, type EmpresaBackoffice } from "@/lib/configuracion";
import { AYUDA_CLS, BOTON_REINTENTAR_CLS } from "@/components/configuracion/estilos";

export interface SelectorEmpresaBackofficeProps {
  /** Empresa elegida (uuid) o vacío. Con una del listado, el combobox la muestra elegida (p. ej. la de ?empresa=). */
  value: string;
  /** Se llama con la empresa del listado que se eligió; nunca con un identificador escrito a mano. */
  onChange: (empresa: EmpresaBackoffice) => void;
  /** Id del combobox (para enlazarlo desde fuera). */
  id?: string;
  disabled?: boolean;
  className?: string;
  /** Texto bajo el combobox cuando el listado cargó y no hay empresa elegida. */
  avisoSinElegir?: string;
}

type Listado =
  | { tipo: "cargando" }
  | { tipo: "error"; noConfigurado: boolean; mensaje: string }
  | { tipo: "vacio" }
  | { tipo: "listo"; empresas: EmpresaBackoffice[] };

/**
 * Selector de la empresa del backoffice (GET /backoffice/empresas, HU #13741; Punto 6 de la ola 2026-11-11). Lo comparten el
 * editor de contrato y el listado de diagnósticos del backoffice: se busca por nombre o NIT, sin escribir el identificador.
 * Solo lista las empresas activas con Diagnóstico encendido (decisión 6.1). Cuatro estados: cargando, error de Identidad
 * (no configurado, sin reintento porque no se arregla solo; no disponible o formato inesperado, con Reintentar), vacío y
 * listo. Accesibilidad la del SearchableSelect de @flit/ui: etiqueta visible, teclado completo y anuncio de resultados.
 */
export function SelectorEmpresaBackoffice({ value, onChange, id = "backoffice-empresa", disabled = false, className = "max-w-xl", avisoSinElegir }: SelectorEmpresaBackofficeProps) {
  const [listado, setListado] = useState<Listado>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    listarEmpresasBackoffice(controller.signal).then(
      (empresas) => {
        if (!controller.signal.aborted) setListado(empresas.length > 0 ? { tipo: "listo", empresas } : { tipo: "vacio" });
      },
      (e: unknown) => {
        if (controller.signal.aborted) return;
        setListado({
          tipo: "error",
          noConfigurado: e instanceof ApiError && e.code === CODIGO_IDENTIDAD_NO_CONFIGURADA,
          mensaje: e instanceof Error ? e.message : "Intenta de nuevo en un momento.",
        });
      },
    );
    return () => controller.abort();
  }, [intento]);

  const opciones = useMemo<SearchableSelectOption[]>(
    () => (listado.tipo === "listo" ? listado.empresas.map((e) => ({ value: e.id, label: e.nombre, hint: etiquetaNit(e.nit) })) : []),
    [listado],
  );

  function reintentar() {
    setListado({ tipo: "cargando" });
    setIntento((n) => n + 1);
  }

  function alElegir(empresaId: string) {
    if (listado.tipo !== "listo") return;
    const empresa = listado.empresas.find((e) => e.id === empresaId);
    if (empresa) onChange(empresa);
  }

  if (listado.tipo === "cargando") return <VehicleLoader label="Cargando empresas…" />;
  if (listado.tipo === "error") {
    return (
      <InlineAlert
        tone="error"
        title={listado.noConfigurado ? "El listado de empresas no está configurado" : "No se pudo cargar el listado de empresas"}
        action={
          listado.noConfigurado ? undefined : (
            <button type="button" onClick={reintentar} className={BOTON_REINTENTAR_CLS}>
              Reintentar
            </button>
          )
        }
      >
        {listado.noConfigurado ? "Diagnóstico no tiene configurada la conexión con Identidad. Avisa a soporte FLIT." : listado.mensaje}
      </InlineAlert>
    );
  }
  if (listado.tipo === "vacio") return <p className={AYUDA_CLS}>No hay empresas con Diagnóstico habilitado</p>;

  const elegida = listado.empresas.some((e) => e.id === value);
  return (
    <>
      <SearchableSelect
        id={id}
        label="Empresa"
        options={opciones}
        value={elegida ? value : ""}
        onChange={alElegir}
        placeholder="Busca por nombre o NIT"
        noResultsLabel="Ninguna empresa coincide"
        disabled={disabled}
        className={className}
      />
      {!elegida && avisoSinElegir && <p className={AYUDA_CLS}>{avisoSinElegir}</p>}
    </>
  );
}
