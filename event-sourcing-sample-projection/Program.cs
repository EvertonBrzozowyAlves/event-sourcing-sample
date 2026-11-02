using EventStore.Client;
using event_sourcing_sample_projection;
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

var builder = Host.CreateApplicationBuilder(args);

// Configuração do EventStore / KurrentDB
builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("EventStore")
                           ?? "esdb://localhost:2113?tls=false";
    var settings = EventStoreClientSettings.Create(connectionString);
    return new EventStoreClient(settings);
});

// Configuração do MongoDB
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

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
