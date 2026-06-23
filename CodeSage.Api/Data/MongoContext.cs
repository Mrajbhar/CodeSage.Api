using CodeSage.Api.Models;
using CodeSage.Api.Settings;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CodeSage.Api.Data;

public class MongoContext
{
    public IMongoCollection<User> Users { get; }
    public IMongoCollection<Comment> Comments { get; }
    public IMongoCollection<Organization> Organizations { get; }
    public IMongoCollection<Usage> Usage { get; }
    public IMongoCollection<BillingEvent> BillingEvents { get; }

    public MongoContext(IOptions<MongoDbSettings> options)
    {
        var client = new MongoClient(options.Value.ConnectionString);
        var db = client.GetDatabase(options.Value.DatabaseName);

        Users = db.GetCollection<User>("users");
        Comments = db.GetCollection<Comment>("comments");
        Organizations = db.GetCollection<Organization>("organizations");
        Usage = db.GetCollection<Usage>("usage");
        BillingEvents = db.GetCollection<BillingEvent>("billingEvents");

        // one usage doc per org per month
        Usage.Indexes.CreateOne(new CreateIndexModel<Usage>(
            Builders<Usage>.IndexKeys.Ascending(u => u.OrgId).Ascending(u => u.Period),
            new CreateIndexOptions { Unique = true }));

        // one account per email
        Users.Indexes.CreateOne(new CreateIndexModel<User>(
            Builders<User>.IndexKeys.Ascending(u => u.Email),
            new CreateIndexOptions { Unique = true }));

        // fetch a discussion thread quickly
        Comments.Indexes.CreateOne(new CreateIndexModel<Comment>(
            Builders<Comment>.IndexKeys.Ascending(c => c.Target).Ascending(c => c.CreatedAt)));
    }
}