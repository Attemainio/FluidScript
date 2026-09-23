using FluidScript.Core.Components.Exchangers;

namespace FluidScript.Core.Solvers.Results;

/// <summary>An extended-mode exchanger at the solution: both sides, the duty and the two routes to its conductance.</summary>
/// <param name="Inlet1">Side 1 entering temperature, K.</param>
/// <param name="Outlet1">Side 1 leaving temperature, K.</param>
/// <param name="Capacity1">Side 1 capacity rate, W/K.</param>
/// <param name="Inlet2">Side 2 entering temperature, K -- the stated profile when the side is not wired.</param>
/// <param name="Outlet2">Side 2 leaving temperature, K.</param>
/// <param name="Capacity2">Side 2 capacity rate, W/K.</param>
/// <param name="Duty">Heat into side 1, W, positive when side 1 gains.</param>
/// <param name="Ntu">Number of transfer units on Cmin.</param>
/// <param name="Effectiveness">ε for the arrangement.</param>
/// <param name="CapacityRatio">Cmin / Cmax.</param>
/// <param name="Lmtd">The log-mean temperature difference, K.</param>
/// <param name="ConductanceByLogMean">|Duty| / Lmtd, W/K -- the validation route.</param>
/// <param name="Approach">The closest approach, K.</param>
/// <param name="Rating">The rating the exchanger was built with.</param>
public sealed record SolvedExchanger(
    double Inlet1,
    double Outlet1,
    double Capacity1,
    double Inlet2,
    double Outlet2,
    double Capacity2,
    double Duty,
    double Ntu,
    double Effectiveness,
    double CapacityRatio,
    double Lmtd,
    double ConductanceByLogMean,
    double Approach,
    ExchangerRating Rating);
