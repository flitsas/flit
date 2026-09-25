import type { ManualArticle } from "../types";

/**
 * HU-H (Fase 3) — sección «Super Admin» (equipo FLIT). Documenta las consolas globales agrupadas
 * por tema; cada artículo cubre varias entradas del dock «Administradores». Fuentes: títulos y
 * subtítulos reales de `app/admin/**`, `components/admin/**`, `RbacAdmin.tsx`, `Auditoria.tsx`,
 * módulos ICT/Log QX y ADR-0059 (procesos periódicos).
 */
export const SUPERADMIN_ARTICLES: ManualArticle[] = [
  {
    slug: "4-superadmin/1-companias-y-organismos",
    title: "Compañías, organismos de tránsito y causales",
    audience: "Super Admin",
    sectionId: "superadmin",
    keywords: [
      "companias",
      "crear compania",
      "administracion de companias",
      "organismos de transito",
      "activar organismo",
      "codigo integrador",
      "divipol",
      "causales de rechazo",
      "concesion",
      "marca blanca",
      "tipo de compania",
    ],
    summary:
      "Alta y ciclo de vida de compañías y organismos, entrada al hub OT y catálogo de causales de rechazo.",
    blocks: [
      {
        id: "companias",
        title: "1. Compañías",
        paragraphs: [
          "Administradores → Compañías. Catálogo de compañías B2B con filtros y estado. «Crear compañía»: razón social, NIT, código único (máx. 32), tipo de compañía (normal, Concesión o Marca Blanca) y, para Marca Blanca, dominio propio. «Activar / Desactivar» cambia el estado con confirmación; una compañía de tipo de sistema no se edita desde aquí.",
          "Abrir una fila entra a la misma ficha que ve el Administrador de la compañía (pestañas de políticas, proveedores, documentos, representantes, mandatarios, usuarios, historial), más lo exclusivo de FLIT: asociar organismos a una Concesión o a sus hijas, vincular clientes a una Concesión, configurar y retirar la marca de una cabeza de red, y registrar o comprobar su dominio.",
        ],
      },
      {
        id: "organismos",
        title: "2. Tránsito → Organismos",
        paragraphs: [
          "Catálogo de organismos de tránsito con código DIVIPOL, código integrador y departamento; filtra por estado, departamento o integrador. «Activar organismo de tránsito» crea su inquilino y lo deja operable; desde la fila entras a su hub (bandeja, reglas, documentos, requisitos, preasignación, usuarios, reportes, mandatos, validar impronta, configuración) tal como lo ve el administrador del OT.",
        ],
        callouts: [
          {
            variant: "info",
            text: "Un organismo sin código integrador cargado no puede radicar en la secretaría por Quipux; el aviso aparece en su fila.",
          },
        ],
      },
      {
        id: "causales",
        title: "3. Tránsito → Causales de rechazo",
        paragraphs: [
          "Catálogo de causales que el organismo marca al rechazar («¿Qué falló?»). Cada causal tiene código, descripción y familia (matrícula inicial o traspaso: no son intercambiables). Se crean, editan y activan o desactivan; una causal inactiva deja de ofrecerse sin borrar el historial.",
        ],
      },
    ],
  },
  {
    slug: "4-superadmin/2-documental-e-improntas",
    title: "Gestión documental e improntas",
    audience: "Super Admin",
    sectionId: "superadmin",
    keywords: [
      "gestion documental",
      "catalogo de documentos",
      "configuracion documental",
      "overrides ot",
      "matriz resuelta",
      "improntas",
      "certificado de improntas",
      "generar impronta",
      "historial de improntas",
    ],
    summary:
      "El catálogo de documentos y su configuración por tipo de trámite, y la generación del Certificado de Improntas Digitales.",
    blocks: [
      {
        id: "documental",
        title: "1. Documental",
        paragraphs: [
          "Administradores → Documental. Administra el catálogo de documentos (nombre, formatos admitidos: PDF, JPG, PNG, WEBP) y, por tipo de trámite, tres vistas: Documentos (lo que exige el catálogo), Overrides OT (lo que cada organismo añade o quita) y Matriz resuelta (lo que efectivamente verá el gestor por organismo). Es la fuente de los requisitos que el asistente muestra en el paso de adjuntos.",
        ],
      },
      {
        id: "improntas",
        title: "2. Improntas",
        paragraphs: [
          "Generación de improntas: emite el Certificado de Improntas Digitales del vehículo (Res. 17145/2023 Mintransporte) y descárgalo. Historial de improntas: consulta las generadas para tu inquilino, filtrables por placa y rango de fechas. La firma digital de cada impronta la verifica el organismo desde su pestaña «Validar impronta».",
        ],
      },
    ],
  },
  {
    slug: "4-superadmin/3-plataforma",
    title: "Plataforma: tipos de trámite, mandatos, FUR, notificaciones, Confirmación RUNT y banners",
    audience: "Super Admin",
    sectionId: "superadmin",
    keywords: [
      "plataforma",
      "tipos de tramite",
      "capacidades",
      "recorrido",
      "familia",
      "mandatos plantilla",
      "contrato de mandato",
      "fur simulador",
      "notificaciones",
      "plantillas de correo",
      "confirmacion runt",
      "banners",
      "carrusel",
    ],
    summary: "Parametrización global que define qué trámites existen, cómo se recorren y cómo se comunica la plataforma.",
    blocks: [
      {
        id: "tipos",
        title: "1. Tipos de trámite",
        paragraphs: [
          "Catálogo de trámites y su parametrización: qué recorrido sigue cada uno, qué exige y qué documentos pide. Cada tipo tiene familia (MATRÍCULAS, TRASPASO, OTROS), código, nombre, el copy que ve el gestor al elegirlo y un interruptor operable / no operable (no operable = no aparece en el selector).",
        ],
        bullets: [
          "Capacidades: captura de actores (y si la parte compradora admite varias personas), consulta del vehículo, datos comerciales, identidad («las partes validan identidad antes de radicar»), checklist de documentos, firma y FUR («el expediente se firma antes de radicarse»), decisión de prenda (puerta que bloquea o no), generación de impronta.",
          "Recorrido: el orden de pasos del asistente para ese tipo.",
          "Documentos: qué exige el tipo; el detalle vive en Documental.",
        ],
      },
      {
        id: "mandatos",
        title: "2. Mandatos",
        paragraphs: [
          "Plantillas de Contrato Privado de Mandato por organismo. «Configurar mandato» fija la redacción que aplica el OT (plantilla, mandatario institucional / unión temporal, NIT, ciudad de cámara, sigla). «Configurar mandatario» define por compañía el tipo de mandato (Persona o RL · Institucional · Abierto) y el mandatario por defecto. Incluye simulador del contrato.",
        ],
      },
      {
        id: "fur-notif",
        title: "3. FUR y Notificaciones",
        paragraphs: [
          "FUR: simula cómo se construye el Formulario Único de Registro con datos sintéticos, para validar plantillas sin un trámite real.",
          "Notificaciones: banco de pruebas de plantillas de correo por canal y compañía (lectura). Vista previa con el tema de la red (marca blanca) y envío de prueba a un buzón; permite ver cómo queda cada correo de Seguridad, Trámites y Analítica antes de que lo reciba un cliente.",
        ],
      },
      {
        id: "runt-banners",
        title: "4. Confirmación RUNT y Banners",
        paragraphs: [
          "Confirmación RUNT: consulta periódica al RUNT para confirmar que los trámites aprobados en FLIT quedaron registrados. Pestaña Configuración (global, permiso de administrar) y pestaña Historial (corridas e intentos por trámite: qué se consultó, qué respondió el RUNT y por qué se marcó SÍ o NO).",
          "Banners: configura y programa el contenido del carrusel informativo que ven los usuarios, sin intervención técnica.",
        ],
      },
    ],
  },
  {
    slug: "4-superadmin/4-integraciones-y-procesos",
    title: "Integraciones (Quipux, ICT, Log QX), procesos periódicos y migración",
    audience: "Super Admin",
    sectionId: "superadmin",
    keywords: [
      "quipux",
      "integracion quipux",
      "log qx",
      "ict",
      "log ict",
      "trazabilidad ict",
      "reportes ict",
      "procesos periodicos",
      "jobs",
      "cadencia",
      "migracion v1",
      "sistema anterior",
    ],
    summary: "Cómo se observan y se configuran las integraciones con secretarías y terceros, los procesos automáticos y la migración desde FLIT 1.",
    blocks: [
      {
        id: "quipux",
        title: "1. Quipux y Log QX",
        paragraphs: [
          "Integración Quipux: interruptor, direcciones, claves y cadencia con que FLIT radica en las secretarías. Log QX (píldora Integraciones): trámites con integración Quipux; filtra por fecha, placa, documento o estado y abre la trazabilidad completa del que necesites. Requiere el permiso de lectura de Log QX.",
        ],
      },
      {
        id: "ict",
        title: "2. ICT (Integración con Terceros)",
        paragraphs: [],
        bullets: [
          "Log ICT: peticiones HTTP de la integración, ya enmascaradas.",
          "Trazabilidad ICT: qué pasó con cada trámite que entró por la integración; busca por número, placa o VIN y abre su recorrido completo.",
          "Reportes ICT: novedades, atascados y entregas, con consultas propias y envío programado; rendimiento por job.",
        ],
      },
      {
        id: "jobs",
        title: "3. Procesos periódicos",
        paragraphs: [
          "Catálogo unificado de los procesos automáticos de la plataforma: estado de cada uno y su cadencia, configurada una sola vez por módulo. Cadencia ICT: ventana horaria (Bogotá), intervalos, lotes y concurrencia; los cambios aplican en el siguiente ciclo, sin reinicio.",
        ],
      },
      {
        id: "migracion",
        title: "4. Migración V1 → V2",
        paragraphs: [
          "Trae trámites del sistema anterior. Reintentar es seguro: un trámite ya migrado no se duplica.",
        ],
      },
    ],
  },
  {
    slug: "4-superadmin/5-rbac-y-auditoria",
    title: "RBAC, usuarios y auditoría",
    audience: "Super Admin",
    sectionId: "superadmin",
    keywords: [
      "rbac",
      "roles",
      "permisos",
      "modulos",
      "roles de compania",
      "roles de organismo",
      "auditoria",
      "rastro",
      "seguridad",
      "usuarios flit",
      "perfil flit",
    ],
    summary: "Cómo se definen módulos, permisos y roles, quién los recibe y dónde queda el rastro.",
    blocks: [
      {
        id: "rbac",
        title: "1. RBAC Admin",
        paragraphs: [
          "Gestiona módulos, permisos y roles del sistema. Los módulos son las entradas navegables (con sus acciones); los permisos son slugs (por ejemplo tramites.create, logqx.read); los roles agrupan permisos y se separan en Roles de Compañía y Roles de Organismo de Tránsito. El dock de cada usuario se filtra por los módulos accesibles de su rol; la API vuelve a validar cada permiso.",
        ],
        callouts: [
          {
            variant: "warning",
            text: "Quitar un permiso a un rol afecta de inmediato a todos sus usuarios. El Super Admin no pasa por RBAC: tiene bypass total.",
          },
        ],
      },
      {
        id: "usuarios",
        title: "2. Usuarios",
        paragraphs: [
          "El módulo Usuarios para el Super Admin es global: eliges el destino de la invitación (compañía, organismo o perfil FLIT interno) y los roles que aplican a ese destino. Un perfil FLIT no pertenece a una compañía ni a un organismo y admite un solo rol.",
        ],
      },
      {
        id: "auditoria",
        title: "3. Auditoría",
        paragraphs: [
          "Rastro global de operaciones administrativas y de seguridad: usuarios, roles, permisos y autenticación, además del módulo Trámites (creación, aceptación de términos, accesos de red). Filtra por módulo, actor, entidad y fecha. Es de solo lectura y no se puede alterar (append-only).",
        ],
      },
    ],
  },
  {
    slug: "4-superadmin/6-dr-flit-global",
    title: "DR. FLIT para el equipo FLIT",
    audience: "Super Admin",
    sectionId: "superadmin",
    keywords: ["dr flit superadmin", "buscar en todas las companias", "soporte interno", "asistente global"],
    summary: "Qué cambia en el asistente cuando quien pregunta es Super Admin.",
    blocks: [
      {
        id: "alcance",
        title: "1. Búsqueda global",
        paragraphs: [
          "Como Super Admin, DR. FLIT busca en todas las compañías: por placa, VIN, radicado o cliente. Cada tarjeta muestra la compañía dueña del trámite para que sepas dónde estás mirando. El historial por placa también es global.",
        ],
      },
      {
        id: "ayuda",
        title: "2. Ayuda",
        paragraphs: [
          "«Necesito ayuda» te ofrece toda la documentación (Gestor, Organismo, Administración de compañía y Super Admin), así que sirve para responder por un cliente: escribe la duda tal como te la plantearon y abre el artículo que le corresponde a su perfil.",
        ],
      },
    ],
  },
];
