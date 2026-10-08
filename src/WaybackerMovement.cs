using Il2CppMenace.Tactical;
using UnityEngine;
using Math = System.Math;
using VectorPath = Il2CppSystem.Collections.Generic.List<UnityEngine.Vector3>;

namespace MenaceSnipersPromise;

internal sealed class WaybackerPathState
{
    internal bool Owned, Restored;
    internal WaybackerPathPlan? Plan;
}
internal sealed class WaybackerPathPlan
{
    internal int[] NativeCosts = Array.Empty<int>();
    internal int[] Savings = Array.Empty<int>();
}
internal static class WaybackerMovement
{
    internal static WaybackerPathState Begin(Actor actor)
    {
        var name = WaybackerPerks.Current.Definition(actor)?.Name;
        var state = new WaybackerPathState { Owned = WaybackerMovementQuery.BypassDepth == 0 && name is "Lucky" or "Rook" or "Viper" or "Talia" or "Vale" };
        if (state.Owned) ++WaybackerMovementQuery.BypassDepth;
        return state;
    }
    internal static void End(WaybackerPathState state)
    {
        if (!state.Owned || state.Restored) return;
        state.Restored = true; --WaybackerMovementQuery.BypassDepth;
    }
    // Ask the native engine for unmodified prefix costs. This also preserves terrain,
    // diagonal movement, learned perks, entering/leaving and the game's own rounding.
    private static int NativeCost(MovementType movement, VectorPath path, int end, MovementAction action, Actor actor, Direction dir)
    {
        var prefix = new VectorPath();
        for (var i = 0; i <= end; i++) prefix.Add(path[i]);
        ++WaybackerMovementQuery.BypassDepth;
        try { return movement.GetTotalPathCost(prefix, action, actor, dir); }
        finally { --WaybackerMovementQuery.BypassDepth; }
    }

    internal static WaybackerPathPlan Plan(MovementType movement, VectorPath path, MovementAction action, Actor actor, Direction initialDir)
    {
        var plan = new WaybackerPathPlan { NativeCosts = new int[path.Count], Savings = new int[path.Count] };
        if (path.Count == 0) return plan;
        var definition = WaybackerPerks.Current.Definition(actor);
        var map = TacticalManager.Get()?.GetMap();
        if (definition == null || map == null) return plan;
        var state = WaybackerPerks.Current.Turn(actor);
        var properties = actor.GetCurrentProperties();
        var dir = initialDir;
        var moveAction = action & MovementAction.Backwards;
        var movePrevious = NativeCost(movement, path, 0, moveAction, actor, initialDir);
        int spentEscape = 0, saved = 0;
        var ordinaryCost = int.MaxValue;
        ++WaybackerMovementQuery.BypassDepth;
        try
        {
            for (var surface = 0; surface < Math.Min((int)SurfaceType.COUNT, movement.m_MovementCosts.Length); surface++)
            {
                var cost = movement.GetMovementCostForTileType(surface, actor);
                if (cost > 0) ordinaryCost = Math.Min(ordinaryCost, cost);
            }
            for (var i = 0; i < path.Count; i++)
            {
                var prefixAction = i == path.Count - 1 ? action : action & ~MovementAction.Enter;
                plan.NativeCosts[i] = NativeCost(movement, path, i, prefixAction, actor, initialDir);
                if (i == 0) continue;
                var previousTile = map.GetTileAtPos(path[i - 1]);
                var tile = map.GetTileAtPos(path[i]);
                if (previousTile == null || tile == null) { plan.Savings[i] = saved; continue; }
                var steps = previousTile.GetDistanceTo(tile);
                if (steps == 0) { plan.Savings[i] = saved; continue; }
                var nextDir = previousTile.GetDirectionTo(tile);
                if ((action & MovementAction.Backwards) != 0) nextDir = nextDir.Flip();
                var turningCost = Actor.GetTurningCost(dir, nextDir, movement, properties);
                dir = nextDir;
                var nativeMovePrefix = NativeCost(movement, path, i, moveAction, actor, initialDir);
                var delta = Math.Max(0, nativeMovePrefix - movePrevious);
                movePrevious = nativeMovePrefix;
                turningCost = Math.Clamp(turningCost, 0, delta);
                var moveCost = delta - turningCost;
                int save = 0;
                switch (definition.Name)
                {
                    case "Talia":
                    case "Vale":
                        if (state.EscapeEnds > 0 && spentEscape < state.EscapeTiles)
                        {
                            var eligible = Math.Min(steps, state.EscapeTiles - spentEscape);
                            var eligibleCost = moveCost * eligible / steps;
                            save = eligibleCost - WaybackerRules.Discount(eligibleCost, .75f);
                            spentEscape += eligible;
                        }
                        break;
                    case "Viper":
                        if (WaybackerPerks.Walker(actor)) save = Math.Min(2 * steps, Math.Max(0, moveCost - steps));
                        break;
                    case "Rook":
                        save = turningCost - WaybackerRules.Discount(turningCost, .75f);
                        if ((action & MovementAction.Backwards) != 0) save += moveCost - WaybackerRules.Discount(moveCost, .75f);
                        break;
                    case "Lucky":
                        var tileCost = movement.GetMovementCostForTileType((int)tile.m_SurfaceType, actor);
                        if (tileCost > ordinaryCost)
                        {
                            var reduced = Math.Max(ordinaryCost, WaybackerRules.Discount(tileCost, .8f));
                            var reducedMove = (int)Math.Ceiling(moveCost * (double)reduced / tileCost);
                            save = Math.Max(0, moveCost - reducedMove);
                        }
                        break;
                }
                saved += Math.Max(0, save);
                plan.Savings[i] = saved;
            }
        }
        finally { --WaybackerMovementQuery.BypassDepth; }
        return plan;
    }
}

