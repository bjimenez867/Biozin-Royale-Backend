using System.Security.Cryptography;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations.Blackjack;

// Motor de reglas puro: sin red, sin DB, sin timers. El RoomManager (capa superior)
// decide CUÁNDO se juega cada turno (humanos por SignalR, bots con delay) y maneja
// el dinero real; este motor solo decide QUÉ es válido y cuánto paga cada mano.

// ── Cartas ────────────────────────────────────────────────────────────────────

/// R = rango ("A".."K"), S = palo ("S","H","D","C"). Mismos nombres cortos que
/// usa el frontend en cards.logic.ts para que el JSON viaje sin adaptadores.
public sealed record BjCard(string R, string S);

public static class BjRules
{
    public static readonly string[] Ranks = ["A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K"];
    public static readonly string[] Suits = ["S", "H", "D", "C"];

    public static int BaseValue(string rank) => rank switch
    {
        "A" => 11,
        "J" or "Q" or "K" => 10,
        _ => int.Parse(rank),
    };

    /// Mejor total de la mano: los ases valen 11 y bajan a 1 mientras se pase de 21.
    public static (int Total, bool Soft) HandValue(IReadOnlyList<BjCard> cards)
    {
        int total = 0, aces = 0;
        foreach (var c in cards)
        {
            if (c.R == "A") { aces++; total += 11; }
            else total += BaseValue(c.R);
        }
        while (total > 21 && aces > 0) { total -= 10; aces--; }
        return (total, aces > 0 && total <= 21);
    }

    /// Natural: 21 con las dos cartas iniciales (no cuenta en manos divididas).
    public static bool IsBlackjack(IReadOnlyList<BjCard> cards)
        => cards.Count == 2 && HandValue(cards).Total == 21;
}

// ── Zapato ────────────────────────────────────────────────────────────────────

public sealed class BjShoe
{
    private const int Decks = 6;
    // Se rebaraja al quedar pocas cartas (equivalente a la "cut card" de casino).
    // 60 alcanza de sobra para una ronda completa de 4 jugadores + crupier.
    private const int ReshuffleAt = 60;

    private readonly List<BjCard> _cards = [];

    public BjCard Draw()
    {
        if (_cards.Count < ReshuffleAt) Refill();
        var card = _cards[^1];
        _cards.RemoveAt(_cards.Count - 1);
        return card;
    }

    private void Refill()
    {
        _cards.Clear();
        for (int d = 0; d < Decks; d++)
            foreach (var s in BjRules.Suits)
                foreach (var r in BjRules.Ranks)
                    _cards.Add(new BjCard(r, s));

        // Fisher-Yates con RNG criptográfico: el orden no es predecible ni
        // reproducible desde el cliente (a diferencia del Math.random del sim viejo).
        for (int i = _cards.Count - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
        }
    }
}

// ── Manos y asientos ──────────────────────────────────────────────────────────

public enum BjHandResult { Pending, Blackjack, Win, Push, Lose, Surrender }
public enum BjAction { Hit, Stand, Double, Split, Surrender }

public sealed class BjHand
{
    public List<BjCard> Cards { get; } = [];
    public decimal Bet { get; set; }
    public bool Done { get; set; }
    public BjHandResult Result { get; set; } = BjHandResult.Pending;
    public bool FromSplit { get; init; }
    /// Retorno total a acreditar al liquidar (incluye la apuesta devuelta).
    public decimal Payout { get; set; }

    public int Total => BjRules.HandValue(Cards).Total;
    public bool Busted => Total > 21;
}

public sealed class BjSeat
{
    public required int Index { get; init; }
    /// null = bot (el RoomManager juega por él).
    public Guid? UserId { get; init; }
    public required string Name { get; init; }
    public bool IsBot => UserId is null;

    public List<BjHand> Hands { get; } = [];
    public int ActiveHand { get; set; }

    public BjHand? CurrentHand =>
        ActiveHand < Hands.Count && !Hands[ActiveHand].Done ? Hands[ActiveHand] : null;

    public bool AllDone => Hands.All(h => h.Done);
}

// ── Ronda ─────────────────────────────────────────────────────────────────────

public sealed class BjRound
{
    private readonly BjShoe _shoe;

    public IReadOnlyList<BjSeat> Seats { get; }
    public List<BjCard> DealerCards { get; } = [];
    public bool HoleHidden { get; private set; } = true;
    public bool Settled { get; private set; }

    public BjRound(BjShoe shoe, IEnumerable<BjSeat> seatsWithBets)
    {
        _shoe = shoe;
        Seats = seatsWithBets.Where(s => s.Hands.Count > 0 && s.Hands[0].Bet > 0).ToList();
        if (Seats.Count == 0)
            throw new InvalidOperationException("Una ronda necesita al menos un asiento con apuesta.");
    }

    /// Reparte dos vueltas (jugadores y crupier) y marca los blackjacks naturales.
    public void DealInitial()
    {
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var seat in Seats)
                seat.Hands[0].Cards.Add(_shoe.Draw());
            DealerCards.Add(_shoe.Draw());
        }

        foreach (var seat in Seats)
        {
            var hand = seat.Hands[0];
            if (BjRules.IsBlackjack(hand.Cards))
            {
                hand.Done = true;
                hand.Result = BjHandResult.Blackjack; // provisional: Settle lo baja a Push si el crupier también tiene BJ
            }
        }
    }

    /// Próximo asiento/mano que debe actuar, en orden de asiento. null = turnos terminados.
    public (BjSeat Seat, BjHand Hand)? CurrentTurn
    {
        get
        {
            foreach (var seat in Seats)
            {
                // Avanza el puntero de mano dividida saltando manos ya cerradas
                while (seat.ActiveHand < seat.Hands.Count && seat.Hands[seat.ActiveHand].Done)
                    seat.ActiveHand++;
                if (seat.ActiveHand < seat.Hands.Count)
                    return (seat, seat.Hands[seat.ActiveHand]);
            }
            return null;
        }
    }

    // Validez por reglas. Los fondos para Double/Split los verifica el RoomManager
    // contra la wallet real ANTES de llamar Apply; aquí solo se valida el juego.
    public bool CanDouble(BjSeat s) => s.CurrentHand is { Cards.Count: 2 };
    public bool CanSurrender(BjSeat s) => s.CurrentHand is { Cards.Count: 2, FromSplit: false } && s.Hands.Count == 1;
    public bool CanSplit(BjSeat s) =>
        s.Hands.Count == 1 && s.CurrentHand is { Cards.Count: 2 } h
        && BjRules.BaseValue(h.Cards[0].R) == BjRules.BaseValue(h.Cards[1].R);

    /// Aplica la acción del asiento en turno. Lanza si no es su turno o la acción
    /// no es válida — el Hub convierte eso en un error hacia el cliente.
    public void Apply(int seatIndex, BjAction action)
    {
        var turn = CurrentTurn ?? throw new InvalidOperationException("No hay turnos pendientes.");
        if (turn.Seat.Index != seatIndex)
            throw new InvalidOperationException("No es el turno de este asiento.");

        var (seat, hand) = turn;

        switch (action)
        {
            case BjAction.Hit:
                hand.Cards.Add(_shoe.Draw());
                if (hand.Busted) { hand.Done = true; hand.Result = BjHandResult.Lose; }
                break;

            case BjAction.Stand:
                hand.Done = true;
                break;

            case BjAction.Double:
                if (!CanDouble(seat)) throw new InvalidOperationException("No se puede doblar.");
                hand.Bet *= 2;
                hand.Cards.Add(_shoe.Draw());
                hand.Done = true;
                if (hand.Busted) hand.Result = BjHandResult.Lose;
                break;

            case BjAction.Split:
                if (!CanSplit(seat)) throw new InvalidOperationException("No se puede dividir.");
                var second = hand.Cards[1];
                hand.Cards.RemoveAt(1);
                hand.Cards.Add(_shoe.Draw());
                var newHand = new BjHand { Bet = hand.Bet, FromSplit = true };
                newHand.Cards.Add(second);
                newHand.Cards.Add(_shoe.Draw());
                seat.Hands.Add(newHand);
                break;

            case BjAction.Surrender:
                if (!CanSurrender(seat)) throw new InvalidOperationException("No se puede rendir.");
                hand.Done = true;
                hand.Result = BjHandResult.Surrender;
                break;
        }
    }

    /// El crupier revela y pide hasta 17 (se planta en todo 17, incluido soft).
    /// Si nadie sigue vivo (todos pasados/rendidos/BJ) solo revela, sin pedir.
    /// El RoomManager lo llama en loop con delay para animar carta por carta.
    public void PlayDealerStep(out bool needsMoreCards)
    {
        HoleHidden = false;

        var anyLive = Seats.SelectMany(s => s.Hands)
            .Any(h => h.Result == BjHandResult.Pending && !h.Busted);

        if (anyLive && BjRules.HandValue(DealerCards).Total < 17)
            DealerCards.Add(_shoe.Draw());

        needsMoreCards = anyLive && BjRules.HandValue(DealerCards).Total < 17;
    }

    /// Cierra la ronda: fija Result y Payout de cada mano.
    public void Settle()
    {
        if (Settled) return;
        HoleHidden = false;

        var dealerTotal = BjRules.HandValue(DealerCards).Total;
        var dealerBust = dealerTotal > 21;
        var dealerBJ = BjRules.IsBlackjack(DealerCards);

        foreach (var seat in Seats)
        {
            foreach (var hand in seat.Hands)
            {
                hand.Done = true;
                switch (hand)
                {
                    case { Result: BjHandResult.Surrender }:
                        hand.Payout = hand.Bet / 2m;
                        break;

                    case { Busted: true }:
                        hand.Result = BjHandResult.Lose;
                        hand.Payout = 0m;
                        break;

                    case { Result: BjHandResult.Blackjack }:
                        if (dealerBJ) { hand.Result = BjHandResult.Push; hand.Payout = hand.Bet; }
                        else hand.Payout = hand.Bet * 2.5m; // natural paga 3:2
                        break;

                    default:
                        var t = hand.Total;
                        if (dealerBJ) { hand.Result = BjHandResult.Lose; hand.Payout = 0m; }
                        else if (dealerBust || t > dealerTotal) { hand.Result = BjHandResult.Win; hand.Payout = hand.Bet * 2m; }
                        else if (t == dealerTotal) { hand.Result = BjHandResult.Push; hand.Payout = hand.Bet; }
                        else { hand.Result = BjHandResult.Lose; hand.Payout = 0m; }
                        break;
                }
                hand.Payout = Math.Round(hand.Payout, 2);
            }
        }

        Settled = true;
    }

    // ── Estrategia de bots ────────────────────────────────────────────────────
    // Regla simple estilo crupier: pedir con menos de 17. Con soft 17 también pide
    // (una mano soft no puede pasarse con una carta, así que mejora su esperanza).
    public static BjAction BotDecide(BjHand hand)
    {
        var (total, soft) = BjRules.HandValue(hand.Cards);
        if (total < 17 || (soft && total == 17)) return BjAction.Hit;
        return BjAction.Stand;
    }
}
