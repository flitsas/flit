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

// Imágenes del asistente Dr. FLIT (`dr-flit`, `dr-flit-full`, `chatbot_dr_flit_1-02`): AÚN NO se han entregado. Mientras una sea `null`
// el chat dibuja un marcador accesible (ícono de lucide en un círculo). Para enchufarlas, copiar el archivo a /public/assets/flit y
// poner aquí su ruta; no hace falta tocar ningún componente.
export const ASSETS_DR_FLIT: { lanzador: string | null; avatar: string | null; completo: string | null } = {
  lanzador: null, // chatbot_dr_flit_1-02: foto de la pestaña flotante (56 px, redonda)
  avatar: null, // dr-flit: avatar de la cabecera del chat (40 px, redondo)
  completo: null, // dr-flit-full: cuerpo completo (aún sin uso en el diseño del portal)
};
