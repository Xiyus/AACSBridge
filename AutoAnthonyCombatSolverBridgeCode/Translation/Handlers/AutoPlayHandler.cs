using AutoAnthony;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Extensions;
using AutoAnthonyCombatSolverBridge.CombatSolver;

namespace AutoAnthonyCombatSolverBridge.Translation.Handlers;

public sealed class AutoPlayHandler : IOperationHandler
{
    public string Describe => "嵌套自动出牌：按当前分支牌堆逐张执行";
    public string? ValidateSupport(OperationShape shape) => shape.Spec.Variant is
        "i_playtopxcards" or "i_playtopcardandexhaust" or "i_playthiscard" or "r_playthiscard"
        or "i_autoplayrandomattackfromhand" or "d_autoplayrandomattackfromdraw" or "i_playatrandomenemy"
        or "i_playexhaustedshivsattarget" or "r_playselectedskillmultipletimes" or "cl_proxyatomic_catastrophe"
        or "cl_proxyatomic_beatdown" or "i_proxyatomic_eidolon" or "cl_playtopdrawcard" ? null : "自动出牌形状未适配";

    internal static PredictedCard? LiveSource(OperationExecutionContext context)
    {
        var simulator = context.Mirror.Simulator;
        if (context.Mirror.Card.GetPile(simulator.State) is not null) return context.Mirror.Card;
        var source = context.Card;
        var cards = context.Mirror.OwnerState.AllCards;
        if (source.DeckVersion is { } deck)
        {
            var indexed = cards.FirstOrDefault(candidate => ReferenceEquals(candidate.Preview.DeckVersion, deck));
            if (indexed is not null) return indexed;
        }
        return cards.FirstOrDefault(candidate => candidate.Preview is ChaosCardModel chaos
            && chaos.Definition.Slot == source.Definition.Slot && chaos.Generated.Character == source.Generated.Character
            && chaos.RuntimeProfileId == source.RuntimeProfileId && chaos.EffectiveDefinitionPayload == source.EffectiveDefinitionPayload);
    }

    public void Execute(OperationExecutionContext context)
    {
        var simulator = context.Mirror.Simulator;
        var owner = context.Card.Owner;
        var player = context.Mirror.OwnerState;
        var variant = context.Shape.Spec.Variant;
        if (variant == "i_proxyatomic_eidolon")
        {
            foreach (var card in player.ExhaustPile.Cards.Where(card => card.Preview.Keywords.Contains(CardKeyword.Ethereal)
                         && !card.Preview.Keywords.Contains(CardKeyword.Unplayable)).ToArray())
            {
                Play(card);
                if (simulator.HasPendingChoice) break;
            }
            return;
        }
        if (variant == "cl_proxyatomic_beatdown")
        {
            var attacks = player.DiscardPile.Cards.Where(card => card.Preview.Type == CardType.Attack
                    && !card.Preview.Keywords.Contains(CardKeyword.Unplayable)).ToList()
                .StableShuffle(context.Mirror.Rng.Shuffle).Take(Math.Max(1, context.Card.OperationAmount(context.Shape.OperationIndex))).ToArray();
            foreach (var card in attacks)
            {
                if (simulator.IsOverOrEnding) break;
                var target = card.Preview.TargetType == TargetType.AnyEnemy
                    ? context.Mirror.Rng.CombatTargets.NextItem(context.Mirror.CombatState.HittableEnemies) : null;
                if (card.Preview.TargetType == TargetType.AnyEnemy && target is null) break;
                simulator.AutoPlay(card, target, nestedChoiceSourceId: context.Card.Id.Entry);
                if (simulator.HasPendingChoice) break;
            }
            return;
        }
        if (variant == "cl_proxyatomic_catastrophe")
        {
            for (var index = 0; index < Math.Max(1, context.Card.OperationAmount(context.Shape.OperationIndex)); index++)
            {
                var draw = player.DrawPile.Cards.ToList();
                if (draw.Count == 0) break;
                var playable = draw.Where(card => !card.Preview.Keywords.Contains(CardKeyword.Unplayable)).ToList();
                var selected = (playable.Count > 0 ? playable : draw).StableShuffle(context.Mirror.Rng.Shuffle).FirstOrDefault();
                if (selected is null) break;
                var target = selected.Preview.TargetType == TargetType.AnyEnemy
                    ? context.Mirror.Rng.CombatTargets.NextItem(context.Mirror.CombatState.HittableEnemies) : null;
                if (selected.Preview.TargetType == TargetType.AnyEnemy && target is null) break;
                simulator.AutoPlay(selected, target, nestedChoiceSourceId: context.Card.Id.Entry);
                if (simulator.HasPendingChoice) break;
            }
            return;
        }
        if (variant is "i_playthiscard" or "r_playthiscard")
        {
            if (LiveSource(context) is { } live) Play(live);
            return;
        }
        if (variant == "i_playatrandomenemy")
        {
            var selected = context.EventCard ?? context.Resolution?.IterationCard
                ?? context.Resolution?.CardSelections.Values.Select(cards => cards.FirstOrDefault()).FirstOrDefault(card => card is not null);
            if (selected is not null) Play(selected);
            return;
        }
        if (variant is "i_playtopxcards" or "i_playtopcardandexhaust" or "cl_playtopdrawcard")
        {
            var count = variant == "i_playtopxcards" ? context.RuntimeValue("amount", context.Card.ResolveEffectEnergyXValue()) : 1;
            for (var index = 0; index < Math.Max(0, count); index++)
            {
                if (simulator.IsOverOrEnding || simulator.State.GetCreature(owner.Creature).IsDead) break;
                simulator.ShuffleIfNecessary(owner);
                if (simulator.HasPendingChoice) break;
                var next = player.DrawPile.Cards.FirstOrDefault();
                if (next is null) break;
                next.MutablePreview.ExhaustOnNextPlay = variant == "i_playtopcardandexhaust";
                Play(next);
                if (simulator.HasPendingChoice) break;
            }
            return;
        }
        if (variant == "i_playexhaustedshivsattarget")
        {
            foreach (var card in player.ExhaustPile.Cards.Where(candidate => ChaosDerivativeResolver.Matches(candidate.Preview, context.Operation)).ToArray())
            {
                if (ChaosOperationExecutor.DerivativeIsUpgraded(context.Card, context.Shape.OperationIndex) && card.Preview.IsUpgradable) simulator.Upgrade(card);
                simulator.AutoPlay(card, context.Target, nestedChoiceSourceId: context.Card.Id.Entry);
                if (simulator.HasPendingChoice) break;
            }
            return;
        }
        if (variant == "r_playselectedskillmultipletimes")
        {
            if (context.SelectedCards.FirstOrDefault() is { } card)
                for (var index = 0; index < ChaosOperationExecutor.ExecutableOperationCount(context.Operation, context.ExecutableAmount); index++)
                {
                    Play(card);
                    if (simulator.HasPendingChoice) break;
                }
            return;
        }
        var played = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < Math.Max(0, context.ExecutableAmount); index++)
        {
            if (simulator.IsOverOrEnding) break;
            var pile = variant == "d_autoplayrandomattackfromdraw" ? player.DrawPile : player.Hand;
            var candidates = pile.Cards.Where(card => card.Preview.Type == CardType.Attack
                && !card.Preview.Keywords.Contains(CardKeyword.Unplayable) && !played.Contains(card.Original)).ToList();
            var selected = variant == "d_autoplayrandomattackfromdraw"
                ? context.Mirror.Rng.CombatCardSelection.NextItem(candidates) : context.Mirror.Rng.Shuffle.NextItem(candidates);
            if (selected is null) break;
            played.Add(selected.Original);
            Play(selected);
            if (simulator.HasPendingChoice) break;
        }
        void Play(PredictedCard card) => simulator.AutoPlay(card, nestedChoiceSourceId: context.Card.Id.Entry);
    }
}
