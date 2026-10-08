using MenaceSnipersPromise;

var cases = new (string Name, float Base, bool Primary, bool Sniper, int Moved, bool Promise, bool Rooftops, bool Always, float Expected)[]
{
    ("primary: flat +15", 50, true, false, 0, true, false, false, 65),
    ("primary: low chance floor", 5, true, false, 0, true, false, false, 30),
    ("primary moved: no +15", 50, true, false, 1, true, false, false, 50),
    ("primary moved: no floor", 5, true, false, 4, true, false, false, 5),
    ("primary next turn: bonus returns", 50, true, false, 0, true, false, false, 65),
    ("primary next turn: floor returns", 5, true, false, 0, true, false, false, 30),
    ("primary cap", 95, true, false, 0, true, false, false, 100),
    ("unlearned sniper: no initial bonus", 60, true, true, 0, true, false, false, 60),
    ("unlearned sniper: no initial floor", 5, true, true, 0, true, false, false, 5),
    ("learned sniper: exclusive +10, not +25", 60, true, true, 0, true, true, false, 70),
    ("learned sniper: floor 50", 5, true, true, 0, true, true, false, 50),
    ("learned sniper moved: no +10", 60, true, true, 1, true, true, false, 60),
    ("learned sniper moved: no floor", 5, true, true, 1, true, true, false, 5),
    ("sniper next turn: bonus returns", 60, true, true, 0, true, true, false, 70),
    ("sniper next turn: floor returns", 5, true, true, 0, true, true, false, 50),
    ("sniper cap", 95, true, true, 0, true, true, false, 100),
    ("tier 3 does not change ordinary primary", 50, true, false, 0, true, true, false, 65),
    ("special weapon excluded", 5, false, false, 0, true, true, false, 5),
    ("native special-slot sniper included", 5, false, true, 0, true, true, false, 50),
    ("no matching perk", 5, true, false, 0, false, true, false, 5),
    ("guaranteed hit preserved", 100, true, true, 0, true, true, true, 100),
};
foreach (var c in cases)
{
    var actual = PromiseRules.Apply(c.Base, c.Primary, c.Sniper, c.Moved, c.Promise, c.Rooftops, c.Always);
    if (Math.Abs(actual - c.Expected) > .0001f) throw new Exception($"{c.Name}: expected {c.Expected}, got {actual}");
    Console.WriteLine("PASS " + c.Name);
}

var moves = new (string Name, string Perk, int Cost, int Floor, bool Escape, bool Walker, bool Reverse, int Expected)[]
{
    ("rough terrain discount", "Lucky", 20, 6, false, false, false, 16),
    ("rough terrain floor", "Lucky", 7, 6, false, false, false, 6),
    ("ordinary terrain unchanged", "Lucky", 6, 6, false, false, false, 6),
    ("reverse discount", "Rook", 20, 6, false, false, true, 15),
    ("forward unchanged", "Rook", 20, 6, false, false, false, 20),
    ("walker step", "Viper", 8, 6, false, true, false, 6),
    ("walker cost floor", "Viper", 1, 1, false, true, false, 1),
    ("nonwalker excluded", "Viper", 8, 6, false, false, false, 8),
    ("escape active", "Vale", 20, 6, true, false, false, 15),
    ("escape inactive", "Vale", 20, 6, false, false, false, 20),
    ("post attack movement", "Talia", 8, 6, true, false, false, 6),
    ("post attack movement consumed", "Talia", 8, 6, false, false, false, 8),
    ("unrelated leader unchanged", "Don", 20, 6, true, false, true, 20)
};
foreach (var c in moves)
{
    var actual = WaybackerRules.MovementCost(c.Perk,c.Cost,c.Floor,c.Escape,c.Walker,c.Reverse);
    if (actual != c.Expected) throw new Exception(c.Name + ": " + actual);
    Console.WriteLine("PASS " + c.Name);
}
if (WaybackerRules.Discount(30,.9f) != 27 || WaybackerRules.Discount(31,.9f) != 28 || WaybackerRules.Discount(40,.75f) != 30 || WaybackerRules.Discount(0,.75f) != 0)
    throw new Exception("AP rounding failed");
Console.WriteLine("PASS action point rounding / zero cost");
