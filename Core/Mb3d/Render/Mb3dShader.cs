namespace FractalAnimator.Core.Mb3d.Render;

/// <summary>
/// Per-pixel shading, a faithful subset of CalcPixelColor2 (PaintThread.pas:468). Implemented:
/// the LCols surface-color lookup, the 6-light diffuse (table-function) + specular (reflect^exp)
/// accumulation, the AmbCol/AmbCol2 normal-Y ambient gradient, and the background depth gradient.
/// Not yet layered in (constant 1 / skipped for now): SSAO (dAmbSh), hard shadows, depth/dynamic fog,
/// diffuse image maps, indirect light, gamma. Colors: lights/ambient/depth 0..255, surface 0..1.
/// </summary>
public static class Mb3dShader
{
    public static uint Shade(in Mb3dHit hit, Mb3dPaintParameters paint, Mb3dSceneParameters scene, int iy)
    {
        if (!hit.IsHit) return Pack(Background(paint, scene, iy));

        Vec3d n = hit.Normal;
        Vec3d view = hit.ViewVec;

        // DE-based ambient occlusion (CalcPixelColor2:484). dAmbSh in (0,1], 1 = unoccluded. It always
        // scales the ambient term; per light it scales by dAmbSh only when bSubAmbSh (hard shadow wanted
        // but not computed), otherwise by the gentler diffuse-shadow fog dFog (PaintThread.pas:521/536).
        double dAmbSh = Mb3dAmbientOcclusion.Compute(hit, scene);
        double dFog = 1 + paint.DiffuseShadowing * (dAmbSh - 1);

        double index = (paint.ColorOnOrbitTrap & 1) == 0
            ? hit.IterationCount * paint.SmoothIterationScale
            : EncodeOrbitTrap(hit.OrbitTrap, scene);
        LookupSurfaceColor(paint, index, out Vec3d surfaceDiffuse, out Vec3d surfaceSpecular);

        Vec3d diffuseLight = default;
        Vec3d specularLight = default;
        double viewDotN = Vec3d.Dot(n, view);
        for (var i = 0; i < 6; i++)
        {
            ref readonly var light = ref paint.Lights[i];
            if (!light.Active || light.Positional) continue; // global lights only for now
            Vec3d l = light.Direction;
            double lightShadow = light.SubAmbShadow ? dAmbSh : dFog;

            // Directional hard/soft shadow: march a ray from the surface toward this light. The factor
            // (1 = lit, 0 = shadowed) multiplies IN ADDITION to dFog/dAmbSh, exactly as MB3D applies
            // (PaintThread.pas:521-522/536-537: dTmp *= dFog then dTmp *= (Shadow shr 10)*s1d63).
            if (light.HardShadow)
                lightShadow *= Mb3dHardShadow.Compute(hit, light, scene);

            double diffuse = Mb3dDiffuseTable.Evaluate(light.DiffuseFunction, Vec3d.Dot(n, l), paint.Roughness) * lightShadow;
            diffuseLight += light.Color * diffuse;

            // Specular: reflect the view vector about the normal, dot with the light direction.
            Vec3d reflect = view - n * (2 * viewDotN);
            double rl = Vec3d.Dot(l, reflect);
            if (rl > 0)
                specularLight += light.Color * (paint.SpecularMultiplier * FastPow(rl, light.SpecularExponent) * lightShadow);
        }

        double a = n.Y * 0.5 + 0.5;
        Vec3d ambient = Hadamard((paint.AmbientColor * (1 - a) + paint.AmbientColor2 * a) * dAmbSh, surfaceDiffuse);
        Vec3d diffuseColor = surfaceDiffuse * paint.DiffuseMultiplier;
        Vec3d final = ambient + Hadamard(diffuseColor, diffuseLight) + Hadamard(surfaceSpecular, specularLight);

        // Depth fog: blend toward the depth-gradient color by distance (CalcPixelColor2:623-650).
        double zpos = DepthToZpos(hit.Depth, scene);
        double near = Math.Max(0, (zpos - 28000) * paint.DepthFog + 1);
        if (near < 1)
        {
            Vec3d depthColor = Background(paint, scene, iy);
            final = final * near + depthColor * (1 - near);
        }
        return Pack(ApplyGamma(final, paint));
    }

    // Gamma blend (CalcPixelColor2:716-724): brighten via sqrt(c*255) or darken via c^2/255, lerped by sGamma.
    private static Vec3d ApplyGamma(Vec3d color, Mb3dPaintParameters paint)
    {
        if (paint.GammaSign == 0) return color;
        Vec3d clamped = new(Math.Clamp(color.X, 0, 255), Math.Clamp(color.Y, 0, 255), Math.Clamp(color.Z, 0, 255));
        Vec3d gamma = paint.GammaSign > 0
            ? new Vec3d(Math.Sqrt(clamped.X * 255), Math.Sqrt(clamped.Y * 255), Math.Sqrt(clamped.Z * 255))
            : new Vec3d(clamped.X * clamped.X / 255, clamped.Y * clamped.Y / 255, clamped.Z * clamped.Z / 255);
        return gamma * paint.GammaBlend + clamped * (1 - paint.GammaBlend);
    }

    // mZZ (step-unit depth) -> 0..32767 Zpos (near=32767, far=0); CalcZposAndRough (Calc.pas:2780).
    private static double DepthToZpos(double depth, Mb3dSceneParameters scene)
    {
        double itmp = 8388352 - scene.ZcMul * (Math.Sqrt(depth * scene.Zcorr + 1) - 1);
        return Math.Clamp(itmp, 0, 8388352) / 256.0;
    }

    // CalcColors (PaintThread.pas:387): map the iteration/orbit-trap value into the LCols stop range,
    // find the bracketing stops, and interpolate the diffuse + specular colors.
    private static void LookupSurfaceColor(Mb3dPaintParameters p, double index, out Vec3d diffuse, out Vec3d specular)
    {
        double scaled = (index - p.ColorStart) * p.ColorMul * 16384;
        int ir = (int)Math.Round(Math.Clamp(scaled, -1e9, 1e9));
        if (p.ColorCycling) ir &= 32767;
        else if (ir < 0) { diffuse = p.ColorDiffuse[0]; specular = p.ColorSpecular[0]; return; }
        else if (ir >= p.ColorPosition[9]) { diffuse = p.ColorDiffuse[9]; specular = p.ColorSpecular[9]; return; }

        int hi = 5;
        if (p.ColorPosition[hi] < ir)
            do hi++; while (hi != 10 && p.ColorPosition[hi] < ir);
        else
            while (hi > 1 && p.ColorPosition[hi - 1] >= ir) hi--;

        if (p.NoColorInterp)
        {
            diffuse = p.ColorDiffuse[hi - 1];
            specular = p.ColorSpecular[hi - 1];
            return;
        }
        int lo = hi - 1;
        if (hi > 9) hi = 0;
        double t = (ir - p.ColorPosition[lo]) * p.ColorSlope[lo];
        diffuse = Lerp(p.ColorDiffuse[hi], p.ColorDiffuse[lo], t);
        specular = Lerp(p.ColorSpecular[hi], p.ColorSpecular[lo], t);
    }

    // Background = vertical depth gradient (PreCalcDepthCol). Refined with fog later.
    private static Vec3d Background(Mb3dPaintParameters p, Mb3dSceneParameters scene, int iy)
    {
        double s = scene.Height > 1 ? (double)iy / (scene.Height - 1) : 0;
        return Lerp(p.DepthColor2, p.DepthColor, s);
    }

    // Orbit-trap -> 15-bit index: RMdoColor (Calc.pas:1296) default option is OTrap*4096, clipped to 0..32767.
    private static double EncodeOrbitTrap(double orbitTrap, Mb3dSceneParameters scene) =>
        Math.Clamp(orbitTrap * 4096, 0, 32767);

    private static double FastPow(double baseValue, int exponent)
    {
        double result = 1, b = baseValue;
        int e = exponent;
        while (e > 0) { if ((e & 1) != 0) result *= b; b *= b; e >>= 1; }
        return result;
    }

    private static Vec3d Hadamard(Vec3d a, Vec3d b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    private static Vec3d Lerp(Vec3d a, Vec3d b, double w) => a * w + b * (1 - w);

    private static uint Pack(Vec3d color)
    {
        byte r = (byte)Math.Clamp(color.X, 0, 255);
        byte g = (byte)Math.Clamp(color.Y, 0, 255);
        byte b = (byte)Math.Clamp(color.Z, 0, 255);
        return 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
    }
}
