namespace Erya.Core.Distributions;

/// <summary>
/// Detects one kind of RIME distribution on this machine.
/// Returns null when the distribution is not installed.
/// </summary>
public interface IRimeDistributionLocator
{
    IRimeDistribution? Locate();
}
