"use client";

// HU #14217 — Diálogo «Enlace y QR»: la dirección pública del canal y el QR de esa misma dirección.
import { useEffect, useRef, useState } from "react";
import { Copy, Download, QrCode } from "lucide-react";
import { QRCodeSVG } from "qrcode.react";
import { Modal } from "@/components/atom/Modal";
import { getPortalDominios, principalActivo } from "@/lib/api/admin-ot-portal-dominio";
import { direccionConDominio, origenPublicoCanal } from "@/lib/plataforma/direccion-canal";
import { PN_FIELD_ERROR_CLS, PN_INPUT_CLS } from "./pn-ui";
import { PnButton } from "./pn-componentes";

export function EnlaceQrDialog({
  codigo,
  nombre,
  transitOfficeId,
  onClose,
}: {
  codigo: string;
  nombre: string;
  /** Organismo del canal. Con él, el enlace y el QR usan su dominio activo (épica #14297, HU #14386). */
  transitOfficeId?: string;
  onClose: () => void;
}) {
  const origen = origenPublicoCanal();
  const [hostActivo, setHostActivo] = useState<string | null>(null);
  const [otrosActivos, setOtrosActivos] = useState<string[]>([]);
  const [buscandoDominio, setBuscandoDominio] = useState(Boolean(transitOfficeId));
  const direccion = direccionConDominio(codigo, origen, hostActivo);

  // Manda el dominio PRINCIPAL y ACTIVO. Sin él (pendientes, fallidos, ninguno o una falla de la consulta) queda la
  // dirección del punto de siempre: no se inventa otro dominio. Los demás activos se listan aparte, solo como texto.
  useEffect(() => {
    if (!transitOfficeId) return;
    let vigente = true;
    getPortalDominios({ transitOfficeId })
      .then((lista) => {
        if (!vigente) return;
        const principal = principalActivo(lista);
        setHostActivo(principal?.host ?? null);
        setOtrosActivos(
          lista.filter((d) => d.estado === "activo" && d !== principal).map((d) => d.host),
        );
      })
      .catch(() => {
        if (!vigente) return;
        setHostActivo(null);
        setOtrosActivos([]);
      })
      .finally(() => {
        if (vigente) setBuscandoDominio(false);
      });
    return () => {
      vigente = false;
    };
  }, [transitOfficeId]);
  const qrRef = useRef<HTMLDivElement>(null);
  const campoRef = useRef<HTMLInputElement>(null);
  const [copiado, setCopiado] = useState(false);
  const [avisoCopia, setAvisoCopia] = useState<string | null>(null);

  async function copiar() {
    setAvisoCopia(null);
    try {
      await navigator.clipboard.writeText(direccion);
      setCopiado(true);
    } catch {
      campoRef.current?.focus();
      campoRef.current?.select();
      setCopiado(false);
      setAvisoCopia("No se pudo copiar solo. La dirección quedó seleccionada para copiarla.");
    }
  }

  function descargar() {
    const svg = qrRef.current?.querySelector("svg");
    if (!svg) return;
    const xml = new XMLSerializer().serializeToString(svg);
    const blob = new Blob([xml], { type: "image/svg+xml;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const enlace = document.createElement("a");
    enlace.href = url;
    enlace.download = `canal-${codigo}.svg`;
    document.body.appendChild(enlace);
    enlace.click();
    enlace.remove();
    URL.revokeObjectURL(url);
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Enlace y QR"
      icon={QrCode}
      size="sm"
      footer={
        <div className="flex flex-wrap justify-end gap-2">
          <PnButton
            variant="secondary"
            icon={Download}
            onClick={descargar}
            disabled={buscandoDominio}
          >
            Descargar QR
          </PnButton>
          <PnButton variant="secondary" onClick={onClose}>
            Cerrar
          </PnButton>
        </div>
      }
    >
      <div className="flex min-w-0 flex-col gap-3">
        <p className="text-sm">
          Dirección del canal de «{nombre}». Quien atiende en el punto abre esta dirección: la placa
          y el documento no van en el enlace.
        </p>
        {buscandoDominio && (
          <p role="status" className="text-xs">
            Buscando si el organismo tiene un dominio activo…
          </p>
        )}
        {origen === "" && !hostActivo && (
          <p role="status" className={PN_FIELD_ERROR_CLS}>
            Falta el origen público del canal. Configura NEXT_PUBLIC_CANAL_OT_ORIGEN para completar
            la dirección.
          </p>
        )}
        <label
          htmlFor="direccion-canal"
          className="flex min-w-0 flex-col gap-1 text-sm font-semibold"
        >
          Dirección del canal
          <input
            ref={campoRef}
            id="direccion-canal"
            readOnly
            value={direccion}
            className={`${PN_INPUT_CLS} font-normal`}
          />
        </label>
        <div className="flex min-w-0 flex-wrap items-center gap-3">
          <div
            ref={qrRef}
            role="img"
            aria-label={`Código QR de ${direccion}`}
            className="shrink-0 rounded-xl border border-[#DFE5ED] bg-white p-2"
          >
            <QRCodeSVG value={direccion} size={160} aria-hidden />
          </div>
          <PnButton
            variant="primary"
            icon={Copy}
            onClick={() => void copiar()}
            disabled={buscandoDominio}
          >
            Copiar enlace
          </PnButton>
        </div>
        {otrosActivos.length > 0 && (
          <div className="text-xs">
            <p className="font-semibold">Otras direcciones activas del organismo</p>
            <ul className="list-disc pl-5">
              {otrosActivos.map((host) => (
                <li key={host}>https://{host}</li>
              ))}
            </ul>
          </div>
        )}
        <p role="status" aria-live="polite" className="min-h-5 text-xs">
          {copiado ? "Enlace copiado." : avisoCopia}
        </p>
      </div>
    </Modal>
  );
}
