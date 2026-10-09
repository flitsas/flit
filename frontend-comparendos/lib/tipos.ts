// Contratos de la API de Comparendos (services/core-comparendos). JSON en camelCase; los enumerados llegan como texto
// con su nombre (p. ej. estadoLegal "EnTiempo").

export interface Pagina<T> {
  items: T[];
  total: number;
  paginaActual: number;
  tamanoPagina: number;
}

export interface FilaBandeja {
  id: string;
  numero: string;
  placa: string | null;
  nitConsultado: string | null;
  titular: string | null;
  organismoTransitoId: string | null;
  organismo: string | null;
  secretariaTexto: string | null;
  fechaImposicion: string | null;
  fechaNotificacion: string | null;
  valorTotal: number | null;
  fuente: string;
  fuentePrincipal: string | null;
  estadoLegal: string;
  diasHabilesRestantes: number | null;
  proximoAVencer: boolean;
  ventanaDescuento: string;
  finDescuentoVigente: string | null;
  estadoProceso: string;
  estadoGestion: string;
  contraventorNombre: string | null;
  estadoConsultaContraventor: string;
  ultimoAviso: string;
  cambioContraventor: string;
  dpRadicado: string | null;
  dpEstado: string | null;
  dpResultado: string | null;
  pagado: boolean;
  activo: boolean;
  motivoInactivacion: string | null;
  enCobroCoactivo: boolean;
  esFotomulta: boolean;
  ultimoEventoEn: string;
  creadoEn: string;
}

export interface Contadores {
  total: number;
  proximosAVencer: number;
  sinContraventor: number;
  rechazadosOt: number;
}

export interface EmpresaResumen {
  id: string;
  nombre: string | null;
}

export interface Nit {
  id: string;
  tipoDocumento: string;
  numeroBase: string;
  digitoVerificacion: number | null;
  numero: string;
  nombreTitular: string | null;
  activo: boolean;
  creadoEn: string;
}

export interface PlacaMonitoreada {
  id: string;
  placa: string;
  activo: boolean;
  origenRegistro: string;
  creadoEn: string;
}

export interface Organismo {
  id: string;
  codigo: string;
  nombre: string;
  municipio: string | null;
  departamento: string | null;
  correosRadicacion: string[];
  permiteCambioContraventor: boolean;
  activoPlataforma: boolean;
}

export interface EventoTimeline {
  id: string;
  tipo: string;
  ocurridoEn: string;
  actorTipo: string;
  actorNombre: string;
  descripcion: string;
  datos: string | null;
  relacionadoTipo: string | null;
  relacionadoId: string | null;
}

export interface CambioCampo {
  canal: string;
  campo: string;
  valorAnterior: string | null;
  valorNuevo: string | null;
  aplicadoAMaestro: boolean;
  registradoEn: string;
}

export interface Contraventor {
  tipoDocumento: string;
  numeroDocumento: string;
  nombres: string;
  correo: string | null;
  telefono: string | null;
  direccion: string | null;
  ciudad: string | null;
  sucursal: string | null;
  tipoContrato: string | null;
  origen: string;
  creadoEn: string;
}

export interface DetalleComparendo {
  id: string;
  tenantId: string;
  numero: string;
  placa: string | null;
  estadoProceso: string;
  situacion: {
    estadoLegal: string;
    diasHabilesRestantes: number | null;
    proximoAVencer: boolean;
    ventanaDescuento: string;
    finDescuentoVigente: string | null;
    diasHabilesDescuento: number | null;
  };
  plazos: { limiteGestion: string | null; limite50: string | null; limite25: string | null; caducidad: string | null; prescripcion: string | null };
  fuente: string;
  tipoIngreso: string;
  fuentePrincipal: string | null;
  vehiculo: { placa: string | null; tipoVehiculo: string | null; servicioVehiculo: string | null };
  compania: {
    nitConsultado: string | null;
    titular: string | null;
    tipoDocumentoInfractor: string | null;
    numeroDocumentoInfractor: string | null;
    nitMonitoreado: { id: string; numeroBase: string; digitoVerificacion: number | null; nombreTitular: string | null } | null;
    placaMonitoreada: string | null;
  };
  infraccion: {
    codigo: string | null;
    descripcion: string | null;
    direccion: string | null;
    fechaImposicion: string | null;
    horaImposicion: string | null;
    fechaNotificacion: string | null;
    esFotomulta: boolean | null;
  };
  organismo: { id: string; codigo: string; nombre: string; municipio: string | null; departamento: string | null; tieneCorreo: boolean } | null;
  secretariaTexto: string | null;
  valores: { valorCapital: number | null; valorAdicional: number | null; valorTotal: number | null };
  estadoFuente: string | null;
  enCobroCoactivo: boolean;
  evidencia: { archivoEvidenciaId: string | null; fotoEvidenciaUrl: string | null };
  activo: boolean;
  motivoInactivacion: string | null;
  detalleInactivacion: string | null;
  inactivadoEn: string | null;
  estadoGestion: string;
  estadoNotificacionConductor: string;
  estadoCambioContraventor: string;
  estadoConsultaContraventor: string;
  notificadoFisicamente: boolean;
  pagado: boolean;
  tieneDp: boolean;
  gestionado: boolean;
  contraventor: Contraventor | null;
  historialContraventor: { id: string; origen: string; motivo: string | null; datosAnteriores: string | null; datosNuevos: string; registradoEn: string }[];
  documentos: { id: string; tipo: string; vigente: boolean; creadoEn: string; archivoId: string; nombre: string; contentType: string }[];
  solicitudesCambio: {
    id: string;
    numeroIntento: number;
    estado: string;
    causalRechazo: string | null;
    fechaRespuestaOt: string | null;
    solicitadoEn: string;
    respondidoEn: string | null;
    formatoArchivoId: string;
    soporteRespuestaArchivoId: string | null;
  }[];
  avisos: { id: string; tipo: string; disparo: string; ventana: string; porcentajeDescuento: number; correoDestino: string; registradoEn: string; comunicacionId: string }[];
  dp: {
    id: string;
    radicado: string;
    tipologia: { codigo: string; nombre: string } | null;
    estado: string;
    fechaRadicacion: string | null;
    fechaLimiteRespuestaOt: string | null;
    correosDestino: string[];
    documentoArchivoId: string;
    comunicacionId: string | null;
    respuesta: {
      sentido: string;
      origen: string | null;
      respuestaRadicadoOt: string | null;
      respuestaFecha: string | null;
      respuestaArchivoId: string | null;
      respuestaRegistradaEn: string | null;
    } | null;
  } | null;
  pago: { tipo: string; valorPagado: number; fechaPago: string; comprobanteArchivoId: string | null; origen: string; registradoEn: string } | null;
  fuentes: { canal: string; municipioCodigo: string | null; presente: boolean; primeraVezEn: string; ultimaVezVistaEn: string; ausenteDesde: string | null }[];
  creadoEn: string;
  ultimoEventoEn: string;
}

export interface Trabajo {
  tipo: string;
  expresionCron: string;
  siguiente: string | null;
  activo: boolean;
  soloDiasHabiles: boolean;
  ultimaEjecucionEn: string | null;
  ultimoResultado: string | null;
}

export interface Ciclo {
  id: string;
  canal: string;
  disparo: string;
  empresaManual: string | null;
  inicioEn: string;
  finEn: string | null;
  clavesTotal: number;
  clavesError: number;
  resultado: string;
  mensajeError: string | null;
}

export interface LogMigracion {
  id: string;
  cicloId: string;
  empresaId: string;
  empresa: string | null;
  canal: string;
  clave: string | null;
  municipio: string | null;
  inicioEn: string;
  obtenidas: number;
  nuevas: number;
  actualizadas: number;
  sinCambios: number;
  duplicadasOtraFuente: number;
  inactivadas: number;
  reactivadas: number;
  dpFavorableAutomatico: number;
  resultado: string;
  mensajeError: string | null;
}

export interface LogDeCiclo {
  id: string;
  empresaId: string;
  empresa: string | null;
  clave: string | null;
  municipioCodigo: string | null;
  cantidadObtenidas: number;
  cantidadNuevas: number;
  cantidadActualizadas: number;
  cantidadSinCambios: number;
  cantidadDuplicadasOtraFuente: number;
  cantidadInactivadas: number;
  cantidadReactivadas: number;
  cantidadDpFavorableAutomatico: number;
  resultado: string;
  mensajeError: string | null;
}

export interface ConsultaDeCiclo {
  id: string;
  claveConsultada: string;
  municipioCodigo: string | null;
  httpStatus: number | null;
  resultado: string;
  tipoError: string | null;
  mensajeError: string | null;
  cantidadItems: number;
  duracionMs: number;
  tieneRespuesta: boolean;
}

export interface DetalleCiclo {
  ciclo: Ciclo;
  logs: LogDeCiclo[];
  consultas: ConsultaDeCiclo[];
}

export interface ConfigAlertas {
  alertasHabilitadas: boolean;
  umbralFallosCiclo: number;
  plantillaAsunto: string;
  remitenteNombre: string;
}

export interface DestinatarioAlerta {
  id: string;
  correo: string;
  nombre: string;
  tipoAlerta: string;
  activo: boolean;
}

export interface Municipio {
  id: string;
  codigoFuente: string;
  nombre: string;
  organismoTransitoId: string | null;
  activo: boolean;
}

export interface AliasOt {
  id: string;
  texto: string;
  organismoTransitoId: string;
  organismo: string;
  origen: string;
}

export interface Exportacion {
  id: string;
  totalFilas: number | null;
  estado: string;
  creadoEn: string;
  terminadoEn: string | null;
  expiraEn: string | null;
}
