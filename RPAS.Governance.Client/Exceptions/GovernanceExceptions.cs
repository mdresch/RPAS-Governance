using System;

namespace RPAS.Governance.Client.Exceptions;

/// <summary>
/// Thrown when a ritual petition violates an RPAS-CM structural law (409 Conflict).
/// </summary>
public class RpasLawViolationException : Exception
{
    public string RuleName { get; }

    public RpasLawViolationException(string ruleName, string message) 
        : base($"[RPAS-LAW-VIOLATION] ({ruleName}): {message}")
    {
        RuleName = ruleName;
    }
}

/// <summary>
/// Thrown when a mutation attempt violates a G6 Topology Envelope (403 Forbidden).
/// </summary>
public class TopologyViolationException : Exception
{
    public string TargetPath { get; }

    public TopologyViolationException(string targetPath, string message)
        : base($"[TOPOLOGY-VIOLATION] ({targetPath}): {message}")
    {
        TargetPath = targetPath;
    }
}
