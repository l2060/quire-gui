namespace QuireStudio.Domain.History;

/// <summary>
/// Tag 历史图中的一行。图形语义与 Git 提交图一致：顶部是最新的 Tag，
/// 连线由子 Tag 指向父 Tag；同一父 Tag 下的多个候选会占用并列的分支泳道。
/// </summary>
public sealed record TagGraphRow
{
    public required QuireTag Tag { get; init; }

    /// <summary>行序号，从 0 开始（最新）。</summary>
    public required int Index { get; init; }

    /// <summary>本行节点所在的泳道。</summary>
    public required int Lane { get; init; }

    /// <summary>从上方进入本行的连线所在泳道。等于 <see cref="Lane"/> 时是直线，否则在行内弯折。</summary>
    public IReadOnlyList<int> LinesFromTop { get; init; } = [];

    /// <summary>从本行继续向下延伸的连线所在泳道。</summary>
    public IReadOnlyList<int> LinesToBottom { get; init; } = [];

    /// <summary>是否存在指向更早 Tag 的连线。</summary>
    public bool HasChildren { get; init; }

    /// <summary>是否为历史起点（没有父 Tag）。</summary>
    public bool IsRoot { get; init; }
}

/// <summary>Tag 历史图的完整布局结果。</summary>
public sealed record TagGraph
{
    public static TagGraph Empty { get; } = new() { Rows = [], LaneCount = 0 };

    public required IReadOnlyList<TagGraphRow> Rows { get; init; }

    /// <summary>泳道总数，用于计算图形列宽。</summary>
    public required int LaneCount { get; init; }

    public int Count => Rows.Count;
}

/// <summary>
/// 根据 Tag 的父子关系计算泳道布局。算法保持与 Git 图相同的直觉：
/// 子 Tag 在自己的泳道向下延伸，遇到父 Tag 时若泳道不同则在父行内弯折汇合。
/// </summary>
public static class TagGraphBuilder
{
    /// <summary>按构建时间倒序构建历史图。</summary>
    public static TagGraph Build(IEnumerable<QuireTag> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        List<QuireTag> ordered = tags
            .OrderByDescending(tag => tag.BuildTimeUtc)
            .ThenByDescending(tag => tag.TagId, StringComparer.Ordinal)
            .ToList();

        if (ordered.Count == 0)
        {
            return TagGraph.Empty;
        }

        int count = ordered.Count;
        var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < count; index++)
        {
            indexById[ordered[index].TagId] = index;
        }

        int[] lanes = AssignLanes(ordered, indexById);

        var incoming = new List<HashSet<int>>(count);
        var outgoing = new List<HashSet<int>>(count);
        var childCount = new int[count];
        for (int index = 0; index < count; index++)
        {
            incoming.Add([]);
            outgoing.Add([]);
        }

        int[] parentOf = new int[count];
        Array.Fill(parentOf, -1);

        for (int index = 0; index < count; index++)
        {
            QuireTag tag = ordered[index];
            if (tag.ParentTagId is null || !indexById.TryGetValue(tag.ParentTagId, out int parentIndex))
            {
                continue;
            }

            parentOf[index] = parentIndex;
            int lane = lanes[index];

            // 子行向下引出连线。
            outgoing[index].Add(lane);

            // 跨越的中间行保持该泳道的直通连线。
            for (int between = index + 1; between < parentIndex; between++)
            {
                incoming[between].Add(lane);
                outgoing[between].Add(lane);
            }

            // 父行接收连线并在需要时弯折到自身泳道。
            incoming[parentIndex].Add(lane);
            childCount[parentIndex]++;
        }

        var rows = new List<TagGraphRow>(count);
        for (int index = 0; index < count; index++)
        {
            rows.Add(new TagGraphRow
            {
                Tag = ordered[index],
                Index = index,
                Lane = lanes[index],
                LinesFromTop = incoming[index].Order().ToArray(),
                LinesToBottom = outgoing[index].Order().ToArray(),
                HasChildren = childCount[index] > 0,
                IsRoot = parentOf[index] < 0,
            });
        }

        return new TagGraph
        {
            Rows = rows,
            LaneCount = lanes.Length == 0 ? 0 : lanes.Max() + 1,
        };
    }

    /// <summary>
    /// 自顶向下分配泳道：先出现的 Tag 占据最小可用泳道，
    /// 并把该泳道预留给它的父 Tag；未被预留的 Tag 另开泳道。
    /// </summary>
    private static int[] AssignLanes(List<QuireTag> ordered, Dictionary<string, int> indexById)
    {
        int count = ordered.Count;
        int[] lanes = new int[count];
        var occupiedBy = new List<int?>();
        var reservedLanes = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int index = 0; index < count; index++)
        {
            QuireTag tag = ordered[index];

            int lane;
            if (reservedLanes.Remove(tag.TagId, out int reserved))
            {
                lane = reserved;
            }
            else
            {
                lane = AllocateLane(occupiedBy);
            }

            lanes[index] = lane;
            while (occupiedBy.Count <= lane)
            {
                occupiedBy.Add(null);
            }

            bool hasParent = tag.ParentTagId is not null && indexById.ContainsKey(tag.ParentTagId);
            occupiedBy[lane] = hasParent ? index : null;

            if (hasParent)
            {
                reservedLanes.TryAdd(tag.ParentTagId!, lane);
            }
        }

        return lanes;
    }

    private static int AllocateLane(List<int?> occupiedBy)
    {
        for (int lane = 0; lane < occupiedBy.Count; lane++)
        {
            if (occupiedBy[lane] is null)
            {
                return lane;
            }
        }

        occupiedBy.Add(null);
        return occupiedBy.Count - 1;
    }
}
