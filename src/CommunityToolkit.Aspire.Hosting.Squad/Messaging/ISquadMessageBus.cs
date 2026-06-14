using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

public interface ISquadMessageBus
{
    Task SendAsync(SquadMessage message, CancellationToken ct = default);
    Task<SquadMessage> ReplyAsync(string originalMessageId, string fromSquad, string body, CancellationToken ct = default);
    Task<IReadOnlyList<SquadMessage>> GetInboxAsync(string squadName, bool unreadOnly = false, CancellationToken ct = default);
    Task<IReadOnlyList<SquadMessage>> GetConversationAsync(string correlationId, CancellationToken ct = default);
    Task MarkReadAsync(string messageId, CancellationToken ct = default);
    IAsyncEnumerable<SquadMessage> SubscribeAsync(string squadName, CancellationToken ct = default);
}
