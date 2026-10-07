using System.Text;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;

namespace Flit.Ict.Application.Register;

/// <summary>
/// Normaliza una fila del payload v1 a las entidades del pre-trámite (master + actores),
/// aplanando seller/buyer/lessee a <c>external_integration_actors</c>. Lógica pura (testeable).
/// </summary>
public static class IctPayloadNormalizer
{
    public static ExternalIntegrationMaster ToMaster(RegisterRowInput row, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(row);

        var managerIdTransaction = string.IsNullOrWhiteSpace(row.ManagerIdTransaction)
            ? Guid.NewGuid().ToString("N")[..20]
            : row.ManagerIdTransaction.Trim();

        var master = new ExternalIntegrationMaster
        {
            TenantId = tenantId,
            CompanyManagerDocument = row.CompanyManagerDocument?.Trim() ?? string.Empty,
            ManagerUser = row.ManagerUser?.Trim() ?? string.Empty,
            ManagerMail = row.ManagerMail?.Trim() ?? string.Empty,
            DeliveryAddress = row.DeliveryAddress?.Trim() ?? string.Empty,
            ManagerIdTransaction = managerIdTransaction,
            TransactionOperation = row.TransactionOperation,
            TransactionType = row.TransactionType,
            Plate = row.Plate?.Trim().ToUpperInvariant() ?? string.Empty,
            Vin = row.Vin?.Trim().ToUpperInvariant(),
            TrafficSecretaryCode = row.TrafficSecretaryCode?.Trim() ?? string.Empty,
            SellingDate = row.SellingDate?.Trim() ?? string.Empty,
            SellingPrice = row.SellingPrice ?? 0m,
            UrlWebHook = row.UrlWebHook?.Trim() ?? string.Empty,
            ProcessWithoutAttachedDocuments = row.ProcessWithoutAttachedDocuments,

            // Banderas de comportamiento del contrato v1.
            Priority = row.Priority,
            TransactionFlit = Clean(row.TransactionFlit),
            StartsProcedureInPaused = row.StartsProcedureInPaused,
            ObservationWhenPaused = Clean(row.ObservationWhenPaused),
            SendAutomaticTrafficSecretary = row.SendAutomaticTrafficSecretary,
            // Bug #13109 punto 6: en traspaso (3, 4) el campo no aplica (el vehículo ya tiene placa): se
            // ignora lo que envíe el cliente y se persiste 0. En matrícula se conserva.
            PlateAssignmentType = RegisterRowValidator.IsTraspaso(row.TransactionType)
                ? (short)0
                : row.PlateAssignmentType,

            // Compañía relacionada (servicio público).
            RelatedCompanyDocument = Clean(row.RelatedCompanyDocument),
            RelatedCompanyName = Clean(row.RelatedCompanyName),

            // Limitación / garantía mobiliaria (prenda).
            LimitationsOperationType = row.LimitationsOperationType,
            LimitationsCreditor = Clean(row.LimitationsCreditor),
            LimitationsCreditorDocumentType = Clean(row.LimitationsCreditorDocumentType),
            LimitationsCreditorDocumentNumber = Clean(row.LimitationsCreditorDocumentNumber),
            LimitationsInscriptionDate = Clean(row.LimitationsInscriptionDate),

            // Otros trámites.
            ArmorLevelNumberId = row.ArmorLevelNumberId,
            NewVehicleFuelType = row.NewVehicleFuelType,

            ProcessStatusId = 1,
            BusinessValidation = 0,
            ExternalValidation = 0,
        };

        AddActors(master, tenantId, row.Seller, "seller");
        AddActors(master, tenantId, row.Buyer, "buyer");
        AddActors(master, tenantId, row.Lessee, "lessee");
        AddTransformations(master, tenantId, row.MoreTransactionTransactionType);
        return master;
    }

    /// <summary>Códigos RUNT de transformación del catálogo (12-ICT-catalogs-parity.sql).</summary>
    private static readonly int[] KnownTransformationCodes = [5, 9, 17];

    /// <summary>
    /// Aplana <c>more_transaction_transaction_type</c> al puente master↔transformación. Se ignoran
    /// los códigos fuera del catálogo (la FK los rechazaría y tumbaría el registro completo) y los
    /// repetidos (la PK es compuesta por master + código).
    /// TODO(ICT-TRANSFORM-VALIDATION): v1 validaba el código en el SP de negocio como NOVEDAD
    /// (business.sql reglas 85/86); aquí se descartan en silencio hasta portar esas reglas.
    /// </summary>
    private static void AddTransformations(
        ExternalIntegrationMaster master,
        Guid tenantId,
        IReadOnlyList<RegisterTransformationInput>? transformations)
    {
        if (transformations is null)
        {
            return;
        }

        var seen = new HashSet<int>();
        foreach (var t in transformations)
        {
            if (!KnownTransformationCodes.Contains(t.TransactionType) || !seen.Add(t.TransactionType))
            {
                continue;
            }

            master.Transformations.Add(new ExternalIntegrationMasterTransformation
            {
                TenantId = tenantId,
                IdTransformationType = t.TransactionType,
                Description = t.Description?.Trim() ?? string.Empty,
            });
        }
    }

    /// <summary>
    /// Normaliza un NIT para compararlo de forma TOLERANTE entre el payload y el token: descarta el
    /// dígito de verificación (lo que va tras el primer '-', p. ej. <c>"900123456-1" → "900123456"</c>) y
    /// los separadores de miles (puntos y espacios). Así <c>"900123456"</c>, <c>"900.123.456"</c> y
    /// <c>"900123456-1"</c> se consideran la MISMA compañía. Devuelve solo los dígitos de la base
    /// (<c>"900.123.456-1" → "900123456"</c>). Cadena vacía/espacios → <c>string.Empty</c>.
    /// </summary>
    public static string NormalizeNit(string? nit)
    {
        if (string.IsNullOrWhiteSpace(nit))
        {
            return string.Empty;
        }

        var value = nit.Trim();
        var dashIndex = value.IndexOf('-');
        if (dashIndex >= 0)
        {
            value = value[..dashIndex];
        }

        return new string(value.Where(char.IsAsciiDigit).ToArray());
    }

    /// <summary>Clave de deduplicación intra-lote por tipo de trámite (v1 findDuplicatesInBatch, extendida a 5-16).</summary>
    /// <remarks>
    /// Delega en <see cref="PreTramiteClave.Texto"/>: el duplicado contra lo ya guardado (Bug #13109, punto 7)
    /// usa la misma clave, así las dos barreras no pueden divergir.
    /// </remarks>
    public static string DedupKey(RegisterRowInput row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return PreTramiteClave.Texto(row.TransactionType, row.Plate, row.Vin);
    }

    private static void AddActors(
        ExternalIntegrationMaster master,
        Guid tenantId,
        IReadOnlyList<RegisterActorInput>? actors,
        string actorType)
    {
        if (actors is null)
        {
            return;
        }

        foreach (var a in actors)
        {
            var actor = new ExternalIntegrationActor
            {
                TenantId = tenantId,
                ActorType = actorType,
                // Bug #13304: se guarda (y viaja a core-api) el CÓDIGO del catálogo, no el alias recibido.
                DocumentType = NormalizeDocumentType(a.DocumentType) ?? a.DocumentType?.Trim() ?? string.Empty,
                DocumentNumber = a.DocumentNumber?.Trim() ?? string.Empty,
                Name = a.Name?.Trim() ?? string.Empty,
                FirstLastName = a.FirstLastName?.Trim() ?? string.Empty,
                SecondLastName = a.SecondLastName?.Trim(),
                Phone = NormalizePhone(a.Phone),
                Email = a.Email?.Trim() ?? string.Empty,
                City = a.City?.Trim(),
                State = a.State?.Trim(),
                Address = a.Address?.Trim(),
                ExpeditionDate = a.ExpeditionDate?.Trim(),
            };

            // Representante legal y mandante del actor (contrato v1): se aplanan en la misma fila,
            // que ya tiene las columnas legal_representative_* / principal_mandante_*.
            var rl = a.LegalRepresentative;
            if (rl is not null)
            {
                actor.LegalRepresentativeDocumentType = Clean(rl.DocumentType);
                actor.LegalRepresentativeDocumentNumber = Clean(rl.DocumentNumber);
                actor.LegalRepresentativeName = Clean(rl.Name);
                actor.LegalRepresentativeFirstLastName = Clean(rl.FirstLastName);
                actor.LegalRepresentativeSecondLastName = Clean(rl.SecondLastName);
                actor.LegalRepresentativePhone = Clean(rl.Phone);
                actor.LegalRepresentativeEmail = Clean(rl.Email);
                actor.LegalRepresentativeCity = Clean(rl.City);
                actor.LegalRepresentativeState = Clean(rl.State);
                actor.LegalRepresentativeAddress = Clean(rl.Address);
            }

            var pm = a.PrincipalMandante;
            if (pm is not null)
            {
                actor.PrincipalMandanteDocumentType = Clean(pm.DocumentType);
                actor.PrincipalMandanteDocumentNumber = Clean(pm.DocumentNumber);
                actor.PrincipalMandanteName = Clean(pm.Name);
                actor.PrincipalMandanteFirstLastName = Clean(pm.FirstLastName);
                actor.PrincipalMandanteSecondLastName = Clean(pm.SecondLastName);
                actor.PrincipalMandanteEmail = Clean(pm.Email);
            }

            master.Actors.Add(actor);
        }
    }

    /// <summary>
    /// Tope del nombre completo del actor en core-api (<c>procedure_instance_actors.full_name varchar(320)</c>).
    /// Con los topes por parte (name/first_last_name/second_last_name ≤ 100) el armado llega como mucho a 302.
    /// </summary>
    public const int MaxActorFullNameLength = 320;

    /// <summary>
    /// Tope del teléfono del actor en core-api (<c>procedure_instance_actors.phone varchar(50)</c>); coincide con
    /// la columna de ICT (<c>external_integration_actors.phone varchar(50)</c>).
    /// </summary>
    public const int MaxActorPhoneLength = 50;

    /// <summary>Códigos de documento que core-api acepta para un actor (ActorsCommand.ValidDocumentTypes).</summary>
    public static readonly IReadOnlyList<string> DocumentTypeCodes = ["CC", "CE", "NIT", "PAS", "TI"];

    /// <summary>
    /// Alias habituales → código del catálogo (Bug #13304). La clave ya viene normalizada: mayúsculas, sin
    /// tildes, sin puntos/guiones y con espacios colapsados (ver <see cref="NormalizeDocumentType"/>). Las
    /// letras sueltas (C/E/N/P/T) son el código RUNT que ya usa el proyecto (KyverumRuntDocType). NO se
    /// aceptan códigos numéricos: el proyecto no tiene una convención numérica propia para el tipo de
    /// documento (la de Quipux, C=2, choca con un «1 = CC»), así que un número se rechaza.
    /// </summary>
    private static readonly Dictionary<string, string> DocumentTypeAliases = new(StringComparer.Ordinal)
    {
        ["CC"] = "CC",
        ["C"] = "CC",
        ["CEDULA"] = "CC",
        ["CEDULA DE CIUDADANIA"] = "CC",
        ["CEDULA CIUDADANIA"] = "CC",
        ["CE"] = "CE",
        ["E"] = "CE",
        ["CEDULA DE EXTRANJERIA"] = "CE",
        ["CEDULA EXTRANJERIA"] = "CE",
        ["NIT"] = "NIT",
        ["N"] = "NIT",
        ["NUMERO DE IDENTIFICACION TRIBUTARIA"] = "NIT",
        ["PAS"] = "PAS",
        ["P"] = "PAS",
        ["PA"] = "PAS",
        ["PP"] = "PAS",
        ["PASAPORTE"] = "PAS",
        ["TI"] = "TI",
        ["T"] = "TI",
        ["TARJETA DE IDENTIDAD"] = "TI",
        ["TARJETA IDENTIDAD"] = "TI",
    };

    /// <summary>
    /// Convierte el tipo de documento del actor al código que core-api acepta (CC, CE, NIT, PAS, TI).
    /// Tolera mayúsculas/minúsculas, tildes, puntos y guiones (<c>"c.c."</c>, <c>"Cédula de ciudadanía"</c>,
    /// <c>"N.I.T"</c>). Devuelve null si no lo reconoce: esa fila se rechaza en la entrada.
    /// </summary>
    public static string? NormalizeDocumentType(string? documentType)
    {
        if (string.IsNullOrWhiteSpace(documentType))
        {
            return null;
        }

        // Sin string.Normalize(FormD): el servicio corre con InvariantGlobalization y ahí la descomposición no
        // quita las tildes. Se mapean explícitamente las vocales acentuadas del español.
        var value = documentType.Trim();
        var sb = new StringBuilder(value.Length);
        var lastWasSpace = false;
        foreach (var raw in value)
        {
            var c = char.ToUpperInvariant(StripAccent(raw));
            if (c is '.' or '-' or '_')
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            sb.Append(c);
            lastWasSpace = false;
        }

        var key = sb.ToString().Trim();
        // "C C" (puntos con espacios) y "N I T" → sin espacios también.
        if (DocumentTypeAliases.TryGetValue(key, out var code)
            || DocumentTypeAliases.TryGetValue(key.Replace(" ", string.Empty, StringComparison.Ordinal), out code))
        {
            return code;
        }

        return null;
    }

    private static char StripAccent(char c) => c switch
    {
        'á' or 'à' or 'Á' or 'À' => 'A',
        'é' or 'è' or 'É' or 'È' => 'E',
        'í' or 'ì' or 'Í' or 'Ì' => 'I',
        'ó' or 'ò' or 'Ó' or 'Ò' => 'O',
        'ú' or 'ù' or 'ü' or 'Ú' or 'Ù' or 'Ü' => 'U',
        'ñ' or 'Ñ' => 'N',
        _ => c,
    };

    /// <summary>
    /// Teléfono del actor tal como se guarda y viaja a core-api: solo dígitos (se quitan espacios, '+',
    /// paréntesis, guiones, puntos...). Vacío si no hay dígitos.
    /// </summary>
    public static string NormalizePhone(string? phone) =>
        string.IsNullOrWhiteSpace(phone) ? string.Empty : new string(phone.Where(char.IsAsciiDigit).ToArray());

    /// <summary>
    /// Nombre completo del actor EXACTAMENTE como lo arma el cliente gRPC para core-api
    /// (<c>IctGrpcProcedureDraftClient.BuildRequestAsync</c>: <c>$"{Name} {FirstLastName} {SecondLastName}".Trim()</c>
    /// sobre los valores ya recortados por <see cref="ToMaster"/>).
    /// </summary>
    public static string ActorFullName(string? name, string? firstLastName, string? secondLastName) =>
        $"{name?.Trim() ?? string.Empty} {firstLastName?.Trim() ?? string.Empty} {secondLastName?.Trim()}".Trim();

    /// <summary>Normaliza un opcional del payload: recorta y convierte vacío en null (no se persiste ruido).</summary>
    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
