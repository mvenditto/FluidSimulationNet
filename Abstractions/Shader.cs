using System.Data;
using System.IO;
using System.Numerics;
using Silk.NET.GLFW;
#if OPENGLES
using Silk.NET.OpenGLES;
#else
using Silk.NET.OpenGL;
#endif

namespace FluidSim.Abstractions;

public class ShaderProgram : IDisposable
{
    private uint _handle;
    private GL _gl;
    private string _vertSource;
    private string _fragSource;

    public ShaderProgram(GL gl, string vertexPath, string fragmentPath, IEnumerable<string> fragDefines)
    {
        _gl = gl;
        _vertSource = File.ReadAllText(vertexPath);
        _fragSource = File.ReadAllText(fragmentPath);
        Initialize(_vertSource, _fragSource, fragDefines);
    }

    private void Initialize(string vertSource, string fragSource, IEnumerable<string> fragDefines)
    {
        uint vertex = LoadShader(ShaderType.VertexShader, vertSource);
        uint fragment = LoadShader(ShaderType.FragmentShader, fragSource, fragDefines);
        if (_handle != 0)
        {
            _gl.DeleteProgram(_handle);
        }
        _handle = _gl.CreateProgram();
        _gl.AttachShader(_handle, vertex);
        _gl.AttachShader(_handle, fragment);
        _gl.LinkProgram(_handle);
        _gl.GetProgram(_handle, GLEnum.LinkStatus, out var status);
        if (status == 0)
        {
            throw new Exception($"Program failed to link with error: {_gl.GetProgramInfoLog(_handle)}");
        }
        _gl.DetachShader(_handle, vertex);
        _gl.DetachShader(_handle, fragment);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);
    }

    public void Bind()
    {
        _gl.UseProgram(_handle);
    }

    public void SetUniform(string name, int value)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            throw new Exception($"{name} uniform not found on shader.");
        }
        _gl.Uniform1(location, value);
    }

    public void SetUniform(string name, double value)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            throw new Exception($"{name} uniform not found on shader.");
        }
        _gl.Uniform1(location, (float)value);
    }

    public void SetUniform(string name, double x, double y)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            throw new Exception($"{name} uniform not found on shader.");
        }
        _gl.Uniform2(location, (float)x, (float)y);
    }

    public void SetUniform(string name, float value)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            throw new Exception($"{name} uniform not found on shader.");
        }
        _gl.Uniform1(location, value);
    }

    public void SetUniform(string name, Vector3 value)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            throw new Exception($"{name} uniform not found on shader.");
        }
        _gl.Uniform3(location, value.X, value.Y, value.Z);
    }

    public void SetUniform(string name, float x, float y, float z)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            throw new Exception($"{name} uniform not found on shader.");
        }
        _gl.Uniform3(location, x, y, z);
    }
    public void SetUniform(string name, float x, float y, float z, float w)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            throw new Exception($"{name} uniform not found on shader.");
        }
        _gl.Uniform4(location, x, y, z, w);
    }

    public void SetUniform(string name, double x, double y, double z)
    {
        SetUniform(name, (float)x, (float)y, (float)z);
    }

    public void SetUniform(string name, System.Drawing.Color color)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            throw new Exception($"{name} uniform not found on shader.");
        }
        _gl.Uniform3(location, color.R / 255f, color.G / 255f, color.B / 255f);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _gl.DeleteProgram(_handle);
    }

    public void UpdateDefines(IEnumerable<string>? defines = null)
    {
        Initialize(_vertSource, _fragSource, defines);
    }

    private uint LoadShader(ShaderType type, string src, IEnumerable<string>? defines = null)
    {
        var headers = "#version 100\n"; // TODO

        if (defines != null)
        {
            foreach (var define in defines)
            {
                headers += "#define " + define + "\n";
            }
        }

        src = headers + src;
        
        uint handle = _gl.CreateShader(type);
        _gl.ShaderSource(handle, src);
        _gl.CompileShader(handle);
        string infoLog = _gl.GetShaderInfoLog(handle);
        if (!string.IsNullOrWhiteSpace(infoLog))
        {
            throw new Exception($"Error compiling shader of type {type}, failed with error {infoLog}");
        }

        return handle;
    }
}