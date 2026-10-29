using System.Security.Cryptography;
using System.Text.Json;
using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Infrastructure.PersonaNatural;
using Flit.Tramites.Application.UseCases.ProcedureTypes;
using Flit.Tramites.Domain.Tramites.PersonaNatural;
using Flit.Tramites.Grpc.V1;
using Google.Protobuf;

namespace Flit.Api.Grpc;

/// <summary>
/// HU #14078 (Feature K, Épica #14021) — arma la <see cref="DefinicionTipoTramite"/> que consume el portal a partir de lo que
/// FLIT 2 ya resolvió: pasos y campos (<c>GetProcedureStepsHandler</c>), matriz documental con precedencia organismo sobre
/// defecto (<c>IResolvedDocumentMatrixResolver</c>) y perfil de capacidades (<see cref="DefinicionPersonaNatural"/>). Es una
/// función pura: no consulta nada ni evalúa reglas, solo proyecta a nombres del contrato. El perfil crudo no sale.
/// </summary>
internal static class PersonaNaturalDefinicionMapper
{
    public static DefinicionTipoTramite Armar(
        TipoPersonaNatural tipo,
        IReadOnlyList<ProcedureStepDto> pasos,
        IReadOnlyList<ResolvedDocumentMatrixItem> matriz)
    {
        ArgumentNullException.ThrowIfNull(tipo);
        ArgumentNullException.ThrowIfNull(pasos);
        ArgumentNullException.ThrowIfNull(matriz);

        var definicion = new DefinicionTipoTramite
        {
            Codigo = tipo.Codigo,
            Nombre = tipo.Nombre,
            Familia = tipo.Familia,
            Version = tipo.Version,
            DisponiblePn = tipo.Elegibilidad.Elegible,
            MotivoNoDisponible = tipo.Elegibilidad.Motivo,
        };

        // Pasos activos en el orden del tipo; los campos bloqueados (precargados por consulta) no los digita el ciudadano.
        foreach (var paso in pasos.Where(p => p.IsActive).OrderBy(p => p.SortOrder))
        {
            var secciones = paso.Sections.OrderBy(s => s.SortOrder).ToList();
            var salida = new Paso { Clave = paso.Code, Titulo = paso.Title };
            salida.SectionTypes.AddRange(secciones.Select(s => s.SectionType).Distinct(StringComparer.Ordinal));
            foreach (var campo in secciones.SelectMany(s => s.FormFields).Where(f => !f.IsLocked).OrderBy(f => f.SortOrder))
            {
                var c = new Campo
                {
                    FieldKey = campo.FieldKey,
                    Etiqueta = campo.Label,
                    Tipo = campo.FieldType,
                    Obligatorio = campo.IsRequired,
                    ValidationSchema = campo.ValidationSchema ?? "{}",
                };
                c.Opciones.AddRange(Opciones(campo.Options));
                salida.Campos.Add(c);
            }

            definicion.Pasos.Add(salida);
        }

        foreach (var actor in DefinicionPersonaNatural.Actores(tipo.Perfil, tipo.Elegibilidad.Elegible))
        {
            definicion.Actores.Add(new ActorRol
            {
                Rol = actor.Rol,
                Etiqueta = actor.Etiqueta,
                Min = actor.Min,
                Max = actor.Max,
                Biometria = actor.Biometria,
                Firma = actor.Firma,
                PersonaNaturalPermitida = actor.PersonaNaturalPermitida,
            });
        }

        // El portal solo pide lo que el ciudadano carga: ni los buzones informativos ni lo que el sistema genera.
        var (porDocumento, adicionales) = DefinicionPersonaNatural.CondicionesDocumentales(tipo.Codigo);
        foreach (var doc in matriz.Where(m => !m.EsDummy && !m.EsGeneradoSistema).OrderBy(m => m.OrdenResuelto))
        {
            definicion.Documentos.Add(new DocumentoRequerido
            {
                DocCode = doc.Codigo,
                Nombre = doc.Nombre,
                Obligatorio = doc.Obligatorio,
                Orden = doc.OrdenResuelto,
                InstruccionCargue = doc.InstruccionCargue ?? string.Empty,
                Condicion = porDocumento.GetValueOrDefault(doc.Codigo) ?? string.Empty,
            });
        }

        var siguiente = definicion.Documentos.Count == 0 ? 1 : definicion.Documentos.Max(d => d.Orden) + 1;
        foreach (var extra in adicionales.Where(a => !definicion.Documentos.Any(d => string.Equals(d.DocCode, a.DocCode, StringComparison.OrdinalIgnoreCase))))
        {
            definicion.Documentos.Add(new DocumentoRequerido
            {
                DocCode = extra.DocCode,
                Nombre = extra.Nombre,
                Obligatorio = extra.Obligatorio,
                Orden = siguiente++,
                InstruccionCargue = string.Empty,
                Condicion = extra.Condicion,
            });
        }

        var cap = DefinicionPersonaNatural.Capacidades(tipo.Perfil, tipo.Familia);
        definicion.Flags = new FlagsTipo
        {
            EntryMode = cap.EntryMode,
            RequiereComercial = cap.RequiereComercial,
            RequierePrenda = cap.RequierePrenda,
            PermiteTransformaciones = cap.PermiteTransformaciones,
            PermitePrendaComplementaria = cap.PermitePrendaComplementaria,
            RequiereBiometria = cap.RequiereBiometria,
            RequiereSolicitudPlaca = cap.RequiereSolicitudPlaca,
        };

        definicion.Huella = Huella(definicion);
        return definicion;
    }

    /// <summary>SHA-256 del mensaje sin la huella: cambia si cambia cualquier paso, campo, actor, documento o flag.</summary>
    internal static string Huella(DefinicionTipoTramite definicion)
    {
        var copia = definicion.Clone();
        copia.Huella = string.Empty;
        return Convert.ToHexString(SHA256.HashData(copia.ToByteArray())).ToLowerInvariant();
    }

    /// <summary>Las opciones de un campo se guardan como JSON; se aceptan textos y objetos con <c>value</c> o <c>label</c>.</summary>
    internal static IEnumerable<string> Opciones(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            yield break;

        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                yield break;

            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var texto = e.ValueKind switch
                {
                    JsonValueKind.String => e.GetString(),
                    JsonValueKind.Object when e.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String => v.GetString(),
                    JsonValueKind.Object when e.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String => l.GetString(),
                    _ => null,
                };
                if (!string.IsNullOrWhiteSpace(texto))
                    yield return texto;
            }
        }
    }
}
