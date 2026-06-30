using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

public class PromotionLN : IPromotionLN
{
    private readonly IUnitWork _unitOfWork;

    public PromotionLN(IUnitWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // ──────────────────────────── Admin ────────────────────────────

    public Task<Response<List<TPromotion>>> ObtenerTodasAsync(Guid adminId)
    {
        var resultado = new Response<List<TPromotion>>();
        if (!EsAdmin(adminId))
        {
            resultado.lpError("Acceso denegado", "No tienes permisos para esta acción.");
            return Task.FromResult(resultado);
        }

        var promos = _unitOfWork.Promotions.Listar().ReturnValue ?? [];
        resultado.ReturnValue = promos.Select(Mapear).ToList();
        return Task.FromResult(resultado);
    }

    public Task<Response<TPromotion>> CrearPromocionAsync(Guid adminId, TCreatePromotion datos)
    {
        var resultado = new Response<TPromotion>();
        if (!EsAdmin(adminId))
        {
            resultado.lpError("Acceso denegado", "No tienes permisos para esta acción.");
            return Task.FromResult(resultado);
        }

        if (string.IsNullOrWhiteSpace(datos.Title))
        {
            resultado.lpError("Datos inválidos", "El título es obligatorio.");
            return Task.FromResult(resultado);
        }

        if (datos.Amount <= 0)
        {
            resultado.lpError("Datos inválidos", "El monto debe ser mayor a cero.");
            return Task.FromResult(resultado);
        }

        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            Title = datos.Title.Trim(),
            Description = datos.Description?.Trim(),
            PromotionType = datos.PromotionType.Trim(),
            Amount = datos.Amount,
            IsActive = datos.IsActive,
            StartsAt = datos.StartsAt,
            EndsAt = datos.EndsAt,
            CreatedAt = DateTime.UtcNow
        };

        _unitOfWork.Promotions.Insertar(promo);
        _unitOfWork.Completar();

        resultado.ReturnValue = Mapear(promo);
        return Task.FromResult(resultado);
    }

    public Task<Response<TPromotion>> ToggleActivoAsync(Guid adminId, Guid promotionId)
    {
        var resultado = new Response<TPromotion>();
        if (!EsAdmin(adminId))
        {
            resultado.lpError("Acceso denegado", "No tienes permisos para esta acción.");
            return Task.FromResult(resultado);
        }

        var promo = _unitOfWork.Promotions.ObtenerEntidad(p => p.Id == promotionId).ReturnValue;
        if (promo is null)
        {
            resultado.lpError("No encontrado", "La promoción no existe.");
            return Task.FromResult(resultado);
        }

        promo.IsActive = !promo.IsActive;
        _unitOfWork.Promotions.Modificar(promo);
        _unitOfWork.Completar();

        resultado.ReturnValue = Mapear(promo);
        return Task.FromResult(resultado);
    }

    public Task<Response<List<TAdminUser>>> ObtenerUsuariosAsync(Guid adminId)
    {
        var resultado = new Response<List<TAdminUser>>();
        if (!EsAdmin(adminId))
        {
            resultado.lpError("Acceso denegado", "No tienes permisos para esta acción.");
            return Task.FromResult(resultado);
        }

        var perfiles = _unitOfWork.Profiles
            .ObtenerEntidades(p => p.Role == "user" && !p.IsGuest)
            .ReturnValue ?? [];

        resultado.ReturnValue = perfiles.Select(p => new TAdminUser
        {
            Id = p.UserId,
            Username = p.Username,
            DisplayName = p.DisplayName,
            Email = p.Email,
            Status = p.Status,
            Role = p.Role,
            CreatedAt = p.CreatedAt
        }).ToList();

        return Task.FromResult(resultado);
    }

    public Task<Response<TPromotionClaim>> OtorgarBonoAsync(Guid adminId, Guid targetUserId, TCreatePromotion datos)
    {
        var resultado = new Response<TPromotionClaim>();
        if (!EsAdmin(adminId))
        {
            resultado.lpError("Acceso denegado", "No tienes permisos para esta acción.");
            return Task.FromResult(resultado);
        }

        if (datos.Amount <= 0)
        {
            resultado.lpError("Datos inválidos", "El monto debe ser mayor a cero.");
            return Task.FromResult(resultado);
        }

        var wallet = _unitOfWork.Wallets.ObtenerEntidad(w => w.UserId == targetUserId).ReturnValue;
        if (wallet is null)
        {
            resultado.lpError("Usuario no encontrado", "No se encontró la billetera del usuario.");
            return Task.FromResult(resultado);
        }

        var ahora = DateTime.UtcNow;

        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            Title = string.IsNullOrWhiteSpace(datos.Title) ? "Bono personalizado" : datos.Title.Trim(),
            Description = datos.Description?.Trim(),
            PromotionType = string.IsNullOrWhiteSpace(datos.PromotionType) ? "Liquidez" : datos.PromotionType,
            Amount = datos.Amount,
            IsActive = false,
            CreatedAt = ahora
        };
        _unitOfWork.Promotions.Insertar(promo);
        // La promotion debe existir en DB antes de insertar el claim (FK promotion_claims_promotion_id_fkey)
        _unitOfWork.Completar();

        wallet.Balance = Math.Round(wallet.Balance + promo.Amount, 2);
        wallet.UpdatedAt = ahora;
        _unitOfWork.Wallets.Modificar(wallet);

        var claim = new PromotionClaim
        {
            Id = Guid.NewGuid(),
            PromotionId = promo.Id,
            UserId = targetUserId,
            Status = "completado",
            ClaimedAt = ahora,
            CompletedAt = ahora
        };
        _unitOfWork.PromotionClaims.Insertar(claim);
        _unitOfWork.Completar();

        resultado.ReturnValue = new TPromotionClaim
        {
            Id = claim.Id,
            PromotionId = claim.PromotionId,
            Promotion = Mapear(promo),
            Status = claim.Status,
            ClaimedAt = claim.ClaimedAt,
            CompletedAt = claim.CompletedAt
        };

        return Task.FromResult(resultado);
    }

    // ──────────────────────────── Jugador ────────────────────────────

    public Task<Response<List<TPromotion>>> ObtenerActivasAsync(Guid userId)
    {
        var resultado = new Response<List<TPromotion>>();

        var promos = _unitOfWork.Promotions
            .ObtenerEntidades(p => p.IsActive)
            .ReturnValue ?? [];

        var reclamadas = _unitOfWork.PromotionClaims
            .ObtenerEntidades(c => c.UserId == userId)
            .ReturnValue?.Select(c => c.PromotionId).ToHashSet() ?? [];

        resultado.ReturnValue = promos
            .Where(p => !reclamadas.Contains(p.Id))
            .Select(Mapear)
            .ToList();

        return Task.FromResult(resultado);
    }

    public Task<Response<TPromotionClaim>> ReclamarAsync(Guid userId, Guid promotionId)
    {
        var resultado = new Response<TPromotionClaim>();

        var promo = _unitOfWork.Promotions.ObtenerEntidad(p => p.Id == promotionId).ReturnValue;
        if (promo is null || !promo.IsActive)
        {
            resultado.lpError("No disponible", "Esta promoción no está disponible.");
            return Task.FromResult(resultado);
        }

        var yaReclamada = _unitOfWork.PromotionClaims
            .ObtenerEntidad(c => c.UserId == userId && c.PromotionId == promotionId)
            .ReturnValue;
        if (yaReclamada is not null)
        {
            resultado.lpError("Ya reclamada", "Ya canjeaste esta promoción.");
            return Task.FromResult(resultado);
        }

        var wallet = _unitOfWork.Wallets.ObtenerEntidad(w => w.UserId == userId).ReturnValue;
        if (wallet is null)
        {
            resultado.lpError("Error", "Billetera no encontrada.");
            return Task.FromResult(resultado);
        }

        wallet.Balance = Math.Round(wallet.Balance + promo.Amount, 2);
        wallet.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Wallets.Modificar(wallet);

        var ahora = DateTime.UtcNow;
        var claim = new PromotionClaim
        {
            Id = Guid.NewGuid(),
            PromotionId = promotionId,
            UserId = userId,
            Status = "completado",
            ClaimedAt = ahora,
            CompletedAt = ahora
        };
        _unitOfWork.PromotionClaims.Insertar(claim);
        _unitOfWork.Completar();

        resultado.ReturnValue = new TPromotionClaim
        {
            Id = claim.Id,
            PromotionId = claim.PromotionId,
            Promotion = Mapear(promo),
            Status = claim.Status,
            ClaimedAt = claim.ClaimedAt,
            CompletedAt = claim.CompletedAt
        };

        return Task.FromResult(resultado);
    }

    public Task<Response<List<TPromotionClaim>>> ObtenerMisReclamosAsync(Guid userId)
    {
        var resultado = new Response<List<TPromotionClaim>>();

        var claims = _unitOfWork.PromotionClaims
            .ObtenerEntidades(c => c.UserId == userId)
            .ReturnValue?.ToList() ?? [];

        var promoIds = claims.Select(c => c.PromotionId).ToHashSet();
        var promos = _unitOfWork.Promotions
            .ObtenerEntidades(p => promoIds.Contains(p.Id))
            .ReturnValue?.ToDictionary(p => p.Id) ?? [];

        resultado.ReturnValue = claims.Select(c => new TPromotionClaim
        {
            Id = c.Id,
            PromotionId = c.PromotionId,
            Promotion = promos.TryGetValue(c.PromotionId, out var p) ? Mapear(p) : null,
            Status = c.Status,
            ClaimedAt = c.ClaimedAt,
            CompletedAt = c.CompletedAt
        }).ToList();

        return Task.FromResult(resultado);
    }

    // ──────────────────────────── Helpers ────────────────────────────

    private bool EsAdmin(Guid userId)
    {
        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == userId).ReturnValue;
        return perfil?.Role == "admin";
    }

    private static TPromotion Mapear(Promotion p) => new()
    {
        Id = p.Id,
        Title = p.Title,
        Description = p.Description,
        PromotionType = p.PromotionType,
        Amount = p.Amount,
        IsActive = p.IsActive,
        StartsAt = p.StartsAt,
        EndsAt = p.EndsAt,
        CreatedAt = p.CreatedAt
    };
}
