using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using MemoApp.Core.Editing;
using MemoApp.Core.Lifecycle;
using MemoApp.Core.Storage;
internal static class DeviceUiChecks
{
    internal static async Task Run()
    {
        var smallNegative=new PixelWorkArea(-900,-500,800,450);var fit=DesktopLayoutGeometry.Provisional(smallNegative,3000,2000);
        VaultChecks.Require(fit==new PixelWindowRect(-900,-500,800,450),"oversized old HWND must fit wholly in small negative-origin target before DPI lookup");
        var geometry=new StoredWindowLayout("clock",null,"synthetic",1,1,400,200,96);
        VaultChecks.Require(DesktopLayoutGeometry.Restore(new(-1920,0,1920,1080),geometry,144)==new PixelWindowRect(-600,780,600,300),"negative-origin mixed-DPI geometry converts DIP exactly once and clamps bottom/right");
        VaultChecks.Require(DesktopLayoutGeometry.Restore(smallNegative,geometry with{Folded=true},192)==new PixelWindowRect(-900,-370,800,320),"folded geometry uses fixed DIP height independent of recorded DPI");
        var workspace=new EditingWorkspace(TimeProvider.System); var note=workspace.CreateNote();note.Title="합성 위치 제목";note.Text="DEVICE_LAYOUT_SYNTHETIC";
        var before=workspace.Capture();workspace.AcceptPrepared(before);Guid profile=Guid.NewGuid(),other=Guid.NewGuid();
        workspace.SetUiPreferences(profile,new(true,18,1.25));
        var layout=new StoredWindowLayout("memo",note.Id,"SYNTHETIC_MONITOR",0.2,0.3,360,400,144,true,true,0.8,true,true);
        workspace.SetWindowLayout(profile,layout);var snapshot=workspace.Capture();
        VaultChecks.Require(snapshot.SchemaVersion==4 && snapshot.UiDevices.Length==1 && snapshot.Notes[0]==before.Notes[0] && snapshot.History.Length==0 && note.EditVersion==2,"layout/preferences must not alter content revision, timestamps or draft version");
        VaultChecks.Require(workspace.GetUiDevice(other).Windows.Length==0 && !workspace.GetUiDevice(other).Preferences.DarkMode,"other device profile must not inherit windows/preferences");
        VaultChecks.ExpectFailure(()=>workspace.SetWindowLayout(profile,layout with{NoteId=Guid.NewGuid()}),"dangling layout note reject");
        VaultChecks.ExpectFailure(()=>workspace.SetWindowLayout(profile,layout with{X=double.NaN}),"nonfinite layout reject");
        VaultChecks.ExpectFailure(()=>workspace.SetUiPreferences(profile,new(true,999,1)),"UI font bound reject");
        VaultChecks.Require(JsonSerializer.Serialize(workspace.Capture())==JsonSerializer.Serialize(snapshot),"failed layout mutation must preserve full snapshot");
        var root=Path.Combine(Path.GetTempPath(),"memo-device-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var secret=EncryptedVault.GenerateRecoverySecret();
        try
        {
            void Reject(Action<JsonObject> alter,string reason)
            {
                var payload=JsonSerializer.SerializeToNode(snapshot,VaultEnvelope.JsonOptions)!.AsObject();alter(payload);
                VaultChecks.ExpectFailure(()=>VaultEnvelope.Decrypt(Schema2Checks.Encode(payload,secret),secret),reason);
            }
            Reject(p=>p.Remove("uiDevices"),"v3 UI records required");
            foreach(var field in new[]{"uiDeviceId","preferences","windows"})Reject(p=>p["uiDevices"]![0]!.AsObject().Remove(field),"v3 device field required: "+field);
            foreach(var field in new[]{"darkMode","fontSize","scale"})Reject(p=>p["uiDevices"]![0]!["preferences"]!.AsObject().Remove(field),"v3 preference field required: "+field);
            foreach(var field in new[]{"kind","noteId","monitor","x","y","width","height","dpi","open","topmost","opacity","folded","positionLocked"})Reject(p=>p["uiDevices"]![0]!["windows"]![0]!.AsObject().Remove(field),"v3 layout field required: "+field);
            Reject(p=>p["uiDevices"]![0]!["preferences"]!["darkMode"]="true","UI boolean type must be strict");
            Reject(p=>p["uiDevices"]![0]!["preferences"]!["fontSize"]="18","UI numeric type must be strict");
            Reject(p=>p["uiDevices"]![0]!["windows"]![0]!["unexpected"]=true,"UI unknown layout field reject");
            Reject(p=>p["uiDevices"]!.AsArray().Add(p["uiDevices"]![0]!.DeepClone()),"duplicate UI profiles reject");
            Reject(p=>p["uiDevices"]![0]!["windows"]!.AsArray().Add(p["uiDevices"]![0]!["windows"]![0]!.DeepClone()),"duplicate UI window reject");
            Reject(p=>p["uiDevices"]![0]!["uiDeviceId"]=Guid.Empty.ToString(),"empty UI profile ID reject");
            Reject(p=>p["uiDevices"]![0]!["preferences"]=null,"null UI preferences reject");
            Reject(p=>p["uiDevices"]![0]!["windows"]=null,"null immutable UI windows reject");
            foreach(var flag in new[]{"open","topmost","folded","positionLocked"})Reject(p=>p["uiDevices"]![0]!["windows"]![0]![flag]=0,"strict UI flag type: "+flag);
            foreach(var pair in new[]{("x",-0.01),("y",1.01),("width",199d),("height",1601d),("dpi",47d),("opacity",0.29)})Reject(p=>p["uiDevices"]![0]!["windows"]![0]![pair.Item1]=pair.Item2,"UI range bound: "+pair.Item1);
            Reject(p=>{var windows=p["uiDevices"]![0]!["windows"]!.AsArray();while(windows.Count<=102)windows.Add(windows[0]!.DeepClone());},"UI layout array cap rejects authenticated payload");
            Reject(p=>{var devices=p["uiDevices"]!.AsArray();while(devices.Count<=32){var extra=devices[0]!.DeepClone();extra["uiDeviceId"]=Guid.NewGuid().ToString();devices.Add(extra);}},"UI profile array cap rejects authenticated payload");
            Reject(p=>p["schemaVersion"]=2,"v2 must refuse UI records rather than silently accepting newer metadata");
            var duplicate=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot,VaultEnvelope.JsonOptions).Replace("\"scale\":1.25","\"scale\":1.25,\"scale\":1.25",StringComparison.Ordinal));
            var syntheticKey=RandomNumberGenerator.GetBytes(32);
            try{VaultChecks.ExpectFailure(()=>VaultEnvelope.Decrypt(VaultEnvelope.Encrypt(duplicate,syntheticKey,secret,new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),1,2,duplicate.Length)),secret),"authenticated duplicate UI JSON property reject");}
            finally{CryptographicOperations.ZeroMemory(syntheticKey);CryptographicOperations.ZeroMemory(duplicate);}
            VaultChecks.ExpectFailure(()=>VaultEnvelope.Validate(snapshot with{UiDevices=[new(profile,new(),default)]}),"default immutable UI windows reject");
            var capped=new EditingWorkspace(TimeProvider.System,before);for(int i=0;i<32;i++)capped.SetUiPreferences(Guid.NewGuid(),new(true,14,1));
            var capBefore=JsonSerializer.SerializeToUtf8Bytes(capped.Capture(),VaultEnvelope.JsonOptions);int capEvents=0;capped.Changed+=()=>capEvents++;
            VaultChecks.ExpectFailure(()=>capped.SetUiPreferences(Guid.NewGuid(),new(true,14,1)),"UI profile count preflight cap");
            VaultChecks.Require(capEvents==0 && JsonSerializer.SerializeToUtf8Bytes(capped.Capture(),VaultEnvelope.JsonOptions).SequenceEqual(capBefore),"profile cap rejection preserves snapshot and events");capped.Clear();
            using(var vault=EncryptedVault.Create(root,secret,secret))vault.Save(snapshot);
            using(var reopened=EncryptedVault.Open(root,secret))
            {
                var restored=new EditingWorkspace(TimeProvider.System,reopened.Loaded);
                VaultChecks.Require(restored.GetUiDevice(profile).Windows.Single()==layout && restored.GetUiDevice(profile).Preferences.FontSize==18,"real encrypted device restart");
            }
            var oldRevision=Guid.NewGuid();var oldFolder=Guid.NewGuid();var oldTag=Guid.NewGuid();var oldMeta=before.Notes[0].Metadata with{FolderId=oldFolder,TagIds=[oldTag]};
            var legacy=before with{SchemaVersion=2,Notes=[before.Notes[0] with{Parents=[oldRevision],Metadata=oldMeta}],History=[new(note.Id,oldRevision,[],note.ModifiedAt,"old title","LEGACY_V2_HISTORY"){Metadata=oldMeta}],Folders=[new(oldFolder,null,"legacy2 folder")],Tags=[new(oldTag,"legacy2 tag")]};var payload=JsonSerializer.SerializeToNode(legacy,VaultEnvelope.JsonOptions)!.AsObject();payload.Remove("uiDevices");Schema2Checks.StripDocumentFields(payload);
            var legacyRoot=Path.Combine(root,"legacy2");Directory.CreateDirectory(legacyRoot);var original=Schema2Checks.Encode(payload,secret);File.WriteAllBytes(Path.Combine(legacyRoot,"current.vault"),original);
            using(var session=new SaveCoordinator(EncryptedVault.Open(legacyRoot,secret),TimeProvider.System)){VaultChecks.Require(session.Workspace.Notes.Single().Mode=="plain" && session.Workspace.Notes.Single().Document is null && session.Workspace.Capture().History.Single().Text=="LEGACY_V2_HISTORY" && session.Workspace.Capture().History.Single().Mode=="plain" && session.Workspace.Capture().History.Single().Document is null && session.Workspace.Folders.Single().FolderId==oldFolder && session.Workspace.Tags.Single().TagId==oldTag,"actual legacy2 history and organization load losslessly");VaultChecks.Require(session.IsDirty && await session.SaveAsync(),"schema2 to4 migration save");await session.LockAsync();}
            VaultChecks.Require(Directory.GetFiles(legacyRoot,"previous-*.vault").Any(p=>File.ReadAllBytes(p).SequenceEqual(original)),"schema2 migration exact previous bytes");
            var failedRoot=Path.Combine(root,"failed-legacy2");Directory.CreateDirectory(failedRoot);File.WriteAllBytes(Path.Combine(failedRoot,"current.vault"),original);
            using(var session=new SaveCoordinator(EncryptedVault.Open(failedRoot,secret,files:new VaultFailureChecks.FaultFiles("pre-flush")),TimeProvider.System))
            {VaultChecks.Require(!await session.SaveAsync(),"v2 to3 failed flush cannot report migration success");await session.LockAsync();}
            VaultChecks.Require(File.ReadAllBytes(Path.Combine(failedRoot,"current.vault")).SequenceEqual(original),"v2 to3 failed migration preserves original bytes");
            using(var hidden=new SaveCoordinator(EncryptedVault.Open(root,secret),TimeProvider.System))
            {
                var dirty=hidden.Workspace.Notes.Single();dirty.Text="DEVICE_UI_UNSAVED_SYNTHETIC";
                hidden.Workspace.SetUiPreferences(profile,new(false,20,1.1));
                VaultChecks.Require(hidden.Workspace.FrozenBasis.Notes.Single().Text==note.Text && hidden.Workspace.FrozenBasis.History.Length==0,"UI change must not accept an unrelated dirty content draft");
                dirty.Title=new string('x',257);await hidden.LockAsync();hidden.ResumeHidden(secret);
                VaultChecks.Require(hidden.Workspace.GetUiDevice(profile).Preferences.FontSize==20 && hidden.Workspace.GetUiDevice(profile).Windows.Single()==layout,"hidden invalid-draft recovery keeps full latest UI profile");
                hidden.Workspace.Notes.Single().Title="fixed UI hidden synthetic";VaultChecks.Require(await hidden.SaveAsync(),"hidden UI recovery can save corrected content");await hidden.LockAsync();
            }
            PayloadBoundary(snapshot,profile);
            string identity=Path.Combine(root,"identity");var first=LocalUiIdentity.GetOrCreate(identity);VaultChecks.Require(first!=Guid.Empty && LocalUiIdentity.GetOrCreate(identity)==first,"local nonsecret UI identifier stable restart");
            var concurrent=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Task.Run(()=>LocalUiIdentity.GetOrCreate(Path.Combine(root,"concurrent-identity")))));
            VaultChecks.Require(concurrent.Distinct().Count()==1,"concurrent local identifier creators must share the winning ID");
            var idFile=Path.Combine(identity,"ui-device.id");File.WriteAllBytes(idFile,[1,2,3]);VaultChecks.ExpectFailure(()=>LocalUiIdentity.GetOrCreate(identity),"corrupt existing UI identity must not silently replace");
            VaultChecks.Require(File.ReadAllBytes(idFile).SequenceEqual(new byte[]{1,2,3}),"corrupt UI identity must remain preserved");
            if(!OperatingSystem.IsWindows())
            {
                var linkedRoot=Path.Combine(root,"linked-identity");Directory.CreateSymbolicLink(linkedRoot,identity);
                VaultChecks.ExpectFailure(()=>LocalUiIdentity.GetOrCreate(linkedRoot),"linked identity ancestor must reject");Directory.Delete(linkedRoot);
                var linkedLeaf=Path.Combine(root,"linked-leaf");Directory.CreateDirectory(linkedLeaf);File.CreateSymbolicLink(Path.Combine(linkedLeaf,"ui-device.id"),idFile);
                VaultChecks.ExpectFailure(()=>LocalUiIdentity.GetOrCreate(linkedLeaf),"linked identity leaf must reject");
            }
            workspace.Clear();VaultChecks.Require(workspace.FrozenBasis.UiDevices.Length==0,"close removes private layout basis");
            Console.WriteLine("PASS: schema3 bounded device-local layout/preferences isolation, no content revision noise, encrypted restart/migration and stable/concurrent/corrupt UI identifier");
        }
        finally{CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
    private static void PayloadBoundary(VaultSnapshot snapshot,Guid profile)
    {
        var note=snapshot.Notes.Single();var history=Enumerable.Range(0,255).Select(_=>new StoredRevision(note.NoteId,Guid.NewGuid(),[],note.ModifiedAt,"h",new string('x',65536))).ToArray();
        var candidate=snapshot with{UiDevices=[],History=history};
        // Build a valid payload only 8 bytes below the same bounded encrypted-payload limit.
        int limit=VaultEnvelope.MaxFile-VaultEnvelope.HeaderSize-148;
        int length=SnapshotSerialization.Bytes(candidate).Length;
        int last=history[^1].Text.Length-(length-(limit-8));VaultChecks.Require(last is >=0 and <=65536,"synthetic UI byte-boundary fixture");history[^1]=history[^1] with{Text=new string('x',last)};
        VaultEnvelope.Validate(candidate);var bounded=new EditingWorkspace(TimeProvider.System,candidate);var original=JsonSerializer.SerializeToUtf8Bytes(bounded.Capture(),VaultEnvelope.JsonOptions);int events=0;bounded.Changed+=()=>events++;
        VaultChecks.ExpectFailure(()=>bounded.SetUiPreferences(profile,new(true,18,1)),"UI profile byte-budget overflow rejects before state change");
        VaultChecks.Require(events==0 && bounded.GetUiDevice(profile).Windows.Length==0 && JsonSerializer.SerializeToUtf8Bytes(bounded.Capture(),VaultEnvelope.JsonOptions).SequenceEqual(original),"UI byte-budget rejection preserves snapshot/events/basis");
        bounded.Clear();
    }
}
