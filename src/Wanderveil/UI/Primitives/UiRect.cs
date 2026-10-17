using System.Numerics;

namespace Wanderveil.UI.Primitives;

internal readonly record struct UiRect(Vector2 Position, Vector2 Size)
{
    internal Vector2 Max => Position + Size;
}
