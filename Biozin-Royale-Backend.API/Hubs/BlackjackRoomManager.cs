using Microsoft.AspNetCore.SignalR;
using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.LogicaNegocio.Implementations.Blackjack;

namespace Biozin_Royale_Backend.API.Hubs;

// Orquestador de las mesas de blackjack. Vive como singleton: el estado de las
// salas es en memoria (exige una sola instancia de App Service). Todo el dinero
// pasa por aquí contra la wallet real; el cliente jamás reporta saldos.

public sealed class BjRoomPlayer
{
    public required Guid UserId { get; init; }
    public required string Name { get; init; }
    public string? AvatarUrl { get; init; }
    public string? ConnectionId { get; set; }
    /// Silla 0-3. null = entró a mitad de ronda y espera la siguiente.
    public int? Chair { get; set; }
    public decimal Bet { get; set; }
    /// Fila de games_history (status=pending) de la ronda en curso.
    public Guid? HistoryId { get; set; }
    /// Asiento del motor en la ronda en curso (null si no apostó).
    public BjSeat? Seat { get; set; }
    public int MissedRounds { get; set; }
}

public sealed class BjBot
{
    public required int Chair { get; init; }
    public required string Name { get; init; }
    public required string Avatar { get; init; }
    public BjSeat? Seat { get; set; }
}

public sealed class BjRoom
{
    public required int Id { get; init; }
    public required decimal Min { get; init; }
    public required decimal Max { get; init; }

    public readonly SemaphoreSlim Sem = new(1, 1);
    public readonly BjShoe Shoe = new();
    public readonly List<BjRoomPlayer> Players = [];
    public readonly List<BjBot> Bots = [];

    public string State = "waiting"; // waiting | starting | betting | dealing | acting | dealer | settled
    public DateTime? PhaseEndsUtc;
    public Guid RoundId;
    public BjRound? Round;
    public int? TurnChair;
    public BjSeat? TurnSeatRef;
    public TaskCompletionSource<BjAction>? TurnTcs;
    public TaskCompletionSource<bool>? BetsClosedTcs;
    public bool LoopRunning;

    public string Group => $"bj:{Id}";
}

public sealed class BlackjackRoomManager
{
    private const int MaxSeats = 4;
    private const int StartingSeconds = 30;   // espera en sala antes de completar con bots
    private const int BettingSeconds = 15;
    private const int TurnSeconds = 20;
    private const int IntermissionMs = 4500;
    private const int MaxMissedRounds = 3;    // rondas sin apostar antes de expulsar

    private static readonly (string Name, string Avatar)[] BotProfiles =
    [
        ("Valeria", "assets/bj-av1.png"),
        ("Andrés",  "assets/bj-av2.png"),
        ("Camila",  "assets/bj-av3.png"),
    ];

    private readonly List<BjRoom> _rooms;
    private readonly IHubContext<BlackjackHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BlackjackRoomManager> _logger;

    public BlackjackRoomManager(
        IHubContext<BlackjackHub> hub,
        IServiceScopeFactory scopeFactory,
        ILogger<BlackjackRoomManager> logger)
    {
        _hub = hub;
        _scopeFactory = scopeFactory;
        _logger = logger;

        // Mismas 6 mesas que muestra el lobby del frontend (tables.data.ts)
        _rooms =
        [
            new BjRoom { Id = 1, Min = 10,  Max = 1000  },
            new BjRoom { Id = 2, Min = 25,  Max = 2500  },
            new BjRoom { Id = 3, Min = 50,  Max = 5000  },
            new BjRoom { Id = 4, Min = 100, Max = 10000 },
            new BjRoom { Id = 5, Min = 250, Max = 25000 },
            new BjRoom { Id = 6, Min = 10,  Max = 1000  },
        ];
    }

    // ── Lobby ─────────────────────────────────────────────────────────────────

    public object GetLobbySummary() => _rooms.Select(r => new
    {
        id = r.Id,
        min = r.Min,
        max = r.Max,
        players = r.Players.Count(p => p.ConnectionId != null),
        maxPlayers = MaxSeats,
        state = r.State,
        // Solo el countdown pre-ronda tiene sentido en el lobby ("inicia en");
        // los deadlines de turnos individuales no se exponen
        phaseEndsUtc = r.State is "starting" or "betting" ? r.PhaseEndsUtc : null,
    }).ToList();

    private Task BroadcastLobbyAsync() =>
        _hub.Clients.Group("bj:lobby").SendAsync("lobby", GetLobbySummary());

    // ── Entrar / salir ────────────────────────────────────────────────────────

    public async Task<object> JoinAsync(int roomId, Guid userId, string connectionId)
    {
        var room = _rooms.FirstOrDefault(r => r.Id == roomId)
            ?? throw new HubException("La mesa no existe.");

        // Perfil ANTES del lock (round-trip a DB)
        string name;
        string? avatar;
        using (var scope = _scopeFactory.CreateScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitWork>();
            var perfil = await uow.Profiles.ObtenerEntidadAsync(p => p.UserId == userId)
                ?? throw new HubException("Perfil no encontrado.");
            if (perfil.IsGuest)
                throw new HubException("Crea una cuenta para jugar con dinero real.");
            name = perfil.DisplayName ?? perfil.Username;
            avatar = null; // el avatar del jugador lo resuelve el frontend con su propio perfil
        }

        await room.Sem.WaitAsync();
        try
        {
            var player = room.Players.FirstOrDefault(p => p.UserId == userId);
            if (player is not null)
            {
                player.ConnectionId = connectionId; // reconexión: conserva silla y apuesta
            }
            else
            {
                if (room.Players.Count(p => p.ConnectionId != null) >= MaxSeats)
                    throw new HubException("Mesa llena.");
                player = new BjRoomPlayer
                {
                    UserId = userId,
                    Name = name,
                    AvatarUrl = avatar,
                    ConnectionId = connectionId,
                };
                room.Players.Add(player);
            }

            // Silla de inmediato si no hay ronda en curso: así la sala de espera
            // muestra "N/4" real. A mitad de ronda queda de espectador y la toma
            // al abrirse las apuestas.
            AsignarSilla(room, player);

            if (!room.LoopRunning)
            {
                room.LoopRunning = true;
                // El estado "starting" y su deadline se fijan AQUÍ (bajo el lock)
                // y no en el loop: así el snapshot que devuelve este join ya trae
                // el countdown y el primer jugador lo ve sin esperar un broadcast.
                room.State = "starting";
                room.PhaseEndsUtc = DateTime.UtcNow.AddSeconds(StartingSeconds);
                _ = Task.Run(() => RunLoopAsync(room));
            }

            var snap = BuildSnapshot(room);
            _ = BroadcastLobbyAsync();
            _ = _hub.Clients.GroupExcept(room.Group, connectionId).SendAsync("state", snap);
            return snap;
        }
        finally { room.Sem.Release(); }
    }

    public async Task LeaveAsync(string connectionId)
    {
        foreach (var room in _rooms)
        {
            await room.Sem.WaitAsync();
            try
            {
                var player = room.Players.FirstOrDefault(p => p.ConnectionId == connectionId);
                if (player is null) continue;

                player.ConnectionId = null;

                // Sin ronda activa (o sin apuesta en la ronda) se puede ir ya;
                // con apuesta viva se queda hasta liquidar para no perder su pago.
                if (player.Seat is null)
                    room.Players.Remove(player);
                // Si era su turno, que no bloquee: el timeout del loop lo planta solo.

                await _hub.Clients.Group(room.Group).SendAsync("state", BuildSnapshot(room));
                _ = BroadcastLobbyAsync();
            }
            finally { room.Sem.Release(); }
        }
    }

    // ── Acciones del jugador (llegan desde el Hub, ya autenticadas) ───────────

    public async Task PlaceBetAsync(int roomId, Guid userId, decimal amount)
    {
        var room = GetRoom(roomId);
        await room.Sem.WaitAsync();
        try
        {
            if (room.State != "betting")
                throw new HubException("No es momento de apostar.");

            var player = room.Players.FirstOrDefault(p => p.UserId == userId)
                ?? throw new HubException("No estás en esta mesa.");
            if (player.Bet > 0)
                throw new HubException("Ya apostaste en esta ronda.");
            if (amount < room.Min || amount > room.Max)
                throw new HubException($"La apuesta debe estar entre {room.Min:C0} y {room.Max:C0}.");

            // Débito real + registro pending (dentro del lock: la ventana de
            // apuestas es tolerante a ~100ms y así no hay carreras con el cierre)
            var historyId = Guid.NewGuid();
            if (!await TryDebitAsync(userId, amount, room.RoundId, historyId, isInitial: true))
                throw new HubException("Saldo insuficiente.");

            player.Bet = amount;
            player.HistoryId = historyId;
            player.MissedRounds = 0;

            // Si ya apostaron todos los humanos conectados, la ronda arranca sin esperar
            if (room.Players.Where(p => p.ConnectionId != null).All(p => p.Bet > 0))
                room.BetsClosedTcs?.TrySetResult(true);

            await _hub.Clients.Group(room.Group).SendAsync("state", BuildSnapshot(room));
        }
        finally { room.Sem.Release(); }
    }

    public async Task SubmitActionAsync(int roomId, Guid userId, BjAction action)
    {
        var room = GetRoom(roomId);
        await room.Sem.WaitAsync();
        try
        {
            var player = room.Players.FirstOrDefault(p => p.UserId == userId)
                ?? throw new HubException("No estás en esta mesa.");

            if (room.Round is null || room.TurnSeatRef?.UserId != userId
                || room.TurnTcs is null || room.TurnTcs.Task.IsCompleted)
                throw new HubException("No es tu turno.");

            var seat = room.TurnSeatRef;

            switch (action)
            {
                case BjAction.Double:
                    if (!room.Round.CanDouble(seat)) throw new HubException("No puedes doblar ahora.");
                    if (!await TryDebitAsync(userId, seat.CurrentHand!.Bet, room.RoundId, player.HistoryId!.Value, isInitial: false))
                        throw new HubException("Saldo insuficiente para doblar.");
                    break;
                case BjAction.Split:
                    if (!room.Round.CanSplit(seat)) throw new HubException("No puedes dividir ahora.");
                    if (!await TryDebitAsync(userId, seat.CurrentHand!.Bet, room.RoundId, player.HistoryId!.Value, isInitial: false))
                        throw new HubException("Saldo insuficiente para dividir.");
                    break;
                case BjAction.Surrender:
                    if (!room.Round.CanSurrender(seat)) throw new HubException("No puedes rendirte ahora.");
                    break;
            }

            room.TurnTcs.TrySetResult(action);
        }
        finally { room.Sem.Release(); }
    }

    // ── Loop de la sala ───────────────────────────────────────────────────────

    private async Task RunLoopAsync(BjRoom room)
    {
        try
        {
            while (true)
            {
                // ── Sala de espera: countdown para completar con bots ──
                await room.Sem.WaitAsync();
                try
                {
                    room.Players.RemoveAll(p => p.ConnectionId is null);
                    room.Round = null;
                    room.Bots.Clear();
                    foreach (var p in room.Players) { p.Seat = null; p.Bet = 0; p.HistoryId = null; }

                    if (room.Players.Count == 0)
                    {
                        room.State = "waiting";
                        room.PhaseEndsUtc = null;
                        await _hub.Clients.Group(room.Group).SendAsync("state", BuildSnapshot(room));
                        _ = BroadcastLobbyAsync();
                        return; // el loop se relanza cuando entre alguien
                    }

                    room.State = "starting";
                    // Respeta el deadline que pudo fijar JoinAsync al arrancar el loop
                    if (room.PhaseEndsUtc is null || room.PhaseEndsUtc <= DateTime.UtcNow)
                        room.PhaseEndsUtc = DateTime.UtcNow.AddSeconds(StartingSeconds);
                    await _hub.Clients.Group(room.Group).SendAsync("state", BuildSnapshot(room));
                    _ = BroadcastLobbyAsync();
                }
                finally { room.Sem.Release(); }

                // Espera hasta el deadline, o antes si la mesa se llena o se vacía
                while (DateTime.UtcNow < room.PhaseEndsUtc)
                {
                    await Task.Delay(500);
                    var connected = room.Players.Count(p => p.ConnectionId != null);
                    if (connected >= MaxSeats || connected == 0) break;
                }
                if (!room.Players.Any(p => p.ConnectionId != null)) continue;

                // ── Rondas consecutivas mientras haya humanos ──
                var keepPlaying = true;
                while (keepPlaying)
                    keepPlaying = await PlayRoundAsync(room);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loop de mesa blackjack {RoomId} terminó con error.", room.Id);
        }
        finally
        {
            room.LoopRunning = false;
            room.State = "waiting";
        }
    }

    private async Task<bool> PlayRoundAsync(BjRoom room)
    {
        // ── Apuestas ──
        await room.Sem.WaitAsync();
        try
        {
            room.Players.RemoveAll(p => p.ConnectionId is null);
            if (room.Players.Count == 0) return false;

            // Sillas: los que ya tienen conservan; nuevos toman la libre más baja
            var taken = room.Players.Where(p => p.Chair != null).Select(p => p.Chair!.Value).ToHashSet();
            foreach (var p in room.Players.Where(p => p.Chair is null))
            {
                var free = Enumerable.Range(0, MaxSeats).FirstOrDefault(i => !taken.Contains(i), -1);
                if (free >= 0) { p.Chair = free; taken.Add(free); }
            }

            // Bots ocupan las sillas restantes
            room.Bots.Clear();
            var botIdx = 0;
            foreach (var chair in Enumerable.Range(0, MaxSeats).Where(i => !taken.Contains(i)))
            {
                var profile = BotProfiles[botIdx++ % BotProfiles.Length];
                room.Bots.Add(new BjBot { Chair = chair, Name = profile.Name, Avatar = profile.Avatar });
            }

            room.State = "betting";
            room.RoundId = Guid.NewGuid();
            room.Round = null;
            room.TurnChair = null;
            room.TurnSeatRef = null;
            foreach (var p in room.Players) { p.Bet = 0; p.HistoryId = null; p.Seat = null; }
            room.BetsClosedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            room.PhaseEndsUtc = DateTime.UtcNow.AddSeconds(BettingSeconds);
            await _hub.Clients.Group(room.Group).SendAsync("state", BuildSnapshot(room));
            _ = BroadcastLobbyAsync();
        }
        finally { room.Sem.Release(); }

        await Task.WhenAny(room.BetsClosedTcs!.Task, Task.Delay(BettingSeconds * 1000));

        // ── Armar la ronda ──
        BjRound round;
        await room.Sem.WaitAsync();
        try
        {
            // Expulsión por inactividad (no bloquear la mesa con sillas ociosas)
            foreach (var p in room.Players.Where(p => p.Bet <= 0 && p.ConnectionId != null))
                p.MissedRounds++;
            foreach (var idle in room.Players.Where(p => p.MissedRounds >= MaxMissedRounds).ToList())
            {
                room.Players.Remove(idle);
                if (idle.ConnectionId != null)
                    _ = _hub.Clients.Client(idle.ConnectionId).SendAsync("kicked",
                        "Saliste de la mesa por no apostar en varias rondas.");
            }

            if (!room.Players.Any(p => p.ConnectionId != null)) return false;

            var bettors = room.Players.Where(p => p.Bet > 0 && p.Chair != null).ToList();

            // Sin apuestas humanas la ronda corre igual con los bots: la mesa se ve
            // viva y quien no apostó mira. Solo si tampoco hay bots (4 humanos
            // sentados y nadie apostó) se reabre la ventana de apuestas.
            if (bettors.Count == 0 && room.Bots.Count == 0)
                return true;

            var seats = new List<BjSeat>();
            foreach (var p in bettors)
            {
                var seat = new BjSeat { Index = p.Chair!.Value, UserId = p.UserId, Name = p.Name };
                seat.Hands.Add(new BjHand { Bet = p.Bet });
                p.Seat = seat;
                seats.Add(seat);
            }
            foreach (var bot in room.Bots)
            {
                var seat = new BjSeat { Index = bot.Chair, UserId = null, Name = bot.Name };
                seat.Hands.Add(new BjHand { Bet = BotBet(room.Min, room.Max) });
                bot.Seat = seat;
                seats.Add(seat);
            }

            round = new BjRound(room.Shoe, seats.OrderBy(s => s.Index));
            room.Round = round;
            room.State = "dealing";
            room.PhaseEndsUtc = null;
            round.DealInitial();
            await _hub.Clients.Group(room.Group).SendAsync("state", BuildSnapshot(room));
            _ = BroadcastLobbyAsync();
        }
        finally { room.Sem.Release(); }

        await Task.Delay(1800); // el cliente anima el reparto con este colchón

        // ── Turnos ──
        while (true)
        {
            BjSeat seat;
            BjHand hand;
            bool isHumanConnected;

            await room.Sem.WaitAsync();
            try
            {
                var turn = round.CurrentTurn;
                if (turn is null) break;
                (seat, hand) = turn.Value;

                room.State = "acting";
                room.TurnChair = seat.Index;
                room.TurnSeatRef = seat;
                room.TurnTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                room.PhaseEndsUtc = DateTime.UtcNow.AddSeconds(TurnSeconds);

                var owner = room.Players.FirstOrDefault(p => p.UserId == seat.UserId);
                isHumanConnected = !seat.IsBot && owner?.ConnectionId != null;

                await _hub.Clients.Group(room.Group).SendAsync("state", BuildSnapshot(room));
            }
            finally { room.Sem.Release(); }

            BjAction action;
            if (!isHumanConnected)
            {
                // Bot (o humano desconectado): el servidor decide con una pausa natural
                await Task.Delay(seat.IsBot ? 900 : 400);
                action = seat.IsBot ? BjRound.BotDecide(hand) : BjAction.Stand;
            }
            else
            {
                await Task.WhenAny(room.TurnTcs!.Task, Task.Delay(TurnSeconds * 1000));
                action = BjAction.Stand; // por defecto si venció el tiempo
            }

            await room.Sem.WaitAsync();
            try
            {
                // Si la acción del humano llegó (aunque fuera justo al vencer), gana ella:
                // pudo haber debitado un double/split y no puede convertirse en Stand.
                if (room.TurnTcs!.Task.IsCompletedSuccessfully)
                    action = room.TurnTcs.Task.Result;

                round.Apply(seat.Index, action);
                room.TurnTcs = null;
                room.TurnSeatRef = null;
                await _hub.Clients.Group(room.Group).SendAsync("state", BuildSnapshot(room));
            }
            finally { room.Sem.Release(); }

            await Task.Delay(350);
        }

        // ── Crupier ──
        await room.Sem.WaitAsync();
        try
        {
            room.State = "dealer";
            room.TurnChair = null;
            room.PhaseEndsUtc = null;
        }
        finally { room.Sem.Release(); }

        bool more;
        do
        {
            await room.Sem.WaitAsync();
            try
            {
                round.PlayDealerStep(out more);
                await _hub.Clients.Group(room.Group).SendAsync("state", BuildSnapshot(room));
            }
            finally { room.Sem.Release(); }
            await Task.Delay(450);
        } while (more);

        // ── Liquidación ──
        await room.Sem.WaitAsync();
        try
        {
            round.Settle();
            room.State = "settled";
            await CreditPayoutsAsync(room);
            await _hub.Clients.Group(room.Group).SendAsync("state", BuildSnapshot(room));
        }
        finally { room.Sem.Release(); }

        await Task.Delay(IntermissionMs);

        return room.Players.Any(p => p.ConnectionId != null);
    }

    // ── Dinero (siempre server-side, vía scope propio) ────────────────────────

    private async Task<bool> TryDebitAsync(Guid userId, decimal amount, Guid roundId, Guid historyId, bool isInitial)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitWork>();

            var wallet = await uow.Wallets.ObtenerEntidadAsync(w => w.UserId == userId);
            if (wallet is null || wallet.Balance < amount) return false;

            wallet.Balance = Math.Round(wallet.Balance - amount, 2);
            wallet.UpdatedAt = DateTime.UtcNow;
            uow.Wallets.Modificar(wallet);

            if (isInitial)
            {
                uow.GamesHistory.Insertar(new GamesHistory
                {
                    Id = historyId,
                    UserId = userId,
                    GameType = "blackjack",
                    RoundId = roundId,
                    Amount = amount,
                    Payout = 0m,
                    Profit = 0m,
                    Result = "pending",
                    Status = "pending",
                    CreatedAt = DateTime.UtcNow,
                });
            }
            else
            {
                var row = await uow.GamesHistory.ObtenerEntidadAsync(b => b.Id == historyId);
                if (row is not null)
                {
                    row.Amount = Math.Round(row.Amount + amount, 2);
                    uow.GamesHistory.Modificar(row);
                }
            }

            await uow.CompletarAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error debitando {Amount} al usuario {UserId} en blackjack.", amount, userId);
            return false;
        }
    }

    private async Task CreditPayoutsAsync(BjRoom room)
    {
        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitWork>();
        var now = DateTime.UtcNow;

        foreach (var player in room.Players.Where(p => p.Seat is not null && p.HistoryId is not null))
        {
            var payout = Math.Round(player.Seat!.Hands.Sum(h => h.Payout), 2);
            var stake = Math.Round(player.Seat.Hands.Sum(h => h.Bet), 2);
            var profit = Math.Round(payout - stake, 2);

            decimal? newBalance = null;
            var wallet = await uow.Wallets.ObtenerEntidadAsync(w => w.UserId == player.UserId);
            if (wallet is not null)
            {
                if (payout > 0)
                {
                    wallet.Balance = Math.Round(wallet.Balance + payout, 2);
                    wallet.UpdatedAt = now;
                    uow.Wallets.Modificar(wallet);
                }
                newBalance = wallet.Balance;
            }

            var row = await uow.GamesHistory.ObtenerEntidadAsync(b => b.Id == player.HistoryId!.Value);
            if (row is not null)
            {
                row.Payout = payout;
                row.Profit = profit;
                row.Result = profit > 0 ? "win" : profit == 0 ? "push" : "loss";
                row.Status = "settled";
                row.SettledAt = now;
                uow.GamesHistory.Modificar(row);
            }

            if (player.ConnectionId != null)
                _ = _hub.Clients.Client(player.ConnectionId).SendAsync("roundResult", new
                {
                    payout,
                    profit,
                    newBalance,
                });
        }

        await uow.CompletarAsync();
    }

    // ── Snapshot público (lo único que ve el cliente) ─────────────────────────

    private object BuildSnapshot(BjRoom room)
    {
        var round = room.Round;

        // La carta oculta del crupier NUNCA viaja al cliente hasta revelarse
        List<BjCard> dealerCards = [];
        int? dealerTotal = null;
        var hole = false;
        if (round is not null)
        {
            hole = round.HoleHidden;
            dealerCards = hole ? round.DealerCards.Take(1).ToList() : round.DealerCards;
            if (!hole && dealerCards.Count > 0)
                dealerTotal = BjRules.HandValue(dealerCards).Total;
        }

        var chairs = new object?[MaxSeats];
        foreach (var p in room.Players.Where(p => p.Chair is not null))
            chairs[p.Chair!.Value] = new
            {
                index = p.Chair.Value,
                type = "player",
                userId = p.UserId,
                name = p.Name,
                avatar = p.AvatarUrl,
                connected = p.ConnectionId != null,
                bet = p.Bet,
                hands = SnapHands(p.Seat),
                activeHand = p.Seat?.ActiveHand ?? 0,
            };
        foreach (var b in room.Bots)
            chairs[b.Chair] = new
            {
                index = b.Chair,
                type = "bot",
                name = b.Name,
                avatar = b.Avatar,
                bet = b.Seat?.Hands.Sum(h => h.Bet) ?? 0,
                hands = SnapHands(b.Seat),
                activeHand = b.Seat?.ActiveHand ?? 0,
            };

        return new
        {
            roomId = room.Id,
            state = room.State,
            phaseEndsUtc = room.PhaseEndsUtc,
            min = room.Min,
            max = room.Max,
            turnChair = room.TurnChair,
            dealer = new { cards = dealerCards, hole, total = dealerTotal },
            chairs,
            spectators = room.Players
                .Where(p => p.Chair is null && p.ConnectionId != null)
                .Select(p => new { userId = p.UserId, name = p.Name })
                .ToList(),
        };
    }

    private static List<object>? SnapHands(BjSeat? seat) => seat?.Hands.Select(h => (object)new
    {
        cards = h.Cards,
        bet = h.Bet,
        total = h.Total,
        done = h.Done,
        result = h.Result == BjHandResult.Pending ? null : h.Result.ToString().ToLowerInvariant(),
    }).ToList();

    private BjRoom GetRoom(int roomId) =>
        _rooms.FirstOrDefault(r => r.Id == roomId) ?? throw new HubException("La mesa no existe.");

    /// Silla libre más baja, solo cuando no hay ronda repartida (waiting/starting/
    /// betting). En betting puede desplazar a un bot que aún no recibió cartas.
    private static void AsignarSilla(BjRoom room, BjRoomPlayer player)
    {
        if (player.Chair is not null) return;
        if (room.State is not ("waiting" or "starting" or "betting")) return;

        var taken = room.Players
            .Where(p => p != player && p.Chair != null)
            .Select(p => p.Chair!.Value)
            .ToHashSet();
        var free = Enumerable.Range(0, MaxSeats).FirstOrDefault(i => !taken.Contains(i), -1);
        if (free < 0) return;

        player.Chair = free;
        room.Bots.RemoveAll(b => b.Chair == free);
    }

    private static decimal BotBet(decimal min, decimal max)
    {
        decimal[] steps = [10, 25, 50, 100, 250, 500];
        var pick = steps.Where(s => s >= min && s <= max).ToArray();
        return pick.Length > 0
            ? pick[System.Security.Cryptography.RandomNumberGenerator.GetInt32(pick.Length)]
            : min;
    }
}
