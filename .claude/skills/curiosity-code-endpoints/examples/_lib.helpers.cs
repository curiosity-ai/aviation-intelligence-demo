// Reference template — file path in the definitions folder: code/endpoints/app/_lib/helpers.cs
//
// The shared library every other endpoint imports. AdminOnly so it FAILS CLOSED: it is a
// library, not an API, and nothing should be able to call it.
//
// Every class here is declared a SECOND TIME, identically, in the front end's Api/Dto.cs.
// Both ends serialise with the same default resolver, so the two declarations literally are
// the wire format — and a field renamed on one side stops compiling instead of deserialising
// to null. Add a field to one, add it to the other.

[endpoint: Curiosity.Endpoints.Path("app/_lib/helpers")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AdminOnly")]

public static class TD
{
    // ---------------------------------------------------------------------------------------
    // shared constants
    //
    // Written against the injected N, never as string literals: a literal that no longer
    // matches the schema returns nothing, forever, in silence.
    // ---------------------------------------------------------------------------------------

    /// <summary>The node types a decision can be recorded against.</summary>
    public static readonly string[] ImpactableTypes = new[] { N.Case.Type, N.Manual.Type };

    public const string DECISION_CONFIRMED = "CONFIRMED";
    public const string DECISION_REJECTED  = "REJECTED";
    public const string DECISION_NONE      = "NONE";

    /// <summary>A case is "in the inbox" while it is assigned and not yet finished.</summary>
    public const string STATUS_VALIDATED = "Validated";

    // ---------------------------------------------------------------------------------------
    // requests — everything identifying a node is a UID128, never a business identifier
    // ---------------------------------------------------------------------------------------

    public class UserRequest { public UID128 UserUID { get; set; } }

    public class CaseRequest { public UID128 CaseUID { get; set; } }

    public class NodeRequest { public UID128 Uid     { get; set; } }

    public class DecisionRequest
    {
        public UID128   CaseUID    { get; set; }
        public UID128[] TargetUIDs { get; set; }
        public string   Decision   { get; set; }
    }

    // ---------------------------------------------------------------------------------------
    // responses — UIDs, so the front end renders each row with that type's own renderer and
    // no title is ever copied through the wire to go stale
    // ---------------------------------------------------------------------------------------

    public class OkDto { public bool Ok { get; set; } public string Error { get; set; } }

    /// <summary>
    /// Every count ships the UIDs it counted. A count whose list is recomputed by a different
    /// query somewhere else is a count that will eventually lie — this is what makes a metric
    /// tile and the list it opens provably the same set.
    /// </summary>
    public class SummaryDto
    {
        public int      InboxCount      { get; set; }
        public UID128[] InboxUIDs       { get; set; }
        public int      UrgentCount     { get; set; }
        public UID128[] UrgentUIDs      { get; set; }
        public int      OwnedCount      { get; set; }
        public int      UnassignedCount { get; set; }
        public UID128[] UnassignedUIDs  { get; set; }
    }

    public class PerformanceDto
    {
        public WeekPointDto[] PerWeek        { get; set; }
        public double         AvgHoursPerCase{ get; set; }
        public int            MonthTotal     { get; set; }
        public double         ValidatedPct   { get; set; }
        public double         RejectedPct    { get; set; }
        public double         DelegatedPct   { get; set; }
    }

    public class WeekPointDto { public string Week { get; set; } public int Count { get; set; } }

    /// <summary>
    /// BOTH halves of what has been decided, in ONE call. A row badged from a half-loaded
    /// answer says "not reviewed" about something settled last week — and if only the
    /// confirmations were modelled, a dismissed candidate would be indistinguishable from one
    /// nobody has looked at, which makes a long review impossible to pause and resume.
    /// </summary>
    public class DecisionsDto
    {
        public UID128[] Confirmed { get; set; }
        public UID128[] Rejected  { get; set; }
    }

    /// <summary>
    /// Each dimension TWICE: as UIDs, which is what renders (so a speciality opens its own
    /// page), and as text, which is what the client-side search box matches. Nobody types a UID.
    /// </summary>
    public class UserDto
    {
        public UID128   Uid              { get; set; }
        public string   UserName         { get; set; }
        public string   DisplayName      { get; set; }
        public UID128[] SpecialityUIDs   { get; set; }
        public string[] SpecialityLabels { get; set; }
        public UID128   SiteUID          { get; set; }
        public string   SiteLabel        { get; set; }
    }

    public class UsersDto { public UserDto[] Users { get; set; } }

    // ---------------------------------------------------------------------------------------
    // shared reads — one definition of "the inbox", used by the endpoints AND by the
    // assistant's workload tool, so the dashboard and the assistant cannot count differently
    // ---------------------------------------------------------------------------------------

    public static UID128[] OwnedBy(dynamic q, UID128 userUID)
        => q.StartAt(userUID).Out(N.Case.Type, E.HasAssignment).AsUIDEnumerable().ToArray();
}
