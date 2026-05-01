using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using ProctorLti.Api.Services;

namespace ProctorLti.Api.Hubs;

public sealed class ProctorHub(LaunchSessionStore sessions, ILogger<ProctorHub> log) : Hub
{
    private static readonly ConcurrentDictionary<string, StudentEntry> ByConnection = new();
    private static readonly ConcurrentDictionary<string, StudentEntry> BySession = new();

    public async Task JoinProctor(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId))
            throw new HubException("roomId required");

        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(roomId)).ConfigureAwait(false);

        foreach (var entry in BySession.Values)
        {
            if (entry.RoomId != roomId)
                continue;
            await Clients.Caller.SendAsync("StudentJoined", entry.SessionId, entry.DisplayName).ConfigureAwait(false);
        }
    }

    public async Task RegisterStudent(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new HubException("sessionId required");

        var boot = sessions.TryGet(sessionId);
        if (boot is null)
            throw new HubException("Unknown or expired session");

        var roomId = boot.ProctorRoomId;
        var displayName = string.IsNullOrWhiteSpace(boot.UserName) ? "Student" : boot.UserName;

        if (BySession.TryGetValue(sessionId, out var prev) && prev.ConnectionId != Context.ConnectionId)
        {
            ByConnection.TryRemove(prev.ConnectionId, out _);
            await Groups.RemoveFromGroupAsync(prev.ConnectionId, RoomGroup(prev.RoomId)).ConfigureAwait(false);
            await Groups.RemoveFromGroupAsync(prev.ConnectionId, StudentGroup(sessionId)).ConfigureAwait(false);
            await Clients.Group(RoomGroup(prev.RoomId)).SendAsync("StudentLeft", sessionId).ConfigureAwait(false);
        }

        await RemoveStudentPresenceAsync(Context.ConnectionId, notifyRoom: false).ConfigureAwait(false);

        var entry = new StudentEntry(roomId, sessionId, displayName, Context.ConnectionId);
        ByConnection[Context.ConnectionId] = entry;
        BySession[sessionId] = entry;

        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(roomId)).ConfigureAwait(false);
        await Groups.AddToGroupAsync(Context.ConnectionId, StudentGroup(sessionId)).ConfigureAwait(false);

        await Clients.Group(RoomGroup(roomId)).SendAsync("StudentJoined", sessionId, displayName).ConfigureAwait(false);
    }

    public async Task QuizClosed(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;

        if (!BySession.TryGetValue(sessionId, out var entry) || entry.ConnectionId != Context.ConnectionId)
            return;

        await RemovePresenceCoreAsync(entry).ConfigureAwait(false);
    }

    public async Task SendControl(string sessionId, string command)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(command))
            return;

        command = command.Trim().ToLowerInvariant();
        if (command is not ("play" or "pause" or "stop"))
            return;

        await Clients.Group(StudentGroup(sessionId)).SendAsync("control", command).ConfigureAwait(false);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            await RemoveStudentPresenceAsync(Context.ConnectionId, notifyRoom: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Proctor hub disconnect cleanup failed");
        }

        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    private async Task RemoveStudentPresenceAsync(string connectionId, bool notifyRoom)
    {
        if (!ByConnection.TryRemove(connectionId, out var entry))
            return;

        BySession.TryRemove(entry.SessionId, out _);
        if (notifyRoom)
            await NotifyLeftAsync(entry).ConfigureAwait(false);
    }

    private async Task RemovePresenceCoreAsync(StudentEntry entry)
    {
        ByConnection.TryRemove(entry.ConnectionId, out _);
        BySession.TryRemove(entry.SessionId, out _);
        await NotifyLeftAsync(entry).ConfigureAwait(false);
    }

    private async Task NotifyLeftAsync(StudentEntry entry)
    {
        await Clients.Group(RoomGroup(entry.RoomId)).SendAsync("StudentLeft", entry.SessionId).ConfigureAwait(false);
    }

    private static string RoomGroup(string roomId) => $"room:{roomId}";

    private static string StudentGroup(string sessionId) => $"student:{sessionId}";

    private sealed record StudentEntry(string RoomId, string SessionId, string DisplayName, string ConnectionId);
}
