namespace CheckMind.App.Core;

public interface ICaptureOverlay
{
    void SetVisible(bool visible);
    void SetRect(BBox? rect);
}
