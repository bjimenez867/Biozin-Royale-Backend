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

        var grantIds = _unitOfWork.PromotionClaims
            .ObtenerEntidades(c => c.Status == "compensacion")
            .ReturnValue?.Select(c => c.PromotionId).ToHashSet() ?? [];

        var promos = _unitOfWork.Promotions.Listar().ReturnValue ?? [];
        resultado.ReturnValue = promos
            .Where(p => !grantIds.Contains(p.Id))
            .Select(Mapear)
            .ToList();
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
            Amount = datos.Amount,
            IsActive = datos.IsActive,
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

        var ahora = DateTime.UtcNow;

        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            Title = string.IsNullOrWhiteSpace(datos.Title) ? "Bono personalizado" : datos.Title.Trim(),
            Description = datos.Description?.Trim(),
            Amount = datos.Amount,
            IsActive = false,
            CreatedAt = ahora
        };
        _unitOfWork.Promotions.Insertar(promo);
        // La promotion debe existir en DB antes de insertar el claim (FK promotion_claims_promotion_id_fkey)
        _unitOfWork.Completar();

        var claim = new PromotionClaim
        {
            Id = Guid.NewGuid(),
            PromotionId = promo.Id,
            UserId = targetUserId,
            Status = "pendiente",
            ClaimedAt = ahora,
            CompletedAt = null
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

    public Task<Response<List<TPromotionClaim>>> ObtenerBonosUsuarioAsync(Guid adminId, Guid targetUserId)
    {
        var resultado = new Response<List<TPromotionClaim>>();
        if (!EsAdmin(adminId))
        {
            resultado.lpError("Acceso denegado", "No tienes permisos para esta acción.");
            return Task.FromResult(resultado);
        }

        var claims = _unitOfWork.PromotionClaims
            .ObtenerEntidades(c => c.UserId == targetUserId && (c.Status == "compensacion" || c.Status == "pendiente"))
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

        var generales = promos.Where(p => !reclamadas.Contains(p.Id)).ToList();

        // Bonos personales otorgados por admin que el usuario aún no ha canjeado
        var pendingIds = _unitOfWork.PromotionClaims
            .ObtenerEntidades(c => c.UserId == userId && c.Status == "pendiente")
            .ReturnValue?.Select(c => c.PromotionId).ToHashSet() ?? [];

        var grants = _unitOfWork.Promotions
            .ObtenerEntidades(p => pendingIds.Contains(p.Id))
            .ReturnValue ?? [];

        resultado.ReturnValue = generales.Concat(grants).Select(Mapear).ToList();

        return Task.FromResult(resultado);
    }

    public Task<Response<TPromotionClaim>> ReclamarAsync(Guid userId, Guid promotionId)
    {
        var resultado = new Response<TPromotionClaim>();

        var promo = _unitOfWork.Promotions.ObtenerEntidad(p => p.Id == promotionId).ReturnValue;
        if (promo is null)
        {
            resultado.lpError("No disponible", "Esta promoción no existe.");
            return Task.FromResult(resultado);
        }

        var existingClaim = _unitOfWork.PromotionClaims
            .ObtenerEntidad(c => c.UserId == userId && c.PromotionId == promotionId)
            .ReturnValue;

        var isPendingGrant = existingClaim?.Status == "pendiente";

        if (existingClaim is not null && !isPendingGrant)
        {
            resultado.lpError("Ya reclamada", "Ya canjeaste esta promoción.");
            return Task.FromResult(resultado);
        }

        if (!isPendingGrant && !promo.IsActive)
        {
            resultado.lpError("No disponible", "Esta promoción no está disponible.");
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

        if (isPendingGrant)
        {
            existingClaim!.Status = "compensacion";
            existingClaim.CompletedAt = ahora;
            _unitOfWork.PromotionClaims.Modificar(existingClaim);
            _unitOfWork.Completar();

            resultado.ReturnValue = new TPromotionClaim
            {
                Id = existingClaim.Id,
                PromotionId = existingClaim.PromotionId,
                Promotion = Mapear(promo),
                Status = existingClaim.Status,
                ClaimedAt = existingClaim.ClaimedAt,
                CompletedAt = existingClaim.CompletedAt
            };
        }
        else
        {
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
        }

        return Task.FromResult(resultado);
    }

    public Task<Response<List<TPromotionClaim>>> ObtenerMisReclamosAsync(Guid userId)
    {
        var resultado = new Response<List<TPromotionClaim>>();

        var claims = _unitOfWork.PromotionClaims
            .ObtenerEntidades(c => c.UserId == userId && (c.Status == "completado" || c.Status == "compensacion"))
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
        var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == userId).ReturnValue;
        return staff is not null && CredentialsGenerator.DetectRole(staff.Email) == "admin";
    }

    private static TPromotion Mapear(Promotion p) => new()
    {
        Id = p.Id,
        Title = p.Title,
        Description = p.Description,
        Amount = p.Amount,
        IsActive = p.IsActive,
        EndsAt = p.EndsAt,
        CreatedAt = p.CreatedAt
    };
}
