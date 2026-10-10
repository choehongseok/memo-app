using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MemoApp.Windows;
internal static partial class Program
{
    private static byte[] SyntheticDib()
    {
        byte[] bytes=new byte[56];BinaryPrimitives.WriteUInt32LittleEndian(bytes,40);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4),2);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8),-2);BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12),1);BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14),32);
        new byte[]{3,2,1,0,6,5,4,128,9,8,7,255,12,11,10,1}.CopyTo(bytes,40);return bytes;
    }
    private sealed class DibNativeFake:INativeClipboardDib
    {
        internal byte[] Bytes=SyntheticDib();internal uint Sequence=71;internal ulong? Size;internal string? Fail,RevokeAt,ReturnFailure;internal Action? Revoke;internal int Copies,Unlocks,Closes,MaximumCopy;internal List<byte[]> Owned=[];
        void Called(string name){if(RevokeAt==name)Revoke?.Invoke();if(Fail==name)throw new IOException(name);}
        public uint GetSequence(){Called("Sequence");return Sequence;}
        public bool Open(){Called("Open");return ReturnFailure!="Open";}
        public nint GetDib(){Called("Get");return ReturnFailure=="Get"?0:1;}
        public nuint GetSize(nint handle){Called("Size");return checked((nuint)(Size??(ulong)Bytes.Length));}
        public nint Lock(nint handle){Called("Lock");return ReturnFailure=="Lock"?0:2;}
        public void Copy(nint pointer,byte[] destination,int offset,int count){Copies++;MaximumCopy=Math.Max(MaximumCopy,count);Called("Copy");Array.Copy(Bytes,offset,destination,offset,count);}
        public bool Unlock(nint handle){Unlocks++;Called("Unlock");return ReturnFailure!="Unlock";}
        public bool Close(){Closes++;Called("Close");return ReturnFailure!="Close";}
    }
    private static void DibNativeCaptureRun()
    {
        var fake=new DibNativeFake();using(var captured=NativeClipboardDibCapture.Capture(fake,()=>true,CancellationToken.None,fake.Owned.Add)){Require(captured.Sequence==71&&captured.Content.Span.SequenceEqual(fake.Bytes)&&fake.Unlocks==1&&fake.Closes==1,"Owned native capture preserves exact bytes and cleans native handles");}
        Require(fake.Owned.All(b=>b.All(v=>v==0)),"Owned native bytes clear on disposal");
        foreach(string boundary in new[]{"Open","Get","Size","Lock","Copy","Unlock","Close"})
        {
            bool allowed=true;fake=new(){RevokeAt=boundary,Revoke=()=>allowed=false};try{using var unexpected=NativeClipboardDibCapture.Capture(fake,()=>allowed,CancellationToken.None,fake.Owned.Add);throw new Exception("Revoked native capture accepted");}catch(OperationCanceledException){}
            Require(fake.Closes==1&&(boundary is "Open" or "Get" or "Size" or "Lock"?fake.Unlocks==(boundary=="Lock"?1:0):fake.Unlocks==1)&&fake.Owned.All(b=>b.All(v=>v==0)),"Native reentry revokes at "+boundary+" and still cleans acquired resources");
        }
        foreach(string boundary in new[]{"Get","Size","Lock","Copy","Unlock","Close"})
        {fake=new(){Fail=boundary};try{using var unexpected=NativeClipboardDibCapture.Capture(fake,()=>true,CancellationToken.None,fake.Owned.Add);throw new Exception("Failed native capture accepted");}catch(IOException){}Require(fake.Closes==1&&fake.Owned.All(b=>b.All(v=>v==0)),"Native failure cleanup "+boundary);}
        foreach(string boundary in new[]{"Open","Get","Lock","Unlock","Close"})
        {fake=new(){ReturnFailure=boundary};try{using var unexpected=NativeClipboardDibCapture.Capture(fake,()=>true,CancellationToken.None,fake.Owned.Add);throw new Exception("Native failure return accepted");}catch(Exception error)when(error is IOException or InvalidDataException){}Require(fake.Closes==(boundary=="Open"?0:1)&&fake.Unlocks==(boundary is "Open" or "Get" or "Lock"?0:1)&&fake.Owned.All(b=>b.All(v=>v==0)),"Native false/zero return cleanup "+boundary);}
        fake=new(){Bytes=new byte[131112]};using(var large=NativeClipboardDibCapture.Capture(fake,()=>true,CancellationToken.None,fake.Owned.Add)){Require(fake.Copies==3&&fake.MaximumCopy==65536,"Native copying never exceeds 64 KiB and copies whole owned allocation");}
        using(var cancel=new CancellationTokenSource())
        {fake=new(){Bytes=new byte[131112],RevokeAt="Copy",Revoke=cancel.Cancel};try{using var unexpected=NativeClipboardDibCapture.Capture(fake,()=>true,cancel.Token,fake.Owned.Add);throw new Exception("Chunk cancellation accepted");}catch(OperationCanceledException){}Require(fake.Copies==1&&fake.Unlocks==1&&fake.Closes==1&&fake.Owned.All(b=>b.All(v=>v==0)),"Cancellation between native copy chunks discards and cleans");}
        fake=new();try{using var unexpected=NativeClipboardDibCapture.Capture(fake,()=>true,CancellationToken.None,b=>{fake.Owned.Add(b);throw new IOException("Synthetic allocation observer");});throw new Exception("Allocation observer failure accepted");}catch(IOException){}Require(fake.Copies==0&&fake.Unlocks==1&&fake.Closes==1&&fake.Owned.All(b=>b.All(v=>v==0)),"Allocation observer exception zeroes native owned scratch before any copy");
        foreach(ulong size in new ulong[]{0,39,4194305,ulong.MaxValue})
        {fake=new(){Size=size};try{using var unexpected=NativeClipboardDibCapture.Capture(fake,()=>true,CancellationToken.None,fake.Owned.Add);throw new Exception("Invalid native size accepted");}catch(InvalidDataException){}Require(fake.Copies==0&&fake.Owned.Count==0,"Native SIZE_T bound checked before allocation/narrowing");}
        fake=new(){Sequence=0};try{using var unexpected=NativeClipboardDibCapture.Capture(fake,()=>true,CancellationToken.None);throw new Exception("Unknown sequence accepted");}catch(OperationCanceledException){}Require(fake.Closes==0,"Sequence zero refused before native opening");
        fake=new(){RevokeAt="Copy"};fake.Revoke=()=>fake.Sequence++;try{using var unexpected=NativeClipboardDibCapture.Capture(fake,()=>true,CancellationToken.None,fake.Owned.Add);throw new Exception("Changed sequence accepted");}catch(OperationCanceledException){}Require(fake.Unlocks==1&&fake.Closes==1&&fake.Owned.All(b=>b.All(v=>v==0)),"Changed sequence clears scratch and native ownership");
    }
}
internal static partial class Program
{
    // Only this synthetic fixture writes the clipboard. Ownership transfers to Windows on SetClipboardData.
    private sealed class SyntheticNativeDib:IDisposable
    {
        private readonly System.Windows.Interop.HwndSource owner=new(new System.Windows.Interop.HwndSourceParameters("synthetic-dib-fixture"){Width=1,Height=1});
        internal readonly byte[] Source=SyntheticDib();internal uint Sequence{get;}
        internal SyntheticNativeDib()
        {
            Require(FixtureOpen(owner.Handle),"Synthetic native clipboard open");nint memory=0;bool transferred=false;
            try
            {
                Require(FixtureEmpty(),"Synthetic fixture clipboard empty");memory=FixtureAlloc(0x42,(nuint)Source.Length);Require(memory!=0,"Synthetic movable allocation");nint pointer=FixtureLock(memory);Require(pointer!=0,"Synthetic allocation lock");try{Marshal.Copy(Source,0,pointer,Source.Length);}finally{FixtureSetError(0);Require(FixtureUnlock(memory)||Marshal.GetLastPInvokeError()==0,"Synthetic allocation unlock");}
                Require(FixtureSetData(8,memory)==memory,"Synthetic CF_DIB ownership transfer");transferred=true;
            }
            finally{if(!transferred&&memory!=0)FixtureFree(memory);Require(FixtureClose(),"Synthetic fixture close");}
            Sequence=NativeClipboardDib.Instance.GetSequence();Require(Sequence!=0,"Synthetic native sequence");
        }
        internal void Preserved(){using var captured=NativeClipboardDibCapture.Capture(NativeClipboardDib.Instance,()=>true,CancellationToken.None);Require(captured.Sequence==Sequence&&captured.Content.Span.SequenceEqual(Source),"Production reader preserves native sequence and synthetic source bytes");}
        public void Dispose(){Require(FixtureOpen(0),"Synthetic fixture cleanup open");try{Require(FixtureEmpty(),"Synthetic fixture cleanup empty");}finally{Require(FixtureClose(),"Synthetic fixture cleanup close");}CryptographicOperations.ZeroMemory(Source);owner.Dispose();}
        [DllImport("user32.dll",EntryPoint="OpenClipboard",SetLastError=true)] private static extern bool FixtureOpen(nint owner);
        [DllImport("user32.dll",EntryPoint="CloseClipboard",SetLastError=true)] private static extern bool FixtureClose();
        [DllImport("user32.dll",EntryPoint="EmptyClipboard",SetLastError=true)] private static extern bool FixtureEmpty();
        [DllImport("user32.dll",EntryPoint="SetClipboardData",SetLastError=true)] private static extern nint FixtureSetData(uint format,nint memory);
        [DllImport("kernel32.dll",EntryPoint="GlobalAlloc",SetLastError=true)] private static extern nint FixtureAlloc(uint flags,nuint size);
        [DllImport("kernel32.dll",EntryPoint="GlobalFree",SetLastError=true)] private static extern nint FixtureFree(nint memory);
        [DllImport("kernel32.dll",EntryPoint="GlobalLock",SetLastError=true)] private static extern nint FixtureLock(nint memory);
        [DllImport("kernel32.dll",EntryPoint="GlobalUnlock",SetLastError=true)] private static extern bool FixtureUnlock(nint memory);
        [DllImport("kernel32.dll",EntryPoint="SetLastError")] private static extern void FixtureSetError(uint value);
    }
}
internal static partial class Program
{
    [DllImport("user32.dll")] private static extern bool GetKeyboardState(byte[] keys);
    [DllImport("user32.dll")] private static extern bool SetKeyboardState(byte[] keys);
    private static async Task NativeDibControlV(AttachmentPanel panel,MemoApp.Core.Editing.SaveCoordinator active,MemoApp.Core.Editing.NoteDraft note)
    {
        var window=new System.Windows.Window{Content=panel,Width=400,Height=300,ShowInTaskbar=false};byte[] oldKeys=new byte[256];
        try
        {
            window.Show();window.Activate();panel.FilesList.Focus();await Task.Delay(10);Require(panel.IsKeyboardFocusWithin,"Attachment area receives synthetic Ctrl+V focus");Require(GetKeyboardState(oldKeys),"Capture fixture key state");byte[] keys=(byte[])oldKeys.Clone();
            // WPF KeyboardDevice.Modifiers reads LeftCtrl/RightCtrl, not generic VK_CONTROL.
            foreach(int modifier in new[]{0x10,0x12,0xA0,0xA1,0xA3,0xA4,0xA5})keys[modifier]=0;
            keys[0x11]=keys[0xA2]=0x80;Require(SetKeyboardState(keys),"Set synthetic left Control key state");
            try
            {
                Require(System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.LeftCtrl)&&!System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.RightCtrl)&&System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.Control,"Synthetic left Control modifier observed through WPF");int before=note.AttachmentIds.Length;var input=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,System.Windows.PresentationSource.FromVisual(panel)!,0,System.Windows.Input.Key.V){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent};panel.RaiseEvent(input);Require(input.Handled,"Attachment Ctrl+V handler claims explicit route");var elapsed=System.Diagnostics.Stopwatch.StartNew();while(note.AttachmentIds.Length==before||active.IsBusy||active.IsDirty){Require(elapsed.Elapsed<TimeSpan.FromSeconds(20),"Native Ctrl+V settles");await Task.Delay(10);}Require(note.AttachmentIds.Length==before+1&&panel.PreviewImage.Source is null,"Native attachment-focused Ctrl+V attaches exactly once without display");
            }
            finally{Require(SetKeyboardState(oldKeys),"Restore fixture keyboard state");CryptographicOperations.ZeroMemory(keys);}
        }
        finally{window.Content=null;window.Close();CryptographicOperations.ZeroMemory(oldKeys);}
    }
    private sealed class AbsentDibPngProvider(Action presence):System.Windows.IDataObject
    {
        public bool GetDataPresent(string format,bool autoConvert){Require(format=="PNG"&&!autoConvert,"Only exact PNG queried before DIB");presence();return false;}
        public object GetData(string format,bool autoConvert)=>throw new Exception("Absent PNG must not be read");
        public object GetData(string format)=>throw new NotSupportedException();public object GetData(Type format)=>throw new NotSupportedException();public bool GetDataPresent(string format)=>throw new NotSupportedException();public bool GetDataPresent(Type format)=>throw new NotSupportedException();public string[] GetFormats()=>throw new NotSupportedException();public string[] GetFormats(bool autoConvert)=>throw new NotSupportedException();
        public void SetData(object data)=>throw new NotSupportedException();public void SetData(string format,object data)=>throw new NotSupportedException();public void SetData(string format,object data,bool autoConvert)=>throw new NotSupportedException();public void SetData(Type format,object data)=>throw new NotSupportedException();
    }
    private sealed class DelayedDibBackend:DibClipboardBackend
    {
        internal readonly ManualResetEventSlim Started=new(),Release=new();internal readonly DibNativeFake Native=new();internal bool ChangeAfterWorker;internal int SequenceReads,ChangeAtSequenceRead,Captures;internal List<byte[]> Owned=[];
        internal override NativeDibCapture Capture(Func<bool> allowed,CancellationToken token){Captures++;return NativeClipboardDibCapture.Capture(Native,allowed,token,Owned.Add);}
        internal override uint GetSequence(){SequenceReads++;if(SequenceReads==ChangeAtSequenceRead)Native.Sequence++;return Native.Sequence;}
        internal override MemoApp.Core.Transfer.ClipboardPngSource Convert(ReadOnlyMemory<byte> input,CancellationToken token){Started.Set();Release.Wait();var result=base.Convert(input,token);if(ChangeAfterWorker)Native.Sequence++;return result;}
    }
    private static async Task DibClipboardRun()
    {
        DibNativeCaptureRun();string root=Path.Combine(Path.GetTempPath(),"memo-wpf-dib-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);byte[] secret=MemoApp.Core.Storage.EncryptedVault.GenerateRecoverySecret();byte[]? png=null;
        try
        {
            using var active=new MemoApp.Core.Editing.SaveCoordinator(MemoApp.Core.Storage.EncryptedVault.Create(root,secret,secret),TimeProvider.System);var note=active.Workspace.CreateNote();note.Text="synthetic native DIB";Require(await active.SaveAsync(),"DIB baseline");using var panel=new AttachmentPanel(active,note,()=>true,_=>{});
            using(var fixture=new SyntheticNativeDib())
            {
                var button=(System.Windows.Controls.Button)typeof(AttachmentPanel).GetField("pastePng",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(panel)!;button.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));var elapsed=System.Diagnostics.Stopwatch.StartNew();while(note.AttachmentIds.Length==0||active.IsBusy||active.IsDirty){Require(elapsed.Elapsed<TimeSpan.FromSeconds(20),"Native DIB button settles");await Task.Delay(10);}
                Require(note.AttachmentIds.Length==1&&panel.PreviewImage.Source is null,"Native DIB button attaches once without auto display/inline insertion");fixture.Preserved();Guid id=note.AttachmentIds.Single();png=(byte[])typeof(MemoApp.Core.Editing.SaveCoordinator).GetMethod("ReadAttachmentBytes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(active,[note,id,note.EditVersion])!;
                panel.FilesList.SelectedIndex=0;Require(await panel.PreviewSelectedAsync(),"Explicit generated PNG display");var bitmap=panel.PreviewImage.Source as System.Windows.Media.Imaging.BitmapSource??throw new Exception("Explicit generated PNG display has no bitmap");byte[] pixels=new byte[16];bitmap.CopyPixels(pixels,8,0);Require(bitmap.IsFrozen&&bitmap.PixelWidth==2&&bitmap.PixelHeight==2&&pixels.SequenceEqual(new byte[]{3,2,1,255,6,5,4,255,9,8,7,255,12,11,10,255}),"Independent exact WPF BGRA oracle with forced opaque alpha");CryptographicOperations.ZeroMemory(pixels);fixture.Preserved();
            }
            using(var fixture=new SyntheticNativeDib())
            {
                string keyRoot=Path.Combine(root,"ctrl-v-vault");Directory.CreateDirectory(keyRoot);using var keyActive=new MemoApp.Core.Editing.SaveCoordinator(MemoApp.Core.Storage.EncryptedVault.Create(keyRoot,secret,secret),TimeProvider.System);var keyNote=keyActive.Workspace.CreateNote();Require(await keyActive.SaveAsync(),"CtrlV baseline");using var keyPanel=new AttachmentPanel(keyActive,keyNote,()=>true,_=>{});await NativeDibControlV(keyPanel,keyActive,keyNote);fixture.Preserved();
                keyPanel.FilesList.SelectedIndex=0;Require(keyPanel.FilesList.SelectedIndex==0,"Existing attachment selected before native DIB button");var selectedButton=(System.Windows.Controls.Button)typeof(AttachmentPanel).GetField("pastePng",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(keyPanel)!;selectedButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));var selectedElapsed=System.Diagnostics.Stopwatch.StartNew();while(keyNote.AttachmentIds.Length==1||keyActive.IsBusy||keyActive.IsDirty){Require(selectedElapsed.Elapsed<TimeSpan.FromSeconds(20),"Native DIB paste with selected existing attachment settles");await Task.Delay(10);}Require(keyNote.AttachmentIds.Length==2&&keyPanel.PreviewImage.Source is null,"Initial own Refresh permits existing selection while later selection changes remain revoked");fixture.Preserved();
            }
            string sequenceRoot=Path.Combine(root,"initial-sequence-vault");Directory.CreateDirectory(sequenceRoot);
            using(var sequenceActive=new MemoApp.Core.Editing.SaveCoordinator(MemoApp.Core.Storage.EncryptedVault.Create(sequenceRoot,secret,secret),TimeProvider.System))
            {
                var sequenceNote=sequenceActive.Workspace.CreateNote();Require(await sequenceActive.SaveAsync(),"OLE sequence baseline");using var sequencePanel=new AttachmentPanel(sequenceActive,sequenceNote,()=>true,_=>{});
                foreach(string boundary in new[]{"getter","presence","zero"})
                {
                    var sequenceBackend=new DelayedDibBackend();sequenceBackend.Release.Set();if(boundary=="zero")sequenceBackend.Native.Sequence=0;
                    Func<System.Windows.IDataObject?> query=boundary=="getter"?()=>{sequenceBackend.Native.Sequence++;return null;}:()=>new AbsentDibPngProvider(()=>{if(boundary=="presence")sequenceBackend.Native.Sequence++;});
                    Require(!await sequencePanel.ImportClipboardImageAsync(query,sequenceBackend)&&sequenceNote.AttachmentIds.Length==0&&sequenceBackend.Captures==0&&!sequenceBackend.Started.IsSet,"Initial clipboard sequence authority refuses OLE "+boundary+" change before capture or conversion");
                }
                var rawBackend=new DelayedDibBackend();rawBackend.Native.Sequence=0;rawBackend.Release.Set();Require(await sequencePanel.ImportClipboardImageAsync(()=>new System.Windows.DataObject("PNG",PreviewPng,false),rawBackend)&&sequenceNote.AttachmentIds.Length==1&&rawBackend.Captures==0&&!rawBackend.Started.IsSet,"Raw PNG path remains accepted when native sequence is zero");
            }
            string displayRoot=Path.Combine(root,"generated-display-lock-vault");Directory.CreateDirectory(displayRoot);
            using(var fixture=new SyntheticNativeDib())
            using(var displayActive=new MemoApp.Core.Editing.SaveCoordinator(MemoApp.Core.Storage.EncryptedVault.Create(displayRoot,secret,secret),TimeProvider.System))
            {
                var displayNote=displayActive.Workspace.CreateNote();Require(await displayActive.SaveAsync(),"Generated display lock baseline");using var displayPanel=new AttachmentPanel(displayActive,displayNote,()=>true,_=>{});Require(await displayPanel.ImportClipboardImageAsync(System.Windows.Clipboard.GetDataObject),"Native DIB display-lock capture");displayPanel.FilesList.SelectedIndex=0;Require(await displayPanel.PreviewSelectedAsync()&&displayPanel.PreviewImage.Source is not null,"Generated DIB PNG actually displayed before lock");await displayActive.LockAsync();Require(displayPanel.PreviewImage.Source is null&&displayActive.KeysReleased,"Lock clears actual generated DIB PNG display and releases keys");fixture.Preserved();
            }
            panel.FilesList.SelectedIndex=-1;
            var backend=new DelayedDibBackend();Task<bool> pending=panel.ImportClipboardImageAsync(()=>null,backend);while(!backend.Started.IsSet)await Task.Delay(5);Require(!await panel.ImportClipboardImageAsync(()=>throw new Exception("No concurrent clipboard query")),"DIB conversion holds global image admission");backend.ChangeAfterWorker=true;backend.Release.Set();Require(!await pending&&note.AttachmentIds.Length==1&&backend.Owned.All(b=>b.All(v=>v==0)),"Worker sequence change refuses application and clears owned DIB");
            foreach(string boundary in new[]{"source-edit","accepted-snapshot","selection-away-back"})
            {
                backend=new();pending=panel.ImportClipboardImageAsync(()=>null,backend);while(!backend.Started.IsSet)await Task.Delay(5);
                if(boundary=="source-edit")note.Title+=" changed";
                else if(boundary=="accepted-snapshot"){long oldVersion=note.EditVersion;active.Workspace.AcceptPrepared(active.Workspace.Capture());Require(note.EditVersion==oldVersion,"Synthetic accepted snapshot keeps edit version");}
                else{panel.FilesList.SelectedIndex=0;panel.FilesList.SelectedIndex=-1;}
                backend.Release.Set();Require(!await pending&&note.AttachmentIds.Length==1&&backend.Owned.All(b=>b.All(v=>v==0)),"DIB late worker result revoked by "+boundary);Require(await active.SaveAsync(),"Revocation fixture baseline saved");
            }
            foreach(int boundary in new[]{6,7})
            {backend=new(){ChangeAtSequenceRead=boundary};backend.Release.Set();Require(!await panel.ImportClipboardImageAsync(()=>null,backend)&&note.AttachmentIds.Length==1&&backend.Owned.All(b=>b.All(v=>v==0)),"Sequence change after preparation/final attach boundary refuses candidate");}
            active.Workspace.ConvertMode(note,"rich",true);Require(await active.SaveAsync(),"DIB later H01 rich baseline");panel.SetInlineImageInsertion((id,version,allowed)=>allowed()?active.InsertInlineImageAsync(note,id,1,version):Task.FromResult(false),()=>true,()=>{});panel.FilesList.SelectedIndex=0;Guid generatedId=note.AttachmentIds.Single();Require(await panel.InsertSelectedInlineImageAsync()&&MemoApp.Core.Documents.RichDocumentCodec.Images(note.Document!).Single().AttachmentId==generatedId&&note.AttachmentIds.Length==1,"Later explicit H01 panel action references generated authenticated PNG without duplicate attachment application");Require(await active.SaveAsync(),"DIB H01 image reference encrypted save");panel.FilesList.SelectedIndex=-1;
            string faultRoot=Path.Combine(root,"save-failure-vault");Directory.CreateDirectory(faultRoot);var faultFiles=new PreviewFaultFiles();
            try
            {
                using var faultActive=new MemoApp.Core.Editing.SaveCoordinator(MemoApp.Core.Storage.EncryptedVault.Create(faultRoot,secret,secret,faultFiles),TimeProvider.System);var faultNote=faultActive.Workspace.CreateNote();Require(await faultActive.PrepareAttachmentsAsync()&&await faultActive.SaveAsync(),"DIB failed-save anchored baseline");string report="";using var faultPanel=new AttachmentPanel(faultActive,faultNote,()=>true,text=>report=text);var faultBackend=new DelayedDibBackend();faultBackend.Release.Set();faultFiles.Fail=true;faultFiles.Release.Set();Require(!await faultPanel.ImportClipboardImageAsync(()=>null,faultBackend)&&faultNote.AttachmentIds.Length==1&&faultActive.IsDirty&&report.Contains("저장 완료는 확인하지 못했습니다"),"DIB attachment stays edited and reports actual save failure without claiming rollback");await faultActive.LockAsync();Require(faultActive.KeysReleased,"Failed DIB save locks and releases keys");
            }
            finally{faultFiles.Release.Set();faultFiles.Entered.Dispose();faultFiles.Release.Dispose();}
            backend=new();pending=panel.ImportClipboardImageAsync(()=>null,backend);while(!backend.Started.IsSet)await Task.Delay(5);Task locking=active.LockAsync();Require(!ImagePreviewAdmission.TryEnter(panel.Dispatcher),"Lock retains admission while worker actually runs");backend.Release.Set();Require(!await pending,"Lock refuses late DIB result");await locking;Require(active.KeysReleased,"DIB lock releases session keys");Require(ImagePreviewAdmission.TryEnter(panel.Dispatcher),"Worker settlement releases image slot");ImagePreviewAdmission.Exit();
            using var reopened=new MemoApp.Core.Editing.SaveCoordinator(MemoApp.Core.Storage.EncryptedVault.Open(root,secret),TimeProvider.System);var restored=reopened.Workspace.Notes.Single();Require(restored.AttachmentIds.Length==1&&MemoApp.Core.Documents.RichDocumentCodec.Images(restored.Document!).Single().AttachmentId==restored.AttachmentIds.Single(),"Encrypted restart retains one later explicit H01 image reference and one generated attachment");byte[] persisted=(byte[])typeof(MemoApp.Core.Editing.SaveCoordinator).GetMethod("ReadAttachmentBytes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(reopened,[restored,restored.AttachmentIds.Single(),restored.EditVersion])!;try{Require(persisted.SequenceEqual(png!),"Independent encrypted reopen preserves generated PNG bytes");}finally{CryptographicOperations.ZeroMemory(persisted);}
        }
        finally{if(png is not null)CryptographicOperations.ZeroMemory(png);CryptographicOperations.ZeroMemory(secret);Directory.Delete(root,true);}
    }
}
