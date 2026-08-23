using System.Text.Json;
using Sim;

namespace Heroes1918;

/// <summary>
/// Handcrafted maps are data files. Schema (meters, integers for now):
/// { name, width, height, capturePoints[{x,y,r,vp}], cover[{x,y,r,kind}],
///   sightBlockers[{x,y,r}], spawns[{side,type,x,y}] }
/// </summary>
public static class JsonMapLoader
{
    private sealed record MapDto(
        string? name,
        JsonElement? width,
        JsonElement? height,
        JsonElement? capturePoints,
        JsonElement? cover,
        JsonElement? sightBlockers,
        JsonElement? spawns);

    /// <summary>Load a map from a res:// path.</summary>
    public static MapDef Load(string resPath)
    {
        using var file = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Read);
        if (file is null)
            throw new FileNotFoundException($"map not found: {resPath}");
        return Parse(file.GetAsText());
    }

    /// <summary>Parse a map definition from JSON text. Engine-independent: tests and tools use this directly.</summary>
    public static MapDef Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<MapDto>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("empty map json");

        Fixed M(double v) => Fixed.FromRatio((long)(v * 1000), 1000);

        return new MapDef
        {
            Name = dto.name ?? "unnamed",
            Width = M(dto.width?.GetDouble() ?? throw new InvalidDataException("map width missing")),
            Height = M(dto.height?.GetDouble() ?? throw new InvalidDataException("map height missing")),
            CapturePoints = ParseList(dto.capturePoints, e => new CapturePointSpec(
                Pos(e), M(Req(e, "r").GetDouble()), e.TryGetProperty("vp", out var vp) && vp.GetBoolean())),
            Cover = ParseList(dto.cover, e => new CoverObject(
                Pos(e), M(Req(e, "r").GetDouble()), ParseKind(e.TryGetProperty("kind", out var k) ? k.GetString() : "crater"))),
            SightBlockers = ParseList(dto.sightBlockers, e => new Obstacle(
                Pos(e), M(Req(e, "r").GetDouble()))),
            Spawns = ParseList(dto.spawns, e => new SpawnSpec(
                ParseSide(e.GetProperty("side").GetString()),
                ParseType(e.GetProperty("type").GetString()),
                Pos(e))),
        };
    }

    private static List<T> ParseList<T>(JsonElement? element, Func<JsonElement, T> make)
    {
        var result = new List<T>();
        if (element is { ValueKind: JsonValueKind.Array })
            foreach (var e in element.Value.EnumerateArray())
                result.Add(make(e));
        return result;
    }

    private static Fixed2 Pos(JsonElement e) =>
        new(Fixed.FromRatio((long)(Req(e, "x").GetDouble() * 1000), 1000),
            Fixed.FromRatio((long)(Req(e, "y").GetDouble() * 1000), 1000));

    private static JsonElement Req(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v)
            ? v
            : throw new InvalidDataException($"map entry missing '{prop}'");

    private static CoverKind ParseKind(string? kind) => kind switch
    {
        "trench" => CoverKind.Trench,
        "rubble" => CoverKind.Rubble,
        _ => CoverKind.Crater,
    };

    private static Side ParseSide(string? side) => side switch
    {
        "central" => Side.Central,
        "allies" => Side.Allies,
        _ => throw new InvalidDataException($"unknown side '{side}'"),
    };

    private static int ParseType(string? type) => type switch
    {
        "mg" => UnitTypes.MachineGunSection.Id,
        "rifles" => UnitTypes.RifleSquad.Id,
        _ => throw new InvalidDataException($"unknown unit type '{type}'"),
    };
}
