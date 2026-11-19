namespace GosTek.Models;

public sealed record DocumentFormatting(
    double? LeftMm, double? RightMm, double? TopMm, double? BottomMm,
    string? FontFamily, double? FontSize, double? LineSpacing, double? FirstLineMm, TextAlignment? Alignment);
