"use client";

import { useCallback, useEffect, useRef, useState, type MouseEvent } from "react";
import { descargarArchivo, mensajeDescarga } from "./api";

/**
 * Clic en un enlace `<a href download>` del BFF (HU #13851): sin JS el navegador baja el archivo solo; con JS se intercepta
 * para bajarlo por blob con el nombre del Content-Disposition y, si el servidor responde 409/404/403, mostrar el mensaje
 * en lugar de guardar el cuerpo del error como archivo.
 */
export function useDescarga(porDefecto: string) {
  const [descargando, setDescargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const montado = useRef(true);
  useEffect(() => {
    montado.current = true;
    return () => {
      montado.current = false;
    };
  }, []);

  const alHacerClic = useCallback(
    (e: MouseEvent<HTMLAnchorElement>) => {
      e.preventDefault();
      if (descargando) return;
      const url = e.currentTarget.getAttribute("href");
      if (!url) return;
      setDescargando(true);
      setError(null);
      descargarArchivo(url, porDefecto)
        .catch((err: unknown) => {
          if (montado.current) setError(mensajeDescarga(err));
        })
        .finally(() => {
          if (montado.current) setDescargando(false);
        });
    },
    [descargando, porDefecto],
  );

  return { descargando, error, alHacerClic };
}
