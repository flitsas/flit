// Imágenes propias del diseño (exportadas del prototipo): se sirven desde /assets/flit. Los íconos genéricos salen de
// lucide-react, no se copian. Faltan por entregar el video «Cómo funciona», tres banners (bannerpnanatural-17/18/19) y los logos de terceros.
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
  // Banners del carrusel (3967×908 px, con el texto dentro de la imagen). Se sirven con next/image para no mandar ~1 MB al móvil.
  banners: [
    { src: "/assets/banners/banner-f-20.jpg", ancho: 3967, alto: 908, alt: "Tus trámites de tránsito sin filas ni complicaciones. Realiza y haz seguimiento a tus trámites vehiculares desde donde estés, de forma segura y en minutos." },
    { src: "/assets/banners/banner-f-23.jpg", ancho: 3967, alto: 908, alt: "Recuerda que para realizar un trámite debes estar a paz y salvo. Conoce más aquí: curso de infractores, impuestos y más." },
  ],
} as const;
