import type { ManualArticle } from "../types";

/**
 * Sección «Normativa» — la norma que avala la operación virtual de FLIT ante los organismos de
 * tránsito. Es la FUENTE PRINCIPAL del Centro de Ayuda y de DR. FLIT: el PDF oficial vive en
 * `frontend/public/legal/` (servido en `/legal/...`) y aquí se resume lo que aplica a la plataforma,
 * citando el artículo de la Resolución 20223040045295 de 2022 tal como quedó tras la modificación.
 *
 * PREMISA (vigente para toda evolución de DR-FLIT — ver `docs/plan-tecnico-dr-flit-v3.md` §6):
 * esta resolución es la BASE DEL CRITERIO con el que el asistente responde, no un apartado más del
 * manual. El chip «Ayuda → Normativa» es solo la puerta de entrada explícita: la norma debe sostener
 * búsquedas, estados, requisitos, ayuda contextual y mensajes de error aunque el usuario nunca abra
 * esta sección. Regla práctica: primero la respuesta útil en lenguaje del usuario, después el
 * sustento legal como refuerzo (`sources`); la norma acompaña al how-to, no lo desplaza
 * (garantizado por `lib/manual/__tests__/normativa.test.ts`).
 *
 * Todo lo que dice este artículo sale del texto de la resolución (57 páginas, publicación de la
 * Secretaría Jurídica Distrital de Bogotá). Si la norma cambia, cambia el PDF y este resumen.
 */
export const NORMATIVA_RESOLUCION_SLUG = "5-normativa/1-resolucion-20233040017145-2023";
export const NORMATIVA_RESOLUCION_PDF_HREF =
  "/legal/resolucion-20233040017145-2023-mintransporte.pdf";
export const NORMATIVA_RESOLUCION_WEB_HREF =
  "https://www.alcaldiabogota.gov.co/sisjur/normas/Norma1.jsp?i=155039";

export const NORMATIVA_ARTICLES: ManualArticle[] = [
  {
    slug: NORMATIVA_RESOLUCION_SLUG,
    title: "Resolución 20233040017145 de 2023 (Ministerio de Transporte)",
    audience: "Todos",
    sectionId: "normativa",
    primarySource: true,
    keywords: [
      "resolucion 20233040017145",
      "resolucion 17145",
      "17145",
      "resolucion 2023",
      "ministerio de transporte",
      "mintransporte",
      "normativa",
      "norma",
      "normatividad",
      "marco legal",
      "legal",
      "ley",
      "decreto",
      "que dice la norma",
      "que dice la resolucion",
      "sustento legal",
      "avala",
      "tramites virtuales",
      "ventanilla virtual",
      "virtualidad",
      "runt",
      "autenticacion digital",
      "carpeta digital",
      "improntas",
      "codigo qr",
      "preasignacion de placa runt",
      "60 dias",
      "requisitos matricula",
      "requisitos traspaso",
      "especie venal",
      "errores de digitacion",
      "reporte al interesado",
      "diario oficial",
    ],
    summary:
      "La norma que habilita los trámites virtuales ante los organismos de tránsito y fija sus requisitos; es la fuente principal que respalda a FLIT.",
    sources: [
      {
        title: "Resolución 20233040017145 de 2023 — texto completo (PDF, 57 páginas)",
        href: NORMATIVA_RESOLUCION_PDF_HREF,
        kind: "pdf",
        ref: "Diario Oficial 52386 · 05/05/2023",
      },
      {
        title: "Publicación en el Régimen Legal de Bogotá (Secretaría Jurídica Distrital)",
        href: NORMATIVA_RESOLUCION_WEB_HREF,
        kind: "web",
      },
    ],
    blocks: [
      {
        id: "ficha",
        title: "1. Qué es y por qué respalda a FLIT",
        paragraphs: [
          "Resolución 20233040017145 del 28 de abril de 2023 del Ministerio de Transporte, «por la cual se modifica la Resolución 20223040045295 de 2022 y se dictan disposiciones para la correcta y amplia implementación de la política de Simplificación y racionalización de Trámites». Publicada en el Diario Oficial 52386 del 5 de mayo de 2023; rige desde su publicación (art. 35).",
          "Su núcleo para FLIT está en el Título 5 (Registro Nacional Automotor): los organismos de tránsito pueden adelantar sus trámites «en modalidad presencial o virtual, ésta última por medio de las plataformas tecnológicas con que cuentan», garantizando radicación por plataforma, liquidación y pago por pasarelas, entrega de documentos digitales, trazabilidad de etapas y «mecanismos de seguridad y confianza digital» (art. 5.1.1, parágrafo 1). Y ningún organismo puede exigir requisitos distintos a los del capítulo (art. 5.1.1).",
        ],
        callouts: [
          {
            variant: "info",
            title: "Fuente principal",
            text: "Ante cualquier duda sobre qué exige un trámite o qué puede pedir un organismo, esta resolución es la referencia. El texto completo está enlazado al final de este artículo y desde DR. FLIT → Ayuda → Normativa.",
          },
        ],
      },
      {
        id: "identidad",
        title: "2. Inscripción y autenticación de identidad (RUNT)",
        paragraphs: [
          "Toda persona natural o jurídica debe estar inscrita en el RUNT para tramitar (art. 5.1.2). La validación y autenticación de identidad «podrá ser realizada de manera virtual, mediante el sistema de autenticación digital dispuesto por el RUNT» conforme a la Registraduría y al Ministerio (art. 5.1.2 par. 2 y 5.1.3 par.). En trámite virtual el organismo «deberá garantizar que el usuario realice la autenticación de identidad a través del sistema RUNT» (art. 5.1.3).",
          "Quien no pueda comparecer, los representantes legales y quienes residan fuera del país pueden inscribirse a través de un tercero «mediante contrato de mandato» (art. 5.1.2 par. 1). Los organismos que implementen trámites virtuales con identificación RUNT «deberán hacerlo a través de ventanillas virtuales» y el pago puede hacerse por consignación o PSE (art. 5.1.16).",
        ],
        bullets: [
          "En FLIT: validación biométrica de identidad de cada parte antes de firmar (módulo Identidad), contrato de mandato con mandatario configurado (Mandatos) y radicación por plataforma hacia el organismo.",
        ],
      },
      {
        id: "virtual",
        title: "3. Reglas de la virtualidad",
        paragraphs: [],
        bullets: [
          "Reporte al interesado (art. 5.1.10): todo trámite se reporta «mediante correo electrónico o mensaje de datos» al contacto registrado en el RUNT; a personas jurídicas, a la dirección del RUES. En FLIT: correos por cambio de estado y por hito de revocatoria.",
          "Carpeta digital (art. 5.1.14): en trámite virtual el usuario diligencia digitalmente y anexa los documentos «en el formato que lo requiera la plataforma», que deben archivarse en la carpeta digital del usuario. En FLIT: expediente por trámite con adjuntos y consolidado.",
          "Errores de digitación (art. 5.1.12): se corrigen en el RUNT «sin que para ello deba mediar acto administrativo de revocatoria», siempre que no afecten la especie venal ni existan trámites posteriores.",
          "Especies venales (art. 5.1.13): en trámite virtual el usuario decide si recoge la especie venal en el organismo o pide envío a domicilio a su cargo.",
          "Certificados de tradición digitales y masivos (art. 5.1.11): los organismos deben poder expedirlos por medios electrónicos.",
        ],
      },
      {
        id: "matricula",
        title: "4. Matrícula inicial (art. 5.3.1.1)",
        paragraphs: [
          "Documentos: Formato de Solicitud de Trámite, factura electrónica de venta, certificado individual de aduana y/o declaración de importación según el caso, y «la imagen del código QR; o certificación expedida por el fabricante, ensamblador o importador… o improntas, según corresponda». En trámite virtual el formato se diligencia digitalmente y los documentos se anexan en la plataforma del organismo.",
          "Preasignación de placa: «Confrontada y validada la información, el sistema RUNT procede a preasignar una placa» de manera automática; luego el usuario paga el impuesto y adquiere el SOAT. «Si en el término de sesenta (60) días contados a partir de la fecha de la preasignación no se ha culminado el proceso de matrícula, el sistema RUNT libera la placa». No procede para remolques y semirremolques.",
          "Validaciones del organismo: pago de impuestos con la entidad competente, SOAT vigente en el RUNT, paz y salvo por multas (SIMIT), certificado de emisiones cuando aplique y pago de derechos del trámite (numerales 3 a 5).",
        ],
        bullets: [
          "En FLIT: la ruta de placa (Preasignación → Asignado → «Enviar al OT» con SOAT e impuestos → Entregado) implementa este procedimiento; consulta «Matrícula inicial: ruta de placa».",
        ],
      },
      {
        id: "traspaso",
        title: "5. Traspaso de propiedad (art. 5.3.2.1 y 5.3.2.14)",
        paragraphs: [
          "Con vendedor y comprador inscritos en el RUNT: Formato de Solicitud de Trámite con el contrato de compraventa (o documento en que conste la transferencia) más QR, certificación del fabricante/ensamblador/importador o improntas; confrontación con el RUNT y con la licencia de tránsito; verificación de medidas judiciales o gravámenes (con levantamiento o autorización del acreedor si los hay); SOAT, revisión técnico-mecánica y paz y salvo SIMIT vigentes; pago de retención en la fuente, impuesto del vehículo y derechos del trámite.",
          "Validados los requisitos, el organismo registra al nuevo propietario en el RUNT y «expide la nueva licencia de tránsito» (art. 5.3.2.14). Si el propietario es un establecimiento bancario o compañía de financiamiento, solo se valida el paz y salvo del locatario.",
        ],
        bullets: [
          "En FLIT: actores del trámite, consultas RUNT/SIMIT, prenda (inscripción o levantamiento, sección 13), documento de transferencia y contrato de mandato; el organismo aprueba y adjunta la Licencia de Tránsito desde la bandeja.",
        ],
      },
      {
        id: "improntas",
        title: "6. Improntas y código QR",
        paragraphs: [
          "En todos los trámites del Título 5 la identificación del vehículo se acredita con la imagen del código QR, la certificación del fabricante, ensamblador o importador, o las improntas de motor, serie, chasis o VIN. «Cuando el trámite se adelante de manera virtual, se podrá aceptar la imagen de las improntas, adhesivos, fotoimprontas o del código QR». Para vehículos con QR, la imagen se acepta «en lugar de la impronta».",
        ],
        bullets: [
          "En FLIT: el Certificado de Improntas Digitales que emite el Super Admin y la firma digital que verifica el organismo en «Validar impronta».",
        ],
      },
      {
        id: "otros",
        title: "7. Otros trámites que la resolución regula",
        paragraphs: [
          "Licencias de conducción (sección 2 del capítulo 2), traslado de matrícula (5.3.4), cancelación de matrícula (sección 5), cambio de características y conversión a gas (secciones 6 y 7), regrabación de guarismos, cambio de servicio, inscripción o levantamiento de limitación o gravamen a la propiedad (sección 13) y cambio de locatario en leasing (art. 5.1.15). Los tipos de trámite parametrizados en FLIT («Otros») siguen los requisitos de estas secciones.",
        ],
        callouts: [
          {
            variant: "warning",
            text: "Este artículo resume; el texto vinculante es el PDF enlazado abajo. Ante diferencias entre una pantalla de FLIT y la norma, prevalece la norma: repórtalo a soporte para corregir la plataforma.",
          },
        ],
      },
    ],
  },
];
