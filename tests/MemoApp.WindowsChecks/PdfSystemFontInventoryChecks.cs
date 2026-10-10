using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MemoApp.Windows;

internal static partial class Program
{
    // Inventory completion is not approval of a bold face or renderer support.
    private static Task PdfSystemFontInventoryRun()
    {
        Dispatcher.CurrentDispatcher.VerifyAccess();
        string[] families=["Segoe UI","Malgun Gothic","Segoe UI Symbol","Segoe UI Emoji"];
        int installedRecords=0,requests=0,resolved=0,missing=0,candidates=0,unsupported=0;
        foreach(string family in families)
        {
            var installed=new FontFamily(family).GetTypefaces().Take(65).ToArray();
            Require(installed.Length<=64,"Fixed-family native font inventory stays within its 64-face bound");
            foreach(var typeface in installed.OrderBy(face=>face.Weight.ToOpenTypeWeight()).ThenBy(face=>face.Style.ToString(),StringComparer.Ordinal).ThenBy(face=>face.Stretch.ToOpenTypeStretch()))
            {
                installedRecords++;
                if(typeface.TryGetGlyphTypeface(out var face))FontInventoryRecord("installed",family,typeface.Weight,face);
                else Console.WriteLine($"N02 FONT kind=installed requestedFamily={FontInventoryField(family)} requestedWeight={typeface.Weight.ToOpenTypeWeight()} resolved=False");
            }
            foreach(var weight in new[]{FontWeights.Normal,FontWeights.Bold})
            {
                requests++;
                var requested=new Typeface(new FontFamily(family),FontStyles.Normal,weight,FontStretches.Normal);
                if(!requested.TryGetGlyphTypeface(out var face))
                {
                    missing++;unsupported++;
                    Console.WriteLine($"N02 FONT kind=request requestedFamily={FontInventoryField(family)} requestedWeight={weight.ToOpenTypeWeight()} resolved=False candidate=False disposition=UNSUPPORTED");
                    Require(weight!=FontWeights.Normal,"Previously supported fixed regular font must provide actual native inventory evidence");
                    continue;
                }
                resolved++;bool candidate=FontInventoryCandidate(face,family,weight);
                if(candidate)candidates++;else unsupported++;
                FontInventoryRecord("request",family,weight,face);
                Require(candidate||weight!=FontWeights.Normal,"Previously supported regular resolution must have exact family/style/weight/stretch, no simulation and approved system origin");
                if(weight==FontWeights.Normal)
                {
                    Require(!FontInventoryCandidate(face,family,FontWeights.Bold),"Actual regular native face is refused as a requested bold inventory candidate");
                    // Construct only from the just-validated system face URI, never a supplied font path.
                    var simulated=new GlyphTypeface(face.FontUri,StyleSimulations.BoldSimulation);
                    Require(simulated.StyleSimulations!=StyleSimulations.None&&!FontInventoryCandidate(simulated,family,FontWeights.Bold)&&!FontInventoryCandidate(simulated,family,FontWeights.Normal),"Actual system glyph face with WPF bold simulation is refused for both requested styles");
                    Console.WriteLine($"N02 FONT control requestedFamily={FontInventoryField(family)} regularAsBoldRejected=True actualSimulation={simulated.StyleSimulations} simulationRejected=True");
                }
            }
        }
        Require(requests==8&&resolved+missing==requests&&candidates+unsupported==requests,"Inventory records every fixed regular/bold request without silently omitting unavailable faces");
        Console.WriteLine($"N02 FONT INVENTORY complete=True installedRecords={installedRecords} requests={requests} resolved={resolved} missing={missing} exactUnsimulatedCandidates={candidates} unsupported={unsupported} rendererApproval=False");
        return Task.CompletedTask;
    }
    private static bool FontInventoryOrigin(GlyphTypeface face)
    {
        try{PdfVisualRenderer.ValidateFontOrigin(face.FontUri);return true;}
        catch(IOException){return false;}
        catch(UnauthorizedAccessException){return false;}
        catch(InvalidDataException){return false;}
    }
    private static string FontInventoryName(IDictionary<CultureInfo,string> names)
        =>names.TryGetValue(CultureInfo.GetCultureInfo("en-US"),out var name)?name:"<missing-en-US>";
    private static bool FontInventoryCandidate(GlyphTypeface face,string family,FontWeight weight)
        =>FontInventoryOrigin(face)&&FontInventoryName(face.FamilyNames)==family&&face.Weight==weight&&face.Style==FontStyles.Normal&&face.Stretch==FontStretches.Normal&&face.StyleSimulations==StyleSimulations.None;
    private static string FontInventoryField(string value)
    {
        Require(value.Length<=512,"Native inventory field output is bounded");return JsonSerializer.Serialize(value);
    }
    private static void FontInventoryRecord(string kind,string requestedFamily,FontWeight requestedWeight,GlyphTypeface face)
    {
        bool origin=FontInventoryOrigin(face),candidate=FontInventoryCandidate(face,requestedFamily,requestedWeight);
        string uri=origin?face.FontUri.AbsoluteUri:"<unapproved-origin-redacted>";
        Console.WriteLine($"N02 FONT kind={kind} requestedFamily={FontInventoryField(requestedFamily)} requestedWeight={requestedWeight.ToOpenTypeWeight()} resolved=True actualFamily={FontInventoryField(FontInventoryName(face.FamilyNames))} actualFace={FontInventoryField(FontInventoryName(face.FaceNames))} actualWeight={face.Weight.ToOpenTypeWeight()} actualStretch={face.Stretch.ToOpenTypeStretch()} actualStyle={face.Style} simulation={face.StyleSimulations} systemOrigin={origin} uri={FontInventoryField(uri)} candidate={candidate} disposition={(candidate?"INVENTORY-CANDIDATE":"UNSUPPORTED")}");
    }
}
