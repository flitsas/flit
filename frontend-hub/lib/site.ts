// Contenido institucional del landing de FLIT: la empresa, por qué FLIT, cómo empezar y contacto. Primera versión
// (2026-10-02), sin precios por decisión de negocio; el área de diseño y la comercial la ajustarán.
//
// POR CONFIRMAR con comercial: el correo y el sitio de contacto (`CONTACT`).

export const COMPANY = {
  name: "FLIT",
  headline: "Tecnología para la movilidad en Colombia",
  summary:
    "FLIT desarrolla soluciones digitales para las empresas que mueven vehículos en Colombia: concesionarios, " +
    "compañías de renting y leasing, administradores de flotas, gestores de trámites y organismos de tránsito.",
  mission:
    "Llevamos a un solo lugar lo que hoy se hace entre ventanillas, correos y hojas de cálculo: trámites, " +
    "comparendos y el estado de cada vehículo, conectados con las fuentes oficiales y con trazabilidad de punta a punta.",
  values: [
    { title: "Cumplimiento", text: "Procesos alineados con la normativa de tránsito y con las fuentes oficiales." },
    { title: "Trazabilidad", text: "Cada acción queda registrada: quién, cuándo y con qué soporte." },
    { title: "Cercanía", text: "Acompañamos la puesta en marcha y la operación de cada cliente." },
  ],
};

export const PILLARS = [
  {
    icon: "key-round",
    title: "Una sola cuenta",
    text: "Tus usuarios entran una vez y pasan de un producto a otro sin volver a iniciar sesión.",
  },
  {
    icon: "plug",
    title: "Conectado con las fuentes oficiales",
    text: "Integraciones con RUNT, SIMIT y proveedores de validación de identidad, sin digitar dos veces.",
  },
  {
    icon: "shield-check",
    title: "Seguridad y auditoría",
    text: "Información cifrada, permisos por rol y registro de auditoría de cada operación.",
  },
  {
    icon: "palette",
    title: "Con tu marca",
    text: "Las redes y organismos pueden operar la plataforma con su propio dominio, logo y colores.",
  },
];

export const STEPS = [
  { title: "Cuéntanos tu operación", text: "Qué trámites, vehículos y equipos manejas hoy, y dónde está el cuello de botella." },
  { title: "Demostración guiada", text: "Te mostramos los productos con casos como los tuyos." },
  { title: "Propuesta a tu medida", text: "Definimos juntos productos, usuarios y alcance." },
  { title: "Habilitación y capacitación", text: "Activamos tu empresa, configuramos roles y formamos a tu equipo." },
  { title: "Acompañamiento", text: "Soporte cercano y mejoras continuas mientras operas." },
];

export const CONTACT = {
  email: "contacto@flitsas.com",
  web: "https://flitsas.com",
  /** Asunto del correo para pedir una demostración. */
  demoSubject: "Quiero conocer los productos FLIT",
};

export function demoMailto(): string {
  return `mailto:${CONTACT.email}?subject=${encodeURIComponent(CONTACT.demoSubject)}`;
}
