namespace ASH.Core.Cel;

public sealed record ConvertResult(System.Collections.Generic.IReadOnlyList<Box> Boxes, string Cel, string Svg, SimplifyResult Stats);

public static class AssetConverter
{
    /// <summary>MeshReader → BoxSimplifier → CelWriter.Cel(cellName) → SvgPreview. Exceptions pass through (FormatException, NotSupportedException, ArgumentException).</summary>
    public static ConvertResult Convert(string sourcePath, string cellName, SimplifyOptions o)
    {
        var st = BoxSimplifier.Simplify(MeshReader.Read(sourcePath), o);
        return new ConvertResult(st.Boxes, CelWriter.Cel(cellName, st.Boxes), SvgPreview.Render(st.Boxes), st);
    }
}
