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
      "vista restringida",
      "representantes legales",
      "mandatarios",
      "equipo flit",
      "soporte",
      "usuarios de la hija",
      "que ves en administracion",
    ],
    summary:
      "Qué ves hoy en «Administración», qué gestiona directamente el equipo FLIT y cómo pedirle un cambio.",
    blocks: [
      {
        id: "entrar",
        title: "1. Qué ves al entrar",
        paragraphs: [
          "Píldora «Administración» del dock (solo Administrador de compañía). Hoy se abre directo en dos pestañas: Representantes legales y Mandatarios. Es un cambio a propósito: antes veías la ficha completa de tu compañía (Trámites, Configuración Empresa, Documentos, Usuarios, Historial de Cambios); esas secciones ahora las gestiona el equipo FLIT para proteger cambios sensibles.",
          "Si administras una compañía hija desde el panel «Red de clientes» (solo cabezas de Concesión o Marca Blanca) y abres su ficha, ves una tercera pestaña, Usuarios, propia de esa hija: ahí resetas contraseñas y suspendes o desactivas sus usuarios.",
        ],
        callouts: [
          {
            variant: "info",
            text: "No es un permiso que te falte ni un error de la plataforma: la ficha completa quedó reservada al equipo FLIT. Sigue leyendo para saber qué puedes hacer tú y a quién pedirle el resto.",
          },
        ],
      },
      {
        id: "tus-pestanas",
        title: "2. Lo que gestionas tú mismo",
        paragraphs: [],
        bullets: [
          "Representantes legales: registra a quienes representan a tu compañía, sus escrituras y su firma del baúl. Ver «Representantes legales y mandatarios».",
          "Mandatarios: registra quién firma el contrato de mandato ante cada organismo por cuenta de tu compañía. Ver «Representantes legales y mandatarios».",
          "Usuarios de una hija de tu red (solo si la administras desde el panel de red): resetear contraseñas y suspender o desactivar sus usuarios.",
          "Usuarios de tu propia compañía: no vive en esta ficha, es el módulo «Usuarios» del dock, con el mismo alcance de siempre (resetear contraseñas, suspender o desactivar; eliminar sigue siendo del equipo FLIT).",
        ],
      },
      {
        id: "pide-a-flit",
        title: "3. Qué gestiona el equipo FLIT (y cómo pedir un cambio)",
        paragraphs: [
          "Estas áreas ya no las editas desde tu consola. Para cualquier ajuste, escribe al equipo FLIT desde Ayuda → Soporte indicando el NIT de tu compañía y el cambio puntual que necesitas:",
        ],
        bullets: [
          "Trámites: qué familias puede radicar tu compañía (matrículas, traspasos, otros) y sus restricciones («Solo vehículos propios», validar SOAT ante el RUNT, etc.).",
          "Configuración Empresa: proveedores de consulta y de avalúo, a quién avisar al aprobar o rechazar un trámite, la lista blanca de correos para invitaciones, y los organismos de tránsito habilitados para tu compañía.",
          "Documentos: plantillas y documentos personalizados que se generan dentro de los trámites.",
          "Placas preasignadas: consulta de las placas que los organismos te han asignado.",
          "Historial de Cambios: quién cambió qué configuración de tu compañía y cuándo.",
        ],
      },
      {
        id: "otras-piezas",
        title: "4. Otras piezas de tu administración",
        paragraphs: [
          "Estas no viven dentro de esta ficha, pero también son parte de tu administración:",
        ],
        bullets: [
          "Red de clientes: píldora aparte del dock, solo si tu compañía es cabeza de Concesión o Marca Blanca. Ver «Red de clientes».",
          "Marca y dominio de la red: dentro del panel de Red de clientes, solo si tu compañía es cabeza Marca Blanca. Ver «Marca y dominio de la red».",
          "Generación documental: píldora aparte del dock, solo si tu rol tiene habilitado ese módulo. Ver «Generación documental sin trámite».",
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
      "vigencia del mandatario",
      "por vencer",
      "validacion de identidad del mandatario",
      "acordeon por compania",
      "nit",
      "bloque de identidad",
      "modulo identidad",
      "estado de identidad",
    ],
    summary:
      "Cómo registrar representantes legales (con escrituras y firma) y mandatarios que firman el contrato de mandato ante cada organismo.",
    blocks: [
      {
        id: "representantes",
        title: "1. Representantes legales",
        paragraphs: [
          "Pestaña «Representantes legales»: un directorio en acordeón, una fila por representante que despliega sus datos, las compañías que representa agrupadas por NIT (con la escritura asociada a cada una) y su firma del baúl. La firma se asocia desde la propia ficha del representante; ya no hay un baúl suelto por separado.",
          "Cada ficha muestra siempre un bloque de «Validación de identidad» con dos etiquetas fijas —el estado de su Identidad y el de su Firma del baúl— solo para consultar. Ya no hay botones de enviar o reenviar validación desde aquí: si el representante necesita una identidad vigente, el bloque te lleva al módulo Identidad. Si la persona ya tiene una validación aprobada y vigente allí, se asocia sola al guardar.",
        ],
        bullets: [
          "Registra la persona (tipo y número de documento, nombre; correo, dirección y ciudad opcionales).",
          "Dentro de su ficha, asocia las empresas que representa: por cada NIT, adjunta la escritura de constitución o poder (PDF) que respalda esa representación en los documentos del trámite.",
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
          "Registrar / Editar mandatario: primero eliges el modelo. Persona natural: nombre, documento, correo (solo dato de contacto; no dispara ningún envío), forma de firma (Baúl de firmas o Validación de identidad) y vigencia (Fija o Rango de fechas con inicio y fin). Con Baúl de firmas aparece el selector de la firma del baúl. Persona jurídica: nombre y NIT de la entidad, sin firma personal. Formato en blanco: no se piden datos; el sistema solo entrega el PDF sin firma.",
          "Al editar ves el mismo bloque de identidad de solo consulta que en representantes legales (solo en Persona natural): si necesita una validación de identidad, se gestiona desde el módulo Identidad, no desde este formulario. Los mandatarios anteriores aparecen como Persona natural y conservan su firma del baúl.",
          "Organismos donde aplica: solo se ofrecen los habilitados para tu compañía. La firma de forma física ya no se ofrece.",
          "Lista de mandatarios: cada fila muestra el modelo y una etiqueta de vigencia con texto e ícono: verde «Vigente», naranja «Por vencer» (faltan 7 días o menos para el fin), rojo «Vencido», gris «Inactivo» y azul «Aún no vigente» si el rango todavía no empieza. En rango de fechas se indica hasta cuándo. Persona jurídica y Formato en blanco no tienen vigencia y muestran un guion. Un mandatario eliminado no aparece.",
          "Compañías asociadas: si tu compañía tiene compañías hijas, el formulario las lista con su nombre y NIT para que marques a cuáles aplica también el mandatario. Es opcional: sin marcar ninguna, el mandatario aplica solo a tu compañía. Si no tienes compañías hijas no hay lista y el formulario dice «Este mandatario aplica solo a su compañía». Ya no aparecen las empresas de los Representantes Legales.",
        ],
        callouts: [
          {
            variant: "warning",
            text: "Si falta la forma de firma, eliges Baúl de firmas sin escoger una firma, o el rango no tiene fechas o el fin es anterior al inicio, el error aparece junto al campo y no se guarda hasta corregirlo. Si cambias una Persona natural a Persona jurídica o Formato en blanco, el formulario te avisa que se descartan la forma de firma y la vigencia.",
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
      "agregar cliente a la red",
      "activar cliente",
      "desactivar cliente",
      "invitar usuario",
      "administrar cliente",
      "equipo flit",
    ],
    summary:
      "Qué puede hacer la cabeza de una red con sus clientes: agregarlos, activarlos o desactivarlos, invitar usuarios y administrarlos, y consultar sus trámites y reportes.",
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
          "Agregar cliente a la red: razón social, NIT, código y tipo de compañía. Nace vinculado a tu red.",
          "Activar / Desactivar cliente, con confirmación.",
          "Invitar usuario a un cliente: eliges el cliente como destino y el rol (Administrador de compañía, Radicador, Gestor, Documentador, Validador u Operario full).",
          "Administrar un cliente: abre su ficha para gestionar sus Representantes legales y Mandatarios (y sus Usuarios), con un aviso de que estás en un cliente de tu red.",
        ],
        callouts: [
          {
            variant: "info",
            text: "Vincular una compañía ya existente a tu red o desvincularla no está en este panel: eso lo hace el equipo FLIT.",
          },
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
      "solo lectura",
      "comprobar dominio",
      "registrar dominio",
    ],
    summary:
      "Cómo una cabeza Marca Blanca configura su identidad visual (logotipo, colores, nombre) y consulta el estado de su dominio propio, y qué heredan sus clientes.",
    blocks: [
      {
        id: "configurador",
        title: "1. Configurador de marca",
        paragraphs: [
          "Solo si tu compañía es cabeza Marca Blanca: en Administración → Red de clientes encuentras el configurador de marca, debajo del listado de clientes. Ahí defines logotipo, color principal, color secundario, color de texto y el nombre visible en el acceso y en el correo. La previsualización muestra la pantalla de acceso, la cabecera de la aplicación y una muestra de correo con el tema aplicado.",
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
          "En la misma página, debajo del configurador de marca, ves el panel «Dominio de la red»: el estado del dominio propio desde el que tu red accede y recibe comunicaciones, y el botón «Comprobar ahora» para volver a validar el registro TXT en tu DNS. Aquí es de solo lectura: registrar el dominio por primera vez, cambiarlo o retirarlo lo hace el equipo FLIT.",
        ],
        bullets: [
          "Estados: Pendiente de comprobación → Verificado → Activo. Un fallo muestra el motivo (TXT no encontrado o con valor distinto) y, mientras corre el plazo de gracia, el dominio sigue operando.",
          "Con el dominio activo, la pantalla de acceso, la recuperación de contraseña y los enlaces de invitación de tu red usan tu dominio; el certificado se emite y renueva automáticamente.",
          "El correo sale con el nombre de tu marca como remitente visible y el tema de la red en cabecera y pie.",
        ],
        callouts: [
          {
            variant: "info",
            text: "¿Necesitas registrar un dominio nuevo, cambiarlo o retirarlo? Pídeselo al equipo FLIT desde Ayuda → Soporte; tú puedes seguir el estado y comprobarlo, pero el alta y los cambios son suyos.",
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
      "modulo habilitado",
      "permiso del modulo",
    ],
    summary:
      "Emitir Certificados RUES y documentos privados de transferencia de dominio sin abrir un trámite, por unidad o por lote, y consultar lo generado.",
    blocks: [
      {
        id: "que-es",
        title: "1. Qué es",
        paragraphs: [
          "Píldora «Generación documental» del dock, visible solo si tu rol tiene habilitado este módulo (no todo Admin de Compañía lo ve por defecto: si te falta, pídeselo al equipo FLIT). Emite documentos que normalmente nacen dentro de un trámite, pero aquí sin abrirlo, con los datos consultados en línea.",
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
