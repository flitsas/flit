"use client";

import { useEffect, useState } from "react";
import { listarEmpresasBackoffice } from "@/lib/configuracion";

// Nombre de la compañía en la bandeja y en la gestión (D-12): la fila solo trae el uuid y no hay endpoint propio; se toma
// del listado de empresas del backoffice. Si falla o no está, «Compañía {8 primeros del id}». Nunca un error en pantalla:
// el nombre es accesorio.

export function rotuloEmpresa(nombres: ReadonlyMap<string, string>, id: string): string {
  return nombres.get(id) ?? `Compañía ${id.slice(0, 8)}`;
}

export function useNombresEmpresas(): ReadonlyMap<string, string> {
  const [nombres, setNombres] = useState<ReadonlyMap<string, string>>(() => new Map());
  useEffect(() => {
    const control = new AbortController();
    listarEmpresasBackoffice(control.signal).then(
      (empresas) => {
        if (!control.signal.aborted) setNombres(new Map(empresas.map((e) => [e.id, e.nombre])));
      },
      () => {},
    );
    return () => control.abort();
  }, []);
  return nombres;
}
