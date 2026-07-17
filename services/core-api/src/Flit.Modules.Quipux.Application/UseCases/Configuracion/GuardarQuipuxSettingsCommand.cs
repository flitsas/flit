namespace Flit.Modules.Quipux.Application.UseCases.Configuracion;

/// <summary>
/// Alta o actualización de la fila única <c>admin.quipux_settings</c>. Los dos secretos
/// (<see cref="Password"/>, <see cref="AwsSecretAccessKey"/>) son opcionales: si llegan vacíos o
/// nulos se CONSERVA el valor cifrado ya almacenado (no se borra), como cualquier formulario de
/// credenciales que no reenvía el secreto salvo que lo cambies.
/// </summary>
public sealed class GuardarQuipuxSettingsCommand
{
    public bool Enabled { get; init; }

    public string UrlLogin { get; init; } = string.Empty;
    public string UrlRegisterDocument { get; init; } = string.Empty;
    public string UrlValidateStatus { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;

    /// <summary>Vacío/nulo = conservar el existente. No se borra nunca por omisión.</summary>
    public string? Password { get; init; }

    public string ConsumerCode { get; init; } = string.Empty;

    public string Bucket { get; init; } = string.Empty;
    public string S3Prefix { get; init; } = "FLIT/";
    public string AwsRegion { get; init; } = "us-east-1";
    public string AwsAccessKeyId { get; init; } = string.Empty;

    /// <summary>Vacío/nulo = conservar el existente. No se borra nunca por omisión.</summary>
    public string? AwsSecretAccessKey { get; init; }

    public int OfficerDocumentType { get; init; } = 3;
    public string OfficerDocumentNumber { get; init; } = string.Empty;

    public int RegisterIntervalMinutes { get; init; } = 15;
    public int PollIntervalMinutes { get; init; } = 15;
    public int BatchSize { get; init; } = 20;
    public int MaxAttempts { get; init; } = 5;
    public int MaxPolls { get; init; } = 500;
    public int TimeoutSeconds { get; init; } = 60;
}
