using System.Reflection;
using System.Collections.Immutable;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using MemoApp.Core.Documents;
using MemoApp.Core.Editing;
using MemoApp.Core.Storage;

internal static class WordImageContextChecks
{
    internal static async Task Run()
    {
        var assembly = typeof(SaveCoordinator).Assembly;
        Require(assembly.GetType("MemoApp.Core.Transfer.OwnedWordImageContext") is not null,
            "Closed authenticated owned Word image context is missing");
        Require(typeof(SaveCoordinator).GetMethod("CaptureWordImageContext", BindingFlags.Instance | BindingFlags.NonPublic) is not null,
            "Original-owner authenticated Word capture API is missing");
        var contextType = assembly.GetType("MemoApp.Core.Transfer.OwnedWordImageContext")!;
        Require(!contextType.IsPublic && contextType.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length == 0,
            "Closed context has no public construction path");
        foreach (var type in new[] { contextType }.Concat(assembly.GetTypes().Where(t => t.Name is "WordNoteSource" or "WordImageDescriptor" or "WordImagePlacement")))
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                Require(!typeof(Delegate).IsAssignableFrom(field.FieldType) && field.FieldType != typeof(SaveCoordinator)
                    && field.FieldType != typeof(EncryptedVault) && field.FieldType != typeof(EditingWorkspace)
                    && field.FieldType != typeof(NoteDraft) && !field.FieldType.FullName!.StartsWith("System.Windows", StringComparison.Ordinal),
                    "Compiled worker context contains no owner/key/UI/callback field");
        string root = Path.Combine(Path.GetTempPath(), "memo-word-context-" + Guid.NewGuid().ToString("N"));
        byte[] secret = EncryptedVault.GenerateRecoverySecret();
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAEElEQVR4AQEFAPr/AAECAwQAGQALubDj6wAAAABJRU5ErkJggg==");
        try
        {
            using var owner = new SaveCoordinator(EncryptedVault.Create(root, secret, secret), TimeProvider.System);
            var note = owner.Workspace.CreateNote(); note.Title = "합성 Word 원본"; note.Text = "원본 글자";
            Require(await owner.PrepareAttachmentsAsync(), "Actual anchored encrypted root prepared");
            Guid objectId = owner.AttachBytes(note, png, "synthetic.png", "image/png", note.EditVersion);
            owner.Workspace.ConvertMode(note, "rich", true);
            Call(owner.Workspace, "InsertInlineImage", [note, objectId, 1, "합성 그림"]);
            Call(owner.Workspace, "InsertInlineImage", [note, objectId, 2, "같은 그림 두번째"]);
            var second = owner.Workspace.Duplicate(note);
            Require(await owner.SaveAsync(), "Actual PNG v2 source encrypted save");
            int ownerThread = Environment.CurrentManagedThreadId;
            Func<bool> checkAccess = () => Environment.CurrentManagedThreadId == ownerThread;
            Call(owner, "RegisterWordImageExportOwner", [checkAccess]);
            Fails<InvalidOperationException>(() => Call(owner, "RegisterWordImageExportOwner", [(Func<bool>)(() => true)]));
            string before = JsonSerializer.Serialize(owner.Workspace.Capture());
            byte[] cipher = File.ReadAllBytes(Path.Combine(root, "current.vault"));
            long version = note.EditVersion;
            object Capture(NoteDraft[] notes, CancellationToken token = default, Action<byte[]>? allocations = null) =>
                Call(owner, "CaptureWordImageContext", [notes, notes.Select(n => n.EditVersion).ToArray(), owner.AttachmentPreviewEpoch, token, allocations])!;
            bool Current(object context) => (bool)Call(owner, "IsWordImageContextCurrent", [context])!;
            var observed = new List<byte[]>();
            using (var context = (IDisposable)Capture([note, second], allocations: observed.Add))
            {
                foreach (Action forbidden in new Action[]
                {
                    () => Capture([note]), () => Current(context),
                    () => Call(owner, "RetireWordImageContext", [context]),
                    () => Call(owner, "SettleWordImageContext", [context]),
                    () => Call(owner, "RegisterWordImageExportOwner", [checkAccess])
                })
                {
                    Exception? refused = null; int foreignId = 0;
                    var thread = new Thread(() => { foreignId = Environment.CurrentManagedThreadId; try { forbidden(); } catch (Exception error) { refused = error; } });
                    thread.Start(); Require(thread.Join(TimeSpan.FromSeconds(10)), "Foreign thread ended");
                    Require(foreignId != ownerThread && refused is InvalidOperationException && Current(context)
                        && observed[0].SequenceEqual(png), "Real foreign thread denied before owner registry/revocation mutation");
                }
                Require(Current(context), "Original captured owner/selection source is current");
                Require(Count(context, "Notes") == 2 && Count(context, "Images") == 1 && Count(context, "Placements") == 4,
                    "Shared repeated placements deduplicate one exact authenticated media object");
                Require(observed.Count == 1 && observed[0].SequenceEqual(png) && owner.WhenAttachmentReadsIdle.IsCompleted,
                    "Same consumed authenticated lease copied once, read slots drained");
                Fails<InvalidOperationException>(() => Capture([note]));
                Call(context, "BeginBuild", [Task.CompletedTask, Task.CompletedTask]);
                byte[] borrowed = [];
                Read(context, objectId, bytes => borrowed = bytes.ToArray());
                Require(borrowed.SequenceEqual(png), "Worker reads exact original PNG without transcoding");
                context.Dispose();
                Require(!Current(context) && observed[0].SequenceEqual(png) && !(bool)Property(context, "IsSettled")!,
                    "Revocation holds active bytes and admission until actual worker teardown");
                Fails<InvalidOperationException>(() => Capture([note]));
                Require(!(bool)Call(context, "CompleteBuild", [])! && observed.All(b => b.All(v => v == 0)),
                    "Canceled build returns no success and zeroes original PNG after teardown");
                Require((bool)Property(context, "IsSettled")!, "Completed terminal tasks and teardown settle canceled context");
                Call(owner, "SettleWordImageContext", [context]);
            }
            using (var context = (IDisposable)Capture([note]))
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Call(context, "BeginBuild", [completion.Task, settled.Task]);
                Read(context, objectId, bytes =>
                {
                    context.Dispose();
                    Require(bytes.SequenceEqual(png), "Self-disposal does not mutate a borrowed image span");
                    Fails<InvalidOperationException>(() => Read(context, objectId, _ => { }));
                    Fails<InvalidOperationException>(() => Call(context, "CompleteBuild", []));
                });
                Call(context, "CompleteBuild", []);
                Require(!(bool)Property(context, "IsSettled")!, "Buffer cleanup alone cannot release unfinished actual operation");
                Fails<InvalidOperationException>(() => Call(owner, "SettleWordImageContext", [context]));
                Fails<InvalidOperationException>(() => Capture([note]));
                completion.SetResult();
                Require(!(bool)Property(context, "IsSettled")!, "Terminal Completion alone cannot release unsettled worker");
                settled.SetResult();
                Call(owner, "SettleWordImageContext", [context]);
            }
            observed.Clear();
            using (var context = (IDisposable)Capture([note], allocations: observed.Add))
            {
                Call(context, "BeginBuild", [Task.CompletedTask, Task.CompletedTask]);
                Fails<IOException>(() => Read(context, objectId, _ => throw new IOException("Synthetic media consumer failure")));
                context.Dispose(); Call(context, "CompleteBuild", []);
                Require(observed.All(b => b.All(v => v == 0)), "Throwing media reader bytes cleared at actual build teardown");
                Call(owner, "SettleWordImageContext", [context]);
            }
            foreach (bool cancel in new[] { false, true })
            {
                observed.Clear(); using var token = new CancellationTokenSource();
                Action<byte[]> failure = bytes => { observed.Add(bytes); if (cancel) token.Cancel(); else throw new IOException("Synthetic capture allocation failure"); };
                if (cancel) Fails<OperationCanceledException>(() => Capture([note], token.Token, failure));
                else Fails<IOException>(() => Capture([note], token.Token, failure));
                Require(observed.Count == 1 && observed.All(b => b.All(v => v == 0)) && owner.WhenAttachmentReadsIdle.IsCompleted,
                    "Capture allocation observer/cancellation clears all exact buffers and lease slots");
            }
            using (var token = new CancellationTokenSource())
            { token.Cancel(); Fails<OperationCanceledException>(() => Capture([note], token.Token)); }
            using (var token = new CancellationTokenSource())
            using (var context = (IDisposable)Capture([note], token.Token))
            {
                token.Cancel(); Require(!Current(context), "Original capture cancellation cannot renew owner write authority");
            }
            Fails<ArgumentException>(() => Capture([note, note]));
            Fails<ArgumentException>(() => Capture([]));
            Fails<ArgumentException>(() => Capture(Enumerable.Repeat(note, 101).ToArray()));
            Fails<InvalidOperationException>(() => Call(owner, "CaptureWordImageContext", [new[] { note }, new[] { version - 1 }, owner.AttachmentPreviewEpoch, default(CancellationToken), null]));
            Fails<InvalidOperationException>(() => Call(owner, "CaptureWordImageContext", [new[] { note }, new[] { version }, owner.AttachmentPreviewEpoch - 1, default(CancellationToken), null]));
            Require(note.EditVersion == version && !owner.IsDirty && JsonSerializer.Serialize(owner.Workspace.Capture()) == before
                && File.ReadAllBytes(Path.Combine(root, "current.vault")).SequenceEqual(cipher),
                "Capture/build ownership paths leave exact source/history/OCR/objects/ciphertext clean and unchanged");
            // Genuine fixed detached worker, rather than synthetic terminal Tasks, determines registry cleanup.
            using (var context = (IDisposable)Capture([note]))
            {
                var export = assembly.GetType("MemoApp.Core.Transfer.StructuredWordExport")!;
                object operation = CallStatic(export, "StartOwned", [context, default(CancellationToken)])!;
                var actualCompletion = (Task)Property(operation, "Completion")!;
                var actualSettled = (Task)Property(operation, "Settled")!;
                actualCompletion.GetAwaiter().GetResult(); actualSettled.GetAwaiter().GetResult();
                using var package = (IDisposable)actualCompletion.GetType().GetProperty("Result")!.GetValue(actualCompletion)!;
                Require(Current(context) && (bool)Property(context, "IsSettled")!,
                    "Real static decoder/package worker settled while original unwritten export still has owner authority");
                Fails<InvalidOperationException>(() => Capture([second]));
                Call(owner, "SettleWordImageContext", [context]);
                Require(!Current(context), "Explicit owner retirement ends ready-result authority before replacement capture");
            }
            var valid = owner.Workspace.Capture();
            foreach (bool foreignRoot in new[] { false, true })
            {
                Guid rootId = foreignRoot ? Guid.NewGuid() : valid.AttachmentRootId;
                var changed = valid with
                {
                    AttachmentRootId = rootId,
                    AttachmentObjects = valid.AttachmentObjects.Select(o => foreignRoot ? o with { RootId = rootId } : o with { Sha256 = new string('0', 64) }).ToImmutableArray()
                };
                owner.Workspace.AcceptPrepared(changed); observed.Clear();
                Fails<InvalidDataException>(() => Capture([note], allocations: observed.Add));
                Require(observed.Count == 0 && owner.WhenAttachmentReadsIdle.IsCompleted,
                    "Actual-root mismatch or authenticated object tampering refuses before any copied media");
                owner.Workspace.AcceptPrepared(valid);
            }
            using (var held = (IDisposable)Capture([note]))
            {
                observed.Clear();
                Fails<InvalidOperationException>(() => Capture([second], allocations: observed.Add));
                Require(observed.Count == 0 && Current(held), "Cap1 refusal precedes decrypt/media allocation and cannot steal source authority");
            }
            using (var stale = (IDisposable)Capture([note]))
            {
                owner.Workspace.AcceptPrepared(owner.Workspace.Capture());
                Require(!Current(stale), "Same source values cannot renew captured original source epoch");
            }
            UniquePixelCaptureLimit(Path.Combine(root, "pixel-budget-synthetic"), secret, checkAccess);
            using (var context = (IDisposable)Capture([note]))
            {
                Require(owner.LockAsync().GetAwaiter().GetResult(), "Actual encrypted owner lock");
                Require(!Current(context), "Closed owner revokes detached unused context");
                Fails<InvalidOperationException>(() => Capture([second]));
                string reopenRoot = Path.Combine(root, "reopened-synthetic");
                string[] durable = Directory.GetFiles(root, "*.vault"); Directory.CreateDirectory(reopenRoot);
                foreach (string file in durable) File.Copy(file, Path.Combine(reopenRoot, Path.GetFileName(file)));
                using var reopened = new SaveCoordinator(EncryptedVault.Open(reopenRoot, secret), TimeProvider.System);
                Call(reopened, "RegisterWordImageExportOwner", [checkAccess]);
                Require(reopened.Workspace.Notes.Any(n => n.Id == note.Id) && !(bool)Call(reopened, "IsWordImageContextCurrent", [context])!,
                    "Same original vault/note/object reopened in new coordinator cannot replay old context");
                Fails<InvalidOperationException>(() => Call(context, "BeginBuild", [Task.CompletedTask, Task.CompletedTask]));
            }
            Console.WriteLine("PASS: N04 closed authenticated Word context, exact same-lease PNG/placements, cap1 true-settlement ownership, cancellation/reentrant cleanup and original source/cipher invariance");
        }
        finally { CryptographicOperations.ZeroMemory(secret); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static object? Call(object target, string method, object?[] arguments)
    {
        try { return target.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, arguments); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
    }
    private static object? CallStatic(Type type, string method, object?[] arguments)
    {
        try { return type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
    }
    private static object? Property(object target, string name) => target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
    private static int Count(object target, string name) => ((System.Collections.IEnumerable)Property(target, name)!).Cast<object>().Count();
    private static void Read(object target, Guid id, AttachmentByteReader reader) => Call(target, "ReadImage", [id, reader]);
    private static void Fails<T>(Action action) where T : Exception
    { bool failed = false; try { action(); } catch (T) { failed = true; } Require(failed, "Expected " + typeof(T).Name); }
    private static void UniquePixelCaptureLimit(string root, byte[] secret, Func<bool> checkAccess)
    {
        // Each independently encoded source has 4,194,304 valid RGBA pixels, yet compresses far below4MiB.
        byte[] png = LargeValidPng();
        using var owner = new SaveCoordinator(EncryptedVault.Create(root, secret, secret), TimeProvider.System);
        var note = owner.Workspace.CreateNote(); note.Text = "합성 CPU 한도";
        Require(owner.PrepareAttachmentsAsync().GetAwaiter().GetResult(), "Pixel-limit fixture actual root");
        owner.Workspace.ConvertMode(note, "rich", true);
        for (int i = 0; i < 16; i++)
        {
            Guid id = owner.AttachBytes(note, png, "synthetic-large-" + i + ".png", "image/png", note.EditVersion);
            Call(owner.Workspace, "InsertInlineImage", [note, id, i + 1, "합성 큰 그림 " + i]);
        }
        Require(owner.SaveAsync().GetAwaiter().GetResult(), "Pixel-limit fixture actual encrypted save");
        Call(owner, "RegisterWordImageExportOwner", [checkAccess]);
        string before = JsonSerializer.Serialize(owner.Workspace.Capture());
        byte[] cipher = File.ReadAllBytes(Path.Combine(root, "current.vault")); var observed = new List<byte[]>();
        bool refused = false;
        try { Call(owner, "CaptureWordImageContext", [new[] { note }, new[] { note.EditVersion }, owner.AttachmentPreviewEpoch, default(CancellationToken), (Action<byte[]>)observed.Add]); }
        catch (InvalidDataException error) { refused = error.Message == "Word unique PNG source pixel budget"; }
        Require(refused && observed.Count == 15 && observed.All(b => b.All(v => v == 0)) && owner.WhenAttachmentReadsIdle.IsCompleted,
            "Sixteenth unique valid source exceeds64M before worker launch, all fifteen owned copies and leases clear");
        Require(JsonSerializer.Serialize(owner.Workspace.Capture()) == before && !owner.IsDirty
            && File.ReadAllBytes(Path.Combine(root, "current.vault")).SequenceEqual(cipher), "Pixel budget refusal leaves source/ciphertext exact");
    }
    private static byte[] LargeValidPng()
    {
        using var output = new MemoryStream(); output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        byte[] header = new byte[13]; BinaryPrimitives.WriteUInt32BigEndian(header, 2048); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 2048); header[8] = 8; header[9] = 6;
        Part("IHDR"u8, header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true))
        { byte[] row = new byte[1 + 2048 * 4]; for (int i = 0; i < 2048; i++) zlib.Write(row); }
        Part("IDAT"u8, compressed.ToArray()); Part("IEND"u8, []); return output.ToArray();
        void Part(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length); output.Write(number); output.Write(type); output.Write(data);
            uint crc = uint.MaxValue;
            foreach (byte value in type) Update(value); foreach (byte value in data) Update(value);
            BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); output.Write(number);
            void Update(byte value) { crc ^= value; for (int i = 0; i < 8; i++) crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320u; }
        }
    }
}
