namespace CheckMind.App.Core;

public readonly record struct DesktopRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;

    public bool IsClearlyMinimizedPlacement =>
        Left <= -32000 ||
        Top <= -32000 ||
        Width <= 0 ||
        Height <= 0;
}
