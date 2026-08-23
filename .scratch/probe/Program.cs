using Sim;

var map = new MapDef
{
    Name="probe", Width=Fixed.FromInt(96), Height=Fixed.FromInt(64),
};
var world = new World(map, 9);
int caller = world.Spawn(Side.Allies, UnitTypes.RifleSquad.Id, new Fixed2(Fixed.FromInt(8), Fixed.FromInt(8)));
int victim = world.Spawn(Side.Central, UnitTypes.RifleSquad.Id, new Fixed2(Fixed.FromInt(48), Fixed.FromInt(32)));

var there = new Fixed2(Fixed.FromInt(48), Fixed.FromInt(32));
Console.WriteLine($"pre: tick={world.Tick} nextAllies={world.Match.NextBarrageTick(Side.Allies)}");
world.Step(new[]
{
    new Command(caller, CommandType.Barrage, there, there),
    new Command(caller, CommandType.Barrage, new Fixed2(Fixed.FromInt(60), Fixed.FromInt(40)), new Fixed2(Fixed.FromInt(60), Fixed.FromInt(40))),
});
Console.WriteLine($"post: barrages={world.Barrages.Count} nextAllies={world.Match.NextBarrageTick(Side.Allies)}");
for (int t = 0; t < 120; t++)
{
    world.Step(new[] { new Command(caller, CommandType.Barrage, new Fixed2(Fixed.FromInt(60), Fixed.FromInt(40)), new Fixed2(Fixed.FromInt(60), Fixed.FromInt(40))) });
}
Console.WriteLine($"after120: barrages={world.Barrages.Count} explosionsTotalSeen separately");
