using Aeralis.Dataset;
using Aeralis.Dataset.Documents;

namespace Aeralis.Attachments.Csdb;

/// <summary>
/// The S1000D data module code for a dataset document, and the pieces it is assembled from.
/// </summary>
/// <remarks>
/// ATA and the data module code line up almost exactly, which is why the projection is
/// mechanical rather than a lookup: an ATA 6D such as <c>53-21-13</c> is a system code of
/// <c>53</c>, a subsystem of <c>2</c>, a sub-subsystem of <c>1</c> and an assembly of
/// <c>13</c>. The information code is the one part ATA does not give, and comes from the
/// kind of document — except for an AMM task, whose own task number carries it
/// (<c>21-20-13-<b>100</b>-052-A</c> is a cleaning task).
/// </remarks>
public readonly record struct DataModuleCode(
    string ModelIdentCode,
    string SystemDiffCode,
    string SystemCode,
    string SubSystemCode,
    string SubSubSystemCode,
    string AssyCode,
    string DisassyCode,
    string DisassyCodeVariant,
    string InfoCode,
    string InfoCodeVariant,
    string ItemLocationCode)
{
    /// <summary>The printed form, as it appears in a footer: <c>DMC-MRD-A-53-2-1-13-00A-040A-A</c>.</summary>
    public override string ToString() =>
        $"DMC-{ModelIdentCode}-{SystemDiffCode}-{SystemCode}-{SubSystemCode}-{SubSubSystemCode}"
        + $"-{AssyCode}-{DisassyCode}{DisassyCodeVariant}-{InfoCode}{InfoCodeVariant}-{ItemLocationCode}";

    /// <summary>Builds the code for a document.</summary>
    public static DataModuleCode For(TechDocument document, string modelIdentCode, object? payload)
    {
        var (system, sub, subSub, assy) = SplitAta(document.Ata);
        var (info, variant) = InfoCodeFor(document, payload);

        return new DataModuleCode(
            ModelIdentCode: modelIdentCode,
            SystemDiffCode: "A",
            SystemCode: system,
            SubSystemCode: sub,
            SubSubSystemCode: subSub,
            AssyCode: assy,
            DisassyCode: "00",
            DisassyCodeVariant: "A",
            InfoCode: info,
            InfoCodeVariant: variant,
            // "A" is the whole item; the dataset does not distinguish left from right at
            // document level, so every module is coded against the complete assembly.
            ItemLocationCode: "A");
    }

    /// <summary><c>53-21-13</c> → system 53, subsystem 2, sub-subsystem 1, assembly 13.</summary>
    private static (string System, string Sub, string SubSub, string Assy) SplitAta(AtaRef? ata)
    {
        var six = (ata?.Ata6d ?? "00-00-00").Replace("-", "");
        six = six.PadRight(6, '0');
        return (six[..2], six.Substring(2, 1), six.Substring(3, 1), six.Substring(4, 2));
    }

    /// <summary>
    /// The information code, which says what kind of information the module holds. An AMM
    /// task states it in its own number; everything else takes the code S1000D reserves for
    /// its kind of content.
    /// </summary>
    private static (string Code, string Variant) InfoCodeFor(TechDocument document, object? payload)
    {
        if (payload is AmmTaskData amm && TaskFunctionCode(amm.TaskNumber) is { } fromTask)
        {
            return (fromTask, "A");
        }

        return document.DocType switch
        {
            DocType.MOD => ("040", "A"),   // description of the change
            DocType.SB => ("700", "A"),    // service bulletin
            DocType.IO => ("018", "A"),    // request / general information
            DocType.PAWO => ("019", "A"),  // technical assessment
            DocType.AMM => ("200", "A"),   // servicing, when the task number gave nothing
            DocType.IPC => ("941", "A"),   // illustrated parts data
            DocType.SRM => ("664", "A"),   // repair
            DocType.TSM => ("420", "A"),   // fault isolation
            DocType.WDM => ("057", "A"),   // wiring data
            DocType.FCOM => ("130", "A"),  // crew operating information
            _ => ("040", "A"),
        };
    }

    /// <summary>The function code out of an Aeralis task number, <c>21-20-13-100-052-A</c> → <c>100</c>.</summary>
    private static string? TaskFunctionCode(string taskNumber)
    {
        var parts = taskNumber.Split('-');
        return parts.Length >= 4 && parts[3].Length == 3 && parts[3].All(char.IsAsciiDigit)
            ? parts[3]
            : null;
    }

    /// <summary>
    /// The model identification code for a program. S1000D wants a short alphanumeric code for
    /// the product; the aircraft family reads best and is what a real Aeralis CSDB uses.
    /// </summary>
    public static string ModelIdentFor(ProgramLetter program) => program switch
    {
        ProgramLetter.M => "MRD",
        ProgramLetter.C => "CRN",
        ProgramLetter.T => "TTN",
        ProgramLetter.Z => "ZPH",
        ProgramLetter.S => "SLN",
        _ => "XXXX",
    };
}
