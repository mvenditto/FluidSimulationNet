#if OPENGLES
using Silk.NET.OpenGLES;
#else
using Silk.NET.OpenGL;
#endif

namespace FluidSim.Abstractions;

public class BufferObject<TDataType> : IDisposable where TDataType : unmanaged
{
    private readonly uint _handle;
    private readonly BufferTargetARB _bufferType;
    private readonly GL _gl;

    public BufferTargetARB BufferType => _bufferType;

    internal unsafe BufferObject(GL gl, Span<TDataType> data, BufferTargetARB bufferType)
    {
        _gl = gl;
        _bufferType = bufferType;
        _handle = _gl.GenBuffer();

        Bind();

        fixed (void* d = data)
        {
            _gl.BufferData(bufferType, (nuint)(data.Length * sizeof(TDataType)), d, BufferUsageARB.StaticDraw);
        }
    }

    public void Bind()
    {
        _gl.BindBuffer(_bufferType, _handle);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _gl.DeleteBuffer(_handle);
    }
}