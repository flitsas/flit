import type { ManualArticle } from "../types";

/**
 * HU-H (Fase 3) — sección «Administración de compañía»: lo que ve y decide el rol AdminCompany.
 * Fuentes: `components/admin/companies/*` (pestañas y copy real), `components/admin/branding/*`,
 * `components/admin/domain/*`, `components/admin/generacion-documental/*`,
 * `docs/guia-mandatarios-contrato-mandato.md`, épicas #12235 (red) y #12237 (marca blanca).
 */
export const ADMIN_COMPANY_ARTICLES: ManualArticle[] = [
  {
    slug: "3-admin-company/1-consola",
    title: "Consola de administración de la compañía",
    audience: "Admin de Compañía",
    sectionId: "admin-company",
    keywords: [
      "administracion",
      "mi empresa",
      "configuracion empresa",
      "politicas",
      "proveedores de consulta",
      "avaluo",
      "lista blanca",
      "organismos habilitados",
      "placas preasignadas",
      "documentos personalizados",
      "historial de cambios",
      "guardar todo",
    ],
    summary:
      "Qué configura cada pestaña de «Administración» y cómo se guardan los cambios de forma segura.",
    blocks: [
      {
        id: "entrar",
        title: "1. Cómo entrar",
        paragraphs: [
          "Píldora «Administración» del dock (solo Administrador de compañía). Abre la ficha de tu compañía con pestañas: Trámites, Configuración Empresa, Documentos, Placas preasignadas, Representantes legales, Mandatarios, Usuarios e Historial de Cambios.",
          "Las pestañas de configuración comparten un solo formulario: «Guardar todo» no guarda directo, abre una ventana que lista exactamente qué cambió y pide confirmar. El resultado se muestra allí mismo.",
        ],
      },
      {
        id: "tramites",
        title: "2. Pestaña Trámites (políticas por familia)",
        paragraphs: ["Decide qué puede radicar tu compañía, por familia de tipo de trámite:"],
        bullets: [
          "MATRÍCULAS (primera matrícula, cancelación y demás): «No permitir trámites de matrículas», «Preasignación de placa activa», «Solo vehículos propios», «Permitir vehículos de categorías misceláneas», «Validar SOAT ante el RUNT al procesar».",
          "TRASPASO: «No permitir trámites de traspaso» (activo = la compañía no puede crear traspasos).",
          "OTROS (blindaje, duplicados, prendas, cambios de color o carrocería…): «No permitir otros trámites».",
        ],
      },
      {
        id: "config",
        title: "3. Pestaña Configuración Empresa",
        paragraphs: [],
        bullets: [
          "Proveedores de consulta (por placa, por VIN, de conductor: Kyverum RUNT, Verifik, Intempo…) con tiempo de failover; proveedores de avalúo habilitados (Fasecolda, Mercado Libre) y el sugerido; fuente de comparendos y métodos de recaudo; módulos activos (Trámites, Comparendos, Resoluciones).",
          "Notificaciones: a quién avisar al aprobar o rechazar (radicador, comprador, vendedor o propietario) y un correo adicional de avisos.",
          "Lista blanca de correos: dominios o correos autorizados para las invitaciones de usuarios.",
          "Organismos de tránsito: habilita los organismos donde radica tu compañía y, por cada uno, sus bloqueos y restricciones. Si tu compañía es una Concesión o una hija de red, esta lista la fija FLIT y aquí es de solo lectura.",
        ],
      },
      {
        id: "otras",
        title: "4. Otras pestañas",
        paragraphs: [],
        bullets: [
          "Documentos: plantillas y documentos personalizados de tu compañía que se generan dentro de los trámites.",
          "Placas preasignadas: consulta de las placas que los organismos te han asignado (solo lectura).",
          "Representantes legales y Mandatarios: ver «Representantes legales y mandatarios».",
          "Usuarios: el mismo módulo Usuarios del dock, embebido en la ficha.",
          "Historial de Cambios: quién cambió qué configuración y cuándo.",
        ],
      },
    ],
  },
  {
    slug: "3-admin-company/2-representantes-mandatarios",
    title: "Representantes legales y mandatarios",
    audience: "Admin de Compañía",
    sectionId: "admin-company",
    keywords: [
      "representante legal",
      "representantes legales",
      "mandatario",
      "mandatarios",
      "firma del baul",
      "baul de firmas",
      "escritura",
      "empresas representadas",
      "firma fisica",
      "validacion de identidad del mandatario",
    ],
    summary:
      "Cómo registrar representantes legales (con escrituras y firma) y mandatarios que firman el contrato de mandato ante cada organismo.",
    blocks: [
      {
        id: "representantes",
        title: "1. Representantes legales",
        paragraphs: [
          "Pestaña «Representantes legales». Cada ficha tiene los datos de la persona, las empresas que representa (con su escritura de constitución o poder asociada) y su firma del baúl. La firma se asocia desde la ficha del representante; ya no hay un baúl suelto.",
        ],
        bullets: [
          "Registra la persona (tipo y número de documento, nombre; correo, dirección y ciudad opcionales).",
          "Asocia empresas representadas y su escritura (PDF): la escritura respalda la representación en los documentos del trámite.",
          "Asocia la firma del baúl para que los documentos la estampen automáticamente.",
        ],
      },
      {
        id: "mandatarios",
        title: "2. Mandatarios",
        paragraphs: [
          "El mandatario recibe el poder del mandante (vendedor o radicador) y gestiona ante el organismo por cuenta de tu compañía. Para aparecer como opción en un trámite debe estar activo, habilitado en el organismo del trámite y aplicar a la empresa que otorga el mandato.",
        ],
        bullets: [
          "Registrar / Editar mandatario: nombre, documento, correo (con correo se le envía la validación de identidad; si ya tiene una vigente, se reutiliza), firma del baúl opcional.",
          "Organismos donde aplica: solo se ofrecen los habilitados para tu compañía. Dentro de cada organismo puedes acotar las empresas para las que firma y marcar «firma de forma física» (el contrato deja la línea para firmar a mano).",
          "Regla que conviene saber: si dentro de un organismo no marcas ninguna empresa, el mandatario firma para TODAS las empresas de ese organismo; al marcar una, queda restringido a esas.",
        ],
        callouts: [
          {
            variant: "warning",
            text: "El formulario bloquea el guardado si la persona queda sin ninguna forma de firmar en alguno de los organismos marcados: captúrale la firma del baúl, registra un correo para la validación de identidad o marca ese organismo como de firma física.",
          },
        ],
      },
      {
        id: "convenio",
        title: "3. Convenio con el organismo",
        paragraphs: [
          "Cuando existe un convenio comercial registrado entre tu compañía y el organismo, el contrato de mandato no lleva recuadro de firma del mandatario: firma solo el mandante. Los datos del mandatario siguen en el cuerpo del contrato.",
        ],
      },
    ],
  },
  {
    slug: "3-admin-company/3-red-de-clientes",
    title: "Red de clientes (Concesión y Marca Blanca)",
    audience: "Admin de Compañía",
    sectionId: "admin-company",
    keywords: [
      "red de clientes",
      "concesion",
      "marca blanca",
      "cabeza de red",
      "cabeza de grupo",
      "clientes hijos",
      "compania hija",
      "alcance",
      "toda la red",
      "mi compania",
      "solo consulta",
      "vincular cliente",
      "crear cliente",
    ],
    summary:
      "Qué puede hacer la cabeza de una red con sus clientes: crearlos, vincularlos, administrarlos y consultar sus trámites y reportes.",
    blocks: [
      {
        id: "que-es",
        title: "1. Qué es una cabeza de red",
        paragraphs: [
          "Una Concesión o una Marca Blanca es una compañía con clientes «hijos». Los hijos operan como cualquier compañía (crean sus usuarios y radican) y heredan los organismos de tránsito de la cabeza. La cabeza ve los trámites y las estadísticas de sus hijos en modo consulta; un hijo no ve nada de la cabeza ni de sus hermanos.",
          "En la Concesión, los organismos y los clientes los asocia FLIT (Super Admin). En la Marca Blanca, la cabeza gestiona sus clientes y además su propia marca y dominio (ver «Marca y dominio de la red»).",
        ],
      },
      {
        id: "panel",
        title: "2. Píldora «Red de clientes»",
        paragraphs: ["Administración → Red de clientes (solo cabezas de red):"],
        bullets: [
          "Crear cliente: razón social, NIT, código y tipo de compañía. Nace vinculado a tu red.",
          "Vincular cliente existente: elige entre los elegibles (una compañía sin padre y que no sea cabeza).",
          "Activar / Desactivar cliente y Desvincular cliente, con confirmación.",
          "Invitar el Administrador de un hijo desde Usuarios, eligiendo el cliente como destino.",
          "Abrir la ficha de un hijo para administrar su configuración (pestañas de la consola, con un aviso de que estás en un cliente de la red).",
        ],
      },
      {
        id: "alcance",
        title: "3. Alcance en Trámites y Reportes",
        paragraphs: [
          "En Trámites y en Reportes verás el selector «Alcance»: Mi compañía, Toda la red o un cliente concreto. La elección se guarda por usuario. Con la red activa, las filas ajenas llevan el distintivo «Red» y la etiqueta «Solo consulta»: puedes abrir el detalle y ver el estado documental, pero no radicar, editar ni descargar documentos. DR. FLIT también busca en el alcance elegido.",
        ],
        callouts: [
          {
            variant: "info",
            text: "El alcance de red es exclusivo del Administrador de la cabeza. Un radicador u operador de la cabeza ve solo su compañía, sin selector.",
          },
        ],
      },
    ],
  },
  {
    slug: "3-admin-company/4-marca-y-dominio",
    title: "Marca y dominio de la red (Marca Blanca)",
    audience: "Admin de Compañía",
    sectionId: "admin-company",
    keywords: [
      "marca blanca",
      "marca",
      "logotipo",
      "colores",
      "identidad de marca",
      "publicar marca",
      "dominio",
      "dominio propio",
      "registro txt",
      "dns",
      "certificado",
      "correo con marca",
    ],
    summary:
      "Cómo una cabeza Marca Blanca configura su identidad visual (logotipo, colores, nombre) y su dominio propio, y qué heredan sus clientes.",
    blocks: [
      {
        id: "configurador",
        title: "1. Configurador de marca",
        paragraphs: [
          "En la ficha de tu compañía (Administración) encuentras el configurador: logotipo de la marca, color principal, color secundario, color de texto y el nombre visible en el acceso y en el correo. La previsualización muestra la pantalla de acceso, la cabecera de la aplicación y una muestra de correo con el tema aplicado.",
        ],
        bullets: [
          "«Guardar borrador» conserva el trabajo sin afectar a nadie.",
          "«Publicar identidad de marca» aplica la marca a tu red: el servidor valida logotipo, paleta, contraste y nombre; solo publica una marca completa.",
          "Tus clientes heredan la marca publicada; el modo oscuro se deriva automáticamente. Si algo falla, la plataforma cae al respaldo FLIT, nunca a una pantalla rota.",
        ],
      },
      {
        id: "dominio",
        title: "2. Dominio de la red",
        paragraphs: [
          "Panel «Dominio de la red»: registra el dominio propio desde el que tu red accede y recibe comunicaciones. Para comprobar la titularidad debes crear en tu DNS el registro TXT que el panel indica (con valores copiables) y pulsar «Comprobar ahora».",
        ],
        bullets: [
          "Estados: Pendiente de comprobación → Verificado → Activo. Un fallo muestra el motivo (TXT no encontrado o con valor distinto).",
          "Con el dominio activo, la pantalla de acceso, la recuperación de contraseña y los enlaces de invitación de tu red usan tu dominio; el certificado se emite y renueva automáticamente.",
          "El correo sale con el nombre de tu marca como remitente visible y el tema de la red en cabecera y pie.",
        ],
        callouts: [
          {
            variant: "warning",
            text: "Cambiar o retirar el dominio afecta el acceso de toda la red. Coordina el cambio con FLIT y hazlo fuera de horario operativo.",
          },
        ],
      },
    ],
  },
  {
    slug: "3-admin-company/5-generacion-documental",
    title: "Generación documental sin trámite",
    audience: "Admin de Compañía",
    sectionId: "admin-company",
    keywords: [
      "generacion documental",
      "certificado rues",
      "rues",
      "transferencia de dominio",
      "documento de transferencia",
      "carga masiva documentos",
      "historial de documentos",
      "leasing",
      "dacion en pago",
    ],
    summary:
      "Emitir Certificados RUES y documentos privados de transferencia de dominio sin abrir un trámite, por unidad o por lote, y consultar lo generado.",
    blocks: [
      {
        id: "que-es",
        title: "1. Qué es",
        paragraphs: [
          "Módulo «Generación documental» (visible para quien tiene el permiso del módulo, no solo por rol). Emite documentos que normalmente nacen dentro de un trámite, pero aquí sin abrirlo, con los datos consultados en línea.",
        ],
      },
      {
        id: "pestanas",
        title: "2. Pestañas",
        paragraphs: [],
        bullets: [
          "Certificado RUES: consulta el NIT y emite el certificado.",
          "Transferencia: consulta la placa en el RUNT y emite el documento privado de transferencia de dominio. Eliges la causal (A traspaso ordinario, B transferencia unilateral de leasing, C entidad financiera a un tercero; dación en pago y contraprestación no monetaria), quién asume el impuesto sobre vehículos, la retención en la fuente y los derechos del trámite, y datos de firma (ciudad, representante legal).",
          "Carga masiva: sube un lote y sigue el avance en vivo (se actualiza cada 4 segundos); descarga los documentos que sí salieron y revisa el detalle de cada fila.",
          "Historial: todo lo generado por tu compañía con tipo, escenario, usuario, fecha y resultado.",
        ],
      },
    ],
  },
];
