using Mosaik.Components;
using Mosaik.Schema;
using Tesserae;
using Aeralis.FrontEnd.Schema;

namespace Aeralis.FrontEnd.Renderers
{
    // One renderer per node type. Each states only what is different about its type —
    // everything else (the card, the modal, Overview, Graph, 360, relationship tabs) comes
    // from AeralisRendererBase, so a change to the shared shape lands on every type at
    // once.
    //
    // Nothing registers these. App.AutoDiscoverViews reflects over the loaded assemblies
    // for non-abstract INodeRenderer implementations with a parameter-less constructor,
    // so adding a type here is the whole job — which is also why every
    // constructor below must stay parameter-less and public.
    //
    // Every renderer sets an Icon. Without one NodeRendererBase falls back to a colour
    // derived from the type name, which distinguishes types from each other but says
    // nothing about what they are — and these are read side by side in a mixed result
    // list, where "wrench" vs "aeroplane tail" is the difference between scanning and
    // reading. Names come from the Tesserae `UIcons` enum, so an icon that does not
    // exist is a build error rather than a blank square.
    //
    // **DisplayName is the abbreviation, upper-case, wherever the discipline has one.**
    // These are the names on the documents themselves and in every conversation about
    // them: an engineer asks for "the MOD" and "the SB", never for "the Modification"
    // and "the Service Bulletin". The type name is read hundreds of times a session — in
    // facet lists, in result rows, on graph nodes — and at that density the short form is
    // what makes a mixed list scannable. Types with no established abbreviation (Drawing,
    // Zone, Operator) keep their words; nothing is contracted just to be short.
    //
    // **ShortDisplayName is what the 34px card tile says**, and only the types whose
    // DisplayName does not fit one set it — the tile clips, so "Aircraft Model" arrives as
    // "Aircraft M". Five characters, four preferred, or two words split on '\n' under the
    // same limit each. The facet list, the modal header and the graph keep the real name:
    // this is the tile alone. See `AeralisRendererBase.ShortDisplayName`.
    //
    // **LabelField is a list of node field names, not a caption.** `Labels.Get` splits it
    // on `|` and takes the first field that has a value; when none of them do — including
    // when the field simply does not exist on the type — it falls back to
    // `"<uid> [Display Name]"`, silently. So every field named below is written as
    // `N.<Type>.<Field>`: a renamed property now fails the build instead of turning every
    // label of that type into a raw UID. Types whose first choice can legitimately be
    // empty name a fallback after it; every type that has both an identifier and a title
    // shows *both*, through `LabelFields.IdAndTitle`.
    //
    // The second and third constructor arguments are what the type's card says: the one
    // property drawn as its body, and the properties drawn in its footer. See
    // `AeralisRendererBase.CreateCompactViewFor` and `AeralisRendererBase.WithFooter`.
    // The body is the abstract on every document type and nothing on the reference types,
    // which have no summary to give. The footer is the handful of fields that place the
    // node without opening it — where it stands, what it is about, and which fleet it
    // belongs to.

    public class ModificationRenderer : AeralisRendererBase
    {
        public ModificationRenderer() : base(new SchemaStyleInfo
        {
            Name = N.Modification.Type, DisplayName = "MOD",
            Icon = UIcons.PencilRuler.ToString(),
            LabelField = LabelFields.IdAndTitle(N.Modification.DocId, N.Modification.Title),
        },
        N.Modification.Abstract,
        N.Modification.Status, N.Modification.EmbodimentStatus, N.Modification.ModClass,
        N.Modification.Ata6d, N.Modification.ProgramLetter) { }
    }

    public class ServiceBulletinRenderer : AeralisRendererBase
    {
        public ServiceBulletinRenderer() : base(new SchemaStyleInfo
        {
            Name = N.ServiceBulletin.Type, DisplayName = "SB",
            Icon = UIcons.Megaphone.ToString(),
            LabelField = LabelFields.IdAndTitle(N.ServiceBulletin.DocId, N.ServiceBulletin.Title),
        },
        N.ServiceBulletin.Abstract,
        N.ServiceBulletin.Status, N.ServiceBulletin.Compliance, N.ServiceBulletin.SbType,
        N.ServiceBulletin.Ata6d, N.ServiceBulletin.ProgramLetter) { }
    }

    public class InternalOrderRenderer : AeralisRendererBase
    {
        public InternalOrderRenderer() : base(new SchemaStyleInfo
        {
            Name = N.InternalOrder.Type, DisplayName = "IO",
            Icon = UIcons.Inbox.ToString(),
            LabelField = LabelFields.IdAndTitle(N.InternalOrder.DocId, N.InternalOrder.Title),
        },
        N.InternalOrder.Abstract,
        N.InternalOrder.IoStatus, N.InternalOrder.Priority, N.InternalOrder.RequestCategory,
        N.InternalOrder.Ata6d, N.InternalOrder.ProgramLetter) { }
    }

    public class AmmTaskRenderer : AeralisRendererBase
    {
        public AmmTaskRenderer() : base(new SchemaStyleInfo
        {
            Name = N.AmmTask.Type, DisplayName = "AMM",
            Icon = UIcons.ToolBox.ToString(),
            LabelField = LabelFields.IdAndTitle(N.AmmTask.DocId, N.AmmTask.Title),
        },
        N.AmmTask.Abstract,
        N.AmmTask.Status, N.AmmTask.TaskType, N.AmmTask.Ata6d, N.AmmTask.ProgramLetter) { }
    }

    public class IpcFigureRenderer : AeralisRendererBase
    {
        public IpcFigureRenderer() : base(new SchemaStyleInfo
        {
            Name = N.IpcFigure.Type, DisplayName = "IPC",
            Icon = UIcons.Boxes.ToString(),
            LabelField = LabelFields.IdAndTitle(N.IpcFigure.DocId, N.IpcFigure.Title),
        },
        N.IpcFigure.Abstract,
        // The next higher assembly rather than `Figure`: the figure number is already in
        // the identifier the label leads with, and the assembly is what an engineer is
        // actually placing the figure against.
        N.IpcFigure.Status, N.IpcFigure.NextHigherAssembly, N.IpcFigure.Ata6d,
        N.IpcFigure.ProgramLetter) { }
    }

    public class SrmRepairRenderer : AeralisRendererBase
    {
        public SrmRepairRenderer() : base(new SchemaStyleInfo
        {
            Name = N.SrmRepair.Type, DisplayName = "SRM",
            Icon = UIcons.Patch.ToString(),
            LabelField = LabelFields.IdAndTitle(N.SrmRepair.DocId, N.SrmRepair.Title),
        },
        N.SrmRepair.Abstract,
        N.SrmRepair.Status, N.SrmRepair.RepairCategory, N.SrmRepair.DamageType,
        N.SrmRepair.Ata6d, N.SrmRepair.ProgramLetter) { }
    }

    public class TsmProcedureRenderer : AeralisRendererBase
    {
        public TsmProcedureRenderer() : base(new SchemaStyleInfo
        {
            Name = N.TsmProcedure.Type, DisplayName = "TSM",
            Icon = UIcons.Bug.ToString(),
            LabelField = LabelFields.IdAndTitle(N.TsmProcedure.DocId, N.TsmProcedure.Title),
        },
        N.TsmProcedure.Abstract,
        // No fourth field: `MelItem` is empty on every procedure in the dataset, and
        // `FaultSymptom` — the only other candidate — is a sentence, not a glance.
        N.TsmProcedure.Status, N.TsmProcedure.Ata6d, N.TsmProcedure.ProgramLetter) { }
    }

    public class WdmSheetRenderer : AeralisRendererBase
    {
        public WdmSheetRenderer() : base(new SchemaStyleInfo
        {
            Name = N.WdmSheet.Type, DisplayName = "WDM",
            Icon = UIcons.Plug.ToString(),
            LabelField = LabelFields.IdAndTitle(N.WdmSheet.DocId, N.WdmSheet.Title),
        },
        N.WdmSheet.Abstract,
        N.WdmSheet.Status, N.WdmSheet.DiagramType, N.WdmSheet.Ata6d, N.WdmSheet.ProgramLetter) { }
    }

    public class FcomEntryRenderer : AeralisRendererBase
    {
        public FcomEntryRenderer() : base(new SchemaStyleInfo
        {
            Name = N.FcomEntry.Type, DisplayName = "FCOM",
            Icon = UIcons.PlaneTail.ToString(),
            LabelField = LabelFields.IdAndTitle(N.FcomEntry.DocId, N.FcomEntry.Title),
        },
        N.FcomEntry.Abstract,
        N.FcomEntry.Status, N.FcomEntry.Section, N.FcomEntry.FlightPhase,
        N.FcomEntry.Ata6d, N.FcomEntry.ProgramLetter) { }
    }

    // --- reference types: the 360 view narrows to what actually touches them ---------

    public class PartNumberRenderer : AeralisRendererBase
    {
        public PartNumberRenderer() : base(new SchemaStyleInfo
        {
            Name = N.PartNumber.Type, DisplayName = "PN",
            Icon = UIcons.Screw.ToString(),
            // The part number itself, not its description: it is what an engineer reads
            // and what every other document refers to it by.
            LabelField = N.PartNumber.Pn,
        },
        null,
        N.PartNumber.PnType, N.PartNumber.Lifecycle, N.PartNumber.Ata6d)
        { }

        protected override string View360Label => "Where Used";
    }

    public class FinRenderer : AeralisRendererBase
    {
        public FinRenderer() : base(new SchemaStyleInfo
        {
            Name = N.Fin.Type, DisplayName = "FIN",
            Icon = UIcons.Barcode.ToString(), LabelField = N.Fin.FinId,
        },
        null,
        N.Fin.SystemName, N.Fin.Ata6d)
        { }

        protected override string View360Label => "Where Used";
    }

    public class DrawingRenderer : AeralisRendererBase
    {
        public DrawingRenderer() : base(new SchemaStyleInfo
        {
            Name = N.Drawing.Type, DisplayName = "Drawing",
            Icon = UIcons.Blueprint.ToString(),
            LabelField = LabelFields.IdAndTitle(N.Drawing.DrawingNumber, N.Drawing.Title),
        },
        null,
        N.Drawing.DrawingType, N.Drawing.Issue, N.Drawing.Ata6d)
        {
            ShortDisplayName = "DWG";
        }

        protected override string View360Label => "Where Used";
    }

    public class SoftwareLoadRenderer : AeralisRendererBase
    {
        public SoftwareLoadRenderer() : base(new SchemaStyleInfo
        {
            // Not "LSAP": only 174 of the 301 loads are one — the rest are DATABASE,
            // EMBEDDED or FIELD_LOADABLE, and `LoadType` in the footer says which.
            Name = N.SoftwareLoad.Type, DisplayName = "Software Load",
            Icon = UIcons.Microchip.ToString(),
            LabelField = N.SoftwareLoad.SoftwareName + "|" + N.SoftwareLoad.SoftwarePn,
        },
        null,
        N.SoftwareLoad.LoadType, N.SoftwareLoad.SoftwareVersion,
        N.SoftwareLoad.CertificationStatus, N.SoftwareLoad.Ata6d)
        {
            ShortDisplayName = "SW\nLOAD";
        }

        protected override string View360Label => "Where Used";
    }

    public class ZoneRenderer : AeralisRendererBase
    {
        public ZoneRenderer() : base(new SchemaStyleInfo
        {
            Name = N.Zone.Type, DisplayName = "Zone",
            Icon = UIcons.MapPin.ToString(),
            LabelField = N.Zone.ZoneName + "|" + N.Zone.ZoneId,
        },
        null,
        N.Zone.MajorZoneName)
        { }

        protected override string View360Label => "In This Zone";
    }

    public class MsnRenderer : AeralisRendererBase
    {
        public MsnRenderer() : base(new SchemaStyleInfo
        {
            Name = N.Msn.Type, DisplayName = "MSN",
            Icon = UIcons.Plane.ToString(),
            LabelField = N.Msn.MsnId + "|" + N.Msn.Registration,
        },
        null,
        N.Msn.Status, N.Msn.AircraftType, N.Msn.Model, N.Msn.EngineType, N.Msn.ProgramLetter)
        { }

        protected override string View360Label => "Applicable To This Aircraft";
    }

    public class AtaChapterRenderer : AeralisRendererBase
    {
        public AtaChapterRenderer() : base(new SchemaStyleInfo
        {
            // "ATA", not "ATA2D": one node type holds the 2D, 4D and 6D levels and
            // `Level` says which — naming the type after one of them would mislabel the
            // other two. The card tile says the level per node; see ShortDisplayNameFor.
            Name = N.AtaChapter.Type, DisplayName = "ATA",
            Icon = UIcons.Book.ToString(),
            // Interpolated so a chapter reads as "53-10-00 — Fuselage Structure" wherever
            // it is linked. The code alone is what documents cite; the title alone leaves
            // twelve chapters called "General" indistinguishable.
            LabelField = "$ATA {Code} — {Title}",
        },
        null,
        N.AtaChapter.Domain)
        { }

        /// <summary>
        /// The tile says which level this one is — <c>ATA 2D</c>, <c>ATA 4D</c>, <c>ATA 6D</c>.
        /// The type name cannot: all three levels are this one node type, so a tile reading
        /// "ATA" puts a chapter, a section and a subject in a result list looking identical,
        /// and the level is the first thing that places any of them. Anything else the
        /// catalogue grows falls back to the plain mark.
        /// </summary>
        protected override string ShortDisplayNameFor(Node node)
        {
            var level = node.GetString(N.AtaChapter.Level);

            if (level == "2" || level == "4" || level == "6") return "ATA\n" + level + "D";

            return ShortDisplayName;
        }

        protected override string View360Label => "In This Chapter";
    }

    public class RuleRenderer : AeralisRendererBase
    {
        public RuleRenderer() : base(new SchemaStyleInfo
        {
            Name = N.Rule.Type, DisplayName = "Engineering Rule",
            Icon = UIcons.Scroll.ToString(),
            LabelField = LabelFields.IdAndTitle(N.Rule.RuleId, N.Rule.Title),
        },
        null,
        N.Rule.Category, N.Rule.Severity, N.Rule.Ata2d)
        {
            ShortDisplayName = "RULE";
        }
    }

    public class ExpectedFindingRenderer : AeralisRendererBase
    {
        public ExpectedFindingRenderer() : base(new SchemaStyleInfo
        {
            Name = N.ExpectedFinding.Type, DisplayName = "Expected Finding",
            Icon = UIcons.Search.ToString(),
            // Was `Rationale` — a whole paragraph, which is unreadable as a link label.
            // The finding's identity is its severity and what it points at.
            LabelField = "$F{FindingId} {Severity} → {TargetDocId}",
        },
        null,
        N.ExpectedFinding.FindingType, N.ExpectedFinding.Severity,
        N.ExpectedFinding.Discovery, N.ExpectedFinding.ProgramLetter)
        {
            ShortDisplayName = "EXP\nFIND";
        }
    }

    public class IpcItemRenderer : AeralisRendererBase
    {
        public IpcItemRenderer() : base(new SchemaStyleInfo
        {
            Name = N.IpcItem.Type, DisplayName = "IPC Item",
            Icon = UIcons.Cube.ToString(),
            // Nomenclature is optional in the source data, so it cannot stand alone.
            LabelField = N.IpcItem.Nomenclature + "|" + N.IpcItem.PartNumber + "|" + N.IpcItem.ItemId,
        },
        null,
        N.IpcItem.PartNumber, N.IpcItem.Vendor, N.IpcItem.ProgramLetter)
        {
            ShortDisplayName = "IPC\nITEM";
        }
    }

    public class RepairDrawingRenderer : AeralisRendererBase
    {
        public RepairDrawingRenderer() : base(new SchemaStyleInfo
        {
            Name = N.RepairDrawing.Type, DisplayName = "Repair Drawing",
            Icon = UIcons.RulerTriangle.ToString(),
            LabelField = LabelFields.IdAndTitle(N.RepairDrawing.Reference, N.RepairDrawing.Title),
        },
        null,
        N.RepairDrawing.ProgramLetter)
        {
            ShortDisplayName = "RPR\nDWG";
        }
    }

    // --- applicability and metadata -------------------------------------------------
    //
    // These had no renderer at all, which meant no LabelField either — and they are
    // exactly the types the work-order summary links to. Without a style, `Labels.Get`
    // has no field to read and every one of them rendered as a raw UID.

    public class ProgramRenderer : AeralisRendererBase
    {
        public ProgramRenderer() : base(new SchemaStyleInfo
        {
            Name = N.Program.Type, DisplayName = "Programme",
            Icon = UIcons.Rocket.ToString(),
            LabelField = N.Program.Name + "|" + N.Program.Letter,
        },
        null,
        N.Program.Letter)
        {
            ShortDisplayName = "PROG";
        }

        protected override string View360Label => "In This Programme";
    }

    public class AircraftTypeRenderer : AeralisRendererBase
    {
        public AircraftTypeRenderer() : base(new SchemaStyleInfo
        {
            Name = N.AircraftType.Type, DisplayName = "Aircraft Type",
            Icon = UIcons.PlaneAlt.ToString(),
            LabelField = N.AircraftType.Name,
        },
        null,
        N.AircraftType.ProgramLetter)
        {
            ShortDisplayName = "A/C\nTYPE";
        }

        protected override string View360Label => "Applies To This Type";
    }

    public class AircraftModelRenderer : AeralisRendererBase
    {
        public AircraftModelRenderer() : base(new SchemaStyleInfo
        {
            Name = N.AircraftModel.Type, DisplayName = "Aircraft Model",
            Icon = UIcons.PlaneProp.ToString(),
            LabelField = N.AircraftModel.Name,
        },
        null,
        N.AircraftModel.AircraftType, N.AircraftModel.ProgramLetter)
        {
            ShortDisplayName = "A/C\nMODEL";
        }

        protected override string View360Label => "Applies To This Model";
    }

    public class OperatorRenderer : AeralisRendererBase
    {
        public OperatorRenderer() : base(new SchemaStyleInfo
        {
            Name = N.Operator.Type, DisplayName = "Operator",
            Icon = UIcons.Building.ToString(),
            LabelField = N.Operator.OperatorName + "|" + N.Operator.OperatorCode,
        },
        null,
        N.Operator.Iata, N.Operator.Country, N.Operator.Region)
        {
            ShortDisplayName = "OPER";
        }

        protected override string View360Label => "For This Operator";
    }

    public class SupplierRenderer : AeralisRendererBase
    {
        public SupplierRenderer() : base(new SchemaStyleInfo
        {
            Name = N.Supplier.Type, DisplayName = "Supplier",
            Icon = UIcons.Forklift.ToString(),
            LabelField = N.Supplier.SupplierName + "|" + N.Supplier.SupplierCode,
        },
        null,
        N.Supplier.Country, N.Supplier.CageCode)
        {
            ShortDisplayName = "SUPP";
        }

        protected override string View360Label => "Supplied By";
    }

    public class AccessPanelRenderer : AeralisRendererBase
    {
        public AccessPanelRenderer() : base(new SchemaStyleInfo
        {
            Name = N.AccessPanel.Type, DisplayName = "Access Panel",
            Icon = UIcons.Unlock.ToString(),
            LabelField = N.AccessPanel.Panel,
        },
        null,
        N.AccessPanel.ZoneId)
        {
            ShortDisplayName = "PANEL";
        }
    }

    public class AircraftSystemRenderer : AeralisRendererBase
    {
        public AircraftSystemRenderer() : base(new SchemaStyleInfo
        {
            Name = N.AircraftSystem.Type, DisplayName = "System",
            Icon = UIcons.Network.ToString(),
            LabelField = N.AircraftSystem.Name,
        })
        {
            ShortDisplayName = "SYS";
        }

        protected override string View360Label => "In This System";
    }

    public class FaultCodeRenderer : AeralisRendererBase
    {
        public FaultCodeRenderer() : base(new SchemaStyleInfo
        {
            Name = N.FaultCode.Type, DisplayName = "Fault Code",
            Icon = UIcons.Triangle.ToString(),
            LabelField = N.FaultCode.Code,
        })
        {
            ShortDisplayName = "FAULT";
        }

        protected override string View360Label => "Diagnosed By";
    }

    public class MelItemRenderer : AeralisRendererBase
    {
        public MelItemRenderer() : base(new SchemaStyleInfo
        {
            Name = N.MelItem.Type, DisplayName = "MEL Item",
            Icon = UIcons.ListCheck.ToString(),
            LabelField = N.MelItem.ItemName,
        })
        {
            ShortDisplayName = "MEL";
        }
    }

    public class HarnessRenderer : AeralisRendererBase
    {
        public HarnessRenderer() : base(new SchemaStyleInfo
        {
            Name = N.Harness.Type, DisplayName = "Harness",
            Icon = UIcons.Share.ToString(),
            LabelField = N.Harness.Reference,
        })
        {
            ShortDisplayName = "HARN";
        }

        protected override string View360Label => "Routed On";
    }

    public class SiteRenderer : AeralisRendererBase
    {
        public SiteRenderer() : base(new SchemaStyleInfo
        {
            Name = N.Site.Type, DisplayName = "Site",
            Icon = UIcons.Building.ToString(), LabelField = N.Site.Name,
        }) { }
    }

    public class DepartmentRenderer : AeralisRendererBase
    {
        public DepartmentRenderer() : base(new SchemaStyleInfo
        {
            Name = N.Department.Type, DisplayName = "Department",
            Icon = UIcons.Users.ToString(), LabelField = N.Department.Code,
        })
        {
            ShortDisplayName = "DEPT";
        }
    }

    public class ModNumberRenderer : AeralisRendererBase
    {
        public ModNumberRenderer() : base(new SchemaStyleInfo
        {
            Name = N.ModNumber.Type, DisplayName = "MOD Number",
            Icon = UIcons.Hashtag.ToString(),
            LabelField = "$MOD {Number}",
        })
        {
            ShortDisplayName = "MOD\nNO";
        }

        protected override string View360Label => "This Change";
    }

    public class AirworthinessDirectiveRenderer : AeralisRendererBase
    {
        public AirworthinessDirectiveRenderer() : base(new SchemaStyleInfo
        {
            Name = N.AirworthinessDirective.Type, DisplayName = "AD",
            Icon = UIcons.Stamp.ToString(),
            LabelField = N.AirworthinessDirective.Reference,
        }) { }

        protected override string View360Label => "Answered By";
    }

    public class ServiceKitRenderer : AeralisRendererBase
    {
        public ServiceKitRenderer() : base(new SchemaStyleInfo
        {
            Name = N.ServiceKit.Type, DisplayName = "Service Kit",
            Icon = UIcons.Package.ToString(),
            LabelField = N.ServiceKit.KitNumber,
        })
        {
            ShortDisplayName = "KIT";
        }

        protected override string View360Label => "Called For By";
    }

    public class KeywordRenderer : AeralisRendererBase
    {
        public KeywordRenderer() : base(new SchemaStyleInfo
        {
            Name = N.Keyword.Type, DisplayName = "Keyword",
            Icon = UIcons.Bookmark.ToString(),
            LabelField = N.Keyword.Text,
        })
        {
            ShortDisplayName = "KEY\nWORD";
        }

        protected override string View360Label => "Tagged With This";
    }

    public class EngineTypeRenderer : AeralisRendererBase
    {
        public EngineTypeRenderer() : base(new SchemaStyleInfo
        {
            Name = N.EngineType.Type, DisplayName = "Engine Type",
            Icon = UIcons.Engine.ToString(),
            LabelField = N.EngineType.Name,
        })
        {
            ShortDisplayName = "ENG";
        }

        protected override string View360Label => "With This Engine";
    }

    public class AtaDomainRenderer : AeralisRendererBase
    {
        public AtaDomainRenderer() : base(new SchemaStyleInfo
        {
            Name = N.AtaDomain.Type, DisplayName = "ATA Domain",
            Icon = UIcons.Sitemap.ToString(),
            LabelField = N.AtaDomain.Name,
        })
        {
            ShortDisplayName = "ATA\nDOM";
        }

        protected override string View360Label => "Chapters In This Domain";
    }

    public class MajorZoneRenderer : AeralisRendererBase
    {
        public MajorZoneRenderer() : base(new SchemaStyleInfo
        {
            Name = N.MajorZone.Type, DisplayName = "Major Zone",
            Icon = UIcons.Map.ToString(),
            LabelField = N.MajorZone.Name + "|" + N.MajorZone.Code,
        })
        {
            ShortDisplayName = "MAJ\nZONE";
        }

        protected override string View360Label => "In This Area";
    }

    public class SbRevisionRenderer : AeralisRendererBase
    {
        public SbRevisionRenderer() : base(new SchemaStyleInfo
        {
            Name = N.SbRevision.Type, DisplayName = "SB Revision",
            Icon = UIcons.Clock.ToString(),
            LabelField = "$Rev {Revision} — {SbId}",
        },
        null,
        N.SbRevision.Date)
        {
            ShortDisplayName = "SB\nREV";
        }
    }

    public class MsnRangeApplicabilityRenderer : AeralisRendererBase
    {
        public MsnRangeApplicabilityRenderer() : base(new SchemaStyleInfo
        {
            Name = N.MsnRangeApplicability.Type, DisplayName = "MSN Range",
            Icon = UIcons.ListCheck.ToString(),
            LabelField = "$MSN {FromMsn} → {ToMsn}",
        },
        null,
        N.MsnRangeApplicability.ProgramLetter)
        {
            ShortDisplayName = "MSN\nRANGE";
        }
    }

    /// <summary>
    /// Where something stands: one node per status value, shared by every type that reaches
    /// it. The key is the raw enum member (<c>IN_PROGRESS</c>), so the label is the prose the
    /// connector derived from it and the raw value sits in the footer, where an engineer who
    /// knows the vocabulary can still recognise it.
    /// </summary>
    public class StatusRenderer : AeralisRendererBase
    {
        public StatusRenderer() : base(new SchemaStyleInfo
        {
            Name = N.Status.Type, DisplayName = "Status",
            Icon = UIcons.Flag.ToString(),
            LabelField = N.Status.Label + "|" + N.Status.Value,
        },
        null,
        N.Status.Value)
        {
            ShortDisplayName = "STAT";
        }

        protected override string View360Label => "Everything In This Status";
    }

    /// <summary>
    /// The controlled-vocabulary long tail — compliance, risk level, damage type and two
    /// dozen others — all keyed <c>{Kind}:{Value}</c> in one node type. The key alone would
    /// render as that raw composite, so the label is the value with its kind beside it.
    /// Status vocabularies are not here; they are <see cref="StatusRenderer"/>.
    /// </summary>
    public class ClassificationRenderer : AeralisRendererBase
    {
        public ClassificationRenderer() : base(new SchemaStyleInfo
        {
            Name = N.Classification.Type, DisplayName = "Classification",
            Icon = UIcons.Tags.ToString(),
            LabelField = "${Value} ({Kind})",
        },
        null,
        N.Classification.Kind)
        {
            ShortDisplayName = "CLASS";
        }
    }
}
