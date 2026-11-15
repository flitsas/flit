"use client";

import { useEffect, useMemo, useState } from "react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { SearchableSelect } from "@flit/ui/SearchableSelect";
import { BOTON_REINTENTAR_CLS } from "@/components/configuracion/estilos";
import { etiquetaNit, listarEmpresasBackoffice, type EmpresaBackoffice } from "@/lib/configuracion";

export const TODAS_LAS_COMPANIAS = "Todas las compañías";

/**
 * Selector de compañía de la vista global (spec-ui §4.4, contrato §6): `SearchableSelect` sobre GET /backoffice/empresas
 * (solo compañías con Diagnóstico activo), con «Todas las compañías» (valor "") por defecto. Se monta solo con la
 * capacidad correspondiente; la API vuelve a exigirla. Si la lista falla (503 de Identidad) queda deshabilitado con un
 * aviso compacto y Reintentar: la vista global sigue funcionando. Mientras cargan los datos de la pantalla no se
 * deshabilita.
 */
export function SelectorCompania({ value, onChange, className = "" }: { value: string; onChange: (empresa: string) => void; className?: string }) {
  const [empresas, setEmpresas] = useState<EmpresaBackoffice[] | null>(null);
  const [fallo, setFallo] = useState(false);
  const [intento, setIntento] = useState(0);

  useEffect(() => {
    const control = new AbortController();
    listarEmpresasBackoffice(control.signal).then(
      (lista) => {
        if (control.signal.aborted) return;
        setEmpresas(lista);
        setFallo(false);
      },
      () => {
        if (!control.signal.aborted) setFallo(true);
      },
    );
    return () => control.abort();
  }, [intento]);

  const opciones = useMemo(() => (empresas ?? []).map((e) => ({ value: e.id, label: e.nombre, hint: etiquetaNit(e.nit) })), [empresas]);

  return (
    <div className={`flex w-full flex-col gap-1 sm:w-72 ${className}`}>
      <SearchableSelect
        label="Compañía"
        defaultLabel={TODAS_LAS_COMPANIAS}
        options={opciones}
        value={value}
        onChange={onChange}
        disabled={fallo || empresas === null}
        noResultsLabel="Ninguna compañía coincide"
      />
      {fallo ? (
        <InlineAlert
          tone="warning"
          compact
          action={
            <button
              type="button"
              className={BOTON_REINTENTAR_CLS}
              onClick={() => {
                setFallo(false);
                setIntento((i) => i + 1);
              }}
            >
              Reintentar
            </button>
          }
        >
          No se pudo cargar la lista de compañías
        </InlineAlert>
      ) : null}
    </div>
  );
}
