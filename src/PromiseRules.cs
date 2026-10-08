namespace MenaceSnipersPromise;

public static class PromiseRules
{
    public const float StationaryBonus = 15f;
    public const float MinimumChance = 30f;
    public const float SniperBonus = 10f;
    public const float SniperMinimumChance = 50f;

    // MENACE's HitChance.FinalValue is expressed in percentage points.
    public static float Apply(float chance, bool isPrimaryWeapon, bool isSniperAttack, int tilesMoved,
        bool hasPromise, bool hasRooftops, bool alwaysHits = false)
    {
        if (tilesMoved != 0 || alwaysHits || !float.IsFinite(chance)) return chance;
        // Exclusive branches: a sniper never receives the initial perk, even before tier 3 is learned.
        if (isSniperAttack)
            return hasRooftops ? Math.Clamp(chance + SniperBonus, SniperMinimumChance, 100f) : chance;
        return isPrimaryWeapon && hasPromise ? Math.Clamp(chance + StationaryBonus, MinimumChance, 100f) : chance;
    }
}
