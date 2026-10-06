[endpoint: Curiosity.Endpoints.Path("aeralis/dashboard/users")]
[endpoint: Curiosity.Endpoints.ReadOnly]
[endpoint: Curiosity.Endpoints.AccessMode("AllUsers")]

//ImportEndpoint("aeralis/_lib/aeralis-helpers")

// The engineers the dashboard's perspective dropdown offers. The demo has no real
// login, so the UI renders "as" whoever is chosen here.
//
// Site, department, role and ATA specialities used to come back as empty strings: `_User`
// is a node type the graph owns, so the connector had nowhere to put them. They are edges
// now, and what travels is the UID of the node on the other end — the front-end renders
// those through the workspace's own labels rather than re-implementing them.

UID128[] Neighbours(UID128 uid, string nodeType, string edgeType) =>
    Q().StartAt(uid).Out(nodeType, edgeType).AsUIDEnumerable().ToArray();

// The same neighbours as plain text, for the picker's search box to match on. The UIDs stay
// the thing that gets *rendered* — they carry the workspace's own labels and open the node —
// but a UID is not something anybody types, and a picker you cannot type into is a list.
string Labels(UID128[] uids, string field)
{
    var names = new List<string>();

    foreach (var uid in uids)
    {
        if (Graph.TryGet(uid, out var node))
        {
            var value = node.GetString(field);
            if (!string.IsNullOrEmpty(value)) names.Add(value);
        }
    }

    return string.Join(", ", names);
}

var users = Q().StartAt(N._User.Type).AsEnumerable()
    .Select(u =>
    {
        var ata = Neighbours(u.UID, N.AtaChapter.Type, E.SpecialisesIn);
        var sites = Neighbours(u.UID, N.Site.Type, E.WorksAtSite);
        var departments = Neighbours(u.UID, N.Department.Type, E.InDepartment);
        var roles = Neighbours(u.UID, N.Classification.Type, E.HasUserRole);

        return new TD.UserDto
        {
            Uid = u.UID,
            UserName = u.GetString(N._User.UserName) ?? u.UID.ToString(),
            DisplayName = u.GetString(N._User.FullName)
                          ?? ((u.GetString(N._User.FirstName) + " " + u.GetString(N._User.LastName)) ?? "").Trim(),
            AtaSpecialityUIDs = ata,
            SiteUIDs = sites,
            DepartmentUIDs = departments,
            RoleUIDs = roles,

            AtaSpecialityText = Labels(ata, N.AtaChapter.Code) + " " + Labels(ata, N.AtaChapter.Title),
            SiteText = Labels(sites, N.Site.Name),
            DepartmentText = Labels(departments, N.Department.Code),
            RoleText = Labels(roles, N.Classification.Value),
        };
    })
    .OrderBy(u => u.DisplayName)
    .ToArray();

return new TD.UsersDto { Users = users };

