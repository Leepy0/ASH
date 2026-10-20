using System.Collections.Generic;

namespace ASH.Core.Model;

public sealed record RunScenario(int Seed, IReadOnlyDictionary<string, int> ActiveByGroup);
