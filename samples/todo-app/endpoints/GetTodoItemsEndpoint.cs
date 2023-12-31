using Microsoft.AspNetCore.Mvc;

namespace Cosmodust.Samples.TodoApp.Endpoints;

[ApiController]
public class GetTodoItemsEndpoint : ControllerBase
{
    [HttpGet("api/accounts/{ownerId}/lists/{listId}/items")]
    public async Task Get(
        string ownerId,
        string listId,
        [FromServices] QueryFacade queryFacade)
    {
        Response.ContentType = "application/json; charset=utf-8";

        await queryFacade.ExecuteQueryAsync(
            pipeWriter: Response.BodyWriter,
            containerName: "todo",
            partitionKey: ownerId,
            sql: @"select * from c where c._type = 'TodoItem' and c.listId = @listId",
            parameters: new { listId = listId });
    }
}
