namespace Sim;

public static class MapDigest
{
    /// <summary>
    /// Stable content digest of a map definition (field order is canonical).
    /// Used by lockstep handshakes and replay validation: both peers must be
    /// simulating the same battlefield.
    /// </summary>
    public static ulong Of(MapDef map)
    {
        var h = new Hasher();
        foreach (char c in map.Name)
            h.Mix((long)c);

        h.Mix(map.Width.Raw);
        h.Mix(map.Height.Raw);

        h.Mix(map.CapturePoints.Count);
        foreach (var p in map.CapturePoints)
        {
            h.Mix(p.Pos);
            h.Mix(p.Radius.Raw);
            h.Mix(p.IsVictoryPoint);
        }

        h.Mix(map.Cover.Count);
        foreach (var c in map.Cover)
        {
            h.Mix(c.Pos);
            h.Mix(c.Radius.Raw);
            h.Mix((int)c.Kind);
        }

        h.Mix(map.SightBlockers.Count);
        foreach (var o in map.SightBlockers)
        {
            h.Mix(o.Pos);
            h.Mix(o.Radius.Raw);
        }

        return h.Digest;
    }
}
