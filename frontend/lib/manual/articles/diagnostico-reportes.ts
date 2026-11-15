import type { ManualArticle } from "../types";

/**
 * HU #14580 (FE-7) — Dashboard de estadísticas y módulo de Reportes de Diagnóstico, por rol (ADR-0088).
 *
 * Los rótulos salen de las pantallas reales (`frontend-diagnostico/components/estadisticas/**`, `components/reportes/**`,
 * `lib/estadisticas/**`, `lib/reportes/**`, `components/DiagnosticoShell.tsx`) y las reglas, del contrato cerrado de la
 * ola (rango de 366 días, máximos por formato, mensajes de error). El texto es de usuario: no cita códigos HTTP ni
 * detalles internos de seguridad. Sin datos reales: la placa de ejemplo es ABC123 y la compañía, «Compañía Demo».
 *
 * Audiencias: lo que ve la compañía va como «Gestor» (también lo ven el Admin de Compañía y el Super Admin); la vista
 * global y el reporte de consumo, «Super Admin».
 */
export const DIAGNOSTICO_REPORTES_ARTICLES: ManualArticle[] = [
  {
    slug: "6-diagnostico/13-dashboard",
    title: "Dashboard de Diagnóstico: indicadores, periodo y alcance por rol",
    audience: "Gestor",
    sectionId: "diagnostico",
    keywords: [
      "dashboard",
      "dashboard de diagnostico",
      "estadisticas",
      "indicadores",
      "kpi",
      "resumen de diagnosticos",
      "inicio diagnostico",
      "tasa de exito",
      "tiempo promedio de consulta",
      "uso en el tiempo",
      "salud de las fuentes",
      "veredictos",
      "periodo",
      "rango de fechas",
      "ultimos 30 dias",
      "graficas",
    ],
    summary:
      "Qué mide cada indicador del Dashboard, con qué fecha cuenta, cómo cambiar el periodo y qué ve cada rol.",
    blocks: [
      {
        id: "que-es",
        title: "1. Qué es el Dashboard",
        paragraphs: [
          "El «Dashboard» es la primera pantalla de Diagnóstico y el destino del botón «Inicio Diagnóstico» del dock. Resume los diagnósticos de tu compañía en un periodo: cuántos se hicieron, cómo respondieron las fuentes, qué dictamen salió y cuántos saneamientos y trámites se originaron. No muestra placas, documentos ni nombres de personas: solo cifras.",
          "La cabecera dice «Resumen de diagnósticos de» y el nombre de tu compañía. A la derecha están las fechas «Desde» y «Hasta» y, si tu rol puede crear diagnósticos, el botón «Nuevo diagnóstico».",
        ],
        media: [
          {
            kind: "diagram",
            id: "diagnostico-dashboard-reportes",
            alt: "Dashboard y reportes por rol. Al entrar a Diagnóstico se abre el Dashboard con los últimos 30 días, con un máximo de un año. Un usuario o administrador de la compañía ve el resumen de su compañía, sin selector. El equipo FLIT abre en Todas las compañías, con Compañías con más uso, y puede elegir una compañía en el selector. Desde Reportes del dock, la compañía ve cuatro reportes y FLIT suma Consumo por compañía. La tabla muestra 25, 50 o 100 filas con el aviso de datos personales de la Ley 1581. Si el rol puede descargar reportes, Exportar Excel y Exportar PDF bajan todo el filtro hasta el máximo del formato; si no, solo se ve la tabla. Cada consulta y descarga queda registrada con el usuario.",
            caption: "Dashboard y Reportes según el rol.",
          },
        ],
      },
      {
        id: "indicadores",
        title: "2. Los seis indicadores",
        paragraphs: [
          "Arriba hay seis tarjetas. Las cifras usan separador de miles. Si no hay datos para calcular un valor, la tarjeta dice «sin datos» en lugar de un cero.",
        ],
        bullets: [
          "«Diagnósticos»: los diagnósticos (lotes) creados en el periodo, con el desglose «N express · M masivos». Pulsa la tarjeta para abrir el listado «Diagnósticos» con esas mismas fechas.",
          "«Vehículos»: los vehículos de esos diagnósticos y, debajo, cuántos son únicos (una placa diagnosticada dos veces cuenta una vez). La tarjeta abre la pestaña «Vehículos» del listado.",
          "«Consultas a fuentes»: las consultas de esos vehículos a todas las fuentes, con la tasa de éxito: las consultas exitosas sobre las exitosas más las que terminaron en error. Las que quedaron en Novedad (no aplica) no cuentan en la tasa.",
          "«Tiempo promedio de consulta»: cuánto tarda en promedio una consulta automática, en segundos. Los llenados a mano de FLIT no cuentan.",
          "«Saneamientos solicitados»: las solicitudes de saneamiento hechas en el periodo, en cualquier estado. La tarjeta abre la pestaña «Vehículos» filtrada por «Con saneamiento».",
          "«Trámites creados»: los borradores de trámite que se crearon desde Diagnóstico en el periodo.",
        ],
        callouts: [
          {
            variant: "info",
            title: "Cada indicador cuenta por su propia fecha",
            text: "Los diagnósticos, los vehículos y las consultas cuentan por la fecha de creación del diagnóstico. Los saneamientos, por la fecha de solicitud. Los trámites, por la fecha de creación del trámite. Por eso un saneamiento pedido hoy sobre un diagnóstico del mes pasado aparece en el periodo de hoy.",
          },
        ],
      },
      {
        id: "graficas",
        title: "3. Las gráficas y la tabla de fuentes",
        paragraphs: [
          "Cada gráfica tiene su título, los valores escritos junto a cada barra y una leyenda con texto, así que no dependes del color. Debajo de cada una puedes desplegar los datos en una tabla.",
        ],
        bullets: [
          "«Uso en el tiempo»: diagnósticos y vehículos por día, por semana o por mes. El subtítulo dice cuál: por día hasta 62 días de rango, por semana hasta unos seis meses (desde el lunes) y por mes en rangos más largos.",
          "«Veredictos»: el dictamen vigente de los vehículos (Viables, Bloqueados y Sin dictamen) con su cifra y su porcentaje.",
          "«Tipo de análisis»: cuántos diagnósticos fueron Simple y cuántos Con análisis.",
          "«Salud de las fuentes»: una fila por fuente con consultas, exitosas, con error, no aplica, pendientes, tasa de éxito y tiempo promedio. Solo salen las fuentes que se consultaron en el periodo.",
          "«Saneamientos»: los cinco estados (Solicitado, En gestión, Saneado, No procede y Anulado), por la fecha de solicitud, aunque alguno esté en cero.",
          "«Trámites»: las derivaciones a trámite del periodo, separadas en creadas, omitidas, fallidas y en curso.",
        ],
      },
      {
        id: "periodo",
        title: "4. Cambiar el periodo",
        paragraphs: [
          "Por defecto el Dashboard muestra los últimos 30 días, con hoy incluido. Cambia «Desde» y «Hasta» y el resumen se actualiza solo, sin botón de aplicar. Las fechas son días completos en hora de Colombia. El periodo queda en la dirección de la página: puedes compartir el enlace o guardarlo.",
          "Si el rango es inválido, el campo «Hasta» lo marca y no se pide nada: «El rango no puede superar un año», «La fecha final no puede ser posterior a hoy» o «La fecha final no puede ser anterior a la inicial».",
        ],
      },
      {
        id: "roles",
        title: "5. Qué ve cada rol",
        paragraphs: [
          "El usuario y el administrador de Diagnóstico ven el resumen de su compañía, sin selector de compañía. Si la configuración «Quién ve los diagnósticos» está en «Solo los propios», cada usuario ve las cifras de los diagnósticos que puede ver en el listado.",
          "El equipo FLIT abre el Dashboard en «Todas las compañías» y puede elegir una compañía. Lo explica el artículo «Vista global de FLIT: Dashboard y Reportes de todas las compañías».",
        ],
      },
      {
        id: "estados",
        title: "6. Sin datos, cargando o con error",
        paragraphs: [],
        bullets: [
          "Mientras carga verás «Cargando el resumen…».",
          "Sin diagnósticos en el periodo: «Aún no hay diagnósticos en este periodo», con «Nuevo diagnóstico» (si puedes crear) y «Ver los últimos 90 días».",
          "Si no se pudo cargar: «No se pudo cargar el resumen» con «Reintentar». Si la compañía elegida ya no tiene Diagnóstico activo: «Esa compañía no tiene Diagnóstico activo.» con «Ver todas las compañías». Si tu rol no permite ver el Dashboard, entras directamente a «Diagnósticos» o a «Reportes»; sin ninguno de los dos, verás «No tienes acceso».",
        ],
      },
    ],
  },
  {
    slug: "6-diagnostico/14-reportes",
    title: "Reportes de Diagnóstico: catálogo, filtros, datos personales y exportación a Excel y PDF",
    audience: "Gestor",
    sectionId: "diagnostico",
    keywords: [
      "reportes",
      "reportes de diagnostico",
      "modulo de reportes",
      "exportar",
      "exportar excel",
      "exportar pdf",
      "descargar reporte",
      "datos personales",
      "ley 1581",
      "habeas data",
      "vehiculos diagnosticados",
      "consultas a fuentes",
      "derivaciones a trámite",
      "maximo de registros",
      "acota el filtro",
      "exportacion en curso",
      "registro de descargas",
    ],
    summary:
      "Los reportes detallados de tus diagnósticos: qué trae cada uno, cómo filtrar, el aviso de datos personales y cómo exportar a Excel o PDF.",
    blocks: [
      {
        id: "que-es",
        title: "1. Qué son los Reportes",
        paragraphs: [
          "«Reportes» del dock abre el módulo con la información detallada de tus diagnósticos, renglón por renglón, para revisarla en pantalla o descargarla. La cabecera dice «Descarga la información detallada de tus diagnósticos». Debajo hay una pestaña por reporte: la pestaña elegida, las fechas y los filtros quedan en la dirección de la página, salvo la placa, que no se guarda en el enlace.",
          "Para ver los reportes tu rol necesita el permiso de ver reportes; para exportarlos, además, el de descargar reportes. Los dos los tienen por defecto el usuario y el administrador de Diagnóstico. Sin el permiso de ver, la entrada «Reportes» no aparece en el dock.",
        ],
      },
      {
        id: "catalogo",
        title: "2. Los reportes disponibles",
        paragraphs: [
          "La compañía tiene cuatro reportes. El equipo FLIT tiene además «Consumo por compañía». Cada reporte cuenta por su propia fecha de referencia.",
        ],
        bullets: [
          "«Vehículos diagnosticados»: un renglón por vehículo de los diagnósticos creados en el periodo. Columnas: Fecha del diagnóstico, Diagnóstico, Modalidad, Tipo de análisis, Tipo de trámite, Placa, VIN, Servicio, Organismo de tránsito, Departamento, Propietarios (documento), Locatario (documento), Origen del locatario, Veredicto, Estado de las consultas, Saneamiento y Trámite. Filtros: modalidad, tipo de análisis, veredicto y placa.",
          "«Consultas a fuentes»: un renglón por consulta, con Fecha del diagnóstico, Diagnóstico, Placa, Fuente, Estado, Motivo no aplica, Tipo de error, Intentos, Duración total (ms), Dato obtenido en y Reutilizada. Filtros: fuente, estado y placa.",
          "«Saneamientos»: un renglón por solicitud, por la fecha de solicitud, con Solicitado en, Placa, Diagnóstico, Estado, Tomado en, Cerrado en, Días hasta el cierre, Motivo de cierre, Renglones y Renglones pendientes. Filtros: estado y placa.",
          "«Derivaciones a trámite»: un renglón por placa enviada a Trámites, con Fecha, Placa, Diagnóstico, Operación (individual o masiva), Tipo de trámite, Estado, Motivo y Trámite. Filtros: estado y placa.",
        ],
        callouts: [
          {
            variant: "info",
            text: "El documento de los propietarios y del locatario sale completo, con su tipo. Los reportes no incluyen el nombre de las personas. Si un documento no se puede leer, aparece como «No disponible».",
          },
        ],
      },
      {
        id: "datos-personales",
        title: "3. El aviso de datos personales",
        paragraphs: [
          "Encima de las pestañas hay un aviso fijo, que no se puede cerrar. En «Vehículos diagnosticados» dice: «Este reporte contiene datos personales (números de documento de identidad) protegidos por la Ley 1581 de 2012. Úsalos solo para la finalidad autorizada. Cada consulta y descarga queda registrada con tu usuario.». En los reportes que solo traen placas dice lo mismo sin la mención a los documentos, porque la placa también es un dato protegido.",
          "Cada vez que ves un reporte en pantalla o lo exportas, queda registrado quién lo hizo, cuándo, qué reporte, con qué periodo y filtros y cuántos registros recibió. El registro no guarda el contenido del reporte ni la placa que buscaste.",
        ],
        callouts: [
          {
            variant: "warning",
            title: "Tú respondes por el archivo",
            text: "Un Excel o un PDF descargado sale de FLIT y queda bajo tu responsabilidad y la de tu compañía. Descarga solo lo que necesitas, no lo compartas fuera de la finalidad autorizada y bórralo cuando ya no lo uses. El Excel trae una hoja «Aviso» y el PDF lleva el aviso y el número de registro en el pie de cada página.",
          },
        ],
      },
      {
        id: "filtros-tabla",
        title: "4. Filtros y tabla",
        paragraphs: [
          "El periodo funciona igual que en el Dashboard: «Desde» y «Hasta», los últimos 30 días por defecto y como máximo un año. Los filtros propios de cada reporte se eligen con chips; la placa se busca por su comienzo (por ejemplo ABC) y se aplica al confirmar la búsqueda. «Limpiar filtros» aparece cuando cambiaste algo y vuelve a los valores iniciales. Cambiar un filtro vuelve a la primera página.",
          "La tabla dice cuántos registros hay y se pagina de 25, 50 o 100 filas. En el celular cada renglón pasa a una tarjeta. Si avanzas mucho en las páginas verás «Acota el filtro para ver más registros»: reduce el periodo o agrega un filtro.",
        ],
        bullets: [
          "Sin datos en el periodo: «No hay registros en este periodo».",
          "Sin datos por los filtros: «Ningún registro coincide con los filtros», con «Limpiar filtros».",
          "Si no se pudo cargar: «No se pudo cargar el reporte.» con «Reintentar».",
        ],
      },
      {
        id: "exportar",
        title: "5. Exportar a Excel o PDF",
        paragraphs: [
          "Los botones «Exportar Excel» y «Exportar PDF» exportan todo el filtro, no solo la página que ves: lo dice la ayuda «Se exportan los N registros del filtro». Mientras se genera, el botón dice «Generando…» y aparece «Puede tardar unos segundos»; el otro formato sigue disponible. El archivo baja con un nombre del estilo diagnostico-reporte-vehiculos-AAAAMMDD-AAAAMMDD.",
          "Cada formato tiene un máximo de registros: 5.000 en Excel y 1.000 en PDF. Si el filtro los supera, ese botón queda atenuado con el motivo «Acota el filtro: máximo 5.000 registros en Excel» (o el de PDF). Nunca se exporta un archivo recortado en silencio.",
        ],
        bullets: [
          "Botones atenuados con «No hay registros para exportar» o «Espera a que cargue el reporte»: la tabla está vacía o todavía cargando.",
          "«El reporte tiene N registros y el Excel admite 5.000. Acota el rango o los filtros.»: los datos crecieron entre que cargó la tabla y pediste el archivo.",
          "«Ya hay una exportación en curso. Espera unos segundos e intenta de nuevo.»: solo se genera un archivo a la vez por usuario, y la plataforma limita las exportaciones simultáneas.",
          "«No tienes permiso para descargar reportes.»: tu rol ve los reportes pero no los descarga.",
          "«Revisa el periodo: como máximo un año, sin fechas futuras y con la fecha final igual o posterior a la inicial» o «Revisa los filtros del reporte.»: corrige el periodo o los filtros.",
        ],
      },
    ],
  },
  {
    slug: "6-diagnostico/15-vista-global-flit",
    title: "Vista global de FLIT: Dashboard y Reportes de todas las compañías",
    audience: "Super Admin",
    sectionId: "diagnostico",
    keywords: [
      "vista global",
      "todas las companias",
      "selector de compania",
      "companias con mas uso",
      "consumo por compania",
      "reporte de consumo",
      "estadisticas globales",
      "dashboard global",
      "reportes globales",
      "volumetria",
    ],
    summary:
      "Cómo el equipo FLIT ve el Dashboard y los Reportes de todas las compañías o de una sola, y qué trae el reporte «Consumo por compañía».",
    blocks: [
      {
        id: "quien",
        title: "1. Quién tiene la vista global",
        paragraphs: [
          "La vista global es del equipo FLIT con el permiso de volumetría de Diagnóstico. Solo cuenta las compañías que tienen Diagnóstico activo. Un usuario de una compañía cliente nunca la ve, aunque sea administrador.",
          "Cada lectura de otra compañía queda auditada, igual que en el backoffice.",
        ],
      },
      {
        id: "dashboard",
        title: "2. El Dashboard global",
        paragraphs: [
          "El Dashboard abre en «Todas las compañías», con el rótulo «Vista global · todas las compañías» bajo el título. Además de los indicadores de siempre muestra «Compañías con más uso»: hasta 10 compañías ordenadas por diagnósticos creados en el periodo, con sus vehículos. Pulsa una para ver su resumen.",
          "El selector «Compañía», junto a las fechas, busca por nombre entre las compañías con Diagnóstico activo. Al elegir una, la cabecera dice «Resumen de diagnósticos de» y su nombre, y desaparece la lista de compañías. Para volver al total elige «Todas las compañías». En la vista global no está el botón «Nuevo diagnóstico»: los diagnósticos los crea la compañía.",
        ],
        bullets: [
          "Si la lista de compañías no carga, el selector queda deshabilitado con «No se pudo cargar la lista de compañías» y «Reintentar»; la vista global sigue funcionando.",
          "Si la compañía elegida ya no tiene Diagnóstico activo, o el directorio de compañías no responde, el resumen no carga: pulsa «Reintentar» o elige otra compañía.",
          "En «Reportes» esos casos tienen mensaje propio: «Esa compañía no tiene Diagnóstico activo.» y «El directorio de compañías no responde. Intenta de nuevo en unos segundos.».",
        ],
      },
      {
        id: "reportes",
        title: "3. Los Reportes globales",
        paragraphs: [
          "En «Reportes» tienes el mismo selector «Compañía», con «Todas las compañías» por defecto. En la vista de todas las compañías cada renglón empieza con la columna «Compañía», para saber de quién es el dato. Los reportes traen los mismos datos personales que ve la compañía, con el mismo aviso, y cada consulta y descarga queda registrada con tu usuario. En «Consultas a fuentes» ves además la columna «Origen» (automática o manual).",
          "«Consumo por compañía» es solo de FLIT: una fila por compañía con datos, con Compañía, NIT, Diagnósticos, Vehículos, Consultas exitosas, Consultas con error, Tasa de éxito, consultas RUNT, SIMIT, avalúo e impuesto, Saneamientos solicitados y Trámites creados. No trae datos personales: su aviso solo recuerda que cada descarga queda registrada.",
        ],
      },
    ],
  },
];
