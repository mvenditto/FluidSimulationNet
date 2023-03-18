using System.Numerics;
using System.Diagnostics;
using FluidSim.Abstractions;
using FluidSim.Utilities;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Input;
using ImGuiNET;
#if OPENGLES
using Silk.NET.OpenGLES;
using Silk.NET.OpenGLES.Extensions.ImGui;
#else
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
#endif

namespace FluidSim;

class FluidSimulation
{
    #region Simulation params
    private int _pressureIterations = 100;
    private float _curl = 30;
    private float _pressure = 0.8f;
    private float _velocityDissipation = 0.2f;
    private float _densityDissipation = 1.0f;
    private float _splatRadius = 0.25f;
    private float _splatDx = 0.0f;
    private float _splatDy = 150.0f;
    private float _splatForce = 6000f;
    private Vector4 _splatColor = new(1, 1, 0, 1);
    private bool _paused = true;
    private bool _steppingMode = true;
    private bool _showGui = true;
    private int _vizModeSelected = (int)VisualizationMode.Dye;
    private readonly Stopwatch _simStopwatch = new();

    public int PressureIterations { get => _pressureIterations; set => _pressureIterations = value; }
    public float Vorticity { get => _curl; set => _curl = value; }
    public float Pressure { get => _pressure; set => _pressure = value; }
    public float VelocityDissipation { get => _velocityDissipation; set => _velocityDissipation = value; }
    public float DensityDissipation { get => _densityDissipation; set => _densityDissipation = value; }
    public float DefaultSplatRadius { get => _splatRadius; set => _splatRadius = value; }
    public bool Paused { get => _paused; set => _paused = value; }
    public bool Stepping { get => _steppingMode; set => _steppingMode = value; }
    public bool ShowGui { get => _showGui; set => _showGui = value; }
    public VisualizationMode ActiveVisualizationMode { get => (VisualizationMode)_vizModeSelected; set => _vizModeSelected = (int)value; }

    public float DyeResolution { get; init; } = 1024;
    public float SimResolution { get; init; } = 512;

    private readonly IList<Pointer> _pointers = new List<Pointer>
    {
        new Pointer {Id = 0}
    };
    #endregion

    #region Windowing
    private IWindow _window;
    private IGLBufferFactory _bufferFactory;
    private IInputContext _input;
    private ImGuiController _controller;
    private float _aspectRatio;
    private float _actualWindowWidth;
    private bool _resized;
    #endregion

    #region GL
    private GL _gl;
    private BufferObject<float> _vbo;
    private BufferObject<ushort> _ebo;
    private VertexArrayObject<float, ushort> _vao;
    private readonly static float[] Vertices = new float[] { -1, -1, -1, 1, 1, 1, 1, -1 };
    private readonly static ushort[] Indices = new ushort[] { 0, 1, 2, 0, 2, 3 };
    private bool LinearFiltering { get; init; } = false;
    public bool HalfFloat { get; init; } = true;
    private readonly static InternalFormat Rgba32f = InternalFormat.Rgba32f;
    private readonly static InternalFormat Rg32f = InternalFormat.RG32f;
    private readonly static InternalFormat R32f = InternalFormat.R32f;
    private readonly static InternalFormat Rgba16f = InternalFormat.Rgba16f;
    private readonly static InternalFormat Rg16f = InternalFormat.RG16f;
    private readonly static InternalFormat R16f = InternalFormat.R16f;
    #endregion

    #region Framebuffers
    private FrameBufferObject _curlBuff;
    private FrameBufferObject _divergenceBuff;
    private DoubleFrameBuffer _velocityBuff;
    private DoubleFrameBuffer _pressureBuff;
    private DoubleFrameBuffer _dyeBuff;
    private Dictionary<string, ShaderProgram> Shaders = new();
    #endregion

    #region GUI
    public enum VisualizationMode
    {
        Velocity,
        Curl,
        Divergence,
        Pressure,
        Dye
    }
    private Vector2 _guiPanelSize;
    private readonly static string[] VizModes = Enum.GetNames<VisualizationMode>();
    #endregion

    private float CorrectDeltaX(float delta)
    {
        if (_aspectRatio < 1) return delta * _aspectRatio;
        return delta;
    }
    
    private float CorrectDeltaY(float delta)
    {
        if (_aspectRatio > 1) return delta / _aspectRatio;
        return delta;
    }

    private record Pointer
    {
        public int Id { get; init; }
        public float TexCoordX { get; set; }
        public float TexCoordY { get; set; }
        public float PrevTexCoordX { get; set; }
        public float PrevTexCoordY { get; set; }
        public float DeltaX { get; set; }
        public float DeltaY { get; set; }
        public bool IsDown { get; set; }
        public bool Moved { get; set; }
        public (float r, float g, float b) Color { get; set; }
    }

    public FluidSimulation(int width, int height)
    {
        var options = WindowOptions.Default;
        options.Size = new Vector2D<int>(width, height);
        options.Title = "Fluidsim";
        _window = Window.Create(options);
        _guiPanelSize = new Vector2(400, height);
        _actualWindowWidth = _window.Size.X - _guiPanelSize.X;
        _aspectRatio = _actualWindowWidth / _window.Size.Y;
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.Closing += OnClosing;
        _window.Resize += OnResize;
    }

    private void OnResize(Vector2D<int> newSize)
    {
        _actualWindowWidth = newSize.X - _guiPanelSize.X;
        _aspectRatio = _actualWindowWidth / newSize.Y;
        _guiPanelSize.Y = newSize.Y;
        _resized = true;
    }

    private void InitDrawingBuffers()
    {
        _vbo = _bufferFactory.CreateArrayBuffer(Vertices.AsSpan());
        _ebo = _bufferFactory.CreateElementArrayBuffer(Indices.AsSpan());
        _vao = _bufferFactory.CreateVertexArrayObject(_vbo, _ebo);
        _vao.VertexAttributePointer(0, 2, VertexAttribPointerType.Float, 2, 0);
    }

    public void Run()
    {
        _window.Run();
    }

    private void InitGLContext()
    {
        #if OPENGLES
        Gl = _window.CreateOpenGLES();
        #else
        _gl = _window.CreateOpenGL();
        #endif

        _bufferFactory = new GLBufferFactory(_gl);

        _controller = new ImGuiController(
            _gl,
            _window,
            _input
        );
    }

    private (uint, uint) GetFboSizeFromResolution(float resolution)
    {
        var width = _window.FramebufferSize.X - _guiPanelSize.X;
        var height = _window.FramebufferSize.Y;
        var aspectRatio = width / height;
        
        if (aspectRatio < 1.0f)
        {
            aspectRatio = 1.0f / aspectRatio;
        }

        var min = (uint)Math.Round(resolution);
        var max = (uint)Math.Round(resolution * aspectRatio);

        return width > height ? (max, min) : (min, max);
    }

    private void InitFrameBuffers()
    {
        var (dyeX, dyeY) = GetFboSizeFromResolution(DyeResolution);
        var (x, y) = GetFboSizeFromResolution(SimResolution);

        var filtering = LinearFiltering ? GLEnum.Linear : GLEnum.Nearest;
        var rgba = HalfFloat ? Rgba16f : Rgba32f;
        var rg = HalfFloat ? Rg16f : Rg32f;
        var r = HalfFloat ? R16f : R32f;
        var type = HalfFloat ? GLEnum.HalfFloat : GLEnum.Float;

        _dyeBuff = _bufferFactory.CreateDoubleFrameBuffer(dyeX, dyeY, rgba, PixelFormat.Rgba, type, filtering);
        _velocityBuff = _bufferFactory.CreateDoubleFrameBuffer(x, y, rg, PixelFormat.RG, type, filtering);
        _curlBuff = _bufferFactory.CreateFrameBuffer(x, y, r, PixelFormat.Red, type, GLEnum.Nearest);
        _divergenceBuff = _bufferFactory.CreateFrameBuffer(x, y, r, PixelFormat.Red, type, GLEnum.Nearest);
        _pressureBuff = _bufferFactory.CreateDoubleFrameBuffer(x, y, r, PixelFormat.Red, type, GLEnum.Nearest);
    }

    private void InitShaders()
    {
        foreach (var frag in Directory.EnumerateFiles(".\\Shaders"))
        {
            if (frag.EndsWith(".vert")) continue;
            var vertext = frag.EndsWith("\\blur.frag") ? ".\\Shaders\\blur.vert" : ".\\Shaders\\base.vert";
            Console.WriteLine($"COMPILING: {frag} {vertext}");
            var program = new ShaderProgram(_gl, vertext, frag);
            Shaders.Add(frag.Split("\\")[2].Split(".")[0], program);
        }
        Console.WriteLine($"Loaded {Shaders.Count} shader programs.");
    }
    
    private Pointer GetPointerFromMouse(IMouse mouse)
    {
        var pointer = _pointers.FirstOrDefault(p => p.Id == mouse.Index);

        if (pointer == null)
        {
            pointer = new Pointer() { Id = mouse.Index };
            _pointers.Add(pointer);
        }

        return pointer;
    }

    private void InitInputs()
    {
        _input = _window.CreateInput();

        foreach(var mouse in _input.Mice)
        {
            mouse.MouseDown += (mouse, _) =>
            {
                var pos = mouse.Position;
                var p = GetPointerFromMouse(mouse);
                p.IsDown = true;
                p.Moved = false;
                p.TexCoordX = mouse.Position.X / _actualWindowWidth;
                p.TexCoordY = 1.0f - mouse.Position.Y / _window.Size.Y;
                p.PrevTexCoordX = p.TexCoordX;
                p.PrevTexCoordY = p.TexCoordY;
                p.DeltaX = 0;
                p.DeltaY = 0;
                p.Color = Random.Shared.NextColorRgb();
            }; 
            
            mouse.MouseMove += (mouse, _) =>
            {
                var pos = mouse.Position;
                var p = GetPointerFromMouse(mouse);
                if (p.IsDown == false) return;
                p.PrevTexCoordX = p.TexCoordX;
                p.PrevTexCoordY = p.TexCoordY;
                p.TexCoordX = mouse.Position.X / _actualWindowWidth;
                p.TexCoordY = 1.0f - mouse.Position.Y / _window.Size.Y;
                p.DeltaX = CorrectDeltaX(p.TexCoordX - p.PrevTexCoordX);
                p.DeltaY = CorrectDeltaY(p.TexCoordY - p.PrevTexCoordY);
                p.Moved = Math.Abs(p.DeltaX) > 0 || Math.Abs(p.DeltaY) > 0;
            };

            mouse.MouseUp += (mouse, _) =>
            {
                var pos = mouse.Position;
                var p = GetPointerFromMouse(mouse);
                p.IsDown = false;
            };
        }

        _input.Keyboards.First().KeyDown += (_, k, _) =>
        {
            switch (k)
            {
                case Key.Number1:
                    _vizModeSelected = (int)VisualizationMode.Velocity;
                    break;
                case Key.Number2:
                    _vizModeSelected = (int)VisualizationMode.Curl;
                    break;
                case Key.Number3:
                    _vizModeSelected = (int)VisualizationMode.Divergence;
                    break;
                case Key.Number4:
                    _vizModeSelected = (int)VisualizationMode.Pressure;
                    break;
                case Key.Number5:
                    _vizModeSelected = (int)VisualizationMode.Dye;
                    break;
                case Key.Space:
                    Splat(0.5f, 0.5f, _splatDx, _splatDy, _splatColor.X, _splatColor.Y, _splatColor.Z, _splatRadius);
                    break;
                case Key.P:
                    _paused = !_paused;
                    break;
                case Key.S:
                    _steppingMode = !_steppingMode;
                    break;
                case Key.I:
                    _showGui = !_showGui;
                    break;
            }
        };
    }

    private unsafe void Blit(FrameBufferObject? target)
    {
        if (target == null)
        {
            _gl.Viewport(_window.FramebufferSize);
            _gl.BindFramebuffer(GLEnum.Framebuffer, 0);
            _gl.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
            _gl.Clear(ClearBufferMask.ColorBufferBit);
        }
        else
        {
            _gl.Viewport(0, 0, target.Width, target.Height);
            _gl.BindFramebuffer(GLEnum.Framebuffer, target.Handle);
        }

        _gl.DrawElements(PrimitiveType.Triangles, (uint)Indices.Length, DrawElementsType.UnsignedShort, null);
    }

    private double CorrectRadius(double radius)
    {
        if (_aspectRatio > 1)
        {
            return radius * _aspectRatio;
        }
        return radius;
    }

    private void RandomSplats(uint numSplats)
    {
        for (var i = 0; i < numSplats; i++)
        {
            var (r, g, b) = Random.Shared.NextColorRgb();
            var x = (float)Random.Shared.NextDouble();
            var y = (float)Random.Shared.NextDouble();
            var dx = 1000 * (float)(Random.Shared.NextDouble() - 0.5);
            var dy = 1000 * (float)(Random.Shared.NextDouble() - 0.5);
            var splatRadius = Math.Min(Math.Max(0.01f, (float)Random.Shared.NextDouble()), 1f);
            Splat(x, y, dx, dy, r, g, b, splatRadius);
        }
    }

    private void Splat(float x, float y, float dx, float dy, float r, float g, float b, float radius = 0.25f)
    {
        var splatProgram = Shaders["splat"];
        splatProgram.Bind();
        splatProgram.SetUniform("uTarget", _velocityBuff.Read.Attach(TextureUnit.Texture0));
        splatProgram.SetUniform("aspectRatio", _aspectRatio);
        splatProgram.SetUniform("point", x, y);
        splatProgram.SetUniform("color", dx, dy, 1.0f);
        splatProgram.SetUniform("radius", CorrectRadius(radius / 100.0));
        Blit(_velocityBuff.Write);
        _velocityBuff.Swap();

        _dyeBuff.Read.Attach(0);
        splatProgram.SetUniform("uTarget", 0);
        splatProgram.SetUniform("color", r, g, b);
        Blit(_dyeBuff.Write);
        _dyeBuff.Swap();
    }


    private void Splat(Pointer pointer)
    {
        var (r, g, b) = pointer.Color;
        var x = pointer.TexCoordX;
        var y = pointer.TexCoordY;
        var (dx, dy) =
         (
            pointer.DeltaX * _splatForce,
            pointer.DeltaY * _splatForce
        );
        Splat(x, y, dx, dy, r, g, b, _splatRadius);
    }

    private void OnLoad()
    {
        InitInputs();
        InitGLContext();
        InitShaders();
        InitFrameBuffers();
        InitDrawingBuffers();
    }

    private void RenderGui()
    {
        ImGui.SetWindowSize(_guiPanelSize, ImGuiCond.Always);
        ImGui.SetWindowPos(pos: new Vector2(_window.Size.X - _guiPanelSize.X, 0), ImGuiCond.Always);
        ImGui.SetNextWindowSizeConstraints(_guiPanelSize, _guiPanelSize);

        static void SectionText(string text)
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.86f, 0.6f, 0f, 1));
            ImGui.Text(text);
            ImGui.PopStyleColor();
            ImGui.Separator();
        }

        static void TextSameLine(params string[] text)
        {
            foreach (var t in text)
            {
                ImGui.TextDisabled(t);
                ImGui.SameLine(0, -1);
            }
        }

        SectionText("Parameters");
        ImGui.SliderFloat("vorticity", ref _curl, 0, 50);
        ImGui.SliderFloat("dens. diffusion", ref _densityDissipation, 0, 4f);
        ImGui.SliderFloat("vel. diffusion", ref _velocityDissipation, 0, 4f);
        ImGui.SliderFloat("pressure", ref _pressure, 0, 1f);
        ImGui.SliderInt("press. iters.", ref _pressureIterations, 20, 120);

        SectionText("Simulation");
        float framerate = ImGui.GetIO().Framerate;
        ImGui.Text($"Application average {1000.0f / framerate:0.##} ms/frame ({framerate:0.#} FPS)");
        var simElapsedMs = (float)_simStopwatch.Elapsed.TotalMilliseconds;
        ImGui.Text($"Simulation average {simElapsedMs:0.##} ms/frame");
        ImGui.Checkbox("paused", ref _paused);
        ImGui.SameLine(0, -1);
        ImGui.Checkbox("stepping", ref _steppingMode);
        if (_steppingMode)
        {
            ImGui.SameLine(0, -1);
            if (ImGui.Button("step"))
            {
                _paused = false;
            }
        }

        SectionText("Interaction");
        ImGui.SliderFloat("splat radius", ref _splatRadius, 0.01f, 1f);
        ImGui.SliderFloat("splat dx", ref _splatDx, 0.0f, 10000f);
        ImGui.SliderFloat("splat dy", ref _splatDy, 0.0f, 10000f);
        ImGui.ColorEdit4("splat color", ref _splatColor);
        if (ImGui.Button("splat"))
        {
            Splat(0.25f, 0.5f, _splatDx, _splatDy, _splatColor.X, _splatColor.Y, _splatColor.Z, _splatRadius);
        }
        if (ImGui.Button("random splats"))
        {
            RandomSplats(10);
        }

        SectionText("Display");
        ImGui.Combo("viz. mode", ref _vizModeSelected, VizModes, VizModes.Length);

        SectionText("FBOs");
        TextSameLine("   vel  ", "    curl", "      div  ", "   pre  ", "     dye");
        ImGui.Spacing();
        ImGui.Image((nint)_velocityBuff.Read.TextureHandle, new Vector2(64, 64));
        ImGui.SameLine(0, -1);
        ImGui.Image((nint)_curlBuff.TextureHandle, new Vector2(64, 64));
        ImGui.SameLine(0, -1);
        ImGui.Image((nint)_divergenceBuff.TextureHandle, new Vector2(64, 64));
        ImGui.SameLine(0, -1);
        ImGui.Image((nint)_pressureBuff.Read.TextureHandle, new Vector2(64, 64));
        ImGui.SameLine(0, -1);
        ImGui.Image((nint)_dyeBuff.Read.TextureHandle, new Vector2(64, 64), new Vector2(0, 0), new Vector2(1, 1), Vector4.One, new Vector4(1, 0, 0, 1));
        ImGui.SameLine(0, -1);
    }

    private void OnRender(double dt)
    {
        if (_showGui)
        {
            _controller.Update((float)dt);
        }

        if (_resized)
        {
            _resized = false;
            var (dyeX, dyeY) = GetFboSizeFromResolution(DyeResolution);
            var (x, y) = GetFboSizeFromResolution(SimResolution);
            _velocityBuff.Resize(x, y);
            _dyeBuff.Resize(dyeX, dyeY);
        }

        if (_paused == false)
        {
            _simStopwatch.Restart();
            _gl.Disable(EnableCap.Blend);

            for(var i = 0; i < _pointers.Count; i++)
            {
                var p = _pointers[i];
                if (p.Moved)
                {
                    p.Moved = false;
                    Splat(p);
                }
            }

            var curlProgram = Shaders["curl"];
            curlProgram.Bind();
            curlProgram.SetUniform("texelSize", _velocityBuff.TexelSizeX, _velocityBuff.TexelSizeY);
            curlProgram.SetUniform("uVelocity", _velocityBuff.Read.Attach(TextureUnit.Texture0));
            Blit(_curlBuff);

            var vorticityProgram = Shaders["vorticity"];
            vorticityProgram.Bind();
            vorticityProgram.SetUniform("texelSize", _velocityBuff.TexelSizeX, _velocityBuff.TexelSizeY);
            vorticityProgram.SetUniform("uVelocity", _velocityBuff.Read.Attach(TextureUnit.Texture0));
            vorticityProgram.SetUniform("uCurl", _curlBuff.Attach(TextureUnit.Texture1));
            vorticityProgram.SetUniform("curl", _curl);
            vorticityProgram.SetUniform("dt", dt);
            Blit(_velocityBuff.Write);
            _velocityBuff.Swap();

            var divergenceProgram = Shaders["divergence"];
            divergenceProgram.Bind();
            divergenceProgram.SetUniform("texelSize", _velocityBuff.TexelSizeX, _velocityBuff.TexelSizeY);
            divergenceProgram.SetUniform("uVelocity", _velocityBuff.Read.Attach(TextureUnit.Texture0));
            Blit(_divergenceBuff);

            var clearProgram = Shaders["clear"];
            clearProgram.Bind();
            clearProgram.SetUniform("uTexture", _pressureBuff.Read.Attach(TextureUnit.Texture0));
            clearProgram.SetUniform("value", _pressure); // pressure
            Blit(_pressureBuff.Write);
            _pressureBuff.Swap();

            var pressureProgram = Shaders["pressure"];
            pressureProgram.Bind();
            pressureProgram.SetUniform("texelSize", _velocityBuff.TexelSizeX, _velocityBuff.TexelSizeY);
            pressureProgram.SetUniform("uDivergence", _divergenceBuff.Attach(TextureUnit.Texture0));

            for (var i = 0; i < _pressureIterations; i++)
            {
                pressureProgram.SetUniform("uPressure", _pressureBuff.Read.Attach(TextureUnit.Texture1));
                Blit(_pressureBuff.Write);
                _pressureBuff.Swap();
            }

            var gradientSubtractProgram = Shaders["gradient_subtract"];
            gradientSubtractProgram.Bind();
            gradientSubtractProgram.SetUniform("texelSize", _velocityBuff.TexelSizeX, _velocityBuff.TexelSizeY);
            gradientSubtractProgram.SetUniform("uPressure", _pressureBuff.Read.Attach(TextureUnit.Texture0));
            gradientSubtractProgram.SetUniform("uVelocity", _velocityBuff.Read.Attach(TextureUnit.Texture1));
            Blit(_velocityBuff.Write);
            _velocityBuff.Swap();

            var advectionProgram = Shaders["advection"];
            advectionProgram.Bind();
            advectionProgram.SetUniform("texelSize", _velocityBuff.TexelSizeX, _velocityBuff.TexelSizeY);
            if (LinearFiltering == false)
            {
                advectionProgram.SetUniform("dyeTexelSize", _velocityBuff.TexelSizeX, _velocityBuff.TexelSizeY);
            }
            var velocityId = _velocityBuff.Read.Attach(TextureUnit.Texture0);
            advectionProgram.SetUniform("uVelocity", velocityId);
            advectionProgram.SetUniform("uSource", velocityId);
            advectionProgram.SetUniform("dt", dt);
            advectionProgram.SetUniform("dissipation", _velocityDissipation);
            Blit(_velocityBuff.Write);
            _velocityBuff.Swap();
            if (LinearFiltering == false)
            {
                advectionProgram.SetUniform("dyeTexelSize", _dyeBuff.TexelSizeX, _dyeBuff.TexelSizeY);
            }
            advectionProgram.SetUniform("uVelocity", _velocityBuff.Read.Attach(TextureUnit.Texture0));
            advectionProgram.SetUniform("uSource", _dyeBuff.Read.Attach(TextureUnit.Texture1));
            advectionProgram.SetUniform("dissipation", _densityDissipation);
            Blit(_dyeBuff.Write);
            _dyeBuff.Swap();
            _simStopwatch.Stop();
        }

        if (_steppingMode)
        {
            _paused = true;
        }

        var simStatus = _paused ? "PAUSED" : "RUNNING";

        var vizMode = (VisualizationMode)_vizModeSelected;

        _window.Title = $"status={simStatus} mode={vizMode} stepping={_steppingMode}";

        switch (vizMode)
        {
            case VisualizationMode.Velocity:
                _velocityBuff.Read.BlitToScreen(_window.FramebufferSize); break;
            case VisualizationMode.Curl:
                _curlBuff.BlitToScreen(_window.FramebufferSize); break;
            case VisualizationMode.Divergence:
                _divergenceBuff.BlitToScreen(_window.FramebufferSize); break;
            case VisualizationMode.Pressure:
                _pressureBuff.Read.BlitToScreen(_window.FramebufferSize); break;
            case VisualizationMode.Dye:
                _dyeBuff.Read.BlitToScreen(new Vector2D<int>((int)_actualWindowWidth, _window.Size.Y)); 
                break;
            default:
                break;
        }

        if (_showGui)
        {
            RenderGui();
            _gl.UseProgram(0);
            _gl.Viewport(0, 0, (uint)_window.Size.X, (uint)_window.Size.Y);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            _controller.Render();
        }
    }

    public void OnClosing()
    {
        _input.Dispose();
        _curlBuff.Dispose();
        _divergenceBuff.Dispose();
        _dyeBuff.Dispose();
        _pressureBuff.Dispose();
        _velocityBuff.Dispose();
        foreach (var s in Shaders.Values) s.Dispose();
        _controller.Dispose();
        _gl.Dispose();
    }
}
