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

    public async Task<Response<List<TPromotion>>> ObtenerTodasAsync(Guid adminId)
    {
        var resultado = new Response<List<TPromotion>>();
        if (!await EsAdminAsync(adminId))
        {
            resultado.lpError("Acceso denegado", "No tienes permisos para esta acción.");
            return resultado;
        }

        var grantIds = (await _unitOfWork.PromotionClaims
            .ObtenerEntidadesAsync(c => c.Status == "compensacion"))
            .Select(c => c.PromotionId).ToHashSet();

        var promos = await _unitOfWork.Promotions.ListarAsync();
        resultado.ReturnValue = promos
            .Where(p => !grantIds.Contains(p.Id))
            .Select(Mapear)
            .ToList();
        return resultado;
    }

    public async Task<Response<TPromotion>> CrearPromocionAsync(Guid adminId, TCreatePromotion datos)
    {
        var resultado = new Response<TPromotion>();
        if (!await EsAdminAsync(adminId))
        {
            resultado.lpError("Acceso denegado", "No tienes permisos para esta acción.");
            return resultado;
        }

        if (string.IsNullOrWhiteSpace(datos.Title))
        {
            resultado.lpError("Datos inválidos", "El título es obligatorio.");
            return resultado;
        }

        if (datos.Amount <= 0)
        {
            resultado.lpError("Datos inválidos", "El monto debe ser mayor a cero.");
            return resultado;
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
        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = Mapear(promo);
        return resultado;
    }

    public async Task<Response<TPromotion>> ToggleActivoAsync(Guid adminId, Guid promotionId)
    {
        var resultado = new Response<TPromotion>();
        if (!await EsAdminAsync(adminId))
        {
            resultado.lpError("Acceso denegado", "No tienes permisos para esta acción.");
            return resultado;
        }

        var promo = await _unitOfWork.Promotions.ObtenerEntidadAsync(p => p.Id == promotionId);
        if (promo is null)
        {
            resultado.lpError("No encontrado", "La promoción no existe.");
            return resultado;
        }

        promo.IsActive = !promo.IsActive;
        _unitOfWork.Promotions.Modificar(promo);
        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = Mapear(promo);
        return resultado;
    }

    public async Task<Response<TPromotionClaim>> OtorgarBonoAsync(Guid adminId, Guid targetUserId, TCreatePromotion datos)
    {
        var resultado = new Response<TPromotionClaim>();
        if (!await EsAdminAsync(adminId))
        {
            resultado.lpError("Acceso denegado", "No tienes permisos para esta acción.");
            return resultado;
        }

        if (datos.Amount <= 0)
        {
            resultado.lpError("Datos inválidos", "El monto debe ser mayor a cero.");
            return resultado;
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
        await _unitOfWork.CompletarAsync();

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
        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = new TPromotionClaim
        {
            Id = claim.Id,
            PromotionId = claim.PromotionId,
            Promotion = Mapear(promo),
            Status = claim.Status,
            ClaimedAt = claim.ClaimedAt,
            CompletedAt = claim.CompletedAt
        };

        return resultado;
    }

    public async Task<Response<List<TPromotionClaim>>> ObtenerBonosUsuarioAsync(Guid adminId, Guid targetUserId)
    {
        var resultado = new Response<List<TPromotionClaim>>();
        if (!await EsAdminAsync(adminId))
        {
            resultado.lpError("Acceso denegado", "No tienes permisos para esta acción.");
            return resultado;
        }

        var claims = await _unitOfWork.PromotionClaims
            .ObtenerEntidadesAsync(c => c.UserId == targetUserId && (c.Status == "compensacion" || c.Status == "pendiente"));

        var promoIds = claims.Select(c => c.PromotionId).ToHashSet();
        var promos = (await _unitOfWork.Promotions
            .ObtenerEntidadesAsync(p => promoIds.Contains(p.Id)))
            .ToDictionary(p => p.Id);

        resultado.ReturnValue = claims.Select(c => new TPromotionClaim
        {
            Id = c.Id,
            PromotionId = c.PromotionId,
            Promotion = promos.TryGetValue(c.PromotionId, out var p) ? Mapear(p) : null,
            Status = c.Status,
            ClaimedAt = c.ClaimedAt,
            CompletedAt = c.CompletedAt
        }).ToList();

        return resultado;
    }

    // ──────────────────────────── Jugador ────────────────────────────

    public async Task<Response<List<TPromotion>>> ObtenerActivasAsync(Guid userId)
    {
        var resultado = new Response<List<TPromotion>>();

        var promos = await _unitOfWork.Promotions
            .ObtenerEntidadesAsync(p => p.IsActive && (p.EndsAt == null || p.EndsAt > DateTime.UtcNow));

        var reclamadas = (await _unitOfWork.PromotionClaims
            .ObtenerEntidadesAsync(c => c.UserId == userId))
            .Select(c => c.PromotionId).ToHashSet();

        var generales = promos.Where(p => !reclamadas.Contains(p.Id)).ToList();

        // Bonos personales otorgados por admin que el usuario aún no ha canjeado
        var pendingIds = (await _unitOfWork.PromotionClaims
            .ObtenerEntidadesAsync(c => c.UserId == userId && c.Status == "pendiente"))
            .Select(c => c.PromotionId).ToHashSet();

        var grants = await _unitOfWork.Promotions
            .ObtenerEntidadesAsync(p => pendingIds.Contains(p.Id));

        resultado.ReturnValue = generales.Concat(grants).Select(Mapear).ToList();

        return resultado;
    }

    public async Task<Response<TPromotionClaim>> ReclamarAsync(Guid userId, Guid promotionId)
    {
        var resultado = new Response<TPromotionClaim>();

        var promo = await _unitOfWork.Promotions.ObtenerEntidadAsync(p => p.Id == promotionId);
        if (promo is null)
        {
            resultado.lpError("No disponible", "Esta promoción no existe.");
            return resultado;
        }

        var existingClaim = await _unitOfWork.PromotionClaims
            .ObtenerEntidadAsync(c => c.UserId == userId && c.PromotionId == promotionId);

        var isPendingGrant = existingClaim?.Status == "pendiente";

        if (existingClaim is not null && !isPendingGrant)
        {
            resultado.lpError("Ya reclamada", "Ya canjeaste esta promoción.");
            return resultado;
        }

        if (!isPendingGrant && !promo.IsActive)
        {
            resultado.lpError("No disponible", "Esta promoción no está disponible.");
            return resultado;
        }

        if (promo.EndsAt is not null && promo.EndsAt <= DateTime.UtcNow)
        {
            resultado.lpError("Bono expirado", "Esta promoción ya expiró.");
            return resultado;
        }

        var wallet = await _unitOfWork.Wallets.ObtenerEntidadAsync(w => w.UserId == userId);
        if (wallet is null)
        {
            resultado.lpError("Error", "Billetera no encontrada.");
            return resultado;
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
            await _unitOfWork.CompletarAsync();

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
            await _unitOfWork.CompletarAsync();

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

        return resultado;
    }

    public async Task<Response<List<TPromotionClaim>>> ObtenerMisReclamosAsync(Guid userId)
    {
        var resultado = new Response<List<TPromotionClaim>>();

        var claims = await _unitOfWork.PromotionClaims
            .ObtenerEntidadesAsync(c => c.UserId == userId && (c.Status == "completado" || c.Status == "compensacion"));

        var promoIds = claims.Select(c => c.PromotionId).ToHashSet();
        var promos = (await _unitOfWork.Promotions
            .ObtenerEntidadesAsync(p => promoIds.Contains(p.Id)))
            .ToDictionary(p => p.Id);

        resultado.ReturnValue = claims.Select(c => new TPromotionClaim
        {
            Id = c.Id,
            PromotionId = c.PromotionId,
            Promotion = promos.TryGetValue(c.PromotionId, out var p) ? Mapear(p) : null,
            Status = c.Status,
            ClaimedAt = c.ClaimedAt,
            CompletedAt = c.CompletedAt
        }).ToList();

        return resultado;
    }

    // ──────────────────────────── Helpers ────────────────────────────

    private async Task<bool> EsAdminAsync(Guid userId)
    {
        var staff = await _unitOfWork.StaffMembers.ObtenerEntidadAsync(s => s.Id == userId);
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
