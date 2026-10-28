// Textos fijos de la landing, portados del diseño de referencia (transfer-step-trail). Son copy de interfaz, no datos:
// los organismos, los tipos de trámite y las tarifas llegan siempre del BFF y no se escriben aquí.

/** Segundos entre diapositivas de los carruseles (mismo ritmo del diseño: 4,5 s). */
export const INTERVALO_CARRUSEL_MS = 4500;
export const INTERVALO_OTROS_SERVICIOS_MS = 4000;

export const NAVEGACION = [
  { etiqueta: "Trámites", ancla: "nuevo-tramite" },
  { etiqueta: "Nosotros", ancla: "nosotros" },
  { etiqueta: "Servicios", ancla: "servicios" },
  { etiqueta: "FAQ", ancla: "faq" },
] as const;

export const PASOS_TRAMITE = [
  { clave: "diagnostico", titulo: "Diagnóstico del vehículo" },
  { clave: "validacionDeIdentidad", titulo: "Validación de identidad" },
  { clave: "documentosDigitales", titulo: "Documentos digitales" },
  { clave: "pagoFacil", titulo: "Pago fácil y rápido" },
] as const;

export const SOMOS_FLIT = [
  { numero: "01", titulo: "Cumplimiento", texto: "Procesos alineados con la normativa de tránsito y con las fuentes oficiales." },
  { numero: "02", titulo: "Trazabilidad", texto: "Cada acción queda registrada: quién, cuándo y con qué soporte." },
  { numero: "03", titulo: "Cercanía", texto: "Acompañamos la puesta en marcha y la operación de cada cliente." },
] as const;

export type IconoServicio = "SOAT" | "Curso" | "Taller" | "Licencia" | "Revision" | "Impuestos";

export interface Servicio {
  icono: IconoServicio;
  titulo: string;
  texto: string;
  cta: string;
}

export const SERVICIOS_ADICIONALES: readonly Servicio[] = [
  { icono: "SOAT", titulo: "SOAT Vehicular", texto: "Adquiere tu SOAT", cta: "Compra aquí" },
  { icono: "Curso", titulo: "Curso de infractores", texto: "Aprovecha hasta el 50% de descuento con el curso de infractores", cta: "Iniciar curso en CIA" },
  { icono: "Taller", titulo: "Talleres aliados", texto: "Repuestos, cambio de aceite, pintura y más. Conoce los Talleres aliados", cta: "Agendar cita" },
  { icono: "Licencia", titulo: "Licencia de conducción", texto: "¿Licencia de conducción vencida? Agenda tu cita en nuestro CRC de confianza", cta: "Agendar cita" },
  { icono: "Revision", titulo: "Revisión técnico-mecánica", texto: "Realiza la revisión técnico-mecánica en nuestro CDA de confianza", cta: "Agendar cita" },
];

export const OTROS_SERVICIOS: readonly Servicio[] = [
  { icono: "Curso", titulo: "Cursos de Infractores", texto: "Descuentos en comparendos al completar el curso de infractores.", cta: "Agendar" },
  { icono: "Licencia", titulo: "Renovación de Licencia de Conducción", texto: "Renueva tu licencia de conducción en nuestro CRC de confianza.", cta: "Agendar" },
  { icono: "Taller", titulo: "Talleres Aliados para Mantenimiento Vehicular", texto: "Repuestos, cambio de aceite, pintura y más en talleres aliados.", cta: "Agendar" },
  { icono: "SOAT", titulo: "Compra de SOAT Digital", texto: "Adquiere tu SOAT 100% digital en minutos.", cta: "Comprar" },
  { icono: "Impuestos", titulo: "Pago de Impuestos Vehiculares", texto: "Paga tus impuestos vehiculares en línea, sin filas.", cta: "Comprar" },
];

export const PREGUNTAS_FRECUENTES = [
  {
    pregunta: "¿Cómo me entregan la tarjeta de propiedad?",
    respuesta:
      "Una vez el Organismo de Tránsito aprueba la solicitud, la licencia de tránsito (tarjeta de propiedad) física se envía por mensajería especializada a la dirección registrada, o puedes descargar el documento digital en PDF certificado.",
  },
  {
    pregunta: "¿Cuánto tarda la aprobación de mi trámite?",
    respuesta:
      "El tiempo estimado de respuesta por parte de la Secretaría de Movilidad es de 24 a 48 horas hábiles a partir del pago y validación biométrica de los participantes.",
  },
  {
    pregunta: "¿Cómo hago el pago de mi trámite?",
    // Copy propuesto (HU-E2 AC5): el pago es por transferencia Bancolombia con comprobante, ya no por PSE. Pendiente de aprobación del PO.
    respuesta:
      "Pagas los derechos de tránsito y el servicio digital por transferencia a la cuenta Bancolombia que verás en el Paso 4 del proceso de radicación, y cargas allí el comprobante del pago para que lo revisemos.",
  },
  {
    pregunta: "¿Qué pasa si no tengo SOAT o Técnico-Mecánica vigente?",
    respuesta:
      "Nuestro sistema realiza un diagnóstico automático frente al RUNT. En caso de presentar inconsistencias o faltantes, te notificaremos en el Paso 1 para que puedas ponerte al día antes de radicar.",
  },
  {
    pregunta: "¿Cómo se realiza la validación de identidad biométrica?",
    respuesta:
      "Recibirás un enlace seguro a tu correo electrónico o celular para tomar una fotografía de tu rostro y la foto de tu cédula por ambos lados en cuestión de minutos.",
  },
] as const;
