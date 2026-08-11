using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Biozin_Royale_Backend.LogicaNegocio.Implementations.Blackjack;

namespace Biozin_Royale_Backend.API.Hubs;

/// Único punto de entrada en tiempo real del blackjack. El cliente solo puede:
/// ver el lobby, sentarse, apostar y actuar en su turno. Toda validación de
/// reglas y dinero vive en BlackjackRoomManager — aquí solo se autentica y enruta.
[Authorize]
public class BlackjackHub : Hub
{
    private readonly BlackjackRoomManager _manager;

    public BlackjackHub(BlackjackRoomManager manager)
    {
        _manager = manager;
    }

    private Guid UserId =>
        Guid.TryParse(Context.User?.FindFirst("sub")?.Value, out var id)
            ? id
            : throw new HubException("Sesión inválida.");

    // La sala en la que está esta conexión (para no confiar en un roomId del cliente
    // distinto al que realmente ocupa)
    private int? CurrentRoom
    {
        get => Context.Items.TryGetValue("room", out var v) ? (int?)v : null;
        set => Context.Items["room"] = value;
    }

    // ── Lobby ─────────────────────────────────────────────────────────────────

    public async Task<object> JoinLobby()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "bj:lobby");
        return _manager.GetLobbySummary();
    }

    public Task LeaveLobby() =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, "bj:lobby");

    // ── Mesa ──────────────────────────────────────────────────────────────────

    public async Task<object> JoinRoom(int roomId)
    {
        // Al grupo ANTES de sentarse: el loop de la sala puede emitir el primer
        // "starting" milisegundos después del join y no debe perderse.
        await Groups.AddToGroupAsync(Context.ConnectionId, $"bj:{roomId}");
        try
        {
            var snapshot = await _manager.JoinAsync(roomId, UserId, Context.ConnectionId);
            CurrentRoom = roomId;
            return snapshot;
        }
        catch
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"bj:{roomId}");
            throw;
        }
    }

    public async Task LeaveRoom()
    {
        if (CurrentRoom is not int roomId) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"bj:{roomId}");
        CurrentRoom = null;
        await _manager.LeaveAsync(Context.ConnectionId);
    }

    // ── Mesas privadas ────────────────────────────────────────────────────────

    public async Task<object> CreatePrivateRoom(decimal min, decimal max, bool fillWithBots)
    {
        var (roomId, snapshot) = await _manager.CreatePrivateRoomAsync(
            UserId, Context.ConnectionId, min, max, fillWithBots);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"bj:{roomId}");
        CurrentRoom = roomId;
        return new { roomId, snapshot };
    }

    public async Task<object> JoinByCode(string code)
    {
        var (roomId, snapshot) = await _manager.JoinByCodeAsync(code, UserId, Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"bj:{roomId}");
        CurrentRoom = roomId;
        return new { roomId, snapshot };
    }

    public Task StartGame()
    {
        if (CurrentRoom is not int roomId) throw new HubException("No estás en una mesa.");
        return _manager.StartPrivateGameAsync(roomId, UserId);
    }

    public Task PlaceBet(decimal amount)
    {
        if (CurrentRoom is not int roomId) throw new HubException("No estás en una mesa.");
        return _manager.PlaceBetAsync(roomId, UserId, amount);
    }

    public Task Action(string action)
    {
        if (CurrentRoom is not int roomId) throw new HubException("No estás en una mesa.");

        var parsed = action?.ToLowerInvariant() switch
        {
            "hit"       => BjAction.Hit,
            "stand"     => BjAction.Stand,
            "double"    => BjAction.Double,
            "split"     => BjAction.Split,
            "surrender" => BjAction.Surrender,
            _ => throw new HubException("Acción desconocida."),
        };

        return _manager.SubmitActionAsync(roomId, UserId, parsed);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await _manager.LeaveAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
