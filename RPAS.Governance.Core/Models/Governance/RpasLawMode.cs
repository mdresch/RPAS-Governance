namespace RPAS.Governance.Core.Models.Governance;

/// <summary>
/// Operational mode for RPAS-CM structural law enforcement.
/// </summary>
public enum RpasLawMode
{
    /// <summary>
    /// Laws are enforced at the DB and Interceptor level. Violations result in exceptions and voided transactions.
    /// </summary>
    Enforced,
    
    /// <summary>
    /// Laws are evaluated, and violations generate warnings. Used for transitioning legacy data or smoke testing.
    /// </summary>
    Advisory
}
