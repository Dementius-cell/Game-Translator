namespace GameTranslator.Domain.Profiles;

public readonly record struct AbsoluteRectangle(int X, int Y, int Width, int Height)
{
    public bool HasPositiveSize => Width > 0 && Height > 0;

    public bool Intersects(AbsoluteRectangle other)
    {
        if (!HasPositiveSize || !other.HasPositiveSize)
        {
            return false;
        }

        return X < other.X + other.Width
            && X + Width > other.X
            && Y < other.Y + other.Height
            && Y + Height > other.Y;
    }
}
