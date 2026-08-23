using Godot;

namespace Heroes1918;

/// <summary>Semi-transparent rectangle used for the drag-select box.</summary>
public partial class SelectionBox : Control
{
    private Color _fill = new(1f, 1f, 0.8f, 0.10f);
    private Color _border = new(1f, 1f, 0.8f, 0.55f);

    public void SetRect(Rect2 rect)
    {
        Position = rect.Position;
        Size = rect.Size;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), _fill);
        DrawRect(new Rect2(Vector2.Zero, Size), _border, false, 1.5f);
    }
}
