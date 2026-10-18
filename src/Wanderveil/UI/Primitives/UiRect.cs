using System.Numerics;

namespace Wanderveil.UI.Primitives;

internal readonly record struct UiRect(Vector2 Position, Vector2 Size)
{
    internal Vector2 Max => Position + Size;
    internal bool Contains(Vector2 point) => point.X >= Position.X && point.Y >= Position.Y && point.X < Max.X && point.Y < Max.Y;
}
