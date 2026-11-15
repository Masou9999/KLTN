using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Multiplayer;

// Run the production service with controllable SDK/network boundaries, without credentials.
static class Program
{
    static readonly UgsSessionService Service = UgsSessionService.Instance;
    static readonly FakeMultiplayer Sdk = MultiplayerService.Instance;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static async Task Reject(Task task)
    {
        try { await task; } catch (InvalidOperationException) { return; }
        throw new Exception("Expected rejection");
    }
    static async Task Main()
    {
        int ended = 0;
        Service.HostSessionEnded += () => ended++;
        var host = new FakeSession(true);
        Sdk.Next = Task.FromResult<ISession>(host);
        await Service.HostAsync();
        await Service.LeaveAsync();
        Check(host.Deletes == 1 && host.Leaves == 0 && !Service.HasSession && ended == 0, "Host cleanup");
        Console.WriteLine("PASS host deletes room without reporting remote closure");

        var guest = new FakeSession(false);
        Sdk.Next = Task.FromResult<ISession>(guest);
        await Service.JoinByCodeAsync(" CODE ");
        await Service.LeaveAsync(); await Service.LeaveAsync();
        Check(guest.Leaves == 1 && guest.Deletes == 0 && !Service.HasSession, "Guest cleanup");
        Console.WriteLine("PASS guest leaves; repeated cleanup is idempotent");

        foreach (bool isHost in new[] { true, false })
        {
            var pending = new TaskCompletionSource<ISession>(TaskCreationOptions.RunContinuationsAsynchronously);
            Sdk.Next = pending.Task;
            Task start = isHost ? Service.HostAsync() : Service.JoinByCodeAsync("CODE");
            await Reject(Service.HostAsync());
            await Reject(Service.JoinByCodeAsync("CODE"));
            Task leave = Service.LeaveAsync();
            Check(!leave.IsCompleted, "Leave must wait for Create/Join");
            var late = new FakeSession(isHost);
            pending.SetResult(late);
            await start; await leave;
            Check(!Service.HasSession && late.Deletes + late.Leaves == 1, "Late session leaked");
            Console.WriteLine($"PASS concurrent start rejected; leave cleans late {(isHost ? "host" : "guest")}");
        }

        host = new FakeSession(true) { Fail = true };
        Sdk.Next = Task.FromResult<ISession>(host);
        await Service.HostAsync(); await Reject(Service.LeaveAsync());
        Check(Service.HasSession, "Lost retry handle");
        int creates = Sdk.Creates;
        await Reject(Service.HostAsync());
        Check(Sdk.Creates == creates, "Created before cleanup succeeded");
        host.Fail = false; await Service.LeaveAsync();
        Check(!Service.HasSession, "Retry failed");
        Console.WriteLine("PASS failed delete retains handle and blocks new room until retry succeeds");

        var deletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        host = new FakeSession(true) { Cleanup = deletion.Task };
        Sdk.Next = Task.FromResult<ISession>(host);
        await Service.HostAsync();
        Task firstLeave = Service.LeaveAsync();
        Task secondLeave = Service.LeaveAsync();
        await Reject(Service.HostAsync());
        Check(!firstLeave.IsCompleted && !secondLeave.IsCompleted, "Overlapping leaves must wait");
        deletion.SetResult(true);
        await Task.WhenAll(firstLeave, secondLeave);
        Check(host.Deletes == 1 && !Service.HasSession, "Duplicate deletion");
        Console.WriteLine("PASS simultaneous leaves share serialized cleanup; create cannot bypass it");

        var manager = new NetworkManager { IsListening = true };
        NetworkManager.Singleton = manager;
        Task stop = Service.LeaveAsync();
        Check(!stop.IsCompleted && manager.Shutdowns == 1, "Did not await NGO shutdown");
        manager.IsListening = false; manager.ShutdownInProgress = false;
        await stop; NetworkManager.Singleton = null;
        Console.WriteLine("PASS cleanup waits for deferred NGO shutdown");

        guest = new FakeSession(false); Sdk.Next = Task.FromResult<ISession>(guest);
        await Service.JoinByCodeAsync("CODE"); guest.RemoteDelete();
        Check(!Service.HasSession && ended == 1, "Remote closure");
        await Service.LeaveAsync();
        host = new FakeSession(true); Sdk.Next = Task.FromResult<ISession>(host);
        await Service.HostAsync(); await Service.LeaveAsync();
        Check(host.Deletes == 1 && !Service.HasSession, "Stale second match");
        Console.WriteLine("PASS remote deletion then fresh room lifecycle");
    }
}

namespace Unity.Netcode
{
    public class NetworkManager
    {
        public static NetworkManager Singleton;
        public volatile bool IsListening, ShutdownInProgress;
        public int Shutdowns;
        public void Shutdown() { Shutdowns++; ShutdownInProgress = true; }
    }
}
namespace Unity.Services.Multiplayer
{
    public interface ISession
    {
        bool IsHost { get; } int PlayerCount { get; } string Id { get; } string Code { get; }
        event Action<string> PlayerJoined; event Action<string> PlayerHasLeft;
        event Action Deleted; event Action RemovedFromSession;
        FakeSession AsHost(); Task LeaveAsync();
    }
    public class SessionOptions
    {
        public string Type, Name; public int MaxPlayers; public bool IsPrivate, IsLocked;
        public SessionOptions WithRelayNetwork() => this;
    }
    public class JoinSessionOptions { public string Type; }
    public class ReconnectSessionOptions { public string Type; }
    public class SessionException : Exception { }
    public static class MultiplayerService { public static FakeMultiplayer Instance { get; } = new(); }
    public class FakeMultiplayer
    {
        public Task<ISession> Next; public int Creates;
        public Task<ISession> CreateSessionAsync(SessionOptions options) { Creates++; return Next; }
        public Task<ISession> JoinSessionByCodeAsync(string code, JoinSessionOptions options) => Next;
        public Task<List<string>> GetJoinedSessionIdsAsync() => Task.FromResult(new List<string>());
        public Task<ISession> ReconnectToSessionAsync(string id, ReconnectSessionOptions options) => Next;
    }
    public class FakeSession : ISession
    {
        public FakeSession(bool host) { IsHost = host; }
        public bool IsHost { get; } public int PlayerCount => 1;
        public string Id => "session"; public string Code => "CODE";
        public int Deletes, Leaves; public bool Fail;
        public Task Cleanup = Task.CompletedTask;
        public event Action<string> PlayerJoined { add { } remove { } }
        public event Action<string> PlayerHasLeft { add { } remove { } }
        public event Action Deleted; public event Action RemovedFromSession;
        public FakeSession AsHost() => this;
        public async Task DeleteAsync() { Deletes++; if (Fail) throw new InvalidOperationException("offline"); await Cleanup; Deleted?.Invoke(); }
        public Task LeaveAsync() { Leaves++; if (Fail) throw new InvalidOperationException("offline"); RemovedFromSession?.Invoke(); return Task.CompletedTask; }
        public void RemoteDelete() => Deleted?.Invoke();
    }
}
