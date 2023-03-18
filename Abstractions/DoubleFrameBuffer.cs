#if OPENGLES
using Silk.NET.OpenGLES;
#else
using Silk.NET.OpenGL;
#endif

namespace FluidSim.Abstractions;

public class DoubleFrameBuffer : IDisposable
{
    private FrameBufferObject _fbo1;
    private FrameBufferObject _fbo2;
    private readonly bool _ownsFrameBuffers;

    public float TexelSizeX => _fbo1.TexelSizeX;
    public float TexelSizeY => _fbo1.TexelSizeY;

    public FrameBufferObject Read => _fbo1;

    public FrameBufferObject Write => _fbo2;

    public DoubleFrameBuffer(FrameBufferObject fbo1, FrameBufferObject fbo2, bool ownsFrameBuffers)
    {
        ArgumentNullException.ThrowIfNull(fbo1);
        ArgumentNullException.ThrowIfNull(fbo2);
        _fbo1 = fbo1;
        _fbo2 = fbo2;
        _ownsFrameBuffers = ownsFrameBuffers;
    }

    public void Swap()
    {
        (_fbo2, _fbo1) = (_fbo1, _fbo2);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (_ownsFrameBuffers)
        {
            _fbo1.Dispose();
            _fbo2.Dispose();
        }
    }
}
