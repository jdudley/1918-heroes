namespace Sim;

/// <summary>Suppression bleeds off when nobody is shooting at you.</summary>
public static class SuppressionSystem
{
    public static void Step(World world)
    {
        Fixed decay = SimConfig.SuppressionDecayPerSecond * SimConfig.Dt;
        var units = world.Units;
        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (!u.Alive || u.Suppression == Fixed.Zero) continue;
            u.Suppression -= decay;
            if (u.Suppression.Raw < 0) u.Suppression = Fixed.Zero;
            units[i] = u;
        }
    }
}
