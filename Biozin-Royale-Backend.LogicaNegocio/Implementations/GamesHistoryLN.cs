using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

public class GamesHistoryLN : IGamesHistoryLN
{
    private readonly IUnitWork _unitOfWork;

    public GamesHistoryLN(IUnitWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Response<IEnumerable<TGamesHistory>>> ObtenerHistorialAsync(Guid userId)
    {
        var resultado = new Response<IEnumerable<TGamesHistory>>();

        // Igual que user_statistics: solo apuestas ya resueltas, nunca las pendientes.
        var apuestas = await _unitOfWork.GamesHistory
            .ObtenerEntidadesAsync(b => b.UserId == userId && b.Status == "settled");

        resultado.ReturnValue = apuestas
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new TGamesHistory
            {
                Id = b.Id,
                GameType = b.GameType,
                Amount = b.Amount,
                Payout = b.Payout,
                Profit = b.Profit,
                Result = b.Result,
                CreatedAt = b.CreatedAt
            });

        return resultado;
    }

    public async Task<Response<IEnumerable<TGamesHistory>>> ObtenerHistorialDeUsuarioAsync(Guid adminId, Guid targetUserId)
    {
        var resultado = new Response<IEnumerable<TGamesHistory>>();

        var staffEmail = (await _unitOfWork.StaffMembers.ObtenerEntidadAsync(s => s.Id == adminId))?.Email;
        var esAdmin = staffEmail is not null && CredentialsGenerator.DetectRole(staffEmail) == "admin";

        if (!esAdmin)
        {
            resultado.lpError("No autorizado.", "Solo los administradores pueden ver el historial de otros usuarios.");
            return resultado;
        }

        var apuestas = await _unitOfWork.GamesHistory
            .ObtenerEntidadesAsync(b => b.UserId == targetUserId && b.Status == "settled");

        resultado.ReturnValue = apuestas
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new TGamesHistory
            {
                Id = b.Id,
                GameType = b.GameType,
                Amount = b.Amount,
                Payout = b.Payout,
                Profit = b.Profit,
                Result = b.Result,
                CreatedAt = b.CreatedAt
            });

        return resultado;
    }
}
