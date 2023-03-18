using FluidSim;
using static FluidSim.FluidSimulation;

var sim = new FluidSimulation(1200, 600)
{
    Vorticity = 30,
    Pressure = 0.8f,
    VelocityDissipation = 0.2f,
    DensityDissipation = 1.0f,
    DefaultSplatRadius = 0.5f,
    PressureIterations = 100,
    HalfFloat = true,
    DyeResolution = 1024,
    SimResolution = 512,
    Paused = false,
    Stepping = false,
    ShowGui = true,
    ActiveVisualizationMode = VisualizationMode.Dye
};

sim.Run();