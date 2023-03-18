#if OPENGLES
using Silk.NET.OpenGLES;
#else
using Silk.NET.OpenGL;
#endif

namespace FluidSim.Abstractions;

public interface IGLBufferFactory
{
    BufferObject<T> CreateArrayBuffer<T>(Span<T> data) where T : unmanaged;
    
    DoubleFrameBuffer CreateDoubleFrameBuffer(uint width, uint height, InternalFormat internalFormat, PixelFormat format, GLEnum type, GLEnum textureFilter);
    
    BufferObject<T> CreateElementArrayBuffer<T>(Span<T> data) where T : unmanaged;
    
    FrameBufferObject CreateFrameBuffer(uint width, uint height, InternalFormat internalFormat, PixelFormat format, PixelType type, GLEnum textureFilter);
    
    FrameBufferObject CreateFrameBuffer(uint width, uint height, InternalFormat internalFormat, PixelFormat format, GLEnum type, GLEnum textureFilter);
    
    VertexArrayObject<TVertex, TIndex> CreateVertexArrayObject<TVertex, TIndex>(BufferObject<TVertex> vbo, BufferObject<TIndex> ebo)
        where TVertex : unmanaged
        where TIndex : unmanaged;
}
