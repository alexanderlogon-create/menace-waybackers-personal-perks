namespace MenaceSnipersPromise;

internal static class WaybackerRules
{
    internal static int Discount(int cost, float mult) => cost == 0 ? 0 : System.Math.Max(1, (int)System.Math.Ceiling(cost * (double)mult - 0.00001));
    internal static int MovementCost(string name, int cost, int ordinaryCost, bool escape, bool walker, bool backwards)
    {
        if (name == "Lucky" && cost > ordinaryCost) return System.Math.Max(ordinaryCost, Discount(cost, .8f));
        if (name == "Rook" && backwards) return Discount(cost, .75f);
        if (name == "Viper" && walker) return System.Math.Max(1, cost - 2);
        if ((name == "Talia" || name == "Vale") && escape) return Discount(cost, .75f);
        return cost;
    }
}
