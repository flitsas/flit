// Datos de muestra (ficticios) y definición de todos los módulos de FLIT 2.0 para la propuesta.
// Fuente del árbol de módulos: docs/arbol-menu-por-rol.md (vista de Super Admin, con acceso a todo).
window.FLIT = (function () {
  "use strict";

  // Estados del embudo de trámites (página 11 del prototipo). Tonos: "" neutro, info, warn, ok, danger.
  var ESTADOS_TRAMITE = {
    borrador: ["Borrador", ""], validacion: ["En validación", "info"], firma: ["Pendiente de firma", "warn"],
    radicado: ["Radicado", "info"], finalizado: ["Finalizado", "ok"], rechazado: ["Rechazado", "danger"]
  };
  var TRAMITES = [
    ["KLM482", "Traspaso", "Andrés Rojas", "María Fernanda Ruiz", "firma", 72, "Hoy, 10:42"],
    ["JNX219", "Traspaso", "Transportes del Valle S.A.S.", "Carlos Mejía", "validacion", 45, "Hoy, 09:15"],
    ["GHT904", "Matrícula inicial", "Concesionario Andino", "Paula Restrepo", "radicado", 88, "Ayer, 17:30"],
    ["BCD771", "Traspaso", "Luis Fernando Gil", "Ana Sofía Pérez", "firma", 70, "Ayer, 16:02"],
    ["QWE335", "Traspaso", "Daniela Ortiz", "Jorge Castaño", "borrador", 12, "Ayer, 11:48"],
    ["RTY608", "Traspaso", "Leasing Medellín S.A.", "Camila Vélez", "finalizado", 100, "3 nov, 15:20"],
    ["ZXC147", "Matrícula inicial", "Concesionario Andino", "Esteban Toro", "validacion", 38, "3 nov, 10:05"],
    ["MNB552", "Traspaso", "Sergio Arango", "Valentina Mesa", "firma", 74, "2 nov, 18:40"],
    ["PLK903", "Traspaso", "Natalia Bedoya", "Julián Henao", "rechazado", 30, "2 nov, 09:12"],
    ["HJK286", "Traspaso", "Ricardo Zapata", "Laura Cano", "radicado", 90, "1 nov, 14:55"],
    ["TGB640", "Traspaso", "Gabriela Ríos", "Mateo Londoño", "firma", 68, "1 nov, 11:30"],
    ["VFR318", "Matrícula inicial", "Concesionario Andino", "Isabel Correa", "finalizado", 100, "31 oct, 16:10"],
    ["WSX725", "Traspaso unilateral", "Leasing Medellín S.A.", "Felipe Montoya", "borrador", 8, "31 oct, 09:02"],
    ["EDC413", "Traspaso", "Juliana Marín", "Tomás Giraldo", "firma", 76, "30 oct, 17:45"],
    ["YHN590", "Traspaso", "Andrés Rojas", "Diana Patiño", "validacion", 52, "30 oct, 12:20"]
  ].map(function (r, i) {
    // Fechas reales relativas a hoy para poder filtrar por rango; organismo, compañía y gestor de muestra.
    var dia = 864e5, ahora = Date.now();
    var act = new Date(ahora - i * 0.55 * dia - (i % 3) * 3.6e6), cre = new Date(act.getTime() - (4 + (i % 5) * 3) * dia);
    return { placa: r[0], tipo: r[1], vende: r[2], compra: r[3], estado: r[4], avance: r[5], fecha: r[6],
      actualizado: act.toISOString(), creado: cre.toISOString(),
      organismo: ["Medellín", "Envigado", "Sabaneta", "Itagüí"][i % 4], compania: ["Concesionario Andino", "Transportes del Valle S.A.S.", "Leasing Medellín S.A."][i % 3],
      gestor: ["Sofía Cardona", "Camilo Ruiz", "Pedro Álvarez"][i % 3], prioritario: i % 4 === 1, firmaPendiente: r[4] === "firma" };
  });

  // Menú inferior: a la izquierda y a la derecha del botón central de Inicio (estándar FLIT).
  var DOCK = {
    izq: [
      { t: "Trámites", il: "tramites", items: [["tramites", "Trámites"], ["historial-placa", "Historial por placa"], ["revocatorias", "Revocatorias"]] },
      { t: "Identidad", il: "identidad", href: "identidad" },
      { t: "Reportes", il: "reportes", items: [["reportes", "Reportes"], ["reportes-detallados", "Reportes detallados"]] }
    ],
    der: [
      { t: "Usuarios", il: "colaboradores", href: "usuarios" },
      { t: "Administradores", il: "configuracion", secciones: [
        ["Compañías y tránsito", [["companias", "Compañías"], ["organismos", "Organismos de tránsito"], ["causales", "Causales de rechazo"]]],
        ["Documentos", [["documental", "Documental"], ["improntas", "Improntas"], ["generacion-documental", "Generación documental"], ["quipux", "Quipux"]]],
        ["Plataforma", [["tipos-tramite", "Tipos de trámite"], ["confirmacion-runt", "Confirmación RUNT"], ["mandatos", "Mandatos"], ["fur", "FUR"], ["notificaciones", "Notificaciones"], ["banners", "Banners"]]],
        ["Seguridad y operación", [["rbac", "Roles y permisos"], ["auditoria", "Auditoría"], ["procesos", "Procesos periódicos"]]]
      ] },
      { t: "Integraciones", il: "integraciones", items: [["log-qx", "Log QX"], ["ict-logs", "Log ICT"], ["ict-trazabilidad", "Trazabilidad ICT"], ["ict-reportes", "Reportes ICT"]] }
    ]
  };
  var PRODUCTOS = [
    ["inicio", "Trámites", "Matrículas, traspasos y trámites vehiculares", "flit-tramites"],
    ["comparendos", "Comparendos", "Los comparendos de tu flota, a tiempo", "flit-comparendos"],
    ["diagnostico", "Diagnóstico", "Estado legal y documental de tus vehículos", "flit-diagnostico"]
  ];

  var E = {
    activo: { activa: ["Activa", "ok"], activo: ["Activo", "ok"], invitado: ["Invitado", "warn"], suspendido: ["Suspendido", "danger"], inactivo: ["Inactivo", ""], borrador: ["Borrador", ""], publicado: ["Publicado", "ok"], programado: ["Programado", "info"] },
    log: { ok: ["Correcto", "ok"], reintento: ["En reintento", "warn"], error: ["Con error", "danger"] }
  };

  // Formularios que abren los botones de acción. Campo: [id, etiqueta, tipo, opciones o ayuda, obligatorio].
  var FORMS = {
    "nuevo-tramite": { t: "Nuevo trámite", il: "tramites", texto: "Elige la modalidad. Solo aparecen las que tu empresa tiene habilitadas.", campos: [["modalidad", "Modalidad", "tarjetas", [["traspaso", "Traspaso", "Entre dos personas o empresas", "traspaso"], ["unilateral", "Traspaso unilateral", "Leasing hacia el locatario", "empresas"], ["matricula", "Matrícula inicial", "Vehículo nuevo de concesionario", "matricula"]], true]], boton: "Continuar", ir: "#traspaso" },
    "invitar-usuario": { t: "Invitar colaborador", il: "colaboradores", ancho: true, texto: "Le llega un correo para crear su contraseña. El acceso se puede retirar cuando quieras.", campos: [["nombre", "Nombres y apellidos", "texto", "", true], ["correo", "Correo", "correo", "nombre@empresa.co", true], ["documento", "Número de documento", "texto", ""], ["compania", "Compañía", "select", ["Concesionario Andino", "Transportes del Valle S.A.S.", "Leasing Medellín S.A."]], ["rol", "Rol", "tarjetas", [["radicador", "Radicador", "Crea y radica trámites", "tramites"], ["admin", "Admin de compañía", "Administra su empresa", "configuracion"], ["consulta", "Consulta", "Solo ve trámites y reportes", "reportes"]], true], ["vence", "Acceso hasta", "fecha", ""]], boton: "Enviar invitación", exito: "Invitación enviada" },
    "crear-compania": { t: "Crear compañía", il: "empresas", ancho: true, campos: [["razon", "Razón social", "texto", "", true], ["nit", "NIT sin dígito de verificación", "texto", "900123456", true], ["tipo", "Tipo", "tarjetas", [["concesionario", "Concesionario", "Vende vehículos nuevos", "matricula"], ["gestor", "Gestor", "Tramita para terceros", "colaboradores"], ["concesion", "Cabeza de red", "Agrupa otras compañías", "empresas"]], true], ["correo", "Correo de contacto", "correo", "", true]], boton: "Crear compañía", exito: "Compañía creada" },
    "crear-organismo": { t: "Agregar organismo de tránsito", il: "organismos", campos: [["nombre", "Nombre", "texto", "Secretaría de Movilidad de", true], ["codigo", "Código RUNT", "texto", "", true], ["modo", "Modo de radicación", "select", ["FLIT", "Quipux"]]], boton: "Agregar organismo", exito: "Organismo agregado" },
    "nueva-causal": { t: "Nueva causal de rechazo", il: "causales", campos: [["nombre", "Causal", "texto", "", true], ["detalle", "Qué debe corregir el gestor", "area", "", true]], boton: "Guardar causal", exito: "Causal guardada" },
    "nuevo-documento": { t: "Nuevo documento", il: "documentos", campos: [["nombre", "Nombre del documento", "texto", "", true], ["tramites", "Se pide en", "select", ["Traspaso", "Matrícula inicial", "Todos los trámites"]], ["formato", "Formatos aceptados", "texto", "PDF o imagen, hasta 5 MB"]], boton: "Guardar documento", exito: "Documento guardado" },
    "nuevo-tipo": { t: "Nuevo tipo de trámite", il: "tipos-tramite", campos: [["nombre", "Nombre", "texto", "", true], ["codigo", "Código", "texto", "", true], ["asistente", "Asistente en línea", "select", ["Habilitado", "Deshabilitado"]]], boton: "Guardar tipo", exito: "Tipo de trámite guardado" },
    "nuevo-mandato": { t: "Nueva plantilla de mandato", il: "mandatos", campos: [["nombre", "Nombre", "texto", "", true], ["tipo", "Aplica a", "select", ["Persona natural", "Persona jurídica"]], ["archivo", "Plantilla", "archivo", "Documento de Word"]], boton: "Guardar plantilla", exito: "Plantilla guardada" },
    "nueva-notificacion": { t: "Nueva notificación", il: "notificaciones", campos: [["evento", "Cuándo se envía", "select", ["Trámite radicado", "Firma pendiente", "Trámite rechazado", "Trámite finalizado"]], ["canal", "Canal", "select", ["Correo", "Mensaje de texto", "Correo y mensaje de texto"]], ["asunto", "Asunto", "texto", "", true]], boton: "Guardar notificación", exito: "Notificación guardada" },
    "nuevo-banner": { t: "Nuevo banner", il: "banners", campos: [["titulo", "Título", "texto", "", true], ["texto", "Texto", "area", "", true], ["desde", "Desde", "fecha", ""], ["hasta", "Hasta", "fecha", ""]], boton: "Programar banner", exito: "Banner programado" },
    "nuevo-rol": { t: "Nuevo rol", il: "roles", campos: [["nombre", "Nombre del rol", "texto", "", true], ["ambito", "Ámbito", "select", ["Compañía", "Organismo de tránsito", "Plataforma"]]], boton: "Crear rol", exito: "Rol creado" },
    "cliente-ict": { t: "Nuevo cliente de integración", il: "integraciones", texto: "La contraseña se muestra una sola vez al crearlo.", campos: [["nombre", "Nombre del cliente", "texto", "", true], ["compania", "Compañía", "select", ["Concesionario Andino", "Transportes del Valle S.A.S."]]], boton: "Crear cliente", exito: "Cliente creado" },
    "programar-reporte": { t: "Programar envío", il: "reportes", campos: [["reporte", "Reporte", "select", ["Resumen general", "Operación", "Organismo de tránsito", "Uso del aplicativo", "Productividad"]], ["frecuencia", "Frecuencia", "select", ["Cada día", "Cada semana", "Cada mes"]], ["correo", "Enviar a", "correo", "", true]], boton: "Programar", exito: "Envío programado" },
    "contrasena": { t: "Cambiar contraseña", il: "contrasena", campos: [["actual", "Contraseña actual", "clave", "", true], ["nueva", "Contraseña nueva", "clave", "Mínimo 12 caracteres", true]], boton: "Cambiar contraseña", exito: "Contraseña cambiada" },
    "rango-placas": { t: "Agregar rango de placas", il: "preasignacion", campos: [["desde", "Desde", "texto", "KLM100", true], ["hasta", "Hasta", "texto", "KLM199", true], ["compania", "Para", "select", ["Concesionario Andino", "Leasing Medellín S.A."]]], boton: "Agregar rango", exito: "Rango agregado" },
    "nueva-regla": { t: "Nueva regla", il: "configuracion", campos: [["nombre", "Regla", "texto", "", true], ["tramite", "Trámite", "select", ["Traspaso", "Matrícula inicial"]], ["accion", "Qué hace", "select", ["Pide un documento", "Bloquea la radicación", "Avisa al gestor"]]], boton: "Guardar regla", exito: "Regla guardada" },
    "representante": { t: "Agregar representante legal", il: "perfil", campos: [["nombre", "Nombres y apellidos", "texto", "", true], ["documento", "Número de documento", "texto", "", true], ["correo", "Correo", "correo", "", true]], boton: "Agregar representante", exito: "Representante agregado" },
    "mandatario": { t: "Agregar mandatario", il: "mandatos", campos: [["nombre", "Nombres y apellidos", "texto", "", true], ["documento", "Número de documento", "texto", "", true], ["vence", "Mandato vigente hasta", "fecha", ""]], boton: "Agregar mandatario", exito: "Mandatario agregado" },
    "requisito": { t: "Nuevo requisito", il: "documentos", campos: [["nombre", "Requisito", "texto", "", true], ["tramite", "Trámite", "select", ["Traspaso", "Matrícula inicial"]]], boton: "Guardar requisito", exito: "Requisito guardado" },
    "documento-generar": { t: "Generar documentos", il: "generacion-documental", campos: [["plantilla", "Plantilla", "select", ["Contrato de compraventa", "Mandato", "FUR"]], ["tramites", "Trámites", "select", ["Los que esperan firma (5)", "Los radicados hoy (2)"]]], boton: "Generar", exito: "Documentos generados" }
  };

  // Columnas: [título, tipo]. Tipos: placa, texto, mono, num, dinero, estado, personas, avance.
  var MODULOS = {
    "historial-placa": { titulo: "Historial por placa", il: "historial-placa", ante: "Trámites", texto: "Todo lo que ha pasado con un vehículo en la plataforma.",
      buscar: "Escribe la placa", columnas: [["Placa", "placa"], ["Trámite", "texto"], ["Partes", "personas"], ["Estado", "estado"], ["Fecha", "texto"]], estados: ESTADOS_TRAMITE,
      filas: [["KLM482", "Traspaso", ["Andrés Rojas", "María Fernanda Ruiz"], "firma", "Hoy, 10:42"], ["KLM482", "Traspaso", ["Concesionario Andino", "Andrés Rojas"], "finalizado", "12 mar 2022"], ["KLM482", "Matrícula inicial", ["Mazda de Colombia", "Concesionario Andino"], "finalizado", "2 mar 2022"]] },
    revocatorias: { titulo: "Revocatorias", il: "revocatorias", ante: "Trámites", texto: "Solicitudes para revertir un trámite ya radicado.", pendiente: "pendiente",
      barra: { pendiente: ["solicitudes esperan respuesta del organismo.", "Enviar recordatorio", "aviso:Recordatorio enviado al organismo"] },
      estados: { pendiente: ["Pendiente", "warn"], aprobada: ["Aprobada", "ok"], negada: ["Negada", "danger"] },
      columnas: [["Placa", "placa"], ["Trámite", "texto"], ["Motivo", "texto"], ["Estado", "estado"], ["Solicitada", "texto"]],
      filas: [["RTY608", "Traspaso", "Error en el comprador", "pendiente", "Hoy"], ["VFR318", "Matrícula inicial", "Factura anulada", "aprobada", "28 oct"], ["HJK286", "Traspaso", "Desistimiento", "pendiente", "27 oct"], ["GHT904", "Matrícula inicial", "Datos del vehículo", "negada", "20 oct"]],
      acciones: [["Solicitar revocatoria", "form:nueva-causal"]] },
    identidad: { titulo: "Identidad", il: "identidad", ante: "Validaciones", texto: "Prevalidación de identidad de vendedores y compradores antes de firmar.", pendiente: "pendiente",
      barra: { pendiente: ["personas no han terminado la validación.", "Reenviar enlace", "aviso:Enlace reenviado a las personas pendientes"] },
      estados: { validada: ["Validada", "ok"], pendiente: ["Pendiente", "warn"], fallida: ["No coincide", "danger"] },
      columnas: [["Persona", "texto"], ["Documento", "mono"], ["Trámite", "placa"], ["Estado", "estado"], ["Actualizado", "texto"]],
      filas: [["María Fernanda Ruiz", "CC 1036548902", "KLM482", "pendiente", "Hoy, 10:40"], ["Andrés Rojas", "CC 71845210", "KLM482", "validada", "Hoy, 09:58"], ["Carlos Mejía", "CC 98765432", "JNX219", "validada", "Hoy, 09:10"], ["Ana Sofía Pérez", "CC 1152003418", "BCD771", "pendiente", "Ayer"], ["Julián Henao", "CC 8023311", "PLK903", "fallida", "2 nov"]],
      acciones: [["Nueva prevalidación", "form:invitar-usuario"]] },
    "reportes-detallados": { titulo: "Reportes detallados", il: "reportes-detallados", ante: "Reportes", texto: "Cada trámite con todos sus datos, para filtrar y descargar.",
      estados: ESTADOS_TRAMITE, columnas: [["Placa", "placa"], ["Trámite", "texto"], ["Organismo", "texto"], ["Valor", "dinero"], ["Estado", "estado"], ["Fecha", "texto"]],
      filas: TRAMITES.slice(0, 10).map(function (t, i) { return [t.placa, t.tipo, ["Medellín", "Envigado", "Sabaneta", "Itagüí"][i % 4], 380000 + i * 12500, t.estado, t.fecha]; }),
      acciones: [["Descargar en Excel", "descargar:reporte-detallado.xlsx"], ["Programar envío", "form:programar-reporte", "secundario"]] },
    usuarios: { titulo: "Usuarios", il: "colaboradores", ante: "Personas", texto: "Quién entra a la plataforma y qué puede hacer.", pestanas: [
      ["usuarios", "Usuarios", { pendiente: "invitado", barra: { invitado: ["invitaciones sin aceptar.", "Reenviar invitaciones", "aviso:Invitaciones reenviadas"] }, estados: E.activo,
        columnas: [["Nombre", "personas"], ["Rol", "texto"], ["Compañía", "texto"], ["Estado", "estado"], ["Último ingreso", "texto"]],
        filas: [[["Laura Gómez", "laura.gomez@flitsas.com"], "Super Admin", "FLIT S.A.S.", "activo", "Ahora"], [["Pedro Álvarez", "pedro@andino.co"], "Admin de compañía", "Concesionario Andino", "activo", "Hoy"], [["Sofía Cardona", "sofia@andino.co"], "Radicador", "Concesionario Andino", "activo", "Ayer"], [["Martín Ochoa", "martin@tvalle.co"], "Radicador", "Transportes del Valle S.A.S.", "invitado", "Nunca"], [["Lucía Franco", "lucia@leasingmed.co"], "Consulta", "Leasing Medellín S.A.", "suspendido", "12 oct"], [["Camilo Ruiz", "camilo@andino.co"], "Radicador", "Concesionario Andino", "invitado", "Nunca"]],
        acciones: [["Invitar colaborador", "form:invitar-usuario"]] }],
      ["roles", "Roles y permisos", { render: "rbac" }],
      ["clientes-ict", "Clientes ICT", { estados: E.activo, columnas: [["Cliente", "texto"], ["Compañía", "texto"], ["Usuario", "mono"], ["Estado", "estado"], ["Último uso", "texto"]],
        filas: [["Integración Andino", "Concesionario Andino", "andino-ict", "activo", "Hoy, 10:02"], ["Centro de soporte", "FLIT S.A.S.", "flit-soporte", "activo", "Hoy, 09:40"], ["Valle pruebas", "Transportes del Valle S.A.S.", "tvalle-qa", "inactivo", "2 oct"]],
        acciones: [["Nuevo cliente", "form:cliente-ict"]] }],
      ["eliminados", "Eliminados", { estados: E.activo, columnas: [["Nombre", "personas"], ["Rol", "texto"], ["Eliminado", "texto"], ["Por", "texto"]],
        filas: [[["Ramiro Duque", "ramiro@andino.co"], "Radicador", "15 oct", "Pedro Álvarez"], [["Elena Vargas", "elena@tvalle.co"], "Consulta", "2 sep", "Laura Gómez"]], fila: "Restaurar" }]
    ] },
    companias: { titulo: "Compañías", il: "empresas", ante: "Administradores", texto: "Todas las empresas que usan FLIT y cómo está configurada cada una.", abrir: "#compania", pendiente: "suspendido",
      estados: E.activo, columnas: [["Razón social", "personas"], ["Tipo", "texto"], ["Trámites del mes", "num"], ["Usuarios", "num"], ["Estado", "estado"]],
      filas: [[["Concesionario Andino", "NIT 900123456"], "Concesionario", 148, 12, "activa"], [["Transportes del Valle S.A.S.", "NIT 890456123"], "Gestor", 63, 5, "activa"], [["Leasing Medellín S.A.", "NIT 811009988"], "Leasing", 41, 4, "activa"], [["Red Movilidad Antioquia", "NIT 901334455"], "Cabeza de red", 0, 3, "activa"], [["Autos del Sur", "NIT 900778899"], "Concesionario", 0, 1, "suspendido"]],
      barra: { suspendido: ["compañía suspendida: no puede radicar.", "Revisar la suspensión", "#compania"] },
      acciones: [["Crear compañía", "form:crear-compania"]] },
    organismos: { titulo: "Organismos de tránsito", il: "organismos", ante: "Tránsito", texto: "Los organismos donde se radican los trámites y su forma de trabajo.", abrir: "#organismo",
      estados: { flit: ["En FLIT", "ok"], quipux: ["Por Quipux", "info"], inactivo: ["Inactivo", ""] }, columnas: [["Organismo", "personas"], ["Trámites en curso", "num"], ["Tiempo medio", "texto"], ["Radicación", "estado"]],
      filas: [[["Secretaría de Movilidad de Medellín", "Código 05001000"], 58, "1,8 días", "flit"], [["Tránsito de Envigado", "Código 05266000"], 21, "2,4 días", "quipux"], [["Tránsito de Sabaneta", "Código 05631000"], 14, "1,2 días", "flit"], [["Tránsito de Itagüí", "Código 05360000"], 0, "Sin datos", "inactivo"]],
      acciones: [["Agregar organismo", "form:crear-organismo"]] },
    causales: { titulo: "Causales de rechazo", il: "causales", ante: "Tránsito", texto: "El catálogo con el que un organismo explica por qué devuelve un trámite.",
      estados: E.activo, columnas: [["Causal", "personas"], ["Veces este mes", "num"], ["Estado", "estado"]],
      filas: [[["Documento ilegible", "Volver a cargar el documento escaneado completo"], 14, "activo"], [["Firma no coincide", "Repetir la firma con la persona presente"], 6, "activo"], [["Impronta sin validar", "Validar la impronta antes de radicar"], 4, "activo"], [["Paz y salvo vencido", "Cargar un paz y salvo con menos de 30 días"], 3, "activo"], [["Código antiguo", "Ya no se usa"], 0, "inactivo"]],
      acciones: [["Nueva causal", "form:nueva-causal"]] },
    documental: { titulo: "Documental", il: "documentos", ante: "Documentos", texto: "Los documentos que pide cada trámite en toda la plataforma.",
      estados: E.activo, columnas: [["Documento", "personas"], ["Se pide en", "texto"], ["Obligatorio", "texto"], ["Estado", "estado"]],
      filas: [[["Improntas", "Del motor y del chasis"], "Traspaso", "Sí", "activo"], [["Paz y salvo", "De impuestos y multas"], "Traspaso", "Sí", "activo"], [["Factura", "Del concesionario"], "Matrícula inicial", "Sí", "activo"], [["Documento del comprador", "Cédula o NIT"], "Todos", "Sí", "activo"], [["Poder del vendedor", "Si firma un apoderado"], "Traspaso", "No", "activo"], [["Certificado de blindaje", "Solo vehículos blindados"], "Todos", "No", "inactivo"]],
      acciones: [["Nuevo documento", "form:nuevo-documento"]] },
    improntas: { titulo: "Improntas", il: "improntas", ante: "Documentos", texto: "Las improntas del motor y del chasis, generadas y validadas.", pestanas: [
      ["generar", "Generar impronta", { render: "impronta" }],
      ["historial", "Historial", { estados: { validada: ["Validada", "ok"], pendiente: ["Por validar", "warn"], rechazada: ["Rechazada", "danger"] }, pendiente: "pendiente",
        barra: { pendiente: ["improntas esperan validación del organismo.", "Ver la primera", "#tramite"] },
        columnas: [["Placa", "placa"], ["Motor", "mono"], ["Chasis", "mono"], ["Estado", "estado"], ["Fecha", "texto"]],
        filas: [["KLM482", "PE30123456", "3MVDM2W7XNL1234", "validada", "Hoy"], ["JNX219", "ZY40981234", "9BWZZZ377VT0045", "pendiente", "Hoy"], ["BCD771", "K24Z7123456", "MRHRW2850KP0001", "pendiente", "Ayer"], ["PLK903", "G4FC876543", "KMHCT41BAEU0002", "rechazada", "2 nov"]] }]
    ] },
    "generacion-documental": { titulo: "Generación documental", il: "generacion-documental", ante: "Documentos", texto: "Contratos, mandatos y FUR generados en lote para los trámites.",
      estados: { listo: ["Listo", "ok"], generando: ["Generando", "info"], error: ["Con error", "danger"] }, columnas: [["Lote", "personas"], ["Documentos", "num"], ["Estado", "estado"], ["Fecha", "texto"]],
      filas: [[["Contratos de compraventa", "5 trámites que esperan firma"], 5, "listo", "Hoy, 10:20"], [["Mandatos", "Concesionario Andino"], 12, "generando", "Hoy, 10:41"], [["FUR", "Radicados del 3 nov"], 2, "error", "3 nov"]], fila: "Descargar",
      acciones: [["Generar documentos", "form:documento-generar"]] },
    quipux: { titulo: "Quipux", il: "quipux", ante: "Documentos", texto: "Trámites que se radican en organismos que trabajan con Quipux.", pendiente: "error",
      estados: E.log, barra: { error: ["envíos fallaron. La plataforma los reintenta cada 15 minutos.", "Reintentar ahora", "aviso:Reintento en curso"] },
      columnas: [["Placa", "placa"], ["Organismo", "texto"], ["Radicado Quipux", "mono"], ["Estado", "estado"], ["Enviado", "texto"]],
      filas: [["JNX219", "Tránsito de Envigado", "ENV-2026-88123", "ok", "Hoy, 09:20"], ["YHN590", "Tránsito de Envigado", "Sin número", "error", "Hoy, 08:02"], ["ZXC147", "Tránsito de Envigado", "ENV-2026-88011", "ok", "Ayer"]] },
    "tipos-tramite": { titulo: "Tipos de trámite", il: "tipos-tramite", ante: "Plataforma", texto: "Qué trámites existen y cuáles tienen asistente en línea.",
      estados: { habilitado: ["Asistente habilitado", "ok"], apagado: ["Sin asistente", ""] }, columnas: [["Trámite", "personas"], ["Código", "mono"], ["Asistente", "estado"]],
      filas: [[["Matrícula inicial", "Vehículo nuevo"], "1", "habilitado"], [["Traspaso", "Entre dos personas"], "3", "habilitado"], [["Traspaso unilateral", "Leasing al locatario"], "4", "habilitado"], [["Cambio de color", ""], "5", "apagado"], [["Duplicado de licencia", ""], "7", "apagado"], [["Inscripción de prenda", ""], "9", "apagado"]],
      acciones: [["Nuevo tipo de trámite", "form:nuevo-tipo"]] },
    "confirmacion-runt": { titulo: "Confirmación RUNT", il: "fuentes-oficiales", ante: "Plataforma", texto: "Trámites radicados que esperan que el RUNT confirme el cambio.", pendiente: "pendiente",
      estados: { pendiente: ["Por confirmar", "warn"], confirmado: ["Confirmado", "ok"], diferencia: ["Con diferencias", "danger"] },
      barra: { pendiente: ["trámites esperan la confirmación del RUNT.", "Consultar de nuevo", "aviso:Consulta enviada al RUNT"] },
      columnas: [["Placa", "placa"], ["Trámite", "texto"], ["Propietario en el RUNT", "texto"], ["Estado", "estado"], ["Radicado", "texto"]],
      filas: [["GHT904", "Matrícula inicial", "Paula Restrepo", "pendiente", "Ayer"], ["HJK286", "Traspaso", "Ricardo Zapata", "diferencia", "1 nov"], ["RTY608", "Traspaso", "Camila Vélez", "confirmado", "3 nov"], ["VFR318", "Matrícula inicial", "Isabel Correa", "confirmado", "31 oct"]] },
    mandatos: { titulo: "Mandatos", il: "mandatos", ante: "Plataforma", texto: "Plantillas de mandato que firman los clientes para que un gestor tramite por ellos.",
      estados: E.activo, columnas: [["Plantilla", "personas"], ["Aplica a", "texto"], ["Versión", "num"], ["Estado", "estado"]],
      filas: [[["Mandato persona natural", "Actualizada el 2 oct"], "Persona natural", 4, "publicado"], [["Mandato persona jurídica", "Con representante legal"], "Persona jurídica", 3, "publicado"], [["Mandato leasing", "Borrador en revisión"], "Persona jurídica", 1, "borrador"]],
      acciones: [["Nueva plantilla", "form:nuevo-mandato"]] },
    fur: { titulo: "FUR", il: "fur", ante: "Plataforma", texto: "Formulario Único de Solicitud generado por trámite.", pendiente: "incompleto",
      estados: { completo: ["Completo", "ok"], incompleto: ["Faltan datos", "warn"] }, barra: { incompleto: ["FUR tienen datos por completar.", "Completar el primero", "#traspaso"] },
      columnas: [["Placa", "placa"], ["Trámite", "texto"], ["Falta", "texto"], ["Estado", "estado"]],
      filas: [["KLM482", "Traspaso", "Nada", "completo"], ["JNX219", "Traspaso", "Correo del comprador", "incompleto"], ["ZXC147", "Matrícula inicial", "Número de factura", "incompleto"], ["GHT904", "Matrícula inicial", "Nada", "completo"]] },
    notificaciones: { titulo: "Notificaciones", il: "notificaciones", ante: "Plataforma", texto: "Los correos y mensajes que la plataforma envía en cada momento del trámite.", render: "notificaciones" },
    banners: { titulo: "Banners", il: "banners", ante: "Plataforma", texto: "Avisos que aparecen arriba de la plataforma para todos los usuarios.", render: "banners" },
    rbac: { titulo: "Roles y permisos", il: "roles", ante: "Seguridad", texto: "Qué puede ver y hacer cada rol en cada módulo.", render: "rbac" },
    auditoria: { titulo: "Auditoría", il: "auditoria", ante: "Seguridad", texto: "Quién hizo qué y cuándo, en toda la plataforma.",
      estados: { normal: ["Normal", ""], sensible: ["Sensible", "warn"], bloqueado: ["Bloqueado", "danger"] }, buscar: "Buscar por persona, placa o acción",
      columnas: [["Cuándo", "texto"], ["Persona", "personas"], ["Acción", "texto"], ["Sobre", "texto"], ["Tipo", "estado"]],
      filas: [["Hoy, 10:42", ["Sofía Cardona", "Concesionario Andino"], "Envió a firma", "Traspaso KLM 482", "normal"], ["Hoy, 10:15", ["Laura Gómez", "FLIT S.A.S."], "Cambió permisos", "Rol Radicador", "sensible"], ["Hoy, 09:58", ["Sistema", "Ingreso"], "Bloqueó un ingreso", "5 intentos fallidos de lucia@leasingmed.co", "bloqueado"], ["Hoy, 09:30", ["Pedro Álvarez", "Concesionario Andino"], "Invitó a un colaborador", "camilo@andino.co", "normal"], ["Ayer, 18:02", ["Laura Gómez", "FLIT S.A.S."], "Descargó un reporte", "Reportes detallados", "sensible"]],
      acciones: [["Descargar registro", "descargar:auditoria.csv", "secundario"]] },
    procesos: { titulo: "Procesos periódicos", il: "procesos", ante: "Operación", texto: "Las tareas que la plataforma corre sola y cuándo corrieron por última vez.", render: "procesos" },
    "log-qx": { titulo: "Log QX", il: "log-qx", ante: "Integraciones", texto: "Cada mensaje intercambiado con QX.", pendiente: "error", estados: E.log,
      barra: { error: ["mensajes con error en las últimas 24 horas.", "Reintentar los fallidos", "aviso:Reintento en curso"] },
      columnas: [["Cuándo", "texto"], ["Operación", "mono"], ["Placa", "placa"], ["Duración", "texto"], ["Resultado", "estado"]],
      filas: [["Hoy, 10:40", "consultarVehiculo", "KLM482", "820 ms", "ok"], ["Hoy, 10:12", "registrarTramite", "JNX219", "1,4 s", "ok"], ["Hoy, 09:51", "registrarTramite", "YHN590", "30 s", "error"], ["Hoy, 09:20", "consultarEstado", "GHT904", "640 ms", "reintento"]] },
    "ict-logs": { titulo: "Log ICT", il: "log-ict", ante: "Integraciones", texto: "Las llamadas que hacen los clientes de integración.", pendiente: "error", estados: E.log,
      barra: { error: ["llamadas fallaron hoy.", "Ver la primera", "detalle:0"] },
      columnas: [["Cuándo", "texto"], ["Cliente", "mono"], ["Operación", "mono"], ["Respuesta", "num"], ["Resultado", "estado"]],
      filas: [["Hoy, 10:41", "flit-soporte", "POST /external-transaction/register", 201, "ok"], ["Hoy, 10:05", "andino-ict", "GET /ict/status/88", 200, "ok"], ["Hoy, 09:47", "andino-ict", "POST /transact-attachments/upload", 413, "error"], ["Hoy, 09:02", "flit-soporte", "POST /auth/login", 200, "ok"]] },
    "ict-trazabilidad": { titulo: "Trazabilidad ICT", il: "trazabilidad", ante: "Integraciones", texto: "Cada pre-trámite recibido por integración, del registro al trámite.", pendiente: "espera",
      estados: { espera: ["Esperando documentos", "warn"], materializado: ["Convertido en trámite", "ok"], descartado: ["Descartado", "danger"] },
      barra: { espera: ["pre-trámites esperan documentos.", "Avisar al cliente", "aviso:Aviso enviado al cliente"] },
      columnas: [["Referencia", "mono"], ["Cliente", "texto"], ["Placa", "placa"], ["Estado", "estado"], ["Recibido", "texto"]],
      filas: [["SOP-MV1BS05VD", "Centro de soporte", "KLM482", "materializado", "Hoy, 10:41"], ["AND-00412", "Integración Andino", "ZXC147", "espera", "Hoy, 09:47"], ["AND-00409", "Integración Andino", "WSX725", "descartado", "2 nov"]] },
    "ict-reportes": { titulo: "Reportes ICT", il: "reportes-ict", ante: "Integraciones", texto: "Volumen y resultado de las integraciones por cliente.", render: "reportes-ict" },
    comparendos: { titulo: "Comparendos", il: "comparendos", ante: "FLIT Comparendos", texto: "Los comparendos de tu flota, a tiempo para pagar con descuento.", producto: "Comparendos", pendiente: "descuento",
      estados: { descuento: ["Con descuento por vencer", "warn"], pendiente: ["Por pagar", "info"], pagado: ["Pagado", "ok"], cobro: ["En cobro coactivo", "danger"] },
      barra: { descuento: ["comparendos pierden el descuento esta semana.", "Ver cómo pagar", "aviso:Te enviamos los enlaces de pago al correo"] },
      columnas: [["Placa", "placa"], ["Comparendo", "mono"], ["Infracción", "texto"], ["Valor", "dinero"], ["Estado", "estado"], ["Fecha", "texto"]],
      filas: [["KLM482", "05001000000041235", "C29 Exceso de velocidad", 604100, "descuento", "28 oct"], ["JNX219", "05266000000008812", "C02 Estacionar en sitio prohibido", 604100, "pendiente", "15 oct"], ["BCD771", "05001000000039870", "C35 Revisión técnico-mecánica vencida", 604100, "descuento", "30 oct"], ["RTY608", "05631000000002231", "C14 Transitar en contravía", 604100, "pagado", "2 oct"], ["PLK903", "05001000000020011", "D12 Servicio no autorizado", 1208200, "cobro", "12 jun"]] },
    diagnostico: { titulo: "Diagnóstico", il: "diagnostico", ante: "FLIT Diagnóstico", texto: "El estado legal y documental de cada vehículo de tu flota.", producto: "Diagnóstico", pendiente: "alerta",
      estados: { aldia: ["Al día", "ok"], alerta: ["Requiere atención", "warn"], bloqueado: ["No se puede traspasar", "danger"] },
      barra: { alerta: ["vehículos tienen documentos por vencer o multas.", "Descargar el diagnóstico", "descargar:diagnostico-flota.pdf"] },
      columnas: [["Placa", "placa"], ["SOAT", "texto"], ["Revisión", "texto"], ["Multas", "texto"], ["Limitaciones", "texto"], ["Estado", "estado"]],
      filas: [["KLM482", "Vigente", "Vigente", "1", "Ninguna", "alerta"], ["JNX219", "Vence en 9 días", "Vigente", "1", "Ninguna", "alerta"], ["RTY608", "Vigente", "Vigente", "0", "Ninguna", "aldia"], ["PLK903", "Vencido", "Vencida", "1", "Embargo", "bloqueado"], ["VFR318", "Vigente", "No aplica", "0", "Prenda a favor de Leasing Medellín", "aldia"]],
      acciones: [["Agregar vehículos", "abrir:consulta"]] }
  };

  // Flota de muestra más grande para Comparendos y Diagnóstico: así se ven las vistas de 10 y el paso de página.
  (function () {
    var placas = ["MNB552", "QWE335", "ZXC147", "HJK286", "TGB640", "YHN590", "EDC413", "WSX725", "UJM118", "IKL904", "OLP367", "AZS812", "DFG450", "CVB273", "RTG689", "NHY134", "MJU578", "XSW921", "LOP246", "KIU803"];
    var infr = [["C29 Exceso de velocidad", 604100], ["C02 Estacionar en sitio prohibido", 604100], ["C35 Revisión técnico-mecánica vencida", 604100], ["C14 Transitar en contravía", 604100], ["D02 Conducir sin SOAT", 1208200], ["B01 Conducir sin portar la licencia", 302000]];
    var est = ["descuento", "pendiente", "pagado", "pagado", "pendiente", "cobro", "pagado"], dias = ["27 oct", "24 oct", "20 oct", "17 oct", "11 oct", "6 oct", "29 sep", "22 sep", "14 sep", "3 sep"];
    placas.forEach(function (pl, i) {
      var f = infr[i % infr.length];
      MODULOS.comparendos.filas.push([pl, "0500100000" + String(3100000 + i * 7919).slice(0, 7), f[0], f[1], est[i % est.length], dias[i % dias.length]]);
      var d = i % 5;
      MODULOS.diagnostico.filas.push([pl, d === 1 ? "Vence en " + (5 + i) + " días" : d === 3 ? "Vencido" : "Vigente", d === 2 ? "Vence en " + (10 + i) + " días" : "Vigente", String(d === 3 ? 2 : d === 1 ? 1 : 0), d === 3 ? "Embargo" : d === 4 ? "Prenda a favor de Banco de Occidente" : "Ninguna", d === 3 ? "bloqueado" : d === 1 || d === 2 ? "alerta" : "aldia"]);
    });
  })();

  // Consola de compañía y hub de organismo (pestañas)
  MODULOS.compania = { titulo: "Concesionario Andino", il: "empresas", ante: "Compañía · NIT 900123456", texto: "Concesionario. 12 usuarios, 148 trámites este mes.", volver: ["companias", "Compañías"], pestanas: [
    ["matricula", "Matrícula inicial", { render: "config-tramite", tramite: "matrícula inicial" }],
    ["traspasos", "Traspasos", { render: "config-tramite", tramite: "traspaso" }],
    ["configuracion", "Configuración", { render: "config-empresa" }],
    ["documentos", "Documentos", { estados: E.activo, columnas: [["Documento", "texto"], ["Trámite", "texto"], ["Estado", "estado"]], filas: [["Factura", "Matrícula inicial", "activo"], ["Improntas", "Traspaso", "activo"], ["Certificado de blindaje", "Todos", "inactivo"]], acciones: [["Nuevo documento", "form:nuevo-documento"]] }],
    ["placas", "Placas preasignadas", { estados: { libre: ["Libre", "ok"], usada: ["Usada", "info"], reservada: ["Reservada", "warn"] }, columnas: [["Placa", "placa"], ["Rango", "texto"], ["Estado", "estado"]], filas: [["KLM100", "KLM100 a KLM199", "usada"], ["KLM101", "KLM100 a KLM199", "reservada"], ["KLM102", "KLM100 a KLM199", "libre"]], acciones: [["Agregar rango", "form:rango-placas"]] }],
    ["representantes", "Representantes legales", { estados: E.activo, columnas: [["Nombre", "personas"], ["Documento", "mono"], ["Estado", "estado"]], filas: [[["Juan Pablo Arango", "jp.arango@andino.co"], "CC 71234567", "activo"]], acciones: [["Agregar representante", "form:representante"]] }],
    ["mandatarios", "Mandatarios", { estados: E.activo, columnas: [["Nombre", "personas"], ["Documento", "mono"], ["Vigente hasta", "texto"], ["Estado", "estado"]], filas: [[["Sofía Cardona", "sofia@andino.co"], "CC 1037654321", "31 dic 2026", "activo"], [["Ramiro Duque", "ramiro@andino.co"], "CC 98012345", "15 oct 2026", "inactivo"]], acciones: [["Agregar mandatario", "form:mandatario"]] }],
    ["usuarios-cia", "Usuarios", { estados: E.activo, columnas: [["Nombre", "personas"], ["Rol", "texto"], ["Estado", "estado"]], filas: [[["Pedro Álvarez", "pedro@andino.co"], "Admin de compañía", "activo"], [["Sofía Cardona", "sofia@andino.co"], "Radicador", "activo"], [["Camilo Ruiz", "camilo@andino.co"], "Radicador", "invitado"]], acciones: [["Invitar colaborador", "form:invitar-usuario"]] }],
    ["historial", "Historial de cambios", { estados: { normal: ["Cambio", ""], sensible: ["Sensible", "warn"] }, columnas: [["Cuándo", "texto"], ["Persona", "texto"], ["Cambio", "texto"], ["Tipo", "estado"]], filas: [["Hoy, 09:30", "Pedro Álvarez", "Invitó a camilo@andino.co", "normal"], ["2 nov", "Laura Gómez", "Habilitó traspaso unilateral", "sensible"], ["28 oct", "Pedro Álvarez", "Cambió el correo de notificaciones", "normal"]] }]
  ] };
  MODULOS.organismo = { titulo: "Secretaría de Movilidad de Medellín", il: "organismos", ante: "Organismo de tránsito · Código 05001000", texto: "Radica en FLIT. 58 trámites en curso, 1,8 días en promedio.", volver: ["organismos", "Organismos de tránsito"], pestanas: [
    ["bandeja", "Trámites", { estados: ESTADOS_TRAMITE, pendiente: "radicado", barra: { radicado: ["trámites esperan revisión del organismo.", "Revisar el primero", "#tramite"] }, columnas: [["Placa", "placa"], ["Trámite", "texto"], ["Compañía", "texto"], ["Estado", "estado"], ["Recibido", "texto"]], filas: TRAMITES.filter(function (t) { return t.estado !== "borrador"; }).slice(0, 8).map(function (t) { return [t.placa, t.tipo, t.vende, t.estado, t.fecha]; }), abrir: "#tramite" }],
    ["reglas", "Reglas", { estados: E.activo, columnas: [["Regla", "texto"], ["Trámite", "texto"], ["Qué hace", "texto"], ["Estado", "estado"]], filas: [["Vehículo con prenda", "Traspaso", "Pide la carta de levantamiento", "activo"], ["Comprador menor de edad", "Traspaso", "Bloquea la radicación", "activo"], ["Placa de otro organismo", "Matrícula inicial", "Avisa al gestor", "inactivo"]], acciones: [["Nueva regla", "form:nueva-regla"]] }],
    ["requisitos", "Requisitos", { estados: E.activo, columnas: [["Requisito", "texto"], ["Trámite", "texto"], ["Estado", "estado"]], filas: [["Paz y salvo municipal", "Traspaso", "activo"], ["Impronta validada", "Traspaso", "activo"], ["Factura electrónica", "Matrícula inicial", "activo"]], acciones: [["Nuevo requisito", "form:requisito"]] }],
    ["preasignacion", "Preasignación", { estados: { libre: ["Libre", "ok"], usada: ["Usada", "info"] }, columnas: [["Rango", "texto"], ["Compañía", "texto"], ["Usadas", "num"], ["Estado", "estado"]], filas: [["KLM100 a KLM199", "Concesionario Andino", 41, "libre"], ["GHT900 a GHT949", "Concesionario Andino", 50, "usada"]], acciones: [["Agregar rango", "form:rango-placas"]] }],
    ["usuarios-ot", "Usuarios", { estados: E.activo, columnas: [["Nombre", "personas"], ["Rol", "texto"], ["Estado", "estado"]], filas: [[["Gloria Patiño", "gpatino@medellin.gov.co"], "Admin del organismo", "activo"], [["Hernán Cano", "hcano@medellin.gov.co"], "Revisor", "activo"]], acciones: [["Invitar colaborador", "form:invitar-usuario"]] }],
    ["configuracion-ot", "Configuración", { render: "config-organismo" }]
  ] };

  var REPORTES = [
    ["resumen", "Resumen general", [["Trámites radicados", "128"], ["Finalizados", "96"], ["Tiempo medio", "2,1 días"], ["Rechazos", "4 %"]], [["Medellín", 58], ["Envigado", 21], ["Sabaneta", 14], ["Itagüí", 9], ["Bello", 6]]],
    ["operacion", "Operación", [["En curso", "12"], ["Esperan firma", "5"], ["En validación", "3"], ["Borradores", "2"]], [["Traspaso", 92], ["Matrícula inicial", 31], ["Traspaso unilateral", 5]]],
    ["organismo", "Organismo de tránsito", [["Organismos activos", "3"], ["Más rápido", "Sabaneta"], ["Más lento", "Envigado"], ["Devoluciones", "11"]], [["Sabaneta", 1.2], ["Medellín", 1.8], ["Envigado", 2.4]]],
    ["uso", "Uso del aplicativo", [["Usuarios activos", "18"], ["Ingresos hoy", "41"], ["Desde celular", "37 %"], ["Firmas en línea", "88 %"]], [["Lunes", 52], ["Martes", 61], ["Miércoles", 58], ["Jueves", 66], ["Viernes", 49]]],
    ["productividad", "Productividad", [["Trámites por radicador", "21"], ["Más productivo", "Sofía Cardona"], ["Tiempo de radicación", "14 min"], ["Correcciones", "6 %"]], [["Sofía Cardona", 34], ["Camilo Ruiz", 22], ["Pedro Álvarez", 9]]]
  ];

  var MANUAL = [
    ["Primeros pasos", "Cómo entrar, cambiar tu contraseña y ubicarte en la plataforma", "inicio"],
    ["Crear un traspaso", "Del vehículo a la radicación, paso por paso", "traspaso"],
    ["Matrícula inicial", "Para concesionarios: factura, improntas y placa", "matricula"],
    ["Firmas e identidad", "Cómo firman el vendedor y el comprador desde su celular", "firmas"],
    ["Documentos y OCR", "Qué documentos se piden y cómo cargarlos bien", "documentos"],
    ["Reportes", "Qué mide cada reporte y cómo descargarlo", "reportes"],
    ["Usuarios y roles", "Invitar colaboradores y darles permisos", "colaboradores"],
    ["Integraciones", "Registrar trámites desde tu sistema", "integraciones"]
  ];

  return { ESTADOS_TRAMITE: ESTADOS_TRAMITE, TRAMITES: TRAMITES, DOCK: DOCK, PRODUCTOS: PRODUCTOS, FORMS: FORMS, MODULOS: MODULOS, REPORTES: REPORTES, MANUAL: MANUAL };
})();
