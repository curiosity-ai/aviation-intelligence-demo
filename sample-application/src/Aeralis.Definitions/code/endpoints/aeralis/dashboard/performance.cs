[endpoint: Curiosity.Endpoints.Path("aeralis/dashboard/performance")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("aeralis/_lib/aeralis-helpers")

// "My performance": weekly throughput over twelve weeks, mean hours per work order
// last month, and the outcome split for that month. All derived from CompletedDate,
// HoursSpent and Outcome on the closed work orders this engineer owns — reached through
// the assignment edge rather than by matching a user name stored on the order.

var req = Body.FromJson<TD.UserRequest>();
var userUID = req?.UserUID ?? default(UID128);
if (userUID == default(UID128)) return BadRequest("userUID is required");
if (!Graph.TryGet(userUID, out var user) || user.Type != N._User.Type)
    return NotFound($"User '{userUID}' not found.");

var mine = Q().StartAt(userUID)
    .Out(N.PreAnalysisWorkOrder.Type, E.HasAssignment)
    .AsEnumerable()
    .Where(p => p.GetTime(N.PreAnalysisWorkOrder.CompletedDate) > Time.Zero)
    .Select(p => new
    {
        Completed = p.GetTime(N.PreAnalysisWorkOrder.CompletedDate),
        Hours = p.GetDouble(N.PreAnalysisWorkOrder.HoursSpent),
        Outcome = p.GetString(N.PreAnalysisWorkOrder.Outcome) ?? "",
    })
    .ToList();

// Twelve buckets, most recent last, so the chart reads left to right.
var throughput = new List<TD.WeeklyThroughputDto>();
for (int week = 11; week >= 0; week--)
{
    var start = TD.Today.Add(TimeSpan.FromDays(-7 * (week + 1)));
    var end = TD.Today.Add(TimeSpan.FromDays(-7 * week));
    throughput.Add(new TD.WeeklyThroughputDto
    {
        WeekStarting = start.ToString("yyyy-MM-dd"),
        Completed = mine.Count(m => m.Completed >= start && m.Completed < end),
    });
}

var lastMonth = mine.Where(m => (TD.Today.Subtract(m.Completed)).TotalDays <= 30).ToList();
var total = lastMonth.Count;
double Pct(string outcome) => total == 0 ? 0 : (100.0 * lastMonth.Count(m => m.Outcome == outcome)) / total;

return new TD.PerformanceDto
{
    Throughput = throughput.ToArray(),
    AverageHoursPerPawo = total == 0 ? 0 : lastMonth.Average(m => m.Hours),
    LastMonthTotal = total,
    ValidatedPercent = Pct("VALIDATED"),
    RejectedPercent = Pct("REJECTED"),
    DelegatedPercent = Pct("DELEGATED"),
};

