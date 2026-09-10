using Flit.DataMigration.V1.Mapping;
using Flit.DataMigration.V1.Source;
using Xunit;

namespace Flit.DataMigration.Tests.Mapping;

/// <summary>
/// Qué partes da V1 por acreditadas. Es la decisión que hace que un trámite migrado NO vuelva a pedir
/// una validación de identidad que ya se hizo — y, al revés, que sí la pida cuando en V1 nunca ocurrió.
/// </summary>
public sealed class IdentityPolicyTests
{
    private static V1SourceRecord Traspaso(params (string Column, string? Value)[] columns) =>
        new()
        {
            Id = 7426,
            SourceTable = "vehicle_transfer_master",
            ProcessStatus = 1,
            Columns = columns.ToDictionary(c => c.Column, c => c.Value, StringComparer.OrdinalIgnoreCase),
            StatusHistory = [],
        };

    private static V1SourceRecord Matricula(params (string Column, string? Value)[] columns) =>
        new()
        {
            Id = 26350,
            SourceTable = "vehicle_registration_master",
            ProcessStatus = 1,
            Columns = columns.ToDictionary(c => c.Column, c => c.Value, StringComparer.OrdinalIgnoreCase),
            StatusHistory = [],
        };

    [Fact]
    public void Biometrica_aprobada_en_v1_se_acredita_y_exige_la_carta_selfie()
    {
        var aprobadas = IdentityPolicy.Transfer.AprobadasEnV1(Traspaso(
            ("buyer_validation_identity", "true"),
            ("id_attach_image_face_buyer", "abc123"),
            ("identity_validation_date_approve_buyer", "2026-02-13 10:30:00")));

        var comprador = Assert.Single(aprobadas);
        Assert.Equal("comprador", comprador.Nombre);
        Assert.Equal("comprador", comprador.PartyRole);
        Assert.False(comprador.PorFirmaFisica);
        Assert.True(comprador.ExigeCartaSelfie);
        Assert.Equal(new DateTime(2026, 2, 13, 10, 30, 0, DateTimeKind.Utc), comprador.AprobadaEnV1!.Value.UtcDateTime);
    }

    [Fact]
    public void Firma_fisica_acredita_la_identidad_sin_exigir_carta_selfie()
    {
        // El vendedor firmó en papel: no hubo biométrica, luego no hay carta que respalde nada. La
        // acreditación vale igual (es la decisión que tomó el gestor y V1 la registró).
        var aprobadas = IdentityPolicy.Transfer.AprobadasEnV1(Traspaso(
            ("is_fisical_signature_seller", "true")));

        var vendedor = Assert.Single(aprobadas);
        Assert.Equal("vendedor", vendedor.PartyRole);
        Assert.True(vendedor.PorFirmaFisica);
        Assert.False(vendedor.ExigeCartaSelfie);
        Assert.Null(vendedor.AprobadaEnV1);
    }

    [Fact]
    public void La_columna_de_firma_fisica_del_comprador_no_lleva_sufijo()
    {
        // Regresión: en V1 el comprador usa `is_fisical_signature` a secas y el vendedor
        // `is_fisical_signature_seller`. Buscar `is_fisical_signature_buyer` no encuentra nada y la
        // identidad del comprador se quedaría sin acreditar en silencio.
        var aprobadas = IdentityPolicy.Transfer.AprobadasEnV1(Traspaso(("is_fisical_signature", "true")));

        Assert.Equal("comprador", Assert.Single(aprobadas).PartyRole);
    }

    [Fact]
    public void Identidad_pendiente_o_rechazada_no_acredita_nada()
    {
        // Ni bandera de validación ni firma física: en V1 esa validación nunca prosperó, así que V2
        // debe pedirla. Es el caso de los 375 traspasos de la copia de producción.
        Assert.Empty(IdentityPolicy.Transfer.AprobadasEnV1(Traspaso(
            ("buyer_validation_identity", "false"),
            ("seller_validation_identity", null),
            ("identity_validation_date_reject_buyer", "2026-02-13 10:30:00"))));
    }

    [Fact]
    public void El_titular_de_matricula_se_acredita_con_el_rol_comprador()
    {
        // La trampa del tipo de trámite: en matrícula la parte se llama "titular" en V1 pero la
        // instancia 1 la migra como actor 'comprador'. Si el rol no empareja, la validación queda
        // colgando de un actor que no existe y el gate nunca la ve.
        var aprobadas = IdentityPolicy.Registration.AprobadasEnV1(Matricula(
            ("owner_validation_identity", "true"),
            ("id_attach_image_face_owner", "face-owner")));

        var titular = Assert.Single(aprobadas);
        Assert.Equal("titular", titular.Nombre);
        Assert.Equal("comprador", titular.PartyRole);
        Assert.Equal("getLetterSelfieOwner", titular.SelfieLetterPieceKey);
    }

    [Fact]
    public void Sin_selfie_no_hay_carta_que_exigir_aunque_la_identidad_este_validada()
    {
        // V1 solo arma la carta si además de la bandera hay selfie. Exigir una carta que V1 nunca iba a
        // producir dejaría sin acreditar una identidad que sí estaba validada.
        var aprobadas = IdentityPolicy.Transfer.AprobadasEnV1(Traspaso(
            ("buyer_validation_identity", "true")));

        Assert.False(Assert.Single(aprobadas).ExigeCartaSelfie);
    }

    [Fact]
    public void Las_imagenes_de_identidad_nunca_se_migran_pero_el_motivo_cambia()
    {
        var validado = IdentityPolicy.Transfer.RedundantColumns(Traspaso(
            ("buyer_validation_identity", "true"),
            ("id_attach_image_face_buyer", "face")));
        var sinValidar = IdentityPolicy.Transfer.RedundantColumns(Traspaso());

        // Las tres columnas de cada parte quedan excluidas en ambos casos (6 en traspaso).
        Assert.Equal(6, validado.Count);
        Assert.Equal(6, sinValidar.Count);
        Assert.Contains("ya contiene esta imagen", validado["id_attach_image_face_buyer"], StringComparison.Ordinal);
        Assert.Contains("no quedó validada en V1", sinValidar["id_attach_image_face_buyer"], StringComparison.Ordinal);
    }
}
