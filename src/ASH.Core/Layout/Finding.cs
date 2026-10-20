using System.Collections.Generic;
using ASH.Core.Geometry;

namespace ASH.Core.Layout;

public enum FindingCode { DanglingEnd, DeadEndNode, DuplicateCpName, UnnamedCp, CpOffPath, BlockTouchesNoPath, UnsplitIntersection, RouteCpMissing }

public sealed record Finding(FindingCode Code, string Message, Point2 Where, string? EntityId);

public sealed record CheckOptions(double CpMaxOffset = 0.05, IReadOnlyCollection<string>? RouteCpNames = null);
