using System.Text.Json.Serialization;
using EventStore.Client;
using event_sourcing_sample_api.Domain;
using event_sourcing_sample_api.DTOs;
using event_sourcing_sample_api.Repositories;
using event_sourcing_sample_shared.Enums;
using event_sourcing_sample_shared.Models;
using Microsoft.AspNetCore.Http.Json;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

#pragma warning disable CS0618
try
{
    BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
}
catch (BsonSerializationException)
{
    // Já registrado
}
#pragma warning restore CS0618

var builder = WebApplication.CreateBuilder(args);

// Configuração de serialização JSON (converte enums como string)
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Configuração do KurrentDB / EventStoreDB (Fonte da Verdade)
builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("EventStore")
                           ?? "esdb://localhost:2113?tls=false";
    var settings = EventStoreClientSettings.Create(connectionString);
    return new EventStoreClient(settings);
});

// Configuração do MongoDB (Banco de Leitura / Projeções)
builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("MongoDb")
                           ?? "mongodb://localhost:27017";
    return new MongoClient(connectionString);
});

builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var client = sp.GetRequiredService<IMongoClient>();
    var databaseName = configuration["MongoDb:DatabaseName"] ?? "todosdb";
    return client.GetDatabase(databaseName);
});

// Repositório de escrita baseado em Event Sourcing
builder.Services.AddScoped<ITodoRepository, TodoRepository>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var todosApi = app.MapGroup("/todos")
                  .WithTags("Todos");

// 1. Criar Todo
todosApi.MapPost("/", async (CreateTodoRequest request, ITodoRepository repository, CancellationToken ct) =>
{
    try
    {
        var id = request.Id ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var externalId = request.ExternalId.HasValue && request.ExternalId.Value != Guid.Empty
            ? request.ExternalId.Value
            : Guid.NewGuid();

        var status = request.Status ?? TodoStatus.Pending;

        var aggregate = TodoAggregate.Create(id, externalId, request.Description, status);
        await repository.SaveAsync(aggregate, ct);

        var response = new TodoResponse(
            aggregate.Id,
            aggregate.ExternalId,
            aggregate.Description,
            aggregate.Status,
            aggregate.IsDone,
            aggregate.IsDeleted,
            aggregate.CreatedAt
        );

        return Results.Created($"/todos/{aggregate.Id}", response);
    }
    catch (DomainException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("CreateTodo")
.WithSummary("Cria um novo Todo e grava o evento TodoCreated no KurrentDB.");

// 2. Alterar Todo
todosApi.MapPut("/{id:long}", async (long id, UpdateTodoRequest request, ITodoRepository repository, CancellationToken ct) =>
{
    try
    {
        // Consulta a fonte da verdade antes de aplicar a alteração
        var aggregate = await repository.GetByIdAsync(id, ct);
        if (aggregate == null)
        {
            return Results.NotFound(new { error = $"Todo #{id} não foi encontrado na fonte da verdade." });
        }

        aggregate.Update(request.Description, request.Status);
        await repository.SaveAsync(aggregate, ct);

        var response = new TodoResponse(
            aggregate.Id,
            aggregate.ExternalId,
            aggregate.Description,
            aggregate.Status,
            aggregate.IsDone,
            aggregate.IsDeleted,
            aggregate.CreatedAt,
            aggregate.UpdatedAt,
            aggregate.CompletedAt,
            aggregate.DeletedAt
        );

        return Results.Ok(response);
    }
    catch (DomainException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("UpdateTodo")
.WithSummary("Altera descrição e status de um Todo gravando o evento TodoUpdated no KurrentDB.");

// 3. Marcar Todo como Concluído (isDone = true)
todosApi.MapPatch("/{id:long}/complete", async (long id, ITodoRepository repository, CancellationToken ct) =>
{
    try
    {
        // Consulta a fonte da verdade antes de marcar como concluído
        var aggregate = await repository.GetByIdAsync(id, ct);
        if (aggregate == null)
        {
            return Results.NotFound(new { error = $"Todo #{id} não foi encontrado na fonte da verdade." });
        }

        aggregate.MarkAsCompleted();
        await repository.SaveAsync(aggregate, ct);

        var response = new TodoResponse(
            aggregate.Id,
            aggregate.ExternalId,
            aggregate.Description,
            aggregate.Status,
            aggregate.IsDone,
            aggregate.IsDeleted,
            aggregate.CreatedAt,
            aggregate.UpdatedAt,
            aggregate.CompletedAt,
            aggregate.DeletedAt
        );

        return Results.Ok(response);
    }
    catch (DomainException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("CompleteTodo")
.WithSummary("Marca um Todo como concluído gravando o evento TodoCompleted no KurrentDB.");

// Endpoint alternativo PUT /{id}/complete
todosApi.MapPut("/{id:long}/complete", async (long id, ITodoRepository repository, CancellationToken ct) =>
{
    try
    {
        var aggregate = await repository.GetByIdAsync(id, ct);
        if (aggregate == null)
        {
            return Results.NotFound(new { error = $"Todo #{id} não foi encontrado na fonte da verdade." });
        }

        aggregate.MarkAsCompleted();
        await repository.SaveAsync(aggregate, ct);

        var response = new TodoResponse(
            aggregate.Id,
            aggregate.ExternalId,
            aggregate.Description,
            aggregate.Status,
            aggregate.IsDone,
            aggregate.IsDeleted,
            aggregate.CreatedAt,
            aggregate.UpdatedAt,
            aggregate.CompletedAt,
            aggregate.DeletedAt
        );

        return Results.Ok(response);
    }
    catch (DomainException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("CompleteTodoPut")
.WithSummary("Marca um Todo como concluído (PUT) gravando o evento TodoCompleted no KurrentDB.");

// 4. Excluir Todo (isDeleted = true)
todosApi.MapDelete("/{id:long}", async (long id, ITodoRepository repository, CancellationToken ct) =>
{
    try
    {
        // Consulta a fonte da verdade antes de excluir
        var aggregate = await repository.GetByIdAsync(id, ct);
        if (aggregate == null)
        {
            return Results.NotFound(new { error = $"Todo #{id} não foi encontrado na fonte da verdade." });
        }

        aggregate.Delete();
        await repository.SaveAsync(aggregate, ct);

        return Results.NoContent();
    }
    catch (DomainException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("DeleteTodo")
.WithSummary("Exclui logicamente um Todo gravando o evento TodoDeleted no KurrentDB.");

// 5. Consultas (Leitura via MongoDB - CQRS Read Side)
todosApi.MapGet("/", async (bool? includeDeleted, IMongoDatabase mongoDatabase, CancellationToken ct) =>
{
    var collection = mongoDatabase.GetCollection<TodoReadModel>("todos");
    var filter = includeDeleted == true
        ? Builders<TodoReadModel>.Filter.Empty
        : Builders<TodoReadModel>.Filter.Eq(x => x.IsDeleted, false);

    var todos = await collection.Find(filter).ToListAsync(ct);
    return Results.Ok(todos);
})
.WithName("GetTodos")
.WithSummary("Consulta a lista de Todos a partir da base de leitura (MongoDB).");

todosApi.MapGet("/{id:long}", async (long id, IMongoDatabase mongoDatabase, CancellationToken ct) =>
{
    var collection = mongoDatabase.GetCollection<TodoReadModel>("todos");
    var todo = await collection.Find(x => x.Id == id).FirstOrDefaultAsync(ct);

    return todo != null ? Results.Ok(todo) : Results.NotFound(new { error = $"Todo #{id} não foi encontrado no banco de leitura." });
})
.WithName("GetTodoById")
.WithSummary("Consulta um Todo pelo Id a partir da base de leitura (MongoDB).");

// 6. Consultar stream de eventos na íntegra (KurrentDB - Fonte da Verdade)
todosApi.MapGet("/{id:long}/events", async (long id, ITodoRepository repository, CancellationToken ct) =>
{
    var events = await repository.GetEventsAsync(id, ct);
    if (events.Count == 0)
    {
        return Results.NotFound(new { error = $"Nenhum evento encontrado no KurrentDB para o Todo #{id}." });
    }

    return Results.Ok(new
    {
        streamId = $"todo-{id}",
        eventCount = events.Count,
        events
    });
})
.WithName("GetTodoEvents")
.WithSummary("Consulta o histórico completo de eventos armazenados no KurrentDB para o Todo informado.");

app.Run();
