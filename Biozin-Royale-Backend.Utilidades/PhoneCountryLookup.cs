namespace Biozin_Royale_Backend.Utilidades;

/// Infiere el país a partir del código de marcación del teléfono (ej. "+506" -> "Costa Rica").
/// Es una ayuda de autocompletado, no una validación estricta: códigos como "+1" son
/// compartidos por varios países (se asume el más común) y el usuario puede corregirlo
/// después desde el formulario de perfil.
public static class 
    
    PhoneCountryLookup
{
    private static readonly (string Code, string Country)[] CallingCodes =
    {
        ("+506", "Costa Rica"),
        ("+507", "Panamá"),
        ("+503", "El Salvador"),
        ("+502", "Guatemala"),
        ("+504", "Honduras"),
        ("+505", "Nicaragua"),
        ("+1", "Estados Unidos"),
        ("+52", "México"),
        ("+57", "Colombia"),
        ("+58", "Venezuela"),
        ("+51", "Perú"),
        ("+593", "Ecuador"),
        ("+591", "Bolivia"),
        ("+595", "Paraguay"),
        ("+598", "Uruguay"),
        ("+54", "Argentina"),
        ("+56", "Chile"),
        ("+55", "Brasil"),
        ("+53", "Cuba"),
        ("+1809", "República Dominicana"),
        ("+1829", "República Dominicana"),
        ("+1849", "República Dominicana"),
        ("+34", "España"),
        ("+33", "Francia"),
        ("+39", "Italia"),
        ("+49", "Alemania"),
        ("+44", "Reino Unido"),
        ("+351", "Portugal"),
        ("+31", "Países Bajos"),
        ("+32", "Bélgica"),
        ("+41", "Suiza"),
        ("+86", "China"),
        ("+91", "India"),
        ("+81", "Japón"),
        ("+82", "Corea del Sur"),
        ("+61", "Australia"),
        ("+64", "Nueva Zelanda"),
        ("+27", "Sudáfrica"),
        ("+20", "Egipto"),
        ("+971", "Emiratos Árabes Unidos"),
        ("+966", "Arabia Saudita"),
        ("+7", "Rusia"),
        ("+90", "Turquía"),
    };

    /// Devuelve el país correspondiente al código de marcación del teléfono, o null si no se reconoce.
    public static string? GetCountry(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;

        var digitsOnly = "+" + new string(phone.Where(char.IsDigit).ToArray());

        return CallingCodes
            .Where(entry => digitsOnly.StartsWith(entry.Code, StringComparison.Ordinal))
            .OrderByDescending(entry => entry.Code.Length)
            .Select(entry => entry.Country)
            .FirstOrDefault();
    }
}