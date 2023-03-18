using Silk.NET.Core.Attributes;
using Silk.NET.Maths;
using System.Reflection;
using System.Runtime.InteropServices;
using FluidSim.Extensions;

#if OPENGLES
using Silk.NET.OpenGLES;
#else
using Silk.NET.OpenGL;
#endif

namespace FluidSim.Abstractions;

public class FrameBufferObject: IDisposable
{
    private readonly GL _gl;
    private uint _width;
	private uint _height;
	private float _texelSizeX;
	private float _texelSizeY;
    private uint _textureHandle;
    private uint _fboHandle;
    private readonly InternalFormat _internalFormat;
    private readonly PixelFormat _pixelFormat;
    private readonly GLEnum _type;
    private readonly GLEnum _textureFilter;
    private readonly int _internalFormatComponentNumber;

    public uint Handle => _fboHandle;
    public uint Width => _width;
    public uint Height => _height;
    public float TexelSizeX => _texelSizeX;
    public float TexelSizeY => _texelSizeY;
    public InternalFormat InternalFormat => _internalFormat;
    public GLEnum Type => _type;
    public PixelFormat PixelFormat => _pixelFormat;
    public GLEnum TextureFilter => _textureFilter;

    public uint TextureHandle => _textureHandle;

    public unsafe FrameBufferObject(
		GL gl, uint width, uint height, 
        InternalFormat internalFormat,
		PixelFormat format, GLEnum type, 
		GLEnum textureFilter)
	{
        _gl = gl;
        _internalFormat = internalFormat;
        _pixelFormat = format;
        _type = type;
        _internalFormatComponentNumber = GetInternalFormatComponentNumber(internalFormat);
        _textureFilter = textureFilter;
        _width = width;
        _height = height;
        _texelSizeX = 1.0f / width;
        _texelSizeY = 1.0f / height;

        _textureHandle = _gl.GenTexture();
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _textureHandle);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)_textureFilter);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)_textureFilter);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, _internalFormat, width, height, 0, _pixelFormat, _type, null);

        _fboHandle = _gl.GenFramebuffer();
        _gl.BindFramebuffer(GLEnum.Framebuffer, _fboHandle);
        _gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0, GLEnum.Texture2D, _textureHandle, 0);
        _gl.Viewport(0, 0, width, height);
        _gl.Clear((uint)GLEnum.ColorBufferBit);

        Console.WriteLine($"CREATE_FBO: w={width} h={height} ifmt={(int)_internalFormat} fmt={(int)_pixelFormat} filt={(int)_textureFilter}");
    }

    public void Resize(uint width, uint height, bool clear = false)
    {
        var newFbo = new FrameBufferObject(_gl, width, height, _internalFormat, _pixelFormat, _type, _textureFilter);
        if (clear == false)
        {
            BlitTo(newFbo);
        }
        Dispose();
        _width = width;
        _height = height;
        _texelSizeX = 1.0f / width;
        _texelSizeY = 1.0f / height;
        _textureHandle = newFbo.TextureHandle;
        _fboHandle = newFbo.Handle;
    }

    public void BlitTo(uint dstFboHandle, uint dstWidth, uint dstHeight)
    {
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _fboHandle);   // bind the FBO to read from
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, dstFboHandle); // bind the FBO to write to

        _gl.BlitFramebuffer(
            0, 0, (int)_width, (int)_height,
            0, 0, (int)dstWidth, (int)dstHeight,
            (uint)GLEnum.ColorBufferBit, 
            GLEnum.Nearest);
    }

    private static int GetInternalFormatComponentNumber(InternalFormat internalFormat)
    {
        var nativeName = typeof(InternalFormat)
            .GetMember(internalFormat.ToString())
            .Single()
            .GetCustomAttribute<NativeNameAttribute>()
            .Name;

        var tokens = nativeName.Split("_");

        var components = tokens[1];

        if (components.StartsWith("RGBA")) return 4;
        if (components.StartsWith("RGB")) return 3;
        if (components.StartsWith("RG")) return 2;
        if (components.StartsWith("R")) return 1;

        throw new Exception($"Cannot determine components number for internal format: {internalFormat}");
    }

    public void BlitToScreen(Vector2D<int> screenFboSize)
    {
        BlitTo(0, (uint)screenFboSize.X, (uint)screenFboSize.Y);
    }

    public void BlitToScreen(uint screenWidth, uint screenHeight)
    {
        BlitTo(0, screenWidth, screenHeight);
    }

    public void BlitTo(FrameBufferObject dst)
    {
        BlitTo(dst.Handle, dst.Width, dst.Height);
    }

    public unsafe Span<T> ReadAllPixels<T>(TextureUnit unit = TextureUnit.Texture0) where T : unmanaged
    {
        Attach(unit);
        T[] data = new T[_width * _height * _internalFormatComponentNumber];
        fixed(void* d = data)
        {
            _gl.ReadPixels(0, 0, _width, _height, _pixelFormat, _type, d);
        }
        _gl.EnsureNoErrors();
        return data.AsSpan();
    }

    public unsafe void FillFromMemory(void* data, TextureUnit unit = TextureUnit.Texture0)
    {
        Attach(unit);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, _internalFormat, _width, _height, 0, _pixelFormat, _type, data);
        _gl.EnsureNoErrors();
    }

    public unsafe void FillFromMemory<T>(T value) where T : unmanaged
    {
        var size = _width * _height * _internalFormatComponentNumber;
        var d = NativeMemory.Alloc((nuint)(sizeof(T) * size));
        try
        {
            var s = new Span<T>(d, (int)size);
            s.Fill(value);
            FillFromMemory(d);
        }
        finally
        {
            NativeMemory.Free(d);
        }
    }

    public unsafe FrameBufferObject(
        GL gl, uint width, uint height, InternalFormat internalFormat,
        PixelFormat format, PixelType type,
        GLEnum textureFilter): this(gl, width, height, internalFormat, format, (GLEnum)type, textureFilter)
    {

    }

    public int Attach(TextureUnit textureX)
    {
        _gl.ActiveTexture(textureX);
        _gl.BindTexture(TextureTarget.Texture2D, _textureHandle);
        return ((int)textureX) - ((int)TextureUnit.Texture0);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _gl.DeleteFramebuffer(_fboHandle);
        _gl.DeleteTexture(_textureHandle);
    }
}
