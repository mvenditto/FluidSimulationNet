#if OPENGLES
using Silk.NET.OpenGLES;
#else
using Silk.NET.OpenGL;
#endif

namespace FluidSim.Extensions;

public static class GLExtensions
{
    public static void EnsureNoErrors(this GL gl)
    {
        var err = gl.GetError();
        
        if (err != GLEnum.NoError)
        {
            throw new Exception($"GLError: {err} {(int)err})");
        }
    }
}
