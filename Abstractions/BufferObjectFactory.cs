#if OPENGLES
using Silk.NET.OpenGLES;
#else
using Silk.NET.OpenGL;
#endif

namespace FluidSim.Abstractions;

public class GLBufferFactory: IGLBufferFactory
{
    private readonly GL _gl;

    public GLBufferFactory(GL gl)
    {
        _gl = gl;
    }

    public BufferObject<T> CreateArrayBuffer<T>(Span<T> data) where T : unmanaged
    {
        return new BufferObject<T>(_gl, data, BufferTargetARB.ArrayBuffer);
    }

    public BufferObject<T> CreateElementArrayBuffer<T>(Span<T> data) where T : unmanaged
    {

        return new BufferObject<T>(_gl, data, BufferTargetARB.ElementArrayBuffer);
    }

    public VertexArrayObject<TVertex, TIndex> CreateVertexArrayObject<TVertex, TIndex>(BufferObject<TVertex> vbo, BufferObject<TIndex> ebo) 
        where TVertex : unmanaged 
        where TIndex : unmanaged
    {
        return new VertexArrayObject<TVertex, TIndex>(_gl, vbo, ebo);
    }

    public FrameBufferObject CreateFrameBuffer(uint width, uint height, InternalFormat internalFormat, PixelFormat format, PixelType type, GLEnum textureFilter)
    {
        return new FrameBufferObject(_gl, width, height, internalFormat, format, type, textureFilter);
    }

    public FrameBufferObject CreateFrameBuffer(uint width, uint height, InternalFormat internalFormat, PixelFormat format, GLEnum type, GLEnum textureFilter)
    {
        return new FrameBufferObject(_gl, width, height, internalFormat, format, type, textureFilter);
    }

    public DoubleFrameBuffer CreateDoubleFrameBuffer(uint width, uint height, InternalFormat internalFormat, PixelFormat format, GLEnum type, GLEnum textureFilter)
    {
        var fbo1 = CreateFrameBuffer(width, height, internalFormat, format, type, textureFilter);
        var fbo2 = CreateFrameBuffer(width, height, internalFormat, format, type, textureFilter);
        return new DoubleFrameBuffer(fbo1, fbo2, true);
    }
}
