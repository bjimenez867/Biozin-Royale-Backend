using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesLN;

public interface IPromotionLN
{
    Task<Response<List<TPromotion>>> ObtenerTodasAsync(Guid adminId);
    Task<Response<TPromotion>> CrearPromocionAsync(Guid adminId, TCreatePromotion datos);
    Task<Response<TPromotion>> ToggleActivoAsync(Guid adminId, Guid promotionId);
    Task<Response<List<TAdminUser>>> ObtenerUsuariosAsync(Guid adminId);
    Task<Response<TPromotionClaim>> OtorgarBonoAsync(Guid adminId, Guid targetUserId, TCreatePromotion datos);

    Task<Response<List<TPromotion>>> ObtenerActivasAsync(Guid userId);
    Task<Response<TPromotionClaim>> ReclamarAsync(Guid userId, Guid promotionId);
    Task<Response<List<TPromotionClaim>>> ObtenerMisReclamosAsync(Guid userId);
}
