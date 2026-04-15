using System;

namespace RPAS.Governance.Core.Models.Exceptions;

public class RpasLawViolationException : Exception
{
    public string RuleName { get; }

    public RpasLawViolationException(string ruleName, string message) 
        : base($"[RPAS-LAW-VIOLATION] ({ruleName}): {message}")
    {
        RuleName = ruleName;
    }

    public RpasLawViolationException(string ruleName, string message, Exception innerException) 
        : base($"[RPAS-LAW-VIOLATION] ({ruleName}): {message}", innerException)
    {
        RuleName = ruleName;
    }
}
