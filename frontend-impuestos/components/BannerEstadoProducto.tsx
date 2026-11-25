import { InlineAlert, type InlineAlertTone } from "@flit/ui/InlineAlert";
import { PRIORIDAD_BANNERS, type BannerEstado } from "@/lib/sesion";

// Banners de estado del producto (plan §6.1, spec-visual §4.3). Títulos en lenguaje de negocio, sin nombrar credenciales,
// webhooks ni códigos internos. Contenido estático (`vivo={false}`): se pintan al abrir el producto, no se cierran y no
// interrumpen al lector de pantalla; el icono y el título dicen el estado, no solo el color. FIRMA_SIN_CONFIRMAR va en
// `info` (azul, en proceso): no pide ninguna acción.
const BANNERS: Readonly<Record<BannerEstado, { tono: InlineAlertTone; titulo: string; texto: string }>> = {
  SUSPENDIDO: {
    tono: "warning",
    titulo: "Impuestos está suspendido: solo lectura",
    texto: "Puedes consultar las liquidaciones, los recibos y los reportes, pero no crear liquidaciones nuevas. Los lotes que estaban en curso terminan solos.",
  },
  NO_LISTO: {
    tono: "pending",
    titulo: "Impuestos todavía no está listo para liquidar",
    texto:
      "La conexión de tu compañía con el proveedor se está configurando. Puedes consultar lo que ya existe; para liquidar, espera a que termine o pídele ayuda a tu administrador.",
  },
  SIN_WEBHOOK: {
    tono: "info",
    titulo: "Los resultados pueden tardar un poco más",
    texto: "Ya puedes liquidar. Los resultados se consultan con el proveedor cada pocos minutos, en lugar de llegar al instante.",
  },
  FIRMA_SIN_CONFIRMAR: {
    tono: "info",
    titulo: "Conexión con el proveedor en verificación",
    texto: "Ya puedes liquidar. Se confirma sola con el primer resultado que llegue.",
  },
};

/** Si llegan varios, solo se muestra el de mayor prioridad (SUSPENDIDO > NO_LISTO > SIN_WEBHOOK > FIRMA_SIN_CONFIRMAR). */
export function BannerEstadoProducto({ banners }: { banners: readonly BannerEstado[] }) {
  const b = PRIORIDAD_BANNERS.find((clave) => banners.includes(clave));
  if (!b) return null;
  return (
    <InlineAlert tone={BANNERS[b].tono} title={BANNERS[b].titulo} vivo={false}>
      {BANNERS[b].texto}
    </InlineAlert>
  );
}
