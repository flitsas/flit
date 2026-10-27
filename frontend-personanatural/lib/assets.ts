// Imágenes propias del diseño (exportadas del prototipo): se sirven desde /assets/flit. Los íconos genéricos salen de
// lucide-react, no se copian. Faltan por entregar el video «Cómo funciona», los banners y los logos de terceros.
export const ASSETS = {
  logo: "/assets/flit/logo-flit.svg",
  acciones: {
    iniciar: "/assets/flit/action-car.svg",
    cotizar: "/assets/flit/action-currency.svg",
    consultar: "/assets/flit/action-search.svg",
  },
  pasos: {
    diagnostico: "/assets/flit/paso-diagnostico.svg",
    validacionDeIdentidad: "/assets/flit/paso-validacion_de_identidad.svg",
    documentosDigitales: "/assets/flit/paso-documentos_digitales.svg",
    pagoFacil: "/assets/flit/paso-pago_facil.svg",
  },
} as const;
