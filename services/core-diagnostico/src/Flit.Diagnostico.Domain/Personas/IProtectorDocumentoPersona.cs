namespace Flit.Diagnostico.Domain.Personas;

/// <summary>
/// Cifrado en aplicación del documento de una persona (propietario o locatario) antes de guardarlo en
/// <c>*_doc_cifrado</c> (HU #13751, plan §9.3, R-3). El número en claro solo vive en memoria: nunca en la base, en logs ni
/// en eventos. La implementación va sobre ASP.NET Data Protection con llaves protegidas fuera de la base.
/// </summary>
public interface IProtectorDocumentoPersona
{
    /// <summary>Cifra el número del documento.</summary>
    byte[] Proteger(string numero);

    /// <summary>Descifra lo que devolvió <see cref="Proteger"/>.</summary>
    string Desproteger(byte[] cifrado);
}
