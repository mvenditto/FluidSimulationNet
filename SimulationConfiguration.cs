namespace FluidSim;

public record SimulationConfiguration
{
    public int WindowWidth { get; init; } = 1200;
    public int WindowHeight { get; init; } = 600;
    public float Curl { get; init; } = 30;
    public float Pressure { get; init; } = 0.8f;
    public float VelocityDissipation { get; init; } = 0.2f;
    public float DensityDissipation { get; init; } = 1.0f;
    public int PressureIterations { get; init; } = 100;
    public int DyeResolution { get; init; } = 1024;
    public int SimResolution { get; init; } = 512;
    public float SplatRadius { get; init; } = 0.25f;
    public float DefaultSplatDx { get; init; } = 0.0f;
    public float DefaultSplatDy { get; init; } = 150.0f;
    public bool Paused { get; init; } = false;
    public bool HalfFloat { get; init; } = true;
    public bool LinearFiltering { get; init; } = false;
    public bool Stepping { get; init; } = false;
    public bool ShowGui { get; init; } = true;
    public bool Bloom { get; init; } = true;
    public int BloomIterations { get; init; } = 8;
    public int BloomResolution { get; init; } = 256;
    public float BloomIntensity { get; init; } = 0.8f;
    public float BloomThreshold { get; init; } = 0.6f;
    public float BloomSoftKnee { get; init; } = 0.7f;
    public VisualizationMode ActiveVisualizationMode { get; init; } = VisualizationMode.Fancy;
    public string? ScreenshotsFolder { get; init; }
}
