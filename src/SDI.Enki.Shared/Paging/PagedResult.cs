namespace SDI.Enki.Shared.Paging;

/// <summary>
/// Wire envelope for a paginated list endpoint. Wraps the page slice
/// (<see cref="Items"/>) with the parameters used to slice it
/// (<see cref="Skip"/>, <see cref="Take"/>) and the total row count
/// before paging (<see cref="Total"/>) so the client can render
/// pagination controls without a second roundtrip.
///
/// <para>
/// Lives in <c>SDI.Enki.Shared.Paging</c> so every admin grid
/// (users, tenants, settings history, future audit) reuses the same
/// shape — a Syncfusion <c>SfGrid</c> wired to one envelope works
/// across all of them.
/// </para>
///
/// <para>
/// <see cref="HasMore"/> is a derived convenience: <c>true</c> when
/// the next page exists. Trivial to compute client-side; surfaced
/// here so the wire shape doesn't force every consumer to redo the
/// arithmetic.
/// </para>
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int              Total,
    int              Skip,
    int              Take)
{
    public bool HasMore => Skip + Items.Count < Total;
}
