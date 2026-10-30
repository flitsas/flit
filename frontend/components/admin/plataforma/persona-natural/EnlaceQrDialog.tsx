"use client";

// HU #14217 — Diálogo «Enlace y QR»: la dirección pública del canal y el QR de esa misma dirección.
import { useRef, useState } from "react";
import { Copy, Download, QrCode } from "lucide-react";
import { QRCodeSVG } from "qrcode.react";
import { Modal } from "@/components/atom/Modal";
import { direccionCanalPunto, origenPublicoCanal } from "@/lib/plataforma/direccion-canal";
import { PN_FIELD_ERROR_CLS, PN_INPUT_CLS } from "./pn-ui";
import { PnButton } from "./pn-componentes";

export function EnlaceQrDialog({
  codigo,
  nombre,
  onClose,
}: {
  codigo: string;
  nombre: string;
  onClose: () => void;
}) {
  const origen = origenPublicoCanal();
  const direccion = direccionCanalPunto(codigo, origen);
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
          <PnButton variant="secondary" icon={Download} onClick={descargar}>
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
          Dirección del canal de «{nombre}». Quien atiende en el punto abre esta dirección: la placa y el documento no
          van en el enlace.
        </p>
        {origen === "" && (
          <p role="status" className={PN_FIELD_ERROR_CLS}>
            Falta el origen público del canal. Configura NEXT_PUBLIC_CANAL_OT_ORIGEN para completar la dirección.
          </p>
        )}
        <label htmlFor="direccion-canal" className="flex min-w-0 flex-col gap-1 text-sm font-semibold">
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
          <PnButton variant="primary" icon={Copy} onClick={() => void copiar()}>
            Copiar enlace
          </PnButton>
        </div>
        <p role="status" aria-live="polite" className="min-h-5 text-xs">
          {copiado ? "Enlace copiado." : avisoCopia}
        </p>
      </div>
    </Modal>
  );
}
