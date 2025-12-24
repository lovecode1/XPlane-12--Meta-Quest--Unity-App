using UnityEngine;

internal readonly struct DecodedImage
{
    public DecodedImage(byte[] pixels, int width, int height, int sourceBytes)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
        SourceBytes = sourceBytes;
    }

    public byte[] Pixels { get; }
    public int Width { get; }
    public int Height { get; }
    public int SourceBytes { get; }

    public bool IsValid => Pixels != null && Pixels.Length == Width * Height * 4 && Width > 0 && Height > 0;
}
