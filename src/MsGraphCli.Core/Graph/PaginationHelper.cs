using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions.Serialization;

namespace MsGraphCli.Core.Graph;

/// <summary>
/// Collects items from paginated Graph API responses using PageIterator.
/// </summary>
public static class PaginationHelper
{
    /// <summary>
    /// Iterates through all pages of a message collection, collecting up to maxItems.
    /// </summary>
    public static async Task<List<Message>> CollectMessagesAsync(
        GraphServiceClient client,
        MessageCollectionResponse? firstPage,
        int? maxItems,
        CancellationToken cancellationToken)
    {
        if (firstPage?.Value is null)
        {
            return [];
        }

        var items = new List<Message>();
        var pageIterator = PageIterator<Message, MessageCollectionResponse>.CreatePageIterator(
            client,
            firstPage,
            message =>
            {
                items.Add(message);
                return !maxItems.HasValue || items.Count < maxItems.Value;
            });

        await pageIterator.IterateAsync(cancellationToken);
        return items;
    }

    /// <summary>
    /// Iterates through all pages of an event collection, collecting up to maxItems.
    /// </summary>
    public static async Task<List<Event>> CollectEventsAsync(
        GraphServiceClient client,
        EventCollectionResponse? firstPage,
        int? maxItems,
        CancellationToken cancellationToken)
    {
        if (firstPage?.Value is null)
        {
            return [];
        }

        var items = new List<Event>();
        var pageIterator = PageIterator<Event, EventCollectionResponse>.CreatePageIterator(
            client,
            firstPage,
            evt =>
            {
                items.Add(evt);
                return !maxItems.HasValue || items.Count < maxItems.Value;
            });

        await pageIterator.IterateAsync(cancellationToken);
        return items;
    }
}
