import { useState } from "react";

/**
 * Pantallas que guardan su estado en la URL (rediseño S1): distingue el eco de lo que la pantalla escribió con
 * `router.replace` de una URL que llega por otra vía (dock, enlace). `useVersion` sube solo con la segunda; úsala como
 * `key` del contenido para rearmarlo con la URL nueva. `registrar` va en el efecto que escribe la URL.
 */
export function crearUrlPropia() {
  let ultima: string | null = null;
  return {
    registrar(query: string) {
      ultima = query;
    },
    useVersion(qs: string): number {
      const [base, setBase] = useState({ qs, version: 0 });
      if (qs !== base.qs) setBase({ qs, version: qs === ultima ? base.version : base.version + 1 });
      return base.version;
    },
  };
}
