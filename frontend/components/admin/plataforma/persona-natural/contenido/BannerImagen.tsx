"use client";

// HU #14076 — Imagen de un banner del BORRADOR. El endpoint exige `Authorization`, así que se descarga como blob y se
// muestra con una URL de objeto que se libera al desmontar. Si no se puede cargar, queda un texto en su lugar.
import { useEffect, useState } from "react";
import { fetchImagenBannerPn } from "@/lib/api/admin-pn-contenido";

export function BannerImagen({ id, alt, className = "" }: { id: string; alt: string; className?: string }) {
  const [src, setSrc] = useState<string | null>(null);
  const [fallo, setFallo] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    let url: string | null = null;
    fetchImagenBannerPn(id, controller.signal)
      .then((u) => {
        if (controller.signal.aborted) {
          URL.revokeObjectURL(u);
          return;
        }
        url = u;
        setSrc(u);
      })
      .catch(() => {
        if (!controller.signal.aborted) setFallo(true);
      });
    return () => {
      controller.abort();
      if (url) URL.revokeObjectURL(url);
    };
  }, [id]);

  if (src) {
    // eslint-disable-next-line @next/next/no-img-element -- blob autenticado: next/image no aplica
    return <img src={src} alt={alt} className={className} />;
  }
  return (
    <span
      role={fallo ? "img" : undefined}
      aria-label={fallo ? `${alt} (sin vista previa)` : undefined}
      className={`flex items-center justify-center bg-[#EEF5FF] text-[10px] text-[#59677D] dark:bg-white/5 dark:text-white/70 ${className}`}
    >
      {fallo ? "Sin vista previa" : "Cargando…"}
    </span>
  );
}
