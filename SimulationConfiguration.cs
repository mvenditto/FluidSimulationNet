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
    public VisualizationMode ActiveVisualizationMode { get; init; } = VisualizationMode.Dye;
    public string? ScreenshotsFolder { get; init; }
}
