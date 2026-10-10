using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;

internal static class RichParagraphPdfSourceChecks
{
    internal static async Task Run()
    {
        Require(typeof(SaveCoordinator).Assembly.GetType("MemoApp.Core.Transfer.RichParagraphPdfCapture") is not null,
            "N02 inert rich paragraph PDF capture API missing");
        Profile(); ExactLimits(); Refusals(); Bounds(); Detached(); await Preservation();
        Console.WriteLine("PASS: N02 inert paragraph/run capture, exact source, whole refusal, grapheme/style/budgets and encrypted source preservation");
    }
    // No reference to the new production types: this test also compiles against the immutable pre-feature DLL.
    private static object[] Capture(NoteDraft[] notes, CancellationToken token = default)
        => CaptureList(notes,token);
    private static object[] CaptureList(IReadOnlyList<NoteDraft> notes,CancellationToken token)
    {
        var type=typeof(SaveCoordinator).Assembly.GetType("MemoApp.Core.Transfer.RichParagraphPdfCapture")!;
        try { return ((IEnumerable)type.GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[notes,token])!).Cast<object>().ToArray(); }
        catch(TargetInvocationException e) when(e.InnerException is not null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
    }
    private static object Value(object value,string name)=>value.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(value)!;
    private static object[] Items(object value,string name)=>((IEnumerable)Value(value,name)).Cast<object>().ToArray();
    private static NoteDraft Note(string text,string mode="plain",StyledDocument? document=null,string title="synthetic")
    {
        var now=DateTimeOffset.UtcNow;
        var stored=new StoredNote(Guid.NewGuid(),Guid.NewGuid(),[],now,now,title,text,mode){Document=document};
        return new EditingWorkspace(TimeProvider.System,new(10,Guid.NewGuid(),[stored])).Notes.Single();
    }
    private static StyledDocument Doc(params object[] runs)=>new(1,JsonSerializer.Serialize(new {nodes=new[]{new {type="paragraph",runs}}}));
    private static NoteDraft Rich(StyledDocument d)=>Note(RichDocumentCodec.Inspect(d).Text??"", "rich",d);
    private static void Profile()
    {
        var doc=Doc(new{text="e",bold=false},new{text="\u0301",fontFamily="Segoe UI",fontSize=14,foreground="#FF000000",background="#00000000"},
            new{text="색 한글 😀",bold=true,underline=true,strike=true,fontFamily="Malgun Gothic",fontSize=96,foreground="#123456",background="#80123456"});
        var rich=Rich(doc);var plain=Note("literal https://synthetic.invalid <html>\r\n\t");
        var sources=Capture([plain,rich]);Require(sources.Length==2,"Exact selection order/count");
        Require((string)Value(sources[0],"Text")==plain.Text&&(string)Value(sources[0],"Mode")=="plain","Plain literal source preserved");
        Require(ReferenceEquals(Value(sources[1],"OriginalDocument"),doc)&&(string)Value(sources[1],"Text")==rich.Text,"Exact immutable source identity and projection");
        var runs=Items(Items(sources[1],"Paragraphs").Single(),"Runs");Require(runs.Length==3,"Original run segmentation retained");
        Require(Value(runs[0],"Style").Equals(Value(runs[1],"Style")),"Missing and explicit effective defaults equivalent across combining cluster");
        var style=Value(runs[2],"Style");Require((bool)Value(style,"Bold")&&(bool)Value(style,"Underline")&&(bool)Value(style,"Strike")&&
            (double)Value(style,"FontSize")==96&&(uint)Value(style,"Foreground")==0xff123456&&(uint)Value(style,"Background")==0x80123456,"All accepted style values exact");
        Capture([Rich(Doc(new{text="\r"},new{text="\n\t"}))]);
        Capture([Rich(new(1,"{\"nodes\":[{\"type\":\"paragraph\",\"runs\":[]},{\"type\":\"paragraph\",\"runs\":[{\"text\":\"\"}]}]}"))]);
        var accepted=Capture([Note(new string('x',65536),title:new string('t',256))]);Require(accepted.Length==1,"Exact text/title boundaries accepted");
    }
    private static void ExactLimits()
    {
        Require(Capture(Enumerable.Range(0,100).Select(_=>Note("x")).ToArray()).Length==100,"Exact selection limit accepted");
        Capture([Rich(Doc(new{text="e"},new{text="",bold=true},new{text="\u0301"}))]);
        Capture([Note("e"+new string('\u0301',127))]);
        var exact=Enumerable.Range(0,85).Select(_=>Note(new string('漢',65536),title:"")).Append(Note(new string('x',65536),title:"")).ToArray();
        Require(Capture(exact).Length==86,"Exact 16 MiB aggregate payload accepted");
        Fails(()=>Capture(exact.Append(Note("x",title:"")).ToArray()));
    }
    private static void Refusals()
    {
        var valid=Note("first valid");
        foreach(var doc in new[]{
            new StyledDocument(2,"{\"nodes\":[]}"),new(1,"{\"nodes\":[{\"type\":\"list\",\"ordered\":false,\"items\":[{\"runs\":[{\"text\":\"x\"}]}]}]}"),
            new(1,"{\"nodes\":[{\"type\":\"checklist\",\"items\":[{\"checked\":true,\"runs\":[{\"text\":\"x\"}]}]}]}"),
            new(1,"{\"nodes\":[{\"type\":\"table\",\"rows\":[[{\"runs\":[{\"text\":\"x\"}]}]]}]}"),
            Doc(new{text="x",link="https://synthetic.invalid"}),Doc(new{text="x",fontFamily="Arial"}),Doc(new{text="x",future=true}),
            new(1,"{\"nodes\":[],\"future\":true}"),new(1,"{\"nodes\":[],\"nodes\":[]}"),
            Doc(new{text="e"},new{text="\u0301",bold=true}),Doc(new{text="\r"},new{text="\n",underline=true})})
        {
            string projection;
            try { projection=RichDocumentCodec.Inspect(doc).Text??""; }
            catch(Exception e) when(e is InvalidDataException or JsonException){projection="";}
            Fails(()=>Capture([valid,Note(projection,"rich",doc)]));
        }
        Fails(()=>Capture([valid,Note("x","markdown")]));Fails(()=>Capture([valid,Note("x","unknown")]));
        Fails(()=>Capture([Note("wrong projection","rich",Doc(new{text="right"}))]));
        Fails(()=>Capture([valid,valid]));Fails(()=>Capture([]));
        var workspace=new EditingWorkspace(TimeProvider.System);var trash=workspace.CreateNote();workspace.DeleteNote(trash);Fails(()=>Capture([trash]));
        var closed=Note("closed");typeof(NoteDraft).GetMethod("Close",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(closed,null);Fails(()=>Capture([closed]));
        foreach(string text in new[]{"\0","\ud800","\u200d","\ufe0f","\u05d0","\u202e",new string('e',1)+new string('\u0301',128)})
            Fails(()=>Capture([Note(text)]));
        Fails(()=>Capture([Rich(Doc(new{text="x",fontSize=7}))]));Fails(()=>Capture([Rich(Doc(new{text="x",foreground="#zzzzzz"}))]));
        using var canceled=new CancellationTokenSource();canceled.Cancel();Fails<OperationCanceledException>(()=>Capture([valid],canceled.Token));
        using var duringSelection=new CancellationTokenSource();Fails<OperationCanceledException>(()=>CaptureList(new CancelingSelection(valid,duringSelection),duringSelection.Token));
    }
    private static void Bounds()
    {
        Fails(()=>Capture(Enumerable.Range(0,101).Select(_=>Note("x")).ToArray()));
        Fails(()=>Capture([Note(new string('x',65537))]));Fails(()=>Capture([Note("x",title:new string('t',257))]));
        Fails(()=>Capture(Enumerable.Range(0,100).Select(_=>Note(new string('漢',65536))).ToArray()));
        var padding=new string(' ',170000);var source=new StyledDocument(1,"{"+padding+"\"nodes\":[{\"type\":\"paragraph\",\"runs\":[{\"text\":\"x\"}]}]}");
        Fails(()=>Capture(Enumerable.Range(0,100).Select(_=>Note("x","rich",source)).ToArray()));
        var minimal="{\"nodes\":[]}";
        Capture([Note("","rich",new(1,"{"+new string(' ',1024*1024-minimal.Length)+"\"nodes\":[]}"))]);
        Fails(()=>Capture([Note("","rich",new(1,"{"+new string(' ',1024*1024)+"\"nodes\":[]}"))]));
        Capture([Note("","rich",Doc(Enumerable.Range(0,4096).Select(_=>(object)new{text=""}).ToArray()))]);
        Fails(()=>Capture([Note("","rich",Doc(Enumerable.Range(0,4097).Select(_=>(object)new{text=""}).ToArray()))]));
        var paragraphs=JsonSerializer.Serialize(new{nodes=Enumerable.Range(0,1025).Select(_=>new{type="paragraph",runs=Array.Empty<object>()})});
        Fails(()=>Capture([Note(new string('\n',1024),"rich",new(1,paragraphs))]));
        var boundedParagraphs=JsonSerializer.Serialize(new{nodes=Enumerable.Range(0,1024).Select(_=>new{type="paragraph",runs=Array.Empty<object>()})});
        Capture([Note(new string('\n',1023),"rich",new(1,boundedParagraphs))]);
        var tokenHeavy=Doc(Enumerable.Range(0,2000).Select(_=>(object)new{text="",bold=false,underline=false,strike=false,fontFamily="Segoe UI",fontSize=14,foreground="#000000",background="#00000000"}).ToArray());
        Fails(()=>Capture([Note("","rich",tokenHeavy)]));
    }
    private static void Detached()
    {
        var source=Capture([Note("captured")]).Single();
        foreach(var type in typeof(SaveCoordinator).Assembly.GetTypes().Where(t=>t.Name is "RichParagraphPdfSource" or "RichPdfParagraph" or "RichPdfRun" or "RichPdfRunStyle"))
        {
            Require(!type.IsPublic,"Profile DTO has no public authority claim");
            foreach(var field in type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
                Require(!typeof(Delegate).IsAssignableFrom(field.FieldType)&&field.FieldType!=typeof(NoteDraft)&&field.FieldType!=typeof(EditingWorkspace)&&field.FieldType!=typeof(SaveCoordinator)&&field.FieldType!=typeof(EncryptedVault)&&!field.FieldType.FullName!.StartsWith("System.Windows",StringComparison.Ordinal),"Detached data DTO has no owner/key/UI/callback");
        }
        Require((string)Value(source,"Text")=="captured","Detached source stable");
    }
    private static async Task Preservation()
    {
        string root=Path.Combine(Path.GetTempPath(),"memo-n02-paragraph-"+Guid.NewGuid().ToString("N"));byte[] secret=EncryptedVault.GenerateRecoverySecret();
        try
        {
            var vault=EncryptedVault.Create(root,secret,secret);using var owner=new SaveCoordinator(vault,TimeProvider.System);
            var n=owner.Workspace.CreateNote();n.Text="source";n.Title="synthetic encrypted source";n.Favorite=true;
            Require(await owner.PrepareAttachmentsAsync(),"Actual attachment root prepared");
            owner.AttachBytes(n,Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAEElEQVR4AQEFAPr/AAECAwQAGQALubDj6wAAAABJRU5ErkJggg=="),"synthetic.png","image/png",n.EditVersion);
            owner.Workspace.ConvertMode(n,"rich",true);Require(await owner.SaveAsync(),"Actual source encrypted saved");
            var saved=owner.Workspace.Capture();var obj=saved.AttachmentObjects.Single();
            var provenance=new StoredOcrProvenance("db0ec62f81b0737fbbe184d8fea40af5738f8eef","13275a278eb55b5746e33f95fbf5a2c8f604b3ab","87416418657359cb625c412a48b6e1d6d41c29bd",new string('a',64),"6b85e11d9bbf07863b97b3523b1b112844c43e713df8b66418a081fd1060b3b2","7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2","kor+eng",1,6,"png-nearest-1024-straight-alpha-white-ppm-v1",1,1,1,1,new string('b',64));
            var historical=new StoredAttachmentOcrResult(obj.ObjectId,obj.Sha256,"synthetic historical OCR",Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("synthetic historical OCR"))),provenance);
            var imported=saved with{SchemaVersion=11,Notes=saved.Notes.Select(s=>s with{Metadata=s.Metadata with{AttachmentOcrResults=[historical]}}).ToArray()};
            vault.Save(imported);owner.Workspace.AcceptPrepared(imported); // Historical stored facts, not a runtime OCR result.
            owner.Workspace.SetRichDocument(n,Doc(new{text="edited",bold=true}));Require(await owner.SaveAsync(),"Actual historical rich/OCR revision saved");
            string snapshot=JsonSerializer.Serialize(owner.Workspace.Capture());long version=n.EditVersion,epoch=owner.AttachmentPreviewEpoch;
            var cipher=File.ReadAllBytes(Path.Combine(root,"current.vault"));var captured=Capture([n]);
            Fails(()=>Capture([n,Note("bad","markdown")]));
            Require(JsonSerializer.Serialize(owner.Workspace.Capture())==snapshot&&n.EditVersion==version&&owner.AttachmentPreviewEpoch==epoch&&!owner.IsDirty,"All workspace fields/history/metadata/object refs and source authority unchanged");
            Require(File.ReadAllBytes(Path.Combine(root,"current.vault")).SequenceEqual(cipher),"Actual ciphertext unchanged after success/refusal");
            n.Title="later edit";Require((string)Value(captured.Single(),"Title")=="synthetic encrypted source","Immutable captured title independent of later source edits");
        }
        finally{CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static void Fails(Action action)
    {
        try{action();}catch(Exception e) when(e is InvalidDataException or InvalidOperationException or JsonException or System.Text.EncoderFallbackException){return;}
        throw new InvalidOperationException("Expected whole profile refusal");
    }
    private static void Fails<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private sealed class CancelingSelection(NoteDraft note,CancellationTokenSource source):IReadOnlyList<NoteDraft>
    {
        public int Count=>2;
        public NoteDraft this[int index]{get{source.Cancel();return note;}}
        public IEnumerator<NoteDraft> GetEnumerator()=>new[]{note,note}.AsEnumerable().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }
}
