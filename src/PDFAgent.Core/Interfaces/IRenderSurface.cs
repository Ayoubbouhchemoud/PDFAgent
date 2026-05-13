namespace PDFAgent.Core.Interfaces;

public interface IRenderSurface : IDisposable
{
    int Width { get; }
    int Height { get; }
    byte[] GetPngData();
    byte[] GetJpegData(int quality = 85);
}
