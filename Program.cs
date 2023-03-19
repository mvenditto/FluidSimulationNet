using FluidSim;
using Microsoft.Extensions.Configuration;

var config = new SimulationConfiguration();

try {
    new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .Build()
        .GetSection("Simulation")
        .Bind(config);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Console.WriteLine("Failed to load configuration file. Running with defaults.");
}

var sim = new FluidSimulation(config);

sim.Run();