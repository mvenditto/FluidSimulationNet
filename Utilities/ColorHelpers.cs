namespace FluidSim.Utilities;

public static class ColorHelpers
{
    public static (float r, float g, float b) HsvToRgb(float h, float s, float v)
    {
        double r, g, b, i, f, p, q, t;
        i = Math.Floor(h * 6);
        f = h * 6 - i;
        p = v * (1 - s);
        q = v * (1 - f * s);
        t = v * (1 - (1 - f) * s);

        r = g = b = 0;

        switch (i % 6)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            case 5: r = v; g = p; b = q; break;
        }

        return ((float)r, (float)g, (float)b);
    }

    public static (float r, float g, float b) RandomColor()
    {
        var (r, g, b) = HsvToRgb((float)Random.Shared.NextDouble(), 1, 1);
        return (r * 0.15f, g * 0.15f, b * 0.15f);
    }

    public static (float r, float g, float b) NextColorRgb(this Random rand)
    {
        var (r, g, b) = HsvToRgb((float)rand.NextDouble(), 1, 1);
        return (r * 0.15f, g * 0.15f, b * 0.15f);
    }
}
