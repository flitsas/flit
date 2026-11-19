"use client";

import { useCallback, useEffect, useState } from "react";
import { DocumentPreviewModal } from "@/components/shared/DocumentPreviewModal";
import { fetchOtAttachmentPreviewUrl, type OtApiScope } from "@/lib/api/admin-ot";
import type { TimelineArchivo } from "@/components/operacion/detalle/TimelineTrackPanel";
import { mimetypeDe } from "@/lib/documents/mimetype";

/**
 * Bug #14604 — visor del OT para abrir un adjunto del trámite que no está en la lista de documentos a
 * mano (p. ej. el archivo nuevo o el anterior de un documento actualizado, desde la línea de tiempo).
 * Mismo camino que `OtDetalleDocumentos`: URL de previsualización del OT y bytes re-empaquetados como
 * Blob con el tipo real, para que el PDF se pinte en el iframe en vez de forzar la descarga.
 */
export function useOtVisorArchivo(procedureId: string, scope?: OtApiScope) {
  const [archivo, setArchivo] = useState<TimelineArchivo | null>(null);
  const [url, setUrl] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const liberar = useCallback(() => {
    setUrl((prev) => {
      if (prev) URL.revokeObjectURL(prev);
      return null;
    });
  }, []);

  useEffect(() => liberar, [liberar]);

  const cerrar = useCallback(() => {
    liberar();
    setArchivo(null);
    setError(null);
    setLoading(false);
  }, [liberar]);

  const abrir = useCallback(
    async (a: TimelineArchivo) => {
      liberar();
      setArchivo(a);
      setError(null);
      setLoading(true);
      try {
        const res = await fetchOtAttachmentPreviewUrl(procedureId, a.id, scope);
        const blob = await fetch(res.url).then((r) => {
          if (!r.ok) throw new Error(String(r.status));
          return r.blob();
        });
        setUrl(URL.createObjectURL(new Blob([blob], { type: mimetypeDe(a.filename) })));
      } catch {
        setError("No se pudo abrir el documento. Intenta de nuevo más tarde.");
      } finally {
        setLoading(false);
      }
    },
    [procedureId, scope, liberar],
  );

  const visor = (
    <DocumentPreviewModal
      open={archivo !== null}
      onClose={cerrar}
      title={archivo?.filename ?? "Previsualización"}
      mimetype={archivo ? mimetypeDe(archivo.filename) : null}
      url={url}
      loading={loading}
      error={error}
    />
  );

  return { abrir, visor };
}
