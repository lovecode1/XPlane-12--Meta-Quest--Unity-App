using System;

internal interface IFrameDecodeStrategy : IDisposable
{
    void Initialize(FrameDecodeContext context);
    bool TryHandleUpload(byte[] payload);
}
