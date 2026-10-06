// Reference template: the wire contract, front-end half. See curiosity-code-endpoints §4.
//
// EVERY CLASS HERE IS DECLARED A SECOND TIME, IDENTICALLY, inside TD in the endpoints'
// shared library (code/endpoints/app/_lib/helpers.cs), and the endpoints return THOSE types
// rather than anonymous objects. Both ends serialise with the same default resolver, so the
// two declarations literally are the wire format — a field renamed on one side stops
// compiling instead of deserialising to null. Add a field to one, add it to the other.
//
// TRANSPOSE CONSTRAINT: there is no runtime reflection, so every DTO crossing the boundary is
// FLAT — no inheritance, no polymorphism, no nested generics beyond arrays. They are ordinary
// classes; the endpoint call deserialises them normally, so [ObjectLiteral] is neither needed
// nor wanted.
//
// Everything identifying a node is a UID128 — never a title, never a business identifier. In
// the front end a UID128 IS a JS string at run time and server-side it is a struct whose
// converter reads and writes the same plain string, so the wire bytes are identical to a
// string either way and you get a compile error when a UID reaches the wrong parameter.

using UID;

namespace App.FrontEnd.Api
{
    // --- requests ---------------------------------------------------------------------

    internal class UserRequest { public UID128 UserUID { get; set; } }

    internal class CaseRequest { public UID128 CaseUID { get; set; } }

    internal class NodeRequest { public UID128 Uid     { get; set; } }

    internal class DecisionRequest
    {
        public UID128   CaseUID    { get; set; }
        public UID128[] TargetUIDs { get; set; }
        public string   Decision   { get; set; }
    }

    // --- responses --------------------------------------------------------------------

    internal class OkDto { public bool Ok { get; set; } public string Error { get; set; } }

    /// <summary>
    /// Each count ships the UIDs it counted, so a metric tile and the list it opens are
    /// provably the same set rather than two queries that agree today.
    /// </summary>
    internal class SummaryDto
    {
        public int      InboxCount      { get; set; }
        public UID128[] InboxUIDs       { get; set; }
        public int      UrgentCount     { get; set; }
        public UID128[] UrgentUIDs      { get; set; }
        public int      OwnedCount      { get; set; }
        public int      UnassignedCount { get; set; }
        public UID128[] UnassignedUIDs  { get; set; }
    }

    internal class PerformanceDto
    {
        public WeekPointDto[] PerWeek         { get; set; }
        public double         AvgHoursPerCase { get; set; }
        public int            MonthTotal      { get; set; }
        public double         ValidatedPct    { get; set; }
        public double         RejectedPct     { get; set; }
        public double         DelegatedPct    { get; set; }
    }

    internal class WeekPointDto { public string Week { get; set; } public int Count { get; set; } }

    internal class DecisionsDto
    {
        public UID128[] Confirmed { get; set; }
        public UID128[] Rejected  { get; set; }
    }

    /// <summary>
    /// Each dimension twice: UIDs for rendering (so a speciality opens its own page through
    /// NeighborsLinks) and text for the client-side search box. Nobody types a UID.
    /// </summary>
    internal class UserDto
    {
        public UID128   Uid              { get; set; }
        public string   UserName         { get; set; }
        public string   DisplayName      { get; set; }
        public UID128[] SpecialityUIDs   { get; set; }
        public string[] SpecialityLabels { get; set; }
        public UID128   SiteUID          { get; set; }
        public string   SiteLabel        { get; set; }
    }

    internal class UsersDto { public UserDto[] Users { get; set; } }
}
