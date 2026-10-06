namespace AutoAnthonyCombatSolverBridge.CombatSolver;

/// <summary>Order and filters match the pinned AutoAnthony AfterCardDrawn hook.</summary>
internal static class ChaosDrawEventPolicy
{
    internal static string[] Events(bool strike, bool ethereal, bool status, bool statusAlreadyDrawn,
        bool fromHandDraw, bool ownerTurn)
    {
        var events = new List<string>();
        if (strike) events.Add("strike_card_drawn");
        if (!fromHandDraw && ownerTurn) events.Add("card_drawn_during_turn");
        events.Add("card_drawn");
        if (ethereal) events.Add("ethereal_card_drawn");
        if (status && !statusAlreadyDrawn) events.Add("first_status_drawn_each_turn");
        return events.ToArray();
    }
}
