using Godot;
using Sim;

using Side = Sim.Side;

namespace Heroes1918;

/// <summary>Tickets, match outcome, and the one-line controls hint.</summary>
public partial class Hud : CanvasLayer
{
    private Label _alliesTickets = null!;
    private Label _centralTickets = null!;
    private Label _banner = null!;
    private Label _status = null!;

    public override void _Ready()
    {
        var root = new MarginContainer();
        // Full-rect margins container.
        root.AnchorLeft = 0;
        root.AnchorRight = 1;
        root.AnchorTop = 0;
        root.AnchorBottom = 1;
        root.AddThemeConstantOverride("margin_left", 16);
        root.AddThemeConstantOverride("margin_right", 16);
        root.AddThemeConstantOverride("margin_top", 10);
        root.AddThemeConstantOverride("margin_bottom", 12);
        root.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(root);

        var topRow = new HBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.AddChild(topRow);

        _alliesTickets = TicketLabel(new Color(0.75f, 0.85f, 0.55f));
        topRow.AddChild(_alliesTickets);

        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.Expand, MouseFilter = Control.MouseFilterEnum.Ignore };
        topRow.AddChild(spacer);

        _centralTickets = TicketLabel(new Color(0.65f, 0.72f, 0.9f));
        topRow.AddChild(_centralTickets);

        _banner = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _banner.AnchorLeft = 0;
        _banner.AnchorRight = 1;
        _banner.AnchorTop = 0.38f;
        _banner.AnchorBottom = 0.5f;
        _banner.AddThemeFontSizeOverride("font_size", 64);
        _banner.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(_banner);

        _status = new Label { Text = "" };
        _status.AnchorLeft = 0;
        _status.AnchorRight = 1;
        _status.AnchorTop = 0.92f;
        _status.AnchorBottom = 1;
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.AddThemeFontSizeOverride("font_size", 15);
        _status.Modulate = new Color(1, 1, 1, 0.75f);
        _status.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(_status);

        SetHint("LMB select · drag box · RMB attack-move · WASD pan · wheel zoom");
    }

    private static Label TicketLabel(Color color)
    {
        var l = new Label();
        l.AddThemeFontSizeOverride("font_size", 26);
        l.Modulate = color;
        l.MouseFilter = Control.MouseFilterEnum.Ignore;
        return l;
    }

    public void SetHint(string text) => _status.Text = text;

    /// <summary>Current center-banner text (VICTORY / DEFEAT once the match ends).</summary>
    public string BannerText => _banner.Text;

    public void Sync(World world, Side mySide)
    {
        _alliesTickets.Text = $"Allies  {world.Match.TicketsAllies}";
        _centralTickets.Text = $"{world.Match.TicketsCentral}  Central";

        if (world.Match.Finished)
        {
            bool playerWon = world.Match.Winner == mySide;
            _banner.Text = playerWon ? "VICTORY" : "DEFEAT";
            _banner.Modulate = playerWon
                ? new Color(0.8f, 1f, 0.7f)
                : new Color(1f, 0.6f, 0.5f);
            SetHint($"{world.Match.Winner} wins · press R to fight again");
        }
        else
        {
            _banner.Text = "";
        }
    }
}
