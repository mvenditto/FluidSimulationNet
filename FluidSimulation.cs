using System.Numerics;
using System.Diagnostics;
using FluidSim.Abstractions;
using FluidSim.Utilities;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Input;
using ImGuiNET;
using System.Runtime.InteropServices;
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
    
    private int _simResolution;
    private int _dyeResolution;
    private int _bloomResolution;
    private int _sunraysResolution;
    private int _bloomIterations;
    private float _bloomThreshold;
    private float _bloomSoftKnee;
    private float _bloomIntensity;
    private float _sunraysWeight = 1.0f;
    private int _pressureIterations;
    private float _curl;
    private float _pressure;
    private float _velocityDissipation;
    private float _densityDissipation;
    private float _splatRadius;
    private float _splatForce = 6000f;
    private float _splatDx;
    private float _splatDy;
    private bool _enableBloom = true;
    private bool _enableShading = true;
    private bool _enableSunrays = true;
    private bool _paused;
    private bool _steppingMode = true;
    private bool _showGui = true;
    private bool _screenshotRequested = false;
    private int _vizModeSelected = (int)VisualizationMode.Dye;
    private Vector4 _splatColor = new(1, 1, 0, 1);
    private float _fixedDt = 1.0f / 60f;
    private readonly Stopwatch _simStopwatch = new();
    private int _simResolutionIndex = SimResolutionValues.Length - 2;
    private int _dyeResolutionIndex = 0;
    private static readonly string[] DyeResolutionLabels = new string[] { "high (1024)", "medium (512)", "low (256)", "very low (128)" };
    private static readonly int[] DyeResolutionValues = new int[] { 1024, 512, 256, 128 };
    private static readonly string[] SimResolutionLabels = new string[] { "very low (32)", "low (64)", "medium (128)", "high (256)", "very high(512)", "ultra (1024)" };
    private static readonly int[] SimResolutionValues = new int[] { 32, 64, 128, 256, 512, 1024 };
    private string _screenshotFolder;

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
    private bool _linearFiltering = false;
    private bool _halfFloat = true;
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
    private FrameBufferObject _bloom;
    private FrameBufferObject _sunrays;
    private FrameBufferObject _sunraysTemp;
    private DoubleFrameBuffer _velocityBuff;
    private DoubleFrameBuffer _pressureBuff;
    private DoubleFrameBuffer _dyeBuff;
    private IDictionary<string, ShaderProgram> Shaders = new Dictionary<string, ShaderProgram>();
    private IList<FrameBufferObject> _bloomFramebuffers = new List<FrameBufferObject>();
    private Abstractions.Texture _ditheringTexture;
    private Vector2D<float> _ditherScale;
    #endregion

    #region GUI
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

    public FluidSimulation(SimulationConfiguration config)
    {
        _paused = config.Paused;
        _steppingMode = config.Stepping;
        _pressure = config.Pressure;
        _pressureIterations = config.PressureIterations;
        _curl = config.Curl;
        _velocityDissipation = config.VelocityDissipation;
        _densityDissipation = config.DensityDissipation;
        _splatDx = config.DefaultSplatDx;
        _splatDy = config.DefaultSplatDy;
        _splatRadius = config.SplatRadius;
        _splatForce = 6000f;
        _simResolution = config.SimResolution;
        _dyeResolution = config.DyeResolution;
        _vizModeSelected = (int)config.ActiveVisualizationMode;
        _halfFloat = config.HalfFloat;
        _linearFiltering = config.LinearFiltering;
        _bloomIterations = config.BloomIterations;
        _bloomResolution = config.BloomResolution;
        _bloomThreshold = config.BloomThreshold;
        _bloomSoftKnee = config.BloomSoftKnee;
        _bloomIntensity = config.BloomIntensity;
        _enableBloom = config.Bloom;
        _enableSunrays = config.Sunrays;
        _sunraysResolution = config.SunraysResolution;
        _sunraysWeight = config.SunraysWeight;

        _screenshotFolder = string.IsNullOrEmpty(config.ScreenshotsFolder)
            ? Path.GetDirectoryName(Environment.ProcessPath)
            : config.ScreenshotsFolder;

        var options = WindowOptions.Default;
        options.Size = new Vector2D<int>(config.WindowWidth, config.WindowHeight);
        options.Title = "Fluidsim";
#if OPENGLES
        options.API = new GraphicsAPI(
            ContextAPI.OpenGLES, 
            ContextProfile.Core, 
            ContextFlags.Default, 
            new APIVersion(3, 0));
#endif
        _window = Window.Create(options);
        _guiPanelSize = new Vector2(400, config.WindowHeight);
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
        _gl = _window.CreateOpenGLES();
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
        var (dyeX, dyeY) = GetFboSizeFromResolution(_dyeResolution);
        var (x, y) = GetFboSizeFromResolution(_simResolution);

        var filtering = _linearFiltering ? GLEnum.Linear : GLEnum.Nearest;
        var rgba = _halfFloat ? Rgba16f : Rgba32f;
        var rg = _halfFloat ? Rg16f : Rg32f;
        var r = _halfFloat ? R16f : R32f;
        var type = _halfFloat ? GLEnum.HalfFloat : GLEnum.Float;

        _dyeBuff = _bufferFactory.CreateDoubleFrameBuffer(dyeX, dyeY, rgba, PixelFormat.Rgba, type, filtering);
        _velocityBuff = _bufferFactory.CreateDoubleFrameBuffer(x, y, rg, PixelFormat.RG, type, filtering);
        _curlBuff = _bufferFactory.CreateFrameBuffer(x, y, r, PixelFormat.Red, type, GLEnum.Nearest);
        _divergenceBuff = _bufferFactory.CreateFrameBuffer(x, y, r, PixelFormat.Red, type, GLEnum.Nearest);
        _pressureBuff = _bufferFactory.CreateDoubleFrameBuffer(x, y, r, PixelFormat.Red, type, GLEnum.Nearest);

        _ditheringTexture = new Abstractions.Texture(
            _gl,
            "Resources\\LDR_LLL1_0.png",
            InternalFormat.Rgba,
            PixelFormat.Rgba,
            PixelType.UnsignedByte);

        _ditherScale = new Vector2D<float>(
            _actualWindowWidth / (float) _ditheringTexture.Width,
            _window.FramebufferSize.Y / (float) _ditheringTexture.Height
         );

        InitBloomFrameBuffers(); 
        InitSunraysFrameBuffers();
    }

    private void InitBloomFrameBuffers()
    {
        Console.WriteLine("BLOOM_FILTERS");
        var (w, h) = GetFboSizeFromResolution(_bloomResolution);
        var filtering = _linearFiltering ? GLEnum.Linear : GLEnum.Nearest;
        var rgba = _halfFloat ? Rgba16f : Rgba32f;
        var type = _halfFloat ? GLEnum.HalfFloat : GLEnum.Float;
        _bloom = _bufferFactory.CreateFrameBuffer(w, h, rgba, PixelFormat.Rgba, type, filtering);
        _bloomFramebuffers.Clear();
        for (var i = 0; i < _bloomIterations; i++)
        {
            var width = w >> (i + 1);
            var height = h >> (i + 1);
            if (width < 2 || height < 2) break;
            var fbo = _bufferFactory.CreateFrameBuffer(width, height, rgba, PixelFormat.Rgba, type, filtering);
            _bloomFramebuffers.Add(fbo);
        }
    }

    private void InitSunraysFrameBuffers()
    {
        var (w, h) = GetFboSizeFromResolution(_sunraysResolution);
        var filtering = _linearFiltering ? GLEnum.Linear : GLEnum.Nearest;
        var r = _halfFloat ? R16f : R32f;
        var type = _halfFloat ? GLEnum.HalfFloat : GLEnum.Float;
        _sunrays = _bufferFactory.CreateFrameBuffer(w, h, r, PixelFormat.Red, type, filtering);
        _sunraysTemp = _bufferFactory.CreateFrameBuffer(w, h, r, PixelFormat.Red, type, filtering);
    }

    private void ResizeFrameBuffers()
    {
        var (dyeX, dyeY) = GetFboSizeFromResolution(_dyeResolution);
        var (x, y) = GetFboSizeFromResolution(_simResolution);

        _velocityBuff.Resize(x, y);
        _dyeBuff.Resize(dyeX, dyeY);
        _curlBuff.Resize(x, y);
        _divergenceBuff.Resize(x, y);
        _pressureBuff.Resize(x, y);

        _bloom.Dispose();
        foreach(var bloomFbo in _bloomFramebuffers)
        {
            bloomFbo.Dispose();
        }
        InitBloomFrameBuffers();

        _sunrays.Dispose();
        _sunraysTemp.Dispose();
        InitSunraysFrameBuffers();
    }

    private void ApplySunrays()
    {
        var source = _dyeBuff.Read;
        var mask = _dyeBuff.Write;
        var destination = _sunrays;

        _gl.Disable(EnableCap.Blend);
        var sunraysMaskProgram = Shaders["sunrays_mask"];
        sunraysMaskProgram.Bind();
        sunraysMaskProgram.SetUniform("uTexture", source.Attach(TextureUnit.Texture0));
        Blit(mask);

        var sunraysProgram = Shaders["sunrays"];
        sunraysProgram.Bind();
        sunraysProgram.SetUniform("weight", _sunraysWeight);
        sunraysProgram.SetUniform("uTexture", mask.Attach(TextureUnit.Texture0));
        Blit(destination);
    }

    private void ApplyBloom()
    {
        var source = _dyeBuff.Read;
        var destination = _bloom;

        if (_bloomFramebuffers.Count < 2) return;

        var last = destination;

        _gl.Disable(EnableCap.Blend);

        var bloomPrefilterProgram = Shaders["blur_prefilter"];
        bloomPrefilterProgram.Bind();
        var knee = _bloomThreshold * _bloomSoftKnee + 0.0001;
        var curve0 = _bloomThreshold - knee;
        var curve1 = knee * 2;
        var curve2 = 0.25 / knee;
        bloomPrefilterProgram.SetUniform("curve", curve0, curve1, curve2);
        bloomPrefilterProgram.SetUniform("threshold", _bloomThreshold);
        bloomPrefilterProgram.SetUniform("uTexture", source.Attach(TextureUnit.Texture0));
        Blit(last);

        var bloomBlurProgram = Shaders["bloom_blur"];
        bloomBlurProgram.Bind();

        for (var i = 0; i < _bloomFramebuffers.Count; i++)
        {
            var dest = _bloomFramebuffers[i];
            bloomBlurProgram.SetUniform("texelSize", last.TexelSizeX, last.TexelSizeY);
            bloomBlurProgram.SetUniform("uTexture", last.Attach(TextureUnit.Texture0));
            Blit(dest);
            last = dest;
        }

        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);
        _gl.Enable(EnableCap.Blend);

        for (var i = _bloomFramebuffers.Count - 2; i >= 0; i--)
        {
            var baseTex = _bloomFramebuffers[i];
            bloomBlurProgram.SetUniform("texelSize", last.TexelSizeX, last.TexelSizeY);
            bloomBlurProgram.SetUniform("uTexture", last.Attach(TextureUnit.Texture0));
            Blit(baseTex);
            last = baseTex;
        }

        _gl.Disable(EnableCap.Blend);
        var bloomFinalProgram = Shaders["bloom_final"];
        bloomFinalProgram.Bind();
        bloomFinalProgram.SetUniform("texelSize", last.TexelSizeX, last.TexelSizeY);
        bloomFinalProgram.SetUniform("uTexture", last.Attach(TextureUnit.Texture0));
        bloomFinalProgram.SetUniform("intensity", _bloomIntensity);
        Blit(destination);
    }

    private void Blur(FrameBufferObject target, FrameBufferObject temp, int iterations)
    {
        var blurProgram = Shaders["blur"];
        blurProgram.Bind();
        for (var i = 0; i < iterations; i++)
        {
            blurProgram.SetUniform("texelSize", target.TexelSizeX, 0.0);
            blurProgram.SetUniform("uTexture", target.Attach(TextureUnit.Texture0));
            Blit(temp);


            blurProgram.SetUniform("texelSize", 0.0, target.TexelSizeY);
            blurProgram.SetUniform("uTexture", temp.Attach(TextureUnit.Texture0));
            Blit(target);
        }
    }

    private unsafe void FrameBufferToImage(FrameBufferObject fbo)
    {
        var (w, h) = (fbo.Width, fbo.Height);
        var data = NativeMemory.Alloc((nuint)w * h * 3);
        try
        {
            using var capture = _bufferFactory.CreateFrameBuffer(w, h, InternalFormat.Rgb, PixelFormat.Rgb, GLEnum.UnsignedByte, GLEnum.Nearest);
            fbo.BlitTo(capture);
            var span = new Span<byte>(data, (int)(w * h * 3));
            _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, capture.Handle);
            _gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
            _gl.ReadPixels(0, 0, (uint)w, (uint)h, GLEnum.Rgb, GLEnum.UnsignedByte, span);
            var mem = new Memory<byte>(span.ToArray());
            var img = Image.WrapMemory<Rgb24>(mem, (int)w, (int)h);
            img.Mutate(x => x.Flip(FlipMode.Vertical));
            var path = Path.Join(
                _screenshotFolder,
               "capture_"  + DateTime.Now.ToString("yyyyMMddTHHmmss") + ".png");
            img.SaveAsPng(path);
        }
        finally
        {
            NativeMemory.Free(data);
        }
    }

    private IEnumerable<string> GetShaderDefines()
    {
        var defines = new List<string>();
        if (_enableBloom) defines.Add("BLOOM");
        if (_enableShading) defines.Add("SHADING");
        if (_enableSunrays) defines.Add("SUNRAYS");
        return defines;
    }

    private void InitShaders()
    {
        var baseVertex = Path.Join("Shaders", "base.vert");
        var blurVertex = Path.Join("Shaders", "blur.vert");
        foreach (var frag in Directory.EnumerateFiles("Shaders"))
        {
            if (frag.EndsWith(".vert")) continue;
            var name = Path.GetFileNameWithoutExtension(frag);
            var vertext = name == "blur" ? blurVertex : baseVertex;
            Console.WriteLine($"COMPILING: {frag} {vertext}");
            IEnumerable<string>? defines = name == "display" ? GetShaderDefines() : null;
            var program = new ShaderProgram(_gl, vertext, frag, defines);
            Shaders.Add(name, program);
        }

        if (_linearFiltering == false)
        {
            Shaders["advection"] = Shaders["advection_manual_filtering"];
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
                    Splat(0.5f, 0.5f, 
                        _splatDx,
                        _splatDy, 
                        _splatColor.X, _splatColor.Y, _splatColor.Z, 
                        _splatRadius);
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
            _gl.Viewport(0, 0, (uint)_actualWindowWidth, (uint)_window.FramebufferSize.Y);
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
        
        if(ImGui.Combo("sim. resolution.", ref _simResolutionIndex, SimResolutionLabels, SimResolutionLabels.Length))
        {
            var newSimResolution = SimResolutionValues[_simResolutionIndex];
            if (newSimResolution != _simResolution)
            {
                _simResolution = newSimResolution;
                _resized = true;
            }
        }

        if (ImGui.Combo("dye. resolution.", ref _dyeResolutionIndex, DyeResolutionLabels, DyeResolutionLabels.Length))
        {
            var newDyeResolution = DyeResolutionValues[_dyeResolutionIndex];
            if (newDyeResolution != _dyeResolution)
            {
                _dyeResolution = newDyeResolution;
                _resized = true;
            }
        }

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
            Splat(0.25f, 0.5f, 
                _splatDx, _splatDy, 
                _splatColor.X, _splatColor.Y, _splatColor.Z, _splatRadius);
        }
        ImGui.SameLine(0, -1);
        if (ImGui.Button("random splats"))
        {
            RandomSplats(10);
        }

        SectionText("Display");
        ImGui.Combo("viz. mode", ref _vizModeSelected, VizModes, VizModes.Length); 
        if (ImGui.Checkbox("shading", ref _enableShading))
        {
            Shaders["display"].UpdateDefines(GetShaderDefines());
        }
        if (ImGui.Checkbox("bloom", ref _enableBloom))
        {
            Shaders["display"].UpdateDefines(GetShaderDefines());
        }
        ImGui.SliderFloat("bloom intensity", ref _bloomIntensity, 0.1f, 2.0f);
        ImGui.SliderFloat("bloom threshold", ref _bloomThreshold, 0.0f, 0.1f);
        if (ImGui.Checkbox("sunrays", ref _enableSunrays))
        {
            Shaders["display"].UpdateDefines(GetShaderDefines());
        }
        ImGui.SliderFloat("sunrays weight", ref _sunraysWeight, 0.3f, 1.0f);

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

        SectionText("Captures");
        ImGui.InputText(string.Empty, ref _screenshotFolder, 260);
        if (ImGui.Button("take screenshot"))
        {
            _screenshotRequested = true;
        }
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
            ResizeFrameBuffers();
        }

        if (_screenshotRequested)
        {
            FrameBufferToImage(_dyeBuff.Read);
            _screenshotRequested = false;
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
            vorticityProgram.SetUniform("dt", _fixedDt);
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
            if (_linearFiltering == false)
            {
                advectionProgram.SetUniform("dyeTexelSize", _velocityBuff.TexelSizeX, _velocityBuff.TexelSizeY);
            }
            var velocityId = _velocityBuff.Read.Attach(TextureUnit.Texture0);
            advectionProgram.SetUniform("uVelocity", velocityId);
            advectionProgram.SetUniform("uSource", velocityId);
            advectionProgram.SetUniform("dt", _fixedDt);
            advectionProgram.SetUniform("dissipation", _velocityDissipation);
            Blit(_velocityBuff.Write);
            _velocityBuff.Swap();
            if (_linearFiltering == false)
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
            case VisualizationMode.Fancy:
                if (_enableBloom)
                {
                    ApplyBloom();
                }
                if (_enableSunrays)
                {
                    ApplySunrays();
                    Blur(_sunrays, _sunraysTemp, 1);
                }
                _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
                _gl.Enable(EnableCap.Blend);
                var displayProgram = Shaders["display"];
                displayProgram.Bind();
                var (width, height) = ((uint)_actualWindowWidth, (uint)_window.FramebufferSize.Y);
                if (_enableShading)
                {
                    displayProgram.SetUniform("texelSize", 1.0f / width, 1.0f / height);
                }
                displayProgram.SetUniform("uTexture", _dyeBuff.Read.Attach(TextureUnit.Texture0));
                if (_enableBloom)
                {
                    displayProgram.SetUniform("uBloom", _bloom.Attach(TextureUnit.Texture1));
                    displayProgram.SetUniform("uDithering", _ditheringTexture.Attach(TextureUnit.Texture2));
                    displayProgram.SetUniform("ditherScale", _ditherScale.X, _ditherScale.Y);
                }
                if (_enableSunrays)
                {
                    displayProgram.SetUniform("uSunrays", _sunrays.Attach(TextureUnit.Texture3));
                }
                Blit(null);
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
