import type { ManualArticle } from "../types";
import { COPY } from "@/lib/copy/copy-catalog";

export const INTRO_ARTICLES: ManualArticle[] = [
  {
    slug: "0-introduccion/1-bienvenida",
    title: "Centro de Ayuda FLIT",
    audience: "Todos",
    sectionId: "introduccion",
    keywords: ["bienvenida", "manual", "ayuda", "documentacion", "inicio", "centro", "flit"],
    summary: `Portal de documentación operativa de FLIT para ${COPY.A06}, ${COPY.A05}, Administración de compañía y Super Admin.`,
    blocks: [
      {
        id: "para-que",
        title: "1. ¿Para qué sirve este manual?",
        paragraphs: [
          "Bienvenido al ecosistema de FLIT. Este portal está diseñado para que resuelvas dudas sobre el uso de la plataforma en segundos, sin depender de una llamada a soporte en cada paso.",
          `Documenta las acciones del ${COPY.A06} (empresa cliente que radica trámites vehiculares), del Gestor de ${COPY.A05} (OT), del Administrador de compañía (consola, red de clientes, marca) y del equipo FLIT (Super Admin: compañías, plataforma, integraciones, RBAC). Cada artículo indica «Aplica para» y DR. FLIT solo sugiere los de tu perfil.`,
        ],
        media: [
          {
            kind: "image",
            src: "/manual/screenshots/login.png",
            alt: "Pantalla de inicio de sesión de FLIT 2.0 con los campos Usuario Corporativo y Contraseña",
            caption: "Todo empieza aquí: inicia sesión con tu usuario corporativo.",
          },
        ],
      },
      {
        id: "navegar",
        title: "2. Pasos rápidos para navegar",
        paragraphs: [],
        bullets: [
          `Utiliza el menú lateral izquierdo para explorar Introducción, Normativa (la resolución que respalda a FLIT), ${COPY.A06}, ${COPY.A05}, Administración de compañía y Super Admin.`,
          "Si buscas algo específico (ej. «cómo crear un trámite» o «preasignación de placas»), usa la barra de Búsqueda en la parte superior (⌘ K / Ctrl K).",
          "Si el artículo es largo, usa la tabla de contenidos «En este artículo» a la derecha para saltar a la sección que necesitas.",
        ],
      },
      {
        id: "faq",
        title: "3. Lo que debes saber (FAQ)",
        paragraphs: [],
        bullets: [
          "¿Por qué veo manuales de cosas a las que no tengo acceso? El manual es público para fomentar transparencia. En la aplicación real, el sistema restringe el acceso según tu rol. Por eso, al inicio de cada artículo verás la etiqueta «Aplica para».",
          "¿Encontré un error en el manual? FLIT se actualiza constantemente. Si una pantalla se ve distinta a lo descrito, notifícalo a soporte@flitsas.com para que el equipo actualice el documento.",
          "¿Puede ayudarme DR. FLIT? Sí. En el chat elige «Necesito ayuda»: si estás en un módulo con documentación te la sugiere de entrada; si describes tu duda, te ofrece los artículos que aplican a tu perfil. En Gestión busca trámites por placa, VIN, radicado o cliente.",
        ],
      },
    ],
  },
  {
    slug: "0-introduccion/2-como-navegar",
    title: "Cómo navegar el manual",
    audience: "Todos",
    sectionId: "introduccion",
    keywords: ["navegar", "buscar", "sidebar", "menu", "toc", "busqueda", "atalhos"],
    summary: "Menú lateral, búsqueda inteligente y tabla de contenidos.",
    blocks: [
      {
        id: "sidebar",
        title: "1. Menú lateral",
        paragraphs: [
          `Las secciones agrupan artículos por audiencia. Introducción aplica a todos; ${COPY.A06} documenta al operador de compañía; ${COPY.A05} documenta al perfil OT (rol ot_admin).`,
          "Puedes colapsar cada sección haciendo clic en su título. El artículo activo se resalta con fondo turquesa suave.",
        ],
      },
      {
        id: "search",
        title: "2. Búsqueda",
        paragraphs: [
          "La barra superior indexa títulos, palabras clave y el cuerpo de los artículos. Escribe al menos dos caracteres para ver sugerencias.",
          "Atajo de teclado: ⌘ K (macOS) o Ctrl K (Windows). Esc cierra el panel de resultados.",
        ],
        callouts: [
          {
            variant: "tip",
            title: "Consejo",
            text: "Usa verbos concretos: «crear trámite», «documentos matrícula», «preasignación placas».",
          },
        ],
      },
      {
        id: "toc",
        title: "3. Tabla de contenidos",
        paragraphs: [
          "En pantallas anchas, la columna derecha lista las secciones del artículo actual. Cada enlace hace scroll suave al encabezado correspondiente.",
        ],
      },
    ],
  },
  {
    slug: "0-introduccion/3-perfiles-y-roles",
    title: `Perfiles: ${COPY.A06} vs ${COPY.A05}`,
    audience: "Todos",
    sectionId: "introduccion",
    keywords: ["perfil", "gestor", "ot", "radicador", "ot_admin", "roles", "diferencia"],
    summary: "Qué perfil usa cada tipo de usuario y qué módulos ve en el dock.",
    blocks: [
      {
        id: "gestor",
        title: `1. Perfil ${COPY.A06} (empresa cliente)`,
        paragraphs: [
          "Corresponde a usuarios de una compañía que radica trámites vehiculares (concesionario, renting, gestoría). En el dock típico de un operador (rol Radicador) verás Inicio, Trámites y Ayuda.",
          "Módulos adicionales (Identidad, Reportes, Usuarios) solo aparecen si tu rol custom tiene los permisos RBAC correspondientes.",
        ],
        bullets: [
          "Ruta principal de trámites: /tramites",
          "Wizard server-driven: el backend define pasos, bloqueos y cuándo puedes radicar.",
          `No tiene acceso al hub del ${COPY.A05} ni a consolas /admin de plataforma.`,
        ],
      },
      {
        id: "ot",
        title: `2. Perfil ${COPY.A05}`,
        paragraphs: [
          "Usuarios con rol ot_admin administran la operación de su organismo: bandeja de trámites de compañías, preasignación, reportes, usuarios OT y parametrización (Reglas, Documentos, Requisitos).",
          "El menú del OT vive en el dock (no duplica módulos SPA homónimos). Las rutas del hub siguen el patrón /admin/transit-offices/{id}/…",
        ],
      },
      {
        id: "administracion",
        title: "3. Perfiles de administración",
        paragraphs: [
          "Además de Gestor y Organismo hay dos perfiles de administración con secciones propias en este manual, visibles según tu rol:",
        ],
        bullets: [
          "Admin de Compañía: consola «Administración» de la compañía, usuarios, revocatorias y, si la compañía es cabeza de red (concesión o marca blanca), la red de clientes y la marca.",
          "Super Admin (equipo FLIT): compañías, organismos, documental de plataforma, improntas, integraciones, RBAC, auditoría y procesos periódicos.",
        ],
        callouts: [
          {
            variant: "info",
            text: "El menú es UX: ocultar un ítem no sustituye la validación de permisos en la API.",
          },
        ],
      },
    ],
  },
  {
    slug: "0-introduccion/4-fechas-y-horas",
    title: "Fechas, horas y formatos",
    audience: "Todos",
    sectionId: "introduccion",
    keywords: [
      "fecha",
      "hora",
      "formato de fecha",
      "zona horaria",
      "hora de colombia",
      "dd/mm/yyyy",
      "vigencia",
      "exportar",
    ],
    summary: "Cómo se muestran fechas y horas en FLIT y por qué una vigencia no lleva hora.",
    blocks: [
      {
        id: "instantes",
        title: "1. Instantes: DD/MM/YYYY HH:mm en hora de Colombia",
        paragraphs: [
          "Todo lo que representa un momento (radicación, cambio de estado, asignación de placa, firma, línea de tiempo, correos) se muestra como DD/MM/YYYY HH:mm en hora de Colombia, sin importar la zona horaria del navegador. Sin segundos: no aportan a la lectura operativa.",
        ],
      },
      {
        id: "calendario",
        title: "2. Fechas de calendario: sin hora",
        paragraphs: [
          "Las vigencias (SOAT, RTM, identidad, escrituras, baúl de firmas) son fechas de calendario: se muestran como DD/MM/YYYY y no se convierten de zona horaria. Por eso una vigencia nunca aparece «un día antes».",
        ],
      },
      {
        id: "exportables",
        title: "3. Exportables y documentos",
        paragraphs: [
          "Los archivos Excel y los documentos generados usan el mismo formato estándar que la interfaz. Los certificados del consolidado conservan la hora; el sello de tiempo de la impronta conserva los segundos por exigencia normativa.",
        ],
      },
    ],
  },
  {
    slug: "0-introduccion/5-cuenta-y-acceso",
    title: "Tu cuenta: acceso y contraseña",
    audience: "Todos",
    sectionId: "introduccion",
    keywords: [
      "acceso",
      "iniciar sesion",
      "login",
      "usuario corporativo",
      "contraseña",
      "olvidó su contraseña",
      "recuperar contraseña",
      "restablecer contraseña",
      "activar invitación",
      "activar cuenta",
      "cambiar contraseña",
      "cerrar sesión",
      "sesión expirada",
      "acceso denegado",
      "403",
      "cuenta bloqueada",
      "cuenta suspendida",
    ],
    summary: "Cómo iniciar sesión, recuperar tu contraseña, activar una invitación y qué hacer ante avisos de sesión expirada o acceso denegado.",
    blocks: [
      {
        id: "iniciar-sesion",
        title: "1. Iniciar sesión",
        paragraphs: [
          "Para entrar a FLIT necesitas dos datos: tu Usuario Corporativo (el correo con el que te crearon la cuenta) y tu Contraseña. Escríbelos en la pantalla de inicio y pulsa «Iniciar Sesión».",
          "Si el usuario o la contraseña no coinciden, el sistema te avisa con el mensaje «Correo o contraseña incorrectos». Revisa que no tengas el bloqueo de mayúsculas activado y vuelve a intentar.",
        ],
        media: [
          {
            kind: "image",
            src: "/manual/screenshots/login.png",
            alt: "Pantalla de inicio de sesión de FLIT 2.0 con los campos Usuario Corporativo y Contraseña",
            caption: "La pantalla de inicio: usuario corporativo, contraseña y el enlace de recuperación.",
          },
        ],
      },
      {
        id: "olvido-contrasena",
        title: "2. ¿Olvidó su contraseña?",
        paragraphs: [
          "Justo debajo del campo de contraseña encontrarás el enlace «¿Olvidó su contraseña?». Haz clic, escribe tu correo y pulsa «Enviar instrucciones».",
          "Por seguridad, siempre verás el mismo mensaje de confirmación (algo como «si el correo está registrado, enviaremos instrucciones de recuperación»), sin importar si el correo existe o no en el sistema. Así nadie puede averiguar qué correos están registrados en FLIT.",
          "Revisa tu bandeja de entrada y, si no llega en unos minutos, revisa la carpeta de correo no deseado (spam). El enlace que recibes te lleva a una pantalla para definir una nueva contraseña. Si el enlace ya venció, simplemente vuelve a pedir uno nuevo desde «¿Olvidó su contraseña?».",
        ],
      },
      {
        id: "activar-invitacion",
        title: "3. Activar una invitación",
        paragraphs: [
          "Cuando el administrador de tu compañía (o el equipo FLIT) crea tu usuario, te llega un correo con un enlace de activación. Ese enlace te lleva a la pantalla «Activa tu cuenta», donde defines tu contraseña por primera vez.",
          "Con eso queda lista tu cuenta y puedes iniciar sesión de inmediato con tu correo y la contraseña que acabas de definir.",
        ],
      },
      {
        id: "cambiar-contrasena",
        title: "4. Cambiar tu contraseña",
        paragraphs: [
          "Ya con la sesión iniciada, puedes cambiar tu contraseña cuando quieras: abre el menú de tu cuenta (tu nombre, arriba a la derecha) y elige «Cambio de contraseña». Escribe tu contraseña actual y la nueva, y confirma.",
        ],
      },
      {
        id: "cerrar-sesion",
        title: "5. Cerrar sesión",
        paragraphs: [
          "Desde el mismo menú de tu cuenta selecciona «Cerrar sesión» para cerrar tu sesión de forma segura, sobre todo si usas un computador compartido.",
        ],
      },
      {
        id: "sesion-expirada-acceso-denegado",
        title: "6. Sesión expirada y acceso denegado",
        paragraphs: [
          "Si dejas la sesión inactiva por mucho tiempo, verás una ventana que dice «Tu sesión expiró. Por seguridad, tu sesión se cerró. Vuelve a iniciar sesión para continuar». Pulsa «Ir a iniciar sesión», escribe tus datos otra vez y FLIT te devuelve a donde estabas.",
          "Si entras a una sección para la que no tienes permiso, verás la pantalla «Acceso restringido»: no significa que algo esté dañado, sino que tu perfil no incluye ese módulo. Pídele acceso al administrador de tu compañía o, si crees que es un error, escríbele al equipo FLIT.",
        ],
        callouts: [
          {
            variant: "info",
            text: "El acceso denegado también aparece marcado como «403» en algunos mensajes técnicos: es el mismo aviso de «no tienes permiso para esto».",
          },
        ],
      },
      {
        id: "casos-frecuentes",
        title: "7. Casos frecuentes",
        paragraphs: [],
        bullets: [
          "No me llega el correo de recuperación o de invitación: revisa la carpeta de spam o correo no deseado antes de pedir uno nuevo.",
          "El enlace de recuperación o de activación ya venció: pide uno nuevo (recuperación desde «¿Olvidó su contraseña?»; invitación, pídele al administrador que te la reenvíe).",
          "Mi cuenta está bloqueada temporalmente: verás el aviso «Tu cuenta está bloqueada temporalmente». Contacta al administrador de tu compañía para que la reactive.",
          "Mi usuario está suspendido: no podrás iniciar sesión aunque la contraseña sea correcta; solo el administrador de tu compañía puede reactivarlo.",
        ],
      },
    ],
  },
  {
    slug: "0-introduccion/6-portal-participantes",
    title: "El portal del comprador y el vendedor",
    audience: "Todos",
    sectionId: "introduccion",
    keywords: [
      "portal",
      "participante",
      "comprador",
      "vendedor",
      "enlace personal",
      "magic link",
      "autorización de datos",
      "ley 1581",
      "tratamiento de datos",
      "carga de documentos",
      "firma",
      "traspaso",
      "validación biométrica",
      "biometria",
      "foto rostro",
      "selfie",
      "cédula",
      "enlace vencido",
      "enlace expirado",
      "foto rechazada",
    ],
    summary: "Qué recibe el comprador o el vendedor en su correo, qué debe hacer en el portal y cómo acompañarlo si algo falla.",
    blocks: [
      {
        id: "el-enlace",
        title: "1. El enlace que llega al participante",
        paragraphs: [
          "Cuando un trámite necesita que el comprador, el vendedor o cualquier otro participante haga algo (autorizar el uso de sus datos, subir un documento o firmar), FLIT le envía un correo con un enlace propio.",
          "Ese enlace es personal e intransferible: la persona no necesita crear usuario ni contraseña, solo abrirlo desde su celular o computador. Si el participante te pregunta si necesita «una cuenta en FLIT», la respuesta es no.",
        ],
      },
      {
        id: "dentro-del-portal",
        title: "2. Qué encuentra dentro del portal",
        paragraphs: [
          "Al abrir el enlace, el participante primero ve la autorización de tratamiento de datos personales (conforme a la Ley 1581 de 2012). Debe leerla, marcar la casilla de aceptación y pulsar «Aceptar y continuar» antes de ver el resto de pasos.",
          "Después de aceptar, el portal le muestra sus pasos pendientes:",
        ],
        bullets: [
          "Documentos: subir los documentos que el gestor solicitó (en PDF, JPG, PNG o WEBP, hasta 20 MB). Los obligatorios llevan una etiqueta que lo indica; una marca de check confirma que ya se subieron.",
          "Firma (solo en traspasos): si el trámite lo requiere, el participante encuentra un enlace para firmar la compraventa. Si aún no le aparece el enlace, es porque el gestor todavía no ha solicitado la firma.",
          "Finalizar: cuando ya completó lo que le corresponde, pulsa «Finalizar mi parte». El portal confirma con un mensaje de «¡Listo!» y desde ahí puede cerrar la página.",
        ],
      },
      {
        id: "validacion-biometrica",
        title: "3. La validación biométrica (otro enlace)",
        paragraphs: [
          "La validación de identidad no ocurre dentro del mismo portal: le llega al participante como un segundo enlace, en otro correo. Ahí le piden 3 fotos: una selfie de su rostro y las dos caras de su cédula (frente y reverso).",
          "Para que la validación salga bien a la primera, coméntale al participante estos consejos:",
        ],
        bullets: [
          "Buscar un lugar con buena luz, de frente y sin sombras sobre la cara.",
          "Quitarse gorra, gafas oscuras o cualquier accesorio que tape el rostro para la selfie.",
          "Tener la cédula física a la mano y fotografiarla completa, sin brillos ni dedos que tapen los datos.",
        ],
        callouts: [
          {
            variant: "tip",
            title: "Consejo para el gestor",
            text: "Si acompañas al participante por teléfono, pídele que primero revise que tenga buena señal y luz antes de tomar las fotos: así evitas que gaste sus intentos disponibles en fotos borrosas.",
          },
        ],
      },
      {
        id: "casos-frecuentes",
        title: "4. Casos frecuentes",
        paragraphs: [],
        bullets: [
          "El enlace del portal o de la biometría ya venció: el participante debe pedirle a la compañía que gestiona el trámite (tu equipo) que le reenvíe un enlace nuevo; el participante no puede generarlo por su cuenta.",
          "Le rechazaron la foto en la validación biométrica: el sistema le indica el motivo y cuántos intentos le quedan. Repite la foto siguiendo los consejos de buena luz y rostro despejado.",
          "El documento que subió se ve ilegible o incompleto: pídele que vuelva a fotografiarlo o escanearlo completo, sin brillos ni recortes, y que lo suba de nuevo con «Reemplazar».",
          "El participante dice que no le ha llegado ningún correo: confírmale al gestor que el correo registrado en el trámite sea el correcto y, si es así, pídele al gestor que reenvíe el enlace desde el trámite.",
        ],
      },
    ],
  },
];
