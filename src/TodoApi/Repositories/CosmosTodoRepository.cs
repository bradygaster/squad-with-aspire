using System.Net;
using Microsoft.Azure.Cosmos;
using TodoApi.Models;

namespace TodoApi.Repositories;

public class CosmosTodoRepository : ITodoRepository
{
    private readonly CosmosClient _cosmosClient;

    public CosmosTodoRepository(CosmosClient cosmosClient)
    {
        _cosmosClient = cosmosClient;
    }

    private async Task<Container> GetContainerAsync()
    {
        var database = await _cosmosClient.CreateDatabaseIfNotExistsAsync("TodoDb");
        var container = await database.Database.CreateContainerIfNotExistsAsync(
            "Todos", "/userId", 400);
        return container.Container;
    }

    public async Task<IEnumerable<TodoItem>> GetAllAsync(string userId)
    {
        var container = await GetContainerAsync();
        var query = new QueryDefinition("SELECT * FROM c WHERE c.userId = @userId ORDER BY c.createdAt DESC")
            .WithParameter("@userId", userId);

        var items = new List<TodoItem>();
        using var iterator = container.GetItemQueryIterator<TodoItem>(query);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            items.AddRange(response);
        }
        return items;
    }

    public async Task<TodoItem?> GetByIdAsync(string id, string userId)
    {
        var container = await GetContainerAsync();
        try
        {
            var response = await container.ReadItemAsync<TodoItem>(id, new PartitionKey(userId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<TodoItem> CreateAsync(TodoItem item)
    {
        var container = await GetContainerAsync();
        var response = await container.CreateItemAsync(item, new PartitionKey(item.UserId));
        return response.Resource;
    }

    public async Task<TodoItem> UpdateAsync(TodoItem item)
    {
        var container = await GetContainerAsync();
        var response = await container.ReplaceItemAsync(item, item.Id, new PartitionKey(item.UserId));
        return response.Resource;
    }

    public async Task<bool> DeleteAsync(string id, string userId)
    {
        var container = await GetContainerAsync();
        try
        {
            await container.DeleteItemAsync<TodoItem>(id, new PartitionKey(userId));
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
