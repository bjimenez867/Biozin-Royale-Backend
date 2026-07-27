namespace Biozin_Royale_Backend.Utilidades;

public class DeviceParser
{
    /// Aproxima un label legible de dispositivo/navegador a partir del User-Agent,
    /// sin depender de una librería de parsing — suficiente para mostrar en la UI,
    /// no para detección precisa.
    public static string AnalizarUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return "Dispositivo desconocido";

        var so = DetectarSistemaOperativo(userAgent);
        var navegador = DetectarNavegador(userAgent);

        if (so is null && navegador is null) return "Dispositivo desconocido";
        if (so is not null && navegador is not null) return $"{navegador} en {so}";
        return so ?? navegador!;
    }

    private static string? DetectarSistemaOperativo(string userAgent)
    {
        if (userAgent.Contains("iPhone")) return "iPhone";
        if (userAgent.Contains("iPad")) return "iPad";
        if (userAgent.Contains("Android")) return "Android";
        if (userAgent.Contains("Windows")) return "Windows";
        if (userAgent.Contains("Macintosh") || userAgent.Contains("Mac OS")) return "Mac";
        if (userAgent.Contains("Linux")) return "Linux";
        return null;
    }

    private static string? DetectarNavegador(string userAgent)
    {
        if (userAgent.Contains("Edg/")) return "Edge";
        if (userAgent.Contains("OPR/") || userAgent.Contains("Opera")) return "Opera";
        if (userAgent.Contains("Chrome")) return "Chrome";
        if (userAgent.Contains("CriOS")) return "Chrome";
        if (userAgent.Contains("Firefox")) return "Firefox";
        if (userAgent.Contains("Safari")) return "Safari";
        return null;
    }
}
