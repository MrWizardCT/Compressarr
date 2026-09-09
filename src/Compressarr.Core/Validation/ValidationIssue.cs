namespace Compressarr.Core.Validation;

/// <summary>A single "this required piece of configuration is missing or wrong" finding - Field is
/// a stable logical key (not a literal DOM id; the frontend maps it to whichever input actually
/// represents it), Message is the full sentence shown in that field's tooltip/help text. Surfaced
/// on the Settings and Lanes pages so a broken required field is visible before a real run ever
/// hits it, instead of only showing up in the log or the HTML report's ReportErrorCode 106+
/// values.</summary>
public sealed record ValidationIssue(string Field, string Message);
