using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

public class MetodosPagoLN : IMetodosPagoLN
{
    private readonly IUnitWork _unitOfWork;

    public MetodosPagoLN(IUnitWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Response<IEnumerable<TMetodoPagoResultado>> Listar(Guid userId)
    {
        var resultado = new Response<IEnumerable<TMetodoPagoResultado>>();

        var wallet = _unitOfWork.Wallets.ObtenerEntidad(w => w.UserId == userId).ReturnValue;
        if (wallet is null) { resultado.lpError("Billetera", "No se encontró la billetera del usuario."); return resultado; }

        var metodos = _unitOfWork.PaymentMethods
            .ObtenerEntidades(m => m.WalletId == wallet.Id)
            .ReturnValue ?? [];

        resultado.ReturnValue = metodos
            .OrderByDescending(m => m.IsDefault)
            .ThenByDescending(m => m.CreatedAt)
            .Select(ToResultado);

        return resultado;
    }

    public Response<TMetodoPagoResultado> AgregarPayPal(Guid userId, TAgregarPayPalRequest request)
    {
        var resultado = new Response<TMetodoPagoResultado>();

        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
        {
            resultado.lpError("Email inválido", "Ingresá un correo electrónico válido.");
            return resultado;
        }

        var (wallet, existentes, errorRes) = ObtenerWalletYExistentes<TMetodoPagoResultado>(userId);
        if (errorRes is not null) return errorRes;

        var lista = existentes!.ToList();

        if (lista.Count >= 10) { resultado.lpError("Límite alcanzado", "Podés registrar hasta 10 métodos de pago."); return resultado; }

        var emailNorm = request.Email.Trim().ToLowerInvariant();
        if (lista.Any(m => m.Type == "paypal" && m.Email != null && m.Email.Equals(emailNorm, StringComparison.OrdinalIgnoreCase)))
        {
            resultado.lpError("Ya registrada", "Esa cuenta de PayPal ya está registrada."); return resultado;
        }

        if (request.IsDefault) DesmarcarPrincipal(lista);

        var nuevo = new PaymentMethod
        {
            Id        = Guid.NewGuid(),
            WalletId  = wallet!.Id,
            Type      = "paypal",
            Email     = emailNorm,
            Alias     = string.IsNullOrWhiteSpace(request.Alias) ? null : request.Alias.Trim(),
            IsDefault = request.IsDefault || lista.Count == 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _unitOfWork.PaymentMethods.Insertar(nuevo);
        _unitOfWork.Completar();

        resultado.ReturnValue = ToResultado(nuevo);
        return resultado;
    }

    public Response<TMetodoPagoResultado> AgregarTarjeta(Guid userId, TAgregarTarjetaRequest request)
    {
        var resultado = new Response<TMetodoPagoResultado>();

        var digitos = new string(request.CardNumber.Where(char.IsDigit).ToArray());
        if (digitos.Length < 13 || digitos.Length > 19)
        {
            resultado.lpError("Número inválido", "El número de tarjeta no es válido."); return resultado;
        }

        if (request.ExpiryMonth < 1 || request.ExpiryMonth > 12 || request.ExpiryYear < DateTime.UtcNow.Year)
        {
            resultado.lpError("Vencimiento inválido", "La fecha de vencimiento no es válida."); return resultado;
        }

        if (string.IsNullOrWhiteSpace(request.CardHolder))
        {
            resultado.lpError("Titular requerido", "Ingresá el nombre del titular."); return resultado;
        }

        var (wallet, existentes, errorRes) = ObtenerWalletYExistentes<TMetodoPagoResultado>(userId);
        if (errorRes is not null) return errorRes;

        var lista       = existentes!.ToList();
        var lastFour    = digitos[^4..];
        var network     = DetectarRed(digitos);

        if (lista.Count >= 10) { resultado.lpError("Límite alcanzado", "Podés registrar hasta 10 métodos de pago."); return resultado; }

        if (lista.Any(m => m.Type == "card" && m.LastFour == lastFour))
        {
            resultado.lpError("Ya registrada", "Ya tenés una tarjeta con esos últimos 4 dígitos registrada."); return resultado;
        }

        if (request.IsDefault) DesmarcarPrincipal(lista);

        var nuevo = new PaymentMethod
        {
            Id          = Guid.NewGuid(),
            WalletId    = wallet!.Id,
            Type        = "card",
            Alias       = string.IsNullOrWhiteSpace(request.Alias) ? null : request.Alias.Trim(),
            CardHolder  = request.CardHolder.Trim(),
            LastFour    = lastFour,
            Network     = network,
            ExpiryMonth = request.ExpiryMonth,
            ExpiryYear  = request.ExpiryYear,
            IsDefault   = request.IsDefault || lista.Count == 0,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };

        _unitOfWork.PaymentMethods.Insertar(nuevo);
        _unitOfWork.Completar();

        resultado.ReturnValue = ToResultado(nuevo);
        return resultado;
    }

    public Response<bool> Eliminar(Guid userId, Guid methodId)
    {
        var resultado = new Response<bool>();

        var wallet = _unitOfWork.Wallets.ObtenerEntidad(w => w.UserId == userId).ReturnValue;
        if (wallet is null) { resultado.lpError("Billetera", "No se encontró la billetera del usuario."); return resultado; }

        var metodo = _unitOfWork.PaymentMethods
            .ObtenerEntidad(m => m.Id == methodId && m.WalletId == wallet.Id).ReturnValue;

        if (metodo is null) { resultado.lpError("No encontrado", "Método de pago no encontrado."); return resultado; }

        bool eraPrincipal = metodo.IsDefault;
        _unitOfWork.PaymentMethods.Eliminar(metodo);
        _unitOfWork.Completar();

        if (eraPrincipal)
        {
            var otro = _unitOfWork.PaymentMethods
                .ObtenerEntidades(m => m.WalletId == wallet.Id)
                .ReturnValue?.OrderBy(m => m.CreatedAt).FirstOrDefault();

            if (otro is not null)
            {
                otro.IsDefault = true;
                otro.UpdatedAt = DateTime.UtcNow;
                _unitOfWork.PaymentMethods.Modificar(otro);
                _unitOfWork.Completar();
            }
        }

        resultado.ReturnValue = true;
        return resultado;
    }

    public Response<bool> EstablecerPrincipal(Guid userId, Guid methodId)
    {
        var resultado = new Response<bool>();

        var wallet = _unitOfWork.Wallets.ObtenerEntidad(w => w.UserId == userId).ReturnValue;
        if (wallet is null) { resultado.lpError("Billetera", "No se encontró la billetera del usuario."); return resultado; }

        var todos = _unitOfWork.PaymentMethods
            .ObtenerEntidades(m => m.WalletId == wallet.Id)
            .ReturnValue?.ToList() ?? [];

        if (!todos.Any(m => m.Id == methodId)) { resultado.lpError("No encontrado", "Método de pago no encontrado."); return resultado; }

        foreach (var m in todos)
        {
            bool debe = m.Id == methodId;
            if (m.IsDefault != debe) { m.IsDefault = debe; m.UpdatedAt = DateTime.UtcNow; _unitOfWork.PaymentMethods.Modificar(m); }
        }

        _unitOfWork.Completar();
        resultado.ReturnValue = true;
        return resultado;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private (Wallet? wallet, IEnumerable<PaymentMethod>? existentes, Response<T>? error)
        ObtenerWalletYExistentes<T>(Guid userId)
    {
        var wallet = _unitOfWork.Wallets.ObtenerEntidad(w => w.UserId == userId).ReturnValue;
        if (wallet is null)
        {
            var err = new Response<T>();
            err.lpError("Billetera", "No se encontró la billetera del usuario.");
            return (null, null, err);
        }
        var existentes = _unitOfWork.PaymentMethods
            .ObtenerEntidades(m => m.WalletId == wallet.Id).ReturnValue ?? [];
        return (wallet, existentes, null);
    }

    private void DesmarcarPrincipal(IEnumerable<PaymentMethod> lista)
    {
        foreach (var m in lista.Where(m => m.IsDefault))
        {
            m.IsDefault = false;
            _unitOfWork.PaymentMethods.Modificar(m);
        }
    }

    private static string DetectarRed(string digitos) => digitos[0] switch
    {
        '4'                          => "visa",
        '5'                          => "mastercard",
        '3' when digitos.Length >= 2 && (digitos[1] == '4' || digitos[1] == '7') => "amex",
        _                            => "other",
    };

    private static TMetodoPagoResultado ToResultado(PaymentMethod m) => new()
    {
        Id          = m.Id,
        Type        = m.Type,
        Alias       = m.Alias,
        IsDefault   = m.IsDefault,
        CreatedAt   = m.CreatedAt,
        Email       = m.Email,
        CardHolder  = m.CardHolder,
        LastFour    = m.LastFour,
        Network     = m.Network,
        ExpiryMonth = m.ExpiryMonth,
        ExpiryYear  = m.ExpiryYear,
    };
}
