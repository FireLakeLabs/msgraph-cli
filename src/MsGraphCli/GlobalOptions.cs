using System.CommandLine;

namespace MsGraphCli;

/// <summary>
/// Holds references to global options so command builders can access them.
/// </summary>
public sealed record GlobalOptions(
    Option<bool> Json,
    Option<bool> Plain,
    Option<bool> Verbose,
    Option<bool> Beta,
    Option<bool> ReadOnly
);
