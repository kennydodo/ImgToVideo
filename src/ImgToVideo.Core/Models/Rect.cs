namespace ImgToVideo.Core.Models;

public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool IsInside(Rect bounds) =>
        X >= bounds.X && Y >= bounds.Y &&
        Right <= bounds.Right && Bottom <= bounds.Bottom;
}
