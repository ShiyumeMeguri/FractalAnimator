namespace FractalAnimator.Core.Mb3d.Render;

/// <summary>A double-precision 3-vector for the ray marcher.</summary>
public readonly record struct Vec3d(double X, double Y, double Z)
{
    public static Vec3d operator +(Vec3d a, Vec3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3d operator -(Vec3d a, Vec3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3d operator *(Vec3d a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);
    public Vec3d Normalized() { double l = Length(); return l > 0 ? new(X / l, Y / l, Z / l) : this; }
    public static double Dot(Vec3d a, Vec3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}

/// <summary>
/// Per-pixel ray construction, mirroring RMCalculateStartPos (Calc.pas:1101) and
/// RMCalculateVgradsFOV (Calc.pas:1205). Origin walks a flat grid on the front plane; direction is a
/// camera-space vector rotated into world space by the (StepWidth-scaled) camera basis.
/// </summary>
public sealed class Mb3dCamera
{
    private readonly Mb3dSceneParameters _scene;

    public Mb3dCamera(Mb3dSceneParameters scene) => _scene = scene;

    public Vec3d Origin(int ix, int iy)
    {
        var g = _scene.VGrads;
        return new Vec3d(
            _scene.Ystart[0] + g[0] * ix + g[3] * iy,
            _scene.Ystart[1] + g[1] * ix + g[4] * iy,
            _scene.Ystart[2] + g[2] * ix + g[5] * iy);
    }

    /// <summary>The camera/Vgrads-frame view direction (unit), before rotation into world space.</summary>
    public Vec3d CameraSpaceDirection(int ix, int iy)
    {
        double cafY = ((double)iy / _scene.Height - 0.5) * _scene.FovY;
        double cafX = (_scene.FovXOffset - ix) * _scene.FovXMul;
        return _scene.CameraOptic switch
        {
            1 => new Vec3d(-cafX, cafY, _scene.PlanarOpticZ).Normalized(),     // rectilinear
            2 => BuildPanorama(cafY, cafX),                                    // equirectangular
            _ => BuildAngular(cafY, cafX),                                     // fisheye / angular
        };
    }

    /// <summary>World-space ray direction (length = StepWidth, matching the marcher's step units).</summary>
    public Vec3d Direction(int ix, int iy) => CameraToWorld(CameraSpaceDirection(ix, iy));

    /// <summary>RotateVectorReverse: V[j] = sum_i cam[i] * VGrads[row i][j] (multiply by the basis rows).</summary>
    public Vec3d CameraToWorld(Vec3d cam)
    {
        var g = _scene.VGrads;
        return new Vec3d(
            cam.X * g[0] + cam.Y * g[3] + cam.Z * g[6],
            cam.X * g[1] + cam.Y * g[4] + cam.Z * g[7],
            cam.X * g[2] + cam.Y * g[5] + cam.Z * g[8]);
    }

    // BuildViewVectorDFOV (Math3D.pas:2743): angular projection from per-pixel sines.
    private static Vec3d BuildAngular(double cafY, double cafX)
    {
        double sx = Math.Sin(cafY), sy = Math.Sin(cafX);
        double cx = Math.Cos(cafY), cy = Math.Cos(cafX);
        double z = Math.Sqrt(Math.Max(0, 1 - sx * sx - sy * sy));
        if (cx * cy < 0) z = -z;
        return new Vec3d(-sy, sx, z);
    }

    private static Vec3d BuildPanorama(double cafY, double cafX)
    {
        // Equirectangular: cafX is longitude, cafY latitude.
        double cl = Math.Cos(cafY);
        return new Vec3d(Math.Sin(cafX) * cl, Math.Sin(cafY), Math.Cos(cafX) * cl);
    }
}
