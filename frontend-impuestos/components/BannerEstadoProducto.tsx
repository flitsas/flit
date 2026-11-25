import { InlineAlert, type InlineAlertTone } from "@flit/ui/InlineAlert";
import type { BannerEstado } from "@/lib/sesion";

// Banners de estado del producto (plan §6.1 y §3.1). Los títulos son los del plan; los textos explican el efecto sin
// nombrar credenciales ni códigos internos. Contenido estático (`vivo={false}`): se pintan al abrir el producto y no
// interrumpen al lector de pantalla; el icono y el título dicen el estado, no solo el color.
const BANNERS: Readonly<Record<BannerEstado, { tono: InlineAlertTone; titulo: string; texto: string }>> = {
  FIRMA_SIN_CONFIRMAR: {
    tono: "pending",
    titulo: "Firma sin confirmar",
    texto: "Todavía no llega el primer aviso firmado del proveedor. Las liquidaciones se resuelven igual, pero el resultado puede tardar más.",
  },
  SIN_WEBHOOK: {
    tono: "info",
    titulo: "Modo sin webhook",
    texto: "Los resultados se consultan al proveedor cada cierto tiempo, así que pueden tardar unos minutos más en aparecer.",
  },
  SUSPENDIDO: {
    tono: "warning",
    titulo: "Producto suspendido: solo lectura",
    texto: "Puedes consultar el historial y los reportes, pero no crear liquidaciones nuevas.",
  },
};

export function BannerEstadoProducto({ banners }: { banners: readonly BannerEstado[] }) {
  if (banners.length === 0) return null;
  return (
    <div className="flex flex-col gap-2">
      {banners.map((b) => (
        <InlineAlert key={b} tone={BANNERS[b].tono} title={BANNERS[b].titulo} vivo={false}>
          {BANNERS[b].texto}
        </InlineAlert>
      ))}
    </div>
  );
}
