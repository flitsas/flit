using System.Security.Cryptography;
using System.Text.Json;
using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Infrastructure.PersonaNatural;
using Flit.Tramites.Application.UseCases.ProcedureTypes;
using Flit.Tramites.Domain.Tramites.PersonaNatural;
using Flit.Tramites.Domain.Tramites.ValueObjects;
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
        foreach (var doc in matriz.Where(m => !m.EsDummy && !m.EsGeneradoSistema && !DefinicionPersonaNatural.DocumentoNoAplicaAPersonaNatural(m.Codigo)).OrderBy(m => m.OrdenResuelto))
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
        foreach (var extra in adicionales.Where(a => !DefinicionPersonaNatural.DocumentoNoAplicaAPersonaNatural(a.DocCode) && !definicion.Documentos.Any(d => string.Equals(d.DocCode, a.DocCode, StringComparison.OrdinalIgnoreCase))))
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

        // Un tipo prendario de acción única exige el documento de la decisión (PrendaGate): el portal debe pedirlo aunque la matriz
        // del tipo solo traiga otro código (paz_salvo_prenda / inscripcion_prenda). Los de la matriz no se quitan.
        if (DefinicionPersonaNatural.AccionPrenda(tipo.Codigo) is { } accionPrenda
            && PrendaDecision.DocTipoFor(accionPrenda) is { } docDecision
            && !definicion.Documentos.Any(d => string.Equals(d.DocCode, docDecision, StringComparison.OrdinalIgnoreCase)))
        {
            definicion.Documentos.Add(new DocumentoRequerido
            {
                DocCode = docDecision,
                Nombre = accionPrenda == PrendaDecision.Levantar ? "Documento del levantamiento de la prenda" : "Documento de registro de la prenda",
                Obligatorio = true,
                Orden = siguiente++,
                InstruccionCargue = string.Empty,
                Condicion = string.Empty,
            });
        }

        var cap = DefinicionPersonaNatural.Capacidades(tipo.Perfil, tipo.Familia, tipo.Codigo);

        // Un tipo con prenda complementaria (p. ej. traspaso) deja decidir registrar o levantar; PrendaGate exige el documento exacto de la
        // decisión. Se ofrecen los dos, condicionados: el BFF solo reclama el de la decisión guardada.
        if (cap.PermitePrendaComplementaria && DefinicionPersonaNatural.AccionPrenda(tipo.Codigo) is null)
        {
            foreach (var (codigo, nombre, condicion) in new[]
            {
                ("prenda_registro", "Documento de registro de la prenda", "Se pide si se registra una prenda."),
                ("prenda_levantamiento", "Documento del levantamiento de la prenda", "Se pide si se levanta la prenda."),
            })
            {
                if (definicion.Documentos.Any(d => string.Equals(d.DocCode, codigo, StringComparison.OrdinalIgnoreCase))) continue;
                definicion.Documentos.Add(new DocumentoRequerido
                {
                    DocCode = codigo, Nombre = nombre, Obligatorio = true, Orden = siguiente++, InstruccionCargue = string.Empty, Condicion = condicion,
                });
            }
        }

        definicion.Flags = new FlagsTipo
        {
            EntryMode = cap.EntryMode,
            RequiereComercial = cap.RequiereComercial,
            RequierePrenda = cap.RequierePrenda,
            PermiteTransformaciones = cap.PermiteTransformaciones,
            PermitePrendaComplementaria = cap.PermitePrendaComplementaria,
            RequiereBiometria = cap.RequiereBiometria,
            RequiereSolicitudPlaca = cap.RequiereSolicitudPlaca,
            RequiereDestino = cap.RequiereDestino,
            OrganismoLoEligeOperador = cap.OrganismoLoEligeOperador,
            AccionPrenda = cap.AccionPrenda ?? string.Empty,
        };

        // HU 4 — declaraciones del tipo (valor nuevo, blindaje, causal, destino, simultáneos) con las constantes de FLIT 2.
        foreach (var d in DeclaracionesPersonaNatural.Para(tipo.Perfil, tipo.Familia, tipo.Codigo))
            definicion.Declaraciones.Add(Declaracion(d));

        definicion.Huella = Huella(definicion);
        return definicion;
    }

    internal static DeclaracionTipo Declaracion(DeclaracionPortal d)
    {
        var salida = new DeclaracionTipo
        {
            Clave = d.Clave,
            Control = d.Control,
            Etiqueta = d.Etiqueta,
            Obligatoria = d.Obligatoria,
            FieldKey = d.FieldKey,
            DocCodeSoporte = d.DocCodeSoporte,
            SoporteObligatorio = d.SoporteObligatorio,
            Catalogo = d.Catalogo,
            ActivadaPor = d.ActivadaPor,
        };
        salida.FieldKeysExtra.AddRange(d.FieldKeysExtra);
        foreach (var o in d.Opciones)
        {
            var opcion = new OpcionDeclaracion { Codigo = o.Codigo, Etiqueta = o.Etiqueta, ExigeSoporte = o.ExigeSoporte };
            opcion.DocCodes.AddRange(o.DocCodes);
            salida.Opciones.Add(opcion);
        }

        return salida;
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
