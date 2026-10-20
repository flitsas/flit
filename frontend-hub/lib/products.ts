// Catálogo de presentación de los productos de la suite: lo que dice el landing, las tarjetas del inicio y la pantalla
// «Próximamente». Primera versión del texto (2026-10-02); el área de diseño y la comercial lo ajustarán. Función pura.
//
// Qué productos ve cada usuario y si ya están desplegados lo decide la API (`GET /me/apps`, `comingSoon`); esto solo
// los describe. Los nombres de ícono son los de platform.products (los traduce `appIcon` de @flit/shell).

export interface ProductInfo {
  code: string;
  name: string;
  icon: string;
  /** Una línea para tarjetas. */
  tagline: string;
  /** Un párrafo para la sección del producto. */
  description: string;
  features: string[];
  /** Para quién es. */
  audiences: string[];
  /**
   * Cómo lo presenta el landing (público, sin sesión). En el inicio con sesión manda la API: `comingSoon` de
   * `GET /me/apps`, que sale de `Suite:Hosts:ComingSoon` por ambiente.
   */
  status: "available" | "soon";
}

export const PRODUCTS: ProductInfo[] = [
  {
    code: "tramites",
    name: "Trámites",
    icon: "file-text",
    tagline: "Matrículas, traspasos y demás trámites vehiculares, de la radicación a la entrega.",
    description:
      "Gestiona cada trámite vehicular ante los organismos de tránsito en un solo flujo: validación de identidad, " +
      "documentos, firmas, radicación y seguimiento, con la información del RUNT a la mano y sin papeles sueltos.",
    features: [
      "Matrícula inicial, traspaso y otros trámites con su proceso completo",
      "Consulta y confirmación automática en el RUNT",
      "Validación biométrica de identidad y firma de mandatos y contratos",
      "Lectura inteligente de documentos y cargue masivo",
      "Bandeja para organismos de tránsito con radicado y trazabilidad",
      "Reportes y consultas de la operación",
    ],
    audiences: ["Concesionarios", "Renting y leasing", "Gestores de trámites", "Organismos de tránsito"],
    status: "available",
  },
  {
    code: "comparendos",
    name: "Comparendos",
    icon: "ticket",
    tagline: "Encuentra, gestiona y defiende los comparendos de tu flota a tiempo.",
    description:
      "Detecta los comparendos de tus vehículos apenas aparecen, controla cada plazo y gestiona su defensa con apoyo " +
      "jurídico, para pagar solo lo que corresponde y en el mejor momento.",
    features: [
      "Monitoreo de comparendos por NIT y por placa",
      "Alertas de plazos para descuentos, descargos y prescripción",
      "Lectura automática de notificaciones",
      "Derechos de petición con apoyo jurídico",
      "Seguimiento de cada caso hasta su cierre",
      "Reportes por vehículo, conductor y cliente",
    ],
    audiences: ["Renting y leasing", "Administradores de flotas", "Empresas de transporte"],
    status: "soon",
  },
  {
    code: "diagnostico",
    name: "Diagnóstico",
    icon: "gauge",
    tagline: "El estado legal y documental de cada vehículo en una sola consulta.",
    description:
      "Consolida en un informe lo que hoy se consulta en varias fuentes: estado en el RUNT, multas, SOAT, revisión " +
      "técnico-mecánica e historial, para un vehículo o para toda tu flota.",
    features: [
      "Consulta consolidada de RUNT, SIMIT, SOAT y revisión técnico-mecánica",
      "Historial del vehículo: propietarios, limitaciones y prendas",
      "Alertas de vencimiento de documentos",
      "Diagnóstico de flota completa por lotes",
      "Informes descargables para compra, retoma o aseguramiento",
    ],
    audiences: ["Concesionarios y compraventas", "Aseguradoras", "Renting y leasing", "Flotas"],
    status: "soon",
  },
];

export function productInfo(code: string): ProductInfo | null {
  return PRODUCTS.find((p) => p.code === code) ?? null;
}
