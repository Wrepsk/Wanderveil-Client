using System.Numerics;

namespace Wanderveil.UI.Primitives;

internal readonly record struct UiRect(Vector2 Position, Vector2 Size)
{
    internal Vector2 Max => Position + Size;
    internal UiRect Union(UiRect other)
    {
        var min = Vector2.Min(Position, other.Position);
        return new UiRect(min, Vector2.Max(Max, other.Max) - min);
    }
    internal bool Contains(Vector2 point) => point.X >= Position.X && point.Y >= Position.Y && point.X < Max.X && point.Y < Max.Y;
}
