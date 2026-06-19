namespace Flit.Modules.Security.Application.Auth.RememberUsername;

/// <summary>Solicitud de "recordar usuario" a partir de un identificador (número de documento).</summary>
public sealed record RememberUsernameCommand(string DocumentNumber);
