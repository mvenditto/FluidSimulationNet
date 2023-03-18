using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
#if OPENGLES
using Silk.NET.OpenGLES;
#else
using Silk.NET.OpenGL;
#endif

namespace FluidSim.Abstractions;

public class Texture : IDisposable
{
    private uint _handle;
    private GL _gl;

    public uint Width { get; private set; }
    public uint Height { get; private set; }

    public unsafe Texture(GL gl, 
        string path, 
        InternalFormat internalFormat,
        PixelFormat pixelFormat,
        PixelType pixelType,
        TextureWrapMode wrap = TextureWrapMode.Repeat, 
        TextureMinFilter minFilter = TextureMinFilter.Linear, 
        TextureMagFilter magFilter = TextureMagFilter.Linear)
    {
        _gl = gl;

        _handle = _gl.GenTexture();
        Attach();

        //Loading an image using imagesharp.
        using (var img = Image.Load<Rgba32>(path))
        {
            Width = (uint) img.Width;
            Height = (uint) img.Height;
            //Reserve enough memory from the gpu for the whole image
            gl.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, (uint)img.Width, (uint)img.Height, 0, pixelFormat, pixelType, null);

            img.ProcessPixelRows(accessor =>
            {
                //ImageSharp 2 does not store images in contiguous memory by default, so we must send the image row by row
                for (int y = 0; y < accessor.Height; y++)
                {
                    fixed (void* data = accessor.GetRowSpan(y))
                    {
                        //Loading the actual image.
                        gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, y, (uint)accessor.Width, 1, pixelFormat, pixelType, data);
                    }
                }
            });
        }


        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)wrap);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)wrap);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)minFilter);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)magFilter);

    }
    public int Attach(TextureUnit textureSlot = TextureUnit.Texture0)
    {
        //When we bind a texture we can choose which textureslot we can bind it to.
        _gl.ActiveTexture(textureSlot);
        _gl.BindTexture(TextureTarget.Texture2D, _handle);
        return ((int)textureSlot) - ((int)TextureUnit.Texture0);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _gl.DeleteTexture(_handle);
    }
}