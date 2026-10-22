using Flit.Consultas.Grpc.V1;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// RUES real (HU #13792, F05-C9, decisión Q2): <c>ConsultasService.ConsultarRues</c> de core-consultas (proveedor
/// <c>verifik_rues</c>) por <see cref="ClienteConsultas"/>, con el NIT sin dígito de verificación. Normaliza a
/// <see cref="ResultadoRues"/> leyendo SOLO el estado y la fecha de renovación: de la primera matrícula mercantil certificada
/// (<c>estado</c>, <c>renovada_en</c>) y, si no viene, de los campos <c>rues_estado</c>, <c>rues_fecha_renovacion</c> o
/// <c>rues_ultimo_ano_renovado</c>. Nunca lee la razón social, el NIT, la dirección ni los representantes.
/// </summary>
public sealed class FuenteRuesReal(ClienteConsultas cliente) : IFuenteRues
{
    public async Task<RespuestaFuente<ResultadoRues>> ConsultarAsync(Guid empresa, string nit, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nit);
        var solicitud = new ConsultarRuesRequest { Opciones = new OpcionesConsulta(), Nit = nit };
        var respuesta = await cliente.LlamarAsync(empresa, (c, o) => c.ConsultarRuesAsync(solicitud, o), ct).ConfigureAwait(false);
        if (respuesta.Error is { } errorGrpc)
            return RespuestaFuente<ResultadoRues>.Fallo(errorGrpc);

        var resultado = respuesta.Respuesta!.Resultado;
        var servicio = TraductorErroresConsultas.Servicio(resultado?.Proveedor);
        if (TraductorErroresConsultas.Traducir(resultado) is { } error)
            return RespuestaFuente<ResultadoRues>.Fallo(error, servicio);

        return RespuestaFuente<ResultadoRues>.Exito(Normalizar(resultado!), servicio, null);
    }

    internal static ResultadoRues Normalizar(ResultadoConsulta resultado)
    {
        var matricula = resultado.Certificaciones?.MatriculasMercantiles.FirstOrDefault();
        var estado = Texto(matricula?.Estado?.Crudo) ?? Vigencia(matricula?.Estado) ?? Campo(resultado, "rues_estado");
        var renovacion = Texto(matricula?.RenovadaEn?.Valor) ?? Campo(resultado, "rues_fecha_renovacion") ?? Campo(resultado, "rues_ultimo_ano_renovado");
        return new ResultadoRues(estado is not null || renovacion is not null, estado?.ToUpperInvariant(), renovacion);
    }

    private static string? Vigencia(EstadoVigencia? estado) => estado?.Valor switch
    {
        Flit.Consultas.Grpc.V1.Vigencia.Vigente => "ACTIVA",
        Flit.Consultas.Grpc.V1.Vigencia.Vencido => "VENCIDA",
        _ => null,
    };

    private static string? Texto(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static string? Campo(ResultadoConsulta resultado, string clave) =>
        resultado.Campos.FirstOrDefault(c => c.Clave == clave && c.HasValorTexto) is { } campo ? Texto(campo.ValorTexto) : null;
}
